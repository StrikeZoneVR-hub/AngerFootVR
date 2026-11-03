using AngerFootVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Input;

/// <summary>
/// The VR controllers as the game's own kind of input (IInputProvider, beside its keyboard and gamepad ones).
///
/// Play: trigger (gun hand) shoots, A or X kicks, B jumps, Y uses your shoes' power, the gun hand's grip throws your
/// gun, the other grip interacts; left stick moves, right stick turns, the menu button pauses. Your head does the
/// looking. Menus: either stick moves, A accepts, B goes back, the grips change tabs.
/// </summary>
internal class VrInputProvider : IInputProvider
{
    public static readonly VrInputProvider Instance = new VrInputProvider();

    /// <summary>The hand holding the gun (the other hand's grip interacts).</summary>
    public static XRNode GunHand = XRNode.RightHand;

    private static XRNode OffHand => GunHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;

    private static XRNode JumpHand => XRNode.RightHand; // B

    private static XRNode PowerHand => XRNode.LeftHand; // Y

    private const float StickDeadzone = 0.2f;
    private const float MenuDeadzone = 0.5f;

    // play
    public bool IsShootPressed() => VrControls.Held(GunHand, VrButton.Trigger);
    public bool WasShootReleased() => VrControls.Up(GunHand, VrButton.Trigger);
    public bool IsThrowPressed() => VrControls.Held(GunHand, VrButton.Grip) || MotionThrow.Held;
    public bool IsKickPressed() => VrControls.HeldEither(VrButton.Primary);
    public bool WasKickPressed() => VrControls.DownEither(VrButton.Primary);
    public bool WasKickReleased() => VrControls.UpEither(VrButton.Primary);
    public bool IsJumpPressed() => VrControls.Held(JumpHand, VrButton.Secondary);
    public bool WasJumpPressed() => VrControls.Down(JumpHand, VrButton.Secondary);
    public bool WasJumpReleased() => VrControls.Up(JumpHand, VrButton.Secondary);
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
    // (in the movie house the laser clicks the menus, and A/X kicks: only with no controllers to point does A/X accept)
    public bool WasMenuAcceptPressed() => Live && (CinemaRig.Active
        ? !VrHands.Get(XRNode.LeftHand).Tracked && !VrHands.Get(XRNode.RightHand).Tracked && VrControls.DownEither(VrButton.Primary)
        : VrControls.DownEither(VrButton.Primary) || VrControls.DownEither(VrButton.Trigger));
    public bool WasMenuCancelPressed() => Live && VrControls.DownEither(VrButton.Secondary);
    public bool WasMenuNextTabPressed() => Live && VrControls.Down(XRNode.RightHand, VrButton.Grip);
    public bool WasMenuPreviousTabPressed() => Live && VrControls.Down(XRNode.LeftHand, VrButton.Grip);
    public bool WasMenuCancelInputRemappingPressed() => VrControls.DownEither(VrButton.Menu);
    public bool WasSkipCinematicPressed() => Live && (VrControls.DownEither(VrButton.Secondary) || VrControls.DownEither(VrButton.Menu));
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
