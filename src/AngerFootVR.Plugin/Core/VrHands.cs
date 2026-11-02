using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// Where your hands are in the game world: each controller's tracked pose, placed relative to your head the way the VR
/// rig places the head (so hands and head always agree). Without controllers (or on the test runtime) the dev poses
/// stand in.
/// </summary>
internal static class VrHands
{
    public struct Pose
    {
        public bool Tracked;
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>How fast the hand moves in your play space (m/s, turned with it; walking doesn't count).</summary>
        public Vector3 Velocity;
    }

    public static Pose Left, Right;

    /// <summary>How far the pointing way (laser, gun barrel) is tilted up from the controller's grip (degrees, negative = up).</summary>
    public static float AimPitch = -35f;

    /// <summary>Dev: hand poses relative to the head (metres in head-yaw space; rotation as pitch, yaw, roll), for the
    /// test runtime, which has no controllers. Null = off.</summary>
    public static (Vector3 pos, Vector3 euler)? DevLeft, DevRight;

    public static Pose Get(XRNode hand) => hand == XRNode.LeftHand ? Left : Right;

    /// <summary>Called after the VR rig placed the head this frame.</summary>
    public static void Update()
    {
        VrRig.TryGetTrackedHead(out var headTracked, out _);
        UpdateAt(VrRig.HeadPosition, headTracked, VrRig.PlayYaw);
    }

    /// <summary>The hands around a head at this world point (tracked there as headTracked), the play space turned by yaw.</summary>
    public static void UpdateAt(Vector3 headWorld, Vector3 headTracked, float yaw)
    {
        _head = headWorld;
        var yawRot = Quaternion.Euler(0f, yaw, 0f);
        Left = Read(XRNode.LeftHand, headTracked, yawRot, yawRot, DevLeft);
        Right = Read(XRNode.RightHand, headTracked, yawRot, yawRot, DevRight);
    }

    private static Vector3 _head;
    private static readonly Vector3[] Last = new Vector3[2], Vel = new Vector3[2];
    private static readonly int[] LastFrame = { -1, -1 };

    /// <summary>The hand's speed in tracking space, from where it was last frame (smoothed a little).</summary>
    private static Vector3 Speed(XRNode node, Vector3 p)
    {
        var i = node == XRNode.LeftHand ? 0 : 1;
        if (LastFrame[i] == Time.frameCount) return Vel[i];
        var dt = Time.unscaledDeltaTime;
        if (LastFrame[i] == Time.frameCount - 1 && dt > 1e-4f) Vel[i] = Vector3.Lerp(Vel[i], (p - Last[i]) / dt, 0.6f);
        else Vel[i] = Vector3.zero;
        Last[i] = p;
        LastFrame[i] = Time.frameCount;
        return Vel[i];
    }

    private static Pose Read(XRNode node, Vector3 headTracked, Quaternion yaw, Quaternion headYaw, (Vector3 pos, Vector3 euler)? dev)
    {
        if (dev.HasValue)
            return new Pose
            {
                Tracked = true,
                Position = _head + headYaw * dev.Value.pos,
                Rotation = headYaw * Quaternion.Euler(dev.Value.euler),
                Velocity = headYaw * Speed(node, dev.Value.pos),
            };

        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.devicePosition, out var p) || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out var r))
            return new Pose { Tracked = false };
        return new Pose { Tracked = true, Position = _head + yaw * (p - headTracked), Rotation = yaw * r, Velocity = yaw * Speed(node, p) };
    }
}
