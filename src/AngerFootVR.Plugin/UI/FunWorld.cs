using System;
using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The movie house's world of things you can touch: the rage room, the arcade, the gun range and the lobby's props,
/// in one physics scene of its own (stepped here every frame, so it works the same from the title and from the pause
/// menu, where the game's time stands still; nothing in it touches the game's). Your hands and body push things as
/// they move. The grip picks a thing up (or, pointing at one up to 4 m off, pulls it to your hand); let go and it
/// flies the way your hand was going. A kick (B/X) reaches whatever's in front of your foot.
/// </summary>
internal static class FunWorld
{
    public const int PhysLayer = 9; // the game's "Prop": collides with itself (the pictures are on the movie house's layer)
    public const float Step = 1f / 90f;

    public static Scene Scene { get; private set; }
    public static PhysicsScene Physics { get; private set; }
    public static Transform Root { get; private set; }
    public static bool Ready => Scene.IsValid() && Root != null;

    /// <summary>Something a kick reaches (B/X, what's in front of your foot).</summary>
    public interface IKickable
    {
        void OnKicked(Vector3 foot, Vector3 facing);
    }

    /// <summary>Every physics step, before it runs (things that move themselves: held things, targets on rails).</summary>
    public static event Action<float> BeforeStep;

    /// <summary>Every frame in the movie house, after the physics (the rooms' own goings-on).</summary>
    public static event Action Tick;

    /// <summary>A kick that landed: where the foot was, the way it went.</summary>
    public static event Action<Vector3, Vector3> Kicked;

    /// <summary>Built (the first time the world is wanted: the rooms add their things then).</summary>
    public static event Action Built;

    /// <summary>Gone (left, or a scene load took the world's scene with it): the rooms forget what they had.</summary>
    public static event Action Clearing;

    private static void ClearRooms()
    {
        try
        {
            Clearing?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Fun world clearing: " + e.Message);
        }
    }

    public static Rigidbody HandL, HandR, You;
    private static readonly Queue<(string what, Action act)> Later = new Queue<(string, Action)>();

    /// <summary>A part of the rooms built a little later, a few milliseconds a frame (all at once, coming into the movie
    /// house stalled a tenth of a second).</summary>
    public static void Soon(string what, Action act) => Later.Enqueue((what, act));
    private static float _accum, _kickAt = -1f;
    private static Vector3 _kickFrom, _kickFacing;
    private static readonly List<(GameObject go, float until)> Debris = new List<(GameObject, float)>();

    // ================================================================================== the world

