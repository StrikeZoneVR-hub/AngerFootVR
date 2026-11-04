using System.Collections.Generic;
using Framework;
using UnityEngine;

namespace AngerFootVR.Rendering;

/// <summary>
/// Blood that leaves the body (GORE: EXTRA, with Wounds): drops falling off the end of a run of blood, a splat where each
/// lands (a goon bleeding as it runs leaves a trail); a pool spreading slowly on the floor under a body from its worst
/// wounds; and out of a big wound, chunks of flesh and bits of bone (the mod's own, made in Blender) thrown out the way
/// the shot went, bouncing, leaving a smear where they land. None of it touches anything in the game (it only falls and
/// lands on the level's surfaces, as the game's own blood does), and it keeps to a budget.
/// </summary>
internal static class Bleeding
{
    private const int MaxDrops = 48, MaxGibs = 36, MaxPools = 14, MaxDropSplats = 60;

    private sealed class Falling
    {
        public GameObject Go;
        public Vector3 Pos, Vel, Spin;
        public float Size, Life, Born;
        public bool Gib;
        public int Bounces;
        public bool Resting;
    }

    private sealed class Pool
    {
        public GameObject Go;
        public float Born, From, To, Grow;
    }

    private static readonly List<Falling> Moving = new List<Falling>();
    private static readonly Queue<GameObject> Splats = new Queue<GameObject>();
    private static readonly List<Pool> Pools = new List<Pool>();
    private static readonly List<Falling> Resting = new List<Falling>();
    private static Mesh _drop;
    private static Material _dropMat, _flesh, _bone, _skin;
    private static int _drops, _gibs, _pools, _landed;

    private static int Mask => LayerMasks.ThingsBloodSplatterCanHit;

    // ------------------------------------------------------------------ drops

    /// <summary>A drop of blood leaves here (the end of a run of blood) and falls.</summary>
    public static void Drop(Vector3 at, float width)
    {
        if (!Gore.Extra || Moving.Count >= MaxDrops + MaxGibs) return;
        if ((at - Core.VrRig.HeadPosition).sqrMagnitude > 25f * 25f) return;
        if (_drop == null)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _drop = s.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(s);
        }

        if (_dropMat == null)
        {
            _dropMat = new Material(Shader.Find("Standard")) { name = "AngerFootVR blood drop" };
            _dropMat.SetFloat("_Glossiness", 0.92f);
            _dropMat.enableInstancing = true;
        }

