using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// Fingers that really hold what's in your hand (the way Boneworks and Bonelab do it, on the game's own grips): each
/// finger takes the pose the game's first-person hand has round that gun (its animators made one for every gun, and it
/// moves with recoil and reloads), turned onto your hand's own bones; then it closes until it meets the gun's real
/// surface, and no further: no finger sinks into the grip (what looked see-through) and none hangs off it (what looked
/// floating). The gun's surface is the gun's own mesh as the game draws it, made into a small distance field round each
/// of its parts when it comes into your hand (once a session for each kind of gun, on a worker thread). The index finger
/// lies along the frame until you touch the trigger, then sits on it and squeezes as you pull. A hand with no pose of the
/// game's to copy (your other hand on a pistol) closes from open until it meets the gun and the gun hand's fingers.
/// </summary>
internal static class GripFingers
{
    /// <summary>One hand's grip for this frame.</summary>
    public sealed class Grip
    {
        public int SetFrame = -10;

        /// <summary>Bumped when what's held changes (the fingers then snap to it instead of easing over).</summary>
        public int Generation;

        /// <summary>The fingers copy a source hand (the game's first-person one) as it holds the thing.</summary>
        public bool FromSource;

        /// <summary>The source hand's finger joints, world: index, middle, ring, little, thumb; four each, knuckle to tip.</summary>
        public readonly Vector3[] Joints = new Vector3[20];

        /// <summary>The source hand as a frame (z along its fingers, y out of the back of the hand).</summary>
        public Quaternion SourceFrame = Quaternion.identity;

        /// <summary>What the fingers stop on: the held thing's parts.</summary>
        public readonly List<ShapeField> Shapes = new List<ShapeField>();

        /// <summary>And these capsules (world): the gun hand's fingers, for the hand cupped round them.</summary>
        public int Capsules;

        public readonly Vector3[] CapA = new Vector3[16], CapB = new Vector3[16];
        public readonly float[] CapR = new float[16];

        /// <summary>The index on the trigger (1) or along the frame (0); how far the trigger's pulled (0..1).</summary>
        public float IndexOn = 1f, Squeeze;

        /// <summary>Contact on (off: the source's pose as it is).</summary>
        public bool Contact = true;
    }

    public static readonly Grip[] Hands = { new Grip(), new Grip() };

    public static bool Active(int side) => Time.frameCount - Hands[side].SetFrame <= 1;

    /// <summary>A hand's grip, to be filled for this frame.</summary>
    public static Grip Begin(int side, bool fromSource)
    {
        var g = Hands[side];
        if (!Active(side) || g.FromSource != fromSource) g.Generation++;
        g.SetFrame = Time.frameCount;
        g.FromSource = fromSource;
        g.Capsules = 0;
        g.Contact = true;
        return g;
    }

    /// <summary>The fingers' size against the hand's (radius per bone, world metres at a body scale of 1): tunable (dev "fingerr").</summary>
    public static float RadiusBase = 0.0088f, RadiusMid = 0.0080f, RadiusTip = 0.0072f;

    // ------------------------------------------------------------------ what each hand's fingers look like now

    /// <summary>Each hand's finger bones as last drawn (capsules: base, joint, joint, tip), for the other hand to wrap round.</summary>
    public static readonly Vector3[][] Drawn = { new Vector3[20], new Vector3[20] };

    public static readonly int[] DrawnFrame = { -10, -10 };

    // ------------------------------------------------------------------ touching

    private static int _queries;
    public static int Queries => _queries;

    /// <summary>Whether a capsule (a finger bone: from a to b, radius r, world) meets what this grip holds.</summary>
    public static bool Touches(Grip g, Vector3 a, Vector3 b, float r)
    {
        _queries++;
        for (var i = 0; i < g.Shapes.Count; i++)
            if (g.Shapes[i].Touches(a, b, r))
                return true;
        for (var i = 0; i < g.Capsules; i++)
            if (SegmentDistance(a, b, g.CapA[i], g.CapB[i]) < r + g.CapR[i])
                return true;
        return false;
    }

    /// <summary>The shortest distance between two segments.</summary>
    public static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-9f && e <= 1e-9f) return r.magnitude;
        if (a <= 1e-9f)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= 1e-9f)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denom = a * e - b * b;
                s = denom > 1e-9f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }

        return (p1 + d1 * s - (p2 + d2 * t)).magnitude;
    }

    public static string Describe()
    {
        var sb = new System.Text.StringBuilder("GRIPFINGERS");
        for (var i = 0; i < 2; i++)
        {
            var g = Hands[i];
            sb.Append($" | {(i == 0 ? "L" : "R")} active {Active(i)} source {g.FromSource} gen {g.Generation} shapes {g.Shapes.Count} ({string.Join(",", g.Shapes.ConvertAll(s => s.Describe()).ToArray())}) capsules {g.Capsules} index on {g.IndexOn:F2} squeeze {g.Squeeze:F2}");
        }

        return sb.Append($" | queries {_queries} | radii {RadiusBase:F4}/{RadiusMid:F4}/{RadiusTip:F4}").ToString();
    }
}

