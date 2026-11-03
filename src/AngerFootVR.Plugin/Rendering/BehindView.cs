using System.Reflection;
using AngerFootVR.Core;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.XR;

namespace AngerFootVR.Rendering;

/// <summary>
/// MONITOR VIEW: BEHIND YOU. Whoever's watching sees you from over your shoulder: your whole body (head and all, the
/// mirror's twin), your gun, the kicks, the room round you. A camera a couple of metres behind and above your head,
/// following you smoothly (it never shakes with your head, it swings round gently when you turn), pulled in where a
/// wall would come between. Only in a level and the movie house (where there's a body); elsewhere the monitor shows the
/// STEADY view. Nothing changes in the headset. It's a second picture of the scene, so it's kept light (no wider than
/// 1280 pixels, nearer shadows, simpler far away).
/// </summary>
internal static class BehindView
{
    private const float Back = 2.1f, Up = 0.55f, Fov = 72f;
    private static readonly int WallMask = LayerMasks.WallLayers | LayerMasks.Floor | LayerMasks.Door | LayerMasks.Roof | LayerMasks.Fence;
    private static readonly FieldInfo Resources = AccessTools.Field(typeof(PostProcessLayer), "m_Resources");

    private static Camera _cam;
    private static PostProcessLayer _post;
    private static RenderTexture _rt;
    private static Material _mat;
    private static bool _on, _hooked, _have, _postFailed, _failed;
    private static Vector3 _pos;
    private static Quaternion _rot = Quaternion.identity;
    private static int _drawnFrame = -10;
    private static float _pulled;

    /// <summary>The setting asks for it and there's a body to see (a level, or the movie house).</summary>
    public static bool Usable =>
        XrManager.IsRunning && VrSettings.Monitor.Value == VrSettings.MonitorView.Behind && !_failed && Body.VrBody.Active && !MapRig.Active &&
        (CinemaRig.Active ? CinemaRig.Camera != null : SingletonBehaviour<PlayerCamera>.HasInstance && SingletonBehaviour<Player>.HasInstance);

    public static string State => $"behind view {(_on ? "on" : "off")} (usable {Usable}) at {_pos:F2} pulled in {_pulled:F2} m, drawn frame {_drawnFrame} (now {Time.frameCount}), post {(_post != null && _post.enabled ? "on" : _postFailed ? "failed" : "off")}, picture {(_rt != null ? $"{_rt.width}x{_rt.height}" : "none")}";

    public static void Update()
    {
        var want = Usable;
        if (want && !_on) Start();
        else if (!want && _on) Stop();
        if (_on && XRSettings.gameViewRenderMode != GameViewRenderMode.None) XRSettings.gameViewRenderMode = GameViewRenderMode.None;
    }

    private static void Start()
    {
        _on = true;
        _have = false;
        if (_mat == null)
        {
            // (a plain copy, no blending: the picture's alpha after the game's post-processing isn't 1 everywhere)
            _mat = new Material(Shader.Find("Hidden/BlitCopy") ?? Shader.Find("UI/Default")) { name = "AngerFootVR Behind View" };
            Object.DontDestroyOnLoad(_mat);
        }

        if (!_hooked)
        {
            _hooked = true;
            Plugin.Instance.StartCoroutine(EndOfFrames());
        }

        XRSettings.gameViewRenderMode = GameViewRenderMode.None;
        Plugin.L.LogInfo("Monitor view: behind you");
    }

    private static void Stop()
    {
        _on = false;
        XRSettings.gameViewRenderMode = GameViewRenderMode.LeftEye; // (the steady view takes the window back if it's wanted)
    }

    private static void Ensure()
    {
        var w = Mathf.Min(Screen.width, 1280);
        var h = Mathf.Max(64, Mathf.RoundToInt(w * (float)Screen.height / Mathf.Max(1, Screen.width)));
        w = Mathf.Max(64, w);
        if (_rt == null || _rt.width != w || _rt.height != h)
        {
            if (_rt != null) _rt.Release();
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "AngerFootVR Behind View" };
            _rt.Create();
        }

