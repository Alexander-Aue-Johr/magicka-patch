using System;
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

		public static bool ShouldSend(string reason)
		{
			string key = reason ?? string.Empty;
			lock (NetworkGuardTelemetryBackoff.sLock)
			{
				BackoffState backoffState;
				if (!NetworkGuardTelemetryBackoff.sStates.TryGetValue(key, out backoffState))
				{
					NetworkGuardTelemetryBackoff.sStates.Add(key, new BackoffState());
					return true;
				}
				return NetworkGuardTelemetryBackoff.ShouldSend(backoffState);
			}
		}

		private static bool ShouldSend(BackoffState state)
		{
			long elapsedMilliseconds = state.Timer.ElapsedMilliseconds;
			if (elapsedMilliseconds < (long)state.DelayMilliseconds)
			{
				return false;
			}

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
		}
	}
}
