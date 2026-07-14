using System;
using System.Diagnostics;

namespace PalliativeAutoPlan
{
    /// <summary>
    /// Small stopwatch helper for timing workflow sections. <see cref="Run"/> wraps a call and logs
    /// how long it took; <see cref="Start"/>/<see cref="Stop"/> bracket a longer span (e.g. a whole
    /// workflow). All lines are prefixed "[time]" so they are easy to grep in the log.
    /// </summary>
    public static class Timing
    {
        /// <summary>Runs <paramref name="work"/>, then logs the elapsed time under <paramref name="label"/>.</summary>
        public static void Run(string label, Action<string> log, Action work)
        {
            var sw = Stopwatch.StartNew();
            try { work(); }
            finally { sw.Stop(); log?.Invoke($"  [time] {label}: {Format(sw.Elapsed)}"); }
        }

        /// <summary>Runs <paramref name="work"/> and returns its result, logging the elapsed time.</summary>
        public static T Run<T>(string label, Action<string> log, Func<T> work)
        {
            var sw = Stopwatch.StartNew();
            try { return work(); }
            finally { sw.Stop(); log?.Invoke($"  [time] {label}: {Format(sw.Elapsed)}"); }
        }

        /// <summary>Starts and returns a running stopwatch (pair with <see cref="Stop"/>).</summary>
        public static Stopwatch Start() => Stopwatch.StartNew();

        /// <summary>Stops <paramref name="sw"/> and logs its elapsed time under <paramref name="label"/>.</summary>
        public static void Stop(Stopwatch sw, string label, Action<string> log)
        {
            sw.Stop();
            log?.Invoke($"  [time] {label}: {Format(sw.Elapsed)}");
        }

        private static string Format(TimeSpan t)
        {
            return t.TotalSeconds >= 60.0 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{t.TotalSeconds:0.0}s";
        }
    }
}