    private static bool Build()
    {
        if (MovieHouse.Root == null || !Props.Load()) return false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ClearRooms(); // (a scene load unloads the world's own scene too: what the rooms had went with it)
        Later.Clear();
        Handles.Clear();
        Presses.Clear();
        Dragging.Clear();
        Scene = SceneManager.CreateScene("AngerFootVR Fun World " + Time.frameCount, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        Physics = Scene.GetPhysicsScene();
        var root = new GameObject("AngerFootVR Fun World");
        SceneManager.MoveGameObjectToScene(root, Scene);
        Root = root.transform;
        Root.SetPositionAndRotation(MovieHouse.Root.position, Quaternion.identity);
        Debris.Clear();
        Grabbing.Clear();

        // the movie house's floor and walls (the rooms add their own)
        Solid(new Vector3(0f, -0.25f, 5f), new Vector3(14f, 0.5f, 22f));
        Solid(new Vector3(-7.25f, 5f, 5f), new Vector3(0.5f, 10f, 22f));
        Solid(new Vector3(7.25f, 5f, 5f), new Vector3(0.5f, 10f, 22f));
        Solid(new Vector3(0f, 0.35f, 14.6f), new Vector3(14f, 0.7f, 3.2f)); // the stage
        HandL = Pusher("Hand L", 0.07f);
        HandR = Pusher("Hand R", 0.07f);
        You = Pusher("You", 0.25f, true);
        try
        {
            Built?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Fun world: " + e);
        }

        Plugin.L.LogInfo($"Fun world built in {watch.Elapsed.TotalMilliseconds:F1} ms");
        return true;
    }

    /// <summary>Every frame in the movie house: built when first wanted, stepped, your hands and body in it.</summary>
    public static void Update()
    {
        if (MovieHouse.Root == null) return;
        if (!Ready && !Build()) return;
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        _accum += dt;
        var steps = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        HandInput();
        while (_accum >= Step && steps < 4)
        {
            MoveYou();
            try
            {
                BeforeStep?.Invoke(Step);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Fun world step: " + e.Message);
            }

            Physics.Simulate(Step);
            _accum -= Step;
            steps++;
        }

        if (steps == 4) _accum = 0f; // (a long frame: the world doesn't try to catch up)
        if (Plugin.DevRun && sw.Elapsed.TotalMilliseconds > 8) Plugin.L.LogInfo($"Fun world: physics {steps} steps in {sw.Elapsed.TotalMilliseconds:F1} ms");

        if (Later.Count > 0)
        {
            var bw = System.Diagnostics.Stopwatch.StartNew();
            do
            {
                var (what, act) = Later.Dequeue();
                var one = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    act();
                }
                catch (Exception e)
                {
                    Plugin.L.LogWarning($"Fun world ({what}): {e}");
                }

                if (Plugin.DevRun && one.Elapsed.TotalMilliseconds > 4) Plugin.L.LogInfo($"Fun world: {what} in {one.Elapsed.TotalMilliseconds:F1} ms");
            } while (Later.Count > 0 && bw.Elapsed.TotalMilliseconds < 3);
        }

        if (_kickAt > 0f && Time.unscaledTime >= _kickAt)
        {
            _kickAt = -1f;
            DoKick();
        }

        var now = Time.unscaledTime;
        for (var i = Debris.Count - 1; i >= 0; i--)
            if (Debris[i].go == null || now > Debris[i].until)
            {
                if (Debris[i].go != null) UnityEngine.Object.Destroy(Debris[i].go);
                Debris.RemoveAt(i);
            }

        try
        {
            Tick?.Invoke();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Fun world: " + e);
        }
    }

    /// <summary>Gone (the movie house left): the next visit builds it fresh.</summary>
    public static void Clear()
    {
        foreach (var h in Grabbing.Values)
            if (h.Thing != null)
                h.Thing.Holder = null;
        Grabbing.Clear();
        Dragging.Clear();
        Handles.Clear();
        Later.Clear();
        Presses.Clear();
        ClearRooms();
        if (Scene.IsValid()) SceneManager.UnloadSceneAsync(Scene);
        Root = null;
        Debris.Clear();
        _kickAt = -1f;
    }

    /// <summary>A pile of debris that goes after a while (never more than a few hundred pieces about at once).</summary>
    public static void AddDebris(GameObject go, float life)
    {
        Debris.Add((go, Time.unscaledTime + life));
        while (Debris.Count > 360)
        {
            if (Debris[0].go != null) UnityEngine.Object.Destroy(Debris[0].go);
            Debris.RemoveAt(0);
        }
    }

    public static int DebrisCount => Debris.Count;

