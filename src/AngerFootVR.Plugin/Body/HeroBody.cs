using AngerFootVR.Core;
using Framework;
using UnityEngine;
using UnityEngine.XR;

namespace AngerFootVR.Body;

/// <summary>
/// Poses your body every frame (InkVR's way): the Skeleton is solved from your head and hands, then the hero's model
/// (HeroSkin) is put on it, and its mirror twin with it. In a level the body is the game's size (its eyes at the game's
/// eye height, standing on the ground under you, legs tucked in the air); in the movie house it's yours (its eyes at
/// your standing eye height, on the floor). Each hand is placed from its controller's grip as InkVR's hands are (the
/// fingers down the handle, the back of the hand outward), and curls with the trigger and grip; the gun hand closes
/// round the gun.
/// </summary>
internal static class HeroBody
{
    private static HeroSkin _skin;
    private static readonly Skeleton Sk = new Skeleton();
    private static bool _showHead;
    private static float _lastPose = -1f;

    public static bool Ready => _skin != null && _skin.Ok;
    public static Skeleton Skeleton => Sk;

    public static void Attach(Transform root, Animator animator, Transform twin)
    {
        _skin = new HeroSkin(root, animator, twin);
        Sk.Reset();
    }

    public static void Kick() => Sk.Kick();

    /// <summary>Dev: draw the head too (for pictures of the whole body).</summary>
    public static void ShowHead(bool show) => _showHead = show;

    /// <summary>The feet plant afresh (a level load, the movie house, a recentre).</summary>
    public static void Replant() => Sk.Reset();

    private static int GroundMask => Physics.DefaultRaycastLayers & ~((1 << 10) | (1 << 11) | (1 << 13) | (1 << 14) | (1 << 15) | (1 << 17) | (1 << 18) | (1 << 22) | (1 << 5) | (1 << VrBody.MirrorLayer));

    /// <summary>In a level: called right after the VR rig placed the view cameras.</summary>
    public static void Pose()
    {
        if (!Ready || !VrBody.Active || !SingletonBehaviour<Player>.HasInstance || CinemaRig.Active) return;
        VrHands.Update();
        var player = SingletonBehaviour<Player>.Instance;
        var feet = player.transform.position;
        var gameEye = player.GetEyePosition(alive: true).y - feet.y;
        var k = Mathf.Clamp(gameEye / Skeleton.EyeHeight, 0.5f, 2f);
        var airborne = player.Movement != null && !player.Movement.IsGrounded;
        Solve(VrRig.HeadPosition, VrRig.HeadRotation, k, p => Ground(p, feet.y), airborne);
    }

    /// <summary>In the movie house: your own height, on its flat floor.</summary>
    public static void PoseCinema(Vector3 head, Quaternion rot, float floorY, float standingEye)
    {
        if (!Ready || !VrBody.Active) return;
        var k = Mathf.Clamp(standingEye / Skeleton.EyeHeight, 0.6f, 1.3f);
        Solve(head, rot, k, _ => floorY, false);
    }

    private static void Solve(Vector3 eye, Quaternion headRot, float k, System.Func<Vector3, float> groundAt, bool airborne)
    {
        var now = Time.unscaledTime;
        var dt = _lastPose < 0f ? 0f : Mathf.Clamp(now - _lastPose, 0f, 0.1f);
        if (now - _lastPose > 0.5f) Sk.Reset(); // (back from a pause, a load, the movie house)
        _lastPose = now;
        var l = VrHands.Get(XRNode.LeftHand);
        var r = VrHands.Get(XRNode.RightHand);
        var pose = Sk.Solve(eye, headRot, k, l.Tracked ? l.Position : (Vector3?)null, r.Tracked ? r.Position : (Vector3?)null, groundAt, airborne, dt);
        VrBody.SetShown(!pose.Low);
        if (pose.Low) return;
        _skin.Apply(pose, Hand(0, l), Hand(1, r), _showHead);
        VrBody.SyncTwinParts();
    }

    /// <summary>The ground under a point (the floor, stairs, a ledge); in the air or off an edge, where your feet are.</summary>
    private static float Ground(Vector3 p, float feetY)
    {
        var from = new Vector3(p.x, Mathf.Max(p.y, feetY + 0.5f), p.z);
        if (Physics.Raycast(from, Vector3.down, out var hit, from.y - feetY + 0.6f, GroundMask, QueryTriggerInteraction.Ignore) && hit.point.y > feetY - 0.45f)
            return hit.point.y;
        return feetY;
    }

    /// <summary>A hand from its controller: InkVR's grip-to-hand frame (z along the fingers, y out of the back of the hand).</summary>
    private static HeroSkin.HandPose Hand(int side, VrHands.Pose pose)
    {
        if (!pose.Tracked) return default;
        var s = side == 0 ? -1f : 1f;
        // grip space: the handle along +z, the fingers wrapping down it (-y), the back of the hand outward (+x right hand)
        var local = Quaternion.LookRotation(Vector3.down, new Vector3(s, 0, 0));
        var rot = pose.Rotation * local;
        var pos = pose.Position + pose.Rotation * (new Vector3(0.032f * s, 0, 0) - local * new Vector3(0, 0, 0.05f));
        var node = side == 0 ? XRNode.LeftHand : XRNode.RightHand;
        var c = side == 0 ? XrActions.Left : XrActions.Right;
        var trigger = XrActions.Ready ? c.Trigger : VrControls.Held(node, VrButton.Trigger) ? 1f : 0f;
        var grip = XrActions.Ready ? c.Grip : VrControls.Held(node, VrButton.Grip) ? 1f : 0f;
        var thumb = XrActions.Ready && c.ThumbTouch ? 0.85f : 0.2f;
        var index = Mathf.Lerp(0.15f, 1f, trigger);
        var fist = Mathf.Lerp(0.15f, 1f, grip);
        if (node == Input.GunHold.Hand && Input.GunHold.Holding)
        {
            // round the gun's grip, the index finger on its trigger
            fist = 1f;
            index = Mathf.Lerp(0.45f, 0.85f, trigger);
            thumb = 0.8f;
        }

        return new HeroSkin.HandPose { Valid = true, Wrist = pos, Rot = rot, Scale = 1f, Index = index, Grip = fist, Thumb = thumb };
    }

    public static string Describe() => (Ready ? $"scale {_skin.Scale:F3} rest eyes {_skin.RestEyeHeight:F2} {_skin.DescribeArms()} | " : "no skin | ") + Sk.Describe();
}
