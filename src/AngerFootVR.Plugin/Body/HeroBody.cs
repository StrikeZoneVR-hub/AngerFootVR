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

    /// <summary>A hand's bone (0 left, 1 right) as the body draws it, or null.</summary>
    public static Transform HandBone(int side) => Ready ? _skin.HandBone(side) : null;

    /// <summary>A foot's bone (the ankle) and its toes' (0 left, 1 right) as the body draws them, or null.</summary>
    public static Transform FootBone(int side) => Ready ? _skin.FootBone(side) : null;

    public static Transform ToeBone(int side) => Ready ? _skin.ToeBone(side) : null;

    public static void Attach(Transform root, Animator animator, Transform twin)
    {
        _skin = new HeroSkin(root, animator, twin);
        Sk.Reset();
    }

    public static void Kick() => Sk.Kick();

    /// <summary>The game's kick, as its first-person legs do it: the normal kick with the left leg, the flying kick (in
    /// the air, moving) right then left, the roundhouse (in the air, standing) the right leg swept round, the stomp
    /// (looking down) a stamp, the slide low.</summary>
    public static void Kick(PlayerMeleeAttack attack)
    {
        if (attack == PlayerMeleeAttack.FlyingKick)
        {
            Sk.Kick(1, Skeleton.KickStyle.High, 0f);
            Sk.Kick(0, Skeleton.KickStyle.High, 0.16f);
        }
        else if (attack == PlayerMeleeAttack.RoundhouseKick) Sk.Kick(1, Skeleton.KickStyle.Sweep, 0f);
        else if (attack == PlayerMeleeAttack.StompKick || attack == PlayerMeleeAttack.AOEStompKick) Sk.Kick(0, Skeleton.KickStyle.Stomp, 0f);
        else if (attack == PlayerMeleeAttack.SlidingKick) Sk.Kick(0, Skeleton.KickStyle.Low, 0f);
        else Sk.Kick();
    }

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
        Input.GunHold.EnsurePlaced();
        var player = SingletonBehaviour<Player>.Instance;
        var feet = player.transform.position;
        var gameEye = player.GetEyePosition(alive: true).y - feet.y + VrRig.Lift; // (VIEW HEIGHT: a taller you)
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

    private static readonly float[] _thumb = { 0.1f, 0.1f }, _flip = { 0f, 0f };

    /// <summary>How far the middle finger is up (0..1) on this hand (0 left, 1 right).</summary>
    public static float FlipAmount(int side) => _flip[side];

    /// <summary>Dev: the fingers held at index, fist, thumb, middle finger (null: from the controllers).</summary>
    public static Vector4? DevFingers;

    private static float Smooth(float t) => t * t * (3f - 2f * t);

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
        // (a full fist before the grip is squeezed all the way: nobody holds it at 100%)
        var index = Mathf.Lerp(0.12f, 1f, Smooth(Mathf.Clamp01(trigger / 0.85f)));
        var fist = Mathf.Lerp(0.12f, 1f, Smooth(Mathf.Clamp01(grip / 0.8f)));
        // the thumb: on the stick, a button or the thumb rest it folds over the fist; lifted, it's a thumbs-up
        var thumbOn = XrActions.Ready ? c.ThumbTouch : VrControls.Held(node, VrButton.Grip);
        _thumb[side] = Mathf.MoveTowards(_thumb[side], thumbOn ? 0.55f + 0.4f * fist : 0.1f, Time.unscaledDeltaTime * 8f);
        var thumb = _thumb[side];
        if (Input.MotionDrink.HoldingIn(node))
        {
            fist = 0.8f;
            index = 0.75f;
            thumb = 0.65f;
        }
        else if (Input.GunSpin.Spinning(node))
        {
            // a gun spinning round the index: the index hooked through its trigger guard, the rest open
            fist = 0.2f;
            index = 0.72f;
            thumb = 0.3f;
        }
        else if (node != Input.GunHold.Hand && Input.Arsenal.Dual)
        {
            fist = 1f;
            index = Mathf.Lerp(0.45f, 0.85f, trigger);
            thumb = 0.8f;
        }
        else if (node != Input.GunHold.Hand && Input.Arsenal.TwoHands)
        {
            fist = 0.9f;
            index = 0.85f;
            thumb = 0.75f;
        }
        else if (node == Input.GunHold.Hand && Input.GunHold.Holding)
        {
            // the held thing's own grip (a gun: round its handle, the index on its trigger; a box: hands open under it)
            var held = Input.GunHold.HeldGrip;
            fist = held.x;
            index = held.y < 0f ? Mathf.Lerp(0.45f, 0.85f, trigger) : held.y;
            thumb = held.z;
        }

        // the right stick's click: the middle finger (eased in and out)
        var flipOn = side == 1 && VrControls.Held(XRNode.RightHand, VrButton.StickClick);
        _flip[side] = Mathf.MoveTowards(_flip[side], flipOn ? 1f : 0f, Time.unscaledDeltaTime * 9f);
        if (DevFingers != null)
        {
            var d = DevFingers.Value;
            index = d.x;
            fist = d.y;
            thumb = d.z;
            _flip[side] = d.w;
        }

        return new HeroSkin.HandPose { Valid = true, Wrist = pos, Rot = rot, Scale = 1f, Index = index, Grip = fist, Thumb = thumb, Flip = _flip[side] };
    }

    /// <summary>Dev: the hands as the body draws them (overrides, controller, hand bones), NaN flagged.</summary>
    public static string HandDiag()
    {
        var sb = new System.Text.StringBuilder("HANDDIAG");
        for (var i = 0; i < 2; i++)
        {
            var node = i == 0 ? XRNode.LeftHand : XRNode.RightHand;
            var raw = VrHands.GetRaw(node);
            var bone = Ready ? _skin.HandBone(i) : null;
            var bp = bone != null ? bone.position : Vector3.zero;
            var nan = float.IsNaN(bp.x) || float.IsInfinity(bp.x);
            sb.Append($" | {(i == 0 ? "L" : "R")} raw {raw.Position} at {(VrHands.HoldAt[i].HasValue ? VrHands.HoldAt[i].Value.ToString() : "-")} rot {(VrHands.HoldRot[i].HasValue ? VrHands.HoldRot[i].Value.eulerAngles.ToString() : "-")} bone {bp}{(nan ? " NaN!" : "")} shown {(bone != null && bone.gameObject.activeInHierarchy)}");
        }

        return sb.ToString();
    }

    public static string Describe() => (Ready ? $"scale {_skin.Scale:F3} rest eyes {_skin.RestEyeHeight:F2} {_skin.DescribeArms()} | " : "no skin | ") + Sk.Describe();
}
