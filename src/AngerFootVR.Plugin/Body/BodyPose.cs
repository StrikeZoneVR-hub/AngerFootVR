using AngerFootVR.Core;
using Framework;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// Poses the VR body every frame, after the VR rig has placed your head (the way InkVR poses its bodies):
/// - the body stands under your head, a little behind your eyes, scaled so his eyes are at the game's eye height, and
///   turns with your head (it lets your head turn 40 degrees on its own, more when you walk);
/// - the torso leans towards your head, and when you crouch for real the hips drop and the knees bend;
/// - the arms reach from the shoulders to your controllers, the hands turned as the controllers are (at the sides when
///   there are none);
/// - the feet plant on the ground and step when you shuffle or turn; running, they stride with your speed;
/// - when the game kicks (A/X), his leg swings out with it.
/// His head is never drawn for you (you look out of it). Each frame starts from the humanoid rig's neutral pose.
/// </summary>
internal static class BodyPose
{
    private static Transform _root, _head, _hips, _spine, _chest, _neck;
    private static readonly Transform[] UpperArm = new Transform[2], LowerArm = new Transform[2], Hand = new Transform[2];
    private static readonly Transform[] UpperLeg = new Transform[2], LowerLeg = new Transform[2], Foot = new Transform[2];
    private static readonly Quaternion[] HandOffset = new Quaternion[2], FootToRoot = new Quaternion[2];
    private static Animator _animator;
    private static HumanPoseHandler _pose;
    private static HumanPose _neutral;
    private static Vector3 _baseScale = Vector3.one;
    private static float _modelEyeHeight = 1.6f, _hipHalfWidth = 0.1f, _ankleHeight = 0.08f, _legLength = 0.9f, _palmReach = 0.08f;
    private static bool _showHead;

    /// <summary>Dev: which parts of the posing run ("bodyik" command): 0 neutral only, 1 + legs/torso, 2 + helpers, 3 all.</summary>
    public static int DevStage = 3;
    private static float _hipHeight = 0.9f; // the hips' height standing (own scale)

    // helper bones (twist joints the humanoid map doesn't drive): each follows its main bone as in the bind pose
    private static Transform[] _helpers = new Transform[0], _drivers = new Transform[0];
    private static Matrix4x4[] _helperOffsets = new Matrix4x4[0];

    // body yaw and walking
    private static float _bodyYaw;
    private static bool _yawSet;
    private static Vector3 _lastRoot;
    private static float _phase;
    private static readonly Vector3[] Planted = new Vector3[2], StepFrom = new Vector3[2];
    private static readonly float[] StepT = { 1f, 1f };
    private static float _kickT = 1f;
    private static int _kickLeg = 1;

    /// <summary>How far behind your eyes the body stands (metres, at the game's scale).</summary>
    public const float BehindEyes = 0.12f;

    private const float FreeHeadTurn = 40f, StepTime = 0.18f, KickTime = 0.38f;

    private static int GroundMask => Physics.DefaultRaycastLayers & ~((1 << 10) | (1 << 11) | (1 << 13) | (1 << 14) | (1 << 15) | (1 << 17) | (1 << 18) | (1 << 22) | (1 << 5));

