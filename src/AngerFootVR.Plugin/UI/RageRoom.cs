using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The rage room behind the movie house's padded doors (you kick them open): a concrete back room full of things to
/// smash, the mod's own Blender models, each made to break (wood along its planks and legs, glass, china, the TV and
/// the gnome into chunks). Kick (A/X) and what's in front of your foot breaks, or flies if it's sturdy; your hands
/// knock things over, and a fast swing breaks the fragile ones; anything that hits the floor or a wall hard enough
/// breaks. Broken things come back a few seconds later, once you've stepped away.
/// It has its own little physics world (a scene of its own, stepped here every frame), so it works the same from the
/// title and from the pause menu (where the game's time stands still), and nothing in it touches the game's.
/// </summary>
internal static class RageRoom
{
    private sealed class Spot
    {
        public string Kind;
        public Vector3 Pos;
        public float Yaw;
        public Item Live;
        public float BrokeAt = -1f;
    }

    /// <summary>One thing to break: its whole body, until it breaks.</summary>
    private sealed class Item : MonoBehaviour
    {
        public Spot Spot;
        public Rigidbody Body;
        public bool Broken;
        private float _born;

        private void Awake() => _born = Time.unscaledTime;

        private void OnCollisionEnter(Collision c)
        {
            if (Broken || Time.unscaledTime - _born < 0.5f) return;
            var v = c.relativeVelocity.magnitude;
            var hand = c.collider.gameObject.name.StartsWith("Hand");
            if (v > Fragility(Spot.Kind, hand)) Break(this, -c.relativeVelocity * 0.25f);
            else if (v > 2.2f && Time.unscaledTime - _lastThud > 0.08f)
            {
                _lastThud = Time.unscaledTime;
                Sounds.Play(v > 4f ? "thud" : "step_hard", transform.position, Mathf.Clamp01(v / 8f));
            }
        }
    }

    private static float _lastThud;
    private static Scene _scene;
    private static PhysicsScene _physics;
    private static Transform _root;
    private static readonly List<Spot> Spots = new List<Spot>();
    private static readonly List<(GameObject go, float until)> Debris = new List<(GameObject, float)>();
    private static Rigidbody _handL, _handR, _you;
    private static float _accum, _kickAt = -1f;
    private static Vector3 _kickFrom, _kickFacing;
    private const float Step = 1f / 90f;
    private const int PhysLayer = 9; // the game's "Prop": collides with itself (the visuals are on the movie house's layer)

    /// <summary>How hard a thing has to be hit to break (m/s): glass and china go easily, wood takes a kick.</summary>
    private static float Fragility(string kind, bool byHand) => kind switch
    {
        "bottle_green" or "bottle_brown" => byHand ? 2.5f : 3.0f,
        "vase" => byHand ? 2.8f : 3.4f,
        "gnome" => byHand ? 3.4f : 4.2f,
        "tv" or "jukebox" => byHand ? 4.5f : 5.0f,
        "fishtank" => byHand ? 2.6f : 3.0f,
        "box" => 7f,
        _ => byHand ? 9f : 7.5f, // crates, chairs, tables
    };

    private static string SoundOf(string kind) => kind switch
    {
        "bottle_green" or "bottle_brown" => "glass",
        "vase" => "china",
        "gnome" => "plaster",
        "tv" or "jukebox" => "tv",
        "fishtank" => "glass",
        "box" => "cardboard",
        _ => "wood",
    };

    private static float MassOf(string kind) => kind switch
    {
        "bottle_green" or "bottle_brown" => 0.4f,
        "vase" => 1.2f,
        "gnome" => 2f,
        "tv" => 9f,
        "jukebox" => 22f,
        "fishtank" => 10f,
        "box" => 1.5f,
        "crate" => 6f,
        "table" => 8f,
        "chair" => 4f,
        _ => 2f,
    };

    public static bool Ready => _scene.IsValid() && _root != null;

    private static readonly string[] Kinds = { "bottle_green", "bottle_brown", "vase", "gnome", "tv", "box", "crate", "table", "chair", "jukebox", "fishtank" };
    private static Queue<Mesh> _toBake;

