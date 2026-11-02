using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>The VR controllers' buttons the mod reads.</summary>
internal enum VrButton
{
    Trigger,
    Grip,
    Primary, // A / X
    Secondary, // B / Y
    StickClick,
    Menu
}

/// <summary>
/// The VR controllers, read through Unity's XR input (the OpenXR provider's controller profiles) once a frame, so
/// "pressed this frame" and "released this frame" are known for every button the way the game's own input works.
/// </summary>
internal static class VrControls
{
    private const int Buttons = 6;

    /// <summary>Dev: replaces the turn stick (-1..1) for the test runtime, which has no controllers.</summary>
    public static float? DevTurn;

    /// <summary>Dev: buttons held down by command (hand, button) for the test runtime, and for how many frames.</summary>
    private static readonly Dictionary<(XRNode, VrButton), int> DevHeld = new Dictionary<(XRNode, VrButton), int>();

    public static Vector2 DevMove;
    private static float _devUntil;

    private static readonly bool[] Now = new bool[Buttons * 2], Before = new bool[Buttons * 2];
    private static readonly Vector2[] Sticks = new Vector2[2];
    private static int _frame = -1;

    /// <summary>Whether VR controllers are there (or the dev pretends they are).</summary>
    public static bool Present { get; private set; }

    /// <summary>Turning this frame (-1 left .. 1 right): the right stick.</summary>
    public static float TurnInput => DevTurn ?? Stick(XRNode.RightHand).x;

    public static void Update()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        System.Array.Copy(Now, Before, Now.Length);
        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (DevHeld.Count > 0 || DevMove != Vector2.zero) _devUntil = Time.unscaledTime + 3f; // dev input stays "present" a moment (its releases count)
        Present = left.isValid || right.isValid || Time.unscaledTime < _devUntil;
        Read(left, 0);
        Read(right, 1);
        foreach (var key in new List<(XRNode, VrButton)>(DevHeld.Keys))
        {
            Now[Index(key.Item1, key.Item2)] = true;
            if (--DevHeld[key] <= 0) DevHeld.Remove(key);
        }

        if (DevMove != Vector2.zero) Sticks[0] = DevMove;
    }

    private static void Read(InputDevice device, int hand)
    {
        var o = hand * Buttons;
        if (!device.isValid)
        {
            for (var i = 0; i < Buttons; i++) Now[o + i] = false;
            Sticks[hand] = Vector2.zero;
            return;
        }

        Now[o + (int)VrButton.Trigger] = device.TryGetFeatureValue(CommonUsages.trigger, out var t) ? t > (Now[o] ? 0.35f : 0.55f) : Flag(device, CommonUsages.triggerButton);
        Now[o + (int)VrButton.Grip] = device.TryGetFeatureValue(CommonUsages.grip, out var g) ? g > (Now[o + 1] ? 0.4f : 0.6f) : Flag(device, CommonUsages.gripButton);
        Now[o + (int)VrButton.Primary] = Flag(device, CommonUsages.primaryButton);
        Now[o + (int)VrButton.Secondary] = Flag(device, CommonUsages.secondaryButton);
        Now[o + (int)VrButton.StickClick] = Flag(device, CommonUsages.primary2DAxisClick);
        Now[o + (int)VrButton.Menu] = Flag(device, CommonUsages.menuButton);
        Sticks[hand] = device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var s) ? s : Vector2.zero;
    }

    private static bool Flag(InputDevice device, InputFeatureUsage<bool> usage) => device.TryGetFeatureValue(usage, out var v) && v;

    private static int Index(XRNode hand, VrButton b) => (hand == XRNode.LeftHand ? 0 : Buttons) + (int)b;

    public static bool Held(XRNode hand, VrButton b) => Now[Index(hand, b)];

    public static bool Down(XRNode hand, VrButton b) => Now[Index(hand, b)] && !Before[Index(hand, b)];

    public static bool Up(XRNode hand, VrButton b) => !Now[Index(hand, b)] && Before[Index(hand, b)];

    public static bool HeldEither(VrButton b) => Held(XRNode.LeftHand, b) || Held(XRNode.RightHand, b);

    public static bool DownEither(VrButton b) => Down(XRNode.LeftHand, b) || Down(XRNode.RightHand, b);

    public static bool UpEither(VrButton b) => (Up(XRNode.LeftHand, b) || Up(XRNode.RightHand, b)) && !HeldEither(b);

    public static Vector2 Stick(XRNode hand) => Sticks[hand == XRNode.LeftHand ? 0 : 1];

    /// <summary>Dev: holds a button down for some frames.</summary>
    public static void DevPress(XRNode hand, VrButton b, int frames) => DevHeld[(hand, b)] = frames;

    /// <summary>Rumble on one controller (amount 0..1, seconds).</summary>
    public static void Rumble(XRNode hand, float amount, float seconds)
    {
        var device = InputDevices.GetDeviceAtXRNode(hand);
        if (device.isValid && device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
            device.SendHapticImpulse(0, Mathf.Clamp01(amount), seconds);
    }
}
