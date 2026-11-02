using System.Collections.Generic;
using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace AngerFootVR.Rendering;

/// <summary>
/// MONITOR VIEW (STEADY by default, or EYE): what the monitor shows while you play (InkVR's, which is tested). EYE is the
/// headset's left eye as it is, every twitch of your head with it. STEADY is for whoever's watching, a recording or a
/// stream: a level 16:9 view, a little narrower than the eye's, that follows your head smoothly (a quick glance or a nod
/// moves it gently, it never tilts with your head) and fills the window.
/// It costs no second render of the scene: the left eye's finished picture is copied once a frame as it's drawn, and the
/// monitor shows a window onto it, turned back by however far the smoothed view lags your head. MONITOR FOV (70 to 110
/// degrees across, 90 by default) sets how wide; what the eye sees past that is the smoothing's room. Nothing changes in
/// the headset. If no eye picture comes, the plain eye mirror is put back.
/// </summary>
internal static class SpectatorView
{
    private const float MaxLag = 10f, Follow = 4.5f;

    private static RenderTexture _eye;
    private static CommandBuffer _copy;
    private static Material _mat;
    private static readonly HashSet<Camera> Attached = new HashSet<Camera>();
    private static bool _on, _hooked, _failed;
    private static int _copiedFrame = -1;
    private static float _since;
    private static Matrix4x4 _eyeView, _eyeProj;
    private static Quaternion _smooth = Quaternion.identity;
    private static bool _haveSmooth;
    private static float _shownFov, _room;

    private static string _angles = "";
    private static readonly Vector2[] _probe = new Vector2[4];

    public static string State => $"probe left {_probe[0]} mid {_probe[1]} right {_probe[2]} screen {Screen.width}x{Screen.height} smooth {_smooth.eulerAngles} | eye fov {_angles} | monitor view {(_on ? "steady" : "eye")}{(_failed ? " (fell back to the eye: no eye picture)" : "")}, {_shownFov:F0} deg across (asked {VrSettings.MonitorFov.Value:F0}), smoothing room {_room:F1} deg, copied frame {_copiedFrame} (now {Time.frameCount})";

    public static void Update()
    {
        var want = XrManager.IsRunning && VrSettings.Monitor.Value == VrSettings.MonitorView.Steady && !_failed;
        if (want && !_on) Start();
        else if (!want && _on) Stop();
        if (!_on) return;
        if (XRSettings.gameViewRenderMode != GameViewRenderMode.None) XRSettings.gameViewRenderMode = GameViewRenderMode.None; // (the display resets it when it starts)

        // no eye picture for a couple of seconds: the plain mirror comes back
        if (Time.frameCount - _copiedFrame > 3)
        {
            if (Time.unscaledTime - _since > 2f)
            {
                Plugin.L.LogWarning("Monitor view: no picture from the eye; showing the eye as it is");
                _failed = true;
                Stop();
            }
        }
        else _since = Time.unscaledTime;
    }

    private static void Start()
    {
        _on = true;
        _since = Time.unscaledTime;
        if (!_hooked)
        {
            _hooked = true;
            Camera.onPreRender += OnPreRender;
        }

        if (_mat == null)
        {
            _mat = new Material(Shader.Find("UI/Default")) { name = "AngerFootVR Monitor View" };
            Object.DontDestroyOnLoad(_mat);
            Plugin.Instance.StartCoroutine(EndOfFrames());
        }

        XRSettings.gameViewRenderMode = GameViewRenderMode.None;
        Plugin.L.LogInfo("Monitor view: steady");
    }

    private static void Stop()
    {
        _on = false;
        foreach (var c in Attached)
            if (c != null && _copy != null)
                c.RemoveCommandBuffer(CameraEvent.AfterEverything, _copy);
        Attached.Clear();
        XRSettings.gameViewRenderMode = GameViewRenderMode.LeftEye;
        _haveSmooth = false;
    }

    /// <summary>Each camera, each eye: the left eye's cameras copy their picture when they're done (the last to draw
    /// leaves the finished one); the right eye's don't.</summary>
    private static void OnPreRender(Camera cam)
    {
        if (!_on || cam == null) return;
        if (!cam.stereoEnabled || cam.targetTexture != null) return;
        var left = cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left;
        var has = Attached.Contains(cam);
        if (left)
        {
            EnsureEyeTexture();
            if (!has)
            {
                cam.AddCommandBuffer(CameraEvent.AfterEverything, _copy);
                Attached.Add(cam);
            }

            _eyeView = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            _eyeProj = cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            _copiedFrame = Time.frameCount;
        }
        else if (has)
        {
            cam.RemoveCommandBuffer(CameraEvent.AfterEverything, _copy);
            Attached.Remove(cam);
        }
    }

