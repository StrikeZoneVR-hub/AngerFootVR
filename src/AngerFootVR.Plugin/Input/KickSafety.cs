using System;
using HarmonyLib;

namespace AngerFootVR.Input;

/// <summary>
/// Kicks keep working, whatever they hit. The game's kick is a coroutine (FootRig.KickRoutine): an error in anything a
/// kick hits (a door's sound, a prop's) ended it half way, the foot stayed "kicking", and no kick worked again in that
/// level. Two guards: a sound already back in the game's pool isn't stopped again (the props and doors that held one
/// after a scene unload threw there), and an error while a kick hits something is logged and the kick goes on.
/// </summary>
[HarmonyPatch]
internal static class KickSafety
{
    private static bool _soundSeen, _hitSeen;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SoundManager.SoundInstance), nameof(SoundManager.SoundInstance.StopAndDestroy))]
    private static bool NotTwice(SoundManager.SoundInstance __instance, ref SoundManager.SoundInstance __result)
    {
        if (__instance == null || !__instance.IsPooled) return true;
        __result = null;
        if (!_soundSeen)
        {
            _soundSeen = true;
            Plugin.L.LogInfo("Kick safety: a sound already back in the game's pool was stopped again (skipped)");
        }

        return false;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(FootRig), "TryHitCollider")]
    private static Exception KickGoesOn(Exception __exception)
    {
        if (__exception == null) return null;
        if (!_hitSeen)
        {
            _hitSeen = true;
            Plugin.L.LogWarning("Kick safety: the game failed on what a kick hit (" + __exception.GetType().Name + ": " + __exception.Message + "); the kick goes on");
        }

        return null;
    }
}
