using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// The movie house's effects, made here: water gushing out and spreading into puddles (they splash when you walk
/// through them), splashes, sparks, smoke, explosions (a flash, a fireball, smoke, everything near flung out), candy
/// and confetti bursts, a TV screen's cracks, muzzle flashes and bullet holes. All in unscaled time (the movie house
/// plays on while the game's time stands still), on the movie house's layer.
/// </summary>
internal static class Fx
{
    private static Material _soft, _softAdd, _puddle, _hole;
    private static Texture2D _dot, _ring, _crackTex;
    private static readonly List<PuddleState> Puddles = new List<PuddleState>();

    private sealed class PuddleState
    {
        public Transform T;
        public Material M;
        public float Born, Radius, Grow, Life;
        public Vector3 At;
        public float LastSplash;
    }

    private static Shader FindShader(params string[] names)
    {
        foreach (var n in names)
        {
            var s = Shader.Find(n);
            if (s != null) return s;
        }

        return Shader.Find("Sprites/Default");
    }

    private static Texture2D Dot()
    {
        if (_dot != null) return _dot;
        const int n = 32;
        _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AngerFootVR soft dot" };
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
            var a = Mathf.Clamp01(1f - d);
            _dot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }

        _dot.Apply();
        Object.DontDestroyOnLoad(_dot);
        return _dot;
    }

    private static Material Soft(bool additive)
    {
        if (additive && _softAdd != null) return _softAdd;
        if (!additive && _soft != null) return _soft;
        var m = new Material(FindShader(additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended", "Particles/Standard Unlit", "Sprites/Default", "UI/Default"))
        {
            mainTexture = Dot(), name = "AngerFootVR particles " + (additive ? "glow" : "soft")
        };
        m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        if (additive) _softAdd = m;
        else _soft = m;
        return m;
    }

    /// <summary>A one-off burst of particles (gone when they are), or a stream that runs for a while.</summary>
    public static ParticleSystem Particles(Vector3 at, Quaternion facing, int burst, float rate, float duration, float life, float speed, float size,
        Color colour, float gravity, float spread, bool glow, bool stretch, float floorY = float.NaN, Transform follow = null)
    {
        var go = new GameObject("AngerFootVR fx") { layer = VrUi.Layer };
        if (follow != null) go.transform.SetParent(follow, false);
        go.transform.SetPositionAndRotation(at, facing);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = Mathf.Max(0.05f, duration);
        main.loop = false;
        main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.2f);
        main.startColor = colour;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(burst, 1) + Mathf.CeilToInt(rate * duration) + 8;
        main.playOnAwake = false;
        var em = ps.emission;
        em.rateOverTime = rate;
        if (burst > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = spread;
        shape.radius = 0.02f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        if (!float.IsNaN(floorY))
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.Planes;
            var plane = new GameObject("floor").transform;
            plane.SetParent(go.transform, false);
            plane.position = new Vector3(at.x, floorY, at.z);
            plane.rotation = Quaternion.identity;
            c.SetPlane(0, plane);
            c.dampen = 0.6f;
            c.bounce = 0.15f;
            c.lifetimeLoss = 0.4f;
        }

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Soft(glow);
        r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (stretch)
        {
            r.velocityScale = 0.04f;
            r.lengthScale = 1.2f;
        }

        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Object.Destroy(go, duration + life + 0.5f);
        return ps;
    }

    // ---------------------------------------------------------------------------------- water

    private static readonly Color Water = new Color(0.55f, 0.75f, 1f, 0.7f);

    /// <summary>Water gushing out of something (the fish tank, the cooler's jug), and the puddle it makes under it.</summary>
    public static void Gush(Vector3 at, Vector3 dir, float seconds, float puddle, float floorY)
    {
        Particles(at, Quaternion.LookRotation(dir == Vector3.zero ? Vector3.down : dir), 40, 160, seconds, 0.9f, 2.8f, 0.05f, Water, 1.1f, 28f, false, true, floorY);
        Particles(at, Quaternion.LookRotation(Vector3.up), 25, 0, 0.2f, 0.6f, 1.5f, 0.06f, Water, 0.9f, 60f, false, true, floorY);
        Puddle(new Vector3(at.x, floorY + 0.004f, at.z), puddle, seconds + 1.5f, 40f);
    }

    /// <summary>A pour of water from a thing (a stream that follows it).</summary>
    public static ParticleSystem Pour(Transform from, Vector3 localAt, float seconds, float floorY)
    {
        var ps = Particles(from.TransformPoint(localAt), Quaternion.LookRotation(Vector3.down), 0, 90, seconds, 0.8f, 1.2f, 0.035f, Water, 1f, 6f, false, true, floorY);
        ps.transform.SetParent(from, true);
        return ps;
    }

    /// <summary>A puddle growing on the floor to its size, drying away after a while.</summary>
    public static void Puddle(Vector3 at, float radius, float growSeconds, float lifeSeconds)
    {
        if (_puddle == null)
        {
            _puddle = new Material(FindShader("Standard")) { name = "AngerFootVR puddle", mainTexture = Dot(), color = new Color(0.25f, 0.45f, 0.65f, 0.55f) };
            _puddle.SetFloat("_Glossiness", 0.96f);
            _puddle.SetFloat("_Metallic", 0f);
            _puddle.SetFloat("_Mode", 2f);
            _puddle.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _puddle.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _puddle.SetInt("_ZWrite", 0);
            _puddle.EnableKeyword("_ALPHABLEND_ON");
            _puddle.renderQueue = 2990;
            _puddle.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "AngerFootVR puddle";
        go.layer = VrUi.Layer;
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        go.transform.localScale = Vector3.one * 0.05f;
        var m = new Material(_puddle);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Puddles.Add(new PuddleState { T = go.transform, M = m, Born = Time.unscaledTime, Radius = radius, Grow = growSeconds, Life = lifeSeconds, At = at });
        while (Puddles.Count > 8) Drop(0);
    }

    private static void Drop(int i)
    {
        if (Puddles[i].T != null) Object.Destroy(Puddles[i].T.gameObject);
        if (Puddles[i].M != null) Object.Destroy(Puddles[i].M);
        Puddles.RemoveAt(i);
    }

    /// <summary>Whether a point of the floor is in a puddle (footsteps splash).</summary>
    public static bool InPuddle(Vector3 feet)
    {
        foreach (var p in Puddles)
        {
            if (p.T == null) continue;
            var r = p.T.localScale.x * 0.42f;
            if (new Vector2(feet.x - p.At.x, feet.z - p.At.z).magnitude < r && Mathf.Abs(feet.y - p.At.y) < 0.3f) return true;
        }

        return false;
    }

    public static void Splash(Vector3 at, float amount = 1f)
    {
        Particles(at + Vector3.up * 0.02f, Quaternion.LookRotation(Vector3.up), Mathf.RoundToInt(18 * amount), 0, 0.1f, 0.5f, 1.6f * amount, 0.04f, Water, 1f, 50f, false, true, at.y);
        GameSounds.PlayOr("Player Footstep Water", "step", at, 0.7f * amount);
    }

    // ---------------------------------------------------------------------------------- fire and sparks

    public static void Sparks(Vector3 at, Vector3 dir, int count = 30)
    {
        Particles(at, Quaternion.LookRotation(dir == Vector3.zero ? Vector3.up : dir), count, 0, 0.1f, 0.6f, 4f, 0.018f, new Color(1f, 0.75f, 0.35f), 1.2f, 55f, true, true, float.NaN);
    }

    public static void Smoke(Vector3 at, float seconds, float amount = 1f)
    {
        Particles(at, Quaternion.LookRotation(Vector3.up), 4, 14 * amount, seconds, 2.2f, 0.45f, 0.35f * amount, new Color(0.25f, 0.24f, 0.24f, 0.55f), -0.05f, 18f, false, false);
    }

    /// <summary>A blast: a flash of light, a fireball, smoke, sparks and the bang.</summary>
    public static void Explosion(Vector3 at, float size = 1f)
    {
        Particles(at, Quaternion.LookRotation(Vector3.up), Mathf.RoundToInt(40 * size), 0, 0.1f, 0.5f, 5f * size, 0.45f * size, new Color(1f, 0.55f, 0.15f), -0.2f, 90f, true, false);
        Particles(at, Quaternion.LookRotation(Vector3.up), Mathf.RoundToInt(20 * size), 0, 0.1f, 0.25f, 2f * size, 0.9f * size, new Color(1f, 0.9f, 0.6f), 0f, 90f, true, false);
        Smoke(at + Vector3.up * 0.3f, 1.2f, 2f * size);
        Sparks(at, Vector3.up, Mathf.RoundToInt(50 * size));
        var lg = new GameObject("AngerFootVR blast light") { layer = VrUi.Layer };
        lg.transform.position = at + Vector3.up * 0.4f;
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.6f, 0.25f);
        l.range = 7f * size;
        l.intensity = 6f;
        lg.AddComponent<Fade>().Light = l;
        if (!GameSounds.Play("Barrel Explosion", at, 1f) && !GameSounds.Play("Grenade explosion", at, 1f)) Sounds.Play("door_bang", at, 1f, 0.6f);
    }

    /// <summary>Candy and confetti flying out (the piñata).</summary>
    public static void Confetti(Vector3 at)
    {
        foreach (var c in new[] { new Color(1f, 0.3f, 0.65f), new Color(0.2f, 0.9f, 1f), new Color(1f, 0.85f, 0.2f) })
            Particles(at, Quaternion.LookRotation(Vector3.up), 30, 0, 0.1f, 2.2f, 3.2f, 0.03f, c, 0.35f, 80f, false, false, float.NaN);
    }

    private sealed class Fade : MonoBehaviour
    {
        public Light Light;
        private float _born;

        private void Start() => _born = Time.unscaledTime;

        private void Update()
        {
            var t = (Time.unscaledTime - _born) / 0.35f;
            if (Light != null) Light.intensity = Mathf.Lerp(6f, 0f, t);
            if (t >= 1f) Destroy(gameObject);
        }
    }

    // ---------------------------------------------------------------------------------- the TV's cracks

    /// <summary>A cracked screen (a picture of cracks, made once), stuck on a TV's glass.</summary>
    public static GameObject Cracks(Transform tv, Vector3 localCentre, Vector2 size)
    {
        if (_crackTex == null)
        {
            const int n = 256;
            _crackTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "AngerFootVR cracks", wrapMode = TextureWrapMode.Clamp };
            var clear = new Color(1f, 1f, 1f, 0f);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                _crackTex.SetPixel(x, y, clear);
            var rnd = new System.Random(4);
            var hit = new Vector2(n * 0.42f, n * 0.58f);
            for (var k = 0; k < 14; k++)
            {
                var a = (float)(rnd.NextDouble() * Mathf.PI * 2);
                var p = hit;
                var len = 40 + rnd.Next(110);
                for (var s = 0; s < len; s++)
                {
                    a += (float)(rnd.NextDouble() - 0.5) * 0.35f;
                    p += new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var ix = (int)p.x;
                    var iy = (int)p.y;
                    if (ix < 1 || iy < 1 || ix >= n - 1 || iy >= n - 1) break;
                    var w = 1f - s / (float)len;
                    _crackTex.SetPixel(ix, iy, new Color(0.92f, 0.97f, 1f, 0.55f + 0.45f * w));
                    if (w > 0.6f) _crackTex.SetPixel(ix + 1, iy, new Color(0.92f, 0.97f, 1f, 0.6f * w));
                }
            }

            for (var k = 0; k < 3; k++)
            {
                var r = 10 + k * 9;
                for (var t = 0; t < 120; t++)
                {
                    var a = t / 120f * Mathf.PI * 2;
                    var ix = (int)(hit.x + Mathf.Cos(a) * r * (0.8f + 0.3f * (float)rnd.NextDouble()));
                    var iy = (int)(hit.y + Mathf.Sin(a) * r * (0.8f + 0.3f * (float)rnd.NextDouble()));
                    if (ix > 0 && iy > 0 && ix < n && iy < n) _crackTex.SetPixel(ix, iy, new Color(0.92f, 0.97f, 1f, 0.7f));
                }
            }

            _crackTex.Apply();
            Object.DontDestroyOnLoad(_crackTex);
        }

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.name = "Cracks";
        q.layer = VrUi.Layer;
        q.transform.SetParent(tv, false);
        q.transform.localPosition = localCentre;
        q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        var m = new Material(FindShader("Sprites/Default", "UI/Default", "Unlit/Transparent")) { mainTexture = _crackTex, renderQueue = 3001 };
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
        q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q;
    }

    // ---------------------------------------------------------------------------------- the range

    /// <summary>A muzzle flash at a gun's muzzle (a moment of light and a star of glow).</summary>
    public static void MuzzleFlash(Vector3 at, Vector3 dir, float size = 1f)
    {
        Particles(at, Quaternion.LookRotation(dir), 6, 0, 0.05f, 0.06f, 1.2f, 0.16f * size, new Color(1f, 0.8f, 0.4f), 0f, 25f, true, false);
        var lg = new GameObject("AngerFootVR muzzle light") { layer = VrUi.Layer };
        lg.transform.position = at;
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.75f, 0.4f);
        l.range = 3.5f;
        l.intensity = 3f;
        Object.Destroy(lg, 0.05f);
    }

    /// <summary>A bullet hole stuck to what it hit (it goes with it).</summary>
    public static void Hole(Vector3 at, Vector3 normal, Transform onto, float size = 0.035f)
    {
        if (_hole == null)
        {
            const int n = 32;
            _ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AngerFootVR hole" };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                var a = d < 0.45f ? 1f : Mathf.Clamp01((1f - d) / 0.55f) * 0.55f;
                _ring.SetPixel(x, y, new Color(0.03f, 0.02f, 0.02f, a));
            }

            _ring.Apply();
            Object.DontDestroyOnLoad(_ring);
            _hole = new Material(FindShader("Sprites/Default", "UI/Default")) { mainTexture = _ring, renderQueue = 2995, name = "AngerFootVR hole" };
            _hole.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(q.GetComponent<Collider>());
        q.name = "Hole";
        q.layer = VrUi.Layer;
        q.transform.SetPositionAndRotation(at + normal * 0.004f, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        q.transform.localScale = Vector3.one * size;
        if (onto != null) q.transform.SetParent(onto, true);
        q.GetComponent<MeshRenderer>().sharedMaterial = _hole;
        q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Holes.Enqueue(q);
        while (Holes.Count > 300)
        {
            var old = Holes.Dequeue();
            if (old != null) Object.Destroy(old);
        }
    }

    private static readonly Queue<GameObject> Holes = new Queue<GameObject>();

    /// <summary>Every frame in the movie house: puddles grow and dry, and splash under your feet.</summary>
    public static void Update(Vector3 feet, bool walked)
    {
        var now = Time.unscaledTime;
        for (var i = Puddles.Count - 1; i >= 0; i--)
        {
            var p = Puddles[i];
            if (p.T == null)
            {
                Puddles.RemoveAt(i);
                continue;
            }

            var age = now - p.Born;
            var grow = Mathf.Clamp01(age / Mathf.Max(0.1f, p.Grow));
            p.T.localScale = Vector3.one * Mathf.Max(0.05f, p.Radius * 2.4f * Mathf.Sqrt(grow));
            var fade = Mathf.Clamp01((p.Life - age) / 6f);
            p.M.color = new Color(0.25f, 0.45f, 0.65f, 0.55f * fade);
            if (age > p.Life) Drop(i);
        }

        if (walked && InPuddle(feet)) Splash(feet, 0.8f);
    }

    /// <summary>Every puddle gone (the movie house left).</summary>
    public static void Clear()
    {
        for (var i = Puddles.Count - 1; i >= 0; i--) Drop(i);
        while (Holes.Count > 0)
        {
            var h = Holes.Dequeue();
            if (h != null) Object.Destroy(h);
        }
    }
}
