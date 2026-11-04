using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AngerFootVR.Rendering;

/// <summary>
/// The game's full-screen effects, over your view in the headset (1.2): its fade from and to black (every level starts
/// with one, 3.5 s for a story level), the white-out of a blast close by, the death tint and the flash when you're hurt.
/// The game draws them on its HUD, which in VR is a panel out in front of you: a level's fade hung in the world there as
/// a black square, and a hit's flash was a small patch of colour down by the horizon that was easy to miss ("I can't even
/// tell if we took damage", a player's video). Now they're in front of your eyes, the way they cover a flat screen,
/// drawn only by your eyes (never the monitor's behind-you view); and a hit buzzes both hands.
/// </summary>
[HarmonyPatch]
internal static class ViewFx
{
    private static readonly string[] Names = { "_damageOverlay", "_deathOverlay", "_whiteoutOverlay", "_fadeOverlay" }; // (bottom to top)
    private static FieldInfo[] _fields;
    private static readonly MeshRenderer[] Quads = new MeshRenderer[4];
    private static readonly Material[] Mats = new Material[4];
    private static readonly Texture[] Textures = new Texture[4];
    private static Transform _root;
    private static Camera _eye;
    private static bool _hooked, _failed;
    private static readonly float[] Alpha = new float[4];
    private static int _lastFrame = -10;

    /// <summary>Dev: every camera draws them (to test the eyes-only switch).</summary>
    public static bool DevAllCameras;

    private const float Distance = 0.3f;


    [HarmonyPostfix]
    [HarmonyPatch(typeof(HUD), "Update")]
    private static void AfterHud(HUD __instance)
    {
        if (!XrManager.IsRunning || _failed || __instance == null) return;
        try
        {
            Mirror(__instance);
        }
        catch (System.Exception e)
        {
            _failed = true;
            Plugin.L.LogWarning("View effects: " + e.Message);
        }
    }

    private static void Mirror(HUD hud)
    {
        if (_fields == null)
        {
            _fields = new FieldInfo[Names.Length];
            for (var i = 0; i < Names.Length; i++) _fields[i] = AccessTools.Field(typeof(HUD), Names[i]);
        }

        // (drawn by the last of your eyes' cameras, the weapon camera: the effects camera draws its glowing smoke and
        // sparks after the main one, and they'd shine through a fade)
        var pc = SingletonBehaviour<PlayerCamera>.HasInstance ? SingletonBehaviour<PlayerCamera>.Instance : null;
        var cam = pc == null ? null : pc.WeaponCamera != null && pc.WeaponCamera.isActiveAndEnabled && pc.WeaponCamera.stereoEnabled ? pc.WeaponCamera : pc.MainCamera;
        if (cam == null || CinemaRig.Active)
        {
            Hide();
            return;
        }

        Ensure(cam);
        _lastFrame = Time.frameCount;
        for (var i = 0; i < Names.Length; i++)
        {
            if (!(_fields[i]?.GetValue(hud) is Image img) || img == null)
            {
                Alpha[i] = 0f;
                Quads[i].enabled = false;
                continue;
            }

            var c = img.color;
            var shown = img.isActiveAndEnabled ? c.a : 0f;
            img.canvasRenderer.SetAlpha(0f); // (not on the panel out in the world)
            Alpha[i] = shown;
            if (Textures[i] == null)
            {
                Textures[i] = img.sprite != null && img.sprite.texture != null ? img.sprite.texture : Texture2D.whiteTexture;
                Mats[i].mainTexture = Textures[i];
                if (img.sprite != null && img.sprite.texture != null)
                {
                    // (a sprite on an atlas: just its own part)
                    var r = img.sprite.textureRect;
                    var t = img.sprite.texture;
                    var scale = new Vector2(r.width / t.width, r.height / t.height);
                    var offset = new Vector2(r.x / t.width, r.y / t.height);
                    if (i == 0) t.wrapMode = TextureWrapMode.Clamp; // (its edge colour carried on out past it)
                    Mats[i].mainTextureScale = scale;
                    Mats[i].mainTextureOffset = offset;
                }
            }

            Mats[i].color = new Color(c.r, c.g, c.b, shown);
            Quads[i].enabled = shown > 0.003f;
        }

        // the hit's flash: its vignette over the middle of your view (a screen's edges are all in view, an eye's aren't:
        // drawn a little inside them), its edge colour carried on out to cover the rest, so no edge of it ever shows
        var h = 2f * Distance * Mathf.Tan(Mathf.Clamp(cam.fieldOfView, 60f, 130f) * 0.5f * Mathf.Deg2Rad) * 0.9f;
        var w = h * Mathf.Clamp(cam.aspect, 0.7f, 2f);
        var big = Quads[0].transform.localScale.x;
        var sc = new Vector2(big / w, big / h);
        Mats[0].mainTextureScale = sc;
        Mats[0].mainTextureOffset = new Vector2(0.5f - 0.5f * sc.x, 0.5f - 0.5f * sc.y);
    }

