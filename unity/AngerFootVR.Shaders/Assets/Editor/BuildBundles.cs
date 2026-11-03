using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Anger Foot VR's shaders into one AssetBundle the plugin loads at runtime.
/// Run in batch mode: Unity -batchmode -projectPath unity/AngerFootVR.Shaders -executeMethod BuildBundles.Build -quit
/// Every fog and lightmap variant is kept (a bundle has no scenes for Unity to judge which the game needs).
/// </summary>
public static class BuildBundles
{
    public static void Build()
    {
        var graphics = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        var so = new SerializedObject(graphics);
        void Set(string name, int value)
        {
            var p = so.FindProperty(name);
            if (p != null) p.intValue = value;
        }
        Set("m_LightmapStripping", 1);
        Set("m_LightmapKeepPlain", 1);
        Set("m_LightmapKeepDirCombined", 1);
        Set("m_LightmapKeepDynamicPlain", 1);
        Set("m_LightmapKeepDynamicDirCombined", 1);
        Set("m_LightmapKeepShadowMask", 1);
        Set("m_LightmapKeepSubtractive", 1);
        Set("m_FogStripping", 1);
        Set("m_FogKeepLinear", 1);
        Set("m_FogKeepExp", 1);
        Set("m_FogKeepExp2", 1);
        Set("m_InstancingStripping", 2);
        so.ApplyModifiedPropertiesWithoutUndo();

        var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Bundles"));
        Directory.CreateDirectory(outDir);
        // shaders and compute shaders
        var list = new System.Collections.Generic.List<string>();
        foreach (var kind in new[] { "t:Shader", "t:ComputeShader" })
            foreach (var g in AssetDatabase.FindAssets(kind, new[] { "Assets/Shaders" }))
                list.Add(AssetDatabase.GUIDToAssetPath(g));
        var names = list.ToArray();
        var build = new AssetBundleBuild { assetBundleName = "angerfootvr_shaders", assetNames = names };
        BuildPipeline.BuildAssetBundles(outDir, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        Debug.Log("ANGERFOOTVR bundle built with " + string.Join(", ", names) + " -> " + outDir);
    }
}
