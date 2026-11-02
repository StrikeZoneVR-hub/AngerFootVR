using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using AngerFootVR.Launcher.Game;

namespace AngerFootVR.Launcher.Effects
{
    /// <summary>
    /// The launcher's sounds, synthesised at start-up (all original): a door taking a kick, the door bursting off its
    /// hinges, a neon tube buzzing on, a hover tick, a punchy select and a page whoosh. Written as small WAVs to
    /// %LOCALAPPDATA%\AngerFootVR\sfx. The music is the game's own title track, taken from your copy (see GameInstall.ArtDir).
    /// </summary>
    public static class Sfx
    {
        private const int Rate = 44100;
        private static readonly Dictionary<string, Uri> Files = new Dictionary<string, Uri>();
        private static readonly List<MediaPlayer> Playing = new List<MediaPlayer>();
        private static readonly Random Rng = new Random(11);

        public static bool Muted;

        /// <summary>Capture mode: every sound played, with the time it played at (to mix a recording's audio).</summary>
        public static List<(double T, string Name, double Volume)> Log;

        public static void Prepare()
        {
            var dir = Path.Combine(LauncherPaths.AppData, "sfx");
            Directory.CreateDirectory(dir);
            void Make(string name, float[] samples)
            {
                var path = Path.Combine(dir, name + ".wav");
                WriteWav(path, samples);
                Files[name] = new Uri(path);
            }

            Make("kick", Kick(1.0));
            Make("kick2", Kick(1.25));
            Make("smash", Smash());
            Make("buzz", Buzz());
            Make("tick", Tick());
            Make("hit", Hit());
            Make("whoosh", Whoosh());
        }

        public static string FileOf(string name) => Files.TryGetValue(name, out var u) ? u.LocalPath : null;

        public static void Play(string name, double volume = 1.0)
        {
            Log?.Add((Clock.Now, name, volume));
            if (Muted || !Files.TryGetValue(name, out var uri)) return;
            var p = new MediaPlayer { Volume = volume };
            p.MediaEnded += (s, e) =>
            {
                p.Close();
                Playing.Remove(p);
            };
            p.Open(uri);
            p.Play();
            Playing.Add(p);
        }

        // ---- synthesis ----

        private static float Noise() => (float)(Rng.NextDouble() * 2 - 1);

        /// <summary>A boot into a heavy wooden door: a low boom, the wood's knock and a rattle of the frame.</summary>
        private static float[] Kick(double power)
        {
            var n = (int)(Rate * 0.7);
            var s = new float[n];
            double phase = 0;
            float lp = 0, lp2 = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var f = 48 + 70 * Math.Exp(-t * 28); // the boom drops in pitch
                phase += 2 * Math.PI * f / Rate;
                var boom = Math.Sin(phase) * Math.Exp(-t * 7.5) * 0.9 * power;
                lp += (Noise() - lp) * 0.18f;
                var knock = lp * Math.Exp(-t * 38) * 1.4;
                lp2 += (Noise() - lp2) * 0.05f;
                var rattle = t > 0.03 ? lp2 * Math.Exp(-(t - 0.03) * 9) * 0.9 * Math.Abs(Math.Sin(t * 140)) : 0;
                var wood = Math.Sin(2 * Math.PI * 190 * t) * Math.Exp(-t * 30) * 0.35;
                s[i] = (float)Math.Tanh((boom + knock + rattle + wood) * 1.6);
            }

            return s;
        }

        /// <summary>The door bursting off its hinges: a big boom, splitting wood, splinters landing.</summary>
        private static float[] Smash()
        {
            var n = (int)(Rate * 1.8);
            var s = new float[n];
            double phase = 0;
            float lp = 0, hp = 0, prev = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var f = 38 + 90 * Math.Exp(-t * 18);
                phase += 2 * Math.PI * f / Rate;
                var boom = Math.Sin(phase) * Math.Exp(-t * 3.2) * 1.1;
                lp += (Noise() - lp) * 0.3f;
                var crash = lp * Math.Exp(-t * 6) * 1.2;
                var x = Noise();
                hp = 0.92f * (hp + x - prev);
                prev = x;
                var crackle = hp * Math.Exp(-t * 4) * 0.5 * (Rng.NextDouble() < 0.08 ? 1 : 0.25);
                s[i] = (float)(boom + crash + crackle);
            }

            // splinters landing: little knocks scattered through the tail
            for (var k = 0; k < 26; k++)
            {
                var at = (int)(Rate * (0.25 + Rng.NextDouble() * 1.3));
                var pitch = 900 + Rng.NextDouble() * 2200;
                var amp = 0.25 * (1 - (at / (double)n));
                for (var j = 0; j < Rate * 0.03 && at + j < n; j++)
                {
                    var tt = j / (double)Rate;
                    s[at + j] += (float)(Math.Sin(2 * Math.PI * pitch * tt) * Math.Exp(-tt * 160) * amp + Noise() * Math.Exp(-tt * 300) * amp * 0.6);
                }
            }