    /// <summary>Something solid that only stops things (the rooms' pictures are the movie house's own).</summary>
    public static GameObject Solid(Vector3 localPos, Vector3 size, float yaw = 0f, float pitch = 0f)
    {
        var go = new GameObject("Solid") { layer = PhysLayer };
        go.transform.SetParent(Root, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    /// <summary>A thing of the mod's own models with a body: its picture on the movie house's layer, its collider here.</summary>
    public static GameObject Thing(string model, Vector3 localPos, float yaw, float mass, bool capsule = false, bool kinematic = false)
    {
        var m = Props.Get(model);
        if (m == null) return null;
        var go = new GameObject("Thing " + model) { layer = PhysLayer };
        go.transform.SetParent(Root, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var vis = new GameObject("Visual") { layer = VrUi.Layer };
        vis.transform.SetParent(go.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = m.Mesh;
        vis.AddComponent<MeshRenderer>().sharedMaterials = Props.MaterialsOf(m);
        var b = m.Mesh.bounds;
        if (capsule)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.center = b.center;
            c.height = b.size.y;
            c.radius = Mathf.Max(b.size.x, b.size.z) * 0.45f;
        }
        else
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = b.center;
            c.size = b.size;
        }

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.isKinematic = kinematic;
        rb.collisionDetectionMode = kinematic ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Continuous;
        if (!kinematic) rb.Sleep();
        return go;
    }

    /// <summary>A picture only (no body), of the mod's own models.</summary>
    public static GameObject Picture(string model, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var m = Props.Get(model);
        if (m == null) return null;
        var vis = new GameObject("Picture " + model) { layer = VrUi.Layer };
        vis.transform.SetParent(parent != null ? parent : Root, false);
        vis.transform.localPosition = localPos;
        vis.transform.localRotation = localRot;
        vis.AddComponent<MeshFilter>().sharedMesh = m.Mesh;
        vis.AddComponent<MeshRenderer>().sharedMaterials = Props.MaterialsOf(m);
        return vis;
    }

    public static Vector3 World(Vector3 local) => Root != null ? Root.TransformPoint(local) : local;
    public static Vector3 Local(Vector3 world) => Root != null ? Root.InverseTransformPoint(world) : world;

    private static readonly RaycastHit[] RayHits = new RaycastHit[16];

    /// <summary>The nearest thing along a ray (in this world).</summary>
    public static bool Raycast(Vector3 from, Vector3 dir, float dist, out RaycastHit hit, int mask = 1 << PhysLayer)
    {
        hit = default;
        return Ready && Physics.Raycast(from, dir, out hit, dist, mask, QueryTriggerInteraction.Collide);
    }

    public static int Overlap(Vector3 at, float radius, Collider[] into) =>
        Ready ? Physics.OverlapSphere(at, radius, into, 1 << PhysLayer, QueryTriggerInteraction.Ignore) : 0;

    // ================================================================================== you in it

    private static Rigidbody Pusher(string name, float radius, bool body = false)
    {
        var go = new GameObject(name) { layer = PhysLayer };
        go.transform.SetParent(Root, false);
        if (body)
        {
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = radius;
            c.height = 1.5f;
            c.center = new Vector3(0f, 0.8f, 0f);
        }
        else go.AddComponent<SphereCollider>().radius = radius;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        return rb;
    }

    private static void MoveYou()
    {
        var l = VrHands.Get(XRNode.LeftHand);
        var r = VrHands.Get(XRNode.RightHand);
        // (a hand holding something pushes with what it holds instead)
        Follow(HandL, l.Tracked && !Holding(XRNode.LeftHand) ? l.Position : Root.position + Vector3.down * 50f);
        Follow(HandR, r.Tracked && !Holding(XRNode.RightHand) ? r.Position : Root.position + Vector3.down * 50f + Vector3.right);
        Follow(You, new Vector3(CinemaRig.HeadPosition.x, Root.position.y, CinemaRig.HeadPosition.z));
        foreach (var h in Grabbing.Values) h.Step();
    }

    /// <summary>A pusher moves (sweeping things along); a jump (a recentre, tracking coming back) puts it there instead,
    /// so it never sweeps through the room smashing everything on the way.</summary>
    public static void Follow(Rigidbody body, Vector3 to)
    {
        if (body == null) return;
        if ((body.position - to).sqrMagnitude > 0.35f * 0.35f)
        {
            body.position = to;
            body.transform.position = to;
        }
        else body.MovePosition(to);
    }

    // ================================================================================== kicks

    /// <summary>A kick (B/X): your leg swings out, and a moment later what's in front of your foot gets it.</summary>
    public static void Kick(Vector3 head, Vector3 facing)
    {
        if (!Ready) return;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) return;
        _kickFrom = new Vector3(head.x, Root.position.y, head.z);
        _kickFacing = facing.normalized;
        _kickAt = Time.unscaledTime + 0.12f;
        if (!GameSounds.Play("Kick Whoosh", _kickFrom + Vector3.up * 0.5f, 0.5f)) Sounds.Play("kick", _kickFrom + Vector3.up * 0.5f, 0.4f);
    }

    private static readonly Collider[] Hits = new Collider[48];

    /// <summary>Where the last kick's foot was (the arcade's games ask).</summary>
    public static Vector3 LastFoot { get; private set; }

    private static void DoKick()
    {
        var foot = _kickFrom + _kickFacing * 0.7f + Vector3.up * 0.55f; // (the kick reaches about hip height)
        LastFoot = foot;
        var n = Overlap(foot, 0.6f, Hits);
        var any = false;
        var done = new HashSet<Component>();
        for (var i = 0; i < n; i++)
        {
            var k = Hits[i].GetComponentInParent<IKickable>();
            if (k is Component c && done.Add(c))
            {
                any = true;
                k.OnKicked(foot, _kickFacing);
            }
            else if (k == null && Hits[i].attachedRigidbody != null && !Hits[i].attachedRigidbody.isKinematic && done.Add(Hits[i].attachedRigidbody))
            {
                Hits[i].attachedRigidbody.AddForce((_kickFacing * 9f + Vector3.up * 3f) * 0.6f, ForceMode.VelocityChange);
                any = true;
            }
        }

        foreach (var p in Presses)
            if (p != null && p.Kickable && (p.At - foot).magnitude < 0.75f)
                any |= p.Fire(null);

        try
        {
            Kicked?.Invoke(foot, _kickFacing);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Fun world kick: " + e.Message);
        }

        if (any)
        {
            VrControls.Rumble(XRNode.LeftHand, 0.6f, 0.08f);
            VrControls.Rumble(XRNode.RightHand, 0.6f, 0.08f);
        }
    }

    // ================================================================================== holding things

    /// <summary>A thing you can pick up. Its grip: where the hand holds it (local) and how it's turned in the hand
    /// (the hand's frame: z where you point, y up the back of the hand).</summary>
    public sealed class Grabbable : MonoBehaviour
    {
        public Rigidbody Body;
        public Vector3 GripLocal;
        public Quaternion GripRot = Quaternion.identity;
        public Vector3? ForeLocal; // a second hand's place on it (two-handed things)
        public bool Pullable = true; // (pointed at, the grip pulls it to your hand)
        public float Reach = 0.16f;
        public XRNode? Holder;
        public XRNode? Fore;
        public float LastHeld = -100f;
        public float Kick; // (a gun's recoil: it jumps up and back in the hand, settling)
        public Action<Grabbable, XRNode> OnGrabbed, OnReleased, OnTrigger, OnTriggerHeld;
        public Func<bool> CanGrab;
        public bool Busy => Holder.HasValue;

        public Vector3 GripWorld => transform.TransformPoint(GripLocal);
    }

    private sealed class Hold
    {
        public Grabbable Thing;
        public XRNode Hand;
        public bool WasKinematic;
        public float PullUntil = -1f, PullFrom;
        public Vector3 PullStart;
        public Quaternion PullStartRot;
        public readonly Vector3[] Past = new Vector3[6];
        public readonly float[] PastT = new float[6];
        public int N;
        public Quaternion LastRot;
        public Vector3 AngVel;

        public void Note(Vector3 p, float t)
        {
            for (var i = Past.Length - 1; i > 0; i--)
            {
                Past[i] = Past[i - 1];
                PastT[i] = PastT[i - 1];
            }

            Past[0] = p;
            PastT[0] = t;
            N = Mathf.Min(N + 1, Past.Length);
        }

        public Vector3 Velocity()
        {
            if (N < 2) return Vector3.zero;
            var k = N - 1;
            var dt = PastT[0] - PastT[k];
            return dt > 1e-4f ? (Past[0] - Past[k]) / dt : Vector3.zero;
        }

        public void Target(out Vector3 pos, out Quaternion rot)
        {
            var pose = VrHands.Get(Hand);
            var handRot = pose.Aim;
            if (Thing.Kick > 0f) handRot *= Quaternion.Euler(-Thing.Kick * 9f, 0f, 0f);
            rot = handRot * Thing.GripRot;
            if (Thing.Fore.HasValue && Thing.ForeLocal.HasValue)
            {
                // both hands on it: it points from the back hand to the front one, rolled with the back hand
                var fore = VrHands.Get(Thing.Fore.Value);
                var gripW = pose.Position;
                var along = fore.Position - gripW;
                var localAlong = Thing.ForeLocal.Value - Thing.GripLocal;
                if (along.sqrMagnitude > 0.01f && localAlong.sqrMagnitude > 1e-4f)
                {
                    var want = Quaternion.FromToRotation(rot * localAlong.normalized, along.normalized) * rot;
                    rot = want;
                }
            }

            pos = pose.Position - rot * Thing.GripLocal - handRot * Vector3.forward * (Thing.Kick * 0.035f);
        }

        public void Step()
        {
            if (Thing == null || Thing.Body == null) return;
            Thing.Kick = Mathf.MoveTowards(Thing.Kick, 0f, FunWorld.Step * 7f);
            Target(out var pos, out var rot);
            if (PullUntil > 0f)
            {
                // (flying to your hand)
                var t = Mathf.Clamp01((Time.unscaledTime - PullFrom) / (PullUntil - PullFrom));
                var e = 1f - (1f - t) * (1f - t);
                pos = Vector3.Lerp(PullStart, pos, e);
                rot = Quaternion.Slerp(PullStartRot, rot, e);
                if (t >= 1f) PullUntil = -1f;
            }

            Thing.Body.MovePosition(pos);
            Thing.Body.MoveRotation(rot);
        }
    }

    private static readonly Dictionary<XRNode, Hold> Grabbing = new Dictionary<XRNode, Hold>();

    /// <summary>A part you work with your hand where it is (a lever, a switch, a pull cord): grip it and move.</summary>
    public sealed class Handle
    {
        public Func<Vector3> Where;
        public float Reach = 0.13f;
        public Action<XRNode, Vector3> Begin, Drag;
        public Action<XRNode> End;
        public Func<bool> Enabled;
    }

    /// <summary>A button: touch it with your hand, point at it and pull the trigger, or (a floor one) kick it.</summary>
    public sealed class Press : MonoBehaviour
    {
        public Action<XRNode?> Pressed;
        public float Radius = 0.1f;
        public Func<Vector3> Where;
        public float Cooldown = 0.6f;
        public Func<bool> Enabled;
        public bool Kickable;
        internal float Last = -10f;
        internal readonly bool[] Inside = new bool[2];
        public Vector3 At => Where != null ? Where() : transform.position;

        public bool Fire(XRNode? hand)
        {
            if ((Enabled != null && !Enabled()) || Time.unscaledTime - Last < Cooldown) return false;
            Last = Time.unscaledTime;
            if (hand.HasValue) VrControls.Rumble(hand.Value, 0.35f, 0.05f);
            try
            {
                Pressed?.Invoke(hand);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Press: " + e.Message);
            }

            return true;
        }
    }

    public static readonly List<Handle> Handles = new List<Handle>();
    private static readonly List<Press> Presses = new List<Press>();
    private static readonly Dictionary<XRNode, Handle> Dragging = new Dictionary<XRNode, Handle>();

    /// <summary>A button on a thing (its collider is where you point at it).</summary>
    public static Press MakePress(GameObject go, Action<XRNode?> pressed, float radius = 0.1f, bool kickable = false)
    {
        var p = go.GetComponent<Press>() ?? go.AddComponent<Press>();
        p.Pressed = pressed;
        p.Radius = radius;
        p.Kickable = kickable;
        if (!Presses.Contains(p)) Presses.Add(p);
        return p;
    }

    private static void Presses_()
    {
        var now = Time.unscaledTime;
        for (var i = Presses.Count - 1; i >= 0; i--)
        {
            var p = Presses[i];
            if (p == null)
            {
                Presses.RemoveAt(i);
                continue;
            }

            for (var k = 0; k < 2; k++)
            {
                var hand = k == 0 ? XRNode.LeftHand : XRNode.RightHand;
                var pose = VrHands.Get(hand);
                var inside = pose.Tracked && !Holding(hand) && (pose.Position - p.At).magnitude < p.Radius;
                if (inside && !p.Inside[k]) p.Fire(hand);
                p.Inside[k] = inside;
            }
        }

        _ = now;
    }
    private static readonly Collider[] Near = new Collider[24];

    /// <summary>Dev: press the first button whose object's name has this in it, or begin and end the first handle
    /// near the named object ("handle &lt;name&gt;").</summary>
    public static string DevPoke(string name)
    {
        foreach (var p in Presses)
            if (p != null && p.gameObject.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                p.Fire(XRNode.RightHand);
                return "pressed " + p.gameObject.name;
            }

        return "no button like '" + name + "' (" + Presses.Count + " buttons)";
    }

    public static bool Holding(XRNode hand) => Grabbing.ContainsKey(hand);

    public static Grabbable Held(XRNode hand) => Grabbing.TryGetValue(hand, out var h) ? h.Thing : null;

    /// <summary>Whether a hand is busy with the world (holding a thing): the laser leaves it be.</summary>
    public static bool Busy(XRNode hand) => Ready && (Holding(hand) || IsFore(hand) || Dragging.ContainsKey(hand)); // (a hand on the cord under the screen had clicked the menu with the same squeeze)

    private static bool IsFore(XRNode hand)
    {
        foreach (var h in Grabbing.Values)
            if (h.Thing != null && h.Thing.Fore == hand)
                return true;
        return false;
    }

    private static void HandInput()
    {
        Presses_();
        foreach (var hand in new[] { XRNode.LeftHand, XRNode.RightHand })
        {
            var pose = VrHands.Get(hand);
            if (Dragging.TryGetValue(hand, out var drag))
            {
                if (!pose.Tracked || !VrControls.Held(hand, VrButton.Grip) || (drag.Enabled != null && !drag.Enabled()))
                {
                    Dragging.Remove(hand);
                    drag.End?.Invoke(hand);
                }
                else drag.Drag?.Invoke(hand, pose.Position);

                continue;
            }
            if (Grabbing.TryGetValue(hand, out var held))
            {
                held.Note(pose.Position, Time.unscaledTime);
                if (held.Thing == null || !pose.Tracked)
                {
                    Release(hand, false);
                    continue;
                }

                var q = pose.Aim;
                var dq = q * Quaternion.Inverse(held.LastRot);
                dq.ToAngleAxis(out var ang, out var axis);
                if (ang > 180f) ang -= 360f;
                if (Time.unscaledDeltaTime > 1e-4f && !float.IsNaN(axis.x)) held.AngVel = Vector3.Lerp(held.AngVel, axis * (ang * Mathf.Deg2Rad / Time.unscaledDeltaTime), 0.5f);
                held.LastRot = q;
                if (VrControls.Down(hand, VrButton.Trigger)) held.Thing.OnTrigger?.Invoke(held.Thing, hand);
                if (VrControls.Held(hand, VrButton.Trigger)) held.Thing.OnTriggerHeld?.Invoke(held.Thing, hand);
                if (VrControls.Up(hand, VrButton.Grip)) Release(hand, true);
                continue;
            }

            // a second hand on a held thing's front (two-handed guns, the sledgehammer)
            foreach (var h in Grabbing.Values)
                if (h.Thing != null && h.Thing.Fore == hand && VrControls.Up(hand, VrButton.Grip))
                    h.Thing.Fore = null;

            // pointing at a button and pulling the trigger presses it (the laser has the big screen)
            if (pose.Tracked && VrControls.Down(hand, VrButton.Trigger) && !Laser.OnScreen &&
                Physics.Raycast(pose.Position, pose.Aim * Vector3.forward, out var aimHit, 4f, 1 << PhysLayer, QueryTriggerInteraction.Collide) &&
                aimHit.collider.GetComponentInParent<Press>() is { } pointed)
                pointed.Fire(hand);

            if (!pose.Tracked || !VrControls.Down(hand, VrButton.Grip)) continue;
            if (TryFore(hand, pose.Position)) continue;
            var thing = Nearest(pose.Position);
            if (thing != null)
            {
                Grab(hand, thing, false);
                continue;
            }

            // a lever, a switch, a cord in your hand: worked where it is (before anything pointed at further off, which
            // had come flying to the hand instead when the hand was on the cord)
            Handle best = null;
            var bestD = float.MaxValue;
            foreach (var h in Handles)
            {
                if (h.Where == null || (h.Enabled != null && !h.Enabled())) continue;
                var d = (h.Where() - pose.Position).magnitude;
                if (d < h.Reach && d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }

            if (best != null)
            {
                Dragging[hand] = best;
                VrControls.Rumble(hand, 0.3f, 0.04f);
                best.Begin?.Invoke(hand, pose.Position);
                continue;
            }

            thing = Pointed(pose.Position, pose.Aim * Vector3.forward);
            if (thing != null) Grab(hand, thing, true);
        }
    }

    private static bool TryFore(XRNode hand, Vector3 at)
    {
        foreach (var h in Grabbing.Values)
        {
            var t = h.Thing;
            if (t == null || !t.ForeLocal.HasValue || t.Fore.HasValue || h.Hand == hand) continue;
            if ((t.transform.TransformPoint(t.ForeLocal.Value) - at).magnitude > 0.18f) continue;
            t.Fore = hand;
            VrControls.Rumble(hand, 0.25f, 0.04f);
            return true;
        }

        return false;
    }

    private static Grabbable Nearest(Vector3 at)
    {
        var n = Ready ? Physics.OverlapSphere(at, 0.3f, Near, 1 << PhysLayer, QueryTriggerInteraction.Collide) : 0;
        Grabbable best = null;
        var bestD = float.MaxValue;
        for (var i = 0; i < n; i++)
        {
            var g = Near[i].GetComponentInParent<Grabbable>();
            if (g == null || g.Busy || (g.CanGrab != null && !g.CanGrab())) continue;
            var d = (g.GripWorld - at).magnitude;
            var touch = Near[i].ClosestPoint(at);
            var dTouch = (touch - at).magnitude;
            if (Mathf.Min(d, dTouch + 0.05f) > g.Reach) continue;
            if (d < bestD)
            {
                bestD = d;
                best = g;
            }
        }

        return best;
    }

    private static Grabbable Pointed(Vector3 from, Vector3 dir)
    {
        if (!Ready) return null;
        if (!Physics.SphereCast(from, 0.06f, dir, out var hit, 4f, 1 << PhysLayer, QueryTriggerInteraction.Collide)) return null;
        var g = hit.collider.GetComponentInParent<Grabbable>();
        return g != null && g.Pullable && !g.Busy && (g.CanGrab == null || g.CanGrab()) ? g : null;
    }

    public static void Grab(XRNode hand, Grabbable thing, bool pulled)
    {
        if (thing == null || thing.Body == null) return;
        if (Grabbing.ContainsKey(hand)) Release(hand, false);
        var h = new Hold { Thing = thing, Hand = hand, WasKinematic = thing.Body.isKinematic };
        thing.Holder = hand;
        thing.Fore = null;
        thing.LastHeld = Time.unscaledTime;
        thing.Body.isKinematic = true;
        thing.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        h.LastRot = VrHands.Get(hand).Aim;
        if (pulled)
        {
            h.PullFrom = Time.unscaledTime;
            h.PullUntil = Time.unscaledTime + 0.22f;
            h.PullStart = thing.Body.position;
            h.PullStartRot = thing.Body.rotation;
            if (!GameSounds.Play("Throw Gun", thing.transform.position, 0.35f, 1.3f)) Sounds.Play("kick", thing.transform.position, 0.2f, 1.6f);
        }

        Grabbing[hand] = h;
        // (the hand's own pusher keeps off it)
        var pusher = hand == XRNode.LeftHand ? HandL : HandR;
        foreach (var c in thing.GetComponentsInChildren<Collider>())
        foreach (var p in pusher.GetComponents<Collider>())
            UnityEngine.Physics.IgnoreCollision(c, p, true);
        VrControls.Rumble(hand, 0.3f, 0.05f);
        try
        {
            thing.OnGrabbed?.Invoke(thing, hand);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Grab: " + e.Message);
        }
    }

    /// <summary>Let go: thrown the way your hand was going (or put down, if it wasn't going anywhere).</summary>
    public static void Release(XRNode hand, bool thrown)
    {
        if (!Grabbing.TryGetValue(hand, out var h)) return;
        Grabbing.Remove(hand);
        var t = h.Thing;
        if (t == null) return;
        t.Holder = null;
        t.Fore = null;
        t.LastHeld = Time.unscaledTime;
        if (t.Body != null)
        {
            t.Body.isKinematic = h.WasKinematic && !thrown ? true : false;
            t.Body.collisionDetectionMode = t.Body.isKinematic ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Continuous;
            if (!t.Body.isKinematic)
            {
                var v = thrown ? h.Velocity() : Vector3.zero;
                if (v.magnitude > 1.2f) v *= 1.25f; // (a throw carries a little more than the hand did)
                t.Body.velocity = Vector3.ClampMagnitude(v, 22f);
                t.Body.angularVelocity = Vector3.ClampMagnitude(h.AngVel, 25f);
                t.Body.WakeUp();
            }
        }

        var pusher = hand == XRNode.LeftHand ? HandL : HandR;
        if (pusher != null)
            foreach (var c in t.GetComponentsInChildren<Collider>())
            foreach (var p in pusher.GetComponents<Collider>())
                UnityEngine.Physics.IgnoreCollision(c, p, false);
        try
        {
            t.OnReleased?.Invoke(t, hand);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning("Release: " + e.Message);
        }
    }

    /// <summary>A thing given a grip: picked up where the hand holds it, turned as it's held.</summary>
    public static Grabbable MakeGrabbable(GameObject go, Vector3 gripLocal, Quaternion gripRot, Vector3? foreLocal = null)
    {
        var g = go.GetComponent<Grabbable>() ?? go.AddComponent<Grabbable>();
        g.Body = go.GetComponent<Rigidbody>();
        g.GripLocal = gripLocal;
        g.GripRot = gripRot;
        g.ForeLocal = foreLocal;
        return g;
    }

    /// <summary>Your mouth (drinks, popcorn): just under your eyes, a little in front.</summary>
    public static Vector3 Mouth => CinemaRig.HeadPosition + CinemaRig.HeadRotation * new Vector3(0f, -0.08f, 0.07f);

    public static string Describe()
    {
        var held = "";
        foreach (var kv in Grabbing) held += $" {(kv.Key == XRNode.LeftHand ? "L" : "R")}:{(kv.Value.Thing != null ? kv.Value.Thing.name : "-")}";
        return $"FUN ready={Ready} debris {Debris.Count} holding{(held.Length == 0 ? " nothing" : held)}";
    }
}
