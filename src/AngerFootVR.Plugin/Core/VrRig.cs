using System;
using Framework;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// The head-tracked view in gameplay.
///
/// The game turns one object for looking: the player's camera holder (PlayerCamera), which it keeps at the player's
/// eyes; the body turns with it and kicks and shots go where its Main Camera faces. In VR:
/// - the holder's look is your head's (pitch and yaw, no roll), so kicks and shots go where you look and the body faces
///   the way you look;
/// - the game's view cameras show your head exactly (roll and leaning too), Unity's own camera tracking switched off
///   for them so the head isn't applied twice;
/// - turning (right stick) turns the play space; height is calibrated to the game's eye height at each level start, so
///   crouching for real lowers your view.
/// </summary>
[HarmonyPatch]
internal static class VrRig
{
    /// <summary>The play space's facing in the world (degrees): set from the game's camera at level start, then
    /// changed only by turning.</summary>
    public static float PlayYaw;

    /// <summary>Smooth turn speed (degrees a second at full stick).</summary>
    public static float SmoothTurnSpeed = 180f;

    /// <summary>How far your head may move from the body's centre in the game (metres), so leaning never puts your
    /// eyes through a wall.</summary>
    public const float MaxLean = 0.3f;

    /// <summary>Your head's pose in the game world this frame.</summary>
    public static Vector3 HeadPosition { get; private set; }

    public static Quaternion HeadRotation { get; private set; } = Quaternion.identity;

    /// <summary>Dev: replaces the tracked head's rotation (pitch, yaw, roll in degrees), for the test runtime's
    /// unmoving head.</summary>
    public static Vector3? DevHeadEuler;

    private static Vector3 _center; // tracking-space point under your head when calibrated
    private static float _standingEyeHeight = 1.65f; // your eye height then (metres)
    private static bool _calibrated;
    private static PlayerCamera _rigged;

    private static bool Active => XrManager.IsRunning && SingletonBehaviour<Player>.HasInstance;

    /// <summary>Recentres the play space on your head and takes your standing eye height (next frame).</summary>
    public static void Recenter() => _calibrated = false;

    /// <summary>Dev: VIEW HEIGHT for now (metres; the settings file untouched).</summary>
    public static float? DevLift;

    /// <summary>VIEW HEIGHT: how far above the character's own eyes you see from (the game's are 1.5 m up: a grown-up
    /// in VR feels small, a player's video said). Your hands and body come up with you.</summary>
    public static float Lift => DevLift ?? (VrSettings.ViewHeight == null ? 0f : (int)VrSettings.ViewHeight.Value * 0.1f);

