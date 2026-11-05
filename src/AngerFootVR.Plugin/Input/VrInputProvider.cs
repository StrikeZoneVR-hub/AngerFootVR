using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// The VR controllers as the game's own kind of input (IInputProvider, beside its keyboard and gamepad ones).
///
/// Play: trigger (gun hand) shoots, B or X kicks, A jumps, Y uses your shoes' power, the gun hand's grip throws your
/// gun (MotionThrow: standing, let go of it mid-swing; seated, press it), the other grip interacts (and grabs a gun within reach); left stick moves, right stick turns, the menu button pauses. Your head does the
/// looking. Menus: either stick moves, A accepts, B goes back, the grips change tabs.
/// </summary>
internal class VrInputProvider : IInputProvider
{
    public static readonly VrInputProvider Instance = new VrInputProvider();

    /// <summary>The hand holding the gun (the other hand's grip interacts).</summary>
    public static XRNode GunHand = XRNode.RightHand;

    private static XRNode OffHand => GunHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;


    private static XRNode PowerHand => XRNode.LeftHand; // Y

    private const float StickDeadzone = 0.2f;
    private const float MenuDeadzone = 0.5f;

    // play
    // (dual pistols: either trigger, each fires its own pistol: Arsenal)
    public bool IsShootPressed() => VrControls.Held(GunHand, VrButton.Trigger) || Arsenal.Dual && VrControls.HeldEither(VrButton.Trigger);
    public bool WasShootReleased() => Arsenal.Dual ? VrControls.UpEither(VrButton.Trigger) && !VrControls.HeldEither(VrButton.Trigger) : VrControls.Up(GunHand, VrButton.Trigger);
    public bool IsThrowPressed() => MotionThrow.Held; // (on letting go of the gun hand's grip, or seated pressing it: the game's own throw)
    // (the player's choice: B kicks and A jumps; X kicks too, so either hand can)
    public bool IsKickPressed() => KickHeld;
    public bool WasKickPressed() => KickDown;
    public bool WasKickReleased() => VrControls.Up(XRNode.RightHand, VrButton.Secondary) || VrControls.Up(XRNode.LeftHand, VrButton.Primary);
    public bool IsJumpPressed() => VrControls.Held(XRNode.RightHand, VrButton.Primary);
    public bool WasJumpPressed() => VrControls.Down(XRNode.RightHand, VrButton.Primary);
    public bool WasJumpReleased() => VrControls.Up(XRNode.RightHand, VrButton.Primary);

    /// <summary>The kick buttons: B (right) and X (left).</summary>
    public static bool KickHeld => VrControls.Held(XRNode.RightHand, VrButton.Secondary) || VrControls.Held(XRNode.LeftHand, VrButton.Primary);

    public static bool KickDown => VrControls.Down(XRNode.RightHand, VrButton.Secondary) || VrControls.Down(XRNode.LeftHand, VrButton.Primary);
    public bool IsShoePowerPressed() => VrControls.Held(PowerHand, VrButton.Secondary);
    public bool WasShoePowerReleased() => VrControls.Up(PowerHand, VrButton.Secondary);
    public bool WasInteractPressed() => VrControls.Down(OffHand, VrButton.Grip);
    public bool WasPausePressed() => Live && VrControls.DownEither(VrButton.Menu);
    public bool WasRestartLevelPressed() => false; // the pause menu has RESTART
    public bool WasRespawnPressed() => AnyFaceOrTrigger();

    // the leg scenes' foot grab: kick or shoot, as with a gamepad
    public bool WasFootGrabPressed() => WasKickPressed() || VrControls.Down(GunHand, VrButton.Trigger);
    public bool WasFootGrabReleased() => WasKickReleased() || WasShootReleased();
    public bool IsFootGrabPressed() => IsKickPressed() || IsShootPressed();

