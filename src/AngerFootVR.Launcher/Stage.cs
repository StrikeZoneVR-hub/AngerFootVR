using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AngerFootVR.Launcher.Effects;
using AngerFootVR.Launcher.Game;

namespace AngerFootVR.Launcher
{
    /// <summary>
    /// The launcher, drawn fresh every frame at 1280x720 (scaled to the window): a rainy neon night over Anger Foot's own
    /// title scene, the game's logo with a flickering VR sign, and the menu as neon tubes that buzz on when you pick
    /// them. It opens by kicking a door in (Stage.Intro.cs). The game's pictures and music come from your own copy
    /// (taken by the mod the first time it runs); before that it draws its own lettering.
    /// </summary>
    public sealed partial class Stage : FrameworkElement
    {
        public const double W = 1280, H = 720;

        // ---------------------------------------------------------------- look

        private static Typeface Face(string family) =>
            new Typeface(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#" + family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Typeface Sign = Face("Bungee");
        private static readonly Typeface Script = Face("Neonderthaw");
        private static readonly Typeface Body = new Typeface(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface BodyBold = new Typeface(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static readonly Color Pink = Color.FromRgb(0xFF, 0x2A, 0x86);
        private static readonly Color Cyan = Color.FromRgb(0x2B, 0xF0, 0xFF);
        private static readonly Color Yellow = Color.FromRgb(0xFF, 0xE0, 0x3A);
        private static readonly Color Green = Color.FromRgb(0x4D, 0xFF, 0x9A);
        private static readonly Color Amber = Color.FromRgb(0xFF, 0xA8, 0x22);
        private static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x3B);
        private static readonly Brush Dim = Frozen(Color.FromRgb(0xB8, 0xA6, 0xC8));
        private static readonly Brush Faint = Frozen(Color.FromArgb(150, 0xB8, 0xA6, 0xC8));
        private static readonly Brush White = Frozen(Color.FromRgb(0xFA, 0xF4, 0xFF));
        private static readonly Brush Night = Frozen(Color.FromRgb(0x07, 0x02, 0x0E));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Color A(Color c, double alpha) => Color.FromArgb((byte)Math.Max(0, Math.Min(255, alpha * 255)), c.R, c.G, c.B);

        private static Color Mix(Color a, Color b, double k) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

        // ---------------------------------------------------------------- state

        private readonly GameInstall _game;
        private readonly Music _music;
        private readonly Session _session = new Session();
        private readonly ImageSource _bg, _logo, _logoGlow;
        private readonly Random _rng = new Random(5);
        private readonly List<(Rect R, Action Click, Action Hover, Cursor Cursor)> _hits = new List<(Rect, Action, Action, Cursor)>();
        private readonly Dictionary<string, Geometry> _glyphs = new Dictionary<string, Geometry>();
        private readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        private Point _mouse = new Point(W / 2, H / 2), _parallax;
        private double _t, _lastT = -1, _dt;
        private bool _ready;
        private string _statusLine;
        private Color _statusColor;

        private enum Page { Home, Options, Controls, Check }
        private Page _page = Page.Home;
        private double _pageSince = -10;
        private bool _inPage;
        private int _menuSel, _pageSel;
        private readonly double[] _litSince = new double[8];
        private double _closing = -1;
        private bool _closeForGame;

        private static readonly string[] MenuItems = { "PLAY IN VR", "PLAY FLAT", "OPTIONS", "CONTROLS", "VR CHECK", "QUIT" };

        public event Action DragRequested, MinimizeRequested, CloseRequested;
        public Func<bool> Minimized;

        public Stage(GameInstall game)
        {
            _game = game;
            Width = W;
            Height = H;
            ClipToBounds = true;
            _music = new Music(game);
            if (game != null)
            {
                _bg = Load(Path.Combine(game.ArtDir, "title.png"));
                _logo = Load(Path.Combine(game.ArtDir, "logo-pink.png"));
                if (_logo != null) _logoGlow = Glow(_logo, 300, 20);
            }

            for (var i = 0; i < _litSince.Length; i++) _litSince[i] = -10;
            _litSince[0] = 0;
            BuildIntro();
            RefreshStatus();
            Focusable = true;
            MouseMove += (s, e) => Hover(e.GetPosition(this));
            MouseLeftButtonDown += (s, e) =>
            {
                Focus();
                if (!Click(e.GetPosition(this))) DragRequested?.Invoke();
            };
            MouseRightButtonDown += (s, e) => Back();
        }

        private static ImageSource Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;
                b.UriSource = new Uri(path);
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception e)
            {
                LauncherLog.Write("couldn't load " + path + ": " + e.Message);
                return null;
            }
        }

