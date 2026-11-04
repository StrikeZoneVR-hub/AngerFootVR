using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace AngerFootVR.Rendering;

/// <summary>
/// LASER SIGHT (1.2, off by default), for anyone still getting used to aiming with their hand ("I'm still getting used
/// to the aim", a player's video): a red dot where your shots go, and with BEAM a thin beam from the barrel to it that
/// fades as it goes. It's the ray the game's bullets take (from the muzzle along the barrel, the same one the shots are
/// fired along, stopped by what stops them), so the dot is where the shot lands. Dual pistols get one each. Never while
/// a gun spins, in the movie house, while the game's paused, or on the flamethrower.
/// The dot is drawn over the surface it's on (never cut into by it, however flat the angle), at its real distance, and
/// only when your eyes can see that spot; the beam goes behind things as it should.
/// </summary>
internal static class LaserSight
{
    private const float MaxDistance = 100f; // (as far as the game's bullets go)
    private const float Width = 0.003f; // the beam: 3 mm
    private const float DotAngle = 0.03f; // the dot's glow: 1.7 degrees across wherever it is (its hot centre about a quarter of that)
    private static readonly Color Red = new Color(1f, 0.12f, 0.1f);

    private static readonly Transform[] Beams = new Transform[2], Dots = new Transform[2];
    private static readonly bool[] Shown = new bool[2];
    private static readonly string[] OnWhat = { "", "" };
    private static readonly Vector3[] HitAt = new Vector3[2];
    private static GameObject _root;
    private static bool _hooked, _failed;
    private static int _placed = -1, _layer = -1;

    /// <summary>Dev: the style for now (the settings file untouched).</summary>
    public static VrSettings.SightStyle? DevStyle;

    public static VrSettings.SightStyle Style => DevStyle ?? (VrSettings.LaserSight == null ? VrSettings.SightStyle.Off : VrSettings.LaserSight.Value);

    /// <summary>Every frame: off at once when it shouldn't be there (it's placed just before your eyes draw).</summary>
    public static void Update()
    {
        if (!_hooked)
        {
            _hooked = true;
            Camera.onPreCull += BeforeCull;
        }

        if (_root != null && !Wanted()) Hide();
    }

    private static bool Wanted()
    {
        if (_failed || Style == VrSettings.SightStyle.Off || !XrManager.IsRunning || CinemaRig.Active || Time.timeScale <= 0.0001f) return false;
        if (!SingletonBehaviour<Player>.HasInstance || !SingletonBehaviour<PlayerCamera>.HasInstance) return false;
        var player = SingletonBehaviour<Player>.Instance;
        if (player == null || player.Health == null || !player.Health.IsAlive) return false;
        if (!(player.Weapon is ProjectileWeapon w) || w == null || w.HoldableObjectType == HoldableObjectType.FlameThrower) return false;
        return Input.GunHold.Holding;
    }

    /// <summary>Placed just before your eyes draw (everything has moved by then: the gun's where you see it).</summary>
    private static void BeforeCull(Camera cam)
    {
        if (_placed == Time.frameCount || _failed) return;
        var pc = SingletonBehaviour<PlayerCamera>.HasInstance ? SingletonBehaviour<PlayerCamera>.Instance : null;
        var eye = pc != null ? pc.MainCamera : null;
        if (eye == null || cam != eye) return;
        _placed = Time.frameCount;
        try
        {
            if (!Wanted())
            {
                Hide();
                return;
            }

            Ensure(eye, pc.WeaponCamera);
            var style = Style;
            var hand = Input.GunHold.Hand;
            var muzzle = Input.GunHold.Muzzle;
            if (!Input.GunSpin.Spinning(hand) && muzzle != null && muzzle.gameObject.activeInHierarchy)
            {
                Input.GunHold.BarrelRay(VrHands.GetRaw(hand), out var p, out var d);
                Place(0, p, d, eye, style);
            }
            else Off(0);

            if (Input.Arsenal.Dual && !Input.GunSpin.Spinning(XRNode.LeftHand) && Input.Arsenal.LeftBarrel(out var lp, out var ld)) Place(1, lp, ld, eye, style);
            else Off(1);
        }
        catch (System.Exception e)
        {
            _failed = true;
            Hide();
            Plugin.L.LogWarning("Laser sight: " + e.Message);
        }
    }

