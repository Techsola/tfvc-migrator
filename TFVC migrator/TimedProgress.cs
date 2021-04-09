using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TfvcMigrator
{
    internal sealed class TimedProgress
    {
        private readonly Stopwatch stopwatch;
        private StrongBox<(int Completed, TimeSpan Elapsed)>? state;

        private TimedProgress()
        {
            stopwatch = Stopwatch.StartNew();
        }

        public static TimedProgress Start() => new();

        public void Increment()
        {
            Utils.InterlockedUpdate(ref state, state: stopwatch.Elapsed, (previousState, elapsed) =>
                new StrongBox<(int, TimeSpan)>(((previousState?.Value.Completed ?? 0) + 1, elapsed)));
        }

        private (int Completed, TimeSpan Elapsed)? GetState()
        {
            return Volatile.Read(ref state)?.Value;
        }

        public TimeSpan? GetAverageDuration()
        {
            var state = GetState();
            return state?.Elapsed / state?.Completed;
        }

        public TimeSpan? GetEta(int total)
        {
            var state = GetState();
            return state?.Elapsed * ((total / (double?)state?.Completed) - 1);
        }

        public string? GetFriendlyEta(int total)
        {
            return
                !(GetEta(total) is { } duration) ? null :
                duration.Days != 0 ? Format(duration.TotalDays, 1, "day", "days") :
                duration.Hours != 0 ? Format(duration.TotalHours, 1, "hour", "hours") :
                duration.Minutes != 0 ? Format(duration.TotalMinutes, 0, "min", "min") :
                Format(duration.TotalSeconds, 0, "sec", "sec");

            static string Format(double value, int decimalPlaces, string singularNoun, string pluralNoun)
            {
                var rounded = Math.Round(value, decimalPlaces);
                return rounded + " " + (rounded == 1 ? singularNoun : pluralNoun);
            }
        }

        public double GetPercent(double total)
        {
            var state = GetState();
            return (state?.Completed / total) ?? 0;
        }
    }
}