        /// <summary>A soft glow of a picture (blurred), drawn under it.</summary>
        private static ImageSource Glow(ImageSource src, double height, double radius)
        {
            var w = src.Width / src.Height * height;
            var pad = radius * 2;
            var v = new DrawingVisual { Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian } };
            using (var dc = v.RenderOpen()) dc.DrawImage(src, new Rect(pad, pad, w, height));
            var rtb = new RenderTargetBitmap((int)(w + pad * 2), (int)(height + pad * 2), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            rtb.Freeze();
            return rtb;
        }

        private void RefreshStatus()
        {
            if (_game == null)
            {
                _statusLine = "ANGER FOOT NOT FOUND: SEE VR CHECK";
                _statusColor = Red;
            }
            else if (ModInstaller.NeedsInstall(_game))
            {
                _statusLine = ModInstaller.InstalledVersion(_game) == null ? "PLAY IN VR SETS THE MOD UP FIRST" : "PLAY IN VR UPDATES THE MOD FIRST";
                _statusColor = Amber;
            }
            else
            {
                var json = string.IsNullOrEmpty(LauncherSettings.Runtime) ? GameLocator.ActiveRuntimeJson() : LauncherSettings.Runtime;
                _statusLine = "READY  ·  " + GameLocator.FriendlyRuntime(json).ToUpperInvariant();
                _statusColor = Green;
            }
        }

        // ---------------------------------------------------------------- frame

        public void Tick(double t)
        {
            _dt = _lastT < 0 ? 0 : Math.Min(0.1, Math.Max(0, t - _lastT));
            _lastT = t;
            _t = t;
            var target = new Point((_mouse.X - W / 2) / (W / 2), (_mouse.Y - H / 2) / (H / 2));
            _parallax = new Point(_parallax.X + (target.X - _parallax.X) * Math.Min(1, _dt * 3), _parallax.Y + (target.Y - _parallax.Y) * Math.Min(1, _dt * 3));
            TickIntro();

            // the music comes in as the door goes, and leaves when the game starts or the launcher closes
            var musicOn = LauncherSettings.Music && _ready && _closing < 0 && _session.State != Session.Kind.Launched && !(Minimized?.Invoke() ?? false);
            _music.Tick(t, musicOn ? 0.55 : 0);

            if (_session.State == Session.Kind.Installed)
            {
                _session.Dismiss();
                RefreshStatus();
                _checks = null;
                Sfx.Play("hit", 0.6);
            }

            // the game is up: off we go (or out of the way)
            if (_session.State == Session.Kind.Launched && t - _launchedAt > 4 && _launchedAt > 0 && _closing < 0)
            {
                if (LauncherSettings.CloseOnLaunch) BeginClose(true);
                else
                {
                    _session.Dismiss();
                    _launchedAt = -1;
                    MinimizeRequested?.Invoke();
                }
            }

            if (_session.State == Session.Kind.Launched && _launchedAt < 0) _launchedAt = t;
            if (_closing >= 0 && t - _closing > (_closeForGame ? 1.4 : 0.45)) CloseRequested?.Invoke();
            InvalidateVisual();
        }

