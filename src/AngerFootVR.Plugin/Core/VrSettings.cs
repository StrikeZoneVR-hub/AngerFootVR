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

    public static ConfigEntry<TurnStyle> Turn;
    public static ConfigEntry<int> SnapAngle;
    public static ConfigEntry<int> SmoothSpeed;
    public static ConfigEntry<Hand> GunHand;
    public static ConfigEntry<OpeningMode> Opening;
    public static ConfigEntry<bool> OpeningSeen;

    public static void Init(ConfigFile cfg)
    {
        Turn = cfg.Bind("Comfort", "TurnStyle", TurnStyle.Snap, "Right stick turning: Snap (a jump at a time, the comfortable one) or Smooth.");
        SnapAngle = cfg.Bind("Comfort", "SnapAngle", 30, new ConfigDescription("Degrees a snap turns.", new AcceptableValueRange<int>(15, 90)));
        SmoothSpeed = cfg.Bind("Comfort", "SmoothTurnSpeed", 150, new ConfigDescription("Smooth turning speed at full stick (degrees a second).", new AcceptableValueRange<int>(60, 360)));
        GunHand = cfg.Bind("Controls", "GunHand", Hand.Right, "The hand that holds the guns (its trigger shoots, its grip throws). The other hand's grip interacts.");
        Opening = cfg.Bind("Opening", "Opening", OpeningMode.FirstLaunchOnly, "The VR opening when the game starts: On, FirstLaunchOnly or Off (A, B or the menu button skips it).");
        OpeningSeen = cfg.Bind("Opening", "OpeningSeen", false, "Set once the opening has played (FirstLaunchOnly plays it until then).");
        ApplyHand();
        GunHand.SettingChanged += (s, e) => ApplyHand();
    }

    private static void ApplyHand() => Input.VrInputProvider.GunHand = GunHand.Value == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;
}
