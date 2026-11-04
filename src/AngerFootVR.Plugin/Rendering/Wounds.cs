using System.Collections.Generic;
using Framework;
using UnityEngine;

namespace AngerFootVR.Rendering;

/// <summary>
/// WOUNDS (GORE: EXTRA; 1.2's immersion update, replacing the cartoon holes): a shot leaves a real wound where it went
/// in and, if it went through, a bigger, more ragged one where it came out; drawn on the body itself, by two passes of the
/// mod's own shaders over the goon's own skin (its mesh and texture never touched), so a wound bends, stretches and
/// ragdolls exactly with the body: nothing floats or slides. Small wounds are dark pits with a torn rim, a graze and a
/// bruise round them; a big one (a sniper's, a shotgun blast close up, an explosion's bite, or several shots landing
/// together) is cut right through, and you see in: the cut skin, the yellow fat under it, wet muscle, a rib across a
/// chest wound, and out the far side to what's behind the body. Blood soaks out from a wound as it bleeds and runs down
/// from it the way gravity pulls (it stays where it ran; a body that falls over bleeds new runs the new way down), and
/// drips off. A kick leaves a bruise. The same on you (Wounds.OnPlayer): where you're hit, your VR body (and its mirror
/// twin) is wounded and bleeds the same way, until you heal.
/// A wound is a few numbers kept on the bone it went into (where, which way, how big), turned into the world each frame
/// for the shaders (WoundCommon.cginc); a body is drawn after the sky from its first wound (its materials are its own
/// copies), the hole pass just before it so its own drawing gives way where a hole is.
/// </summary>
internal static class Wounds
{
    public const int CarveQueue = 2501, GoonQueue = 2502, SkinQueue = 2503;
    private const int MaxW = 12, MaxD = 16, MaxBodies = 48;

    /// <summary>A wound this size (radius, metres) or bigger goes right through (you see through it).</summary>
    public const float ThroughSize = 0.019f;

    private sealed class Wound
    {
        public Transform Bone;
        public Vector3 Entry, Axis, Ref, EntryN, ExitN; // bone space
        public float RIn, ROut, Length, Pit, Born, Seed;
        public int Kind; // 0 shot, 1 bite, 2 bruise
        public bool Torso;
        public int Drips;
    }

    private sealed class Drip
    {
        public Transform Bone;
        public Vector3 Start, Down, Side, Out; // bone space
        public float Width, Len, MaxLen, Born, Seed, NextDrop;
        public Wound Of;
    }

    private sealed class Skin
    {
        public Renderer R;
        public Material Carve, Over;
    }

    private sealed class Body
    {
        public GameObject Root;
        public Enemy Enemy;
        public bool Player;
        public readonly List<Skin> Skins = new List<Skin>();
        public readonly List<Wound> W = new List<Wound>();
        public readonly List<Drip> D = new List<Drip>();
        public float NextCheck, LastHit;
        public bool Pooled;
    }

    private struct Shot
    {
        public GameObject Goon;
        public Collider Part;
        public Vector3 Point, Dir;
        public string Gun;
    }

    private static readonly Dictionary<GameObject, Body> Bodies = new Dictionary<GameObject, Body>();
    private static readonly List<Body> Order = new List<Body>();
    private static readonly List<Shot> Queued = new List<Shot>();
    private static readonly Vector4[] WA = new Vector4[MaxW], WB = new Vector4[MaxW], WC = new Vector4[MaxW], WD = new Vector4[MaxW];
    private static readonly Vector4[] DA = new Vector4[MaxD], DB = new Vector4[MaxD], DC = new Vector4[MaxD], DD = new Vector4[MaxD];
    private static readonly int IdWA = Shader.PropertyToID("_WA"), IdWB = Shader.PropertyToID("_WB"), IdWC = Shader.PropertyToID("_WC"), IdWD = Shader.PropertyToID("_WD"), IdWCount = Shader.PropertyToID("_WCount");
    private static readonly int IdDA = Shader.PropertyToID("_DA"), IdDB = Shader.PropertyToID("_DB"), IdDC = Shader.PropertyToID("_DC"), IdDD = Shader.PropertyToID("_DD"), IdDCount = Shader.PropertyToID("_DCount");
    private static readonly int IdBlood = Shader.PropertyToID("_BloodColor");
    private static readonly Dictionary<SkinnedMeshRenderer, (int frame, Vector3[] verts, int[] tris, bool scaled)> Baked = new Dictionary<SkinnedMeshRenderer, (int, Vector3[], int[], bool)>();
    private static Mesh _bake;
    public static int Made, Through, OnSurface, OffSurface, Merged;
    public static bool DevHits;
    public static string Last = "";

    public static bool Ready => Shaders.Get("AngerFootVR/WoundCarve") != null && Shaders.Get("AngerFootVR/WoundSkin") != null;

    // ------------------------------------------------------------------ how big