        private double _launchedAt = -1;

        public void Stop() => _music.Stop();

        public string Describe() =>
            $"page={_page} inPage={_inPage} menu={_menuSel} pageSel={_pageSel} ready={_ready} session={_session.State} status='{_session.Status}' game={(_game != null ? _game.Dir : "not found")} music={_music.Available} bg={_bg != null} logo={_logo != null} line='{_statusLine}'";

        // ---------------------------------------------------------------- input

        public bool OnKey(Key key)
        {
            if (!_ready)
            {
                SkipIntro();
                return true;
            }

            if (_session.State == Session.Kind.Failed)
            {
                _session.Dismiss();
                return true;
            }

            if (_session.State != Session.Kind.Idle) return true;
            switch (key)
            {
                case Key.Up:
                case Key.W:
                    Move(-1);
                    return true;
                case Key.Down:
                case Key.S:
                    Move(1);
                    return true;
                case Key.Left:
                case Key.A:
                    if (_inPage) Change(-1);
                    return true;
                case Key.Right:
                case Key.D:
                    if (_inPage) Change(1);
                    else if (_page != Page.Home) EnterPage();
                    return true;
                case Key.Enter:
                case Key.Space:
                    Activate();
                    return true;
                case Key.Escape:
                case Key.Back:
                    Back();
                    return true;
                case Key.M:
                    ToggleMusic();
                    return true;
            }

            return false;
        }

        private void Hover(Point p)
        {
            _mouse = p;
            if (!_ready) return;
            Cursor cursor = null;
            foreach (var h in _hits)
                if (h.R.Contains(p))
                {
                    h.Hover?.Invoke();
                    cursor = h.Cursor;
                    break;
                }

            Cursor = cursor;
        }

        private bool Click(Point p)
        {
            if (!_ready)
            {
                SkipIntro();
                return true;
            }

            if (_session.State == Session.Kind.Failed)
            {
                _session.Dismiss();
                return true;
            }

            foreach (var h in _hits)
                if (h.R.Contains(p))
                {
                    h.Hover?.Invoke();
                    h.Click?.Invoke();
                    return true;
                }

            return false;
        }

        /// <summary>Tests and captures: the mouse moves to <paramref name="p"/> (and clicks).</summary>
        public void Poke(Point p, bool click)
        {
            Hover(p);
            if (click) Click(p);
        }

        private void Move(int by)
        {
            if (_inPage)
            {
                var n = PageCount;
                if (n == 0) return;
                SelectPage((_pageSel + by + n) % n);
            }
            else
            {
                SelectMenu((_menuSel + by + MenuItems.Length) % MenuItems.Length);
            }
        }

        private void SelectMenu(int i)
        {
            if (_inPage)
            {
                _inPage = false;
            }

            if (i == _menuSel) return;
            _menuSel = i;
            _litSince[i] = _t;
            Sfx.Play("buzz", 0.35);
        }

        private void SelectPage(int i)
        {
            _inPage = true;
            if (i == _pageSel) return;
            _pageSel = i;
            Sfx.Play("tick", 0.5);
        }

        private void Activate()
        {
            if (_inPage)
            {
                PageActivate();
                return;
            }

            Sfx.Play("hit", 0.7);
            switch (_menuSel)
            {
                case 0:
                case 1:
                    Play(_menuSel == 0);
                    break;
                case 2:
                    Open(Page.Options);
                    break;
                case 3:
                    Open(Page.Controls);
                    break;
                case 4:
                    Open(Page.Check);
                    break;
                case 5:
                    BeginClose(false);
                    break;
            }
        }

        private void Back()
        {
            if (!_ready || _session.State != Session.Kind.Idle) return;
            if (_inPage) _inPage = false;
            else if (_page != Page.Home) Open(Page.Home);
        }

