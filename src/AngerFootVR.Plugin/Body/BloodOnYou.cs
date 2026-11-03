using System.Collections.Generic;
using Framework;
using UnityEngine;

namespace AngerFootVR.Body;

/// <summary>
/// GORE EXTRA: blood on you. A kick that draws blood leaves it on your shoe, a bloody hit up close on your gun hand, a
/// kill close by (a head popped, a goon blown up, a bat swung) spatters the side of you that faced it, your chest and
/// arms; it dries away and is gone after 15 seconds. It lands on whatever you're wearing (the wardrobe's gloves, suits
/// and the killbot's chrome are drawn into the same picture of your body). They're the game's own blood splats (the ones it puts on walls), carried by your foot or
/// hand, and drawn only on you: they paint from a depth picture of your body alone (see BloodDecals), so they never
/// stain the floor you stand on.
/// </summary>
internal static class BloodOnYou
{
    private const float Life = 15f, DryFrom = 9f;
    private const int Max = 10;
    private static readonly int CutoutId = Shader.PropertyToID("_Cutout");
    private static readonly int DepthId = Shader.PropertyToID("_LayerDecalDepthTexture");

    private struct Stain
    {
        public GameObject Go;
        public BFX_ShaderProperies Props;
        public Renderer Rend;
        public float Born, Cut;
    }

    private static readonly List<Stain> Stains = new List<Stain>();
    private static MaterialPropertyBlock _block;

    public static bool Any => Stains.Count > 0 && !DevWorld;

    /// <summary>Dev: the splats on you paint from the world's picture (with you in it) instead of your own.</summary>
    public static bool DevWorld;

    /// <summary>A kick drew blood at a point: the foot nearer it gets some.</summary>
    public static void Kicked(Vector3 hit)
    {
        Transform foot = null, toe = null;
        var best = float.MaxValue;
        for (var i = 0; i < 2; i++)
        {
            var f = HeroBody.FootBone(i);
            if (f == null) continue;
            var d = (f.position - hit).sqrMagnitude;
            if (d >= best) continue;
            best = d;
            foot = f;
            toe = HeroBody.ToeBone(i);
        }

        if (foot == null) return;
        var on = toe != null ? Vector3.Lerp(foot.position, toe.position, 0.6f) : foot.position;
        // (a splat paints the surfaces facing back along it: from above and from your eyes, the shoe's top and front)
        var dir = Vector3.down * 0.7f + (on - Core.VrRig.HeadPosition).normalized * 0.3f;
        Add(foot, on, dir, 0.15f);
    }

    /// <summary>A bloody hit up close: the gun hand (and its gun) gets some.</summary>
    public static void Close(Vector3 hit)
    {
        var side = Input.VrInputProvider.GunHand == UnityEngine.XR.XRNode.LeftHand ? 0 : 1;
        var hand = HeroBody.HandBone(side);
        if (hand == null) return;
        // (from your eyes: the side of the hand and gun you see)
        var dir = hand.position - Core.VrRig.HeadPosition;
        if (dir.sqrMagnitude < 0.0001f) dir = Core.VrRig.HeadRotation * Vector3.forward;
        Add(hand, hand.position, dir.normalized, 0.14f);
    }

