using System;
using System.Collections.Generic;
using AngerFootVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.UI;

/// <summary>
/// The rage room behind the movie house's padded doors (you kick them open): a concrete back room full of things to
/// smash, the mod's own Blender models, each made to break (wood along its planks and legs, glass, china, the TV and
/// the gnome into chunks), with Anger Foot's own smashing sounds. Kick (B/X) and what's in front of your foot breaks,
/// or flies if it's sturdy; your hands knock things over, and a fast swing breaks the fragile ones; anything that
/// hits the floor or a wall hard enough breaks. Pick things up (grip) and throw them; take a bat, a machete, a
/// sledgehammer off the rack and swing. The fish tank floods the floor (its fish flop about), TVs crack then blow,
/// the jukebox plays the game's music until you hit it, the vending machine drops cans when kicked (drink them for
/// real), the water cooler's jug glugs out over the floor, the piñata bursts into candy, the red barrels explode.
/// Broken things come back a few seconds later, once you've stepped away. It lives in the movie house's own physics
/// world (FunWorld).
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
        public float Respawn = 7f;
    }

    /// <summary>One thing to break: its whole body, until it breaks.</summary>
    private sealed class Item : MonoBehaviour, FunWorld.IKickable
    {
        public Spot Spot;
        public Rigidbody Body;
        public bool Broken;
        public int Hits;
        public float Born;
        public float Armed = -1f; // (a kicked barrel: it goes off at the next hard knock, or soon anyway)
        public GameObject Cracks;
        public AudioSource Loop;
        public string Kind => Spot != null ? Spot.Kind : _kind;
        public string _kind;

        private void Awake() => Born = Time.unscaledTime;

        private void OnCollisionEnter(Collision c)
        {
            if (Broken || Time.unscaledTime - Born < 0.5f) return;
            var v = c.relativeVelocity.magnitude;
            var other = c.collider.gameObject.name;
            var hand = other.StartsWith("Hand");
            var held = c.collider.GetComponentInParent<FunWorld.Grabbable>() is { Busy: true };
            if (held && v > 1.5f) Buzz(c.collider.GetComponentInParent<FunWorld.Grabbable>().Holder, Mathf.Clamp01(v / 8f));
            Hit(this, v, -c.relativeVelocity * 0.25f, hand || held);
        }

        public void OnKicked(Vector3 foot, Vector3 facing) => Kick(this, foot, facing);
    }

    private static float _lastThud;
    private static readonly List<Spot> Spots = new List<Spot>();
    private static bool _built;
    private static Transform _root;
    private static readonly List<Weapon> Rack = new List<Weapon>();

    /// <summary>A thing broke (its kind, where): the RAGE MODE score counts it.</summary>
    public static event Action<string, Vector3> Smashed;

    /// <summary>How hard a thing has to be hit to break (m/s): glass and china go easily, wood takes a kick.</summary>
    private static float Fragility(string kind, bool byHand) => kind switch
    {
        "bottle_green" or "bottle_brown" => byHand ? 2.5f : 3.0f,
        "vase" => byHand ? 2.8f : 3.4f,
        "plate" => byHand ? 2.6f : 3.0f,
        "gnome" => byHand ? 3.4f : 4.2f,
        "tv" or "jukebox" => byHand ? 4.5f : 5.0f,
        "fishtank" => byHand ? 2.6f : 3.0f,
        "vending_glass" => byHand ? 3.5f : 4.5f,
        "pinata" => byHand ? 3.0f : 3.0f,
        "barrel" => byHand ? 6.5f : 8f,
        "box" => 7f,
        "can" or "cooler_jug" or "vending" or "cooler" => 999f,
        _ => byHand ? 9f : 7.5f, // crates, chairs, tables
    };

    private static (string game, string own) SoundOf(string kind) => kind switch
    {
        "bottle_green" or "bottle_brown" => ("Bottle Smash", "glass"),
        "vase" or "plate" => ("Smash Ceramic Heavy", "china"),
        "gnome" => ("Smash Ceramic Heavy", "plaster"),
        "tv" => ("TV Impacts", "tv"),
        "jukebox" => ("Wood Smash", "tv"),
        "fishtank" or "vending_glass" => ("Window Smash", "glass"),
        "box" => ("Cardboard box Impacts", "cardboard"),
        "pinata" => ("Kick Soft", "cardboard"),
        "barrel" => ("Barrel Explosion", "door_bang"),
        _ => ("Wood Smash", "wood"),
    };

    private static float MassOf(string kind) => kind switch
    {
        "bottle_green" or "bottle_brown" => 0.4f,
        "vase" => 1.2f,
        "plate" => 0.5f,
        "gnome" => 2f,
        "tv" => 9f,
        "jukebox" => 22f,
        "fishtank" => 10f,
        "box" => 1.5f,
        "crate" => 6f,
        "table" => 8f,
        "chair" => 4f,
        "can" => 0.35f,
        "cooler_jug" => 6f,
        "barrel" => 30f,
        "pinata" => 1.5f,
        _ => 2f,
    };

    private static bool Grabs(string kind) => kind is "bottle_green" or "bottle_brown" or "vase" or "plate" or "gnome" or "box" or "can" or "chair" or "cooler_jug";

    private static bool Fixed(string kind) => kind is "vending" or "vending_glass" or "cooler";

    public static bool Ready => _built && FunWorld.Ready;

    private static readonly string[] Kinds =
        { "bottle_green", "bottle_brown", "vase", "gnome", "tv", "box", "crate", "table", "chair", "jukebox", "fishtank", "plate", "vending_glass", "pinata", "barrel" };

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

    public static void Init()
    {
        FunWorld.Tick += Tick;
        FunWorld.BeforeStep += Step;
        FunWorld.Clearing += Clear;
        FunWorld.Clearing += RageMode.Clear;
        FunWorld.Clearing += Dummy.Clear;
    }

    /// <summary>Every frame in the movie house: built when the doors open; broken things come back.</summary>
    private static void Tick()
    {
        if (!_built)
        {
            if (!MovieHouse.DoorOpen) return;
            Build();
        }

        var now = Time.unscaledTime;
        // broken things come back once you've stepped away from where they stood
        foreach (var s in Spots)
        {
            if (s.Live != null && !s.Live.Broken) continue;
            if (s.BrokeAt < 0f || now - s.BrokeAt < s.Respawn) continue;
            var at = _root.TransformPoint(s.Pos);
            if ((new Vector3(CinemaRig.HeadPosition.x, at.y, CinemaRig.HeadPosition.z) - at).magnitude < 1.4f) continue;
            Spawn(s);
            GameSounds.PlayOr("Kick Soft", "step_hard", at, 0.25f, 1.4f);
        }

        // kicked barrels go off soon anyway
        foreach (var s in Spots)
            if (s.Kind == "barrel" && s.Live != null && !s.Live.Broken && s.Live.Armed > 0f && now - s.Live.Armed > 2f)
                Break(s.Live, Vector3.up);

        Jukebox();
        Fish();
        Jugs();
        Neon();
        foreach (var w in Rack) w.Home();
    }

    private static void Step(float dt)
    {
        if (!_built) return;
        foreach (var w in Rack) w.Fly(dt);
    }

    // ================================================================================== hitting things

    private static void Hit(Item item, float v, Vector3 push, bool byHand)
    {
        if (item.Broken) return;
        var kind = item.Kind;
        if (kind == "pinata")
        {
            if (v > 2.2f)
            {
                item.Hits++;
                GameSounds.PlayOr("Kick Soft", "cardboard", item.transform.position, Mathf.Clamp01(v / 6f));
                if (item.Hits >= 4 || v > 7f) Break(item, push);
            }

            return;
        }

        if (kind == "barrel")
        {
            if (v > 3f) GameSounds.PlayOr("Barrel Impacts", "thud", item.transform.position, Mathf.Clamp01(v / 8f));
            if (v > Fragility(kind, byHand) || (item.Armed > 0f && v > 2.5f && Time.unscaledTime - item.Armed > 0.15f)) Break(item, push);
            return;
        }

        if (kind == "tv" && item.Cracks == null && v > Fragility(kind, byHand) * 0.55f && v <= Fragility(kind, byHand))
        {
            Crack(item);
            return;
        }

        if (v > Fragility(kind, byHand)) Break(item, push);
        else if (v > 2.2f && Time.unscaledTime - _lastThud > 0.08f)
        {
            _lastThud = Time.unscaledTime;
            var bank = kind switch
            {
                "can" => "Can Full Impacts",
                "bottle_green" or "bottle_brown" => "Bottle Impacts",
                "cooler_jug" => "Watercooler jug impacts",
                "box" => "Cardboard box Impacts",
                "plate" or "vase" or "gnome" => "Ceramic Impacts",
                "tv" => "TV Impacts",
                _ => "Heavy Wood Impacts",
            };
            GameSounds.PlayOr(bank, v > 4f ? "thud" : "step_hard", item.transform.position, Mathf.Clamp01(v / 8f));
        }
    }

    private static void Kick(Item item, Vector3 foot, Vector3 facing)
    {
        if (item.Broken) return;
        var kind = item.Kind;
        var push = facing * 9f + Vector3.up * 3f;
        switch (kind)
        {
            case "vending":
                Vending(item);
                return;
            case "vending_glass":
                item.Hits++;
                GameSounds.PlayOr("Window Glass Impacts", "glass", item.transform.position, 0.8f);
                if (item.Hits >= 2) Break(item, push * 0.4f);
                return;
            case "cooler":
                GameSounds.PlayOr("Kick Watercooler jug", "thud", item.transform.position, 0.9f);
                foreach (var s in Spots)
                    if (s.Kind == "cooler_jug" && s.Live != null && !s.Live.Broken)
                    {
                        s.Live.Body.isKinematic = false;
                        s.Live.Body.AddForce((facing * 4f + Vector3.up * 2f) * s.Live.Body.mass, ForceMode.Impulse);
                    }

                return;
            case "barrel":
                // kicked: it flies, fizzing, and goes off when it hits something (or a moment later)
                GameSounds.PlayOr("Barrel Kick", "thud", item.transform.position, 1f);
                if (item.Armed > 0f)
                {
                    Break(item, push);
                    return;
                }

                item.Armed = Time.unscaledTime;
                item.Body.AddForceAtPosition((facing * 7f + Vector3.up * 2.5f) * item.Body.mass, foot + Vector3.up * 0.2f, ForceMode.Impulse);
                GameSounds.Play("Bomb guy ignite", item.transform.position, 0.8f);
                Fx.Sparks(item.transform.TransformPoint(new Vector3(0.14f, 0.9f, 0f)), Vector3.up, 25);
                return;
            case "tv" when item.Cracks == null && UnityEngine.Random.value < 0.5f:
                Crack(item);
                item.Body.AddForceAtPosition(push * item.Body.mass * 0.3f, foot, ForceMode.Impulse);
                return;
            case "can":
            case "cooler_jug":
                item.Body.AddForce(push * 0.7f, ForceMode.VelocityChange);
                GameSounds.PlayOr(kind == "can" ? "Can Full Impacts" : "Kick Watercooler jug", "thud", item.transform.position, 0.8f);
                return;
        }

        var sturdy = kind is "crate" or "table" or "box";
        if (sturdy && UnityEngine.Random.value < 0.35f)
            item.Body.AddForceAtPosition(push * item.Body.mass * 0.9f, foot, ForceMode.Impulse); // it flies (and may break as it lands)
        else Break(item, push);
    }

    private static void Break(Item item, Vector3 push)
    {
        if (item.Broken) return;
        item.Broken = true;
        Surprises.Smashed();
        var spot = item.Spot;
        var kind = item.Kind;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (spot != null) spot.BrokeAt = Time.unscaledTime;
        var t = item.transform;
        var vel = item.Body.isKinematic ? Vector3.zero : item.Body.velocity;
        var ang = item.Body.isKinematic ? Vector3.zero : item.Body.angularVelocity;
        var (game, own) = SoundOf(kind);
        GameSounds.PlayOr(game, own, t.position, 1f);
        if (kind is "tv")
        {
            Fx.Sparks(t.TransformPoint(new Vector3(0f, 0.3f, 0.2f)), t.forward, 50);
            Fx.Smoke(t.TransformPoint(new Vector3(0f, 0.3f, 0f)), 2.5f);
            GameSounds.Play("Lightbulb Smash", t.position, 0.7f);
        }

        if (kind == "fishtank") Flood(item);
        if (kind == "barrel") Blast(item);
        if (kind == "pinata") Candy(item);
        var pieces = Props.PiecesOf(kind);
        var centre = t.TransformPoint(item.Body.centerOfMass);
        foreach (var p in pieces)
        {
            var go = new GameObject("Piece " + p.Name) { layer = FunWorld.PhysLayer };
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(t.TransformPoint(p.Offset), t.rotation);
            var vis = new GameObject("Visual") { layer = VrUi.Layer };
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
            vis.AddComponent<MeshRenderer>().sharedMaterials = Props.MaterialsOf(p);
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true; // (before the mesh: else it's cooked as a triangle mesh first)
            mc.sharedMesh = p.Mesh;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, MassOf(kind) / Mathf.Max(1, pieces.Count));
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var outward = (go.transform.position - centre).normalized;
            var blast = kind == "barrel" ? 3.5f : 1f;
            rb.velocity = vel + push * UnityEngine.Random.Range(0.6f, 1.1f) + outward * UnityEngine.Random.Range(1f, 2.5f) * blast;
            rb.angularVelocity = ang + UnityEngine.Random.insideUnitSphere * 10f;
            var fish = Array.IndexOf(p.Materials, "fish_orange") >= 0;
            if (fish)
            {
                go.AddComponent<Flop>().Body = rb;
                var g = FunWorld.MakeGrabbable(go, Vector3.zero, Quaternion.identity);
                g.Reach = 0.12f;
            }

            FunWorld.AddDebris(go, fish ? 40f : 14f);
        }

        if (item.Loop != null) Destroy(item.Loop.gameObject);
        if (item.Cracks != null) Destroy(item.Cracks);
        var held = item.GetComponent<FunWorld.Grabbable>();
        if (held != null && held.Holder.HasValue) FunWorld.Release(held.Holder.Value, false);
        UnityEngine.Object.Destroy(item.gameObject);
        Smashed?.Invoke(kind, t.position);
        if (Plugin.DevRun) Plugin.L.LogInfo($"Rage room: {kind} broke ({push.magnitude:F1}, {pieces.Count} pieces in {watch.Elapsed.TotalMilliseconds:F2} ms)");
        VrControls.Rumble(XRNode.LeftHand, 0.4f, 0.05f);
        VrControls.Rumble(XRNode.RightHand, 0.4f, 0.05f);
    }

    /// <summary>One of the mod's breakables broken where it stands (the gun range's bottles and barrels): its pieces fly,
    /// its sound plays.</summary>
    public static void Shatter(Transform t, string kind, Vector3 push)
    {
        var (game, own) = SoundOf(kind);
        GameSounds.PlayOr(game, own, t.position, 1f);
        var pieces = Props.PiecesOf(kind);
        foreach (var p in pieces)
        {
            var go = new GameObject("Piece " + p.Name) { layer = FunWorld.PhysLayer };
            go.transform.SetParent(FunWorld.Root, false);
            go.transform.SetPositionAndRotation(t.TransformPoint(p.Offset), t.rotation);
            var vis = new GameObject("Visual") { layer = VrUi.Layer };
            vis.transform.SetParent(go.transform, false);
            vis.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
            vis.AddComponent<MeshRenderer>().sharedMaterials = Props.MaterialsOf(p);
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = p.Mesh;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.05f, MassOf(kind) / Mathf.Max(1, pieces.Count));
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.velocity = push * UnityEngine.Random.Range(0.5f, 1.1f) + (go.transform.position - t.position).normalized * UnityEngine.Random.Range(1f, 2.5f) * (kind == "barrel" ? 3.5f : 1f);
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 10f;
            FunWorld.AddDebris(go, 12f);
        }
    }

    private static void Destroy(UnityEngine.Object o)
    {
        if (o != null) UnityEngine.Object.Destroy(o);
    }

    private static void Buzz(XRNode? hand, float amount)
    {
        if (hand.HasValue) VrControls.Rumble(hand.Value, 0.2f + amount * 0.6f, 0.05f);
    }

    // ================================================================================== what each thing does

    private static void Crack(Item tv)
    {
        tv.Cracks = Fx.Cracks(tv.transform.Find("Visual"), new Vector3(-0.04f, 0.27f, 0.205f), new Vector2(0.44f, 0.33f));
        Fx.Sparks(tv.transform.TransformPoint(new Vector3(-0.04f, 0.27f, 0.22f)), tv.transform.forward, 18);
        GameSounds.PlayOr("Window Glass Impacts", "glass", tv.transform.position, 0.8f);
        tv.Loop = GameSounds.Loop("TV static loop", tv.transform, 0.35f);
    }

    private static void Flood(Item tank)
    {
        var at = tank.transform.TransformPoint(new Vector3(0f, 0.25f, 0f));
        Fx.Gush(at, Vector3.down + tank.transform.forward * 0.3f, 1.6f, 1.3f, _root.position.y);
        GameSounds.Play("Prop Splashes", at, 1f);
        GameSounds.Play("bucket spill", at, 1f);
    }

    private static void Blast(Item barrel)
    {
        var at = barrel.transform.TransformPoint(new Vector3(0f, 0.45f, 0f));
        Fx.Explosion(at, 1.2f);
        VrControls.Rumble(XRNode.LeftHand, 1f, 0.25f);
        VrControls.Rumble(XRNode.RightHand, 1f, 0.25f);
        var hits = new Collider[64];
        var n = FunWorld.Overlap(at, 3.2f, hits);
        var near = new List<Item>();
        for (var i = 0; i < n; i++)
        {
            var rb = hits[i].attachedRigidbody;
            var it = hits[i].GetComponentInParent<Item>();
            if (it != null && it != barrel && !it.Broken && !near.Contains(it)) near.Add(it);
            if (rb != null && !rb.isKinematic) rb.AddExplosionForce(14f * Mathf.Max(1f, rb.mass * 0.25f), at, 3.2f, 0.6f, ForceMode.Impulse);
        }

        foreach (var it in near)
        {
            var d = (it.transform.position - at).magnitude;
            var away = (it.transform.position - at).normalized * 6f + Vector3.up * 3f;
            if (it.Kind == "barrel") it.Hits = 99;
            if (d < 2.4f && Fragility(it.Kind, false) < 50f) Break(it, away);
            else if (!it.Body.isKinematic) it.Body.AddForce(away, ForceMode.VelocityChange);
        }

        Dummy.Blast(at, 3.2f);
    }

    private static void Candy(Item pinata)
    {
        var at = pinata.transform.TransformPoint(new Vector3(0f, 0.3f, 0f));
        Fx.Confetti(at);
        GameSounds.Play("popcorn impacts", at, 1f);
        string[] candies = { "candy_a", "candy_b", "candy_c" };
        for (var k = 0; k < 14; k++)
        {
            var c = FunWorld.Thing(candies[k % 3], _root.InverseTransformPoint(at + UnityEngine.Random.insideUnitSphere * 0.15f), UnityEngine.Random.Range(0f, 360f), 0.03f);
            if (c == null) continue;
            var rb = c.GetComponent<Rigidbody>();
            rb.WakeUp();
            rb.velocity = UnityEngine.Random.insideUnitSphere * 3f + Vector3.up * 1.5f;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f;
            FunWorld.MakeGrabbable(c, Vector3.zero, Quaternion.identity).Reach = 0.12f;
            FunWorld.AddDebris(c, 30f);
        }

        foreach (var s in Spots)
            if (s.Kind == "pinata")
                s.Respawn = 12f;
    }

    private static int _vendKicks;

    private static void Vending(Item machine)
    {
        _vendKicks++;
        var t = machine.transform;
        GameSounds.PlayOr("Kick Heavy Metal", "thud", t.position + Vector3.up, 1f);
        // a shake, and a can down into the tray most kicks
        machine.gameObject.AddComponent<Shake>();
        if (UnityEngine.Random.value < 0.7f)
        {
            string[] cans = { "can", "can_blue", "can_green" };
            var at = t.TransformPoint(new Vector3(-0.1f, 0.32f, 0.36f));
            var c = Spawn(new Spot { Kind = cans[UnityEngine.Random.Range(0, 3)], Pos = _root.InverseTransformPoint(at), Yaw = UnityEngine.Random.Range(0f, 360f) }, false);
            if (c != null)
            {
                c.Body.WakeUp();
                c.Body.velocity = t.forward * 0.6f + Vector3.up * 0.4f;
                c.Spot = null;
                c._kind = c.name.Substring(5);
                FunWorld.AddDebris(c.gameObject, 60f);
                GameSounds.Play("Can Full Impacts", at, 0.9f);
            }
        }

        if (_vendKicks % 3 == 0)
            foreach (var s in Spots)
                if (s.Kind == "vending_glass" && s.Live != null && !s.Live.Broken)
                    Break(s.Live, t.forward * 3f);
    }

    /// <summary>The fish flop about on the floor a while.</summary>
    private sealed class Flop : MonoBehaviour
    {
        public Rigidbody Body;
        private float _born, _next;

        private void Start()
        {
            _born = Time.unscaledTime;
            _next = _born + 0.8f;
        }

        public void Tick()
        {
            if (Body == null || Body.isKinematic || Time.unscaledTime < _next) return;
            var age = Time.unscaledTime - _born;
            if (age > 25f) return;
            _next = Time.unscaledTime + UnityEngine.Random.Range(0.4f, 1.1f) + age * 0.05f;
            if (Body.position.y - FunWorld.Root.position.y > 0.12f) return; // (on the floor)
            var k = Mathf.Lerp(1f, 0.3f, age / 25f);
            Body.AddForce((Vector3.up * UnityEngine.Random.Range(1.2f, 2f) + UnityEngine.Random.insideUnitSphere * 0.5f) * k, ForceMode.VelocityChange);
            Body.AddTorque(UnityEngine.Random.insideUnitSphere * 0.02f * k, ForceMode.Impulse);
            if (UnityEngine.Random.value < 0.5f) GameSounds.Play("Prop Splashes", Body.position, 0.2f, 1.6f);
        }
    }

    private static void Fish()
    {
        foreach (var f in _root.GetComponentsInChildren<Flop>()) f.Tick();
    }

    // the cooler's jug: off its cooler, it glugs out over the floor for a while
    private static readonly Dictionary<Item, float> Glugging = new Dictionary<Item, float>();

    private static void Jugs()
    {
        foreach (var s in Spots)
        {
            if (s.Kind != "cooler_jug" || s.Live == null || s.Live.Broken || Glugging.ContainsKey(s.Live)) continue;
            var jug = s.Live;
            var off = (jug.transform.localPosition - s.Pos).magnitude > 0.25f || Vector3.Angle(jug.transform.up, Vector3.up) > 50f;
            if (!off) continue;
            jug.Body.isKinematic = false;
            Glugging[jug] = Time.unscaledTime + 7f;
            Fx.Pour(jug.transform, new Vector3(0f, 0.97f, 0f), 7f, _root.position.y);
            Fx.Puddle(new Vector3(jug.transform.position.x, _root.position.y + 0.004f, jug.transform.position.z), 1.0f, 7f, 40f);
            jug.Loop = GameSounds.Loop("Faucet Water loop", jug.transform, 0.6f);
            GameSounds.Play("bucket slosh", jug.transform.position, 0.9f);
            s.BrokeAt = Time.unscaledTime; // (a fresh one comes later)
            s.Respawn = 18f;
            jug.Spot = null;
            jug._kind = "cooler_jug";
            FunWorld.AddDebris(jug.gameObject, 18f);
        }

        var done = new List<Item>();
        foreach (var kv in Glugging)
            if (kv.Key == null || Time.unscaledTime > kv.Value)
                done.Add(kv.Key);
        foreach (var d in done)
        {
            if (d != null && d.Loop != null) Destroy(d.Loop.gameObject);
            Glugging.Remove(d);
        }
    }

    // ================================================================================== the jukebox

    private static AudioSource _music;
    private static List<AudioClip> _tracks;
    private static int _track;
    private static float _warble = -1f;

    private static void Jukebox()
    {
        Spot box = null;
        foreach (var s in Spots)
            if (s.Kind == "jukebox")
                box = s;
        var alive = box?.Live != null && !box.Live.Broken;
        if (!alive)
        {
            if (_music != null)
            {
                UnityEngine.Object.Destroy(_music.gameObject);
                _music = null;
            }

            return;
        }

        if (_music == null || _music.transform.parent != box.Live.transform)
        {
            _tracks ??= GameSounds.Tracks();
            if (_tracks.Count == 0) return;
            var go = new GameObject("AngerFootVR jukebox");
            go.transform.SetParent(box.Live.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            _music = GameSounds.Configure(go.AddComponent<AudioSource>());
            _music.minDistance = 1.2f;
            _music.maxDistance = 11f;
            _music.loop = true;
            _music.clip = _tracks[_track % _tracks.Count];
            _music.Play();
        }

        _music.volume = 0.55f * GameSounds.MusicVolume;
        if (_warble >= 0f)
        {
            // a hit: the record skips and warbles, then the next track
            var t = Time.unscaledTime - _warble;
            _music.pitch = t < 0.6f ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 22f)) : 1f;
            if (t >= 0.6f)
            {
                _warble = -1f;
                _track++;
                _music.clip = _tracks[_track % _tracks.Count];
                _music.Play();
            }
        }
    }

    /// <summary>The jukebox got hit (not hard enough to break): it skips to the next track.</summary>
    private static void Skip()
    {
        if (_warble < 0f) _warble = Time.unscaledTime;
    }

    // ================================================================================== neon signs

    private sealed class Sign
    {
        public TextMeshPro Text;
        public Color On;
        public float DeadUntil = -1f;
        public Rigidbody Board;
    }

    private static readonly List<Sign> Signs = new List<Sign>();

    private sealed class SignHit : MonoBehaviour
    {
        public Sign Sign;

        private void OnCollisionEnter(Collision c)
        {
            if (c.relativeVelocity.magnitude < 2.5f || Sign.DeadUntil > Time.unscaledTime) return;
            Sign.DeadUntil = Time.unscaledTime + 14f;
            Fx.Sparks(c.GetContact(0).point, c.GetContact(0).normal, 40);
            GameSounds.PlayOr("Lightbulb Smash", "glass", transform.position, 1f);
            Smashed?.Invoke("neon", transform.position);
        }
    }

    private static void Neon()
    {
        var now = Time.unscaledTime;
        foreach (var s in Signs)
        {
            if (s.Text == null) continue;
            if (s.DeadUntil > now)
            {
                // dying: a few flickers, then dark
                var t = 14f - (s.DeadUntil - now);
                var on = t < 0.8f && Mathf.Sin(t * 60f) > 0.3f;
                s.Text.color = on ? s.On : new Color(s.On.r * 0.12f, s.On.g * 0.12f, s.On.b * 0.12f, 1f);
            }
            else s.Text.color = s.On;
        }
    }

    private static void SignOn(string text, Vector3 local, Vector3 facing, float size, Color colour)
    {
        var tmp = MovieHouse.SignFacing(text, local, size, colour, facing, FunWorld.Root);
        if (tmp == null) return;
        var board = FunWorld.Solid(local, new Vector3(text.Length * size * 0.55f, size * 1.1f, 0.06f), Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg);
        var rb = board.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var sign = new Sign { Text = tmp, On = colour, Board = rb };
        board.AddComponent<SignHit>().Sign = sign;
        Signs.Add(sign);
    }

    // ================================================================================== the room

    private static void Build()
    {
        if (!FunWorld.Ready || !Props.Load()) return;
        _built = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _root = FunWorld.Root;
        Spots.Clear();
        Signs.Clear();
        Rack.Clear();
        Glugging.Clear();
        _vendKicks = 0;
        _music = null;

        // the room's solid parts (seen by the movie house's own walls: these only stop things)
        var lo = MovieHouse.RageMin;
        var hi = MovieHouse.RageMax;
        var mid = (lo + hi) / 2;
        FunWorld.Solid(new Vector3(mid.x, -0.25f, mid.z), new Vector3(hi.x - lo.x + 2f, 0.5f, hi.z - lo.z + 2f));
        FunWorld.Solid(new Vector3(mid.x, hi.y + 0.25f, mid.z), new Vector3(hi.x - lo.x, 0.5f, hi.z - lo.z));
        FunWorld.Solid(new Vector3(lo.x - 0.25f, hi.y / 2, mid.z), new Vector3(0.5f, hi.y, hi.z - lo.z));
        FunWorld.Solid(new Vector3(hi.x + 0.25f, hi.y / 2, mid.z), new Vector3(0.5f, hi.y, hi.z - lo.z));
        FunWorld.Solid(new Vector3(mid.x, hi.y / 2, lo.z - 0.25f), new Vector3(hi.x - lo.x, hi.y, 0.5f));
        FunWorld.Solid(new Vector3(mid.x, hi.y / 2, hi.z + 0.1f), new Vector3(hi.x - lo.x, hi.y, 0.4f)); // the front wall, its doorway closed to flying things

        // shelves of bottles and vases along the back wall
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
                    Spots.Add(new Spot { Kind = kind, Pos = new Vector3(sx - 1.0f + b * 0.4f, y + 0.025f, lo.z + 0.45f), Yaw = UnityEngine.Random.Range(0f, 360f) });
                }
            }

            foreach (var px in new[] { -1.18f, 1.18f })
                Shelf(new Vector3(sx + px, 0.8f, lo.z + 0.45f), new Vector3(0.05f, 1.6f, 0.55f), metal);
        }

        // the plates in their rack, on the back wall between the shelves
        FunWorld.Picture("dish_rack", _root, new Vector3(4f, 1.0f, lo.z + 0.12f), Quaternion.identity);
        Shelf(new Vector3(4f, 1.015f, lo.z + 0.12f), new Vector3(1.3f, 0.03f, 0.22f), null);
        for (var k = 0; k < 10; k++)
            Spots.Add(new Spot { Kind = "plate", Pos = new Vector3(3.45f + k * 0.12f, 1.035f, lo.z + 0.16f), Yaw = 0f });

        // tables with TVs and vases
        Spots.Add(new Spot { Kind = "table", Pos = new Vector3(2.4f, 0f, -10.2f), Yaw = 10f });
        Spots.Add(new Spot { Kind = "tv", Pos = new Vector3(2.4f, 0.76f, -10.2f), Yaw = 190f });
        Spots.Add(new Spot { Kind = "table", Pos = new Vector3(6.2f, 0f, -11.6f), Yaw = -15f });
        Spots.Add(new Spot { Kind = "tv", Pos = new Vector3(6.0f, 0.76f, -11.6f), Yaw = 160f });
        Spots.Add(new Spot { Kind = "vase", Pos = new Vector3(6.55f, 0.76f, -11.45f), Yaw = 0f });
        // crates, stacked
        foreach (var (x, z, y) in new[] { (0.0f, -8.0f, 0f), (0.65f, -8.0f, 0f), (0.32f, -8.05f, 0.63f), (8.0f, -9.2f, 0f), (8.0f, -9.85f, 0f), (8.0f, -9.5f, 0.63f), (0.2f, -13.0f, 0f) })
            Spots.Add(new Spot { Kind = "crate", Pos = new Vector3(x, y, z), Yaw = UnityEngine.Random.Range(-8f, 8f) });
        // boxes in a pile
        foreach (var (x, z, y) in new[] { (7.4f, -14.4f, 0f), (7.9f, -14.0f, 0f), (7.6f, -14.2f, 0.33f), (6.9f, -14.6f, 0f), (7.3f, -14.3f, 0.66f) })
            Spots.Add(new Spot { Kind = "box", Pos = new Vector3(x, y, z), Yaw = UnityEngine.Random.Range(-20f, 20f) });
        // the jukebox against the left wall, the fish tank on its own table by the right wall
        Spots.Add(new Spot { Kind = "jukebox", Pos = new Vector3(-0.68f, 0f, -11.0f), Yaw = 90f });
        Spots.Add(new Spot { Kind = "table", Pos = new Vector3(8.35f, 0f, -12.6f), Yaw = 90f });
        Spots.Add(new Spot { Kind = "fishtank", Pos = new Vector3(8.35f, 0.76f, -12.6f), Yaw = -90f });
        // chairs, gnomes
        Spots.Add(new Spot { Kind = "chair", Pos = new Vector3(3.3f, 0f, -9.4f), Yaw = 200f });
        Spots.Add(new Spot { Kind = "chair", Pos = new Vector3(5.2f, 0f, -12.6f), Yaw = 30f });
        Spots.Add(new Spot { Kind = "chair", Pos = new Vector3(1.4f, 0f, -11.2f), Yaw = 120f });
        foreach (var (x, z) in new[] { (4.0f, -8.4f), (1.0f, -9.6f), (7.2f, -8.0f), (4.6f, -14.6f) })
            Spots.Add(new Spot { Kind = "gnome", Pos = new Vector3(x, 0f, z), Yaw = 180f + UnityEngine.Random.Range(-30f, 30f) });
        // the vending machine and the water cooler along the right wall, facing into the room
        Spots.Add(new Spot { Kind = "vending", Pos = new Vector3(8.5f, 0f, -7.4f), Yaw = -90f });
        Spots.Add(new Spot { Kind = "vending_glass", Pos = new Vector3(8.5f, 0f, -7.4f), Yaw = -90f, Respawn = 10f });
        Spots.Add(new Spot { Kind = "cooler", Pos = new Vector3(8.75f, 0f, -10.8f), Yaw = -90f });
        Spots.Add(new Spot { Kind = "cooler_jug", Pos = new Vector3(8.75f, 0f, -10.8f), Yaw = -90f, Respawn = 18f });
        // the red barrels in the far left corner, the piñata hanging in the middle
        Spots.Add(new Spot { Kind = "barrel", Pos = new Vector3(0.0f, 0f, -15.55f), Yaw = 20f, Respawn = 10f });
        Spots.Add(new Spot { Kind = "barrel", Pos = new Vector3(0.05f, 0f, -14.85f), Yaw = 160f, Respawn = 10f });
        Spots.Add(new Spot { Kind = "pinata", Pos = new Vector3(4f, 1.55f, -11.9f), Yaw = 30f, Respawn = 12f });

        foreach (var s in Spots) Spawn(s);

        // neon on the walls (throw something at them and they blow)
        SignOn("SMASH", new Vector3(lo.x + 0.04f, 2.9f, -12.6f), Vector3.right, 0.55f, new Color(0.15f, 0.95f, 1f));
        SignOn("NO REFUNDS", new Vector3(hi.x - 0.04f, 3.1f, -13.6f), Vector3.left, 0.34f, new Color(1f, 0.16f, 0.62f));
        SignOn("BREAK IT", new Vector3(4f, 3.55f, lo.z + 0.04f), Vector3.forward, 0.42f, new Color(1f, 0.85f, 0.2f));

        // the weapon rack by the door: the game's own bat, machete, knife and claw, and the mod's sledgehammer, pipe
        // and golf club
        FunWorld.Picture("weapon_rack", _root, new Vector3(lo.x + 0.04f, 0.6f, -7.9f), Quaternion.Euler(0f, 90f, 0f));
        var slot = 0;
        foreach (var name in new[] { "Bat", "Machete", "Knife", "Gimp Claw" })
            if (Weapon.FromGame(name, _root, RackSlot(slot), out var w))
            {
                Rack.Add(w);
                slot++;
            }

        foreach (var name in new[] { "sledge", "pipe", "golfclub", "bat" })
        {
            if (slot >= 7) break;
            if (name == "bat" && Rack.Exists(w => w.Name == "Bat")) continue;
            if (Weapon.FromProps(name, _root, RackSlot(slot), out var w))
            {
                Rack.Add(w);
                slot++;
            }
        }

        Dummy.Build(_root, new Vector3(6.4f, 0f, -8.9f));
        RageMode.Build(_root);
        Plugin.L.LogInfo($"Rage room: {Spots.Count} things to break, {Rack.Count} weapons on the rack (built in {watch.Elapsed.TotalMilliseconds:F1} ms)");
        Surprises.RageVisit();
    }

    /// <summary>A place on the weapon rack (local to the world): along the board, hanging handle down.</summary>
    private static Pose RackSlot(int i) => new Pose(new Vector3(MovieHouse.RageMin.x + 0.12f, 0.75f, -7.9f - 0.63f + i * 0.21f), Quaternion.identity);

    private static void Shelf(Vector3 localPos, Vector3 size, Material m)
    {
        var go = new GameObject("Shelf") { layer = FunWorld.PhysLayer };
        go.transform.SetParent(_root, false);
        go.transform.localPosition = localPos;
        go.AddComponent<BoxCollider>().size = size;
        if (m == null) return;
        var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.Destroy(vis.GetComponent<Collider>());
        vis.layer = VrUi.Layer;
        vis.transform.SetParent(go.transform, false);
        vis.transform.localScale = size;
        vis.GetComponent<MeshRenderer>().sharedMaterial = m;
    }

    private static void Spawn(Spot s) => Spawn(s, true);

    private static Item Spawn(Spot s, bool track)
    {
        var round = s.Kind is "bottle_green" or "bottle_brown" or "vase" or "gnome" or "can" or "can_blue" or "can_green" or "barrel" or "cooler_jug";
        var kinematic = Fixed(s.Kind) || s.Kind == "pinata";
        var go = FunWorld.Thing(s.Kind, s.Pos, s.Yaw, MassOf(s.Kind), round, kinematic);
        if (go == null) return null;
        go.name = "Item " + s.Kind;
        var item = go.AddComponent<Item>();
        item.Spot = track ? s : null;
        item._kind = s.Kind;
        item.Body = go.GetComponent<Rigidbody>();
        if (s.Kind == "plate")
        {
            // (stood on its edge in the rack: kept there until something knocks it)
            item.Body.isKinematic = false;
            item.Body.Sleep();
        }

        if (s.Kind == "pinata") Hang(item);
        if (Grabs(s.Kind))
        {
            var b = go.GetComponent<Collider>().bounds;
            var g = FunWorld.MakeGrabbable(go, go.transform.InverseTransformPoint(b.center), Quaternion.identity);
            g.Reach = Mathf.Clamp(b.extents.magnitude + 0.05f, 0.14f, 0.35f);
            if (s.Kind is "can" or "can_blue" or "can_green") Drinks.MakeDrinkable(g, "Player drink can");
        }

        if (s.Kind == "jukebox") go.AddComponent<JukeboxHit>();
        if (track)
        {
            s.Live = item;
            s.BrokeAt = -1f;
        }

        return item;
    }

    private sealed class JukeboxHit : MonoBehaviour
    {
        private void OnCollisionEnter(Collision c)
        {
            if (c.relativeVelocity.magnitude > 2.5f) Skip();
        }
    }

    /// <summary>The piñata on its rope from the ceiling (it swings when hit).</summary>
    private static void Hang(Item p)
    {
        var anchor = new GameObject("Hook") { layer = FunWorld.PhysLayer };
        anchor.transform.SetParent(_root, false);
        anchor.transform.localPosition = new Vector3(p.Spot != null ? p.Spot.Pos.x : p.transform.localPosition.x, MovieHouse.RageMax.y - 0.1f, p.Spot != null ? p.Spot.Pos.z : p.transform.localPosition.z);
        var arb = anchor.AddComponent<Rigidbody>();
        arb.isKinematic = true;
        p.Body.isKinematic = false;
        p.Body.mass = 1.5f;
        p.Body.drag = 0.4f;
        p.Body.angularDrag = 0.8f;
        var j = p.gameObject.AddComponent<ConfigurableJoint>();
        j.connectedBody = arb;
        j.autoConfigureConnectedAnchor = false;
        j.anchor = new Vector3(0f, 0.6f, 0f);
        j.connectedAnchor = Vector3.zero;
        j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
        j.linearLimit = new SoftJointLimit { limit = (anchor.transform.position - p.transform.TransformPoint(j.anchor)).magnitude };
        j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
        var rope = p.gameObject.AddComponent<Rope>();
        rope.From = anchor.transform;
        rope.To = p.transform;
        rope.Local = j.anchor;
        p.gameObject.AddComponent<Destroyer>().Also = anchor;
    }

    private sealed class Rope : MonoBehaviour
    {
        public Transform From, To;
        public Vector3 Local;
        private LineRenderer _line;

        private void Start()
        {
            var go = new GameObject("Rope") { layer = VrUi.Layer };
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.widthMultiplier = 0.012f;
            _line.useWorldSpace = true;
            _line.sharedMaterial = Props.Mat("cardboard");
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void LateUpdate()
        {
            if (_line == null || From == null || To == null) return;
            _line.SetPosition(0, From.position);
            _line.SetPosition(1, To.TransformPoint(Local));
        }
    }

    private sealed class Destroyer : MonoBehaviour
    {
        public GameObject Also;

        private void OnDestroy()
        {
            if (Also != null) UnityEngine.Object.Destroy(Also);
        }
    }

    /// <summary>RESET: everything back where it was (the RESET lever, a new RAGE MODE run).</summary>
    public static void ResetAll()
    {
        if (!Ready) return;
        foreach (var s in Spots)
        {
            if (s.Live != null && !s.Live.Broken)
            {
                if (s.Live.GetComponent<FunWorld.Grabbable>() is { Busy: true } g) FunWorld.Release(g.Holder.Value, false);
                UnityEngine.Object.Destroy(s.Live.gameObject);
            }

            Spawn(s);
        }

        foreach (var sign in Signs) sign.DeadUntil = -1f;
        foreach (var w in Rack) w.GoHome();
        Dummy.Reset();
        Plugin.L.LogInfo("Rage room: reset");
    }

    // ================================================================================== the weapons on the rack

    /// <summary>A melee weapon on the rack: picked up by its handle, swung (a fast swing breaks things), dropped and
    /// it flies home after a while.</summary>
    private sealed class Weapon
    {
        public string Name;
        public GameObject Go;
        public FunWorld.Grabbable Grab;
        public Pose HomePose;
        private float _flyFrom = -1f;
        private Vector3 _flyStart;
        private Quaternion _flyStartRot;

        public static bool FromProps(string model, Transform root, Pose home, out Weapon w)
        {
            w = null;
            var go = FunWorld.Thing(model, home.position, 0f, model == "sledge" ? 4f : 1.2f, true, true);
            if (go == null) return false;
            w = Make(go, model, home, new Vector3(0f, 0.11f, 0f));
            return true;
        }

        /// <summary>One of the game's own melee weapons (its model, from the player's copy).</summary>
        public static bool FromGame(string name, Transform root, Pose home, out Weapon w)
        {
            w = null;
            var prefab = GameWeapons.Find(name);
            if (prefab == null) return false;
            var go = GameWeapons.Model(prefab, root, out var axis, out var handle, out var length);
            if (go == null) return false;
            go.transform.localPosition = home.position;
            // the model stood on its handle end, its length up (as the mod's own weapons are)
            go.transform.localRotation = Quaternion.FromToRotation(axis, Vector3.up);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            w = Make(go, name, home, handle);
            // (its length where you point: its axis turned up, then up turned forward)
            w.Grab.GripRot = Quaternion.Euler(90f, 0f, 0f) * Quaternion.FromToRotation(axis, Vector3.up);
            return true;
        }

        private static Weapon Make(GameObject go, string name, Pose home, Vector3 handle)
        {
            go.name = "Weapon " + name;
            var w = new Weapon { Name = name, Go = go, HomePose = new Pose(home.position, go.transform.localRotation) };
            // held in your fist by the handle, its length pointing where you point
            w.Grab = FunWorld.MakeGrabbable(go, handle, Quaternion.Euler(90f, 0f, 0f), name == "sledge" ? handle + new Vector3(0f, 0.35f, 0f) : (Vector3?)null);
            w.Grab.Reach = 0.2f;
            w.Grab.OnGrabbed = (g, h) => GameSounds.Play("Bat Swing", g.transform.position, 0.3f, 1.3f);
            go.AddComponent<Swing>().Grab = w.Grab;
            return w;
        }

        /// <summary>Dropped a while and away from the rack: back it flies.</summary>
        public void Home()
        {
            if (Go == null || Grab.Busy || _flyFrom > 0f) return;
            if (Time.unscaledTime - Grab.LastHeld < 6f) return;
            if ((Go.transform.localPosition - HomePose.position).magnitude < 0.05f) return;
            GoHome();
        }

        public void GoHome()
        {
            if (Go == null) return;
            if (Grab.Busy) FunWorld.Release(Grab.Holder.Value, false);
            _flyFrom = Time.unscaledTime;
            _flyStart = Go.transform.localPosition;
            _flyStartRot = Go.transform.localRotation;
            Grab.Body.isKinematic = true;
        }

        public void Fly(float dt)
        {
            if (_flyFrom < 0f || Go == null) return;
            if (Grab.Busy)
            {
                _flyFrom = -1f;
                return;
            }

            var t = Mathf.Clamp01((Time.unscaledTime - _flyFrom) / 0.5f);
            var e = t * t * (3f - 2f * t);
            Grab.Body.MovePosition(FunWorld.World(Vector3.Lerp(_flyStart, HomePose.position, e)));
            Grab.Body.MoveRotation(Quaternion.Slerp(_flyStartRot, HomePose.rotation, e));
            if (t >= 1f) _flyFrom = -1f;
        }
    }

    /// <summary>A held weapon's swing: the whoosh when it's fast, a buzz on what it hits.</summary>
    private sealed class Swing : MonoBehaviour
    {
        public FunWorld.Grabbable Grab;
        private Vector3 _last;
        private float _nextWhoosh;

        private void LateUpdate()
        {
            // (60 cm along it from the hand: where you point, in its own turn)
            var tip = transform.TransformPoint(Grab.GripLocal) + transform.rotation * Quaternion.Inverse(Grab.GripRot) * Vector3.forward * 0.6f;
            if (Grab.Busy && Time.unscaledDeltaTime > 0f)
            {
                var v = (tip - _last).magnitude / Time.unscaledDeltaTime;
                if (v > 6f && Time.unscaledTime > _nextWhoosh)
                {
                    _nextWhoosh = Time.unscaledTime + 0.35f;
                    GameSounds.PlayOr("Bat Swing", "kick", tip, Mathf.Clamp01(v / 14f));
                }
            }

            _last = tip;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (!Grab.Busy) return;
            var v = c.relativeVelocity.magnitude;
            if (v < 1.5f) return;
            VrControls.Rumble(Grab.Holder.Value, Mathf.Clamp01(0.3f + v / 10f), 0.07f);
            if (Grab.Fore.HasValue) VrControls.Rumble(Grab.Fore.Value, Mathf.Clamp01(0.3f + v / 10f), 0.07f);
            if (c.collider.GetComponentInParent<Item>() == null && v > 3f) GameSounds.PlayOr("Kick Concrete", "thud", c.GetContact(0).point, Mathf.Clamp01(v / 10f));
        }
    }

    // ================================================================================== the shake (the vending machine)

    private sealed class Shake : MonoBehaviour
    {
        private float _born;
        private Transform _vis;

        private void Start()
        {
            _born = Time.unscaledTime;
            _vis = transform.Find("Visual");
        }

        private void Update()
        {
            var t = Time.unscaledTime - _born;
            if (_vis != null) _vis.localRotation = Quaternion.Euler(Mathf.Sin(t * 45f) * 2.5f * Mathf.Exp(-t * 6f), 0f, Mathf.Cos(t * 38f) * 1.5f * Mathf.Exp(-t * 6f));
            if (t > 0.8f)
            {
                if (_vis != null) _vis.localRotation = Quaternion.identity;
                Destroy(this);
            }
        }
    }

    /// <summary>Gone (the movie house left, the level loaded): the next visit builds it fresh.</summary>
    public static void Clear()
    {
        _built = false;
        _root = null;
        Spots.Clear();
        Signs.Clear();
        Rack.Clear();
        Glugging.Clear();
        _music = null;
        _tracks = null;
    }

    public static string Describe()
    {
        var live = 0;
        foreach (var s in Spots)
            if (s.Live != null && !s.Live.Broken)
                live++;
        return $"RAGE ready={Ready} doors={(MovieHouse.DoorOpen ? "open" : "shut")} things {live}/{Spots.Count} rack {Rack.Count} music {(_music != null && _music.clip != null ? _music.clip.name : "-")} | {FunWorld.Describe()}";
    }
}
