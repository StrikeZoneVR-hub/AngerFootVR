using System.Collections.Generic;
using Framework;
using UnityEngine;

namespace AngerFootVR.Rendering;

/// <summary>
/// CARTOON HOLES (GORE: EXTRA): a shot punches a hole right through a goon, the bigger the gun the bigger the hole, in
/// the game's cartoon way: see-through, a shallow pink crater round it with a dark ring. A shotgun close up blows one
/// big hole (further off, a cluster of small ones); explosions take a bite out of the body.
/// The goons' meshes can't be read (the game ships them locked), so nothing is cut: an invisible plug lies along the
/// shot's way through the body (the mod's own shader: depth only), drawn after the level and the sky and before the
/// goon, so the goon's own drawing is held back there and what's behind shows through. Where the shot meets the
/// drawn surface (going in and coming out) is found on a snapshot of the goon as it stands (SkinnedMeshRenderer.BakeMesh,
/// which works on locked meshes), so the plug sits just outside the skin and the rims on it. The plug rides on the
/// bone it went through, so the hole goes with the body when it ragdolls. A goon with a hole is drawn after the sky
/// from then on (its materials are its own copies).
/// </summary>
internal static class Holes
{
    private const int PlugQueue = 2501, GoonQueue = 2502;
    private const int MaxPerGoon = 12, MaxInLevel = 160;

    private struct Shot
    {
        public GameObject Goon;
        public Collider Part;
        public Vector3 Point, Dir;
        public float Radius;
    }

    private static readonly List<Shot> Queued = new List<Shot>();
    private static readonly Dictionary<GameObject, int> Count = new Dictionary<GameObject, int>();
    private static readonly Queue<GameObject[]> Made = new Queue<GameObject[]>();
    private static readonly Dictionary<SkinnedMeshRenderer, (int frame, Vector3[] verts, int[] tris, bool scaled)> Baked = new Dictionary<SkinnedMeshRenderer, (int, Vector3[], int[], bool)>();
    private static Mesh _tube, _ring, _bite, _bake;
    private static Material _plug, _rim;
    public static int TotalHoles, Refused, OnSurface, OffSurface;
    public static string Last = "";

    public static bool Ready => Shaders.Get("AngerFootVR/HolePlug") != null;

    // ------------------------------------------------------------------ what made the hole

    /// <summary>How big a hole a gun makes (its kind's name; radius in metres): the bigger the gun the bigger.</summary>
    private static float RadiusFor(string gun)
    {
        if (string.IsNullOrEmpty(gun)) return 0.035f;
        var g = gun.ToLowerInvariant();
        if (g.Contains("sniper")) return 0.075f;
        if (g.Contains("shotgun")) return 0.03f; // (each pellet; close up they blow one big hole together)
        if (g.Contains("assault") || g.Contains("rifle")) return 0.045f;
        if (g.Contains("crossbow")) return 0.042f;
        if (g.Contains("minigun")) return 0.036f;
        if (g.Contains("uzi") || g.Contains("uzzi")) return 0.03f;
        return 0.035f; // pistols
    }

    /// <summary>A bullet into a goon (from the game's impact effect): queued, the frame's hits on one goon taken together.</summary>
    public static void Queue(GameObject goon, Collider part, Vector3 point, Vector3 dir)
    {
        if (goon == null || !Ready || dir.sqrMagnitude < 1e-6f) return;
        string gun = null;
        if (SingletonBehaviour<Player>.HasInstance)
        {
            var w = SingletonBehaviour<Player>.Instance.Weapon;
            if (w != null && w.HoldableObjectType != null) gun = w.HoldableObjectType.name;
        }

        Queued.Add(new Shot { Goon = goon, Part = part, Point = point, Dir = dir.normalized, Radius = RadiusFor(gun) });
    }