        _dropMat.color = Gore.Colour * 0.8f;
        var go = new GameObject("AngerFootVR drop") { layer = UI.VrUi.Layer };
        go.AddComponent<MeshFilter>().sharedMesh = _drop;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _dropMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var size = Mathf.Clamp(width * 1.4f, 0.004f, 0.011f);
        go.transform.localScale = new Vector3(size, size * 1.6f, size);
        go.transform.position = at;
        Moving.Add(new Falling { Go = go, Pos = at, Vel = Vector3.down * 0.2f, Size = size, Born = Time.time, Life = 4f });
        _drops++;
    }

    // ------------------------------------------------------------------ gibs

    private static readonly string[] Chunks = { "gib_flesh_0", "gib_flesh_1", "gib_flesh_2", "gib_flesh_3", "gib_flesh_4", "gib_flap_0", "gib_flap_1" };
    private static readonly string[] Bones = { "gib_bone_0", "gib_bone_1", "gib_bone_2", "gib_rib_0" };

    /// <summary>Chunks and bits thrown out of a big wound (from here, the way the shot or blast went).</summary>
    public static void Gibs(Vector3 at, Vector3 dir, int count)
    {
        if (!Gore.Extra || !UI.Props.Load()) return;
        if ((at - Core.VrRig.HeadPosition).sqrMagnitude > 30f * 30f) return;
        var gib = Shaders.Get("AngerFootVR/Gib");
        if (_flesh == null)
        {
            _flesh = new Material(gib != null ? gib : Shader.Find("Standard")) { name = "AngerFootVR gib flesh", enableInstancing = true };
            _bone = new Material(_flesh) { name = "AngerFootVR gib bone" };
            _skin = new Material(_flesh) { name = "AngerFootVR gib skin" };
            if (gib != null)
            {
                _flesh.SetTexture("_MainTex", Shaders.Tex("gore_flesh"));
                _bone.SetTexture("_MainTex", Shaders.Tex("gore_bone"));
                _skin.SetTexture("_MainTex", Shaders.Tex("gore_flesh"));
                _skin.SetColor("_Color", new Color(0.55f, 0.75f, 0.5f));
                _bone.SetFloat("_Gloss", 0.25f);
            }
        }

        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.up;
        for (var i = 0; i < count; i++)
        {
            if (Moving.Count + Resting.Count >= MaxDrops + MaxGibs)
            {
                if (Resting.Count == 0) break;
                Object.Destroy(Resting[0].Go);
                Resting.RemoveAt(0);
            }

            var bone = i % 3 == 2;
            var name = bone ? Bones[Random.Range(0, Bones.Length)] : Chunks[Random.Range(0, Chunks.Length)];
            var model = UI.Props.Get(name);
            if (model == null) continue;
            var go = new GameObject("AngerFootVR " + name) { layer = UI.VrUi.Layer };
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[model.Materials.Length];
            for (var k = 0; k < mats.Length; k++) mats[k] = model.Materials[k].Contains("bone") ? _bone : model.Materials[k].Contains("skin") ? _skin : _flesh;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var s = Random.Range(0.75f, 1.3f);
            go.transform.localScale = Vector3.one * s;
            go.transform.position = at;
            go.transform.rotation = Random.rotation;
            var spread = Random.insideUnitSphere * 0.6f;
            var v = (dir + spread + Vector3.up * 0.35f).normalized * Random.Range(2.2f, 4.5f);
            Moving.Add(new Falling { Go = go, Pos = at, Vel = v, Spin = Random.insideUnitSphere * 720f, Size = model.Mesh.bounds.extents.magnitude * s, Born = Time.time, Life = 25f, Gib = true });
            _gibs++;
        }
    }

    // ------------------------------------------------------------------ pools

    /// <summary>A pool spreading on the floor under a body (from a wound there): to its size over some seconds.</summary>
    public static void PoolUnder(Vector3 wound, float size)
    {
        if (!Gore.Extra) return;
        if (!Physics.Raycast(wound + Vector3.up * 0.1f, Vector3.down, out var hit, 1.6f, Mask, QueryTriggerInteraction.Ignore)) return;
        var go = Gore.SplatNow(hit.point, Vector3.down, 0.3f, null);
        if (go == null) return;
        if (Pools.Count >= MaxPools)
        {
            var old = Pools[0];
            Pools.RemoveAt(0);
            if (old.Go != null) Object.Destroy(old.Go);
        }

        Pools.Add(new Pool { Go = go, Born = Time.time, From = go.transform.localScale.x, To = go.transform.localScale.x * size, Grow = Random.Range(7f, 12f) });
        _pools++;
    }

    // ------------------------------------------------------------------ every frame

    public static void Update()
    {
        var dt = Mathf.Min(Time.deltaTime, 0.05f);
        var now = Time.time;
        var g = Physics.gravity;
        for (var i = Moving.Count - 1; i >= 0; i--)
        {
            var f = Moving[i];
            if (f.Go == null || now - f.Born > f.Life)
            {
                if (f.Go != null) Object.Destroy(f.Go);
                Moving.RemoveAt(i);
                continue;
            }

            f.Vel += g * dt;
            var step = f.Vel * dt;
            if (Physics.SphereCast(f.Pos, f.Size * 0.5f, step.normalized, out var hit, step.magnitude + 0.002f, Mask, QueryTriggerInteraction.Ignore))
            {
                var at = hit.point;
                if (!f.Gib)
                {
                    // a drop lands: a little splat, and it's gone
                    Splat(at, -hit.normal, Random.Range(0.08f, 0.16f));
                    Object.Destroy(f.Go);
                    Moving.RemoveAt(i);
                    _landed++;
                    continue;
                }

                // a chunk: a smear where it hits, a bounce or two, then it lies there
                if (f.Bounces == 0 || f.Vel.magnitude > 2f) Splat(at, -hit.normal, Random.Range(0.18f, 0.32f));
                f.Bounces++;
                f.Pos = at + hit.normal * (f.Size * 0.4f);
                f.Vel = Vector3.Reflect(f.Vel, hit.normal) * 0.28f;
                f.Spin *= 0.4f;
                if (f.Vel.magnitude < 0.5f || f.Bounces >= 3)
                {
                    // (resting on what it fell on, flat side down)
                    f.Go.transform.position = at + hit.normal * (f.Size * 0.3f);
                    f.Go.transform.rotation = Quaternion.FromToRotation(f.Go.transform.up, hit.normal) * f.Go.transform.rotation;
                    Moving.RemoveAt(i);
                    f.Resting = true;
                    Resting.Add(f);
                    continue;
                }
            }
            else f.Pos += step;

            f.Go.transform.position = f.Pos;
            if (f.Gib) f.Go.transform.rotation = Quaternion.Euler(f.Spin * dt) * f.Go.transform.rotation;
            else if (f.Vel.sqrMagnitude > 0.01f) f.Go.transform.rotation = Quaternion.FromToRotation(Vector3.up, -f.Vel.normalized);
        }

        // the gibs that came to rest go after a while (and when there are too many)
        for (var i = Resting.Count - 1; i >= 0; i--)
        {
            var f = Resting[i];
            if (f.Go == null)
            {
                Resting.RemoveAt(i);
                continue;
            }

            var age = now - f.Born;
            if (age > 60f)
            {
                Object.Destroy(f.Go);
                Resting.RemoveAt(i);
            }
        }

        // the pools spread (fast, then slower)
        foreach (var p in Pools)
        {
            if (p.Go == null) continue;
            var t = Mathf.Clamp01((now - p.Born) / p.Grow);
            var e = 1f - (1f - t) * (1f - t);
            p.Go.transform.localScale = Vector3.one * Mathf.Lerp(p.From, p.To, e);
        }
    }

    private static void Splat(Vector3 at, Vector3 dir, float scale)
    {
        var go = Gore.SplatNow(at, dir, scale, null);
        if (go == null) return;
        Splats.Enqueue(go);
        while (Splats.Count > MaxDropSplats)
        {
            var old = Splats.Dequeue();
            if (old != null) Object.Destroy(old);
        }
    }

    public static void NewLevel()
    {
        foreach (var f in Moving)
            if (f.Go != null)
                Object.Destroy(f.Go);
        foreach (var f in Resting)
            if (f.Go != null)
                Object.Destroy(f.Go);
        Moving.Clear();
        Resting.Clear();
        Splats.Clear();
        Pools.Clear();
    }

    public static string Describe() => $"BLEEDING {_drops} drops ({_landed} landed), {_gibs} gibs, {_pools} pools | now {Moving.Count} falling, {Resting.Count} resting, {Splats.Count} splats, {Pools.Count} pools";
}
