using BepInEx.Configuration;
using UnityEngine.XR;

namespace AngerFootVR.Core;

/// <summary>
/// The VR settings, in BepInEx/config/com.strikezonevr.angerfootvr.cfg: the launcher's OPTIONS page edits the same
/// file (only the values, so the comments stay), and the game picks up changes as it plays.
/// </summary>
internal static class VrSettings
{
    public enum TurnStyle { Snap, Smooth }
    public enum Hand { Right, Left }
    public enum OpeningMode { On, FirstLaunchOnly, Off }
    public enum MonitorView { Steady, Eye }

    public static ConfigEntry<TurnStyle> Turn;
    public static ConfigEntry<int> SnapAngle;
    public static ConfigEntry<int> SmoothSpeed;
    public static ConfigEntry<Hand> GunHand;
    public static ConfigEntry<bool> MotionThrow;
    public static ConfigEntry<bool> VrMap;
    public static ConfigEntry<OpeningMode> Opening;
    public static ConfigEntry<bool> OpeningSeen;
    public static ConfigEntry<MonitorView> Monitor;
    public static ConfigEntry<int> MonitorFov;

    public static void Init(ConfigFile cfg)
    {
        Turn = cfg.Bind("Comfort", "TurnStyle", TurnStyle.Snap, "Right stick turning: Snap (a jump at a time, the comfortable one) or Smooth.");
        SnapAngle = cfg.Bind("Comfort", "SnapAngle", 30, new ConfigDescription("Degrees a snap turns.", new AcceptableValueRange<int>(15, 90)));
        SmoothSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 150, new ConfigDescription("Smooth turning speed at full stick (degrees a second).", new AcceptableValueRange<int>(60, 360)));
        GunHand = cfg.Bind("Controls", "GunHand", Hand.Right, "The hand that holds the guns (its trigger shoots, its grip throws). The other hand's grip interacts.");
        MotionThrow = cfg.Bind("Controls", "MotionThrow", true, "Flick your gun hand forward to throw your gun (the gun hand's grip throws too).");
        VrMap = cfg.Bind("Controls", "LevelSelectInVR", true, "The level select in VR: sit in the car with the map book and point at the levels. Off: it plays on the movie screen.");
        Opening = cfg.Bind("Opening", "Opening", OpeningMode.FirstLaunchOnly, "The VR opening when the game starts: On, FirstLaunchOnly or Off (A, B or the menu button skips it).");
        OpeningSeen = cfg.Bind("Opening", "OpeningSeen", false, "Set once the opening has played (FirstLaunchOnly plays it until then).");
        Monitor = cfg.Bind("Monitor", "MonitorView", MonitorView.Steady, "What the monitor shows: Steady (a level, smooth 16:9 view for anyone watching or recording) or Eye (the headset's left eye as it is).");
        MonitorFov = cfg.Bind("Monitor", "MonitorFov", 90, new ConfigDescription("How wide the Steady view is (degrees across); never wider than the headset's eye sees.", new AcceptableValueRange<int>(70, 110)));
        _cfg = cfg;
        ApplyHand();
        GunHand.SettingChanged += (s, e) => ApplyHand();
    }

    private static ConfigFile _cfg;
    private static System.DateTime _stamp;
    private static float _nextPoll;

    /// <summary>Every couple of seconds: when the launcher has changed the file, the new values come in.</summary>
    public static void Poll()
    {
        if (_cfg == null || UnityEngine.Time.unscaledTime < _nextPoll) return;
        _nextPoll = UnityEngine.Time.unscaledTime + 2f;
        try
        {
            var stamp = System.IO.File.GetLastWriteTimeUtc(_cfg.ConfigFilePath);
            if (_stamp == default) _stamp = stamp;
            if (stamp == _stamp) return;
            _stamp = stamp;
            _cfg.Reload();
            Plugin.L.LogInfo("VR settings changed: reloaded");
        }
        catch (System.Exception e)
        {
            Plugin.L.LogWarning("VR settings: " + e.Message);
        }
    }

    private static void ApplyHand() => Input.VrInputProvider.GunHand = GunHand.Value == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
}
