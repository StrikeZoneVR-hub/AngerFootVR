using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AngerFootVR.Core;
using BepInEx.Configuration;
using Framework;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace AngerFootVR.UI;

/// <summary>
/// A VR tab in the game's own SETTINGS menu (title screen, pause menu and the car's settings): a copy of one of the
/// game's settings tabs, filled with the game's own toggles, sliders and drop-downs. They are bound to the mod's
/// settings (BepInEx/config/com.strikezonevr.angerfootvr.cfg), never to the game's: a change saves straight to the
/// mod's file, the game's Settings.json is never written for it, and the game's APPLY/CANCEL don't touch it.
/// </summary>
internal static class VrSettingsTab
{
    public const SettingsCategory Category = (SettingsCategory)100;

    // the controls read and write these (the game's controls work on fields); each change goes straight to the cfg
    public static VrSettings.TurnStyle Turning;
    public static SnapStep SnapAngle;
    public static int SmoothSpeed;
    public static VrSettings.Hand GunHand;
#pragma warning disable CS0649 // (set through their FieldInfo)
    public static bool LevelSelectInCar, SwingThrow, TwoHandGuns, DrinkForReal, AmmoCount, SniperScope, LastKillSlowMo, DoorKickSlowMo, FlipOff;
#pragma warning restore CS0649
    public static VrSettings.OpeningMode Opening;
    public static VrSettings.MonitorView Monitor;
    public static int MonitorFov;

    public enum SnapStep { S15, S30, S45, S60, S90 }

    private static readonly int[] SnapDegrees = { 15, 30, 45, 60, 90 };

    private class Row
    {
        public string Header, Label, Help, Field;
        public string[] Options;
        public int Min, Max;
        public Func<bool> Enabled;
        public Action Load, Save;
        public SettingsControl Control;
    }

    private static List<Row> _rows;

    private static List<Row> Rows() => _rows ??= new List<Row>
    {
        new Row { Header = "COMFORT" },
        new Row
        {
            Label = "TURNING", Field = nameof(Turning), Options = new[] { "SNAP", "SMOOTH" },
            Help = "Right stick: SNAP turns a step at a time (the comfortable one), SMOOTH turns as you hold it.",
            Load = () => Turning = VrSettings.Turn.Value, Save = () => VrSettings.Turn.Value = Turning,
        },
        new Row
        {
            Label = "SNAP ANGLE", Field = nameof(SnapAngle), Options = new[] { "15°", "30°", "45°", "60°", "90°" },
            Help = "How far one snap turns.", Enabled = () => Turning == VrSettings.TurnStyle.Snap,
            Load = () => SnapAngle = Nearest(VrSettings.SnapAngle.Value), Save = () => VrSettings.SnapAngle.Value = SnapDegrees[(int)SnapAngle],
        },
        new Row
        {
            Label = "SMOOTH TURN SPEED", Field = nameof(SmoothSpeed), Min = 60, Max = 360,
            Help = "Degrees a second at full stick.", Enabled = () => Turning == VrSettings.TurnStyle.Smooth,
            Load = () => SmoothSpeed = VrSettings.SmoothSpeed.Value, Save = () => VrSettings.SmoothSpeed.Value = Mathf.Clamp(SmoothSpeed, 60, 360),
        },
        new Row { Header = "CONTROLS" },
        new Row
        {
            Label = "GUN HAND", Field = nameof(GunHand), Options = new[] { "RIGHT", "LEFT" },
            Help = "The hand that holds your gun: its trigger shoots and its grip throws. The other hand's grip interacts.",
            Load = () => GunHand = VrSettings.GunHand.Value, Save = () => VrSettings.GunHand.Value = GunHand,
        },
        Toggle("SWING THROW", nameof(SwingThrow), VrSettings.SwingThrow,
            "Hold the gun hand's grip, swing and let go: it flies the way and as hard as you threw it. Off: the grip throws straight away."),
        Toggle("TWO-HAND GUNS", nameof(TwoHandGuns), VrSettings.TwoHandAim,
            "Rifles, shotguns, the sniper and friends: your other hand on the front (grip squeezed) holds the gun there and steers it."),
        Toggle("DRINK FOR REAL", nameof(DrinkForReal), VrSettings.MotionDrink,
            "Grab a beer, an energy drink or the lizard and bring it to your mouth. Off: grabbing it drinks it, as in the game."),
        Toggle("LEVEL SELECT IN THE CAR", nameof(LevelSelectInCar), VrSettings.VrMap,
            "Sit in the car with the map book and point at a level (the trigger plays it). Off: the level select plays on the movie screen."),
        new Row { Header = "DISPLAY" },
        Toggle("AMMO COUNT", nameof(AmmoCount), VrSettings.AmmoCounter, "An ammo count on the back of your gun (EMPTY: time to throw it)."),
        Toggle("SNIPER SCOPE", nameof(SniperScope), VrSettings.SniperScope, "A scope on the sniper rifle that magnifies when you put your eye to it."),
        new Row
        {
            Label = "MONITOR VIEW", Field = nameof(Monitor), Options = new[] { "STEADY", "EYE" },
            Help = "What your monitor shows: STEADY is a level, smooth view for anyone watching or recording; EYE is your headset's view as it is.",
            Load = () => Monitor = VrSettings.Monitor.Value, Save = () => VrSettings.Monitor.Value = Monitor,
        },
        new Row
        {
            Label = "MONITOR FOV", Field = nameof(MonitorFov), Min = 70, Max = 110,
            Help = "How wide the STEADY view is, in degrees (never wider than your headset shows).", Enabled = () => Monitor == VrSettings.MonitorView.Steady,
            Load = () => MonitorFov = VrSettings.MonitorFov.Value, Save = () => VrSettings.MonitorFov.Value = Mathf.Clamp(MonitorFov, 70, 110),
        },
        new Row { Header = "FUN" },
        Toggle("LAST KILL SLOW-MO", nameof(LastKillSlowMo), VrSettings.LastKillSlowMo, "The last enemy standing goes down in slow motion."),
        Toggle("DOOR KICK SLOW-MO", nameof(DoorKickSlowMo), VrSettings.DoorKickSlowMo, "A moment of slow motion when you kick a door open into enemies."),
        Toggle("FLIP THEM OFF", nameof(FlipOff), VrSettings.FlipOff, "Enemies who see your middle finger (right stick click) come for you."),
        new Row { Header = "OPENING" },
        new Row
        {
            Label = "VR OPENING", Field = nameof(Opening), Options = new[] { "EVERY START", "FIRST START ONLY", "OFF" },
            Help = "The short opening when the game starts in VR (A, B or the menu button skips it).",
            Load = () => Opening = VrSettings.Opening.Value, Save = () => VrSettings.Opening.Value = Opening,
        },
        new Row { Header = "VR MOD MADE BY STRIKEZONEVR" },
    };