    /// <summary>Every frame in the movie house, until done: the pieces' collision shapes cooked a few at a time, and the
    /// sounds made, ahead of the first smash (made when it broke, a TV and a shelf of bottles at once hitched 150 ms).</summary>
    public static void Prepare()
    {
        if (_toBake == null)
        {
            if (!Props.Load()) return;
            _toBake = new Queue<Mesh>();
            foreach (var k in Kinds)
            foreach (var p in Props.PiecesOf(k))
                if (p.Mesh != null)
                    _toBake.Enqueue(p.Mesh);
        }

        for (var n = 0; n < 3 && _toBake.Count > 0; n++) Physics.BakeMesh(_toBake.Dequeue().GetInstanceID(), true);
        Sounds.Warm();
    }

    /// <summary>Every frame in the movie house: built when the doors open, stepped, the hands and you in it.</summary>
    public static void Update()
    {
        if (!MovieHouse.DoorOpen || MovieHouse.Root == null) return;
        if (!Ready && !Build()) return;

        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        _accum += dt;
        var steps = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_accum >= Step && steps < 4)
        {
            MoveYou(Step);
            _physics.Simulate(Step);
            _accum -= Step;
            steps++;
        }

        if (Plugin.DevRun && sw.Elapsed.TotalMilliseconds > 8) Plugin.L.LogInfo($"Rage room: physics {steps} steps in {sw.Elapsed.TotalMilliseconds:F1} ms");

        if (_kickAt > 0f && Time.unscaledTime >= _kickAt)
        {
            _kickAt = -1f;
            var kw = System.Diagnostics.Stopwatch.StartNew();
            DoKick();
            if (Plugin.DevRun) Plugin.L.LogInfo($"Rage room: kick in {kw.Elapsed.TotalMilliseconds:F2} ms");
        }

        var now = Time.unscaledTime;
        for (var i = Debris.Count - 1; i >= 0; i--)
            if (Debris[i].go == null || now > Debris[i].until)
            {
                if (Debris[i].go != null) Object.Destroy(Debris[i].go);
                Debris.RemoveAt(i);
            }