    public static bool TryGetTrackedHead(out Vector3 position, out Quaternion rotation)
    {
        var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        position = default;
        rotation = Quaternion.identity;
        var ok = head.isValid
                 && (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out position) || head.TryGetFeatureValue(CommonUsages.devicePosition, out position))
                 && (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation) || head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation));
        if (DevHeadEuler.HasValue)
        {
            rotation = Quaternion.Euler(DevHeadEuler.Value);
            ok = true;
        }

        return ok;
    }

    /// <summary>After the game's own look (which in VR gets no mouse or stick look): the look becomes the head's.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerInput), "DoMouseLook")]
    private static void AfterMouseLook(PlayerInput __instance, Player ____player)
    {
        if (!Active || ____player == null || !__instance.ControlMode.TurnPlayerWithCamera) return;
        var camera = ____player.Camera.transform;
        if (!_calibrated) Calibrate(camera);
        Turn(VrControls.TurnInput);

        if (!TryGetTrackedHead(out _, out var tracked)) return;
        var head = Quaternion.Euler(0f, PlayYaw, 0f) * tracked;
        var e = head.eulerAngles;
        var pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -89f, 89f);
        camera.rotation = Quaternion.Euler(pitch, e.y, 0f);
        ____player.Rigidbody.transform.rotation = Quaternion.Euler(0f, e.y, 0f);
        ____player.transform.SetPositionAndRotation(____player.transform.position, ____player.Rigidbody.transform.rotation, dontChangeChildren: true);
    }

    /// <summary>After the game places its view cameras (shake, forward offset): they show your head instead.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerCamera), "LateUpdate")]
    private static void AfterCameraLateUpdate(PlayerCamera __instance)
    {
        if (!Active) return;
        if (_rigged != __instance) Rig(__instance);
        if (!PlaceCameras(__instance)) return;
        Body.HeroBody.Pose();
    }

    /// <summary>The view cameras at your head: in LateUpdate (for the body and the game), and again just before the frame
    /// is drawn, from the head pose Unity draws the eyes with (as Unity's own tracked pose driver does): placed from an
    /// earlier reading, every head movement in between showed as a wobble of the whole view (the first test's jitter).</summary>
    private static bool PlaceCameras(PlayerCamera pc)
    {
        if (!TryGetTrackedHead(out var pos, out var tracked)) return false;
        var holder = pc.transform;
        var yaw = Quaternion.Euler(0f, PlayYaw, 0f);
        var offset = pos - _center;
        offset.y = Mathf.Clamp(pos.y - _standingEyeHeight, -0.9f, 0.25f) + Lift;
        var lean = new Vector2(offset.x, offset.z);
        if (lean.magnitude > MaxLean) lean = lean.normalized * MaxLean;
        offset.x = lean.x;
        offset.z = lean.y;

        HeadPosition = holder.position + yaw * offset;
        HeadRotation = yaw * tracked;
        var inverse = Quaternion.Inverse(holder.rotation);
        var localPos = inverse * (HeadPosition - holder.position);
        var localRot = inverse * HeadRotation;
        pc.MainCamera.transform.localPosition = localPos;
        pc.MainCamera.transform.localRotation = localRot;
        pc.WeaponCamera.transform.localPosition = localPos;
        pc.WeaponCamera.transform.localRotation = localRot;
        return true;
    }

    /// <summary>Just before the frame is drawn: the cameras again, from the newest head pose (Unity's own for this frame).</summary>
    public static void BeforeRender()
    {
        if (!Active || _rigged == null || !_calibrated) return;
        var before = HeadRotation;
        if (PlaceCameras(_rigged)) HeadTiming.Note(before, HeadRotation);
    }

    /// <summary>The game's cameras take their pose from the rig, not from Unity's camera tracking.</summary>
    private static void Rig(PlayerCamera pc)
    {
        _rigged = pc;
        foreach (var cam in new[] { pc.MainCamera, pc.EffectsCamera, pc.WeaponCamera })
            if (cam != null)
            {
                XRDevice.DisableAutoXRCameraTracking(cam, true);
                cam.nearClipPlane = Mathf.Max(cam.nearClipPlane, 0.08f); // the body's collar stands close round your eyes
            }
        _calibrated = false;
        Plugin.L.LogInfo("VR rig on the player's cameras");
    }

    private static void Calibrate(Transform camera)
    {
        if (!TryGetTrackedHead(out var pos, out _)) return;
        _calibrated = true;
        _center = new Vector3(pos.x, 0f, pos.z);
        // Your eye height now becomes the game's eye height: standing or seated, you see from the character's eyes,
        // and crouching for real from there lowers the view. (A head at the floor is a runtime without a floor:
        // the test runtime.)
        _standingEyeHeight = pos.y;
        // face the way the game faces you, from where your head is turned now
        TryGetTrackedHead(out _, out var tracked);
        PlayYaw = camera.eulerAngles.y - tracked.eulerAngles.y;
        Plugin.L.LogInfo($"VR rig calibrated: eye height {_standingEyeHeight:F2} m, facing {camera.eulerAngles.y:F0} deg");
    }

    private static bool _snapHeld;

    private static void Turn(float stick) => PlayYaw += TurnDelta(stick, ref _snapHeld);

    /// <summary>How far the right stick turns you this frame (degrees): snap (one jump a push, the stick back to the
    /// middle before the next) or smooth, as set in OPTIONS. Used in play and in the movie house.</summary>
    public static float TurnDelta(float stick, ref bool snapHeld)
    {
        if (VrSettings.Turn == null || VrSettings.Turn.Value == VrSettings.TurnStyle.Smooth)
            return Mathf.Abs(stick) < 0.15f ? 0f : stick * (VrSettings.SmoothSpeed?.Value ?? SmoothTurnSpeed) * Time.unscaledDeltaTime;
        if (Mathf.Abs(stick) < 0.35f) snapHeld = false;
        else if (!snapHeld && Mathf.Abs(stick) > 0.7f)
        {
            snapHeld = true;
            return Mathf.Sign(stick) * VrSettings.SnapAngle.Value;
        }

        return 0f;
    }
}
