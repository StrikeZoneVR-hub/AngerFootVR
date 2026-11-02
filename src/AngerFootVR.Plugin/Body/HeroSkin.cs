using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// Anger Foot's own model worn on the Skeleton: InkVR's CharacterSkin (the way it puts Bendy, Boris and the rest on
/// Henry's skeleton), for the hero. Every frame:
/// - the head turns with yours (up, down, round, tilted), its eyes at yours;
/// - the neck sits where the skeleton's is (looking down, it drops back, by however far the jacket stands out in
///   front, so the chest leaves your view and your legs come into it), and the torso hangs from it along the spine;
/// - the legs reach from the hips to where the skeleton's feet are planted (at this body's own size), knees bending
///   forward over flat feet;
/// - the arms reach from the shoulders to your hands by two-bone IK, elbows down and back, the collarbone swinging
///   forward on a long reach; the forearm's twist bones take part of the wrist's turn so it doesn't wring;
/// - each hand is turned as your controller is (InkVR's grip-to-hand frame), its palm on the grip, and the fingers
///   curl with the trigger and grip.
/// Bones nothing drives go with the nearest bone that is. The body you see and its mirror twin (with the head) take
/// the same pose. Rest pose: the meshes' bind pose, the model standing at the origin facing +z.
/// </summary>
internal sealed class HeroSkin
{
    private enum R
    {
        Hips, Chest, Neck, Head,
        LClav, LUpper, LFore, LHand, RClav, RUpper, RFore, RHand,
        LThigh, LShin, LFoot, LToe, RThigh, RShin, RFoot, RToe,
        Count
    }

    /// <summary>A hand this frame: the wrist (z along the fingers, y out of the back of the hand) and the curls.</summary>
    public struct HandPose
    {
        public bool Valid;
        public Vector3 Wrist;
        public Quaternion Rot;
        public float Scale; // world units per metre of hand
        public float Index, Grip, Thumb;
    }

    private readonly Transform _root, _twinRoot;
    private readonly List<Transform> _all = new List<Transform>();
    private Transform[] _twin;
    private readonly Dictionary<Transform, int> _index = new Dictionary<Transform, int>();
    private Vector3[] _rp;
    private Quaternion[] _rr, _rl;
    private int[] _depth;
    private readonly int[] _role = new int[(int)R.Count];
    private readonly List<int> _torso = new List<int>();
    private readonly int[][] _fingers = new int[10][];
    private readonly List<(int bone, float along)>[] _foreTwist = { new List<(int, float)>(), new List<(int, float)>() };
    private Vector3 _restEye;
    private float _restEyeY, _floor, _chestDepth;

    private Vector3[] _tp;
    private Quaternion[] _tq;
    private bool[] _set;
    private int[] _anchor;
    private readonly List<int> _order = new List<int>();
    private readonly float[] _twist = new float[2];

    public float Scale { get; private set; } = 1f;

    /// <summary>Dev: each wrist's turn about its forearm (degrees), and the hand's frame on the forearm.</summary>
    public string DescribeArms() => $"twist L {_twist[0]:F0} R {_twist[1]:F0}";
    public bool Ok { get; private set; }
    public float RestEyeHeight => _restEyeY;
    public Transform Head => _role[(int)R.Head] >= 0 ? _all[_role[(int)R.Head]] : null;
    public Transform Neck => _role[(int)R.Neck] >= 0 ? _all[_role[(int)R.Neck]] : null;
    public Transform HandBone(int side) => _all[_role[side == 0 ? (int)R.LHand : (int)R.RHand]];

    public HeroSkin(Transform root, Animator animator, Transform twinRoot)
    {
        _root = root;
        _twinRoot = twinRoot;
        try
        {
            Ok = Setup(animator);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogError("VR body: the model couldn't be set up: " + e);
            Ok = false;
        }
    }

    // =====================================================================================================
    // Setup: the rest pose, the bones by role
    // =====================================================================================================