        private void Open(Page page)
        {
            if (_page == page)
            {
                if (page != Page.Home) EnterPage();
                return;
            }

            _page = page;
            _pageSince = _t;
            _pageSel = 0;
            _inPage = false;
            if (page == Page.Options || page == Page.Controls) _cfg = _game == null ? null : new ModSettingsFile(_game.ConfigFile);
            if (page == Page.Check) RunChecks();
            if (page != Page.Home) Sfx.Play("whoosh", 0.5);
        }

        private void EnterPage()
        {
            if (PageCount == 0) return;
            _inPage = true;
            _pageSel = Math.Min(_pageSel, PageCount - 1);
            Sfx.Play("tick", 0.5);
        }

        private void Play(bool vr)
        {
            if (_game == null)
            {
                Open(Page.Check);
                return;
            }

            if (GameLaunch.GameRunning())
            {
                _statusLine = "ANGER FOOT IS ALREADY RUNNING";
                _statusColor = Amber;
                return;
            }

            _launchedAt = -1;
            _session.Play(_game, vr, _t);
        }

        private void ToggleMusic()
        {
            LauncherSettings.Music = !LauncherSettings.Music;
            Sfx.Play("tick", 0.5);
        }

        private void BeginClose(bool forGame)
        {
            if (_closing >= 0) return;
            _closing = _t;
            _closeForGame = forGame;
        }

        // ---------------------------------------------------------------- drawing helpers

        private Geometry Glyphs(string text, Typeface face, double size)
        {
            var key = text + "|" + face.FontFamily.Source + "|" + size.ToString(CultureInfo.InvariantCulture);
            if (_glyphs.TryGetValue(key, out var g)) return g;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.White, 1.0);
            g = ft.BuildGeometry(new Point(0, 0));
            g.Freeze();
            _glyphs[key] = g;
            return g;
        }

        private Pen P(Color c, double width)
        {
            var key = ((long)c.A << 56) | ((long)c.R << 48) | ((long)c.G << 40) | ((long)c.B << 32) | (long)(width * 100);
            if (_pens.TryGetValue(key, out var p)) return p;
            var b = new SolidColorBrush(c);
            b.Freeze();
            p = new Pen(b, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            p.Freeze();
            _pens[key] = p;
            return p;
        }

        private static double TextWidth(string text, Typeface face, double size) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, Brushes.White, 1.0).WidthIncludingTrailingWhitespace;

