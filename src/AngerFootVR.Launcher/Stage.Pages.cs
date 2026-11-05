using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AngerFootVR.Launcher.Effects;
using AngerFootVR.Launcher.Game;

namespace AngerFootVR.Launcher
{
    /// <summary>The pages beside the menu: OPTIONS, CONTROLS and VR CHECK, on a dark glass panel with a neon edge.</summary>
    public sealed partial class Stage
    {
        private ModSettingsFile _cfg;
        private volatile List<Check> _checks;
        private bool _checking;
        private double _uninstallArmed = -10;

        private static readonly Rect Panel = new Rect(604, 78, 636, 594);

        private sealed class Opt
        {
            public string Label, Help;
            public Func<string> Value;
            public Action<int> Step;
            public Func<bool> Enabled = () => true;
            public bool Header;
        }

        // ---------------------------------------------------------------- options

        private List<Opt> _opts;

        private List<Opt> Options
        {
            get
            {
                if (_opts != null) return _opts;
                bool Mod() => _cfg != null;
                Opt Toggle(string section, string key, string label, string help, bool on = true) => new Opt
                {
                    Label = label, Help = help,
                    Value = () => Cfg(section, key, on ? "true" : "false").ToLowerInvariant() == "false" ? "OFF" : "ON",
                    Step = by => Set(section, key, Cfg(section, key, on ? "true" : "false").ToLowerInvariant() == "false" ? "true" : "false"), Enabled = Mod,
                };
                string Cfg(string section, string key, string fallback) => _cfg?.Get(section, key) ?? fallback;
                void Set(string section, string key, string value)
                {
                    try
                    {
                        _cfg?.Set(section, key, value);
                    }
                    catch (Exception e)
                    {
                        LauncherLog.Write("couldn't save the mod settings: " + e.Message);
                    }
                }

                string Cycle(string now, string[] all, int by) => all[((Array.IndexOf(all, now) < 0 ? 0 : Array.IndexOf(all, now)) + by + all.Length) % all.Length];
                int Int(string section, string key, int fallback) => int.TryParse(Cfg(section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
                var snaps = new[] { "15", "30", "45", "60", "90" };
                var openings = new[] { "FirstLaunchOnly", "On", "Off" };
                var runtimes = GameLocator.OpenXRRuntimes();

                _opts = new List<Opt>
                {
                    new Opt { Header = true, Label = "IN VR" },
                    new Opt
                    {
                        Label = "TURNING", Help = "Right stick: SNAP turns a step at a time (comfortable), SMOOTH turns as you hold it.",
                        Value = () => Cfg("Comfort", "TurnStyle", "Snap").ToUpperInvariant(),
                        Step = by => Set("Comfort", "TurnStyle", Cycle(Cfg("Comfort", "TurnStyle", "Snap"), new[] { "Snap", "Smooth" }, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "SNAP ANGLE", Help = "How far one snap turns.",
                        Value = () => Cfg("Comfort", "SnapAngle", "30") + "°",
                        Step = by => Set("Comfort", "SnapAngle", Cycle(Cfg("Comfort", "SnapAngle", "30"), snaps, by)),
                        Enabled = () => Mod() && Cfg("Comfort", "TurnStyle", "Snap") == "Snap",
                    },
                    new Opt
                    {
                        Label = "VIEW HEIGHT", Help = "How high you see from. GAME is the character's own eyes (1.5 m up: a grown-up can feel small); +10 or +20 CM makes you that much taller.",
                        Value = () => Cfg("Comfort", "ViewHeight", "Game") == "Plus10" ? "+10 CM" : Cfg("Comfort", "ViewHeight", "Game") == "Plus20" ? "+20 CM" : "GAME",
                        Step = by => Set("Comfort", "ViewHeight", Cycle(Cfg("Comfort", "ViewHeight", "Game"), new[] { "Game", "Plus10", "Plus20" }, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "SMOOTH TURN SPEED", Help = "Degrees a second at full stick.",
                        Value = () => Int("Comfort", "SmoothTurnSpeed", 150) + "°/S",
                        Step = by => Set("Comfort", "SmoothTurnSpeed", Math.Max(60, Math.Min(360, Int("Comfort", "SmoothTurnSpeed", 150) + by * 30)).ToString(CultureInfo.InvariantCulture)),
                        Enabled = () => Mod() && Cfg("Comfort", "TurnStyle", "Snap") == "Smooth",
                    },
                    new Opt
                    {
                        Label = "PLAY MODE", Help = "STANDING: hold the grip, swing and let go to throw your gun. SEATED: press the gun hand's grip and it flies at the enemy you point at.",
                        Value = () => Cfg("Controls", "PlayMode", "Standing").ToUpperInvariant(),
                        Step = by => Set("Controls", "PlayMode", Cycle(Cfg("Controls", "PlayMode", "Standing"), new[] { "Standing", "Seated" }, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "GUN HAND", Help = "The hand your guns go in: its trigger shoots, its grip throws; the other grip interacts. A gun you hand over stays in the hand that has it till the next level.",
                        Value = () => Cfg("Controls", "GunHand", "Right").ToUpperInvariant(),
                        Step = by => Set("Controls", "GunHand", Cycle(Cfg("Controls", "GunHand", "Right"), new[] { "Right", "Left" }, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "THROW HELP", Help = "STANDING: a throw that's nearly on an enemy goes onto them.",
                        Value = () => Cfg("Controls", "ThrowHelp", "true").ToLowerInvariant() == "false" ? "OFF" : "ON",
                        Step = by => Set("Controls", "ThrowHelp", Cfg("Controls", "ThrowHelp", "true").ToLowerInvariant() == "false" ? "true" : "false"),
                        Enabled = () => Mod() && Cfg("Controls", "PlayMode", "Standing") != "Seated",
                    },
                    new Opt
                    {
                        Label = "LEVEL SELECT", Help = "IN THE CAR: sit in the car with the map book and point at a level (trigger plays it, a grip turns the page). ON THE SCREEN: it plays on the movie screen.",
                        Value = () => Cfg("Controls", "LevelSelectInVR", "true").ToLowerInvariant() == "false" ? "ON THE SCREEN" : "IN THE CAR",
                        Step = by => Set("Controls", "LevelSelectInVR", Cfg("Controls", "LevelSelectInVR", "true").ToLowerInvariant() == "false" ? "true" : "false"), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "VR OPENING", Help = "The short opening when the game starts in VR (A, B or the menu button skips it).",
                        Value = () => Cfg("Opening", "Opening", "FirstLaunchOnly") == "On" ? "EVERY START" : Cfg("Opening", "Opening", "FirstLaunchOnly") == "Off" ? "OFF" : "FIRST START ONLY",
                        Step = by => Set("Opening", "Opening", Cycle(Cfg("Opening", "Opening", "FirstLaunchOnly"), openings, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "MONITOR VIEW", Help = "What your monitor shows: STEADY is a level, smooth view for anyone watching or recording; EYE is your headset's view as it is; BEHIND YOU shows you from over your shoulder, your whole body.",
                        Value = () => Cfg("Monitor", "MonitorView", "Steady") == "Behind" ? "BEHIND YOU" : Cfg("Monitor", "MonitorView", "Steady").ToUpperInvariant(),
                        Step = by => Set("Monitor", "MonitorView", Cycle(Cfg("Monitor", "MonitorView", "Steady"), new[] { "Steady", "Eye", "Behind" }, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "MONITOR FOV", Help = "How wide the STEADY view is (never wider than your headset shows).",
                        Value = () => Int("Monitor", "MonitorFov", 90) + "°",
                        Step = by => Set("Monitor", "MonitorFov", Math.Max(70, Math.Min(110, Int("Monitor", "MonitorFov", 90) + by * 5)).ToString(CultureInfo.InvariantCulture)),
                        Enabled = () => Mod() && Cfg("Monitor", "MonitorView", "Steady") == "Steady",
                    },
                    Toggle("Controls", "TwoHandAim", "TWO-HAND GUNS", "Your other hand on the gun (grip squeezed): the front of a rifle or shotgun holds and steers it; round your gun hand on a pistol or the Uzi it steadies your aim and takes some of the kick."),
                    Toggle("Controls", "SolidGuns", "SOLID GUNS", "Your gun stops at walls instead of going into them (your hand stays with it)."),
                    Toggle("Controls", "GunWeight", "GUN WEIGHT", "The heavy guns swing a touch heavier in one hand; your other hand on them steadies them."),
                    Toggle("Controls", "MotionDrink", "DRINK FOR REAL", "Grab a beer, an energy drink or the lizard, and bring it to your mouth. Off: grabbing it drinks it, as in the game."),
                    Toggle("Controls", "GunGripAdjust", "GUN GRIP ADJUST", "Hold a gun and set how it sits in your hand, live: the gun hand's stick moves it (a click: the next way, held in: back as it was), or grab it with your other hand and place it. Each gun keeps its own. Off when you're done (it borrows that stick).", false),
                    Toggle("Display", "AmmoCounter", "AMMO COUNT", "An ammo count on the back of your gun (EMPTY: time to throw it)."),
                    Toggle("Display", "SniperScope", "SNIPER SCOPE", "A scope on the sniper rifle that magnifies when you put your eye to it."),
                    new Opt
                    {
                        Label = "LASER SIGHT", Help = "For getting used to aiming: DOT puts a red dot where your shots land, BEAM adds a beam from the barrel to it.",
                        Value = () => Cfg("Display", "LaserSight", "Off").ToUpperInvariant(),
                        Step = by => Set("Display", "LaserSight", Cycle(Cfg("Display", "LaserSight", "Off"), new[] { "Off", "Dot", "Beam" }, by)), Enabled = Mod,
                    },
                    Toggle("Display", "HitFlash", "HIT FLASH", "When you're hurt, the game's red-pink flash rings your view. Off: no flash (a hit still buzzes both hands)."),
                    new Opt { Header = true, Label = "GORE" },
                    new Opt
                    {
                        Label = "GORE", Help = "EXTRA: kicks splatter the walls, headshots pop heads, wounds and blood stay on bodies and on you. GAME: the game's own blood. (The game's SHOW BLOOD setting still decides if there's any.)",
                        Value = () => Cfg("Gore", "Gore", "Extra").ToUpperInvariant(),
                        Step = by => Set("Gore", "Gore", Cycle(Cfg("Gore", "Gore", "Extra"), new[] { "Off", "Game", "Extra" }, by)), Enabled = Mod,
                    },
                    new Opt
                    {
                        Label = "BLOOD COLOUR", Help = "PINK is the game's own; RED for the classic look.",
                        Value = () => Cfg("Gore", "BloodColour", "Pink").ToUpperInvariant(),
                        Step = by => Set("Gore", "BloodColour", Cycle(Cfg("Gore", "BloodColour", "Pink"), new[] { "Pink", "Red" }, by)),
                        Enabled = () => Mod() && Cfg("Gore", "Gore", "Extra") != "Off",
                    },
                    Toggle("Gore", "WoundsOnYou", "WOUNDS ON YOU", "Where you're hit, your body is wounded and bleeds as the enemies do, until you heal (EXTRA gore). Your health is the game's own."),
                    new Opt { Header = true, Label = "FUN" },
                    Toggle("Fun", "FlipOff", "FLIP THEM OFF", "Enemies who see your middle finger (right stick click) come for you."),
                    new Opt { Header = true, Label = "LAUNCHER" },
                    new Opt
                    {
                        Label = "VR RUNTIME", Help = "Which VR app the game talks to. PC DEFAULT uses the one Windows has set.",
                        Value = () =>
                        {
                            var r = runtimes.FirstOrDefault(x => string.Equals(x.Json, LauncherSettings.Runtime, StringComparison.OrdinalIgnoreCase));
                            return r.Json == null ? "PC DEFAULT" : r.Name;
                        },
                        Step = by =>
                        {
                            var all = new[] { "" }.Concat(runtimes.Select(r => r.Json)).ToArray();
                            var i = Array.FindIndex(all, j => string.Equals(j, LauncherSettings.Runtime, StringComparison.OrdinalIgnoreCase));
                            LauncherSettings.Runtime = all[((i < 0 ? 0 : i) + by + all.Length) % all.Length];
                            RefreshStatus();
                        },
                    },
                    new Opt { Label = "MUSIC", Help = "Anger Foot's title music, here in the launcher (M).", Value = () => LauncherSettings.Music ? "ON" : "OFF", Step = by => LauncherSettings.Music = !LauncherSettings.Music },
                    new Opt
                    {
                        Label = "SOUNDS", Help = "The launcher's sound effects.", Value = () => LauncherSettings.Sounds ? "ON" : "OFF",
                        Step = by =>
                        {
                            LauncherSettings.Sounds = !LauncherSettings.Sounds;
                            Sfx.Muted = !LauncherSettings.Sounds;
                        },
                    },
                    new Opt { Label = "DOOR KICK OPENING", Help = "The launcher's opening.", Value = () => LauncherSettings.PlayIntro ? "ON" : "OFF", Step = by => LauncherSettings.PlayIntro = !LauncherSettings.PlayIntro },
                    new Opt { Label = "AFTER PLAY", Help = "What the launcher does once the game is up.", Value = () => LauncherSettings.CloseOnLaunch ? "CLOSE" : "STAY OPEN", Step = by => LauncherSettings.CloseOnLaunch = !LauncherSettings.CloseOnLaunch },
                };
                return _opts;
            }
        }

        private List<Opt> Selectable => Options.Where(o => !o.Header).ToList();

        private int PageCount
        {
            get
            {
                switch (_page)
                {
                    case Page.Options: return Selectable.Count;
                    case Page.Check: return CheckButtons.Count;
                    default: return 0;
                }
            }
        }

        private void Change(int by)
        {
            if (_page != Page.Options) return;
            var o = Selectable[_pageSel];
            if (!o.Enabled()) return;
            o.Step(by);
            Sfx.Play("tick", 0.6);
        }

        private void PageActivate()
        {
            switch (_page)
            {
                case Page.Options:
                    Change(1);
                    break;
                case Page.Check:
                    CheckButtons[_pageSel].Do();
                    break;
            }
        }

        private void DrawPage(DrawingContext dc)
        {
            var k = Ease.OutCubic(Ease.Span(_t, _pageSince, 0.3));
            var flick = FlickerOn(_t - _pageSince);
            dc.PushOpacity(k);
            dc.PushTransform(new TranslateTransform((1 - k) * 40, 0));

            // glass with a neon edge
            var r = Panel;
            dc.DrawRoundedRectangle(new SolidColorBrush(A(Color.FromRgb(8, 2, 18), 0.86)), null, r, 6, 6);
            dc.DrawRoundedRectangle(null, P(A(Cyan, 0.08 * flick), 12), r, 6, 6);
            dc.DrawRoundedRectangle(null, P(A(Cyan, 0.18 * flick), 5), r, 6, 6);
            dc.DrawRoundedRectangle(null, P(A(Mix(Cyan, Colors.White, 0.4), 0.9 * flick), 1.4), r, 6, 6);
            var title = _page == Page.Options ? "OPTIONS" : _page == Page.Controls ? "CONTROLS" : "VR CHECK";
            Neon(dc, title, Sign, 30, Pink, flick, r.Left + 34, r.Top + 24);

            switch (_page)
            {
                case Page.Options:
                    DrawOptions(dc, r);
                    break;
                case Page.Controls:
                    DrawControls(dc, r);
                    break;
                case Page.Check:
                    DrawCheck(dc, r);
                    break;
            }

            // back
            var back = new Rect(r.Right - 112, r.Top + 26, 86, 28);
            Text(dc, "◂ BACK", BodyBold, 15, Dim, back.Right, back.Top + 4, 1);
            Hit(back, () => Open(Page.Home));
            dc.Pop();
            dc.Pop();
        }

        private double _optScroll;

        private void DrawOptions(DrawingContext dc, Rect r)
        {
            // the list scrolls (there are more options than fit): the selected one is always in view
            var top = r.Top + 78;
            var bottom = r.Bottom - 66;
            double full = 0, selAt = 0;
            var n = 0;
            foreach (var o in Options)
            {
                if (o.Header)
                {
                    full += 25;
                    continue;
                }

                if (_inPage && n == _pageSel) selAt = full;
                n++;
                full += 31;
            }

            var room = bottom - top;
            var want = _optScroll;
            if (selAt - want > room - 40) want = selAt - room + 40;
            if (selAt - want < 30) want = selAt - 30;
            want = Math.Max(0, Math.Min(Math.Max(0, full - room), want));
            _optScroll += (want - _optScroll) * 0.35;
            if (Math.Abs(want - _optScroll) < 0.5) _optScroll = want;
            dc.PushClip(new RectangleGeometry(new Rect(r.Left, top - 6, r.Width, room + 6)));
            var y = top - _optScroll;
            var index = 0;
            Opt current = null;
            foreach (var o in Options)
            {
                if (o.Header)
                {
                    Text(dc, o.Label, BodyBold, 12.5, new SolidColorBrush(A(Cyan, 0.8)), r.Left + 36, y + 6);
                    dc.DrawLine(P(A(Cyan, 0.25), 1), new Point(r.Left + 36 + TextWidth(o.Label, BodyBold, 12.5) + 10, y + 15), new Point(r.Right - 36, y + 15));
                    y += 25;
                    continue;
                }

                var i = index++;
                var visible = y > top - 20 && y < bottom - 10;
                var selected = _inPage && i == _pageSel;
                var on = o.Enabled();
                if (selected)
                {
                    current = o;
                    dc.DrawRoundedRectangle(new SolidColorBrush(A(Pink, 0.16)), P(A(Pink, 0.55), 1.2), new Rect(r.Left + 22, y - 3, r.Width - 44, 29), 4, 4);
                }

                var labelBrush = !on ? Faint : selected ? White : Dim;
                Text(dc, o.Label, BodyBold, 16, labelBrush, r.Left + 38, y + 3);
                var value = on || _cfg != null ? o.Value() : "FIND THE GAME FIRST";
                var vx = r.Right - 64;
                var valueBrush = !on ? Faint : selected ? new SolidColorBrush(Yellow) : White;
                Text(dc, value, Sign, 15, valueBrush, vx, y + 3, 1);
                if (on && visible)
                {
                    var vw = TextWidth(value, Sign, 15);
                    Text(dc, "‹", BodyBold, 22, selected ? new SolidColorBrush(Pink) : Faint, vx - vw - 16, y - 1, 1);
                    Text(dc, "›", BodyBold, 22, selected ? new SolidColorBrush(Pink) : Faint, r.Right - 40, y - 1, 1);
                    var row = i;
                    Hit(new Rect(vx - vw - 34, y - 3, 22, 29), () =>
                    {
                        SelectPage(row);
                        Change(-1);
                    }, () => SelectPage(row));
                    Hit(new Rect(r.Left + 22, y - 3, r.Width - 44, 29), () =>
                    {
                        SelectPage(row);
                        Change(1);
                    }, () => SelectPage(row));
                }

                y += 31;
            }

            dc.Pop();
            if (full > room)
            {
                // a thin scroll bar on the right
                var bar = room * room / full;
                var at = top + (room - bar) * (_optScroll / Math.Max(1, full - room));
                dc.DrawRoundedRectangle(new SolidColorBrush(A(Pink, 0.45)), null, new Rect(r.Right - 18, at, 4, bar), 2, 2);
            }

            var help = current?.Help ?? "Pick with the arrows or the mouse: ‹ › or click changes it. The VR ones apply next time you play (or straight away if the game is running).";
            Text(dc, help, Body, 14, Dim, r.Left + 36, r.Bottom - 50, 0, r.Width - 72);
        }

        private void DrawControls(DrawingContext dc, Rect r)
        {
            var left = _cfg?.Get("Controls", "GunHand") == "Left";
            var seated = _cfg?.Get("Controls", "PlayMode") == "Seated";
            var gunSide = left ? "LEFT" : "RIGHT";
            var throwDo = seated ? "press: throw at them" : "let go mid-swing: throw";
            var rows = new (string L, string LDo, string R, string RDo)[]
            {
                ("STICK", "move (walk the movie house)", "STICK", "turn (snap or smooth)"),
                ("X", "KICK", "B", "KICK"),
                ("Y", "shoe power", "A", "jump"),
                (left ? "TRIGGER" : "GRIP", left ? "SHOOT" : "interact, catch", left ? "GRIP" : "TRIGGER", left ? "interact, catch" : "SHOOT"),
                (left ? "GRIP" : "", left ? throwDo : "", left ? "" : "GRIP", left ? "" : throwDo),
                ("MENU", "pause", "CLICK STICK", "the middle finger"),
                ("HOLD STICK", "recenter", "", ""),
            };
            var colL = r.Left + 36;
            var colR = r.Left + r.Width / 2 + 10;
            Text(dc, "LEFT HAND", BodyBold, 13, new SolidColorBrush(A(Cyan, 0.85)), colL, r.Top + 80);
            Text(dc, "RIGHT HAND", BodyBold, 13, new SolidColorBrush(A(Cyan, 0.85)), colR, r.Top + 80);
            var y = r.Top + 108;
            foreach (var row in rows)
            {
                void Cell(double x, string button, string does)
                {
                    if (button.Length == 0) return;
                    var big = does == does.ToUpperInvariant();
                    var bw = Math.Max(30, TextWidth(button, Sign, 13) + 16);
                    dc.DrawRoundedRectangle(new SolidColorBrush(A(Pink, big ? 0.3 : 0.14)), P(A(Pink, 0.7), 1.2), new Rect(x, y, bw, 26), 13, 13);
                    Text(dc, button, Sign, 13, White, x + bw / 2, y + 4, 0.5);
                    Text(dc, does, big ? BodyBold : Body, 16, big ? new SolidColorBrush(Yellow) : Dim, x + bw + 12, y + 3);
                }

                Cell(colL, row.L, row.LDo);
                Cell(colR, row.R, row.RDo);
                y += 40;
            }

            y += 6;
            dc.DrawLine(P(A(Cyan, 0.25), 1), new Point(r.Left + 36, y), new Point(r.Right - 36, y));
            y += 16;
            Text(dc, "YOUR BODY", BodyBold, 13, new SolidColorBrush(A(Cyan, 0.85)), colL, y);
            Text(dc, "Full body: look down and it's you, in the shoes you've picked. Your hand aims your gun.", Body, 15, Dim, colL, y + 22, 0, r.Width - 72);
            y += 74;
            Text(dc, "MENUS AND THE MOVIE HOUSE", BodyBold, 13, new SolidColorBrush(A(Cyan, 0.85)), colL, y);
            Text(dc, "Point at the big screen with either hand and pull the trigger (or look at it and press A/X); B goes back. The left stick walks you round the movie house.", Body, 15, Dim, colL, y + 22, 0, r.Width - 72);
            Text(dc, seated
                ? $"Swap hands: hold your gun with both and press its grip. It starts in your {gunSide.ToLowerInvariant()} hand."
                : $"Toss your gun hand to hand and squeeze to catch it. It starts in your {gunSide.ToLowerInvariant()} hand.", Body, 13.5, Faint, colL, r.Bottom - 42);
        }

        // ---------------------------------------------------------------- VR CHECK

        private sealed class Button
        {
            public string Label;
            public Action Do;
            public Color Color;
        }

        private List<Button> CheckButtons
        {
            get
            {
                var list = new List<Button>();
                if (_game == null) return list;
                var installed = ModInstaller.InstalledVersion(_game) != null;
                list.Add(new Button
                {
                    Label = !installed ? "INSTALL" : ModInstaller.NeedsInstall(_game) ? "UPDATE" : "REPAIR", Color = Green,
                    Do = () => _session.Install(_game, _t),
                });
                if (installed)
                    list.Add(new Button
                    {
                        Label = _t - _uninstallArmed < 3 ? "SURE? CLICK AGAIN" : "UNINSTALL", Color = Red,
                        Do = () =>
                        {
                            if (_t - _uninstallArmed < 3)
                            {
                                try
                                {
                                    ModInstaller.Uninstall(_game);
                                }
                                catch (Exception e)
                                {
                                    LauncherLog.Write("uninstall failed: " + e);
                                }

                                _uninstallArmed = -10;
                                RefreshStatus();
                                RunChecks();
                            }
                            else
                            {
                                _uninstallArmed = _t;
                            }
                        },
                    });
                list.Add(new Button { Label = "GAME LOG", Color = Cyan, Do = () => GameLaunch.OpenFile(_game.LogFile) });
                list.Add(new Button { Label = "GAME FOLDER", Color = Cyan, Do = () => GameLaunch.OpenFolder(_game.Dir) });
                return list;
            }
        }

        private void RunChecks()
        {
            if (_checking) return;
            _checking = true;
            _checks = null;
            Task.Run(() =>
            {
                try
                {
                    _checks = Diagnostics.Run(_game);
                }
                catch (Exception e)
                {
                    LauncherLog.Write("checks failed: " + e);
                    _checks = new List<Check> { new Check { Title = "VR CHECK", Level = CheckLevel.Fail, Detail = e.Message } };
                }
                finally
                {
                    _checking = false;
                }
            });
        }

        private void DrawCheck(DrawingContext dc, Rect r)
        {
            var checks = _checks;
            var y = r.Top + 82;
            if (checks == null)
            {
                Text(dc, "CHECKING...", BodyBold, 18, Dim, r.Left + 36, y);
                if (!_checking) RunChecks();
            }
            else
            {
                foreach (var c in checks)
                {
                    var color = c.Level == CheckLevel.Ok ? Green : c.Level == CheckLevel.Warn ? Amber : c.Level == CheckLevel.Fail ? Red : Cyan;
                    dc.DrawEllipse(new SolidColorBrush(A(color, 0.25)), null, new Point(r.Left + 44, y + 11), 8, 8);
                    dc.DrawEllipse(new SolidColorBrush(color), null, new Point(r.Left + 44, y + 11), 4, 4);
                    Text(dc, c.Title, Sign, 13, White, r.Left + 62, y + 2);
                    Text(dc, c.Detail ?? "", Body, 14.5, Dim, r.Left + 220, y + 1, 0, r.Width - 250);
                    y += c.Detail != null && TextWidth(c.Detail, Body, 14.5) > r.Width - 250 ? 44 : 30;
                }
            }

            // buttons
            var bx = r.Left + 36;
            var by = r.Bottom - 62;
            var buttons = CheckButtons;
            for (var i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i];
                var w = TextWidth(b.Label, Sign, 14) + 30;
                var rect = new Rect(bx, by, w, 36);
                var selected = _inPage && i == _pageSel;
                dc.DrawRoundedRectangle(new SolidColorBrush(A(b.Color, selected ? 0.3 : 0.1)), P(A(b.Color, selected ? 1 : 0.55), selected ? 2 : 1.2), rect, 18, 18);
                if (selected) dc.DrawRoundedRectangle(null, P(A(b.Color, 0.15), 8), rect, 18, 18);
                Text(dc, b.Label, Sign, 14, selected ? White : Dim, rect.Left + w / 2, rect.Top + 9, 0.5);
                var index = i;
                Hit(rect, () =>
                {
                    SelectPage(index);
                    PageActivate();
                }, () => SelectPage(index));
                bx += w + 12;
            }
        }
    }
}