    public static void Attach(Transform root, Animator animator)
    {
        _root = root;
        _animator = animator;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        _head = B(HumanBodyBones.Head);
        _hips = B(HumanBodyBones.Hips);
        _spine = B(HumanBodyBones.Spine);
        _chest = B(HumanBodyBones.Chest) ?? _spine;
        _neck = B(HumanBodyBones.Neck) ?? _head;
        UpperArm[0] = B(HumanBodyBones.LeftUpperArm);
        LowerArm[0] = B(HumanBodyBones.LeftLowerArm);
        Hand[0] = B(HumanBodyBones.LeftHand);
        UpperArm[1] = B(HumanBodyBones.RightUpperArm);
        LowerArm[1] = B(HumanBodyBones.RightLowerArm);
        Hand[1] = B(HumanBodyBones.RightHand);
        UpperLeg[0] = B(HumanBodyBones.LeftUpperLeg);
        LowerLeg[0] = B(HumanBodyBones.LeftLowerLeg);
        Foot[0] = B(HumanBodyBones.LeftFoot);
        UpperLeg[1] = B(HumanBodyBones.RightUpperLeg);
        LowerLeg[1] = B(HumanBodyBones.RightLowerLeg);
        Foot[1] = B(HumanBodyBones.RightFoot);

        _pose = new HumanPoseHandler(animator.avatar, root);
        _pose.GetHumanPose(ref _neutral);
        for (var i = 0; i < _neutral.muscles.Length; i++) _neutral.muscles[i] = 0f;
        _baseScale = root.localScale;
        _root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Neutral();

        // measurements at the model's own scale, in its neutral pose (facing +z)
        var eyeL = B(HumanBodyBones.LeftEye);
        var eye = eyeL != null ? eyeL : _head;
        var soles = Mathf.Min(Low(HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot), Low(HumanBodyBones.RightToes, HumanBodyBones.RightFoot));
        _modelEyeHeight = eye != null ? eye.position.y - soles : 1.6f;
        if (_modelEyeHeight < 0.2f) _modelEyeHeight = 1.6f;
        _hipHalfWidth = Mathf.Abs(UpperLeg[1].position.x - UpperLeg[0].position.x) * 0.5f;
        _ankleHeight = Mathf.Max(0.02f, (Foot[0].position.y + Foot[1].position.y) * 0.5f - soles);
        _legLength = (LowerLeg[1].position - UpperLeg[1].position).magnitude + (Foot[1].position - LowerLeg[1].position).magnitude;
        _hipHeight = (_hips.position.y - UpperLeg[1].position.y) + _legLength * 0.98f + _ankleHeight;
        FindHelpers();
        for (var s = 0; s < 2; s++)
        {
            FootToRoot[s] = Foot[s].rotation; // the root is unturned here
            HandOffset[s] = HandBasisOffset(s, out var reach);
            _palmReach = reach;
        }

        _yawSet = false;
        Plugin.L.LogInfo($"VR body: eye height {_modelEyeHeight:F2} m, legs {_legLength:F2} m, hips {_hipHalfWidth * 2:F2} m wide (own scale {_baseScale.x:F3})");
    }

    /// <summary>
    /// The hand bone's rotation relative to a "controller" frame (forward = the fingers' way, up = the back of the hand),
    /// taken from the neutral pose, so the hand can be turned as the controller is.
    /// </summary>
    private static Quaternion HandBasisOffset(int side, out float reach)
    {
        var hand = Hand[side];
        var middle = _animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        var thumb = _animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        var fingers = middle != null ? middle.position - hand.position : hand.right * (side == 0 ? -1f : 1f);
        reach = middle != null ? fingers.magnitude * 1.2f : 0.08f;
        var toThumb = thumb != null ? thumb.position - hand.position : Vector3.forward;
        var palmSide = Vector3.Cross(fingers.normalized, toThumb.normalized);
        var back = side == 0 ? palmSide : -palmSide;
        if (back.sqrMagnitude < 1e-6f) back = Vector3.up;
        return Quaternion.Inverse(Quaternion.LookRotation(fingers.normalized, back.normalized)) * hand.rotation;
    }