    /// <summary>Every frame (from the gore's): the frame's shots made into holes.</summary>
    public static void Update()
    {
        if (Queued.Count == 0) return;
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
            var list = kv.Value;
            // a shotgun's pellets close up: one big hole, where they went in together
            if (list.Count >= 4 && (list[0].Point - head).sqrMagnitude < 3f * 3f)
            {
                var at = Vector3.zero;
                var dir = Vector3.zero;
                foreach (var s in list)
                {
                    at += s.Point;
                    dir += s.Dir;
                }

                at /= list.Count;
                Make(kv.Key, list[0].Part, at, dir.normalized, 0.12f, false);
                continue;
            }

            for (var i = 0; i < list.Count && i < 5; i++) Make(kv.Key, list[i].Part, list[i].Point, list[i].Dir, list[i].Radius, false);
        }
    }

    /// <summary>An explosion's bite out of a goon (it died in one): a big round chunk on the side facing the blast.</summary>
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
        var centre = best.bounds.center;
        var dir = (centre - blast).sqrMagnitude > 1e-4f ? (centre - blast).normalized : Vector3.forward;
        Make(goon, best, centre - dir * 0.15f, dir, 0.15f, true);
    }

    /// <summary>Dev: a hole made now (not queued).</summary>
    public static bool Test(GameObject goon, Collider part, Vector3 point, Vector3 dir, float radius, bool bite = false) => Ready && Make(goon, part, point, dir.normalized, radius, bite);

    // ------------------------------------------------------------------ making one

    private static bool Make(GameObject goon, Collider part, Vector3 point, Vector3 dir, float radius, bool bite)
    {
        Count.TryGetValue(goon, out var n);
        if (n >= MaxPerGoon)
        {
            Refused++;
            return false;
        }

        var bone = part != null && IsPart(part) ? part.transform : Nearest(goon, point);
        if (bone == null) return false;
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

        Goon(goon);
        var parts = new List<GameObject>();
        if (bite)
        {
            parts.Add(Plug(bone, entry, Quaternion.LookRotation(dir), Vector3.one * radius, BiteMesh()));
            parts.Add(Rim(bone, entry, entryN, radius * 0.95f));
        }
        else
        {
            const float lead = 0.02f; // (just outside the skin: the body moves a little against the bone it rides on)
            var from = entry - dir * lead;
            var to = exit + dir * lead;
            var axis = to - from;
            if (axis.magnitude < 0.05f) axis = dir * 0.3f;
            parts.Add(Plug(bone, from, Quaternion.LookRotation(axis), new Vector3(radius * 0.92f, radius * 0.92f, axis.magnitude), Tube()));
            parts.Add(Rim(bone, entry, entryN, radius));
            if ((exit - entry).sqrMagnitude > 0.03f * 0.03f) parts.Add(Rim(bone, exit, exitN, radius * 1.15f));
        }

        Count[goon] = n + 1;
        TotalHoles++;
        Made.Enqueue(parts.ToArray());
        while (Made.Count > MaxInLevel)
            foreach (var go in Made.Dequeue())
                if (go != null)
                    Object.Destroy(go);
        Last = $"{(bite ? "bite" : "hole")} r {radius:F3} on '{bone.name}' in {entry:F2} out {exit:F2} ({(exit - entry).magnitude:F2} m through)";
        return true;
    }

    /// <summary>One of a goon's body parts (its ragdoll's colliders, on the enemy or the corpse layer): not the big
    /// capsule it walks with (EnemyNavigation) or its hitboxes.</summary>
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

    /// <summary>Along a line (from, the way dir, up to len): the first and last points on the goon's drawn body, and
    /// the body's facing there; on a snapshot of it as it stands now.</summary>
    private static bool Surface(GameObject goon, Vector3 from, Vector3 dir, float len, out float tIn, out Vector3 nIn, out float tOut, out Vector3 nOut)
    {
        tIn = float.MaxValue;
        tOut = float.MinValue;
        nIn = -dir;
        nOut = dir;
        var ray = new Ray(from, dir);
        foreach (var smr in goon.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
            var b = smr.bounds;
            b.Expand(0.05f);
            if (!b.IntersectRay(ray, out var enter) || enter > len) continue;
            var (verts, tris, scaled) = Snapshot(smr);
            if (verts == null) continue;
            // (the snapshot is in the renderer's own space: the line taken into it)
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
                var hit = Vector3.Dot(e2, q) * inv / k; // (back in metres along the world line)
                if (hit < 0f || hit > len) continue;
                var nw = toWorld.MultiplyVector(Vector3.Cross(e1, e2)).normalized;
                if (hit < tIn)
                {
                    tIn = hit;
                    nIn = Vector3.Dot(nw, dir) < 0f ? nw : -nw;
                }

                if (hit > tOut)
                {
                    tOut = hit;
                    nOut = Vector3.Dot(nw, dir) > 0f ? nw : -nw;
                }
            }
        }

        return tIn < float.MaxValue;
    }

    /// <summary>The renderer as it stands this frame (baked once a frame, and kept): its vertices in the renderer's own
    /// space, and whether they already carry its scale.</summary>
    private static (Vector3[] verts, int[] tris, bool scaled) Snapshot(SkinnedMeshRenderer smr)
    {
        if (Baked.TryGetValue(smr, out var b) && b.frame == Time.frameCount) return (b.verts, b.tris, b.scaled);
        if (_bake == null) _bake = new Mesh { name = "AngerFootVR hole bake" };
        try
        {
            smr.BakeMesh(_bake);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Holes: bake failed (" + e.Message + ")");
            return (null, null, true);
        }

        var verts = _bake.vertices;
        // (whether the snapshot already carries the renderer's scale: its size against the renderer's bounds)
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

    /// <summary>The goon's renderers drawn after the plugs (their own material copies, once).</summary>
    private static void Goon(GameObject goon)
    {
        if (Count.ContainsKey(goon)) return;
        foreach (var r in goon.GetComponentsInChildren<Renderer>())
        {
            if (r == null || r is ParticleSystemRenderer || r.gameObject.layer == UI.VrUi.Layer) continue;
            var mats = r.materials; // (copies)
            foreach (var m in mats)
                if (m != null && m.renderQueue <= 2500)
                    m.renderQueue = GoonQueue;
            r.materials = mats;
        }
    }

    private static GameObject Plug(Transform bone, Vector3 at, Quaternion rot, Vector3 scale, Mesh mesh)
    {
        if (_plug == null) _plug = new Material(Shaders.Get("AngerFootVR/HolePlug")) { name = "AngerFootVR hole plug", renderQueue = PlugQueue, enableInstancing = true };
        var go = new GameObject("AngerFootVR hole plug") { layer = UI.VrUi.Layer }; // (a layer the player's cameras draw)
        go.transform.SetPositionAndRotation(at, rot);
        go.transform.localScale = scale;
        go.transform.SetParent(bone, true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _plug;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    /// <summary>The cartoon rim at a hole's mouth, on the skin, facing out (normal).</summary>
    private static GameObject Rim(Transform bone, Vector3 at, Vector3 normal, float radius)
    {
        if (_ring == null) _ring = RingMesh();
        if (_rim == null)
        {
            var toon = Shaders.Get("AngerFootVR/Toon");
            _rim = new Material(toon != null ? toon : Shader.Find("Particles/Standard Surface")) { name = "AngerFootVR hole rim", renderQueue = GoonQueue + 1, enableInstancing = true };
            if (toon != null)
            {
                _rim.SetFloat("_VertexColour", 1f);
                _rim.SetFloat("_Outline", 0f);
            }
        }

        var go = new GameObject("AngerFootVR hole rim") { layer = UI.VrUi.Layer };
        go.transform.SetPositionAndRotation(at + normal * 0.003f, Quaternion.LookRotation(normal));
        go.transform.localScale = Vector3.one * radius;
        go.transform.SetParent(bone, true);
        go.AddComponent<MeshFilter>().sharedMesh = _ring;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _rim;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    /// <summary>A closed tube, radius 1, from z 0 to 1, facing out.</summary>
    private static Mesh Tube()
    {
        if (_tube != null) return _tube;
        const int n = 16;
        var v = new List<Vector3>();
        var t = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.PI * 2f / n;
            v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f));
        }

        v.Add(Vector3.zero);
        v.Add(Vector3.forward);
        int c0 = n * 2, c1 = n * 2 + 1;
        for (var i = 0; i < n; i++)
        {
            int a0 = i * 2, a1 = i * 2 + 1, b0 = (i + 1) % n * 2, b1 = (i + 1) % n * 2 + 1;
            t.AddRange(new[] { a0, b0, a1, b0, b1, a1 }); // the side, facing out
            t.AddRange(new[] { c0, b0, a0 }); // the near cap, facing back (-z)
            t.AddRange(new[] { c1, a1, b1 }); // the far cap, facing +z
        }

        _tube = new Mesh { name = "AngerFootVR hole tube" };
        _tube.SetVertices(v);
        _tube.SetTriangles(t, 0);
        _tube.RecalculateNormals();
        _tube.RecalculateBounds();
        return _tube;
    }

    /// <summary>A lumpy ball, radius about 1: an explosion's bite.</summary>
    private static Mesh BiteMesh()
    {
        if (_bite != null) return _bite;
        var src = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m = Object.Instantiate(src.GetComponent<MeshFilter>().sharedMesh);
        Object.Destroy(src);
        var v = m.vertices;
        var rnd = new System.Random(5);
        for (var i = 0; i < v.Length; i++) v[i] = v[i] * 2f * (0.85f + (float)rnd.NextDouble() * 0.25f); // (the primitive's radius is 0.5)
        m.vertices = v;
        m.RecalculateBounds();
        m.name = "AngerFootVR hole bite";
        _bite = m;
        return _bite;
    }

    /// <summary>A rim (unit inner radius, facing +z): a pink crater sunk a little in the middle, a dark ragged ring.</summary>
    private static Mesh RingMesh()
    {
        const int n = 20;
        var verts = new List<Vector3>();
        var cols = new List<Color32>();
        var tris = new List<int>();
        var pink = new Color32(235, 70, 120, 255);
        var deep = new Color32(150, 25, 60, 255);
        var dark = new Color32(35, 5, 15, 255);
        var rnd = new System.Random(7);
        var radii = new[] { 0.96f, 1.1f, 1.16f, 1.45f };
        var depth = new[] { -0.3f, -0.1f, 0f, 0f }; // (sunk in: the flesh pushed in round the hole)
        var colours = new[] { deep, pink, dark, dark };
        for (var i = 0; i < n; i++)
        {
            var a = i * Mathf.PI * 2f / n;
            var rag = 1f + (float)(rnd.NextDouble() - 0.5) * 0.22f;
            for (var k = 0; k < radii.Length; k++)
            {
                var rr = radii[k] * (k == 3 ? rag : 1f);
                verts.Add(new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, depth[k]));
                cols.Add(colours[k]);
            }
        }

        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            for (var k = 0; k < radii.Length - 1; k++)
            {
                int a0 = i * 4 + k, a1 = i * 4 + k + 1, b0 = j * 4 + k, b1 = j * 4 + k + 1;
                tris.AddRange(new[] { a0, a1, b0, b0, a1, b1 });
            }
        }

        var m = new Mesh { name = "AngerFootVR hole rim" };
        m.SetVertices(verts);
        m.SetColors(cols);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>A level's start: the last one's goons are gone (their plugs and rims went with them).</summary>
    public static void NewLevel()
    {
        Count.Clear();
        Made.Clear();
        Baked.Clear();
        Queued.Clear();
    }

    public static string Describe() => $"HOLES last {Last} | {TotalHoles} made on {Count.Count} goons, {OnSurface} on the drawn body / {OffSurface} on colliders, {Refused} refused ({MaxPerGoon} a goon), shaders {(Ready ? "ready" : "MISSING")}";
}