    private static void Place(int i, Vector3 from, Vector3 dir, Camera eye, VrSettings.SightStyle style)
    {
        if (dir.sqrMagnitude < 0.5f)
        {
            Off(i);
            return;
        }

        dir.Normalize();
        var hit = Physics.Raycast(new Ray(from, dir), out var h, MaxDistance, LayerMasks.ThingsPlayerProjectilesCanHit);
        var end = hit ? h.point : from + dir * MaxDistance;
        var length = Vector3.Distance(from, end);
        Shown[i] = true;
        HitAt[i] = end;
        OnWhat[i] = hit && h.collider != null ? h.collider.name : "nothing";

        var beam = Beams[i];
        var beamOn = style == VrSettings.SightStyle.Beam && length > 0.03f;
        if (beam.gameObject.activeSelf != beamOn) beam.gameObject.SetActive(beamOn);
        if (beamOn)
        {
            beam.SetPositionAndRotation(from + dir * (length * 0.5f), Quaternion.LookRotation(dir));
            beam.localScale = new Vector3(Width, Width, length);
        }

        // the dot, facing your eyes at the spot: only if your eyes can see it (from the barrel they may not)
        var dot = Dots[i];
        var eyePos = eye.transform.position;
        var toEye = eyePos - end;
        var dist = toEye.magnitude;
        var dotOn = hit && length > 0.05f && dist > 0.05f && !Physics.Raycast(new Ray(eyePos, -toEye / dist), dist - 0.05f, LayerMasks.ThingsPlayerProjectilesCanHit);
        if (dot.gameObject.activeSelf != dotOn) dot.gameObject.SetActive(dotOn);
        if (!dotOn) return;
        var size = Mathf.Max(0.012f, dist * DotAngle);
        dot.SetPositionAndRotation(end + toEye / dist * Mathf.Min(0.01f, dist * 0.5f), Quaternion.LookRotation(-toEye / dist, eye.transform.up));
        dot.localScale = new Vector3(size, size, 1f);
    }

    private static void Off(int i)
    {
        Shown[i] = false;
        if (Beams[i] != null && Beams[i].gameObject.activeSelf) Beams[i].gameObject.SetActive(false);
        if (Dots[i] != null && Dots[i].gameObject.activeSelf) Dots[i].gameObject.SetActive(false);
    }

    private static void Hide()
    {
        Off(0);
        Off(1);
    }

    private static void Ensure(Camera eye, Camera weapon)
    {
        // a layer your eyes' main camera draws (behind your gun, which the weapon camera draws over everything)
        var mask = eye.cullingMask & ~(weapon != null ? weapon.cullingMask : 0) & ~(1 << UI.VrUi.Layer);
        var layer = -1;
        for (var l = 0; l < 32; l++)
            if ((mask & (1 << l)) != 0)
            {
                layer = l;
                break;
            }

        if (layer < 0) layer = 0;
        if (_root != null)
        {
            if (layer != _layer)
            {
                _layer = layer;
                for (var i = 0; i < 2; i++)
                {
                    Beams[i].gameObject.layer = layer;
                    Dots[i].gameObject.layer = layer;
                }
            }

            return;
        }

        _layer = layer;
        _root = new GameObject("AngerFootVR laser sight");
        Object.DontDestroyOnLoad(_root);
        var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");

        // the beam: a thin box, bright at the barrel and fading to its end
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var beamMesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(cube);
        var vs = beamMesh.vertices;
        var cs = new Color[vs.Length];
        for (var k = 0; k < vs.Length; k++) cs[k] = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0.08f, vs[k].z + 0.5f));
        beamMesh.colors = cs;
        var beamMat = new Material(shader) { name = "AngerFootVR laser beam", color = Red, renderQueue = 3000 };
        beamMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);

        // the dot: a soft red glow round a hot centre
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var dotMesh = Object.Instantiate(quad.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(quad);
        var white = new Color[dotMesh.vertexCount];
        for (var k = 0; k < white.Length; k++) white[k] = Color.white;
        dotMesh.colors = white;
        var dotMat = new Material(shader) { name = "AngerFootVR laser dot", mainTexture = DotTexture(), color = Color.white, renderQueue = 3100 };
        dotMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

        for (var i = 0; i < 2; i++)
        {
            Beams[i] = Part("beam " + i, beamMesh, beamMat, layer);
            Dots[i] = Part("dot " + i, dotMesh, dotMat, layer);
        }
    }

    private static Transform Part(string name, Mesh mesh, Material mat, int layer)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(_root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.SetActive(false);
        return go.transform;
    }

    private static Texture2D DotTexture()
    {
        const int n = 64;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "AngerFootVR laser dot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var dx = (x + 0.5f) / n * 2f - 1f;
            var dy = (y + 0.5f) / n * 2f - 1f;
            var r = Mathf.Sqrt(dx * dx + dy * dy);
            var core = 1f - Mathf.Clamp01(Mathf.InverseLerp(0.2f, 0.34f, r));
            var glow = Mathf.Pow(Mathf.Clamp01(1f - r), 1.5f);
            var hot = (1f - Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.16f, r))) * 0.7f; // (a white-hot middle)
            px[y * n + x] = new Color(1f, Mathf.Lerp(Red.g, 1f, hot), Mathf.Lerp(Red.b, 1f, hot), Mathf.Clamp01(core + glow * 0.9f));
        }

        t.SetPixels32(px);
        t.Apply(false, true);
        return t;
    }

    public static string Describe()
    {
        string One(int i) => Shown[i] ? $"on {OnWhat[i]} at {HitAt[i]:F2} beam {(Beams[i] != null && Beams[i].gameObject.activeSelf)} dot {(Dots[i] != null && Dots[i].gameObject.activeSelf)}" : "off";
        return $"LASER {Style} layer {_layer} | gun hand {One(0)} | left pistol {One(1)}{(_failed ? " | FAILED" : "")}";
    }
}