    /// <summary>
    /// Twist joints such as ShoulderPart1_R or ElbowPart2_L skin the sleeves but aren't in the humanoid map, so posing
    /// the arms would leave them (and the sleeves) behind. Each is tied to its main bone (Shoulder_R, Elbow_L) the way
    /// the mesh's bind pose has them.
    /// </summary>
    private static void FindHelpers()
    {
        var helpers = new System.Collections.Generic.List<Transform>();
        var drivers = new System.Collections.Generic.List<Transform>();
        var offsets = new System.Collections.Generic.List<Matrix4x4>();
        var byName = new System.Collections.Generic.Dictionary<string, Transform>();
        foreach (var t in _root.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
        foreach (var smr in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones;
            var binds = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
            if (bones == null || binds == null || binds.Length != bones.Length) continue;
            var index = new System.Collections.Generic.Dictionary<Transform, int>();
            for (var i = 0; i < bones.Length; i++)
                if (bones[i] != null)
                    index[bones[i]] = i;
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null || helpers.Contains(bone)) continue;
                var m = System.Text.RegularExpressions.Regex.Match(bone.name, @"^(.*?)Part\d+(_[LR])$");
                if (!m.Success || !byName.TryGetValue(m.Groups[1].Value + m.Groups[2].Value, out var driver) || !index.TryGetValue(driver, out var di)) continue;
                // bind pose: world(bone) = inverse(bindpose); the helper relative to its driver
                helpers.Add(bone);
                drivers.Add(driver);
                offsets.Add(binds[di] * binds[i].inverse);
            }
        }

        // dev: which meshes use bones the posing doesn't reach (not under the hips, not a followed helper)
        foreach (var smr in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var outside = new System.Collections.Generic.List<string>();
            foreach (var b in smr.bones)
                if (b != null && !b.IsChildOf(_hips) && !helpers.Contains(b))
                    outside.Add(b.name);
            Plugin.L.LogInfo($"SKIN {smr.name} root {(smr.rootBone != null ? smr.rootBone.name : "-")} bones {smr.bones.Length} unposed {outside.Count}: {string.Join(",", outside.GetRange(0, System.Math.Min(12, outside.Count)).ToArray())}");
        }

        var dup = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Transform>>();
        foreach (var t in _root.GetComponentsInChildren<Transform>(true))
        {
            if (!dup.TryGetValue(t.name, out var l)) dup[t.name] = l = new System.Collections.Generic.List<Transform>();
            l.Add(t);
        }

