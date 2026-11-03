using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Rendering;

/// <summary>
/// Blood decals in VR. The game's blood ("Realistic Blood") paints its splats onto walls and floors from a picture of
/// their depth that a helper camera (the decal prepass camera) draws once a frame, in mono, the size of the desktop
/// window, from the flat game's view. Each VR eye sees from its own place with its own lens, so the splats looked the
/// wrong depth up and were cut off or slid off the surfaces. Here that picture is drawn for each eye, from that eye's own
/// view, just before the eye is drawn (the mod draws the eyes one after the other), and the splats use it.
/// </summary>
internal static class BloodDecals
{
    private const string DepthName = "_LayerDecalDepthTexture";
    private static readonly int DepthId = Shader.PropertyToID(DepthName);
    private const float Scale = 0.5f; // of the eye's size (the game's own is half the window or less)

    private static readonly RenderTexture[] Depth = new RenderTexture[2];
    private static readonly int[] DrawnFrame = { -1, -1 };
    private static bool _hooked, _rendering;
    private static Camera _prepass, _taken;
    private static RenderTexture _gameTarget;

    /// <summary>Dev: false leaves the game's own (mono) way, to compare.</summary>
    public static bool On = true;

    public static void Init()
    {
        if (_hooked) return;
        _hooked = true;
        Camera.onPreCull += BeforeCull;
    }

    private static void BeforeCull(Camera cam)
    {
        if (_rendering || cam == null || !XrManager.IsRunning || CinemaRig.Active) return;
        if (!cam.stereoEnabled || cam.targetTexture != null) return;
        var active = cam.stereoActiveEye;
        if (active == Camera.MonoOrStereoscopicEye.Mono) return;
        if (!SingletonBehaviour<BloodCamera>.HasInstance) return;
        var blood = SingletonBehaviour<BloodCamera>.Instance;
        var prepass = blood.DecalPrepassCamera != null ? blood.DecalPrepassCamera.Camera : null;
        if (prepass == null) return;

        if (_taken != prepass)
        {
            _taken = prepass;
            _gameTarget = prepass.targetTexture;
        }

        if (!On)
        {
            // (the game's own way, as it was)
            if (!prepass.enabled)
            {
                prepass.enabled = true;
                prepass.ResetWorldToCameraMatrix();
                prepass.ResetProjectionMatrix();
                prepass.targetTexture = _gameTarget;
            }

            if (_gameTarget != null) Shader.SetGlobalTexture(DepthId, _gameTarget);
            return;
        }

        // the game's own mono drawing stops: this draws it per eye instead
        if (prepass.enabled) prepass.enabled = false;
        _prepass = prepass;
        var eye = active == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;
        if (DrawnFrame[eye] != Time.frameCount)
        {
            DrawnFrame[eye] = Time.frameCount;
            Draw(cam, eye);
        }

        Shader.SetGlobalTexture(DepthId, Depth[eye]);
    }

    private static void Draw(Camera cam, int eye)
    {
        var w = Mathf.Max(64, (int)(XRSettings.eyeTextureWidth * Scale));
        var h = Mathf.Max(64, (int)(XRSettings.eyeTextureHeight * Scale));
        var rt = Depth[eye];
        if (rt == null || rt.width != w || rt.height != h)
        {
            if (rt != null) Object.Destroy(rt);
            rt = Depth[eye] = new RenderTexture(w, h, 24, RenderTextureFormat.Depth) { name = "AngerFootVR blood depth " + (eye == 0 ? "L" : "R") };
            rt.Create();
            Object.DontDestroyOnLoad(rt);
        }

        var which = eye == 1 ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
        _prepass.stereoTargetEye = StereoTargetEyeMask.None;
        _prepass.worldToCameraMatrix = cam.GetStereoViewMatrix(which);
        _prepass.projectionMatrix = cam.GetStereoProjectionMatrix(which);
        _prepass.farClipPlane = cam.farClipPlane;
        _prepass.nearClipPlane = cam.nearClipPlane;
        _prepass.targetTexture = rt;
        _rendering = true;
        try
        {
            _prepass.Render();
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>Dev: how it's drawing.</summary>
    public static string Describe()
    {
        var blood = SingletonBehaviour<BloodCamera>.HasInstance ? SingletonBehaviour<BloodCamera>.Instance : null;
        var prepass = blood != null && blood.DecalPrepassCamera != null ? blood.DecalPrepassCamera.Camera : null;
        return $"BLOOD per-eye {On} | blood camera {(blood != null ? blood.name : "none")} prepass {(prepass != null ? $"enabled {prepass.enabled} mask {prepass.cullingMask:X} target {(prepass.targetTexture != null ? prepass.targetTexture.width + "x" + prepass.targetTexture.height : "none")}" : "none")} | eyes drawn L {DrawnFrame[0]} R {DrawnFrame[1]} (frame {Time.frameCount}) {(Depth[0] != null ? Depth[0].width + "x" + Depth[0].height : "")}";
    }
}
