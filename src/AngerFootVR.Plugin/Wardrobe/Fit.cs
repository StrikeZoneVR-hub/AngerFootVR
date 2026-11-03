using System.Collections.Generic;
using UnityEngine;

namespace AngerFootVR.Wardrobe;

/// <summary>Puts wardrobe pieces on the VR body and its mirror twin: shells of the skin (skinned to the body's own
/// bones) and rigid pieces riding on a bone.</summary>
internal static class Fit
{
    private static readonly List<GameObject> Worn = new List<GameObject>();

    /// <summary>The bones (by name) under these of the body's humanoid bones, themselves included.</summary>
    public static HashSet<string> Under(Animator animator, params HumanBodyBones[] roots)
    {
        var set = new HashSet<string>();
        if (animator == null) return set;
        foreach (var r in roots)
        {
            var t = animator.GetBoneTransform(r);
            if (t == null) continue;
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) set.Add(c.name);
        }

        return set;
    }

    /// <summary>A shell mesh worn on every copy of the body (the body and its twin), skinned to their own bones.</summary>
    public static void WearShell(string skinName, Mesh shell, Material mat)
    {
        foreach (var r in Body.VrBody.AllRenderers())
        {
            if (!(r is SkinnedMeshRenderer smr) || smr.name != skinName) continue;
            var go = new GameObject("AngerFootVR worn " + shell.name) { layer = smr.gameObject.layer };
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition;
            go.transform.localRotation = smr.transform.localRotation;
            go.transform.localScale = smr.transform.localScale;
            var worn = go.AddComponent<SkinnedMeshRenderer>();
            worn.sharedMesh = shell;
            worn.bones = smr.bones;
            worn.rootBone = smr.rootBone;
            worn.sharedMaterial = mat;
            worn.updateWhenOffscreen = true;
            worn.shadowCastingMode = smr.shadowCastingMode;
            Worn.Add(go);
        }
    }

    public static void TakeOff()
    {
        foreach (var go in Worn)
            if (go != null)
                Object.Destroy(go);
        Worn.Clear();
    }

    public static int Count => Worn.Count;
}
