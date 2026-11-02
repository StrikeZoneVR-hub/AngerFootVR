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
/// - Textures one step down on GPUs with 6 GB of video memory or less.
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
    private static void AfterSettingsApply() => ApplyTextures();

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