    private static void Ensure(Camera cam)
    {
        if (_root == null)
        {
            _root = new GameObject("AngerFootVR view effects").transform;
            Object.DontDestroyOnLoad(_root.gameObject);
            var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            for (var i = 0; i < Quads.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = Names[i];
                Object.Destroy(q.GetComponent<Collider>());
                // (the UI shader takes the mesh's own colours too: the plain quad has none, so it drew nothing)
                var mf = q.GetComponent<MeshFilter>();
                var mesh = Object.Instantiate(mf.sharedMesh);
                var white = new Color[mesh.vertexCount];
                for (var k = 0; k < white.Length; k++) white[k] = Color.white;
                mesh.colors = white;
                mf.sharedMesh = mesh;
                q.layer = UI.VrUi.Layer;
                q.transform.SetParent(_root, false);
                q.transform.localPosition = new Vector3(0f, 0f, Distance - i * 0.002f);
                q.transform.localScale = new Vector3(2.6f, 2.6f, 1f); // (wider than any eye sees)
                var m = new Material(shader) { name = "AngerFootVR " + Names[i], renderQueue = 5000 + i };
                m.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
                Mats[i] = m;
                Quads[i] = q.GetComponent<MeshRenderer>();
                Quads[i].sharedMaterial = m;
                Quads[i].shadowCastingMode = ShadowCastingMode.Off;
                Quads[i].receiveShadows = false;
                Quads[i].enabled = false;
            }
        }

        if (_root.parent != cam.transform)
        {
            _root.SetParent(cam.transform, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            for (var i = 0; i < Textures.Length; i++) Textures[i] = null; // (a new level's HUD: its own sprites)
        }

        _eye = cam;
        var layer = UI.VrUi.Layer;
        for (var l = 0; l < 32; l++)
            if ((cam.cullingMask & (1 << l)) != 0)
            {
                layer = l;
                break;
            }

        for (var i = 0; i < Quads.Length; i++)
            if (Quads[i].gameObject.layer != layer)
                Quads[i].gameObject.layer = layer;
        if (!_hooked)
        {
            _hooked = true;
            Camera.onPreCull += OnlyTheEyes;
        }
    }

    /// <summary>Only your eyes see them (the monitor's own cameras, the scope, the behind-you view don't).</summary>
    private static void OnlyTheEyes(Camera cam)
    {
        if (_root == null) return;
        // (only while the game's HUD is there to say so: a level ending mid-fade never leaves your view black)
        var eye = (cam == _eye || DevAllCameras) && Time.frameCount - _lastFrame <= 2;
        for (var i = 0; i < Quads.Length; i++)
            if (Quads[i] != null)
                Quads[i].forceRenderingOff = !eye;
    }

    private static void Hide()
    {
        for (var i = 0; i < Quads.Length; i++)
        {
            Alpha[i] = 0f;
            if (Quads[i] != null) Quads[i].enabled = false;
        }
    }

    /// <summary>A hit on you buzzes both hands (you feel it as well as see it).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Health), nameof(Health.OnDamageTaken))]
    private static void Hit(Health __instance)
    {
        if (!XrManager.IsRunning || __instance == null || !__instance.IsPlayerHealth || CinemaRig.Active) return;
        VrControls.Rumble(UnityEngine.XR.XRNode.LeftHand, 0.7f, 0.12f);
        VrControls.Rumble(UnityEngine.XR.XRNode.RightHand, 0.7f, 0.12f);
    }

    /// <summary>Dev: the hit's flash as the game shows it (as if hurt), or the fade at some alpha.</summary>
    public static string DevFlash(string what)
    {
        var hud = Object.FindObjectOfType<HUD>();
        if (hud == null) return "no HUD";
        if (what == "all") DevAllCameras = !DevAllCameras;
        else if (what == "fade") hud.FadeScreen(HUD.FadeMode.FadeFromBlack, 3f, 0.5f);
        else AccessTools.Field(typeof(HUD), "_damageOverlayAlpha")?.SetValue(hud, 0.5f);
        return Describe();
    }

    public static string Quad(int i)
    {
        var q = Quads[i];
        if (q == null) return "none";
        var cam = _eye;
        var local = cam != null ? cam.transform.InverseTransformPoint(q.transform.position) : Vector3.zero;
        return $"{Names[i]} on {q.enabled} forced off {q.forceRenderingOff} seen {q.isVisible} at {local:F2} size {q.transform.lossyScale:F2} layer {q.gameObject.layer} colour {Mats[i].color} shader {Mats[i].shader.name} queue {Mats[i].renderQueue}";
    }

    public static string Describe() =>
        $"VIEW FX damage {Alpha[0]:F2} death {Alpha[1]:F2} white {Alpha[2]:F2} fade {Alpha[3]:F2} | on the eye camera {(_root != null && _root.parent != null ? _root.parent.name : "none")} | damage texture {(Textures[0] != null ? Textures[0].name + " " + Textures[0].width + "x" + Textures[0].height : "none")}";
}
