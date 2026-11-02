using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AngerFootVR.Launcher.Effects;
using AngerFootVR.Launcher.Game;

namespace AngerFootVR.Launcher
{
    /// <summary>The borderless launcher window: the stage, scaled to fit, ticking every frame.</summary>
    public sealed class MainWindow : Window
    {
        public static readonly string Version = typeof(MainWindow).Assembly.GetName().Version.ToString(3);

        private readonly Stage _stage;
        private readonly string _captureDir;
        private readonly double _fps, _seconds;
        private readonly List<(double T, string Action)> _script = new List<(double, string)>();
        private readonly bool _smoke;

        public MainWindow(string captureDir = null, double fps = 0, double seconds = 0, string script = null, bool smoke = false)
        {
            _captureDir = captureDir;
            _fps = fps;
            _seconds = seconds;
            _smoke = smoke;
            Title = "Anger Foot VR";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanMinimize;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try
            {
                Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/angerfootvr.png"));
            }
            catch
            {
                // no icon
            }

            // 1280x720, or smaller to fit the screen
            var scale = Math.Min(1, Math.Min(SystemParameters.WorkArea.Width * 0.92 / Stage.W, SystemParameters.WorkArea.Height * 0.92 / Stage.H));
            Width = Stage.W * scale;
            Height = Stage.H * scale;

            if (script != null)
                foreach (var part in script.Split(';'))
                {
                    var i = part.IndexOf('=');
                    if (i > 0 && double.TryParse(part.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var at)) _script.Add((at, part.Substring(i + 1)));
                }

            if (captureDir != null || smoke)
            {
                // off screen
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -4000;
                Top = -4000;
                ShowInTaskbar = false;
            }

            Sfx.Muted = !LauncherSettings.Sounds || captureDir != null || smoke;
            if (captureDir != null)
            {
                Sfx.Log = new List<(double, string, double)>();
                Clock.Fixed = 0;
            }

            Sfx.Prepare();
            var game = GameLocator.Find();
            LauncherLog.Write($"Launcher {Version} open. Game: {(game != null ? game.Dir : "not found")}");
            _stage = new Stage(game) { Minimized = () => WindowState == WindowState.Minimized };
            _stage.DragRequested += () =>
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // the button's already up
                }
            };
            _stage.MinimizeRequested += () => WindowState = WindowState.Minimized;
            _stage.CloseRequested += Close;
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };
            KeyDown += (s, e) =>
            {
                if (_stage.OnKey(e.Key == Key.System ? e.SystemKey : e.Key)) e.Handled = true;
            };
            Loaded += (s, e) =>
            {
                _stage.Focus();
                if (_captureDir != null) Capture();
                else CompositionTarget.Rendering += OnFrame;
            };
            Closed += (s, e) =>
            {
                CompositionTarget.Rendering -= OnFrame;
                _stage.Stop();
            };
        }

        private double _start = -1;

        private void OnFrame(object sender, EventArgs e)
        {
            var now = Clock.Now;
            if (_start < 0) _start = now;
            var t = now - _start;
            RunScript(t);
            if (WindowState != WindowState.Minimized || _smoke) _stage.Tick(now);
            if (_smoke && t > _seconds)
            {
                LauncherLog.Write("smoke: " + _stage.Describe());
                Close();
            }
        }

        private void RunScript(double t)
        {
            while (_script.Count > 0 && _script[0].T <= t)
            {
                var a = _script[0].Action;
                _script.RemoveAt(0);
                if (a.StartsWith("key:")) _stage.OnKey((Key)Enum.Parse(typeof(Key), a.Substring(4), true));
                else if (a.StartsWith("mouse:"))
                {
                    var xy = a.Substring(6).Split(',');
                    _stage.Poke(new Point(double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture)), false);
                }
                else if (a.StartsWith("click:"))
                {
                    var xy = a.Substring(6).Split(',');
                    _stage.Poke(new Point(double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture)), true);
                }
                else if (a == "note") LauncherLog.Write("script: " + _stage.Describe());
            }
        }

        /// <summary>--capture: steps the clock frame by frame and writes each frame as a 1920x1080 PNG (and the sounds'
        /// times to sfx.txt, to mix the recording's audio).</summary>
        private void Capture()
        {
            Directory.CreateDirectory(_captureDir);
            var frames = (int)(_fps * _seconds);
            var frame = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1) };
            timer.Tick += (s, e) =>
            {
                var t = frame / _fps;
                Clock.Fixed = t;
                RunScript(t);
                _stage.Tick(t);
                _stage.Measure(new Size(Stage.W, Stage.H));
                _stage.Arrange(new Rect(0, 0, Stage.W, Stage.H));
                _stage.UpdateLayout();
                var rtb = new RenderTargetBitmap(1920, 1080, 144, 144, PixelFormats.Pbgra32);
                rtb.Render(_stage);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(rtb));
                using (var f = File.Create(Path.Combine(_captureDir, $"f{frame:00000}.png"))) png.Save(f);
                frame++;
                if (frame < frames) return;
                timer.Stop();
                File.WriteAllLines(Path.Combine(_captureDir, "sfx.txt"),
                    Sfx.Log.Select(x => $"{x.T.ToString("0.###", CultureInfo.InvariantCulture)} {Sfx.FileOf(x.Name)} {x.Volume.ToString("0.##", CultureInfo.InvariantCulture)}"));
                LauncherLog.Write("capture done: " + frames + " frames; " + _stage.Describe());
                Close();
            };
            timer.Start();
        }
    }
}
