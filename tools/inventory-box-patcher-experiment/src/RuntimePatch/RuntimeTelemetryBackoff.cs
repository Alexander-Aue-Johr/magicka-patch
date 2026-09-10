using System;
using System.Collections;
using System.Diagnostics;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimeTelemetryBackoff
    {
        private const int InitialDelayMilliseconds = 1000;
        private const int MaximumDelayMilliseconds = 300000;
        private const long QuietResetMilliseconds = 120000L;
        private const int MaximumTrackedCategories = 128;
        private const int MaximumKeyPartLength = 96;
        private const string OverflowKey = "__overflow__";

        private static readonly object Sync = new object();
        private static readonly Hashtable States = new Hashtable();

        public static bool ShouldSend(string reason)
        {
            int suppressedCount;
            return TryBeginSend(reason, String.Empty, out suppressedCount);
        }

        public static bool TryBeginSend(
            string reason,
            string similarityKey,
            out int suppressedCount)
        {
            suppressedCount = 0;
            try
            {
                string key = Normalize(reason) + "|" +
                    Normalize(similarityKey);
                lock (Sync)
                {
                    BackoffState state = States[key] as BackoffState;
                    if (state == null)
                    {
                        if (States.Count >= MaximumTrackedCategories - 1)
                        {
                            key = OverflowKey;
                            state = States[key] as BackoffState;
                        }
                        if (state == null)
                        {
                            States[key] = new BackoffState();
                            return true;
                        }
                    }
                    return TryBeginSend(state, out suppressedCount);
                }
            }
            catch
            {
                suppressedCount = 0;
                return false;
            }
        }

        internal static int TrackedCategoryCount
        {
            get
            {
                lock (Sync)
                {
                    return States.Count;
                }
            }
        }

        internal static void ResetForValidation()
        {
            lock (Sync)
            {
                States.Clear();
            }
        }

        private static bool TryBeginSend(
            BackoffState state,
            out int suppressedCount)
        {
            long elapsed = state.Timer.ElapsedMilliseconds;
            if (elapsed < state.DelayMilliseconds)
            {
                state.SuppressedCount++;
                suppressedCount = 0;
                return false;
            }

            suppressedCount = state.SuppressedCount;
            state.SuppressedCount = 0;
            state.Timer.Reset();
            state.Timer.Start();
            if (elapsed >= QuietResetMilliseconds)
                state.DelayMilliseconds = InitialDelayMilliseconds;
            else if (state.DelayMilliseconds >=
                MaximumDelayMilliseconds / 2)
                state.DelayMilliseconds = MaximumDelayMilliseconds;
            else
                state.DelayMilliseconds *= 2;
            return true;
        }

        private static string Normalize(string value)
        {
            if (String.IsNullOrEmpty(value))
                return String.Empty;
            if (value.Length <= MaximumKeyPartLength)
                return value;
            return value.Substring(0, MaximumKeyPartLength);
        }

        private sealed class BackoffState
        {
            internal readonly Stopwatch Timer = Stopwatch.StartNew();
            internal int DelayMilliseconds = InitialDelayMilliseconds;
            internal int SuppressedCount;
        }
    }
}
