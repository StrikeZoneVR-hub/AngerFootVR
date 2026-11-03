using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace AngerFootVR.Rendering;

/// <summary>
/// The mod's own shaders (unity/AngerFootVR.Shaders, built with Unity 2018.4 into one AssetBundle shipped beside the
/// plugin): the cartoon holes' plug and the cartoon (toon + ink outline) shading of the wardrobe. Loaded once; a shader
/// that's missing or that the graphics card can't run gives null, and what uses it falls back.
/// </summary>
internal static class Shaders
{
    private static readonly Dictionary<string, Shader> Loaded = new Dictionary<string, Shader>();
    private static bool _tried;

    public static string File => Path.Combine(Path.Combine(Paths.PluginPath, "AngerFootVR"), "angerfootvr_shaders");

    public static Shader Get(string name)
    {
        Load();
        return Loaded.TryGetValue(name, out var s) ? s : null;
    }

    private static void Load()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            if (!System.IO.File.Exists(File))
            {
                Plugin.L.LogWarning("Shaders missing: " + File);
                return;
            }

            var bundle = AssetBundle.LoadFromFile(File);
            if (bundle == null)
            {
                Plugin.L.LogWarning("Shaders: the bundle didn't load");
                return;
            }

            foreach (var s in bundle.LoadAllAssets<Shader>())
            {
                if (s == null) continue;
                if (!s.isSupported)
                {
                    Plugin.L.LogWarning($"Shaders: '{s.name}' isn't supported on this card");
                    continue;
                }

                Object.DontDestroyOnLoad(s);
                Loaded[s.name] = s;
            }

            Plugin.L.LogInfo($"Shaders: {Loaded.Count} loaded ({string.Join(", ", new List<string>(Loaded.Keys).ToArray())})");
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("Shaders: " + e.Message);
        }
    }

    public static string Describe()
    {
        Load();
        return $"SHADERS {Loaded.Count}: {string.Join(", ", new List<string>(Loaded.Keys).ToArray())}";
    }
}