            for (var i = 0; i < n; i++) s[i] = (float)Math.Tanh(s[i] * 1.3);
            return s;
        }

        /// <summary>A neon tube flickering on: mains buzz with its sizzle, stuttering, then steady and gone.</summary>
        private static float[] Buzz()
        {
            var n = (int)(Rate * 0.45);
            var s = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var on = t < 0.06 || (t > 0.1 && t < 0.13) || t > 0.18 ? 1.0 : 0.15;
                var env = Math.Min(1, t / 0.005) * Math.Min(1, (0.45 - t) / 0.12);
                var hum = Math.Sin(2 * Math.PI * 120 * t) * 0.5 + Math.Sin(2 * Math.PI * 240 * t) * 0.3 + Math.Sin(2 * Math.PI * 360 * t) * 0.18 + Math.Sin(2 * Math.PI * 600 * t) * 0.08;
                var sizzle = Noise() * 0.22 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 120 * t));
                s[i] = (float)(Math.Tanh((hum + sizzle) * 2.2) * 0.32 * on * env);
            }

            return s;
        }

        private static float[] Tick()
        {
            var n = (int)(Rate * 0.06);
            var s = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                s[i] = (float)((Math.Sin(2 * Math.PI * 1800 * t) * 0.5 + Noise() * 0.3) * Math.Exp(-t * 90) * 0.5);
            }

            return s;
        }

        /// <summary>Select: a short punch with a snap on top.</summary>
        private static float[] Hit()
        {
            var n = (int)(Rate * 0.32);
            var s = new float[n];
            double phase = 0;
            float lp = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var f = 70 + 120 * Math.Exp(-t * 40);
                phase += 2 * Math.PI * f / Rate;
                lp += (Noise() - lp) * 0.5f;
                s[i] = (float)Math.Tanh((Math.Sin(phase) * Math.Exp(-t * 14) + lp * Math.Exp(-t * 60) * 0.8) * 1.5) * 0.8f;
            }

            return s;
        }

        private static float[] Whoosh()
        {
            var n = (int)(Rate * 0.4);
            var s = new float[n];
            float lp = 0;
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / Rate;
                var k = (float)(0.02 + 0.25 * Math.Sin(Math.PI * t / 0.4));
                lp += (Noise() - lp) * k;
                s[i] = (float)(lp * Math.Sin(Math.PI * t / 0.4) * 0.6);
            }

            return s;
        }

        private static void WriteWav(string path, float[] samples)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                var bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(Rate);
                w.Write(Rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(bytes);
                foreach (var f in samples) w.Write((short)(Math.Max(-1, Math.Min(1, f)) * 30000));
            }
        }
    }

    /// <summary>
    /// The launcher's music: Anger Foot's own title track, taken from your copy of the game by the mod the first time it
    /// runs (none ships with the launcher). It fades in after the door goes, loops, and fades out when the game starts.
    /// </summary>
    public sealed class Music
    {
        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly string _file;
        private bool _opened;
        private double _volume, _lastTick = -1;

        public bool Available => _file != null;
        public double Level => _volume;

        public Music(GameInstall game)
        {
            var f = game == null ? null : Path.Combine(game.ArtDir, "title-music.wav");
            _file = f != null && File.Exists(f) ? f : null;
            _player.MediaEnded += (s, e) =>
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
            };
            _player.MediaFailed += (s, e) => LauncherLog.Write("music: couldn't play " + _file + ": " + e.ErrorException?.Message);
        }

        /// <summary>Each frame: towards <paramref name="target"/> volume (0 = silent and stopped).</summary>
        public void Tick(double t, double target)
        {
            var dt = _lastTick < 0 ? 0 : Math.Min(0.1, t - _lastTick);
            _lastTick = t;
            if (_file == null || Sfx.Log != null) return;
            if (target > 0 && !_opened)
            {
                _player.Open(new Uri(_file));
                _player.Volume = 0;
                _player.Play();
                _opened = true;
            }

            if (!_opened) return;
            _volume = target > _volume ? Math.Min(target, _volume + dt * 0.9) : Math.Max(target, _volume - dt * 0.6);
            _player.Volume = _volume;
            if (target <= 0 && _volume <= 0.001)
            {
                _player.Stop();
                _player.Close();
                _opened = false;
            }
        }

        public void Stop()
        {
            try
            {
                _player.Stop();
                _player.Close();
            }
            catch
            {
                // closing
            }
        }
    }
}