        private static void Text(DrawingContext dc, string text, Typeface face, double size, Brush brush, double x, double y, double align = 0, double maxWidth = 0)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, 1.0);
            if (maxWidth > 0) ft.MaxTextWidth = maxWidth;
            dc.DrawText(ft, new Point(x - ft.Width * align, y));
        }

        /// <summary>A neon tube in the shape of the text: unlit glass, or lit (0..1) with its glow.</summary>
        private void Neon(DrawingContext dc, string text, Typeface face, double size, Color color, double lit, double x, double y, double align = 0, double glow = 1)
        {
            var g = Glyphs(text, face, size);
            var w = g.Bounds.Right;
            dc.PushTransform(new TranslateTransform(x - w * align, y));
            if (lit < 0.04)
            {
                dc.DrawGeometry(new SolidColorBrush(A(Mix(color, Colors.Black, 0.35), 0.55)), P(A(Mix(color, Colors.Black, 0.2), 0.75), 1.1), g);
            }
            else
            {
                foreach (var (width, alpha) in new[] { (16.0, 0.06), (9.0, 0.11), (4.5, 0.22) })
                    dc.DrawGeometry(null, P(A(color, alpha * lit * glow), width * Math.Max(0.6, size / 40)), g);
                dc.DrawGeometry(new SolidColorBrush(A(Mix(color, Colors.White, 0.6), lit)), P(A(color, lit), 1.2), g);
            }

            dc.Pop();
        }

        /// <summary>A neon tube buzzing on: stutters, then steady.</summary>
        private static double FlickerOn(double since)
        {
            if (since < 0) return 0;
            if (since < 0.05) return 0.9;
            if (since < 0.09) return 0.1;
            if (since < 0.15) return 0.75;
            if (since < 0.2) return 0.25;
            return 1;
        }

        /// <summary>An old sign's odd stutter (now and then).</summary>
        private static double Stutter(double t, double seed)
        {
            var k = Math.Floor(t * 9 + seed * 17);
            var h = Math.Abs(Math.Sin(k * 12.9898 + seed * 78.233) * 43758.5453) % 1;
            return h > 0.965 ? 0.35 : 1;
        }

        private void Hit(Rect r, Action click, Action hover = null) => _hits.Add((r, click, hover, Cursors.Hand));

        // ---------------------------------------------------------------- drawing

        protected override void OnRender(DrawingContext dc)
        {
            _hits.Clear();
            dc.DrawRectangle(Night, null, new Rect(0, 0, W, H));
            if (_i0 < 0 || Local < LogoTime + 0.45) DrawIntro(dc);
            else DrawCity(dc, 1);

            if (_ready)
            {
                DrawTitle(dc);
                DrawMenu(dc);
                if (_page != Page.Home) DrawPage(dc);
                else DrawStatus(dc);
                DrawChrome(dc);
                if (_session.State != Session.Kind.Idle) DrawSession(dc);
            }

            if (_closing >= 0)
            {
                var k = Ease.Span(_t, _closing, _closeForGame ? 1.3 : 0.4);
                dc.DrawRectangle(new SolidColorBrush(A(Colors.Black, k)), null, new Rect(0, 0, W, H));
            }
        }

        /// <summary>The night: the game's title scene (or a drawn skyline), rain, fog, and the dark for the menu.</summary>
        private void DrawCity(DrawingContext dc, double reveal)
        {
            var t = _t;
            if (_bg != null)
            {
                // slow drift, and a touch of parallax with the mouse
                var zoom = 1.3 + 0.03 * Math.Sin(t * 0.07);
                var w = W * zoom;
                var h = w * _bg.Height / _bg.Width;
                if (h < H * zoom) { h = H * zoom; w = h * _bg.Width / _bg.Height; }
                var x = (W - w) / 2 + 190 - _parallax.X * 18 + Math.Sin(t * 0.05) * 12;
                var y = (H - h) / 2 - _parallax.Y * 10;
                dc.DrawImage(_bg, new Rect(x, y, w, h));
            }
            else
            {
                DrawSkyline(dc);
            }

            // pink and blue haze drifting
            for (var i = 0; i < 4; i++)
            {
                var c = i % 2 == 0 ? Pink : Color.FromRgb(0x55, 0x30, 0xFF);
                var cx = W * (0.35 + 0.5 * ((i * 0.37 + t * 0.008 * (i + 1)) % 1));
                var cy = H * (0.45 + 0.25 * Math.Sin(t * 0.11 + i));
                var b = new RadialGradientBrush(A(c, 0.13), A(c, 0));
                dc.DrawEllipse(b, null, new Point(cx, cy), 420, 260);
            }

            DrawRain(dc, t);

            // the dark down the left for the menu, and a vignette
            var left = new LinearGradientBrush(A(Color.FromRgb(6, 0, 14), 0.9), A(Color.FromRgb(6, 0, 14), 0.0), new Point(0, 0), new Point(1, 0));
            dc.DrawRectangle(left, null, new Rect(0, 0, W * 0.55, H));
            var bottom = new LinearGradientBrush(A(Colors.Black, 0), A(Colors.Black, 0.75), new Point(0, 0), new Point(0, 1));
            dc.DrawRectangle(bottom, null, new Rect(0, H * 0.7, W, H * 0.3));
            var vignette = new RadialGradientBrush(A(Colors.Black, 0), A(Colors.Black, 0.5)) { GradientOrigin = new Point(0.66, 0.5), Center = new Point(0.66, 0.5), RadiusX = 0.85, RadiusY = 0.9 };
            dc.DrawRectangle(vignette, null, new Rect(0, 0, W, H));
        }

        private void DrawSkyline(DrawingContext dc)
        {
            var sky = new LinearGradientBrush(Color.FromRgb(0x12, 0x04, 0x22), Color.FromRgb(0x3A, 0x08, 0x30), new Point(0, 0), new Point(0, 1));
            dc.DrawRectangle(sky, null, new Rect(0, 0, W, H));
            var r = new Random(3);
            for (var layer = 0; layer < 2; layer++)
            {
                var x = -20.0;
                var shade = layer == 0 ? Color.FromRgb(0x1A, 0x06, 0x26) : Color.FromRgb(0x0C, 0x02, 0x14);
                while (x < W)
                {
                    var bw = 50 + r.NextDouble() * 110;
                    var bh = (layer == 0 ? 260 : 160) + r.NextDouble() * (layer == 0 ? 260 : 200);
                    dc.DrawRectangle(new SolidColorBrush(shade), null, new Rect(x - _parallax.X * (layer + 1) * 6, H - bh, bw, bh));
                    for (var wy = H - bh + 14; wy < H - 20; wy += 18)
                    for (var wx = x + 8; wx < x + bw - 10; wx += 14)
                        if (r.NextDouble() < 0.18)
                            dc.DrawRectangle(new SolidColorBrush(A(r.NextDouble() < 0.5 ? Pink : Yellow, 0.35 + 0.3 * r.NextDouble())), null, new Rect(wx - _parallax.X * (layer + 1) * 6, wy, 5, 8));
                    x += bw + 4;
                }
            }
        }

        private void DrawRain(DrawingContext dc, double t)
        {
            var pen = P(A(Color.FromRgb(0xC8, 0xD8, 0xFF), 0.16), 1.1);
            for (var i = 0; i < 110; i++)
            {
                var sx = (i * 97.13) % 1;
                var sy = (i * 57.71) % 1;
                var speed = 900 + (i % 7) * 90;
                var len = 14 + (i % 5) * 5;
                var y = (sy * (H + 100) + t * speed) % (H + 100) - 50;
                var x = (sx * (W + 200) - y * 0.18 - _parallax.X * 8) % (W + 200);
                if (x < -100) x += W + 200;
                dc.DrawLine(pen, new Point(x, y), new Point(x - len * 0.18, y - len));
            }
        }

        /// <summary>The game's logo (or its name in neon), the VR sign, and the credit.</summary>
        private void DrawTitle(DrawingContext dc)
        {
            var since = _t - _logoAt;
            var slam = Ease.OutBack(Ease.Span(since, 0, 0.32), 2.6);
            var scale = 1.7 - 0.7 * slam;
            var alpha = Ease.Span(since, 0, 0.12);
            const double lx = 70, ly = 30, lh = 250;
            dc.PushOpacity(alpha);
            dc.PushTransform(new ScaleTransform(scale, scale, lx + 120, ly + lh / 2));
            if (_logo != null)
            {
                var lw = _logo.Width / _logo.Height * lh;
                var pulse = 0.75 + 0.25 * Math.Sin(_t * 2.1);
                dc.PushOpacity(pulse);
                var pad = 40.0 * lh / 300;
                dc.DrawImage(_logoGlow, new Rect(lx - pad, ly - pad, lw + pad * 2, lh + pad * 2));
                dc.Pop();
                dc.DrawImage(_logo, new Rect(lx, ly, lw, lh));
            }
            else
            {
                Neon(dc, "ANGER", Sign, 84, Pink, 1, lx, ly + 20);
                Neon(dc, "FOOT", Sign, 84, Pink, 1, lx + 40, ly + 118);
            }

            dc.Pop();
            dc.Pop();

            // the VR sign buzzes on after the logo lands, and stutters now and then like an old sign
            var vr = FlickerOn(_t - _vrAt) * Stutter(_t, 1);
            dc.PushTransform(new RotateTransform(-9, 400, 220));
            Neon(dc, "VR", Script, 130, Cyan, vr, 338, 128);
            dc.Pop();
            var credit = FlickerOn(_t - _vrAt - 0.25);
            Neon(dc, "MOD BY STRIKEZONEVR", Sign, 15, Yellow, credit, 344, 262, 0, 0.7);
        }

        private void DrawMenu(DrawingContext dc)
        {
            double y = 330;
            for (var i = 0; i < MenuItems.Length; i++)
            {
                var big = i == 0;
                var size = big ? 38 : 27;
                var on = _t - (_menuAt + i * 0.09);
                if (on < 0) break;
                var selected = i == _menuSel;
                var lit = selected ? FlickerOn(_t - Math.Max(_litSince[i], _menuAt + i * 0.09)) : 0;
                if (_inPage && selected) lit *= 0.55;
                var appear = FlickerOn(on);
                var color = i == 0 ? Pink : i == MenuItems.Length - 1 ? Color.FromRgb(0xC0, 0x60, 0xFF) : Pink;
                var x = 84.0 + (selected ? 14 * Ease.OutCubic(Ease.Span(_t, _litSince[i], 0.15)) : 0);
                dc.PushOpacity(appear);
                Neon(dc, MenuItems[i], Sign, size, color, lit, x, y);
                if (selected && lit > 0.05)
                {
                    // a little neon arrow kicking at the item
                    var ax = 60 + Math.Sin(_t * 8) * 3;
                    var ay = y + size * 0.62;
                    var arrow = Geometry.Parse($"M {ax - 12},{ay - 9} L {ax},{ay} L {ax - 12},{ay + 9}");
                    dc.DrawGeometry(null, P(A(Yellow, 0.25 * lit), 7), arrow);
                    dc.DrawGeometry(null, P(A(Mix(Yellow, Colors.White, 0.5), lit), 2.4), arrow);
                }

                dc.Pop();
                var index = i;
                var w = Glyphs(MenuItems[i], Sign, size).Bounds.Right;
                Hit(new Rect(70, y - 4, w + 40, size + 12), () =>
                {
                    SelectMenu(index);
                    Activate();
                }, () => SelectMenu(index));
                y += big ? 60 : 44;
            }
        }

        private void DrawStatus(DrawingContext dc)
        {
            if (_statusLine == null) return;
            var a = Ease.Span(_t, _menuAt + 0.6, 0.4);
            dc.PushOpacity(a);
            var x = W - 46;
            var y = H - 70;
            var dot = 0.6 + 0.4 * Math.Sin(_t * 3);
            dc.DrawEllipse(new SolidColorBrush(A(_statusColor, 0.25 * dot)), null, new Point(x - TextWidth(_statusLine, BodyBold, 15) - 18, y + 10), 9, 9);
            dc.DrawEllipse(new SolidColorBrush(_statusColor), null, new Point(x - TextWidth(_statusLine, BodyBold, 15) - 18, y + 10), 4, 4);
            Text(dc, _statusLine, BodyBold, 15, new SolidColorBrush(Mix(_statusColor, Colors.White, 0.35)), x, y, 1);
            if (!_music.Available && _game != null)
                Text(dc, "The game's title music plays here after your first start with the mod.", Body, 12.5, Faint, x, y + 24, 1);
            dc.Pop();
        }

        private void DrawChrome(DrawingContext dc)
        {
            // footer
            Text(dc, $"ANGER FOOT VR {MainWindow.Version}  ·  a fan-made VR mod by StrikeZoneVR  ·  Anger Foot © Free Lives / Devolver Digital (not affiliated)", Body, 11.5, Faint, 84, H - 34);

            // music, minimise, close (top right)
            var hover = _hits.Count;
            var musicOn = LauncherSettings.Music;
            var mx = W - 132;
            Neon(dc, musicOn ? "♪" : "♪", Body, 22, musicOn ? Cyan : Color.FromRgb(0x70, 0x60, 0x80), musicOn ? 0.9 : 0, mx, 14, 0.5, 0.5);
            if (!musicOn) dc.DrawLine(P(A(Red, 0.9), 2), new Point(mx - 9, 22), new Point(mx + 9, 40));
            Hit(new Rect(mx - 18, 10, 36, 36), ToggleMusic);
            var min = new Rect(W - 96, 14, 30, 30);
            dc.DrawLine(P(A(Pink, 0.85), 2.2), new Point(min.Left + 8, min.Bottom - 10), new Point(min.Right - 8, min.Bottom - 10));
            Hit(min, () => MinimizeRequested?.Invoke());
            var close = new Rect(W - 56, 14, 30, 30);
            dc.DrawLine(P(A(Pink, 0.85), 2.2), new Point(close.Left + 9, close.Top + 9), new Point(close.Right - 9, close.Bottom - 9));
            dc.DrawLine(P(A(Pink, 0.85), 2.2), new Point(close.Right - 9, close.Top + 9), new Point(close.Left + 9, close.Bottom - 9));
            Hit(close, () => BeginClose(false));
        }

        /// <summary>PLAY: the screen goes dark round a card that says what's happening.</summary>
        private void DrawSession(DrawingContext dc)
        {
            var k = Ease.OutCubic(Ease.Span(_t, _session.Since, 0.35));
            dc.DrawRectangle(new SolidColorBrush(A(Color.FromRgb(4, 0, 10), 0.82 * k)), null, new Rect(0, 0, W, H));
            dc.PushOpacity(k);
            var failed = _session.State == Session.Kind.Failed;
            var title = failed ? "WHOOPS" : _session.Vr ? "PUT ON YOUR HEADSET" : "HERE WE GO";
            var pulse = failed ? 1 : 0.8 + 0.2 * Math.Sin(_t * 5);
            Neon(dc, title, Sign, 54, failed ? Red : Pink, pulse, W / 2, H / 2 - 70, 0.5);
            Text(dc, _session.Status, BodyBold, 20, White, W / 2, H / 2 + 14, 0.5);
            if (failed)
            {
                Text(dc, _session.Error ?? "", Body, 16, Dim, W / 2 - 360, H / 2 + 50, 0, 720);
                Text(dc, "PRESS ANY KEY OR CLICK", BodyBold, 14, Faint, W / 2, H / 2 + 130, 0.5);
            }
            else if (_session.Vr)
            {
                Text(dc, "The game opens in the movie house: point at the screen and pull the trigger.", Body, 16, Dim, W / 2, H / 2 + 50, 0.5);
            }

            // a spinning neon ring while it works
            if (_session.Busy)
            {
                var a = _t * 360 % 360;
                var arc = new PathGeometry();
                var c = new Point(W / 2, H / 2 + 120);
                var s = new Point(c.X + 22 * Math.Cos(a * Math.PI / 180), c.Y + 22 * Math.Sin(a * Math.PI / 180));
                var e = new Point(c.X + 22 * Math.Cos((a + 260) * Math.PI / 180), c.Y + 22 * Math.Sin((a + 260) * Math.PI / 180));
                var f = new PathFigure { StartPoint = s };
                f.Segments.Add(new ArcSegment(e, new Size(22, 22), 0, true, SweepDirection.Clockwise, true));
                arc.Figures.Add(f);
                dc.DrawGeometry(null, P(A(Cyan, 0.2), 9), arc);
                dc.DrawGeometry(null, P(Mix(Cyan, Colors.White, 0.5), 3), arc);
            }

            dc.Pop();
        }
    }
}
