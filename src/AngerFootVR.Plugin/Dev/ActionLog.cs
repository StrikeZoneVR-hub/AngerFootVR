using HarmonyLib;

namespace AngerFootVR.Dev;

/// <summary>Dev: logs the game's own kick and jump as they happen, to check the VR controls reach them.</summary>
[HarmonyPatch]
internal static class ActionLog
{
    public static bool Enabled = Plugin.DevRun;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FootRig), nameof(FootRig.ReleaseAttack))]
    private static void Kick()
    {
        if (Enabled) Plugin.L.LogInfo("ACTION kick released");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.TryJump))]
    private static void Jump()
    {
        if (Enabled) Plugin.L.LogInfo("ACTION jump");
    }
}
