using System;
using System.Globalization;
using System.Windows;
using AngerFootVR.Launcher.Game;

namespace AngerFootVR.Launcher
{
    public partial class App : Application
    {
        private static System.Threading.Mutex _single;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var args = e.Args;
            var quiet = args.Length > 0 && args[0].StartsWith("--") && args[0] != "--play";
            DispatcherUnhandledException += (s, ex) =>
            {
                LauncherLog.Write("Unhandled: " + ex.Exception);
                if (!quiet) MessageBox.Show(ex.Exception.Message, "Anger Foot VR", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // dev: "--capture <folder> <fps> <seconds> [script]" renders the launcher frame by frame to PNGs (off screen).
            // script: "time=action;..." with key:<Key>, mouse:x,y, click:x,y, note
            if (args.Length >= 4 && args[0] == "--capture")
            {
                Show(new MainWindow(args[1], double.Parse(args[2], CultureInfo.InvariantCulture), double.Parse(args[3], CultureInfo.InvariantCulture), args.Length >= 5 ? args[4] : null));
                return;
            }

            // dev: "--smoke <seconds> [script]" runs the real launcher off screen and muted, then quits (launcher.log)
            if (args.Length >= 2 && args[0] == "--smoke")
            {
                Show(new MainWindow(null, 0, double.Parse(args[1], CultureInfo.InvariantCulture), args.Length >= 3 ? args[2] : null, smoke: true));
                return;
            }

            // dev: "--install-to <game folder>" / "--uninstall-from <game folder>" (results in launcher.log)
            if (args.Length >= 2 && (args[0] == "--install-to" || args[0] == "--uninstall-from"))
            {
                var g = new GameInstall { Dir = args[1] };
                try
                {
                    if (args[0] == "--install-to") ModInstaller.Install(g, st => LauncherLog.Write("install: " + st));
                    else ModInstaller.Uninstall(g);
                }
                catch (Exception ex)
                {
                    LauncherLog.Write(args[0] + " failed: " + ex);
                }

                LauncherLog.Write($"check: bepinex={ModInstaller.BepInExInstalled(g)} loader={ModInstaller.LoaderEnabled(g)} provider={ModInstaller.ProviderInstalled(g)} mod={ModInstaller.InstalledVersion(g)} needs={ModInstaller.NeedsInstall(g)}");
                Shutdown();
                return;
            }

            // "--play vr" / "--play flat": straight into the game with the saved options (for shortcuts)
            if (args.Length >= 2 && args[0] == "--play")
            {
                var game = GameLocator.Find();
                if (game == null || ModInstaller.NeedsInstall(game))
                    MessageBox.Show("Open Anger Foot VR normally first: the game or the mod needs setting up (see VR CHECK).", "Anger Foot VR");
                else
                    try
                    {
                        GameLaunch.Start(game, args[1] != "flat", LauncherSettings.Runtime, null);
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Write("Quick launch failed: " + ex);
                        MessageBox.Show(ex.Message, "Anger Foot VR", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                Shutdown();
                return;
            }

            // one launcher at a time: a second start brings the first one forward
            _single = new System.Threading.Mutex(true, "AngerFootVR.Launcher.SingleInstance", out var first);
            if (!first)
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName))
                    if (p.Id != System.Diagnostics.Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero)
                        NativeMethods.SetForegroundWindow(p.MainWindowHandle);
                Shutdown();
                return;
            }

            Show(new MainWindow());
        }

        private void Show(Window w)
        {
            MainWindow = w;
            w.Show();
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
