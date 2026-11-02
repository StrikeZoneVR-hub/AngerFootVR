using AngerFootVR.Core;
using HarmonyLib;

namespace AngerFootVR.Input;

/// <summary>
/// While VR runs with controllers, the game's input is the VR controllers (VrInputProvider) and it plays as if on a
/// gamepad: its menus navigate the gamepad way and show gamepad prompts. Without controllers (a gamepad in VR), the
/// game's own input stays.
/// </summary>
[HarmonyPatch]
internal static class InputPatches
{
    private static bool VrInput => XrManager.IsRunning && VrControls.Present;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameState), nameof(GameState.Input), MethodType.Getter)]
    private static void UseVrControllers(ref IInputProvider __result)
    {
        if (!VrInput || __result is NoInputProvider) return; // (no input while the game changes scenes)
        VrControls.Update();
        __result = VrInputProvider.Instance;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameState), nameof(GameState.IsCurrentlyUsingGamepad), MethodType.Getter)]
    private static void PlayAsGamepad(ref bool __result)
    {
        if (VrInput) __result = true;
    }
}