    // driving
    public bool IsAcceleratePressed() => VrControls.Held(XRNode.RightHand, VrButton.Trigger);
    public bool IsDeceleratePressed() => VrControls.Held(XRNode.LeftHand, VrButton.Trigger);

    // menus
    // (in the movie house the laser clicks the menus: only with no controllers to point does A accept)
    public bool WasMenuAcceptPressed() => Live && (CinemaRig.Active
        ? !VrHands.Get(XRNode.LeftHand).Tracked && !VrHands.Get(XRNode.RightHand).Tracked && VrControls.DownEither(VrButton.Primary)
        : VrControls.DownEither(VrButton.Primary) || VrControls.DownEither(VrButton.Trigger));
    // (B goes back, as on a gamepad: Y is the car's SHOES, and as a back too it opened the shoes and left them at once.
    // In the movie house B kicks, so there it goes back only while you point at the screen; Y goes back there too)
    public bool WasMenuCancelPressed() => Live && (CinemaRig.Active
        ? VrControls.Down(XRNode.LeftHand, VrButton.Secondary) || VrControls.Down(XRNode.RightHand, VrButton.Secondary) && UI.Laser.OnScreen
        : VrControls.Down(XRNode.RightHand, VrButton.Secondary));
    public bool WasMenuNextTabPressed() => Live && VrControls.Down(XRNode.RightHand, VrButton.Grip);
    public bool WasMenuPreviousTabPressed() => Live && VrControls.Down(XRNode.LeftHand, VrButton.Grip);
    public bool WasMenuCancelInputRemappingPressed() => VrControls.DownEither(VrButton.Menu);
    // (the logo screen's "press any key": any button, A/X and the trigger too, though they kick and click in the movie house)
    public bool WasSkipCinematicPressed() => Live && (VrControls.DownEither(VrButton.Secondary) || VrControls.DownEither(VrButton.Menu)
        || CinemaRig.Active && AnyFaceOrTrigger() && OnLogoScreen());

    private static bool OnLogoScreen() =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == Framework.ScriptableObjectSingleton<GameConfig>.Instance.LogoScreen.SceneName;
    public bool WasAdvanceConversationPressed() => Live && AnyFaceOrTrigger();
    public bool WasCancelConversationPressed() => VrControls.DownEither(VrButton.Menu);

    public Vector2 GetMovementDelta() => Deadzone(VrControls.Stick(XRNode.LeftHand), StickDeadzone);

    public Vector2 GetMenuDelta()
    {
        if (!Live) return Vector2.zero;
        // in the movie house the sticks walk and turn you: the menus go by the laser
        if (CinemaRig.Active) return Vector2.zero;
        var left = VrControls.Stick(XRNode.LeftHand);
        var right = VrControls.Stick(XRNode.RightHand);
        return Deadzone(left.sqrMagnitude > right.sqrMagnitude ? left : right, MenuDeadzone);
    }

    public Vector2 GetLookDelta() => Vector2.zero; // your head does the looking (the right stick turns, in VrRig)

    public void Vibrate(float amount, float duration)
    {
        VrControls.Rumble(XRNode.LeftHand, amount * 0.7f, duration);
        VrControls.Rumble(XRNode.RightHand, amount * 0.7f, duration);
    }

    public void StopVibration()
    {
        VrControls.Rumble(XRNode.LeftHand, 0f, 0.01f);
        VrControls.Rumble(XRNode.RightHand, 0f, 0.01f);
    }

    /// <summary>The VR opening keeps the game's menus from taking the buttons that skip it.</summary>
    private static bool Live => !UI.Opening.Playing;

    private static bool AnyFaceOrTrigger() =>
        VrControls.DownEither(VrButton.Primary) || VrControls.DownEither(VrButton.Secondary) || VrControls.DownEither(VrButton.Trigger);

    private static Vector2 Deadzone(Vector2 v, float zone)
    {
        var m = v.magnitude;
        if (m < zone) return Vector2.zero;
        return v / m * Mathf.Clamp01((m - zone) / (1f - zone));
    }
}