        foreach (var kv in dup)
            if (kv.Value.Count > 1)
                Plugin.L.LogInfo($"SKIN duplicate '{kv.Key}' x{kv.Value.Count}: {string.Join(" | ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(kv.Value, t => (t.parent != null ? t.parent.name : "-") + (t == UpperArm[1] || t == UpperArm[0] || t == _hips ? "*" : ""))))}");
        var hipsPath = new System.Text.StringBuilder();
        for (var t = _hips; t != null && t != _root; t = t.parent) hipsPath.Insert(0, "/" + t.name);
        Plugin.L.LogInfo($"SKIN hips path {hipsPath}; humanoid arm {UpperArm[1].name} parent {UpperArm[1].parent.name}");
        _helpers = helpers.ToArray();
        _drivers = drivers.ToArray();
        _helperOffsets = offsets.ToArray();
        Plugin.L.LogInfo($"VR body: {_helpers.Length} twist joints follow their arm and leg bones ({string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(_helpers, h => h.name)))})");
    }

    private static void FollowHelpers()
    {
        for (var i = 0; i < _helpers.Length; i++)
        {
            var w = _drivers[i].localToWorldMatrix * _helperOffsets[i];
            _helpers[i].SetPositionAndRotation(w.GetColumn(3), w.rotation);
        }
    }

    private static float Low(HumanBodyBones a, HumanBodyBones b)
    {
        var ta = _animator.GetBoneTransform(a);
        var tb = _animator.GetBoneTransform(b);
        return Mathf.Min(ta != null ? ta.position.y : 0f, tb != null ? tb.position.y : 0f);
    }

    private static void Neutral()
    {
        _neutral.bodyPosition = new Vector3(0f, _neutral.bodyPosition.y, 0f);
        _neutral.bodyRotation = Quaternion.identity;
        _pose.SetHumanPose(ref _neutral);
    }

    /// <summary>The game kicked: swing a leg out (the right one, unless it is mid-step).</summary>
    public static void Kick()
    {
        _kickLeg = StepT[1] < 1f ? 0 : 1;
        _kickT = 0f;
    }

    /// <summary>Dev: draw the head too (for pictures of the whole body).</summary>
    public static void ShowHead(bool show) => _showHead = show;

    /// <summary>Called right after the VR rig placed the head this frame.</summary>
    public static void Pose()
    {
        if (_root == null || !VrBody.Active || !SingletonBehaviour<Player>.HasInstance) return;
        VrHands.Update();
        var dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var player = SingletonBehaviour<Player>.Instance;
        var feet = player.transform.position;
        var gameEye = player.GetEyePosition(alive: true).y - feet.y;
        var scale = Mathf.Clamp(gameEye / _modelEyeHeight, 0.25f, 4f);
        var headPos = VrRig.HeadPosition;
        var headRot = VrRig.HeadRotation;

        // body yaw: follows the head, which may turn 40 degrees on its own (all the way when walking)
        var headFwd = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
        if (headFwd.sqrMagnitude < 1e-4f) headFwd = Vector3.ProjectOnPlane(headRot * Vector3.up, Vector3.up);
        var headYaw = Mathf.Atan2(headFwd.x, headFwd.z) * Mathf.Rad2Deg;
        var speed = Vector3.ProjectOnPlane(feet - _lastRoot, Vector3.up).magnitude / dt;
        if (!_yawSet || speed > 30f)
        {
            _bodyYaw = headYaw; // first frame, or a teleport
            _yawSet = true;
            speed = 0f;
            Planted[0] = Planted[1] = Vector3.zero;
        }

        var off = Mathf.DeltaAngle(_bodyYaw, headYaw);
        if (speed > 0.5f) _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, headYaw, 360f * dt);
        else if (Mathf.Abs(off) > FreeHeadTurn) _bodyYaw += off - Mathf.Sign(off) * FreeHeadTurn;
        var bodyRot = Quaternion.Euler(0f, _bodyYaw, 0f);
        var fwd = bodyRot * Vector3.forward;

        // place the body and start from the neutral pose
        _root.localScale = _baseScale * scale;
        var under = headPos - fwd * (BehindEyes * scale);
        var rootPos = new Vector3(under.x, feet.y, under.z);
        var velocity = Vector3.ProjectOnPlane(feet - _lastRoot, Vector3.up) / dt;
        _root.SetPositionAndRotation(rootPos, bodyRot);
        Neutral();

        // the hips at standing height (they drop by however much lower your head is than the game's eye height)
        var crouch = Mathf.Clamp(gameEye - (headPos.y - feet.y), 0f, _legLength * scale * 0.55f);
        var hp = _hips.position;
        hp.y = rootPos.y + _hipHeight * scale - crouch;
        _hips.position = hp;

        // the torso leans towards your head: the neck goes to just below and behind your eyes
        var neckTarget = headPos + headRot * (new Vector3(0f, -0.11f, -0.08f) * scale);
        if (DevStage >= 1)
        {
            BodyIk.Lean(_spine, _neck.position, neckTarget, 0.55f);
            BodyIk.Lean(_chest, _neck.position, neckTarget, 0.8f);
        }

        if (DevStage >= 1) Legs(rootPos, bodyRot, scale, dt, speed, velocity);
        if (DevStage >= 3) Arms(bodyRot, scale);

        if (DevStage >= 2) FollowHelpers();
        if (_head != null) _head.localScale = _showHead ? Vector3.one : Vector3.one * 0.001f; // you look out of it
        _lastRoot = feet;
    }

    private static void Arms(Quaternion bodyRot, float scale)
    {
        for (var s = 0; s < 2; s++)
        {
            var pose = s == 0 ? VrHands.Left : VrHands.Right;
            var sign = s == 0 ? -1f : 1f;
            var shoulder = UpperArm[s].position;
            Vector3 wrist;
            Quaternion handRot;
            if (pose.Tracked)
            {
                // the controller's grip is in the palm: the wrist is back along the fingers; the palm faces in
                var ctrlFwd = pose.Rotation * Vector3.forward;
                var ctrlBack = pose.Rotation * (sign > 0 ? Vector3.right : Vector3.left);
                handRot = Quaternion.LookRotation(ctrlFwd, ctrlBack) * HandOffset[s];
                wrist = pose.Position - ctrlFwd * (_palmReach * 0.6f * scale);
            }
            else
            {
                // no controller: the arm hangs at the side
                wrist = shoulder + bodyRot * (new Vector3(sign * 0.12f, -0.55f, 0.05f) * scale);
                handRot = bodyRot * Quaternion.LookRotation(Vector3.down, new Vector3(sign, 0f, 0f)) * HandOffset[s];
            }

            var pole = shoulder + bodyRot * (new Vector3(sign * 0.35f, -0.5f, -0.45f) * scale);
            BodyIk.TwoBone(UpperArm[s], LowerArm[s], Hand[s], wrist, pole);
            Hand[s].rotation = handRot;
        }
    }

    private static void Legs(Vector3 rootPos, Quaternion bodyRot, float scale, float dt, float speed, Vector3 velocity)
    {
        var running = speed > 1.2f;
        var stride = Mathf.Clamp(speed * 0.32f, 0.45f, 1.1f);
        if (running) _phase = Mathf.Repeat(_phase + dt * speed / stride * Mathf.PI, Mathf.PI * 2f);
        _kickT = Mathf.Min(1f, _kickT + dt / KickTime);

        for (var s = 0; s < 2; s++)
        {
            var sign = s == 0 ? -1f : 1f;
            var stance = rootPos + bodyRot * new Vector3(sign * _hipHalfWidth * scale, 0f, 0f);
            Vector3 target;
            if (running)
            {
                // a stride cycle along the way you're going, the feet half a cycle apart
                var ph = _phase + (s == 0 ? 0f : Mathf.PI);
                Planted[s] = Ground(stance + velocity.normalized * (Mathf.Cos(ph) * stride * 0.5f * scale), scale);
                StepT[s] = 1f;
                target = Planted[s] + Vector3.up * (Mathf.Max(0f, Mathf.Sin(ph)) * 0.18f * scale);
            }
            else
            {
                // planted: step (one foot at a time) when the body has moved or turned away from the foot
                if (Planted[s] == Vector3.zero) Planted[s] = Ground(stance, scale);
                var other = 1 - s;
                if (StepT[s] >= 1f && StepT[other] >= 1f && (Planted[s] - stance).sqrMagnitude > Sq(0.2f * scale))
                {
                    StepFrom[s] = Planted[s];
                    StepT[s] = 0f;
                }

                if (StepT[s] < 1f)
                {
                    StepT[s] = Mathf.Min(1f, StepT[s] + dt / StepTime);
                    var to = Ground(stance + velocity * 0.1f, scale);
                    var t = StepT[s];
                    target = Vector3.Lerp(StepFrom[s], to, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.09f * scale);
                    if (t >= 1f) Planted[s] = to;
                }
                else target = Planted[s];
            }

            // the kick: out in front at hip height and back
            if (s == _kickLeg && _kickT < 1f)
            {
                var k = Mathf.Sin(_kickT * Mathf.PI);
                var kickAt = UpperLeg[s].position + bodyRot * (new Vector3(0f, -0.15f, 0.95f) * (_legLength * scale));
                target = Vector3.Lerp(target, kickAt, k);
            }

            var pole = UpperLeg[s].position + bodyRot * (new Vector3(sign * 0.1f, 0f, 1f) * scale);
            BodyIk.TwoBone(UpperLeg[s], LowerLeg[s], Foot[s], target + Vector3.up * (_ankleHeight * scale), pole);
            Foot[s].rotation = bodyRot * FootToRoot[s];
        }
    }

    /// <summary>The ground under a point (the floor, stairs, a ledge), or the point itself.</summary>
    private static Vector3 Ground(Vector3 p, float scale)
    {
        var from = p + Vector3.up * (0.6f * scale);
        return Physics.Raycast(from, Vector3.down, out var hit, 1.4f * scale, GroundMask, QueryTriggerInteraction.Ignore)
            ? hit.point
            : p;
    }

    private static float Sq(float x) => x * x;
}