/// <summary>
/// One part of a held thing (a gun's body, its slide, its pump) as a distance field: how far any point near it is from
/// its surface, in the space of the bone it rides on (so the slide's moves with the slide). Built from the part's mesh
/// as the game draws it (BakeMesh: its own meshes can't be read), on a worker thread, once a session for each kind.
/// </summary>
internal sealed class ShapeField
{
    public sealed class Data
    {
        public string Name;
        public volatile bool Ready;
        public Vector3 Min, Max;
        public float H, Band;
        public int Nx, Ny, Nz;
        public byte[] Dist;
        public int Tris;
        public float BuildMs;
    }

    private static readonly Dictionary<string, Data> Cache = new Dictionary<string, Data>();
    public readonly Transform Bone;
    public readonly Data D;
    private Matrix4x4 _toLocal;
    private int _matrixFrame = -1;
    private float _scale = 1f;

    private ShapeField(Transform bone, Data d)
    {
        Bone = bone;
        D = d;
    }

    public string Describe() => $"{D.Name}{(D.Ready ? $" {D.Nx}x{D.Ny}x{D.Nz} h{D.H * 1000f:F1}mm {D.BuildMs:F0}ms" : " building")}";

    /// <summary>A part of what's held, by its renderer: its field (made now on a worker if this kind's is new).</summary>
    public static ShapeField For(SkinnedMeshRenderer smr, string kind)
    {
        if (smr == null || smr.sharedMesh == null) return null;
        var bone = smr.bones != null && smr.bones.Length == 1 && smr.bones[0] != null ? smr.bones[0] : smr.rootBone != null ? smr.rootBone : smr.transform;
        var key = kind + "/" + smr.name + "/" + smr.sharedMesh.name;
        if (Cache.TryGetValue(key, out var d)) return new ShapeField(bone, d);

        // the part as it's drawn now, into its bone's space
        var baked = new Mesh();
        try
        {
            smr.BakeMesh(baked);
            var verts = baked.vertices;
            var tris = baked.triangles;
            // (whether the snapshot already carries the renderer's scale: its size against the renderer's bounds)
            var t = smr.transform;
            var scaled = true;
            var ls = t.lossyScale;
            if (Mathf.Abs(ls.x - 1f) > 0.01f || Mathf.Abs(ls.y - 1f) > 0.01f || Mathf.Abs(ls.z - 1f) > 0.01f)
            {
                var size = baked.bounds.size.magnitude;
                var world = smr.bounds.size.magnitude;
                scaled = Mathf.Abs(size - world) < Mathf.Abs(Vector3.Scale(baked.bounds.size, ls).magnitude - world);
            }

            var toWorld = Matrix4x4.TRS(t.position, t.rotation, scaled ? Vector3.one : ls);
            var toBone = bone.worldToLocalMatrix * toWorld;
            for (var i = 0; i < verts.Length; i++) verts[i] = toBone.MultiplyPoint3x4(verts[i]);
            var scale = Mathf.Abs(bone.lossyScale.x);
            d = new Data { Name = smr.name, Tris = tris.Length / 3 };
            Cache[key] = d;
            var dd = d;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Build(dd, verts, tris, scale);
                }
                catch (System.Exception e)
                {
                    Plugin.L.LogWarning($"Grip: the {dd.Name} field failed ({e.Message})");
                }
            });
            return new ShapeField(bone, d);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning($"Grip: {smr.name} couldn't be read ({e.Message})");
            return null;
        }
        finally
        {
            Object.Destroy(baked);
        }
    }

    /// <summary>The field: every cell's distance to the nearest triangle, up to a band round the surface (beyond that,
    /// "far"): enough for a finger to find where it touches.</summary>
    private static void Build(Data d, Vector3[] v, int[] tris, float scale)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var band = 0.014f / Mathf.Max(scale, 1e-4f);
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (var p in v)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        min -= Vector3.one * band;
        max += Vector3.one * band;
        var size = max - min;
        var vol = Mathf.Max(size.x * size.y * size.z, 1e-9f);
        var h = Mathf.Max(0.0025f / Mathf.Max(scale, 1e-4f), Mathf.Pow(vol / 400000f, 1f / 3f));
        int nx = Mathf.Max(2, Mathf.CeilToInt(size.x / h) + 1), ny = Mathf.Max(2, Mathf.CeilToInt(size.y / h) + 1), nz = Mathf.Max(2, Mathf.CeilToInt(size.z / h) + 1);
        var dist = new float[nx * ny * nz];
        for (var i = 0; i < dist.Length; i++) dist[i] = band;
        var reach = band;
        for (var t = 0; t + 2 < tris.Length; t += 3)
        {
            var a = v[tris[t]];
            var b = v[tris[t + 1]];
            var c = v[tris[t + 2]];
            var lo = Vector3.Min(a, Vector3.Min(b, c)) - Vector3.one * reach;
            var hi = Vector3.Max(a, Vector3.Max(b, c)) + Vector3.one * reach;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((lo.x - min.x) / h), 0, nx - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((hi.x - min.x) / h), 0, nx - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((lo.y - min.y) / h), 0, ny - 1), y1 = Mathf.Clamp(Mathf.CeilToInt((hi.y - min.y) / h), 0, ny - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((lo.z - min.z) / h), 0, nz - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((hi.z - min.z) / h), 0, nz - 1);
            for (var z = z0; z <= z1; z++)
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector3(min.x + x * h, min.y + y * h, min.z + z * h);
                var k = (z * ny + y) * nx + x;
                var dd = PointTriangle(p, a, b, c);
                if (dd < dist[k]) dist[k] = dd;
            }
        }

        var bytes = new byte[dist.Length];
        for (var i = 0; i < dist.Length; i++) bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(dist[i] / band * 255f), 0, 255);
        d.Min = min;
        d.Max = max;
        d.H = h;
        d.Band = band;
        d.Nx = nx;
        d.Ny = ny;
        d.Nz = nz;
        d.Dist = bytes;
        d.BuildMs = (float)sw.Elapsed.TotalMilliseconds;
        d.Ready = true;
    }

    /// <summary>Distance from a point to a triangle (Ericson's closest point).</summary>
    private static float PointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return ap.magnitude;
        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return bp.magnitude;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (p - (a + ab * (d1 / (d1 - d3)))).magnitude;
        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return cp.magnitude;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (p - (a + ac * (d2 / (d2 - d6)))).magnitude;
        var va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return (p - (b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6))))).magnitude;
        var denom = 1f / (va + vb + vc);
        return (p - (a + ab * (vb * denom) + ac * (vc * denom))).magnitude;
    }

    /// <summary>How far a world point is from the part's surface (world metres; beyond the band, or not built yet: far).</summary>
    public float Distance(Vector3 world)
    {
        if (!D.Ready || Bone == null) return float.MaxValue;
        if (_matrixFrame != Time.frameCount)
        {
            _matrixFrame = Time.frameCount;
            _toLocal = Bone.worldToLocalMatrix;
            _scale = Mathf.Abs(Bone.lossyScale.x);
        }

        var p = _toLocal.MultiplyPoint3x4(world);
        if (p.x < D.Min.x || p.y < D.Min.y || p.z < D.Min.z || p.x > D.Max.x || p.y > D.Max.y || p.z > D.Max.z) return float.MaxValue;
        var fx = (p.x - D.Min.x) / D.H;
        var fy = (p.y - D.Min.y) / D.H;
        var fz = (p.z - D.Min.z) / D.H;
        int x = Mathf.Min((int)fx, D.Nx - 2), y = Mathf.Min((int)fy, D.Ny - 2), z = Mathf.Min((int)fz, D.Nz - 2);
        float tx = fx - x, ty = fy - y, tz = fz - z;
        var nx = D.Nx;
        var nxy = D.Nx * D.Ny;
        var k = z * nxy + y * nx + x;
        var dd = D.Dist;
        float c00 = Mathf.Lerp(dd[k], dd[k + 1], tx), c10 = Mathf.Lerp(dd[k + nx], dd[k + nx + 1], tx);
        float c01 = Mathf.Lerp(dd[k + nxy], dd[k + nxy + 1], tx), c11 = Mathf.Lerp(dd[k + nxy + nx], dd[k + nxy + nx + 1], tx);
        var v = Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz);
        if (v >= 254f) return float.MaxValue;
        return v / 255f * D.Band * _scale;
    }

    /// <summary>Whether a capsule meets the part (sampled along it).</summary>
    public bool Touches(Vector3 a, Vector3 b, float r)
    {
        if (!D.Ready) return false;
        var len = (b - a).magnitude;
        var n = Mathf.Clamp(Mathf.CeilToInt(len / Mathf.Max(r, 0.004f)) + 1, 2, 6);
        for (var i = 0; i < n; i++)
            if (Distance(Vector3.Lerp(a, b, i / (float)(n - 1))) < r)
                return true;
        return false;
    }
}