    private static Row Toggle(string label, string field, ConfigEntry<bool> entry, string help)
    {
        var f = AccessTools.Field(typeof(VrSettingsTab), field);
        return new Row { Label = label, Field = field, Help = help, Load = () => f.SetValue(null, entry.Value), Save = () => entry.Value = (bool)f.GetValue(null) };
    }

    private static SnapStep Nearest(int degrees)
    {
        var best = 0;
        for (var i = 1; i < SnapDegrees.Length; i++)
            if (Math.Abs(SnapDegrees[i] - degrees) < Math.Abs(SnapDegrees[best] - degrees)) best = i;
        return (SnapStep)best;
    }

    /// <summary>The mod's values into the controls' fields (every frame the tab shows: the launcher may change them too).</summary>
    public static void Load()
    {
        foreach (var r in Rows())
            r.Load?.Invoke();
    }

    private static readonly FieldInfo TabsField = AccessTools.Field(typeof(TabbedMenu), "_tabs");
    private static readonly FieldInfo TabButtonsField = AccessTools.Field(typeof(TabbedMenu), "_tabButtons");
    private static readonly FieldInfo CategoryField = AccessTools.Field(typeof(SettingsMenuTab), "_category");
    private static readonly FieldInfo DisplayNameField = AccessTools.Field(typeof(MenuTab), "_displayName");
    private static readonly FieldInfo ControlParentField = AccessTools.Field(typeof(SettingsMenuTab), "_controlParent");
    private static readonly FieldInfo ButtonTabField = AccessTools.Field(typeof(OpenMenuTabButton), "_tab");
    private static readonly FieldInfo EnglishText = AccessTools.Field(typeof(LocalizedString), "_englishText");
    private static readonly FieldInfo AssetsLoaded = AccessTools.Field(typeof(LocalizedString), "_assetsAreLoaded");
    private static readonly FieldInfo Interactable = AccessTools.Field(typeof(SettingsControl), "_interactableCondition");
    private static readonly string[] PrefabFields = { "_boolControlPrefab", "_sliderControlPrefab", "_enumControlPrefab", "_resolutionControlPrefab", "_inputMappingControlPrefab", "_sectionHeaderPrefab" };