    /// <summary>A gun's wound (its kind's name): entry radius, exit radius (metres), and whether it goes through.</summary>
    private static void SizeFor(string gun, out float rIn, out float rOut, out bool exits)
    {
        exits = true;
        var g = string.IsNullOrEmpty(gun) ? "" : gun.ToLowerInvariant();
        if (g.Contains("sniper"))
        {
            rIn = 0.026f;
            rOut = 0.046f;
        }
        else if (g.Contains("shotgun"))
        {
            rIn = 0.0065f; // (a pellet; close up they blow one big hole together)
            rOut = 0.009f;
            exits = false;
        }
        else if (g.Contains("assault") || g.Contains("rifle"))
        {
            rIn = 0.0105f;
            rOut = 0.021f;
        }
        else if (g.Contains("crossbow"))
        {
            rIn = 0.012f;
            rOut = 0.016f;
        }
        else if (g.Contains("minigun"))
        {
            rIn = 0.0105f;
            rOut = 0.02f;
        }
        else if (g.Contains("uzi") || g.Contains("uzzi"))
        {
            rIn = 0.0085f;
            rOut = 0.015f;
        }
        else
        {
            rIn = 0.0095f; // pistols
            rOut = 0.0165f;
        }
    }

    // ------------------------------------------------------------------ what made them

    /// <summary>A bullet into a goon (from the game's impact effect): queued, the frame's hits on a goon taken together.</summary>
    public static void Queue(GameObject goon, Collider part, Vector3 point, Vector3 dir)
    {
        if (goon == null || !Ready || dir.sqrMagnitude < 1e-6f) return;
        string gun = null;
        if (SingletonBehaviour<Player>.HasInstance)
        {
            var w = SingletonBehaviour<Player>.Instance.Weapon;
            if (w != null && w.HoldableObjectType != null) gun = w.HoldableObjectType.name;
        }

        Queued.Add(new Shot { Goon = goon, Part = part, Point = point, Dir = dir.normalized, Gun = gun });
    }

    /// <summary>An explosion's bite out of a goon (it died in one): a ragged bowl on the side that faced the blast.</summary>
    public static void Bite(GameObject goon, Vector3 blast)
    {
        if (goon == null || !Ready) return;
        Collider best = null;
        var bestD = float.MaxValue;
        foreach (var c in goon.GetComponentsInChildren<Collider>())
        {
            if (!IsPart(c)) continue;
            var d = (c.bounds.center - blast).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = c;
            }
        }

