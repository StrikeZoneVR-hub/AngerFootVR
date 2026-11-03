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
        if (!__result) __result = ControllerPressed(button);
    }

    /// <summary>
    /// Those buttons ask the last gamepad used, and with the VR controllers alone there is none: the game then errors
    /// every frame a menu with them is up (SETTINGS: LB/RB, REVERT X, DEFAULT Y) and its stick navigation stops. The
    /// controllers answer instead, as above.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UINavigation), "WasClickablePressed")]
    private static bool ClickableWithoutGamepad(GamepadClickableButton clickable, ref bool __result)
    {
        if (DevClickLog && (VrControls.Held(UnityEngine.XR.XRNode.LeftHand, VrButton.Grip) || VrControls.Held(UnityEngine.XR.XRNode.RightHand, VrButton.Grip)))
            Plugin.L.LogInfo($"CLICKABLE {clickable.Button} on {clickable.transform.parent?.name}: vr {VrInput} pad {Gamepad.GetLastActive() != null} cinema {CinemaRig.Active} clickable {clickable.IsClickable} pressed {ControllerPressed(clickable.Button)}");
        if (!VrInput || Gamepad.GetLastActive() != null) return true;
        __result = clickable.IsClickable && ((clickable.Button == Gamepad.Button.B && GameState.Input.WasMenuCancelPressed()) || ControllerPressed(clickable.Button));
        if (DevClickLog && __result)
        {
            var b = clickable.GetComponentInParent<ButtonBehaviour>();
            var nav = Framework.AutoSingletonBehaviour<UINavigation>.Instance;
            var max = (int)AccessTools.Field(typeof(UINavigation), "_maxContainerPriority").GetValue(nav);
            Plugin.L.LogInfo(b == null ? "CLICK no button" : $"CLICK {b.name}: can {nav.CanBeInteractedWith(b)} active {b.isActiveAndEnabled} interactable {b.IsInteractable} priority {b.ContainerPriority}/{max} onscreen {b.IsOnScreen()} at {b.GetNavigationPosition()} sub {nav.SelectedControl?.name}");
        }

        return false;
    }

    /// <summary>Dev: log the on-screen gamepad buttons' checks while a grip is held.</summary>
    public static bool DevClickLog;

    private static readonly AccessTools.FieldRef<UIControl, UnityEngine.Canvas> ControlCanvas = AccessTools.FieldRefAccess<UIControl, UnityEngine.Canvas>("_canvas");

    /// <summary>
    /// The game lets the stick and the on-screen gamepad buttons reach only the controls it thinks are on screen, and
    /// it measures that against the window. In VR the menus are drawn into the VR picture instead (the movie screen and
    /// the floating panel), which is bigger than the window: the controls in its top half (the settings' tab arrows
    /// LB/RB, the first rows) counted as off screen. They're measured against the picture they're drawn into.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIControl), nameof(UIControl.IsOnScreen))]
    private static bool OnTheVrPicture(UIControl __instance, ref bool __result)
    {
        var canvas = ControlCanvas(__instance);
        if (canvas == null) return true;
        var cam = canvas.rootCanvas.worldCamera;
        if (canvas.rootCanvas.renderMode != UnityEngine.RenderMode.ScreenSpaceCamera || cam == null || cam.targetTexture == null) return true;
        var p = __instance.GetNavigationPosition();
        __result = p.x >= 0f && p.y >= 0f && p.x <= cam.pixelWidth && p.y <= cam.pixelHeight;
        return false;
    }

    private static bool ControllerPressed(Gamepad.Button button)
    {
        // (in the movie house the laser clicks the menus and A/X kicks: there the gamepad stays the gamepad)
        if (!VrInput || CinemaRig.Active || UnityEngine.Time.unscaledTime - _panelShown < 0.35f) return false;
        var right = UnityEngine.XR.XRNode.RightHand;
        var left = UnityEngine.XR.XRNode.LeftHand;
        return button switch
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
