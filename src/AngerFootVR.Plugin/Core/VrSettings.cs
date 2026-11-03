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
    public enum MonitorView { Steady, Eye, Behind }
    public enum GoreLevel { Off, Game, Extra }
    public enum BloodTint { Pink, Red }

    public static ConfigEntry<TurnStyle> Turn;
    public static ConfigEntry<int> SnapAngle;
    public static ConfigEntry<int> SmoothSpeed;
    public static ConfigEntry<Hand> GunHand;
    public static ConfigEntry<bool> VrMap;
    public static ConfigEntry<bool> TwoHandAim, AmmoCounter, SniperScope, MotionDrink, FlipOff, HandSwat, WristWatch;
    public static ConfigEntry<OpeningMode> Opening;
    public static ConfigEntry<bool> OpeningSeen;
    public static ConfigEntry<MonitorView> Monitor;
    public static ConfigEntry<int> MonitorFov;
    public static ConfigEntry<GoreLevel> Gore;
    public static ConfigEntry<BloodTint> BloodColour;

    public static void Init(ConfigFile cfg)
    {
        Turn = cfg.Bind("Comfort", "TurnStyle", TurnStyle.Snap, "Right stick turning: Snap (a jump at a time, the comfortable one) or Smooth.");
        SnapAngle = cfg.Bind("Comfort", "SnapAngle", 30, new ConfigDescription("Degrees a snap turns.", new AcceptableValueRange<int>(15, 90)));
        SmoothSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 150, new ConfigDescription("Smooth turning speed at full stick (degrees a second).", new AcceptableValueRange<int>(60, 360)));
        GunHand = cfg.Bind("Controls", "GunHand", Hand.Right, "The hand that holds the guns (its trigger shoots, its grip throws). The other hand's grip interacts.");
        TwoHandAim = cfg.Bind("Controls", "TwoHandAim", true, "Two-handed guns: your other hand on the front (grip squeezed) holds the gun there and steers the barrel.");
        MotionDrink = cfg.Bind("Controls", "MotionDrink", true, "Drinks and the lizard: grab them and bring them to your mouth.");
        HandSwat = cfg.Bind("Controls", "HandSwat", true, "Grenades (and anything else you could kick back): hit them with your hand, fast, and back they go, as a kick sends them.");
        AmmoCounter = cfg.Bind("Display", "AmmoCounter", true, "An ammo count on the back of your gun.");
        WristWatch = cfg.Bind("Display", "WristWatch", true, "A watch on your free hand's wrist in a level: turn it to you for the time, your ammo and the star goals.");
        SniperScope = cfg.Bind("Display", "SniperScope", true, "The sniper rifle's scope magnifies when you put your eye to it.");
        FlipOff = cfg.Bind("Fun", "FlipOff", true, "Enemies who see your middle finger come for you.");
        VrMap = cfg.Bind("Controls", "LevelSelectInVR", true, "The level select in VR: sit in the car with the map book and point at the levels. Off: it plays on the movie screen.");
        Opening = cfg.Bind("Opening", "Opening", OpeningMode.FirstLaunchOnly, "The VR opening when the game starts: On, FirstLaunchOnly or Off (A, B or the menu button skips it).");
        OpeningSeen = cfg.Bind("Opening", "OpeningSeen", false, "Set once the opening has played (FirstLaunchOnly plays it until then).");
        Monitor = cfg.Bind("Monitor", "MonitorView", MonitorView.Steady, "What the monitor shows: Steady (a level, smooth 16:9 view for anyone watching or recording), Eye (the headset's left eye as it is) or Behind (from behind you, over your shoulder: your whole body, for anyone watching; Steady where there's no body).");
        MonitorFov = cfg.Bind("Monitor", "MonitorFov", 90, new ConfigDescription("How wide the Steady view is (degrees across); never wider than the headset's eye sees.", new AcceptableValueRange<int>(70, 110)));
        Gore = cfg.Bind("Gore", "Gore", GoreLevel.Extra, "Blood: Off (none), Game (the game's own) or Extra (more: kicks splatter the walls, headshots pop heads, wounds and blood stay on bodies and on you). Follows the game's own Show Blood setting.");
        BloodColour = cfg.Bind("Gore", "BloodColour", BloodTint.Pink, "The blood's colour: Pink (the game's own) or Red.");
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

    /// <summary>The mod saved the file itself (the in-game VR tab): no need to reload it.</summary>
    public static void NoteOwnSave()
    {
        try
        {
            if (_cfg != null) _stamp = System.IO.File.GetLastWriteTimeUtc(_cfg.ConfigFilePath);
        }
        catch (System.Exception)
        {
            // (the next poll reloads it, which is harmless)
        }
    }

    private static void ApplyHand() => Input.VrInputProvider.GunHand = GunHand.Value == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
}
