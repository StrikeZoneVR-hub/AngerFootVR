using HarmonyLib;

namespace AngerFootVR.Body;

/// <summary>The game's own actions shown on the VR body.</summary>
[HarmonyPatch]
internal static class BodyPatches
{
    /// <summary>The game kicks (A/X): the body's leg swings out with it, the moment the game's kick starts (not when the
    /// button comes up, and not at all when the game didn't kick: a cooldown, kicking not allowed).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnKick))]
    private static void Kicked() => HeroBody.Kick();
}
