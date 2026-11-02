using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AngerFootVR.Launcher.Effects;
using AngerFootVR.Launcher.Game;

namespace AngerFootVR.Launcher
{
    /// <summary>
    /// The opening (about 4 seconds; any key or click skips it, OPTIONS turns it off): a dark brick wall, a door with the
    /// neon night glowing round its edges and a VR sign buzzing beside it. Two kicks shake it, leaving boot prints and
    /// cracks with light through them; the third blows it off its hinges into the room, splinters flying at you, and you
    /// go through the doorway into the city as the logo slams in and the music starts.
    /// </summary>
    public sealed partial class Stage
    {
        private const double Kick1 = 0.95, Kick2 = 1.8, Kick3 = 2.65, ZoomStart = 2.8, ZoomTime = 0.5, LogoTime = 3.05, VrTime = 3.55, MenuTime = 3.85;
        private static readonly Rect Doorway = new Rect(492, 150, 296, 510);
        private static readonly Point DoorCenter = new Point(640, 405);

        private double _i0 = -1;
        private ImageSource _wall, _door;
        private Geometry _crack1, _crack2, _print;
        private readonly List<Bit> _bits = new List<Bit>();
        private readonly HashSet<int> _fired = new HashSet<int>();

        private double _logoAt => _i0 + LogoTime;
        private double _vrAt => _i0 + VrTime;
        private double _menuAt => _i0 + MenuTime;
        private double _introEnd => _i0 + ZoomStart + ZoomTime;
        private double Local => _t - _i0;

        private struct Bit
        {
            public Point P;
            public Vector V;
            public double Age, Life, Size, Angle, Spin, Grow;
            public Color C;
            public bool Dust;
        }

        private static readonly (double T, string Name, double Volume)[] IntroSounds =
        {
            (0.35, "buzz", 0.45), (Kick1, "kick", 1.0), (Kick2, "kick2", 1.0), (Kick3, "smash", 1.0), (LogoTime, "hit", 0.8), (VrTime, "buzz", 0.55),
            (MenuTime, "tick", 0.3), (MenuTime + 0.09, "tick", 0.3), (MenuTime + 0.18, "tick", 0.3), (MenuTime + 0.27, "tick", 0.3), (MenuTime + 0.36, "tick", 0.3), (MenuTime + 0.45, "tick", 0.3),
        };

        private void BuildIntro()
        {
            _wall = Bake(W, H, DrawWall);
            _door = Bake(Doorway.Width - 6, Doorway.Height - 4, DrawDoor);
            var r = new Random(21);
            Geometry Crack(Point from, int arms)
            {
                var g = new StreamGeometry();
                using (var c = g.Open())
                    for (var a = 0; a < arms; a++)
                    {
                        var p = from;
                        var dir = r.NextDouble() * Math.PI * 2;
                        c.BeginFigure(p, false, false);
                        for (var s = 0; s < 6; s++)
                        {
                            dir += (r.NextDouble() - 0.5) * 1.1;
                            var len = 14 + r.NextDouble() * 26;
                            p = new Point(Math.Max(4, Math.Min(Doorway.Width - 10, p.X + Math.Cos(dir) * len)), Math.Max(4, Math.Min(Doorway.Height - 8, p.Y + Math.Sin(dir) * len)));
                            c.LineTo(p, true, false);
                        }
                    }

                g.Freeze();
                return g;
            }

            _crack1 = Crack(new Point(150, 300), 5);
            _crack2 = Crack(new Point(126, 228), 4);
            _print = FootPrint();
        }