    /// <summary>A kill close by (within 3 m): a spatter on the parts of you facing it, more the closer it was.</summary>
    public static void Splash(Vector3 kill, float strength = 1f)
    {
        var head = Core.VrRig.HeadPosition;
        var dist = (kill - head).magnitude;
        if (dist > 3f) return;
        var anim = VrBody.Animator;
        if (anim == null) return;
        var parts = new List<(Transform bone, float score)>();
        foreach (var b in new[] { HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
        {
            var t = anim.GetBoneTransform(b);
            if (t == null) continue;
            // (nearer the kill and facing it: the front of you if it was in front)
            parts.Add((t, (t.position - kill).magnitude + Random.Range(0f, 0.25f)));
        }

        parts.Sort((a, b) => a.score.CompareTo(b.score));
        var n = Mathf.Clamp(Mathf.RoundToInt((3.2f - dist) * strength), 1, 3);
        for (var i = 0; i < n && i < parts.Count; i++)
        {
            var t = parts[i].bone;
            var dir = t.position - kill;
            if (dir.sqrMagnitude < 0.0001f) continue;
            dir.Normalize();
            // (the splat paints what faces back along it: on the bone's side toward the kill)
            Add(t, t.position - dir * 0.08f, dir, Random.Range(0.13f, 0.2f));
        }
    }

    private static void Add(Transform bone, Vector3 at, Vector3 dir, float scale)
    {
        if (!Rendering.Gore.Extra || !SingletonBehaviour<BloodCamera>.HasInstance) return;
        var attach = SingletonBehaviour<BloodCamera>.Instance.BloodAttach;
        if (attach == null || dir == Vector3.zero) return;
        while (Stains.Count >= Max) Remove(0);
        var go = Object.Instantiate(attach);
        go.name = "AngerFootVR blood on you";
        var t = go.transform;
        t.position = at;
        t.localScale = Vector3.one * (Random.Range(0.85f, 1.15f) * 3f * scale);
        t.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
        t.SetParent(bone, true);
        var p = go.GetComponentInChildren<BFX_ShaderProperies>();
        var cut = Random.Range(0.02f, 0.08f);
        if (p != null)
        {
            p.ForceTime = true;
            p.ForcedTime = cut;
            p.SetColour(Rendering.Gore.Colour);
            p.transform.localRotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        }

        Stains.Add(new Stain { Go = go, Props = p, Rend = p != null ? p.GetComponent<Renderer>() : go.GetComponentInChildren<Renderer>(), Born = Time.time, Cut = cut });
    }

    private static void Remove(int i)
    {
        if (Stains[i].Go != null) Object.Destroy(Stains[i].Go);
        Stains.RemoveAt(i);
    }

    /// <summary>Every frame: the blood dries away (its splat shrinks back, the game's own way), then goes.</summary>
    public static void Update()
    {
        if (Stains.Count == 0) return;
        _block ??= new MaterialPropertyBlock();
        var off = !Rendering.Gore.Extra || !HeroBody.Ready;
        for (var i = Stains.Count - 1; i >= 0; i--)
        {
            var s = Stains[i];
            var age = Time.time - s.Born;
            if (off || s.Go == null || s.Rend == null || age > Life || age < 0f)
            {
                Remove(i);
                continue;
            }

            var cut = Mathf.Lerp(s.Cut, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DryFrom, Life, age)));
            if (s.Props != null) s.Props.ForcedTime = cut;
            s.Rend.GetPropertyBlock(_block);
            _block.SetFloat(CutoutId, cut);
            s.Rend.SetPropertyBlock(_block);
        }
    }

    /// <summary>Before each eye: the splats on you paint from the picture of your body alone, from that eye.</summary>
    public static void UseDepth(Texture depth)
    {
        if (depth == null) return;
        _block ??= new MaterialPropertyBlock();
        foreach (var s in Stains)
        {
            if (s.Rend == null) continue;
            s.Rend.GetPropertyBlock(_block);
            _block.SetTexture(DepthId, depth);
            s.Rend.SetPropertyBlock(_block);
        }
    }

    /// <summary>All of it gone (a new level, the movie house).</summary>
    public static void Clear()
    {
        for (var i = Stains.Count - 1; i >= 0; i--) Remove(i);
    }

    public static string Describe()
    {
        var sb = new System.Text.StringBuilder($"on you {Stains.Count}");
        foreach (var s in Stains)
            if (s.Go != null && s.Rend != null)
                sb.Append($" [{s.Go.transform.parent?.name} at {s.Go.transform.position} scale {s.Go.transform.lossyScale.x:F2} bounds {s.Rend.bounds.size} on {s.Rend.enabled} layer {s.Rend.gameObject.layer} mat {s.Rend.sharedMaterial?.shader?.name} cut {(s.Props != null ? s.Props.ForcedTime : -1):F2}]");
        return sb.ToString();
    }
}
