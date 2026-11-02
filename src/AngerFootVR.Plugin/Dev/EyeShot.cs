using System;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev ("eyeshot [name]" command): copies what each eye of the stereo game camera was drawn into, right after it's
/// drawn (in multi-pass the camera renders once per eye), and saves both as PNGs a frame later.
/// </summary>
internal static class EyeShot
{
    private static string _name;
    private static int _armedFrame = -1;
    private static readonly RenderTexture[] Copies = new RenderTexture[2];
    private static CommandBuffer _copy;

    public static void Arm(string name)
    {
        _name = name;
        _armedFrame = Time.frameCount + 1;
        Camera.onPostRender -= OnPostRender;
        Camera.onPostRender += OnPostRender;
        Plugin.Instance.StartCoroutine(SaveAfterFrame());
    }

    /// <summary>Each stereo camera that draws into the eyes in the armed frame copies them (the last one, the weapon
    /// camera in play, leaves the whole picture, the gun included).</summary>
    private static void OnPostRender(Camera cam)
    {
        if (_armedFrame < 0 || Time.frameCount != _armedFrame || !cam.stereoEnabled || cam.targetTexture != null) return;
        var eye = cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;
        if (Copies[eye] == null)
            Copies[eye] = new RenderTexture(XRSettingsWidth(), XRSettingsHeight(), 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        _copy ??= new CommandBuffer { name = "AngerFootVR eyeshot" };
        _copy.Clear();
        _copy.Blit(BuiltinRenderTextureType.CurrentActive, Copies[eye]);
        Graphics.ExecuteCommandBuffer(_copy);
    }

    private static System.Collections.IEnumerator SaveAfterFrame()
    {
        while (Time.frameCount <= _armedFrame) yield return new WaitForEndOfFrame();
        _armedFrame = -1;
        Camera.onPostRender -= OnPostRender;
        if (Copies[0] != null && Copies[1] != null) yield return Save();
    }

    private static int XRSettingsWidth() => Math.Max(16, UnityEngine.XR.XRSettings.eyeTextureWidth);
    private static int XRSettingsHeight() => Math.Max(16, UnityEngine.XR.XRSettings.eyeTextureHeight);

    private static System.Collections.IEnumerator Save()
    {
        yield return null;
        var dir = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "shots"));
        Directory.CreateDirectory(dir);
        for (var e = 0; e < 2; e++)
        {
            var rt = Copies[e];
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = prev;
            var file = Path.Combine(dir, $"{_name}_{(e == 0 ? "left" : "right")}_{DateTime.Now:HHmmss}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            Copies[e] = null;
            Plugin.L.LogInfo($"eyeshot {(e == 0 ? "left" : "right")}: {file}");
        }
    }
}