        // broken things come back once you've stepped away from where they stood
        foreach (var s in Spots)
        {
            if (s.Live != null && !s.Live.Broken) continue;
            if (s.BrokeAt < 0f || now - s.BrokeAt < 7f) continue;
            var at = MovieHouse.Root.TransformPoint(s.Pos);
            if ((new Vector3(CinemaRig.HeadPosition.x, at.y, CinemaRig.HeadPosition.z) - at).magnitude < 1.4f) continue;
            Spawn(s);
            Sounds.Play("step_hard", at, 0.2f, 1.6f);
        }
    }

    /// <summary>A kick (A/X): your leg swings out, and a moment later what's in front of your foot gets it.</summary>
    public static void Kick(Vector3 head, Vector3 facing)
    {
        if (!Ready) return;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) return;
        _kickFrom = new Vector3(head.x, MovieHouse.Root.position.y, head.z);
        _kickFacing = facing.normalized;
        _kickAt = Time.unscaledTime + 0.12f;
        Sounds.Play("kick", _kickFrom + Vector3.up * 0.5f, 0.4f);
    }

    private static readonly Collider[] Hits = new Collider[32];

    private static void DoKick()
    {
        var foot = _kickFrom + _kickFacing * 0.7f + Vector3.up * 0.55f; // (the kick reaches about hip height)
        var n = _physics.OverlapSphere(foot, 0.6f, Hits, 1 << PhysLayer, QueryTriggerInteraction.Ignore);
        var any = false;
        for (var i = 0; i < n; i++)
        {
            var item = Hits[i].GetComponentInParent<Item>();
            var push = _kickFacing * 9f + Vector3.up * 3f;
            if (item != null && !item.Broken)
            {
                any = true;
                var sturdy = item.Spot.Kind is "crate" or "table" or "box";
                if (sturdy && Random.value < 0.35f)
                    item.Body.AddForceAtPosition(push * item.Body.mass * 0.9f, foot, ForceMode.Impulse); // it flies (and may break as it lands)
                else Break(item, push);
            }
            else if (Hits[i].attachedRigidbody != null && !Hits[i].attachedRigidbody.isKinematic)
            {
                Hits[i].attachedRigidbody.AddForce(push * 0.6f, ForceMode.VelocityChange);
                any = true;
            }
        }

        if (any)
        {
            VrControls.Rumble(XRNode.LeftHand, 0.6f, 0.08f);
            VrControls.Rumble(XRNode.RightHand, 0.6f, 0.08f);
        }
    }

    private static void Break(Item item, Vector3 push)
    {
        if (item.Broken) return;
        item.Broken = true;
        Surprises.Smashed();
        var spot = item.Spot;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        spot.BrokeAt = Time.unscaledTime;
        var t = item.transform;
        var vel = item.Body.velocity;
        var ang = item.Body.angularVelocity;
        Sounds.Play(SoundOf(spot.Kind), t.position, 1f);
        var pieces = Props.PiecesOf(spot.Kind);
        var centre = t.TransformPoint(item.Body.centerOfMass);
        foreach (var p in pieces)
        {
            var go = new GameObject("Piece " + p.Name) { layer = PhysLayer };
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(t.TransformPoint(p.Offset), t.rotation);
            var vis = new GameObject("Visual") { layer = UI.VrUi.Layer };
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
            vis.AddComponent<MeshRenderer>().sharedMaterials = Props.MaterialsOf(p);
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true; // (before the mesh: else it's cooked as a triangle mesh first)
            mc.sharedMesh = p.Mesh;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, MassOf(spot.Kind) / Mathf.Max(1, pieces.Count));
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var outward = (go.transform.position - centre).normalized;
            rb.velocity = vel + push * Random.Range(0.6f, 1.1f) + outward * Random.Range(1f, 2.5f);
            rb.angularVelocity = ang + Random.insideUnitSphere * 10f;
            Debris.Add((go, Time.unscaledTime + 12f));
        }

        // (never more than a few hundred pieces about at once)
        while (Debris.Count > 320)
        {
            if (Debris[0].go != null) Object.Destroy(Debris[0].go);
            Debris.RemoveAt(0);
        }

        Object.Destroy(item.gameObject);
        if (Plugin.DevRun) Plugin.L.LogInfo($"Rage room: {spot.Kind} broke ({push.magnitude:F1}, {pieces.Count} pieces in {watch.Elapsed.TotalMilliseconds:F2} ms)");
        VrControls.Rumble(XRNode.LeftHand, 0.4f, 0.05f);
        VrControls.Rumble(XRNode.RightHand, 0.4f, 0.05f);
    }

    // ================================================================================== the room

    private static bool Build()
    {
        if (!Props.Load()) return false;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _scene = SceneManager.CreateScene("AngerFootVR Rage Room " + Time.frameCount, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        _physics = _scene.GetPhysicsScene();
        var root = new GameObject("AngerFootVR Rage Room");
        SceneManager.MoveGameObjectToScene(root, _scene);
        _root = root.transform;
        _root.SetPositionAndRotation(MovieHouse.Root.position, Quaternion.identity);
        Debris.Clear();

        // the room's solid parts (seen by the movie house's own walls: these only stop things)
        var lo = MovieHouse.RageMin;
        var hi = MovieHouse.RageMax;
        var mid = (lo + hi) / 2;
        Solid(new Vector3(mid.x, -0.25f, mid.z), new Vector3(hi.x - lo.x + 2f, 0.5f, hi.z - lo.z + 2f));
        Solid(new Vector3(mid.x, hi.y + 0.25f, mid.z), new Vector3(hi.x - lo.x, 0.5f, hi.z - lo.z));
        Solid(new Vector3(lo.x - 0.25f, hi.y / 2, mid.z), new Vector3(0.5f, hi.y, hi.z - lo.z));
        Solid(new Vector3(hi.x + 0.25f, hi.y / 2, mid.z), new Vector3(0.5f, hi.y, hi.z - lo.z));
        Solid(new Vector3(mid.x, hi.y / 2, lo.z - 0.25f), new Vector3(hi.x - lo.x, hi.y, 0.5f));
        Solid(new Vector3(mid.x, hi.y / 2, hi.z + 0.1f), new Vector3(hi.x - lo.x, hi.y, 0.4f)); // the front wall, its doorway closed to flying things

        // shelves of bottles and vases along the back wall
        Spots.Clear();
        var metal = Props.Mat("metal_dark");
        foreach (var sx in new[] { 1.6f, 6.4f })
        {
            for (var k = 0; k < 3; k++)
            {
                var y = 0.45f + k * 0.55f;
                Shelf(new Vector3(sx, y, lo.z + 0.45f), new Vector3(2.4f, 0.04f, 0.55f), metal);
                for (var b = 0; b < 6; b++)
                {
                    var kind = k == 1 && (b == 1 || b == 4) ? "vase" : (b + k) % 2 == 0 ? "bottle_green" : "bottle_brown";
                    Spots.Add(new Spot { Kind = kind, Pos = new Vector3(sx - 1.0f + b * 0.4f, y + 0.025f, lo.z + 0.45f), Yaw = Random.Range(0f, 360f) });
                }
            }

            foreach (var px in new[] { -1.18f, 1.18f })
                Shelf(new Vector3(sx + px, 0.8f, lo.z + 0.45f), new Vector3(0.05f, 1.6f, 0.55f), metal);
        }

        // tables with TVs and vases
        Spots.Add(new Spot { Kind = "table", Pos = new Vector3(2.4f, 0f, -10.2f), Yaw = 10f });
        Spots.Add(new Spot { Kind = "tv", Pos = new Vector3(2.4f, 0.76f, -10.2f), Yaw = 190f });
        Spots.Add(new Spot { Kind = "table", Pos = new Vector3(6.2f, 0f, -11.6f), Yaw = -15f });
        Spots.Add(new Spot { Kind = "tv", Pos = new Vector3(6.0f, 0.76f, -11.6f), Yaw = 160f });
        Spots.Add(new Spot { Kind = "vase", Pos = new Vector3(6.55f, 0.76f, -11.45f), Yaw = 0f });
        // crates, stacked
        foreach (var (x, z, y) in new[] { (0.0f, -8.0f, 0f), (0.65f, -8.0f, 0f), (0.32f, -8.05f, 0.63f), (8.0f, -9.2f, 0f), (8.0f, -9.85f, 0f), (8.0f, -9.5f, 0.63f), (0.2f, -13.0f, 0f) })
            Spots.Add(new Spot { Kind = "crate", Pos = new Vector3(x, y, z), Yaw = Random.Range(-8f, 8f) });
        // boxes in a pile
        foreach (var (x, z, y) in new[] { (7.4f, -14.4f, 0f), (7.9f, -14.0f, 0f), (7.6f, -14.2f, 0.33f), (6.9f, -14.6f, 0f), (7.3f, -14.3f, 0.66f) })
            Spots.Add(new Spot { Kind = "box", Pos = new Vector3(x, y, z), Yaw = Random.Range(-20f, 20f) });
        // the jukebox against the left wall, the fish tank on its own table by the right wall
        Spots.Add(new Spot { Kind = "jukebox", Pos = new Vector3(-0.68f, 0f, -11.0f), Yaw = 90f });
        Spots.Add(new Spot { Kind = "table", Pos = new Vector3(8.35f, 0f, -12.6f), Yaw = 90f });
        Spots.Add(new Spot { Kind = "fishtank", Pos = new Vector3(8.35f, 0.76f, -12.6f), Yaw = -90f });
        // chairs, gnomes
        Spots.Add(new Spot { Kind = "chair", Pos = new Vector3(3.3f, 0f, -9.4f), Yaw = 200f });
        Spots.Add(new Spot { Kind = "chair", Pos = new Vector3(5.2f, 0f, -12.6f), Yaw = 30f });
        Spots.Add(new Spot { Kind = "chair", Pos = new Vector3(1.4f, 0f, -11.2f), Yaw = 120f });
        foreach (var (x, z) in new[] { (4.0f, -8.4f), (1.0f, -9.6f), (7.2f, -8.0f), (4.6f, -14.6f) })
            Spots.Add(new Spot { Kind = "gnome", Pos = new Vector3(x, 0f, z), Yaw = 180f + Random.Range(-30f, 30f) });

        foreach (var s in Spots) Spawn(s);

        // your hands and your body, pushing things as they move
        _handL = Pusher("Hand L", 0.07f);
        _handR = Pusher("Hand R", 0.07f);
        _you = Pusher("You", 0.25f, true);
        Plugin.L.LogInfo($"Rage room: {Spots.Count} things to break (built in {watch.Elapsed.TotalMilliseconds:F1} ms)");
        Surprises.RageVisit();
        return true;
    }

    private static void Solid(Vector3 localPos, Vector3 size)
    {
        var go = new GameObject("Solid") { layer = PhysLayer };
        go.transform.SetParent(_root, false);
        go.transform.localPosition = localPos;
        go.AddComponent<BoxCollider>().size = size;
    }

    private static void Shelf(Vector3 localPos, Vector3 size, Material m)
    {
        var go = new GameObject("Shelf") { layer = PhysLayer };
        go.transform.SetParent(_root, false);
        go.transform.localPosition = localPos;
        go.AddComponent<BoxCollider>().size = size;
        var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(vis.GetComponent<Collider>());
        vis.layer = UI.VrUi.Layer;
        vis.transform.SetParent(go.transform, false);
        vis.transform.localScale = size;
        vis.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    private static void Spawn(Spot s)
    {
        var m = Props.Get(s.Kind);
        if (m == null) return;
        var go = new GameObject("Item " + s.Kind) { layer = PhysLayer };
        go.transform.SetParent(_root, false);
        go.transform.localPosition = s.Pos;
        go.transform.localRotation = Quaternion.Euler(0f, s.Yaw, 0f);
        var vis = new GameObject("Visual") { layer = UI.VrUi.Layer };
        vis.transform.SetParent(go.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = m.Mesh;
        vis.AddComponent<MeshRenderer>().sharedMaterials = Props.MaterialsOf(m);
        // the whole body: its bounds as a box (stable to stack); round things a capsule
        var b = m.Mesh.bounds;
        if (s.Kind is "bottle_green" or "bottle_brown" or "vase" or "gnome")
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
        rb.mass = MassOf(s.Kind);
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.Sleep();
        var item = go.AddComponent<Item>();
        item.Spot = s;
        item.Body = rb;
        s.Live = item;
        s.BrokeAt = -1f;
    }

    private static Rigidbody Pusher(string name, float radius, bool body = false)
    {
        var go = new GameObject(name) { layer = PhysLayer };
        go.transform.SetParent(_root, false);
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

    private static void MoveYou(float step)
    {
        var l = VrHands.Get(XRNode.LeftHand);
        var r = VrHands.Get(XRNode.RightHand);
        Follow(_handL, l.Tracked ? l.Position : _root.position + Vector3.down * 50f);
        Follow(_handR, r.Tracked ? r.Position : _root.position + Vector3.down * 50f + Vector3.right);
        Follow(_you, new Vector3(CinemaRig.HeadPosition.x, _root.position.y, CinemaRig.HeadPosition.z));
    }

    /// <summary>A pusher moves (sweeping things along); a jump (a recentre, tracking coming back) puts it there instead,
    /// so it never sweeps through the room smashing everything on the way.</summary>
    private static void Follow(Rigidbody body, Vector3 to)
    {
        if (body == null) return;
        if ((body.position - to).sqrMagnitude > 0.35f * 0.35f)
        {
            body.position = to;
            body.transform.position = to;
        }
        else body.MovePosition(to);
    }

    /// <summary>Gone (the movie house left, the level loaded): the next visit builds it fresh.</summary>
    public static void Clear()
    {
        if (_scene.IsValid()) SceneManager.UnloadSceneAsync(_scene);
        _root = null;
        Debris.Clear();
    }

    public static string Describe()
    {
        var live = 0;
        foreach (var s in Spots)
            if (s.Live != null && !s.Live.Broken)
                live++;
        var hands = _handR == null || MovieHouse.Root == null ? "" : $" hands L{MovieHouse.Root.InverseTransformPoint(_handL.position)} R{MovieHouse.Root.InverseTransformPoint(_handR.position)}";
        return $"RAGE ready={Ready} doors={(MovieHouse.DoorOpen ? "open" : "shut")} things {live}/{Spots.Count} pieces {Debris.Count}{hands}";
    }
}