        private static ImageSource Bake(double w, double h, Action<DrawingContext> draw)
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen()) draw(dc);
            var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>Old bricks, dark and grimy.</summary>
        private static void DrawWall(DrawingContext dc)
        {
            var r = new Random(4);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x05, 0x09)), null, new Rect(0, 0, W, H));
            const double bw = 58, bh = 24;
            for (var row = 0; row * bh < H; row++)
            for (var col = -1; col * bw < W; col++)
            {
                var x = col * bw + (row % 2) * bw / 2;
                var k = 0.75 + r.NextDouble() * 0.4;
                var c = Color.FromRgb((byte)(0x3A * k), (byte)(0x14 * k), (byte)(0x1C * k));
                dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(x + 2, row * bh + 2, bw - 4, bh - 4));
                if (r.NextDouble() < 0.12) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, new Rect(x + 2, row * bh + 2, bw - 4, bh - 4));
            }

            // grime running down
            for (var i = 0; i < 26; i++)
            {
                var x = r.NextDouble() * W;
                var len = 80 + r.NextDouble() * 300;
                var g = new LinearGradientBrush(Color.FromArgb(90, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), new Point(0, 0), new Point(0, 1));
                dc.DrawRectangle(g, null, new Rect(x, r.NextDouble() * 200, 6 + r.NextDouble() * 22, len));
            }

            // the floor
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x14, 0x08, 0x10), Color.FromRgb(0x05, 0x01, 0x04), new Point(0, 0), new Point(0, 1)), null, new Rect(0, Doorway.Bottom, W, H - Doorway.Bottom));
        }

        /// <summary>A heavy old door: planks, two panels, a kick plate, a knob and a spy hole.</summary>
        private void DrawDoor(DrawingContext dc)
        {
            var w = Doorway.Width - 6;
            var h = Doorway.Height - 4;
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x4A, 0x2A, 0x1A), Color.FromRgb(0x2A, 0x16, 0x0D), new Point(0, 0), new Point(1, 1)), null, new Rect(0, 0, w, h));
            var plank = new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1.5);
            for (var x = 37.0; x < w; x += 37) dc.DrawLine(plank, new Point(x, 0), new Point(x, h));
            var r = new Random(8);
            var grain = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 220, 180)), 1);
            for (var i = 0; i < 70; i++)
            {
                var x = r.NextDouble() * w;
                dc.DrawLine(grain, new Point(x, r.NextDouble() * h), new Point(x + (r.NextDouble() - 0.5) * 4, r.NextDouble() * h));
            }

            void PanelAt(Rect p)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null, p);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 200, 160)), 2), p.BottomLeft, p.BottomRight);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 200, 160)), 2), p.TopRight, p.BottomRight);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 2), p.TopLeft, p.TopRight);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 2), p.TopLeft, p.BottomLeft);
            }

            PanelAt(new Rect(34, 36, w - 68, 170));
            PanelAt(new Rect(34, 240, w - 68, 170));

            // kick plate with rivets
            var plate = new Rect(10, h - 74, w - 20, 62);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x5A, 0x58, 0x62), Color.FromRgb(0x2E, 0x2C, 0x34), new Point(0, 0), new Point(0, 1)), new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x18, 0x1E)), 1.5), plate);
            for (var x = plate.Left + 10; x < plate.Right; x += (plate.Width - 20) / 6)
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x8A, 0x88, 0x92)), null, new Point(x, plate.Top + 8), 2.5, 2.5);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x8A, 0x88, 0x92)), null, new Point(x, plate.Bottom - 8), 2.5, 2.5);
            }

            // knob, keyhole, spy hole
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xE8, 0xC0, 0x60), Color.FromRgb(0x6A, 0x48, 0x10)) { GradientOrigin = new Point(0.35, 0.35) }, null, new Point(w - 34, h * 0.52), 11, 11);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x10, 0x08, 0x04)), null, new Rect(w - 36, h * 0.52 + 18, 4, 9));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x20, 0x18, 0x14)), new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x90, 0x88)), 2), new Point(w / 2, 22), 5, 5);
        }

        /// <summary>A boot print (the sole and heel of a sneaker).</summary>
        private static Geometry FootPrint()
        {
            var g = new GeometryGroup();
            g.Children.Add(new EllipseGeometry(new Point(0, -14), 16, 24));
            g.Children.Add(new EllipseGeometry(new Point(2, 26), 12, 14));
            g.Freeze();
            return g;
        }

        // ---------------------------------------------------------------- timing

        private void TickIntro()
        {
            if (_i0 < 0) _i0 = LauncherSettings.PlayIntro ? _t + 0.15 : _t - (Kick3 + 0.05);
            var local = Local;
            for (var i = 0; i < IntroSounds.Length; i++)
            {
                if (_fired.Contains(i) || local < IntroSounds[i].T) continue;
                _fired.Add(i);
                if (local - IntroSounds[i].T < 0.25) Sfx.Play(IntroSounds[i].Name, IntroSounds[i].Volume);
                if (IntroSounds[i].T == Kick1) Burst(false, 12, 0.6);
                if (IntroSounds[i].T == Kick2) Burst(false, 18, 0.8);
                if (IntroSounds[i].T == Kick3) Burst(true, 70, 1);
            }

            _ready = _i0 >= 0 && local >= LogoTime - 0.05;

            // splinters and dust
            for (var i = _bits.Count - 1; i >= 0; i--)
            {
                var b = _bits[i];
                b.Age += _dt;
                b.P += b.V * _dt;
                b.V.Y += (b.Dust ? 30 : 1100) * _dt;
                if (b.Dust) b.V *= Math.Max(0, 1 - _dt * 2.5);
                b.Angle += b.Spin * _dt;
                if (b.Age > b.Life) _bits.RemoveAt(i);
                else _bits[i] = b;
            }
        }

        private void Burst(bool smash, int count, double power)
        {
            if (Local - (smash ? Kick3 : 0) > 0.6 && smash) return;
            for (var i = 0; i < count; i++)
            {
                var dust = !smash || i % 3 == 0;
                var edge = _rng.NextDouble() < 0.5;
                var p = smash
                    ? new Point(Doorway.Left + 30 + _rng.NextDouble() * (Doorway.Width - 60), Doorway.Top + 40 + _rng.NextDouble() * (Doorway.Height - 80))
                    : edge
                        ? new Point(Doorway.Left + _rng.NextDouble() * Doorway.Width, Doorway.Top + 2)
                        : new Point(_rng.NextDouble() < 0.5 ? Doorway.Left + 2 : Doorway.Right - 2, Doorway.Top + _rng.NextDouble() * Doorway.Height);
                var away = p - DoorCenter;
                away.Normalize();
                var speed = smash ? (dust ? 120 : 350 + _rng.NextDouble() * 650) : 20 + _rng.NextDouble() * 60;
                var shade = 0.6 + _rng.NextDouble() * 0.5;
                _bits.Add(new Bit
                {
                    P = p,
                    V = away * speed * power + new Vector(0, smash && !dust ? -250 - _rng.NextDouble() * 250 : dust ? 10 : 0),
                    Life = dust ? 0.9 + _rng.NextDouble() * 0.9 : 0.8 + _rng.NextDouble() * 0.6,
                    Size = dust ? 10 + _rng.NextDouble() * 22 : 4 + _rng.NextDouble() * 9,
                    Angle = _rng.NextDouble() * 360,
                    Spin = (_rng.NextDouble() - 0.5) * 1400,
                    Grow = dust ? 1.4 : 2.2 + _rng.NextDouble() * 2.5,
                    C = dust ? Color.FromRgb(0x8A, 0x78, 0x80) : Color.FromRgb((byte)(0x8A * shade), (byte)(0x55 * shade), (byte)(0x30 * shade)),
                    Dust = dust,
                });
            }
        }

        private void SkipIntro()
        {
            if (_ready || _i0 < 0) return;
            var local = Local;
            _i0 = _t - (MenuTime + 0.6);
            // the sounds skipped over stay quiet; the music comes in now
            for (var i = 0; i < IntroSounds.Length; i++) _fired.Add(i);
            _bits.Clear();
            if (local < Kick3) Sfx.Play("hit", 0.7);
        }

        // ---------------------------------------------------------------- drawing

        private static double Shake(double local, double at, double amplitude, double freq) => amplitude * Ease.Shake(Ease.Span(local, at, 0.4), freq);

        private void DrawIntro(DrawingContext dc)
        {
            var local = Local;
            if (_i0 < 0) return;

            var sx = Shake(local, Kick1, 7, 6) + Shake(local, Kick2, 11, 6) + Shake(local, Kick3, 24, 7) + Shake(local, LogoTime, 9, 5);
            var sy = Shake(local, Kick1, 4, 5) + Shake(local, Kick2, 7, 5) + Shake(local, Kick3, 16, 6) + Shake(local, LogoTime, 6, 4);
            dc.PushTransform(new TranslateTransform(sx, sy * 0.8));

            // the city beyond the door
            DrawCity(dc, 1);

            // you go through the doorway
            var z = 1 + 4.2 * Ease.InCubic(Ease.Span(local, ZoomStart, ZoomTime));
            Rect Zoomed(Rect r) => new Rect(DoorCenter.X + (r.Left - DoorCenter.X) * z, DoorCenter.Y + (r.Top - DoorCenter.Y) * z, r.Width * z, r.Height * z);
            var hole = Zoomed(Doorway);
            var zoom = new ScaleTransform(z, z, DoorCenter.X, DoorCenter.Y);

            // the door: shaken by the kicks, then off its hinges into the room
            var flight = Ease.Span(local, Kick3, 0.6);
            if (flight < 1)
            {
                dc.PushClip(new RectangleGeometry(hole));
                dc.PushTransform(zoom);
                var push = Shake(local, Kick1, 5, 9) + Shake(local, Kick2, 7, 9);
                var bulge = 1 + 0.02 * Math.Abs(Ease.Shake(Ease.Span(local, Kick1, 0.3), 3)) + 0.03 * Math.Abs(Ease.Shake(Ease.Span(local, Kick2, 0.3), 3));
                var g = new TransformGroup();
                g.Children.Add(new ScaleTransform(bulge, bulge, DoorCenter.X, DoorCenter.Y));
                g.Children.Add(new TranslateTransform(push, 0));
                if (flight > 0)
                {
                    var f = Ease.OutCubic(flight);
                    var s = 1 - 0.72 * f;
                    g.Children.Add(new RotateTransform(-58 * f, DoorCenter.X, DoorCenter.Y));
                    g.Children.Add(new ScaleTransform(s, s * (1 - 0.2 * f), DoorCenter.X, DoorCenter.Y));
                    g.Children.Add(new TranslateTransform(60 * f, -90 * Math.Sin(Math.PI * flight) + 70 * f));
                }

                dc.PushTransform(g);
                dc.PushOpacity(1 - Ease.Span(flight, 0.55, 0.45));
                var door = new Rect(Doorway.Left + 3, Doorway.Top + 2, Doorway.Width - 6, Doorway.Height - 4);
                dc.DrawImage(_door, door);
                dc.PushTransform(new TranslateTransform(door.Left, door.Top));
                if (local >= Kick1) dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0x0C, 0x06, 0x04)), null, Offset(_print, 150, 300, -8));
                if (local >= Kick2)
                {
                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(160, 0x0C, 0x06, 0x04)), null, Offset(_print, 126, 228, 12));
                    // cracks, with the night's light coming through them
                    dc.DrawGeometry(null, P(A(Pink, 0.18), 7), _crack1);
                    dc.DrawGeometry(null, P(Color.FromRgb(0x14, 0x06, 0x04), 2.6), _crack1);
                    dc.DrawGeometry(null, P(A(Mix(Pink, Colors.White, 0.4), 0.9), 1), _crack1);
                    dc.DrawGeometry(null, P(A(Pink, 0.18), 7), _crack2);
                    dc.DrawGeometry(null, P(Color.FromRgb(0x14, 0x06, 0x04), 2.4), _crack2);
                    dc.DrawGeometry(null, P(A(Mix(Pink, Colors.White, 0.4), 0.9), 1), _crack2);
                }

                dc.Pop();
                dc.Pop();
                dc.Pop();
                dc.Pop();
                dc.Pop();
            }

            // the wall round the doorway
            var screen = new RectangleGeometry(new Rect(-60, -60, W + 120, H + 120));
            dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, screen, new RectangleGeometry(hole)));
            dc.PushTransform(zoom);
            dc.DrawImage(_wall, new Rect(0, 0, W, H));
            // the night's light spilling out round the door and across the floor
            var open = Ease.Span(local, Kick3, 0.15);
            var leak = (0.55 + 0.1 * Math.Sin(local * 7)) * Ease.Span(local, 0.15, 0.5) + open * 0.6;
            var spill = new RadialGradientBrush(A(Pink, 0.32 * leak), A(Pink, 0)) { Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.6, RadiusY = 1 };
            dc.DrawRectangle(spill, null, new Rect(Doorway.Left - 220, Doorway.Bottom, Doorway.Width + 440, H - Doorway.Bottom));
            var halo = new RadialGradientBrush(A(Color.FromRgb(0x9A, 0x30, 0xFF), 0.22 * leak), A(Colors.Black, 0)) { RadiusX = 0.5, RadiusY = 0.5 };
            dc.DrawEllipse(halo, null, DoorCenter, 520, 420);
            // the frame
            var trim = new Pen(new SolidColorBrush(Color.FromRgb(0x1C, 0x10, 0x0C)), 16);
            dc.DrawRectangle(null, trim, new Rect(Doorway.Left - 8, Doorway.Top - 8, Doorway.Width + 16, Doorway.Height + 8));
            foreach (var (w, a) in new[] { (22.0, 0.08), (10.0, 0.16), (4.0, 0.35), (1.6, 0.9) })
                dc.DrawRectangle(null, P(A(Mix(Pink, Colors.White, a > 0.5 ? 0.5 : 0), a * leak), w), new Rect(Doorway.Left + 1, Doorway.Top + 1, Doorway.Width - 2, Doorway.Height - 1));
            // the VR sign by the door
            Neon(dc, "VR", Script, 84, Cyan, FlickerOn(local - 0.35) * Stutter(_t, 2), 880, 210);
            dc.Pop();
            dc.Pop();

            // the dark, lifting at the start, and darker away from the door before it goes
            var dark = 1 - Ease.Span(local, 0.05, 0.45);
            if (dark > 0) dc.DrawRectangle(new SolidColorBrush(A(Colors.Black, dark)), null, new Rect(-60, -60, W + 120, H + 120));
            var gloom = (1 - Ease.Span(local, Kick3, 0.5)) * 0.65;
            if (gloom > 0)
                dc.DrawRectangle(new RadialGradientBrush(A(Colors.Black, 0), A(Colors.Black, gloom)) { RadiusX = 0.55, RadiusY = 0.75 }, null, new Rect(-60, -60, W + 120, H + 120));

            DrawBits(dc);
            dc.Pop();

            // flashes
            var flash = 0.5 * (1 - Ease.Span(local, Kick3, 0.2)) * (local >= Kick3 ? 1 : 0) + 0.18 * (1 - Ease.Span(local, Kick2, 0.12)) * (local >= Kick2 ? 1 : 0);
            if (flash > 0.01) dc.DrawRectangle(new SolidColorBrush(A(Mix(Pink, Colors.White, 0.7), flash)), null, new Rect(0, 0, W, H));
        }

        private static Geometry Offset(Geometry g, double x, double y, double angle)
        {
            var c = g.Clone();
            var t = new TransformGroup();
            t.Children.Add(new RotateTransform(angle));
            t.Children.Add(new TranslateTransform(x, y));
            c.Transform = t;
            return c;
        }

        private void DrawBits(DrawingContext dc)
        {
            foreach (var b in _bits)
            {
                var k = b.Age / b.Life;
                var size = b.Size * (1 + b.Grow * k);
                if (b.Dust)
                {
                    var a = 0.28 * (1 - k);
                    dc.DrawEllipse(new RadialGradientBrush(A(b.C, a), A(b.C, 0)), null, b.P, size, size);
                }
                else
                {
                    dc.PushTransform(new RotateTransform(b.Angle, b.P.X, b.P.Y));
                    dc.PushOpacity(1 - Ease.Span(k, 0.7, 0.3));
                    dc.DrawRectangle(new SolidColorBrush(b.C), null, new Rect(b.P.X - size, b.P.Y - size * 0.22, size * 2, size * 0.44));
                    dc.Pop();
                    dc.Pop();
                }
            }
        }
    }
}
