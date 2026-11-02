using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AngerFootVR.Body;

/// <summary>
/// Anger Foot himself, for your VR body: the game's own full-body model (the C_Angerfoot_03 humanoid rig, with every
/// pair of shoes in the game as its own mesh). It lives in the title screen's scene, so the first time that scene is
/// loaded a copy is taken (scripts and physics stripped, kept for the whole session) and every level and the cinematic
/// area spawn your body from it. Nothing is shipped: the model comes from your copy of the game.
/// </summary>
internal static class HeroModel
{
    private const string AvatarName = "C_Angerfoot_03Avatar";

    /// <summary>The stripped, inactive copy every body is made from (null until the title screen has been seen).</summary>
    public static GameObject Template { get; private set; }

    public static Avatar Avatar { get; private set; }

    /// <summary>Which shoe each of the model's shoe meshes is (the game's own FootRigShoe tags, read before the
    /// scripts are stripped): mesh name by shoe type.</summary>
    public static readonly System.Collections.Generic.Dictionary<ShoeType, string> ShoeMeshes = new System.Collections.Generic.Dictionary<ShoeType, string>();

    /// <summary>The socks mesh's name (the game's FootRigSocks tag), if the model has one.</summary>
    public static string SocksMesh;

    public static void Init() => SceneManager.sceneLoaded += (scene, mode) => TryCapture();

    /// <summary>Looks for the hero in the loaded scenes and keeps a copy (once).</summary>
    public static void TryCapture()
    {
        if (Template != null) return;
        var source = Object.FindObjectsOfType<Animator>()
            .FirstOrDefault(a => a != null && a.avatar != null && a.avatar.name == AvatarName && a.avatar.isHuman);
        if (source == null) return;

        // copy while the original is switched off, so the copy's scripts never wake up
        var wasActive = source.gameObject.activeSelf;
        source.gameObject.SetActive(false);
        var copy = Object.Instantiate(source.gameObject);
        source.gameObject.SetActive(wasActive);
        copy.name = "AngerFootVR Hero";
        Object.DontDestroyOnLoad(copy);

        foreach (var tag in copy.GetComponentsInChildren<FootRigShoe>(true))
            if (tag.ShoeType != null && !ShoeMeshes.ContainsKey(tag.ShoeType))
                ShoeMeshes[tag.ShoeType] = tag.gameObject.name;
        var socks = copy.GetComponentInChildren<FootRigSocks>(true);
        if (socks != null) SocksMesh = socks.gameObject.name;
        Plugin.L.LogInfo($"Hero shoes from the game's tags: {ShoeMeshes.Count} ({string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ShoeMeshes, kv => kv.Key.name + "=" + kv.Value)))}), socks {SocksMesh ?? "none"}");
        var stripped = 0;
        foreach (var c in copy.GetComponentsInChildren<Component>(true).Reverse())
        {
            if (c == null || c is Transform || c is Animator || c is SkinnedMeshRenderer || c is MeshRenderer || c is MeshFilter) continue;
            Object.DestroyImmediate(c);
            stripped++;
        }

        var animator = copy.GetComponent<Animator>();
        Avatar = animator.avatar;
        animator.runtimeAnimatorController = null; // VR poses him, not the title's animations
        Template = copy;
        var renderers = copy.GetComponentsInChildren<Renderer>(true);
        Plugin.L.LogInfo($"Hero model taken from '{source.gameObject.scene.name}' ({source.name}): {renderers.Length} renderers ({string.Join(", ", renderers.Select(r => r.name + (r.gameObject.activeSelf ? "" : "(off)")).ToArray())}), {stripped} script/physics parts left behind");
    }
}
