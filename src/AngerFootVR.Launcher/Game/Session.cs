using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AngerFootVR.Launcher.Game
{
    /// <summary>The work behind PLAY and INSTALL, off the UI thread: set the mod up when it needs it, then start the game.</summary>
    public sealed class Session
    {
        public enum Kind { Idle, Working, Launched, Installed, Failed }

        private volatile string _status = "";
        public volatile Kind State = Kind.Idle;
        public volatile bool Vr;
        public string Status => _status;
        public string Error { get; private set; }
        public Process Game { get; private set; }
        public double Since { get; private set; }

        public bool Busy => State == Kind.Working;

        public void Play(GameInstall g, bool vr, double now)
        {
            if (Busy) return;
            Begin(vr, now);
            Task.Run(() =>
            {
                try
                {
                    if (ModInstaller.NeedsInstall(g)) ModInstaller.Install(g, s => _status = s);
                    Game = GameLaunch.Start(g, vr, LauncherSettings.Runtime, s => _status = s);
                    _status = vr ? "ANGER FOOT IS STARTING IN VR" : "ANGER FOOT IS STARTING";
                    State = Kind.Launched;
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            });
        }

        public void Install(GameInstall g, double now)
        {
            if (Busy) return;
            Begin(true, now);
            Task.Run(() =>
            {
                try
                {
                    ModInstaller.Install(g, s => _status = s);
                    State = Kind.Installed;
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            });
        }

        public void Dismiss()
        {
            if (!Busy) State = Kind.Idle;
        }

        private void Begin(bool vr, double now)
        {
            Vr = vr;
            Error = null;
            Game = null;
            Since = now;
            _status = "GETTING READY...";
            State = Kind.Working;
        }

        private void Fail(Exception e)
        {
            LauncherLog.Write("Failed: " + e);
            Error = e.Message;
            _status = "THAT DIDN'T WORK";
            State = Kind.Failed;
        }
    }
}
