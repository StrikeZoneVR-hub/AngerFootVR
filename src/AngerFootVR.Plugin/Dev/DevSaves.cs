using System;
using System.IO;
using BepInEx;
using HarmonyLib;

namespace AngerFootVR.Dev;

/// <summary>
/// Dev runs (AFVR_DEVSAVES=1, set by tools/game.sh) keep the game's saves and settings in a test folder inside the game
/// folder, so testing never touches the player's own save or Settings.json (or their Steam Cloud copy). Every save and
/// settings file goes through FileSystem.GetAbsolutePath. The player's Settings.json is copied in once (read only), so
/// tests run with their settings.
/// </summary>
[HarmonyPatch(typeof(FileSystem), nameof(FileSystem.GetAbsolutePath))]
internal static class DevSaves
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("AFVR_DEVSAVES") == "1";
    public static readonly string Root = Path.Combine(Paths.BepInExRootPath, Path.Combine("AngerFootVR", "dev-saves"));
    private static bool _ready;

    private static bool Prefix(string localPath, ref string __result)
    {
        if (!Enabled) return true;
        if (!_ready) Prepare();
        __result = Path.Combine(Root, localPath).Replace('\\', '/');
        return false;
    }

    private static void Prepare()
    {
        _ready = true;
        Directory.CreateDirectory(Path.Combine(Root, "Saves"));
        var mine = Path.Combine(Root, "Settings.json");
        var theirs = Path.Combine(UnityEngine.Application.persistentDataPath, "Settings.json");
        if (!File.Exists(mine) && File.Exists(theirs)) File.Copy(theirs, mine);
        Plugin.L.LogInfo("Dev run: saves and settings kept in " + Root);
    }
}
