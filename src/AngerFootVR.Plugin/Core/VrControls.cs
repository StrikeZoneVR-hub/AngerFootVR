using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// The VR controllers, read through Unity's XR input (the OpenXR provider's controller profiles). Phase 1 uses the
/// right stick for turning; the full controls (guns in the hand, A/X kicks, the rest of the game's actions) build on
/// this in phase 2.
/// </summary>
internal static class VrControls
{
    /// <summary>Dev: replaces the turn stick (-1..1) for the test runtime, which has no controllers.</summary>
    public static float? DevTurn;

    /// <summary>Turning this frame (-1 left .. 1 right): the right stick.</summary>
    public static float TurnInput => DevTurn ?? Stick(XRNode.RightHand).x;

    public static Vector2 Stick(XRNode hand)
    {
        var device = InputDevices.GetDeviceAtXRNode(hand);
        return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var v) ? v : Vector2.zero;
    }
}