    private static readonly Dictionary<string, LocalizedString> Texts = new Dictionary<string, LocalizedString>();

    /// <summary>A LocalizedString of our own (English in every language), without making the game reload all of its own.</summary>
    private static LocalizedString Text(string english)
    {
        if (Texts.TryGetValue(english, out var s) && s != null) return s;
        var loaded = (bool)AssetsLoaded.GetValue(null);
        s = ScriptableObject.CreateInstance<LocalizedString>();
        s.hideFlags = HideFlags.HideAndDontSave;
        EnglishText.SetValue(s, english);
        AssetsLoaded.SetValue(null, loaded); // (a new one marks them all for a reload)
        return Texts[english] = s;
    }

    public static SettingsMenuTab Tab { get; private set; }

    [HarmonyPatch(typeof(TabbedMenu), "Awake")]
    private static class AddTab
    {
        private static void Postfix(TabbedMenu __instance)
        {
            if (__instance.GetComponent<SettingsMenu>() == null) return;
            try
            {
                Add(__instance);
            }
            catch (Exception e)
            {
                Plugin.L.LogError("VR settings tab: " + e);
            }
        }
    }

    private static void Add(TabbedMenu menu)
    {
        var tabs = (MenuTab[])TabsField.GetValue(menu);
        if (tabs.OfType<SettingsMenuTab>().Any(t => t.SettingsCategory == Category)) return;
        // the template: the game's GAMEPLAY tab if there is one (toggles, sliders and drop-downs), otherwise the last
        var settingsTabs = tabs.OfType<SettingsMenuTab>().ToArray();
        if (settingsTabs.Length == 0) return;
        var template = settingsTabs.FirstOrDefault(t => t.SettingsCategory.ToString() == "Gameplay") ?? settingsTabs[settingsTabs.Length - 1];
        var lastTab = tabs[tabs.Length - 1];

        // copy it while it's switched off, so the copy wakes up only once it's ours
        var wasActive = template.gameObject.activeSelf;
        template.gameObject.SetActive(false);
        var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
        template.gameObject.SetActive(wasActive);
        go.name = "VR Settings Tab";
        go.transform.SetSiblingIndex(lastTab.transform.GetSiblingIndex() + 1);
        var tab = go.GetComponent<SettingsMenuTab>();
        CategoryField.SetValue(tab, Category);
        DisplayNameField.SetValue(tab, Text("VR"));

        // without the rows the template had made
        var keep = new HashSet<GameObject>(PrefabFields.Select(f => AccessTools.Field(typeof(SettingsMenuTab), f).GetValue(tab) as GameObject).Where(g => g != null));
        var parent = (Transform)ControlParentField.GetValue(tab);
        if (parent != null)
            foreach (Transform c in parent.Cast<Transform>().ToArray())
                if (!keep.Contains(c.gameObject) && (c.GetComponentInChildren<SettingsControl>(true) != null || c.GetComponentInChildren<SettingsMenuSubsectionHeader>(true) != null || c.GetComponentInChildren<ResolutionControl>(true) != null))
                    UnityEngine.Object.DestroyImmediate(c.gameObject);
        go.AddComponent<Sync>();
        go.SetActive(true); // (its Awake builds the rows: see FillTab)

        // its button on the tab bar, after the last one
        var buttons = (OpenMenuTabButton[])TabButtonsField.GetValue(menu);
        if (buttons.Length > 0)
        {
            var last = buttons[buttons.Length - 1];
            var b = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
            b.name = "VR Tab Button";
            b.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
            var button = b.GetComponent<OpenMenuTabButton>();
            ButtonTabField.SetValue(button, tab);
            if (last.transform.parent.GetComponent<UnityEngine.UI.LayoutGroup>() == null && buttons.Length > 1)
            {
                // no layout on the bar: one step further along than the last
                var lr = (RectTransform)last.transform;
                var pr = (RectTransform)buttons[buttons.Length - 2].transform;
                ((RectTransform)b.transform).anchoredPosition = lr.anchoredPosition + (lr.anchoredPosition - pr.anchoredPosition);
            }

            foreach (var t in b.GetComponentsInChildren<AngerText>(true))
                t.SetTextUnlocalized("VR");
            foreach (var t in b.GetComponentsInChildren<TextMeshProUGUI>(true).Where(t => t.GetComponent<AngerText>() == null))
                t.text = "VR";
            // the bar's buttons are icons: ours is a headset
            var image = b.GetComponent<UnityEngine.UI.Image>();
            if (image != null) image.sprite = Icon();
        }

        AddHelp(tab);

        TabsField.SetValue(menu, menu.GetComponentsInChildren<MenuTab>(true));
        TabButtonsField.SetValue(menu, menu.GetComponentsInChildren<OpenMenuTabButton>());
        Tab = tab;
        Plugin.L.LogInfo($"VR settings tab added ({((MenuTab[])TabsField.GetValue(menu)).Length} tabs, from {template.SettingsCategory})");
    }