    private bool Setup(Animator animator)
    {
        _root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _root.localScale = Vector3.one;
        foreach (var t in _root.GetComponentsInChildren<Transform>(true))
        {
            if (t == _root) continue;
            _index[t] = _all.Count;
            _all.Add(t);
        }

        if (_twinRoot != null)
        {
            var twin = new List<Transform>();
            foreach (var t in _twinRoot.GetComponentsInChildren<Transform>(true))
                if (t != _twinRoot)
                    twin.Add(t);
            _twin = twin.Count == _all.Count ? twin.ToArray() : null;
            if (_twin == null) Plugin.L.LogWarning("VR body: the mirror twin's bones don't match");
        }

        var n = _all.Count;
        _rp = new Vector3[n];
        _rr = new Quaternion[n];
        _rl = new Quaternion[n];
        _depth = new int[n];
        _tp = new Vector3[n];
        _tq = new Quaternion[n];
        _set = new bool[n];
        _anchor = new int[n];
        for (var i = 0; i < n; i++)
        {
            var d = 0;
            for (var p = _all[i].parent; p != null && p != _root; p = p.parent) d++;
            _depth[i] = d;
        }

        ToBindPose();
        for (var i = 0; i < n; i++)
        {
            _rp[i] = _all[i].position;
            _rr[i] = _all[i].rotation;
            _rl[i] = _all[i].localRotation;
        }

        // ---- roles, from the humanoid map ----
        int Of(HumanBodyBones b)
        {
            var t = animator.GetBoneTransform(b);
            return t != null && _index.TryGetValue(t, out var i) ? i : -1;
        }

        int First(params HumanBodyBones[] bs)
        {
            foreach (var b in bs)
            {
                var i = Of(b);
                if (i >= 0) return i;
            }

            return -1;
        }

        _role[(int)R.Hips] = Of(HumanBodyBones.Hips);
        _role[(int)R.Chest] = First(HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.Spine);
        _role[(int)R.Neck] = Of(HumanBodyBones.Neck);
        _role[(int)R.Head] = Of(HumanBodyBones.Head);
        _role[(int)R.LClav] = Of(HumanBodyBones.LeftShoulder);
        _role[(int)R.LUpper] = Of(HumanBodyBones.LeftUpperArm);
        _role[(int)R.LFore] = Of(HumanBodyBones.LeftLowerArm);
        _role[(int)R.LHand] = Of(HumanBodyBones.LeftHand);
        _role[(int)R.RClav] = Of(HumanBodyBones.RightShoulder);
        _role[(int)R.RUpper] = Of(HumanBodyBones.RightUpperArm);
        _role[(int)R.RFore] = Of(HumanBodyBones.RightLowerArm);
        _role[(int)R.RHand] = Of(HumanBodyBones.RightHand);
        _role[(int)R.LThigh] = Of(HumanBodyBones.LeftUpperLeg);
        _role[(int)R.LShin] = Of(HumanBodyBones.LeftLowerLeg);
        _role[(int)R.LFoot] = Of(HumanBodyBones.LeftFoot);
        _role[(int)R.LToe] = Of(HumanBodyBones.LeftToes);
        _role[(int)R.RThigh] = Of(HumanBodyBones.RightUpperLeg);
        _role[(int)R.RShin] = Of(HumanBodyBones.RightLowerLeg);
        _role[(int)R.RFoot] = Of(HumanBodyBones.RightFoot);
        _role[(int)R.RToe] = Of(HumanBodyBones.RightToes);
        foreach (var r in new[] { R.Hips, R.Chest, R.Head, R.LUpper, R.LFore, R.LHand, R.RUpper, R.RFore, R.RHand, R.LThigh, R.LShin, R.LFoot, R.RThigh, R.RShin, R.RFoot })
            if (_role[(int)r] < 0)
            {
                Plugin.L.LogWarning($"VR body: no {r} bone");
                return false;
            }

        // ---- facing +z (the posing assumes it) ----
        var across = _rp[_role[(int)R.RUpper]] - _rp[_role[(int)R.LUpper]];
        var facing = Vector3.Cross(across, Vector3.up);
        facing.y = 0;
        if (facing.sqrMagnitude > 1e-10f)
        {
            var yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(yaw) > 1f)
            {
                _root.rotation = Quaternion.Euler(0, -yaw, 0);
                for (var i = 0; i < n; i++)
                {
                    _rp[i] = _all[i].position;
                    _rr[i] = _all[i].rotation;
                }
            }
        }

