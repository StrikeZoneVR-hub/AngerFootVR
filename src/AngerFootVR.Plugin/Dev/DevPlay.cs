using System.Linq;
using System.Text;
using Framework;
using UnityEngine;

namespace AngerFootVR.Dev;

/// <summary>Dev commands for testing play on the test runtime (no controllers, no one playing).</summary>
internal static class DevPlay
{
    /// <summary>"safe": god mode and enemies that stand still and don't attack (dev runs only; not saved).</summary>
    public static void Safe(bool on)
    {
        Cheats.GodMode = on;
        Cheats.PassiveEnemies = on;
        Cheats.StaticEnemies = on;
        Plugin.L.LogInfo("dev safe mode " + (on ? "on" : "off"));
    }

    /// <summary>"levels": lists the game's levels; "load &lt;name part&gt;": loads the first that matches.</summary>
    public static void Levels(string match)
    {
        var levels = Resources.FindObjectsOfTypeAll<Level>().Where(l => l != null).OrderBy(l => l.name).ToArray();
        if (string.IsNullOrEmpty(match))
        {
            Plugin.L.LogInfo("LEVELS " + string.Join(", ", levels.Select(l => l.name).ToArray()));
            return;
        }

        var level = levels.FirstOrDefault(l => l.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (level == null)
        {
            Plugin.L.LogWarning("no level matches " + match);
            return;
        }

        Plugin.L.LogInfo("loading level " + level.name);
        GameState.LoadLevel(level, LoadingTransition.FadeToAndFromBlack);
    }

    /// <summary>"givegun [name part]": hands the player a copy of a gun found in the level (an enemy's, a pickup).</summary>
    public static void GiveGun(string match)
    {
        if (!SingletonBehaviour<Player>.HasInstance) return;
        var guns = Resources.FindObjectsOfTypeAll<ProjectileWeapon>().Where(w => w != null && w.gameObject.scene.IsValid()).ToArray();
        Plugin.L.LogInfo("GUNS in the level: " + string.Join(", ", guns.Select(g => g.name).Distinct().ToArray()));
        var gun = guns.FirstOrDefault(g => string.IsNullOrEmpty(match) || g.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (gun == null) return;
        SingletonBehaviour<Player>.Instance.SpawnPlayerWeapon(gun.gameObject);
        Plugin.L.LogInfo("gave the player " + gun.name);
    }

    /// <summary>"hands &lt;rest|gun|guard|up|off&gt;": dev hand poses for the test runtime (relative to the head).</summary>
    public static void Hands(string preset)
    {
        switch (preset)
        {
            case "gun": // right hand out aiming, left near the chest
                Core.VrHands.DevRight = (new Vector3(0.22f, -0.25f, 0.45f), new Vector3(0f, 0f, 0f));
                Core.VrHands.DevLeft = (new Vector3(-0.2f, -0.45f, 0.2f), new Vector3(20f, 20f, 0f));
                break;
            case "guard": // fists up
                Core.VrHands.DevRight = (new Vector3(0.18f, -0.2f, 0.3f), new Vector3(-30f, -10f, 0f));
                Core.VrHands.DevLeft = (new Vector3(-0.18f, -0.2f, 0.3f), new Vector3(-30f, 10f, 0f));
                break;
            case "up": // both hands raised
                Core.VrHands.DevRight = (new Vector3(0.35f, 0.25f, 0.15f), new Vector3(-80f, 0f, 0f));
                Core.VrHands.DevLeft = (new Vector3(-0.35f, 0.25f, 0.15f), new Vector3(-80f, 0f, 0f));
                break;
            case "rest":
            case "off":
                Core.VrHands.DevRight = null;
                Core.VrHands.DevLeft = null;
                break;
        }

        Plugin.L.LogInfo("dev hands " + preset);
    }

    /// <summary>"bodyshot [name]": a picture of the whole VR body from in front of it (head shown for it).</summary>
    public static System.Collections.IEnumerator BodyShot(string name)
    {
        yield return new WaitForEndOfFrame();
        var root = Body.VrBody.Root;
        var main = Camera.main;
        if (root == null || main == null)
        {
            Plugin.L.LogInfo("bodyshot: no body");
            yield break;
        }

        var go = new GameObject("AngerFootVR bodyshot");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.cullingMask |= (1 << UI.VrUi.Layer) | (1 << Body.VrBody.MirrorLayer);
        cam.fieldOfView = 50f;
        var size = root.lossyScale.y;
        var look = root.position + Vector3.up * (1.0f * size);
        go.transform.position = root.position + root.rotation * new Vector3(0.6f, 1.3f, 2.4f) * size;
        go.transform.LookAt(look);
        var rt = new RenderTexture(1080, 1350, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        Body.BodyPose.SetHeadVisible(true);
        cam.Render();
        Body.BodyPose.SetHeadVisible(false);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = prev;
        var dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, System.IO.Path.Combine("AngerFootVR", "shots"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, $"{(string.IsNullOrEmpty(name) ? "body" : name)}_{System.DateTime.Now:HHmmss}.png");
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.Destroy(tex);
        cam.targetTexture = null;
        rt.Release();
        Object.Destroy(rt);
        Object.Destroy(go);
        Plugin.L.LogInfo("bodyshot: " + file);
    }

    /// <summary>"hier &lt;name part&gt;": the first object's parents, and its children three levels down.</summary>
    public static void Hierarchy(string match)
    {
        // a name part, or "#Type" for the first object with that component
        var t = match.StartsWith("#")
            ? Resources.FindObjectsOfTypeAll<Component>().FirstOrDefault(c => c != null && c.gameObject.scene.IsValid() && c.GetType().Name == match.Substring(1))?.transform
            : Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x != null && x.gameObject.scene.IsValid() && x.name.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (t == null)
        {
            Plugin.L.LogInfo("HIER nothing matches " + match);
            return;
        }

        var up = new StringBuilder();
        for (var p = t; p != null; p = p.parent) up.Insert(0, "/" + p.name);
        Plugin.L.LogInfo($"HIER {up} pos {t.position} rot {t.eulerAngles} local {t.localPosition} {t.localEulerAngles} scale {t.lossyScale} layer {t.gameObject.layer}");
        Dump(t, 1);
    }

    private static void Dump(Transform t, int depth)
    {
        if (depth > 3) return;
        foreach (Transform c in t)
        {
            var comps = string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name).ToArray());
            Plugin.L.LogInfo($"HIER {new string(' ', depth * 2)}{c.name} [{comps}] local {c.localPosition} {c.localEulerAngles} active {c.gameObject.activeSelf}");
            Dump(c, depth + 1);
        }
    }
}