    private static Sprite _icon;

    /// <summary>A VR headset, white on clear like the bar's other icons (the button tints it).</summary>
    private static Sprite Icon()
    {
        if (_icon != null) return _icon;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "AngerFootVR VR tab icon", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            var p = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f);
            // the visor, its strap either side, less the two lenses and the nose
            var d = Box(p - new Vector2(0f, 2f), new Vector2(25f, 14f), 9f);
            d = Mathf.Min(d, Box(p - new Vector2(0f, 4f), new Vector2(31f, 3.5f), 1.5f));
            d = Mathf.Max(d, -Box(p - new Vector2(-11.5f, 3f), new Vector2(8f, 6.5f), 4f));
            d = Mathf.Max(d, -Box(p - new Vector2(11.5f, 3f), new Vector2(8f, 6.5f), 4f));
            d = Mathf.Max(d, -((p - new Vector2(0f, -13f)).magnitude - 6.5f));
            var a = Mathf.Clamp01(0.5f - d);
            px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }

        tex.SetPixels32(px);
        tex.Apply(false, true);
        _icon = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        _icon.name = "VR";
        return _icon;
    }

    /// <summary>The distance from p to a rounded box (half size h, corner radius r): below 0 inside.</summary>
    private static float Box(Vector2 p, Vector2 h, float r)
    {
        var q = new Vector2(Mathf.Abs(p.x) - h.x + r, Mathf.Abs(p.y) - h.y + r);
        return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
    }

    private static TextMeshProUGUI _help;

    /// <summary>A line under the VR tab's list: what the row in use does (the game has no such line, so it's ours).</summary>
    private static void AddHelp(SettingsMenuTab tab)
    {
        var scroll = tab.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
        if (scroll == null) return;
        const float room = 64f;
        var sr = (RectTransform)scroll.transform;
        sr.offsetMin = new Vector2(sr.offsetMin.x, sr.offsetMin.y + room);

        var go = new GameObject("VR Help", typeof(RectTransform));
        go.layer = tab.gameObject.layer;
        go.transform.SetParent(tab.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 2f);
        rt.sizeDelta = new Vector2(-10f, room - 6f);
        _help = go.AddComponent<TextMeshProUGUI>();
        // the rows' own font
        var prefab = AccessTools.Field(typeof(SettingsMenuTab), "_boolControlPrefab").GetValue(tab) as GameObject;
        var like = prefab != null ? prefab.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (like != null)
        {
            _help.font = like.font;
            _help.fontSharedMaterial = like.fontSharedMaterial;
        }

        _help.fontSize = 21f;
        _help.enableAutoSizing = true;
        _help.fontSizeMin = 14f;
        _help.fontSizeMax = 21f;
        _help.alignment = TextAlignmentOptions.Center;
        _help.color = new Color(1f, 0.86f, 0.55f, 0.95f);
        _help.raycastTarget = false;
        _help.text = "";
    }

    [HarmonyPatch(typeof(SettingsMenuTab), "Awake")]
    private static class FillTab
    {
        private static void Postfix(SettingsMenuTab __instance)
        {
            if (__instance.SettingsCategory != Category) return;
            try
            {
                Fill(__instance);
            }
            catch (Exception e)
            {
                Plugin.L.LogError("VR settings rows: " + e);
            }
        }
    }

    private static void Fill(SettingsMenuTab tab)
    {
        var bools = (ChildPool<BoolSettingsControl>)AccessTools.Field(typeof(SettingsMenuTab), "_boolControls").GetValue(tab);
        var sliders = (ChildPool<SliderSettingsControl>)AccessTools.Field(typeof(SettingsMenuTab), "_sliderControls").GetValue(tab);
        var enums = (ChildPool<EnumSettingsControl>)AccessTools.Field(typeof(SettingsMenuTab), "_enumControls").GetValue(tab);
        var headers = (ChildPool<SettingsMenuSubsectionHeader>)AccessTools.Field(typeof(SettingsMenuTab), "_headers").GetValue(tab);
        Load();
        // the headers in the rows' own font (the game's header has a plain one); the credit in the game's pink
        var prefab = AccessTools.Field(typeof(SettingsMenuTab), "_boolControlPrefab").GetValue(tab) as GameObject;
        var like = prefab != null ? prefab.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        foreach (var r in Rows())
        {
            if (r.Header != null)
            {
                var h = headers.Add();
                h.Setup(r.Header);
                h.transform.SetAsLastSibling();
                var label = h.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null && like != null)
                {
                    label.font = like.font;
                    label.fontSharedMaterial = like.fontSharedMaterial;
                    label.fontSize = 27f;
                    label.characterSpacing = 6f;
                    if (r.Header.StartsWith("VR MOD")) label.color = new Color(1f, 0.16f, 0.48f);
                }

                continue;
            }

            var field = AccessTools.Field(typeof(VrSettingsTab), r.Field);
            var row = r;
            Action changed = () => Changed(row);
            SettingsControl control;
            if (field.FieldType == typeof(bool))
            {
                var c = bools.Add();
                c.Setup(Text(r.Label), field, null, null, changed);
                control = c;
            }
            else if (field.FieldType == typeof(int))
            {
                var c = sliders.Add();
                c.Setup(Text(r.Label), field, null, null, r.Min, r.Max, SliderDataType.Int, changed);
                control = c;
            }
            else
            {
                var c = enums.Add();
                c.Setup(Text(r.Label), field, r.Options.Select(Text).ToArray(), null, null, changed);
                control = c;
            }

            if (r.Enabled != null) Interactable.SetValue(control, r.Enabled);
            control.transform.SetAsLastSibling();
            r.Control = control;
        }
    }

    private static void Changed(Row r)
    {
        try
        {
            r.Save();
            VrSettings.NoteOwnSave();
            Plugin.L.LogInfo($"VR setting {r.Label}: {AccessTools.Field(typeof(VrSettingsTab), r.Field).GetValue(null)}");
        }
        catch (Exception e)
        {
            Plugin.L.LogError("VR setting " + r.Label + ": " + e.Message);
        }
    }

    /// <summary>The help for the row in use (selected with the stick, or under the laser/mouse); null when none is.</summary>
    public static string Help()
    {
        if (_rows == null) return null;
        foreach (var r in _rows)
            if (r.Control != null && r.Control.isActiveAndEnabled && r.Control.IsSelected)
                return r.Help;
        return null;
    }

    /// <summary>On the VR tab: keeps its controls showing the mod's values while it's open.</summary>
    private class Sync : MonoBehaviour
    {
        private void OnEnable() => Load();

        private void Update()
        {
            Load();
            if (_help != null) _help.text = Help() ?? "Changes save straight away (to the VR mod's own settings, not the game's).";
        }
    }

    /// <summary>The game's RESET TO DEFAULTS on the VR tab puts the VR settings back to theirs (not the game's).</summary>
    [HarmonyPatch(typeof(RevertToDefaultSettingsButton), "OnClick")]
    private static class Reset
    {
        private static bool Prefix()
        {
            if (!SingletonBehaviour<SettingsMenu>.HasInstance || SingletonBehaviour<SettingsMenu>.Instance.TabbedMenu.CurrentTab is not SettingsMenuTab t || t.SettingsCategory != Category) return true;
            foreach (var e in new ConfigEntryBase[]
                     {
                         VrSettings.Turn, VrSettings.SnapAngle, VrSettings.SmoothSpeed, VrSettings.GunHand, VrSettings.SwingThrow, VrSettings.TwoHandAim, VrSettings.MotionDrink, VrSettings.VrMap,
                         VrSettings.AmmoCounter, VrSettings.SniperScope, VrSettings.Monitor, VrSettings.MonitorFov, VrSettings.LastKillSlowMo, VrSettings.DoorKickSlowMo, VrSettings.FlipOff, VrSettings.Opening,
                     })
                e.BoxedValue = e.DefaultValue;
            VrSettings.NoteOwnSave();
            Load();
            Plugin.L.LogInfo("VR settings back to their defaults");
            return false;
        }
    }

    // ---- dev ----

    /// <summary>Dev: open SETTINGS (from the title screen or pause menu) and the VR tab; n: open the n-th tab instead.</summary>
    public static string Open(int n)
    {
        if (!SingletonBehaviour<SettingsMenu>.HasInstance || !SingletonBehaviour<SettingsMenu>.Instance.gameObject.activeInHierarchy)
        {
            if (SingletonBehaviour<TitleScreen>.HasInstance)
                SingletonBehaviour<TitleScreen>.Instance.ShowSettingsMenu();
            else if (SingletonBehaviour<WorldMapUI>.HasInstance)
                SingletonBehaviour<WorldMapUI>.Instance.OpenSettingsMenu();
            else
            {
                var pause = SingletonBehaviour<PauseMenu>.HasInstance ? SingletonBehaviour<PauseMenu>.Instance : UnityEngine.Object.FindObjectOfType<PauseMenu>();
                if (pause == null) return "no menu with SETTINGS here";
                pause.SettingsMenu.Show();
                pause.HideOptions();
            }

            if (!SingletonBehaviour<SettingsMenu>.HasInstance) return "SETTINGS didn't open";
        }

        var menu = SingletonBehaviour<SettingsMenu>.Instance.TabbedMenu;
        var tabs = (MenuTab[])TabsField.GetValue(menu);
        var tab = n >= 0 && n < tabs.Length ? tabs[n] : Tab;
        if (tab != null) menu.OpenTab(tab);
        return $"settings open, tab {menu.CurrentTab?.name} of {tabs.Length}: " + string.Join(", ", tabs.Select(t => t.name));
    }

    /// <summary>Dev: change a row through its own control, as a click/slide would; it then reads back the cfg.</summary>
    public static string DevChange(string label)
    {
        var r = _rows?.FirstOrDefault(x => x.Label != null && x.Label.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);
        if (r?.Control == null) return "no VR row " + label;
        switch (r.Control)
        {
            case BoolSettingsControl b:
                var t = b.GetComponentInChildren<UnityEngine.UI.Toggle>();
                t.isOn = !t.isOn;
                break;
            case SliderSettingsControl sl:
                var s = sl.GetComponentInChildren<UnityEngine.UI.Slider>();
                s.value = s.value + 10f > s.maxValue ? s.minValue : s.value + 10f;
                break;
            case EnumSettingsControl e:
                var d = e.GetComponentInChildren<TMP_Dropdown>();
                d.value = (d.value + 1) % d.options.Count;
                break;
        }

        return $"{r.Label} -> {AccessTools.Field(typeof(VrSettingsTab), r.Field).GetValue(null)}; interactable {r.Control.IsInteractable}; help '{Help()}'";
    }

    /// <summary>Dev: press the game's DEFAULT button (on the VR tab it resets the VR settings).</summary>
    public static string DevDefault()
    {
        var b = UnityEngine.Object.FindObjectOfType<RevertToDefaultSettingsButton>();
        if (b == null) return "no DEFAULT button";
        AccessTools.Method(typeof(RevertToDefaultSettingsButton), "OnClick").Invoke(b, null);
        return "DEFAULT pressed on " + SingletonBehaviour<SettingsMenu>.Instance.TabbedMenu.CurrentTab?.name;
    }

    /// <summary>Dev: the settings menu's layout (names, components, rects).</summary>
    public static void Dump()
    {
        if (!SingletonBehaviour<SettingsMenu>.HasInstance)
        {
            Plugin.L.LogInfo("SETTINGS none");
            return;
        }

        var sb = new StringBuilder();
        Dump(SingletonBehaviour<SettingsMenu>.Instance.transform, 0, sb);
        foreach (var line in sb.ToString().Split('\n'))
            if (line.Length > 0) Plugin.L.LogInfo("SETTINGS " + line);
    }

    private static void Dump(Transform t, int depth, StringBuilder sb)
    {
        if (depth > 7) return;
        var comps = string.Join(",", t.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name).ToArray());
        var rect = t is RectTransform r ? $" rect {r.anchoredPosition} {r.sizeDelta} anchors {r.anchorMin}-{r.anchorMax} pivot {r.pivot}" : "";
        var text = t.GetComponent<TMP_Text>() is TMP_Text tx ? $" \"{tx.text}\"" : "";
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append(" [").Append(comps).Append("]").Append(rect).Append(text).Append(t.gameObject.activeSelf ? "" : " (off)").Append('\n');
        // the rows repeat: only the first two of each kind
        var seen = new Dictionary<string, int>();
        foreach (Transform c in t)
        {
            var key = c.name;
            seen[key] = seen.TryGetValue(key, out var k) ? k + 1 : 1;
            if (seen[key] <= 2) Dump(c, depth + 1, sb);
        }
    }
}
