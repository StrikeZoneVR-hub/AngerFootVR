using System.Collections.Generic;
using System.Linq;
using System.Text;
using Framework;
using HarmonyLib;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>Dev: the game's first-person legs (FootRig's Legs_Rig) through a kick, in the main camera's frame, frame by
/// frame (`legrec [frames]`), with the leg animator's state, so the VR body's kick can be made the game's.</summary>
internal static class LegRecorder
{
    private static int _left;
    private static readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
    private static Animator _anim;
    private static FootRig _rig;

    public static void Start(int frames)
    {
        _left = frames;
        Bones.Clear();
        _rig = Object.FindObjectOfType<FootRig>();
        if (_rig == null)
        {
            Plugin.L.LogInfo("LEGREC no FootRig");
            _left = 0;
            return;
        }

        _anim = (Animator)AccessTools.Field(typeof(FootRig), "_legAnimator").GetValue(_rig);
        foreach (var t in _rig.GetComponentsInChildren<Transform>(true))
            foreach (var key in new[] { "l_Leg_Hip", "l_Leg_Knee", "l_Leg_Ankle", "r_Leg_Hip", "r_Leg_Knee", "r_Leg_Ankle" })
                if (t.name.Contains(key) && !Bones.ContainsKey(key))
                    Bones[key] = t;
        Plugin.L.LogInfo($"LEGREC {frames} frames; rig '{_rig.name}' parent '{(_rig.transform.parent != null ? _rig.transform.parent.name : "-")}', bones: {string.Join(", ", Bones.Select(b => b.Key + "=" + b.Value.name).ToArray())}");
    }

    public static void Tick()
    {
        if (_left <= 0 || _rig == null) return;
        _left--;
        var cam = SingletonBehaviour<Player>.HasInstance ? SingletonBehaviour<Player>.Instance.Camera?.MainCamera : null;
        var frame = cam != null ? cam.transform : _rig.transform;
        var sb = new StringBuilder($"LEGREC f{Time.frameCount} ");
        if (_anim != null)
        {
            var st = _anim.GetCurrentAnimatorStateInfo(0);
            var clips = _anim.GetCurrentAnimatorClipInfo(0);
            sb.Append($"state {st.fullPathHash} t{st.normalizedTime:F2} clip {(clips.Length > 0 ? clips[0].clip.name : "-")} ");
        }

        sb.Append($"last {_rig.LastAttack?.name} grounded {SingletonBehaviour<Player>.Instance?.Movement?.IsGrounded} ");
        foreach (var kv in Bones)
        {
            var p = frame.InverseTransformPoint(kv.Value.position);
            sb.Append($"{kv.Key} ({p.x:F2},{p.y:F2},{p.z:F2}) ");
        }

        Plugin.L.LogInfo(sb.ToString());
    }
}