    private static void EnsureEyeTexture()
    {
        var w = Mathf.Max(64, XRSettings.eyeTextureWidth);
        var h = Mathf.Max(64, XRSettings.eyeTextureHeight);
        if (_eye != null && _eye.width == w && _eye.height == h) return;
        if (_eye != null) _eye.Release();
        _eye = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "AngerFootVR Monitor Eye" };
        _eye.Create();
        _copy ??= new CommandBuffer { name = "AngerFootVR monitor copy" };
        _copy.Clear();
        _copy.Blit(BuiltinRenderTextureType.CameraTarget, _eye);
    }

    /// <summary>The monitor's view: where each of its corners looks, smoothed and level, found in the eye's picture.</summary>
    private static void Show()
    {
        if (_eye == null) return;
        var eyeToWorld = _eyeView.inverse;
        var fwd = eyeToWorld.MultiplyVector(Vector3.back); // (view space looks down -z)
        if (fwd.sqrMagnitude < 1e-6f) return;
        var dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        // how wide: as asked, but never wider than the eye sees; the rest is the smoothing's room
        var aspect = Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1, Screen.height));
        var eyeLeft = Mathf.Atan(Mathf.Abs((_eyeProj.m02 - 1f) / _eyeProj.m00)) * Mathf.Rad2Deg;
        var eyeRight = Mathf.Atan(Mathf.Abs((_eyeProj.m02 + 1f) / _eyeProj.m00)) * Mathf.Rad2Deg;
        var eyeDown = Mathf.Atan(Mathf.Abs((_eyeProj.m12 - 1f) / _eyeProj.m11)) * Mathf.Rad2Deg;
        var eyeUp = Mathf.Atan(Mathf.Abs((_eyeProj.m12 + 1f) / _eyeProj.m11)) * Mathf.Rad2Deg;
        // (an eye sees further to its outer side than towards the nose: the view is centred on the middle of what it
        // sees, so the room is the same either side)
        _angles = $"L{eyeLeft:F1} R{eyeRight:F1} U{eyeUp:F1} D{eyeDown:F1} m02 {_eyeProj.m02:F3} m12 {_eyeProj.m12:F3} fwd {fwd} headFwd {CinemaRig.HeadRotation * Vector3.forward}";
        var eyeHalfH = (eyeLeft + eyeRight) * 0.5f;
        var eyeHalfV = (eyeDown + eyeUp) * 0.5f;
        var level = Quaternion.LookRotation(fwd.normalized, Vector3.up) * Quaternion.Euler(-(eyeUp - eyeDown) * 0.5f, -(eyeLeft - eyeRight) * 0.5f, 0f);
        var halfH = Mathf.Min(VrSettings.MonitorFov.Value * 0.5f, eyeHalfH - 0.5f);
        var halfV = Mathf.Atan(Mathf.Tan(halfH * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
        if (halfV > eyeHalfV - 0.5f)
        {
            halfV = eyeHalfV - 0.5f;
            halfH = Mathf.Atan(Mathf.Tan(halfV * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
        }

        var room = Mathf.Clamp(Mathf.Min(eyeHalfH - halfH, eyeHalfV - halfV) - 0.5f, 0f, MaxLag);
        if (!_haveSmooth)
        {
            _smooth = level;
            _haveSmooth = true;
        }
        else
        {
            _smooth = Quaternion.Slerp(_smooth, level, 1f - Mathf.Exp(-dt * Follow));
            var lag = Quaternion.Angle(_smooth, level);
            if (lag > room) _smooth = room <= 0.01f ? level : Quaternion.Slerp(level, _smooth, room / lag);
        }

        var tanH = Mathf.Tan(halfH * Mathf.Deg2Rad);
        var tanV = Mathf.Tan(halfV * Mathf.Deg2Rad);
        _shownFov = halfH * 2f;
        _room = room;
        // onto the window, over everything (the frame's last thing), as a grid so the picture's perspective holds
        // (one quad would bend it: a view turned from the eye's is a keystone in the eye's picture)
        RenderTexture.active = null;
        GL.Viewport(new Rect(0, 0, Screen.width, Screen.height)); // (it's still the eye's from its last camera)
        GL.PushMatrix();
        GL.LoadOrtho();
        _mat.mainTexture = _eye;
        _mat.SetPass(0);
        GL.Begin(GL.QUADS);
        GL.Color(Color.white);
        const int nx = 16, ny = 9;
        for (var gx = 0; gx < nx; gx++)
        for (var gy = 0; gy < ny; gy++)
        {
            Vertex(gx / (float)nx, gy / (float)ny, tanH, tanV);
            Vertex((gx + 1) / (float)nx, gy / (float)ny, tanH, tanV);
            Vertex((gx + 1) / (float)nx, (gy + 1) / (float)ny, tanH, tanV);
            Vertex(gx / (float)nx, (gy + 1) / (float)ny, tanH, tanV);
        }

        GL.End();
        GL.PopMatrix();
    }

    /// <summary>A point of the window (0..1 across and up) and where it looks in the eye's picture.</summary>
    private static void Vertex(float wx, float wy, float tanH, float tanV)
    {
        // (the eye picture is stored upside down: the window's top reads from its bottom)
        var world = _smooth * new Vector3((wx * 2f - 1f) * tanH, (1f - wy * 2f) * tanV, 1f);
        var v = _eyeView.MultiplyVector(world);
        var z = Mathf.Max(1e-4f, -v.z);
        var nx = _eyeProj.m00 * (v.x / z) - _eyeProj.m02;
        var ny = _eyeProj.m11 * (v.y / z) - _eyeProj.m12;
        if (wy == 0.5f || (wy > 0.44f && wy < 0.45f)) _probe[wx <= 0f ? 0 : wx >= 1f ? 2 : wx == 0.5f ? 1 : 3] = new Vector2(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f);
        GL.TexCoord2(nx * 0.5f + 0.5f, ny * 0.5f + 0.5f);
        GL.Vertex3(wx, wy, 0f);
    }

    private static System.Collections.IEnumerator EndOfFrames()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            if (_on && Time.frameCount - _copiedFrame <= 1) Show();
        }
    }

    /// <summary>Dev: the eye picture as copied, to a PNG.</summary>
    public static string Dump(string path)
    {
        if (_eye == null) return "no eye picture";
        var prev = RenderTexture.active;
        RenderTexture.active = _eye;
        var t = new Texture2D(_eye.width, _eye.height, TextureFormat.RGBA32, false);
        t.ReadPixels(new Rect(0, 0, _eye.width, _eye.height), 0, 0);
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        Object.Destroy(t);
        return path;
    }
}
