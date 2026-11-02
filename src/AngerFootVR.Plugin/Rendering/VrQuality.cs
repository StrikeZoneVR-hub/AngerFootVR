using System;
using AngerFootVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace AngerFootVR.Rendering;

/// <summary>
/// The VR graphics profile: in the headset every screen effect runs for two eyes at the headset's resolution, so a few
/// of the game's settings are turned down while VR runs, applied over the game's own each time it applies them and
/// never written to its Settings.json (the player's flat-screen settings stay as they are).
/// - Ambient occlusion (Amplify Occlusion) off: the heaviest effect, twice over in VR.
/// - Textures one step down on GPUs with 6 GB of video memory or less (tried in full on a 6 GB laptop card: a level's
///   load took free video memory down to 1.5 GB with the eyes and Windows' share, too close with a headset's streamer
///   also on the card).
/// - Chromatic aberration off (it smears at the lens edges in a headset).
/// </summary>
[HarmonyPatch]
internal static class VrQuality
{
    public static bool AmbientOcclusion = Environment.GetEnvironmentVariable("AFVR_AO") == "1";

    /// <summary>The lowest texture mip the game may use in VR (0 = full, 1 = half).</summary>
    public static int TextureLimit = int.TryParse(Environment.GetEnvironmentVariable("AFVR_TEXLIMIT"), out var t)
        ? t
        : SystemInfo.graphicsMemorySize <= 6144 ? 1 : 0;

    private static bool InVr => XrManager.IsRunning;

    /// <summary>Called when the VR session starts, so the profile applies at once.</summary>
    public static void OnVrStarted()
    {
        ApplyTextures();
        Plugin.L.LogInfo($"VR graphics profile: ambient occlusion {(AmbientOcclusion ? "on" : "off")}, texture limit {QualitySettings.masterTextureLimit}, chromatic aberration off");
    }

    private static void ApplyTextures()
    {
        if (InVr && QualitySettings.masterTextureLimit < TextureLimit) QualitySettings.masterTextureLimit = TextureLimit;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameSettings), nameof(GameSettings.Apply))]
    private static void AfterSettingsApply()
    {
        ApplyTextures();
        FramePacing();
    }

    /// <summary>In VR the headset paces the frames: the game's own frame cap and v-sync (its settings, for the monitor)
    /// are set aside while it runs (never saved). The test runtime doesn't pace, so it gets a 90 fps cap instead.</summary>
    public static void FramePacing()
    {
        if (!Core.XrManager.IsRunning && Core.XrManager.Current != Core.XrManager.State.WaitingForSession) return;
        var mock = Core.XrManager.RuntimeName.IndexOf("Mock", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
        var cap = DevCap ?? (mock ? 90 : -1);
        if (Application.targetFrameRate != cap) Application.targetFrameRate = cap;
    }

    /// <summary>Dev: a frame cap of its own (the `cap` command), kept over the game's.</summary>
    public static int? DevCap;

    private static float _nextPacing;
    private static readonly System.Diagnostics.Stopwatch Frame = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Once a second while VR runs: the pacing again (the game's own frame limiter sets the cap when it likes).
    /// On the test runtime, which doesn't pace frames and whose frames Unity won't cap in VR, each frame is held to
    /// 90 fps here (dev runs only: a headset's runtime paces them itself).</summary>
    public static void Update()
    {
        if (InVr && Core.XrManager.RuntimeName.IndexOf("Mock", System.StringComparison.OrdinalIgnoreCase) >= 0 && DevCap != -1)
        {
            var left = 1000.0 / (DevCap ?? 90) - Frame.Elapsed.TotalMilliseconds;
            if (left > 1) System.Threading.Thread.Sleep((int)left);
            Frame.Restart();
        }

        if (!InVr || Time.unscaledTime < _nextPacing) return;
        _nextPacing = Time.unscaledTime + 1f;
        FramePacing();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerCamera), "Update")]
    private static void AfterCameraUpdate(PlayerCamera __instance)
    {
        if (!InVr || AmbientOcclusion) return;
        var occlusion = Traverse.Create(__instance).Field("_occulsion").GetValue<Behaviour>();
        if (occlusion != null && occlusion.enabled) occlusion.enabled = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerCameraPostprocessVolume), "ApplyGameSettings")]
    private static void AfterVolumeSettings(PostProcessVolume ____volume)
    {
        if (!InVr || ____volume == null || ____volume.sharedProfile == null) return;
        if (____volume.sharedProfile.TryGetSettings<ChromaticAberration>(out var ca)) ca.enabled.value = false;
    }
}