        if (best == null) return;
        var b = BodyOf(goon, false);
        if (b == null) return;
        var centre = best.bounds.center;
        var dir = (centre - blast).sqrMagnitude > 1e-4f ? (centre - blast).normalized : Vector3.forward;
        AddBite(b, best.transform, centre - dir * 0.05f, dir, Random.Range(0.085f, 0.12f));
        Bleeding.Gibs(centre - dir * 0.08f, -dir, 5);
    }

    /// <summary>A kick: a bruise where it landed.</summary>
    public static void Bruise(GameObject goon, Collider part, Vector3 at)
    {
        if (goon == null || !Ready) return;
        var b = BodyOf(goon, false);
        if (b == null) return;
        var bone = part != null && IsPart(part) ? part.transform : Nearest(goon, at);
        if (bone == null) return;
        AddWound(b, new Wound { Bone = bone, Entry = bone.InverseTransformPoint(at), Axis = bone.InverseTransformDirection(Vector3.forward), Ref = bone.InverseTransformDirection(Vector3.up), RIn = Random.Range(0.05f, 0.075f), Kind = 2, Born = Time.time, Seed = Random.value * 10f });
    }

    /// <summary>Every frame (from the gore's): the frame's shots made into wounds; every wounded body's wounds and
    /// drips given to its shaders.</summary>
    public static void Update()
    {
        if (_warm > 0) Warm();
        if (Queued.Count > 0) TakeShots();
        var dt = Time.deltaTime;
        var now = Time.time;
        var blood = (Vector4)Gore.Colour;
        for (var i = Order.Count - 1; i >= 0; i--)
        {
            var b = Order[i];
            if (b.Root == null)
            {
                Order.RemoveAt(i);
                continue;
            }

            try
            {
                Bleed(b, now, dt);
                Feed(b, now, blood);
            }
            catch (System.Exception e)
            {
                Plugin.L.LogWarning("Wounds: " + e.Message);
                Order.RemoveAt(i);
                Bodies.Remove(b.Root);
            }
        }

        if (Bodies.Count > Order.Count + 8)
        {
            var dead = new List<GameObject>();
            foreach (var kv in Bodies)
                if (kv.Key == null)
                    dead.Add(kv.Key);
            foreach (var k in dead) Bodies.Remove(k);
        }
    }

    private static void TakeShots()
    {
        var shots = new List<Shot>(Queued);
        Queued.Clear();
        var byGoon = new Dictionary<GameObject, List<Shot>>();
        foreach (var s in shots)
        {
            if (s.Goon == null) continue;
            if (!byGoon.TryGetValue(s.Goon, out var list)) byGoon[s.Goon] = list = new List<Shot>();
            list.Add(s);
        }

        var head = Core.VrRig.HeadPosition;
        foreach (var kv in byGoon)
        {
            var b = BodyOf(kv.Key, false);
            if (b == null) continue;
            var list = kv.Value;
            // a shotgun's pellets close up: one big blast right through, where they went in together, pellet pits round it
            if (list.Count >= 4 && (list[0].Point - head).sqrMagnitude < 3.2f * 3.2f)
            {
                var at = Vector3.zero;
                var dir = Vector3.zero;
                foreach (var s in list)
                {
                    at += s.Point;
                    dir += s.Dir;
                }

                at /= list.Count;
                var near = Mathf.InverseLerp(3.2f, 0.8f, (at - head).magnitude);
                AddShot(b, list[0].Part, at, dir.normalized, Mathf.Lerp(0.03f, 0.055f, near), Mathf.Lerp(0.05f, 0.085f, near), true);
                for (var i = 0; i < list.Count && i < 4; i++)
                    if ((list[i].Point - at).magnitude > 0.05f)
                        AddShot(b, list[i].Part, list[i].Point, list[i].Dir, 0.0065f, 0.009f, false);
                Bleeding.Gibs(at + dir.normalized * 0.25f, dir.normalized, 3);
                continue;
            }

            for (var i = 0; i < list.Count && i < 5; i++)
            {
                SizeFor(list[i].Gun, out var rIn, out var rOut, out var exits);
                AddShot(b, list[i].Part, list[i].Point, list[i].Dir, rIn, rOut, exits);
            }
        }
    }

    // ------------------------------------------------------------------ making them

    private static Body BodyOf(GameObject root, bool player)
    {
        if (root == null) return null;
        if (Bodies.TryGetValue(root, out var b)) return b;
        if (Order.Count >= MaxBodies)
        {
            // (the oldest wounded body gives its place: its wounds go)
            var old = Order[0];
            Order.RemoveAt(0);
            Bodies.Remove(old.Root);
            Strip(old);
        }

        b = new Body { Root = root, Player = player, Enemy = root.GetComponent<Enemy>() };
        var carveShader = Shaders.Get("AngerFootVR/WoundCarve");
        var skinShader = Shaders.Get("AngerFootVR/WoundSkin");
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r.GetComponentInParent<BFX_BloodSettings>() != null) continue;
            if (!player && r.gameObject.layer == UI.VrUi.Layer) continue; // (blood stuck on it)
            if (r.name.StartsWith("AngerFootVR")) continue;
            var mats = r.materials; // (its own copies)
            foreach (var m in mats)
                if (m != null && m.renderQueue <= 2500)
                    m.renderQueue = GoonQueue;
            if (r is SkinnedMeshRenderer smr && IsSkin(smr))
            {
                var carve = new Material(carveShader) { name = "AngerFootVR wound carve", renderQueue = CarveQueue };
                var over = new Material(skinShader) { name = "AngerFootVR wound skin", renderQueue = SkinQueue };
                foreach (var m in new[] { carve, over })
                {
                    m.SetTexture("_NoiseTex", Shaders.Tex("gore_noise"));
                    m.SetTexture("_SkinNoise", Shaders.Tex("gore_skin"));
                    // (each array at its full size from the start: Unity fixes an array's size at its first setting)
                    m.SetVectorArray(IdWA, WA);
                    m.SetVectorArray(IdWB, WB);
                    m.SetVectorArray(IdWC, WC);
                    m.SetVectorArray(IdWD, WD);
                    m.SetVectorArray(IdDA, DA);
                    m.SetVectorArray(IdDB, DB);
                    m.SetVectorArray(IdDC, DC);
                    m.SetVectorArray(IdDD, DD);
                    m.SetFloat(IdWCount, 0f);
                    m.SetFloat(IdDCount, 0f);
                }

                carve.SetTexture("_FleshTex", Shaders.Tex("gore_flesh"));
                carve.SetTexture("_BoneTex", Shaders.Tex("gore_bone"));
                carve.SetFloat("_ThroughSize", ThroughSize);
                var all = new Material[mats.Length + 2];
                mats.CopyTo(all, 0);
                all[mats.Length] = carve;
                all[mats.Length + 1] = over;
                mats = all;
                b.Skins.Add(new Skin { R = r, Carve = carve, Over = over });
            }

            r.materials = mats;
        }

        Bodies[root] = b;
        Order.Add(b);
        return b;
    }

    /// <summary>A body's own skins (not its eyes, teeth, tongue).</summary>
    private static bool IsSkin(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null || mesh.vertexCount < 700) return false;
        var n = smr.name.ToLowerInvariant();
        return !(n.Contains("eye") || n.Contains("teeth") || n.Contains("tongue") || n.Contains("lash"));
    }

    private static void Strip(Body b)
    {
        foreach (var s in b.Skins)
        {
            if (s.R == null) continue;
            var mats = new List<Material>(s.R.sharedMaterials);
            mats.Remove(s.Carve);
            mats.Remove(s.Over);
            s.R.sharedMaterials = mats.ToArray();
            Object.Destroy(s.Carve);
            Object.Destroy(s.Over);
        }
    }

    private static void AddShot(Body b, Collider part, Vector3 point, Vector3 dir, float rIn, float rOut, bool exits)
    {
        var goon = b.Root;
        var bone = part != null && IsPart(part) ? part.transform : Nearest(goon, point);
        if (bone == null) return;
        // where the shot meets the drawn body going in and coming out (else the colliders, near enough)
        Vector3 entry, entryN, exit, exitN;
        var start = point - dir * 0.6f;
        if (Surface(goon, start, dir, 1.8f, out var t0, out entryN, out var t1, out exitN))
        {
            entry = start + dir * t0;
            exit = start + dir * t1;
            OnSurface++;
        }
        else
        {
            entry = point;
            entryN = -dir;
            exit = ColliderExit(goon, point, dir);
            exitN = dir;
            OffSurface++;
        }

        AddAt(b, bone, entry, entryN, exit, exitN, dir, rIn, rOut, exits);
    }

    /// <summary>A shot's wound where it met the body (entry/exit world points and the skin's facing there).</summary>
    private static void AddAt(Body b, Transform bone, Vector3 entry, Vector3 entryN, Vector3 exit, Vector3 exitN, Vector3 dir, float rIn, float rOut, bool exits, float seed = -1f)
    {
        var through = (exit - entry).magnitude;
        if (through < 0.02f) exits = false;
        // shots landing together (focused fire): the wound already there grows, until it goes right through
        foreach (var w in b.W)
        {
            if (w.Kind != 0 || w.Bone == null) continue;
            var e = w.Bone.TransformPoint(w.Entry);
            if ((e - entry).magnitude < Mathf.Max(w.RIn, rIn) * 1.7f && Vector3.Angle(w.Bone.TransformDirection(w.Axis), dir) < 50f)
            {
                w.RIn = Mathf.Min(w.RIn * 1.22f + rIn * 0.25f, 0.045f);
                w.ROut = Mathf.Min(Mathf.Max(w.ROut * 1.2f + rOut * 0.2f, w.RIn * 1.5f), 0.075f);
                if (w.Length <= 0f && exits) w.Length = through;
                w.Born = Mathf.Min(w.Born, Time.time);
                Merged++;
                if (w.RIn >= ThroughSize && w.Length > 0f) Through++;
                b.LastHit = Time.time;
                Last = $"wound grew to r {w.RIn:F3} on '{w.Bone.name}'";
                return;
            }
        }

        var n = bone.name.ToLowerInvariant();
        var wound = new Wound
        {
            Bone = bone, Entry = bone.InverseTransformPoint(entry), Axis = bone.InverseTransformDirection(dir), EntryN = bone.InverseTransformDirection(entryN), ExitN = bone.InverseTransformDirection(exitN),
            RIn = rIn, ROut = exits ? rOut : rIn, Length = exits ? through : 0f, Pit = exits ? 0f : Mathf.Min(0.035f, Mathf.Max(0.012f, through * 0.4f)),
            Kind = 0, Torso = n.Contains("chest") || n.Contains("spine"), Born = Time.time, Seed = seed >= 0f ? seed : Random.value * 10f,
            Ref = bone.InverseTransformDirection(Up(b)),
        };
        AddWound(b, wound);
        if (exits && rIn >= ThroughSize) Through++;
        // where it came out: blood on what's behind; an artery (the neck, a thigh) pumps
        if (exits && !b.Player) Gore.WallBehind(exit, dir, Mathf.Clamp(rOut * 30f, 0.5f, 1.4f));
        if (!b.Player && (n.Contains("neck") || n.Contains("hip")) && SingletonBehaviour<BloodCamera>.HasInstance)
        {
            var c = bone.GetComponent<Collider>();
            if (c != null) SingletonBehaviour<BloodCamera>.Instance.DoExtraSpurts(c, 5, 0.38f);
        }

        if (exits && rIn >= ThroughSize && !b.Player) Bleeding.Gibs(exit + dir * 0.03f, dir, rIn > 0.03f ? 3 : 2);
        Last = $"{(exits ? "through" : "pit")} r {rIn:F3}/{rOut:F3} on '{bone.name}' in {entry:F2} out {exit:F2} ({through:F2} m)";
    }

    private static void AddBite(Body b, Transform bone, Vector3 centre, Vector3 dir, float radius)
    {
        var n = bone.name.ToLowerInvariant();
        AddWound(b, new Wound
        {
            Bone = bone, Entry = bone.InverseTransformPoint(centre), Axis = bone.InverseTransformDirection(dir), EntryN = bone.InverseTransformDirection(-dir), ExitN = bone.InverseTransformDirection(dir),
            RIn = radius, ROut = radius, Kind = 1, Torso = n.Contains("chest") || n.Contains("spine"), Born = Time.time, Seed = Random.value * 10f, Ref = bone.InverseTransformDirection(Up(b)),
        });
        Last = $"bite r {radius:F3} on '{bone.name}'";
    }

    private static Vector3 Up(Body b) => b.Root != null ? b.Root.transform.up : Vector3.up;

    private static void AddWound(Body b, Wound w)
    {
        if (b.W.Count >= MaxW)
        {
            // (full: the oldest bruise goes first, else the oldest wound)
            var i = b.W.FindIndex(x => x.Kind == 2);
            var gone = b.W[i >= 0 ? i : 0];
            b.W.Remove(gone);
            b.D.RemoveAll(d => d.Of == gone);
        }

        b.W.Add(w);
        b.LastHit = Time.time;
        Made++;
        if (w.Kind != 2) StartDrips(b, b.W.Count - 1, w, Time.time);
    }

    // ------------------------------------------------------------------ bleeding: drips that run down and stay

    private static void StartDrips(Body b, int index, Wound w, float now)
    {
        var rnd = new System.Random((int)(w.Seed * 1000f) + b.D.Count);
        var mouths = w.Kind == 0 && w.Length > 0f ? 2 : 1;
        for (var m = 0; m < mouths; m++)
        {
            var count = w.RIn >= ThroughSize || w.Kind == 1 ? 2 : 1;
            for (var k = 0; k < count; k++) NewDrip(b, index, w, m == 1, now + (float)rnd.NextDouble() * 0.8f + k * 0.5f, rnd, k * ((float)rnd.NextDouble() - 0.5f) * 40f);
        }
    }

    /// <summary>A run of blood from a wound's mouth, the way gravity pulls now (fixed to the body from here on).</summary>
    private static void NewDrip(Body b, int index, Wound w, bool exit, float born, System.Random rnd, float spread)
    {
        if (b.D.Count >= MaxD || w.Bone == null) return;
        var bone = w.Bone;
        var outN = (exit ? w.ExitN : w.EntryN).normalized;
        var down = bone.InverseTransformDirection(Vector3.down);
        var along = Vector3.ProjectOnPlane(down, outN);
        if (along.magnitude < 0.25f) return; // (a mouth facing up or down: it pools, or drips straight off)
        along = Quaternion.AngleAxis(spread, outN) * along.normalized;
        var r = exit ? w.ROut : w.RIn;
        var mouth = exit ? w.Entry + w.Axis.normalized * w.Length : w.Entry;
        if (w.Kind == 1) mouth = w.Entry + w.EntryN.normalized * w.RIn * 0.85f;
        var big = Mathf.InverseLerp(0.008f, 0.05f, r);
        b.D.Add(new Drip
        {
            Bone = bone, Start = mouth + along * (r * 0.55f), Down = along, Side = Vector3.Cross(along, outN).normalized, Out = outN,
            Width = Mathf.Lerp(0.0028f, 0.0075f, big) * (0.8f + (float)rnd.NextDouble() * 0.4f), MaxLen = Mathf.Lerp(0.07f, 0.26f, big) * (0.6f + (float)rnd.NextDouble() * 0.8f),
            Born = born, Seed = (float)rnd.NextDouble() * 10f, Of = w, NextDrop = born + 1.5f,
        });
        w.Drips++;
    }

    private static void Bleed(Body b, float now, float dt)
    {
        var hurt = !b.Player || PlayerHurt;
        // the drips run (fast at first, slowing; a hurt player's only while hurt)
        foreach (var d in b.D)
        {
            var age = now - d.Born;
            if (age < 0f || d.Len >= d.MaxLen || !hurt) continue;
            d.Len = Mathf.Min(d.MaxLen, d.Len + (0.11f * Mathf.Exp(-age / 3.5f) + 0.003f) * dt);
        }

        // a body turned over (fallen, ragdolled): its wounds bleed new runs the new way down; drops fall off them
        if (now < b.NextCheck) return;
        b.NextCheck = now + 0.6f;
        for (var i = 0; i < b.W.Count; i++)
        {
            var w = b.W[i];
            if (w.Kind == 2 || w.Bone == null || now - w.Born > 25f || !hurt) continue;
            var down = w.Bone.InverseTransformDirection(Vector3.down);
            var newest = b.D.FindLast(x => x.Of == w);
            if (newest != null && Vector3.Angle(Vector3.ProjectOnPlane(down, newest.Out), newest.Down) > 45f && w.Drips < 4)
                NewDrip(b, i, w, false, now, new System.Random(i + b.D.Count * 7), 0f);
        }

        // a body that's done moving: a pool spreads on the floor under its worst wounds
        if (!b.Player && !b.Pooled && b.Enemy != null && b.Enemy.Health != null && b.Enemy.Health.IsDead && Time.time - b.Enemy.Health.TimeOfDeath > 1.6f)
        {
            b.Pooled = true;
            var worst = new List<Wound>(b.W.FindAll(x => x.Kind != 2 && x.Bone != null));
            worst.Sort((x, y) => y.RIn.CompareTo(x.RIn));
            for (var i = 0; i < worst.Count && i < 2; i++)
                Bleeding.PoolUnder(worst[i].Bone.TransformPoint(worst[i].Entry), Mathf.Lerp(3.5f, 6f, Mathf.InverseLerp(0.008f, 0.05f, worst[i].RIn)));
        }

        if (!hurt) return;
        foreach (var d in b.D)
        {
            if (d.Bone == null || now < d.NextDrop || now - d.Born > 18f || d.Len < d.MaxLen * 0.7f) continue;
            d.NextDrop = now + Random.Range(0.5f, 1.4f) + (now - d.Born) * 0.08f;
            Bleeding.Drop(d.Bone.TransformPoint(d.Start + d.Down * d.Len), d.Width);
        }
    }

    // ------------------------------------------------------------------ to the shaders

    private static void Feed(Body b, float now, Vector4 blood)
    {
        var visible = false;
        foreach (var s in b.Skins)
            if (s.R != null && s.R.isVisible)
                visible = true;
        if (!visible) return;
        var nw = 0;
        foreach (var w in b.W)
        {
            if (w.Bone == null || nw >= MaxW) continue;
            var e = w.Bone.TransformPoint(w.Entry);
            var u = w.Bone.TransformDirection(w.Axis).normalized;
            var refv = w.Bone.TransformDirection(w.Ref);
            WA[nw] = new Vector4(e.x, e.y, e.z, w.RIn);
            WB[nw] = new Vector4(u.x, u.y, u.z, w.Length);
            WC[nw] = new Vector4(w.ROut, w.Pit, w.Kind + (w.Torso ? 4f : 0f), now - w.Born);
            WD[nw] = new Vector4(refv.x, refv.y, refv.z, w.Seed);
            nw++;
        }

        var nd = 0;
        foreach (var d in b.D)
        {
            if (d.Bone == null || nd >= MaxD || now < d.Born || d.Len <= 0.0005f) continue;
            var s = d.Bone.TransformPoint(d.Start);
            var down = d.Bone.TransformDirection(d.Down).normalized;
            var side = d.Bone.TransformDirection(d.Side).normalized;
            var o = d.Bone.TransformDirection(d.Out).normalized;
            DA[nd] = new Vector4(s.x, s.y, s.z, d.Width);
            DB[nd] = new Vector4(down.x, down.y, down.z, d.Len);
            DC[nd] = new Vector4(side.x, side.y, side.z, d.Seed);
            DD[nd] = new Vector4(o.x, o.y, o.z, now - d.Born);
            nd++;
        }

        foreach (var s in b.Skins)
        {
            if (s.R == null) continue;
            foreach (var m in new[] { s.Carve, s.Over })
            {
                m.SetVectorArray(IdWA, WA);
                m.SetVectorArray(IdWB, WB);
                m.SetVectorArray(IdWC, WC);
                m.SetVectorArray(IdWD, WD);
                m.SetFloat(IdWCount, nw);
                m.SetVectorArray(IdDA, DA);
                m.SetVectorArray(IdDB, DB);
                m.SetVectorArray(IdDC, DC);
                m.SetVectorArray(IdDD, DD);
                m.SetFloat(IdDCount, nd);
                m.SetColor(IdBlood, blood);
            }
        }
    }

    // ------------------------------------------------------------------ you

    private static Body _you, _twin;
    private static float _lastYou;

    /// <summary>Whether you're hurt now (your blood runs only while you are: it stops as you heal).</summary>
    private static bool PlayerHurt => SingletonBehaviour<Player>.HasInstance && SingletonBehaviour<Player>.Instance.Health != null && SingletonBehaviour<Player>.Instance.Health.Proportion < 0.995f;

    /// <summary>You were hit (the game's own damage: where on you, which way): the wound goes where it hit your VR body
    /// (a hand held up in the way takes it), on your body and its mirror twin alike.</summary>
    public static void OnPlayer(Damage damage, Vector3 position, Vector3 direction)
    {
        if (!Ready || damage.Type == null || !global::AngerFootVR.Body.VrBody.Active) return;
        var type = damage.Type;
        int kind;
        float rIn, rOut;
        if (type == DamageType.Bullet || type == DamageType.MountedGunBullet || type == DamageType.StunningBullet)
        {
            kind = 0;
            rIn = 0.0085f;
            rOut = 0.015f;
        }
        else if (type == DamageType.CrossbowBolt)
        {
            kind = 0;
            rIn = 0.012f;
            rOut = 0.016f;
        }
        else if (type == DamageType.Explosion || type == DamageType.FireImpact)
        {
            kind = 1;
            rIn = rOut = 0.04f;
        }
        else if (type == DamageType.MeleeWeapon || type == DamageType.EnemyKick || type == DamageType.HeavyCollision || type == DamageType.Slam || type == DamageType.Hook)
        {
            kind = 2;
            rIn = rOut = 0.06f;
        }
        else return;

        var root = global::AngerFootVR.Body.VrBody.Root;
        var twinRoot = global::AngerFootVR.Body.VrBody.Roots[1];
        if (root == null) return;
        if (direction.sqrMagnitude < 1e-6f) direction = Core.VrRig.HeadRotation * Vector3.forward * -1f;
        direction = direction.normalized;
        // (the shot's line through you: where it meets your drawn body first; the twin has the head yours hides)
        var probe = twinRoot != null ? twinRoot.gameObject : root.gameObject;
        var from = position - direction * 1.2f;
        Transform hitBone;
        Vector3 entry, entryN, exit, exitN;
        if (Surface(probe, from, direction, 2.4f, out var t0, out entryN, out var t1, out exitN))
        {
            entry = from + direction * t0;
            exit = from + direction * t1;
        }
        else
        {
            // (it missed what's drawn: the chest, facing the shot)
            var chest = NearestBone(probe.transform, position);
            if (chest == null) return;
            entry = chest.position - direction * 0.1f;
            entryN = -direction;
            exit = chest.position + direction * 0.1f;
            exitN = direction;
        }

        hitBone = NearestBone(probe.transform, entry);
        if (hitBone == null) return;
        var seed = Random.value * 10f;
        foreach (var (r, isTwin) in new[] { (root, false), (twinRoot, true) })
        {
            if (r == null) continue;
            var b = BodyOf(r.gameObject, true);
            if (b == null) continue;
            var bone = MapBone(hitBone, r);
            if (bone == null) continue;
            // (the same point on this one: the twin is posed as the body is)
            var m = Matrix(hitBone, bone);
            var e = m.MultiplyPoint3x4(entry);
            var x = m.MultiplyPoint3x4(exit);
            var dirB = m.MultiplyVector(direction).normalized;
            if (kind == 0) AddAt(b, bone, e, m.MultiplyVector(entryN), x, m.MultiplyVector(exitN), dirB, rIn, rOut, (x - e).magnitude < 0.25f, seed);
            else if (kind == 1) AddBite(b, bone, e + dirB * 0.02f, dirB, rIn);
            else AddWound(b, new Wound { Bone = bone, Entry = bone.InverseTransformPoint(e), Axis = bone.InverseTransformDirection(dirB), Ref = bone.InverseTransformDirection(Vector3.up), RIn = rIn, Kind = 2, Born = Time.time, Seed = seed });
            if (isTwin) _twin = b;
            else _you = b;
        }

        _lastYou = Time.time;
        // the hit side of you buzzes
        var hand = Vector3.Dot(direction, Core.VrRig.HeadRotation * Vector3.right) > 0f ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
        Core.VrControls.Rumble(hand, 0.8f, 0.12f);
        var lh = global::AngerFootVR.Body.HeroBody.HandBone(0);
        var rh = global::AngerFootVR.Body.HeroBody.HandBone(1);
        Plugin.L.LogInfo($"Wounds: you were hit ({type.name}) on '{hitBone.name}' at {entry:F2} (from {position:F2}; hands L {(lh != null ? (lh.position - entry).magnitude : -1f):F2} R {(rh != null ? (rh.position - entry).magnitude : -1f):F2} m away; probe {probe.name}) | {Last}");
    }

    /// <summary>The matrix that takes a point on one bone to the same point on its twin (posed the same).</summary>
    private static Matrix4x4 Matrix(Transform from, Transform to) => from == to ? Matrix4x4.identity : to.localToWorldMatrix * from.worldToLocalMatrix;

    private static Transform MapBone(Transform bone, Transform root)
    {
        if (bone.IsChildOf(root)) return bone;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == bone.name)
                return t;
        return null;
    }

    private static Transform NearestBone(Transform root, Vector3 point)
    {
        Transform best = null;
        var bestD = float.MaxValue;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.bones == null) continue;
            foreach (var bone in smr.bones)
            {
                if (bone == null) continue;
                var n = bone.name.ToLowerInvariant();
                if (n.Contains("eye") || n.Contains("jaw") || n.Contains("teeth") || n.Contains("tongue") || n.Contains("part")) continue;
                var d = (bone.position - point).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = bone;
                }
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ where on the body (the drawn body's surface)

    /// <summary>One of a goon's body parts (its ragdoll's colliders, on the enemy or the corpse layer).</summary>
    public static bool IsPart(Collider c) => c != null && !c.isTrigger && (c.gameObject.layer == 11 || c.gameObject.layer == 31);

    private static Transform Nearest(GameObject goon, Vector3 point)
    {
        Transform best = null;
        var bestD = float.MaxValue;
        foreach (var c in goon.GetComponentsInChildren<Collider>())
        {
            if (!IsPart(c)) continue;
            var d = (c.ClosestPointOnBounds(point) - point).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = c.transform;
            }
        }

        return best;
    }

    private static Vector3 ColliderExit(GameObject goon, Vector3 point, Vector3 dir)
    {
        var exit = point + dir * 0.3f;
        var best = -1f;
        foreach (var h in Physics.RaycastAll(new Ray(point + dir * 1.2f, -dir), 1.2f, ~0, QueryTriggerInteraction.Ignore))
            if (IsPart(h.collider) && h.collider.transform.IsChildOf(goon.transform) && h.distance > best)
            {
                best = h.distance;
                exit = h.point;
            }

        return exit;
    }

    private static readonly List<(float t, Vector3 n)> Hits = new List<(float, Vector3)>();

    /// <summary>Along a line: where it goes into the body as drawn, and where it next comes out (a snapshot of the body
    /// as it stands now; not the farthest hit on the line, which may be another limb or the body behind).</summary>
    private static bool Surface(GameObject goon, Vector3 from, Vector3 dir, float len, out float tIn, out Vector3 nIn, out float tOut, out Vector3 nOut)
    {
        Hits.Clear();
        tIn = float.MaxValue;
        tOut = float.MinValue;
        nIn = -dir;
        nOut = dir;
        var ray = new Ray(from, dir);
        foreach (var smr in goon.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy || !IsSkin(smr)) continue;
            var bb = smr.bounds;
            bb.Expand(0.05f);
            if (!bb.IntersectRay(ray, out var enter) || enter > len) continue;
            var (verts, tris, scaled) = Snapshot(smr);
            if (verts == null) continue;
            var t = smr.transform;
            var toWorld = Matrix4x4.TRS(t.position, t.rotation, scaled ? Vector3.one : t.lossyScale);
            var toLocal = toWorld.inverse;
            var o = toLocal.MultiplyPoint3x4(from);
            var dl = toLocal.MultiplyVector(dir);
            var k = dl.magnitude;
            dl /= k;
            for (var i = 0; i + 2 < tris.Length; i += 3)
            {
                var a = verts[tris[i]];
                var e1 = verts[tris[i + 1]] - a;
                var e2 = verts[tris[i + 2]] - a;
                var p = Vector3.Cross(dl, e2);
                var det = Vector3.Dot(e1, p);
                if (det > -1e-10f && det < 1e-10f) continue;
                var inv = 1f / det;
                var s = o - a;
                var u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                var q = Vector3.Cross(s, e1);
                var v = Vector3.Dot(dl, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                var hit = Vector3.Dot(e2, q) * inv / k;
                if (hit < 0f || hit > len) continue;
                var nw = toWorld.MultiplyVector(Vector3.Cross(e1, e2)).normalized;
                Hits.Add((hit, nw));
            }
        }

        if (Hits.Count == 0) return false;
        Hits.Sort((x, y) => x.t.CompareTo(y.t));
        if (DevHits)
        {
            var sb = new System.Text.StringBuilder("WOUND HITS");
            foreach (var h in Hits) sb.Append($" {h.t:F3}{(Vector3.Dot(h.n, dir) < 0f ? "-" : "+")}");
            Plugin.L.LogInfo(sb.ToString());
        }
        tIn = Hits[0].t;
        nIn = Vector3.Dot(Hits[0].n, dir) < 0f ? Hits[0].n : -Hits[0].n;
        // (the first crossing out after it: a face turned the other way to the one it went in by, a centimetre on at
        // least; layers it goes into on the way, clothes over skin, are passed)
        var s0 = Vector3.Dot(Hits[0].n, dir);
        for (var i = 1; i < Hits.Count; i++)
            if (Hits[i].t > tIn + 0.012f && Vector3.Dot(Hits[i].n, dir) * s0 < 0f)
            {
                tOut = Hits[i].t;
                nOut = Vector3.Dot(Hits[i].n, dir) > 0f ? Hits[i].n : -Hits[i].n;
                break;
            }

        if (tOut < tIn)
        {
            tOut = tIn + 0.15f;
            nOut = dir;
        }

        return true;
    }

    private static (Vector3[] verts, int[] tris, bool scaled) Snapshot(SkinnedMeshRenderer smr)
    {
        if (Baked.TryGetValue(smr, out var b) && b.frame == Time.frameCount) return (b.verts, b.tris, b.scaled);
        if (_bake == null) _bake = new Mesh { name = "AngerFootVR wound bake" };
        try
        {
            smr.BakeMesh(_bake);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Wounds: bake failed (" + e.Message + ")");
            return (null, null, true);
        }

        var verts = _bake.vertices;
        var scaled = true;
        var s = smr.transform.lossyScale.x;
        if (Mathf.Abs(s - 1f) > 0.01f)
        {
            var size = _bake.bounds.size.magnitude;
            var world = smr.bounds.size.magnitude;
            scaled = Mathf.Abs(size - world) < Mathf.Abs(size * s - world);
        }

        var tris = b.tris != null && b.tris.Length > 0 ? b.tris : _bake.triangles;
        Baked[smr] = (Time.frameCount, verts, tris, scaled);
        return (verts, tris, scaled);
    }

    // ------------------------------------------------------------------ dev and housekeeping

    /// <summary>Dev: a wound made now on a goon (or you), the way a gun of that kind would.</summary>
    public static bool Test(GameObject goon, Collider part, Vector3 point, Vector3 dir, string kind)
    {
        if (!Ready || goon == null) return false;
        var b = BodyOf(goon, false);
        if (b == null) return false;
        switch (kind)
        {
            case "bite":
                AddBite(b, part != null ? part.transform : Nearest(goon, point), point + dir * 0.05f, dir, 0.1f);
                return true;
            case "bruise":
                Bruise(goon, part, point);
                return true;
            case "blast":
                AddShot(b, part, point, dir, 0.045f, 0.075f, true);
                return true;
            default:
                SizeFor(kind, out var rIn, out var rOut, out var exits);
                AddShot(b, part, point, dir, rIn, rOut, exits);
                return true;
        }
    }

    private static int _warm;
    private static Mesh _warmMesh;
    private static Material _warmCarve, _warmOver, _warmGib;

    /// <summary>The wound shaders drawn once (nothing shows: no wounds) the first frames of a level, so the first shot
    /// doesn't wait on them.</summary>
    private static void Warm()
    {
        _warm--;
        if (!Ready || !SingletonBehaviour<PlayerCamera>.HasInstance) return;
        var cam = SingletonBehaviour<PlayerCamera>.Instance.MainCamera;
        if (cam == null) return;
        if (_warmMesh == null)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _warmMesh = q.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(q);
            _warmCarve = new Material(Shaders.Get("AngerFootVR/WoundCarve")) { renderQueue = CarveQueue };
            _warmOver = new Material(Shaders.Get("AngerFootVR/WoundSkin")) { renderQueue = SkinQueue };
            var gib = Shaders.Get("AngerFootVR/Gib");
            if (gib != null) _warmGib = new Material(gib);
            foreach (var m in new[] { _warmCarve, _warmOver })
            {
                m.SetFloat(IdWCount, 0f);
                m.SetFloat(IdDCount, 0f);
            }
        }

        var t = cam.transform;
        var at = Matrix4x4.TRS(t.position + t.forward * 3f, t.rotation, Vector3.one * 0.001f);
        Graphics.DrawMesh(_warmMesh, at, _warmCarve, UI.VrUi.Layer, cam);
        Graphics.DrawMesh(_warmMesh, at, _warmOver, UI.VrUi.Layer, cam);
        if (_warmGib != null) Graphics.DrawMesh(_warmMesh, at, _warmGib, UI.VrUi.Layer, cam);
    }

    /// <summary>A level's start: the last one's bodies are gone.</summary>
    public static void NewLevel()
    {
        _warm = 4;
        foreach (var b in Order)
            if (b.Player)
                Strip(b);
        Bodies.Clear();
        Order.Clear();
        Baked.Clear();
        Queued.Clear();
        _you = _twin = null;
        Bleeding.NewLevel();
    }

    public static string Describe()
    {
        var w = 0;
        var d = 0;
        foreach (var b in Order)
        {
            w += b.W.Count;
            d += b.D.Count;
        }

        return $"WOUNDS {(Ready ? "ready" : "SHADERS MISSING")} | last {Last} | {Made} made ({Through} through, {Merged} grown), {OnSurface} on the drawn body / {OffSurface} on colliders | {Order.Count} bodies, {w} wounds, {d} drips now | you {(_you != null ? _you.W.Count + " wounds" : "unhurt")} | {Bleeding.Describe()}";
    }
}
