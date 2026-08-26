using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Magicka.CommunityPatch
{
	internal static class NetworkGuardTelemetryBackoff
	{
		private const int InitialDelayMilliseconds = 1000;

		private const int MaximumDelayMilliseconds = 300000;

		private const long QuietResetMilliseconds = 120000L;

		private static readonly object sLock = new object();

		private static readonly Dictionary<string, BackoffState> sStates = new Dictionary<string, BackoffState>(StringComparer.Ordinal);

		private static Hashtable sCountedStates;

		public static bool ShouldSend(string reason)
		{
			int suppressedCount;
			return NetworkGuardTelemetryBackoff.TryBeginSend(reason, string.Empty, out suppressedCount);
		}

		public static bool TryBeginSend(string reason, string similarityKey, out int suppressedCount)
		{
			string key = (reason ?? string.Empty) + "|" + (similarityKey ?? string.Empty);
			lock (NetworkGuardTelemetryBackoff.sLock)
			{
				if (NetworkGuardTelemetryBackoff.sCountedStates == null)
				{
					NetworkGuardTelemetryBackoff.sCountedStates = new Hashtable();
				}
				BackoffState backoffState = NetworkGuardTelemetryBackoff.sCountedStates[key] as BackoffState;
				if (backoffState == null)
				{
					NetworkGuardTelemetryBackoff.sCountedStates[key] = new BackoffState();
					suppressedCount = 0;
					return true;
				}
				return NetworkGuardTelemetryBackoff.TryBeginSend(backoffState, out suppressedCount);
			}
		}

		private static bool TryBeginSend(BackoffState state, out int suppressedCount)
		{
			long elapsedMilliseconds = state.Timer.ElapsedMilliseconds;
			if (elapsedMilliseconds < (long)state.DelayMilliseconds)
			{
				state.SuppressedCount++;
				suppressedCount = 0;
				return false;
			}

			suppressedCount = state.SuppressedCount;
			state.SuppressedCount = 0;
			state.Timer.Reset();
			state.Timer.Start();
			if (elapsedMilliseconds >= QuietResetMilliseconds)
			{
				state.DelayMilliseconds = InitialDelayMilliseconds;
			}
			else if (state.DelayMilliseconds <= 0)
			{
				state.DelayMilliseconds = InitialDelayMilliseconds;
			}
			else if (state.DelayMilliseconds >= MaximumDelayMilliseconds / 2)
			{
				state.DelayMilliseconds = MaximumDelayMilliseconds;
			}
			else
			{
				state.DelayMilliseconds *= 2;
			}
			return true;
		}

		private sealed class BackoffState
		{
			public readonly Stopwatch Timer = Stopwatch.StartNew();

			public int DelayMilliseconds = InitialDelayMilliseconds;

			public int SuppressedCount;
		}
	}
}
