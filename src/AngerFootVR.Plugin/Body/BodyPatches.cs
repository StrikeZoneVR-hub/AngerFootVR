using HarmonyLib;

namespace AngerFootVR.Body;

/// <summary>The game's own actions shown on the VR body.</summary>
[HarmonyPatch]
internal static class BodyPatches
{
    /// <summary>The game kicks (A/X): the body's leg swings out with it.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FootRig), nameof(FootRig.ReleaseAttack))]
    private static void Kicked() => HeroBody.Kick();
}