        // ---- the floor: under the soles ----
        _floor = float.MaxValue;
        foreach (var smr in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable || !smr.gameObject.activeSelf) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (var v in baked.vertices) _floor = Mathf.Min(_floor, toWorld.MultiplyPoint3x4(v).y);
            Object.Destroy(baked);
        }

        if (_floor == float.MaxValue)
        {
            _floor = Mathf.Min(_rp[_role[(int)R.LFoot]].y, _rp[_role[(int)R.RFoot]].y);
            if (_role[(int)R.LToe] >= 0) _floor = Mathf.Min(_floor, _rp[_role[(int)R.LToe]].y);
            _floor -= 0.03f;
        }

        // ---- the torso, bottom to top: the hips, the spine bones between them and the chest, the chest, neck, head ----
        _torso.Add(_role[(int)R.Hips]);
        var chain = new List<int>();
        for (var t = _all[_role[(int)R.Chest]].parent; t != null && t != _all[_role[(int)R.Hips]] && t != _root; t = t.parent)
            if (_index.TryGetValue(t, out var ti))
                chain.Insert(0, ti);
        if (_all[_role[(int)R.Chest]].IsChildOf(_all[_role[(int)R.Hips]])) _torso.AddRange(chain);
        _torso.Add(_role[(int)R.Chest]);
        if (_role[(int)R.Neck] >= 0) _torso.Add(_role[(int)R.Neck]);
        _torso.Add(_role[(int)R.Head]);

        // ---- fingers, base first: index, middle, ring, little, thumb ----
        var names = new[] { "Index", "Middle", "Ring", "Little", "Thumb" };
        for (var side = 0; side < 2; side++)
        for (var f = 0; f < 5; f++)
        {
            var list = new List<int>();
            foreach (var part in new[] { "Proximal", "Intermediate", "Distal" })
            {
                var i = Of((HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), (side == 0 ? "Left" : "Right") + names[f] + part));
                if (i >= 0) list.Add(i);
            }

            _fingers[side * 5 + f] = list.Count > 0 ? list.ToArray() : null;
        }

        // ---- forearm twist bones (ElbowPart1_L ...): along the forearm, each takes its share of the wrist's turn ----
        for (var side = 0; side < 2; side++)
        {
            var fore = _role[side == 0 ? (int)R.LFore : (int)R.RFore];
            var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
            var a = _rp[fore];
            var b = _rp[hand];
            var len = (b - a).magnitude;
            for (var i = 0; i < n; i++)
            {
                var nm = _all[i].name.ToLowerInvariant();
                if (i == fore || i == hand || !(nm.Contains("part") || nm.Contains("twist")) || !(nm.Contains("elbow") || nm.Contains("forearm") || nm.Contains("lowerarm"))) continue;
                if (!nm.EndsWith(side == 0 ? "_l" : "_r") && !nm.Contains(side == 0 ? "left" : "right")) continue;
                var t = Vector3.Dot(_rp[i] - a, (b - a) / Mathf.Max(len, 1e-4f)) / Mathf.Max(len, 1e-4f);
                _foreTwist[side].Add((i, Mathf.Clamp01(t)));
            }
        }

        BuildOrder();

        // ---- the eyes: between the eye bones, or at the front of the head ----
        var le = Of(HumanBodyBones.LeftEye);
        var re = Of(HumanBodyBones.RightEye);
        var head = _role[(int)R.Head];
        if (le >= 0 && re >= 0) _restEye = (_rp[le] + _rp[re]) * 0.5f;
        else
        {
            var eyeL = _all.FindIndex(t => t.name == "Eye_L");
            var eyeR = _all.FindIndex(t => t.name == "Eye_R");
            if (eyeL >= 0 && eyeR >= 0) _restEye = (_rp[eyeL] + _rp[eyeR]) * 0.5f;
            else _restEye = _rp[head] + new Vector3(0, 0.07f, 0.09f);
        }

        _restEyeY = Mathf.Max(0.1f, _restEye.y - _floor);
        _chestDepth = ChestDepth();
        Plugin.L.LogInfo($"VR body (InkVR's posing): {n} bones, eyes {_restEyeY:F2} m up, torso {_torso.Count} bones ({string.Join(" ", _torso.ConvertAll(i => _all[i].name).ToArray())}), " +
                         $"fingers {Count(_fingers)}, forearm twist L {_foreTwist[0].Count} R {_foreTwist[1].Count}, chest depth {_chestDepth:F2}, twin {(_twin != null ? "posed with it" : "none")}");
        return true;
    }

    private static int Count(int[][] f)
    {
        var c = 0;
        foreach (var x in f)
            if (x != null)
                c++;
        return c;
    }

    /// <summary>Every skinned bone put where the meshes were bound to it, parents first.</summary>
    private void ToBindPose()
    {
        var bind = new Dictionary<Transform, Matrix4x4>();
        foreach (var smr in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var bones = smr.bones;
            var poses = mesh.bindposes;
            var meshToWorld = smr.transform.localToWorldMatrix;
            for (var i = 0; i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null && !bind.ContainsKey(bones[i]))
                    bind[bones[i]] = meshToWorld * poses[i].inverse;
        }

        var order = new List<int>();
        for (var i = 0; i < _all.Count; i++)
            if (bind.ContainsKey(_all[i]))
                order.Add(i);
        order.Sort((a, b) => _depth[a].CompareTo(_depth[b]));
        foreach (var i in order)
        {
            var m = bind[_all[i]];
            var pos = new Vector3(m.m03, m.m13, m.m23);
            var fwd = new Vector3(m.m02, m.m12, m.m22);
            var up = new Vector3(m.m01, m.m11, m.m21);
            if (fwd.sqrMagnitude < 1e-12f || up.sqrMagnitude < 1e-12f) continue;
            _all[i].SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up));
        }
    }

    /// <summary>How far the jacket stands out in front of the neck at rest (less what Henry's drawn chest does).</summary>
    private float ChestDepth()
    {
        var neck = _role[(int)R.Neck] >= 0 ? _rp[_role[(int)R.Neck]] : _rp[_role[(int)R.Chest]];
        var lo = _rp[_role[(int)R.Hips]].y;
        var front = neck.z;
        var any = false;
        foreach (var smr in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable || !smr.gameObject.activeSelf) continue;
            var baked = new Mesh();
            smr.BakeMesh(baked);
            var toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (var v0 in baked.vertices)
            {
                var v = toWorld.MultiplyPoint3x4(v0);
                if (v.y > lo && v.y < neck.y && Mathf.Abs(v.x - neck.x) < 0.12f * _restEyeY)
                {
                    front = Mathf.Max(front, v.z);
                    any = true;
                }
            }

            Object.Destroy(baked);
        }

        var height = Mathf.Max(0.1f, _restEyeY);
        if (!any) return 0.08f * height / 1.62f;
        return Mathf.Clamp(front - neck.z - 0.05f * height / 1.62f, 0f, 0.25f * height / 1.62f);
    }

    /// <summary>Which bones are driven; the rest hanging off nothing driven go with the nearest one that is.</summary>
    private void BuildOrder()
    {
        var driven = new bool[_all.Count];
        foreach (var i in _role)
            if (i >= 0)
                driven[i] = true;
        foreach (var i in _torso) driven[i] = true;
        foreach (var side in _foreTwist)
        foreach (var t in side)
            driven[t.bone] = true;

        for (var i = 0; i < _all.Count; i++)
        {
            _anchor[i] = -1;
            if (driven[i] || HasDrivenAncestor(i, driven) || IsAncestorOfDriven(i, driven)) continue;
            var best = -1;
            var bd = float.MaxValue;
            for (var j = 0; j < _all.Count; j++)
            {
                if (!driven[j]) continue;
                var d = (_rp[j] - _rp[i]).sqrMagnitude;
                if (d < bd)
                {
                    bd = d;
                    best = j;
                }
            }

            _anchor[i] = best;
        }

        for (var i = 0; i < _all.Count; i++)
            if (driven[i] || _anchor[i] >= 0)
                _order.Add(i);
        _order.Sort((a, b) => _depth[a].CompareTo(_depth[b]));
    }

    private bool HasDrivenAncestor(int i, bool[] driven)
    {
        for (var p = _all[i].parent; p != null && p != _root; p = p.parent)
            if (_index.TryGetValue(p, out var j) && driven[j])
                return true;
        return false;
    }

    private bool IsAncestorOfDriven(int i, bool[] driven)
    {
        for (var j = 0; j < _all.Count; j++)
        {
            if (!driven[j]) continue;
            for (var p = _all[j].parent; p != null && p != _root; p = p.parent)
                if (p == _all[i])
                    return true;
        }

        return false;
    }

    // =====================================================================================================
    // Posing
    // =====================================================================================================

    public void Apply(in Skeleton.Pose p, in HandPose left, in HandPose right, bool showHead)
    {
        if (!Ok) return;
        var s = Scale = 1.62f * p.K / _restEyeY;
        for (var i = 0; i < _set.Length; i++) _set[i] = false;

        // ---- the head: turned with yours, its eyes at yours ----
        var head = _role[(int)R.Head];
        var headRot = p.HeadRot * _rr[head];
        var eyeInHead = Quaternion.Inverse(_rr[head]) * (_restEye - _rp[head]);
        Target(head, p.Eye - headRot * eyeInHead * s, headRot);

        // ---- the base of the neck: where the skeleton's is, relative to your eyes, at this body's size; looking down
        // it drops back by however far the chest stands out, so the chest leaves your view ----
        var top = _torso.Count - 2;
        var topBone = _torso[top];
        var neck = p.Eye + (p.NeckBase - p.Eye) * ((_restEye - _rp[topBone]).magnitude * s / Mathf.Max((p.NeckBase - p.Eye).magnitude, 1e-4f));
        var lookDown = Mathf.Max(0f, -(p.HeadRot * Vector3.forward).y);
        var facing = p.Pelvis * Vector3.up;
        facing.y = 0;
        if (facing.sqrMagnitude > 1e-6f) neck -= facing.normalized * (_chestDepth * s * lookDown);
        var neckRest = _rp[head] - _rp[topBone];
        Target(topBone, neck, Look(_tp[head] - neck, p.Neck * Vector3.up) * Quaternion.Inverse(Look(neckRest, Vector3.forward)) * _rr[topBone]);

        // ---- the torso hangs from there along the skeleton's spine, each bone its own length ----
        var below = neck;
        var total = 0f;
        for (var j = 0; j < top; j++) total += (_rp[_torso[j + 1]] - _rp[_torso[j]]).magnitude;
        var along = total;
        for (var j = top - 1; j >= 0; j--)
        {
            var bone = _torso[j];
            var next = _torso[j + 1];
            var rest = _rp[next] - _rp[bone];
            var len = rest.magnitude;
            var u = (along - len * 0.5f) / Mathf.Max(total, 1e-4f);
            var frame = u < 0.3f ? p.Pelvis : u < 0.66f ? p.Spine : p.Chest;
            along -= len;
            var rot = frame * Quaternion.Inverse(Look(rest, Vector3.forward)) * _rr[bone];
            var pos = below - frame * Vector3.forward * (len * s);
            Target(bone, pos, rot);
            below = pos;
        }

        // ---- the legs, the arms and hands ----
        Leg(0, p, s);
        Leg(1, p, s);
        Arm(0, p, left, s);
        Arm(1, p, right, s);

        // ---- bones nothing drives go with the nearest one that is ----
        foreach (var i in _order)
        {
            var a = _anchor[i];
            if (a < 0 || !_set[a]) continue;
            var inv = Quaternion.Inverse(_rr[a]);
            Target(i, _tp[a] + _tq[a] * (inv * (_rp[i] - _rp[a])) * s, _tq[a] * inv * _rr[i]);
        }

        // ---- apply, parents first: the body you see (its head shrunk away) and the mirror twin (with it) ----
        var rootPos = _tp[_role[(int)R.Hips]];
        var rootRot = Quaternion.Euler(0, YawOf(p.Pelvis * Vector3.up), 0);
        ApplyTo(_root, _all, rootPos, rootRot, s, showHead);
        if (_twin != null) ApplyTo(_twinRoot, _twin, rootPos, rootRot, s, true);
        Fingers(0, left);
        Fingers(1, right);
    }

    private void ApplyTo(Transform root, IList<Transform> bones, Vector3 pos, Quaternion rot, float s, bool showHead)
    {
        root.SetPositionAndRotation(pos, rot);
        var parentScale = root.parent != null ? root.parent.lossyScale.x : 1f;
        root.localScale = Vector3.one * (s / Mathf.Max(parentScale, 1e-6f));
        var neck = _role[(int)R.Neck] >= 0 ? _role[(int)R.Neck] : _role[(int)R.Head];
        bones[neck].localScale = showHead ? Vector3.one : Vector3.one * 0.001f;
        foreach (var i in _order)
            if (_set[i])
                bones[i].SetPositionAndRotation(_tp[i], _tq[i]);
    }

    private void Target(int i, Vector3 pos, Quaternion rot)
    {
        _tp[i] = pos;
        _tq[i] = rot;
        _set[i] = true;
    }

    private static Quaternion Look(Vector3 dir, Vector3 up) =>
        dir.sqrMagnitude < 1e-10f ? Quaternion.identity : Quaternion.LookRotation(dir, Mathf.Abs(Vector3.Dot(dir.normalized, up.normalized)) > 0.98f ? Vector3.up : up);

    private static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    /// <summary>A child posed relative to its parent's rest, given where its parent is now.</summary>
    private void Carry(int parent, int child)
    {
        var inv = Quaternion.Inverse(_rr[parent]);
        Target(child, _tp[parent] + _tq[parent] * (inv * (_rp[child] - _rp[parent])) * Scale, _tq[parent] * inv * _rr[child]);
    }

    private void Leg(int side, in Skeleton.Pose p, float s)
    {
        var thigh = _role[side == 0 ? (int)R.LThigh : (int)R.RThigh];
        var shin = _role[side == 0 ? (int)R.LShin : (int)R.RShin];
        var foot = _role[side == 0 ? (int)R.LFoot : (int)R.RFoot];
        var toe = _role[side == 0 ? (int)R.LToe : (int)R.RToe];
        Carry(_role[(int)R.Hips], thigh);
        var hip = _tp[thigh];

        var henryHip = side == 0 ? p.HipL : p.HipR;
        var henryAnkle = side == 0 ? p.AnkleL : p.AnkleR;
        var henryKnee = side == 0 ? p.KneeL : p.KneeR;
        var footRot = side == 0 ? p.FootL : p.FootR;
        var ground = side == 0 ? p.GroundL : p.GroundR;

        var a = (_rp[shin] - _rp[thigh]).magnitude * s;
        var b = (_rp[foot] - _rp[shin]).magnitude * s;
        // this body's own stance (its rest foot under its hip, turned with the foot), moved by as much as the
        // skeleton's foot has stepped from its own, scaled to this body's legs
        var yaw = Quaternion.Euler(0, YawOf(Vector3.ProjectOnPlane(footRot * Vector3.forward, Vector3.up)), 0);
        var restOff = _rp[foot] - _rp[thigh];
        var stance = yaw * new Vector3(restOff.x, 0, restOff.z) * s;
        var henryStance = yaw * new Vector3(p.HenryAnkleOffset.x, 0, p.HenryAnkleOffset.z) * p.K;
        var stepped = henryAnkle - henryHip - henryStance;
        var lift = henryAnkle.y - (ground + 0.085f * p.K); // the skeleton's ankle above where it stands (a step, a kick)
        stepped.y = 0;
        var ratio = (a + b) / Mathf.Max(p.HenryLeg, 1e-4f);
        var ankle = hip + stance + stepped * ratio;
        ankle.y = ground + (_rp[foot].y - _floor) * s + lift * ratio;

        var pole = henryKnee - (henryHip + henryAnkle) * 0.5f;
        var knee = TwoBone(hip, ankle, a, b, pole, out ankle);
        var restBend = Vector3.forward;
        Target(thigh, hip, Look(knee - hip, pole) * Quaternion.Inverse(Look(_rp[shin] - _rp[thigh], restBend)) * _rr[thigh]);
        Target(shin, knee, Look(ankle - knee, pole) * Quaternion.Inverse(Look(_rp[foot] - _rp[shin], restBend)) * _rr[shin]);
        var restFoot = toe >= 0 ? _rp[toe] - _rp[foot] : Vector3.forward;
        restFoot.y = Mathf.Min(restFoot.y, 0);
        Target(foot, ankle, footRot * Quaternion.Inverse(Look(restFoot, Vector3.up)) * _rr[foot]);
    }

    private void Arm(int side, in Skeleton.Pose p, in HandPose h, float s)
    {
        var clav = _role[side == 0 ? (int)R.LClav : (int)R.RClav];
        var upper = _role[side == 0 ? (int)R.LUpper : (int)R.RUpper];
        var fore = _role[side == 0 ? (int)R.LFore : (int)R.RFore];
        var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
        var chest = _role[(int)R.Chest];
        if (clav >= 0)
        {
            Carry(chest, clav);
            Carry(clav, upper);
        }
        else Carry(chest, upper);

        var shoulder = _tp[upper];
        if (!h.Valid)
        {
            // no controller: the arm hangs at the side, relaxed
            var down = p.Chest * Vector3.back; // (the chest frame's z runs up the spine)
            var fwd = p.Chest * Vector3.up;
            var right = Vector3.Cross(down * -1f, fwd);
            var aLen = (_rp[fore] - _rp[upper]).magnitude * s;
            var bLen = (_rp[hand] - _rp[fore]).magnitude * s;
            var wristAt = shoulder + down * ((aLen + bLen) * 0.92f) + fwd * (0.08f * s) + right * ((side == 0 ? -0.05f : 0.05f) * s);
            var hangRot = Quaternion.LookRotation(down, side == 0 ? -right : right);
            ReachArm(side, upper, fore, hand, shoulder, wristAt, hangRot * Quaternion.Inverse(Canonical(side, out _)) * _rr[hand], hangRot, p, s);
            return;
        }

        // the hand turned as the controller's hand is, its palm where the controller's palm is
        var canon = Canonical(side, out var palm);
        var handRot = h.Rot * Quaternion.Inverse(canon) * _rr[hand];
        var palmWorld = h.Wrist + h.Rot * new Vector3(0, 0, 0.045f) * h.Scale;
        var wrist = palmWorld - handRot * palm * s;

        // reaching further than the arm: the shoulder comes forward first (the collarbone swings, as yours does)
        var aa = (_rp[fore] - _rp[upper]).magnitude * s;
        var bb = (_rp[hand] - _rp[fore]).magnitude * s;
        if (clav >= 0)
        {
            var over = (wrist - shoulder).magnitude - (aa + bb) * 0.97f;
            if (over > 0f)
            {
                var pivot = _tp[clav];
                var want = shoulder + (wrist - shoulder).normalized * Mathf.Min(over, 0.08f * s);
                var turn = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(shoulder - pivot, want - pivot), 30f);
                Target(clav, pivot, turn * _tq[clav]);
                Carry(clav, upper);
                shoulder = _tp[upper];
            }
        }

        ReachArm(side, upper, fore, hand, shoulder, wrist, handRot, h.Rot, p, s);
    }

    /// <param name="handFrame">The hand as a frame: z along the fingers, y out of the back of the hand.</param>
    private void ReachArm(int side, int upper, int fore, int hand, Vector3 shoulder, Vector3 wrist, Quaternion handRot, Quaternion handFrame, in Skeleton.Pose p, float s)
    {
        var a = (_rp[fore] - _rp[upper]).magnitude * s;
        var b = (_rp[hand] - _rp[fore]).magnitude * s;
        var restReach = _rp[hand] - _rp[upper];
        var restBend = Vector3.ProjectOnPlane(_rp[fore] - (_rp[upper] + _rp[hand]) * 0.5f, restReach);
        if (restBend.magnitude < restReach.magnitude * 0.08f) restBend = Vector3.back; // a straight T-pose arm: elbows bend backwards

        // the elbow: down and back, a little out (InkVR's), and turned with the hand as a real elbow follows the hand's
        // turn (palm down, it goes out; palm up, it tucks in): the rest pose's bend, turned as far as the hand has
        // turned from its rest. The forearm then rolls with the hand and the sleeve doesn't wring (this jacket's puffy
        // sleeves show every degree of it)
        var bodyUp = p.Chest * Vector3.forward;
        var bodyFwd = p.Chest * Vector3.up;
        var bodyRight = Vector3.Cross(bodyUp, bodyFwd);
        var bodyHint = (bodyRight * (side == 0 ? -0.2f : 0.2f) - bodyUp * 0.75f - bodyFwd * 0.35f).normalized;
        var handHint = (handRot * Quaternion.Inverse(_rr[hand]) * restBend).normalized;
        var hint = bodyHint * 0.35f + handHint * 0.85f;
        var reach = wrist - shoulder;
        Vector3 elbow;
        if (reach.magnitude >= (a + b) * 0.999f) elbow = shoulder + reach * (a / (a + b)); // too far: straight, stretched
        else elbow = TwoBone(shoulder, wrist, a, b, hint, out _);

        var bend = Vector3.ProjectOnPlane(hint, reach);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(elbow - (shoulder + wrist) * 0.5f, reach);
        Target(upper, shoulder, Look(elbow - shoulder, bend) * Quaternion.Inverse(Look(_rp[fore] - _rp[upper], restBend)) * _rr[upper]);
        var foreRot = Look(wrist - elbow, bend) * Quaternion.Inverse(Look(_rp[hand] - _rp[fore], restBend)) * _rr[fore];

        // the wrist's turn about the forearm: the twist bones take their share of it (with none, the forearm half)
        var axis = (wrist - elbow).normalized;
        var restAxis = (_rp[hand] - _rp[fore]).normalized;
        var restUp = Vector3.ProjectOnPlane(_rr[hand] * Vector3.up, restAxis);
        var foreUp = foreRot * (Quaternion.Inverse(_rr[fore]) * restUp);
        var handUp = handRot * (Quaternion.Inverse(_rr[hand]) * restUp);
        var twist = Vector3.SignedAngle(Vector3.ProjectOnPlane(foreUp, axis), Vector3.ProjectOnPlane(handUp, axis), axis);
        twist = Mathf.Clamp(_twist[side] + Mathf.DeltaAngle(_twist[side], twist), -160f, 160f); // continuous, never a half-turn flip
        _twist[side] = twist;
        if (_foreTwist[side].Count == 0) foreRot = Quaternion.AngleAxis(twist * 0.5f, axis) * foreRot;
        Target(fore, elbow, foreRot);
        Target(hand, wrist, handRot);
        var invFore = Quaternion.Inverse(_rr[fore]);
        foreach (var (bone, at) in _foreTwist[side])
            Target(bone, Vector3.Lerp(elbow, wrist, at), Quaternion.AngleAxis(twist * Mathf.Lerp(0.2f, 0.8f, at), axis) * foreRot * invFore * _rr[bone]);
    }

    /// <summary>The model's hand at rest as a frame like the controllers' hand: z along the fingers, y out of the back of
    /// the hand. palm: the palm's middle, in the hand bone's own space.</summary>
    private Quaternion Canonical(int side, out Vector3 palm)
    {
        var hand = _role[side == 0 ? (int)R.LHand : (int)R.RHand];
        var middle = _fingers[side * 5 + 1] ?? _fingers[side * 5] ?? _fingers[side * 5 + 3];
        var thumb = _fingers[side * 5 + 4];
        var knuckle = middle != null ? _rp[middle[0]] : _rp[hand] + (_rp[hand] - _rp[_role[side == 0 ? (int)R.LFore : (int)R.RFore]]).normalized * 0.1f;
        var along = (knuckle - _rp[hand]).normalized;
        var t = thumb != null ? Vector3.ProjectOnPlane(_rp[thumb[0]] - _rp[hand], along).normalized : Vector3.forward;
        var back = side == 1 ? Vector3.Cross(t, along) : Vector3.Cross(along, t);
        var index = _fingers[side * 5];
        var pinky = _fingers[side * 5 + 3];
        if (index != null && pinky != null)
        {
            var knuckles = Vector3.Cross(_rp[index[0]] - _rp[hand], _rp[pinky[0]] - _rp[hand]);
            if (knuckles.sqrMagnitude > 1e-12f) back = Vector3.Dot(knuckles, back) >= 0 ? knuckles.normalized : -knuckles.normalized;
        }

        palm = Quaternion.Inverse(_rr[hand]) * ((knuckle - _rp[hand]) * 0.5f - back * ((knuckle - _rp[hand]).magnitude * 0.12f));
        return Quaternion.LookRotation(along, back);
    }

    /// <summary>The fingers curl with the trigger (index), the grip (the rest) and the thumb's rest.</summary>
    private void Fingers(int side, in HandPose h)
    {
        var curlIndex = h.Valid ? h.Index : 0.25f;
        var curlGrip = h.Valid ? h.Grip : 0.3f;
        var curlThumb = h.Valid ? h.Thumb : 0.2f;
        var canonBack = Canonical(side, out _) * Vector3.up;
        for (var f = 0; f < 5; f++)
        {
            var chain = _fingers[side * 5 + f];
            if (chain == null) continue;
            var curl = f == 0 ? curlIndex : f == 4 ? curlThumb : Mathf.Min(1f, curlGrip + f * 0.02f);
            for (var j = 0; j < chain.Length; j++)
            {
                var i = chain[j];
                var next = j + 1 < chain.Length ? chain[j + 1] : -1;
                var dir = next >= 0 ? _rp[next] - _rp[i] : (j > 0 ? _rp[i] - _rp[chain[j - 1]] : Vector3.forward);
                var axisWorld = Vector3.Cross(canonBack, dir.normalized);
                if (axisWorld.sqrMagnitude < 1e-6f) continue;
                var axis = Quaternion.Inverse(_rr[i]) * axisWorld.normalized;
                float angle;
                if (f == 4) angle = j == 0 ? curl * 25f : curl * (j == 1 ? 45f : 55f);
                else angle = 6f + curl * (j == 0 ? 72f : j == 1 ? 95f : 70f);
                var q = _rl[i] * Quaternion.AngleAxis(angle, axis);
                _all[i].localRotation = q;
                if (_twin != null) _twin[i].localRotation = q;
            }
        }
    }

    /// <summary>Two-bone IK: the middle joint, bent towards the pole; the end clamped to what the bones can reach.</summary>
    private static Vector3 TwoBone(Vector3 start, Vector3 end, float a, float b, Vector3 pole, out Vector3 reached)
    {
        var to = end - start;
        var d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 1e-4f, (a + b) * 0.999f);
        var dir = to.sqrMagnitude > 1e-10f ? to.normalized : Vector3.down;
        reached = start + dir * d;
        var cosA = (a * a + d * d - b * b) / (2 * a * d);
        var sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
        var bend = Vector3.ProjectOnPlane(pole, dir).normalized;
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir).normalized;
        return start + dir * (a * cosA) + bend * (a * sinA);
    }
}
