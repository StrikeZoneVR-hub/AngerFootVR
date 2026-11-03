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

    private static float _panelShown = -10f;

    /// <summary>The level's card came up: the press that brought it up doesn't also press its PLAY.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(WorldMapPlayLevelPanel), nameof(WorldMapPlayLevelPanel.Show))]
    private static void CardShown() => _panelShown = UnityEngine.Time.unscaledTime;

    /// <summary>
    /// The game's on-screen buttons that name a gamepad button (SELECT A, BACK B, SHOES Y, the level card's PLAY) ask
    /// the gamepad directly: the controllers' own buttons are those buttons (A and B on the right controller, X and Y on
    /// the left, the triggers A too, the grips the shoulder buttons, the menu button Start). Not in the movie house.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Gamepad), nameof(Gamepad.WasPressed))]
    private static void ControllersAsGamepad(Gamepad.Button button, ref bool __result)
    {
        // (in the movie house the laser clicks the menus and A/X kicks: there the gamepad stays the gamepad)
        if (__result || !VrInput || CinemaRig.Active || UnityEngine.Time.unscaledTime - _panelShown < 0.35f) return;
        var right = UnityEngine.XR.XRNode.RightHand;
        var left = UnityEngine.XR.XRNode.LeftHand;
        __result = button switch
        {
            Gamepad.Button.A => VrControls.Down(right, VrButton.Primary) || VrControls.DownEither(VrButton.Trigger),
            Gamepad.Button.B => VrControls.Down(right, VrButton.Secondary),
            Gamepad.Button.X => VrControls.Down(left, VrButton.Primary),
            Gamepad.Button.Y => VrControls.Down(left, VrButton.Secondary),
            Gamepad.Button.LeftShoulder => VrControls.Down(left, VrButton.Grip),
            Gamepad.Button.RightShoulder => VrControls.Down(right, VrButton.Grip),
            Gamepad.Button.Start => VrControls.Down(left, VrButton.Menu),
            _ => false,
        };
    }
}
