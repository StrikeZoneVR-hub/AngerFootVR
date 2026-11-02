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
    }

    public static Pose Left, Right;

    /// <summary>Dev: hand poses relative to the head (metres in head-yaw space; rotation as pitch, yaw, roll), for the
    /// test runtime, which has no controllers. Null = off.</summary>
    public static (Vector3 pos, Vector3 euler)? DevLeft, DevRight;

    public static Pose Get(XRNode hand) => hand == XRNode.LeftHand ? Left : Right;

    /// <summary>Called after the VR rig placed the head this frame.</summary>
    public static void Update()
    {
        VrRig.TryGetTrackedHead(out var headTracked, out _);
        var yaw = Quaternion.Euler(0f, VrRig.PlayYaw, 0f);
        var headYaw = Quaternion.Euler(0f, VrRig.HeadRotation.eulerAngles.y, 0f);
        Left = Read(XRNode.LeftHand, headTracked, yaw, headYaw, DevLeft);
        Right = Read(XRNode.RightHand, headTracked, yaw, headYaw, DevRight);
    }

    private static Pose Read(XRNode node, Vector3 headTracked, Quaternion yaw, Quaternion headYaw, (Vector3 pos, Vector3 euler)? dev)
    {
        if (dev.HasValue)
            return new Pose
            {
                Tracked = true,
                Position = VrRig.HeadPosition + headYaw * dev.Value.pos,
                Rotation = headYaw * Quaternion.Euler(dev.Value.euler)
            };

        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.devicePosition, out var p) || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out var r))
            return new Pose { Tracked = false };
        return new Pose { Tracked = true, Position = VrRig.HeadPosition + yaw * (p - headTracked), Rotation = yaw * r };
    }
}