        if (_cam != null) return;
        var go = new GameObject("AngerFootVR Behind Camera");
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);
        _cam = go.AddComponent<Camera>();
        _cam.enabled = false;
        _cam.stereoTargetEye = StereoTargetEyeMask.None;
        _cam.allowHDR = true;
        _cam.allowMSAA = false;
        _cam.fieldOfView = Fov;
        _cam.nearClipPlane = 0.05f;
        go.SetActive(true);
    }

    /// <summary>The game's colour and glow, as the eyes have them: the level's own post-processing (its layer is on the
    /// last of the player's cameras, the weapon camera, and grades them all), on this camera too.</summary>
    private static void TryPost(PlayerCamera pc)
    {
        if (_post != null || _postFailed || _cam == null || Resources == null) return;
        PostProcessLayer theirs = null;
        foreach (var c in new[] { pc.WeaponCamera, pc.MainCamera, pc.EffectsCamera })
            if (c != null && (theirs = c.GetComponent<PostProcessLayer>()) != null)
                break;
        if (theirs == null || !(Resources.GetValue(theirs) is PostProcessResources res)) return;
        var go = _cam.gameObject;
        try
        {
            go.SetActive(false); // (its resources set before it wakes)
            _post = go.AddComponent<PostProcessLayer>();
            Resources.SetValue(_post, res);
            _post.volumeLayer = theirs.volumeLayer;
            _post.volumeTrigger = go.transform;
            _post.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
            Plugin.L.LogInfo($"Behind view: the game's post-processing on (from '{theirs.name}')");
        }
        catch (System.Exception e)
        {
            _postFailed = true;
            if (_post != null) Object.Destroy(_post);
            _post = null;
            Plugin.L.LogWarning("Behind view: no post-processing (" + e.Message + ")");
        }
        finally
        {
            go.SetActive(true);
        }
    }

    /// <summary>Where the camera goes: behind your head the way you face, a little above, looking a little past you.</summary>
    private static void Place(Vector3 head, Quaternion headRot, bool cinema)
    {
        var flat = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(headRot * Vector3.up, Vector3.up);
        flat.Normalize();
        var scale = cinema ? 1f : Mathf.Max(0.5f, head.y - (SingletonBehaviour<Player>.Instance != null ? SingletonBehaviour<Player>.Instance.transform.position.y : head.y - 1.7f)) / 1.7f;
        scale = Mathf.Clamp(scale, 0.6f, 1.6f);
        var pivot = head + Vector3.up * 0.1f * scale;
        var want = pivot - flat * Back * scale + Vector3.up * Up * scale;
        // a wall between: in front of it
        var to = want - pivot;
        var dist = to.magnitude;
        var reach = dist;
        if (cinema)
        {
            // (the movie house's rooms are boxes: kept inside the one you're in)
            for (var k = 12; k >= 0; k--)
            {
                reach = Mathf.Max(0.3f, dist * k / 12f);
                if (InRoom(pivot + to / dist * reach)) break;
            }

        }
        else if (Physics.SphereCast(pivot, 0.2f, to / dist, out var hit, dist, WallMask, QueryTriggerInteraction.Ignore))
            reach = Mathf.Max(0.3f, hit.distance - 0.05f);
        _pulled = dist - reach;
        want = pivot + to / dist * reach;
        var look = Quaternion.LookRotation(pivot + flat * 1.6f * scale - want, Vector3.up);
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        if (!_have || (_pos - want).sqrMagnitude > 25f)
        {
            _pos = want;
            _rot = look;
            _have = true;
        }
        else
        {
            // (pulled in by a wall: straight there, or the wall comes between for a moment)
            _pos = _pulled > 0.05f && (want - pivot).sqrMagnitude < (_pos - pivot).sqrMagnitude ? want : Vector3.Lerp(_pos, want, 1f - Mathf.Exp(-dt * 7f));
            _rot = Quaternion.Slerp(_rot, look, 1f - Mathf.Exp(-dt * 6f));
        }
    }

    private static bool InRoom(Vector3 world)
    {
        var p = UI.MovieHouse.Root != null ? UI.MovieHouse.Root.InverseTransformPoint(world) : world - UI.MovieHouse.Origin;
        const float m = 0.25f;
        static bool In(Vector3 q, Vector3 lo, Vector3 hi) => q.x > lo.x + m && q.x < hi.x - m && q.z > lo.z + m && q.z < hi.z - m && q.y > 0.3f && q.y < hi.y - m;
        return In(p, new Vector3(-6.9f, 0f, -5.95f), new Vector3(6.9f, 5.5f, 12.95f)) || In(p, UI.MovieHouse.RageMin, UI.MovieHouse.RageMax);
    }

    private static void Draw()
    {
        var cinema = CinemaRig.Active;
        Camera source;
        Vector3 head;
        Quaternion headRot;
        int mask;
        if (cinema)
        {
            source = CinemaRig.Camera;
            head = CinemaRig.HeadPosition;
            headRot = CinemaRig.HeadRotation;
            mask = source.cullingMask;
        }
        else
        {
            var pc = SingletonBehaviour<PlayerCamera>.Instance;
            source = pc.MainCamera;
            head = VrRig.HeadPosition;
            headRot = VrRig.HeadRotation;
            mask = source.cullingMask;
            if (pc.WeaponCamera != null) mask |= pc.WeaponCamera.cullingMask;
            if (pc.EffectsCamera != null) mask |= pc.EffectsCamera.cullingMask;
        }

        if (source == null) return;
        Ensure();
        if (!cinema) TryPost(SingletonBehaviour<PlayerCamera>.Instance);
        Place(head, headRot, cinema);
        _cam.transform.SetPositionAndRotation(_pos, _rot);
        _cam.cullingMask = mask | (1 << UI.VrUi.Layer) | (1 << Body.VrBody.MirrorLayer);
        _cam.clearFlags = source.clearFlags == CameraClearFlags.Nothing || source.clearFlags == CameraClearFlags.Depth ? CameraClearFlags.SolidColor : source.clearFlags;
        _cam.backgroundColor = source.backgroundColor;
        _cam.renderingPath = source.renderingPath;
        _cam.allowHDR = !cinema && source.allowHDR;
        _cam.farClipPlane = cinema ? 60f : Mathf.Min(source.farClipPlane, 150f);
        _cam.aspect = _rt.width / (float)_rt.height;
        if (_post != null) _post.enabled = !cinema;
        _cam.targetTexture = _rt;
        // (a lighter picture than the eyes': shadows nearer, fewer cascades and lamps, simpler far models; all put back
        // straight after, so the eyes never see it)
        var shadowDist = QualitySettings.shadowDistance;
        var cascades = QualitySettings.shadowCascades;
        var lod = QualitySettings.lodBias;
        var lamps = QualitySettings.pixelLightCount;
        QualitySettings.shadowDistance = Mathf.Min(shadowDist, 25f);
        QualitySettings.shadowCascades = Mathf.Min(cascades, 2);
        QualitySettings.lodBias = Mathf.Min(lod, 1.5f);
        QualitySettings.pixelLightCount = Mathf.Min(lamps, 3);
        Body.VrBody.ForBehindView(true);
        try
        {
            _cam.Render();
        }
        finally
        {
            Body.VrBody.ForBehindView(false);
            QualitySettings.shadowDistance = shadowDist;
            QualitySettings.shadowCascades = cascades;
            QualitySettings.lodBias = lod;
            QualitySettings.pixelLightCount = lamps;
        }

        _drawnFrame = Time.frameCount;
    }

    /// <summary>Onto the window, at the frame's end (after the eyes).</summary>
    private static void Show()
    {
        if (_rt == null) return;
        RenderTexture.active = null;
        GL.Viewport(new Rect(0, 0, Screen.width, Screen.height));
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Clear(true, true, Color.black);
        _mat.mainTexture = _rt;
        _mat.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(Color.white);
        GL.TexCoord2(0f, 0f);
        GL.Vertex3(0f, 0f, 0f);
        GL.TexCoord2(1f, 0f);
        GL.Vertex3(1f, 0f, 0f);
        GL.TexCoord2(1f, 1f);
        GL.Vertex3(1f, 1f, 0f);
        GL.TexCoord2(0f, 1f);
        GL.Vertex3(0f, 1f, 0f);
        GL.End();
        GL.PopMatrix();
    }

    private static System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            if (!_on) continue;
            try
            {
                // (every frame: a picture every other frame made every other frame slower, and the headset feels uneven
                // frames more than slow ones)
                Draw();
                Show();
            }
            catch (System.Exception e)
            {
                Plugin.L.LogWarning("Behind view: " + e.Message);
                _failed = true; // (the steady view takes over; the setting stays as you chose it)
                Stop();
            }
        }
    }
}
