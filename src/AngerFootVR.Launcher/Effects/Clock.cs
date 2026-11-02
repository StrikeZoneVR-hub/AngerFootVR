using System;
using System.Diagnostics;

namespace AngerFootVR.Launcher.Effects
{
    /// <summary>
    /// Time for every animation. Normally the wall clock; the --capture mode steps it frame by frame so recorded frames
    /// are evenly spaced however slow the capture is.
    /// </summary>
    public static class Clock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        public static double? Fixed;
        public static double Now => Fixed ?? Watch.Elapsed.TotalSeconds;
    }

    public static class Ease
    {
        public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        /// <summary>0..1 over [start, start+duration].</summary>
        public static double Span(double t, double start, double duration) => Clamp01((t - start) / duration);

        public static double OutCubic(double x)
        {
            x = Clamp01(x);
            return 1 - Math.Pow(1 - x, 3);
        }

        public static double InCubic(double x) => Math.Pow(Clamp01(x), 3);

        public static double InOutSine(double x) => -(Math.Cos(Math.PI * Clamp01(x)) - 1) / 2;

        /// <summary>Overshoots and settles (a slam).</summary>
        public static double OutBack(double x, double s = 2.2)
        {
            x = Clamp01(x) - 1;
            return 1 + x * x * ((s + 1) * x + s);
        }

        /// <summary>A decaying wobble, 1 at x=0 to 0.</summary>
        public static double Shake(double x, double freq) => x >= 1 || x < 0 ? 0 : Math.Sin(x * freq * Math.PI * 2) * Math.Pow(1 - x, 2);
    }
}
