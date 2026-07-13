using System;
using System.Diagnostics;

namespace Magicka.CommunityPatch
{
	internal static class NetworkGuardTelemetryBackoff
	{
		private const int InitialDelayMilliseconds = 1000;

		private const int MaximumDelayMilliseconds = 60000;

		private const long QuietResetMilliseconds = 120000L;

		private static readonly object sLock = new object();

		private static Stopwatch sIgnoredNotReadyTimer;

		private static int sIgnoredNotReadyDelayMilliseconds;

		private static Stopwatch sUnknownHandleTimer;

		private static int sUnknownHandleDelayMilliseconds;

		public static bool ShouldSend(string reason)
		{
			if (string.Equals(reason, "entity_update_ignored_not_ready", StringComparison.Ordinal))
			{
				lock (NetworkGuardTelemetryBackoff.sLock)
				{
					return NetworkGuardTelemetryBackoff.ShouldSend(ref NetworkGuardTelemetryBackoff.sIgnoredNotReadyTimer, ref NetworkGuardTelemetryBackoff.sIgnoredNotReadyDelayMilliseconds);
				}
			}
			if (string.Equals(reason, "entity_update_unknown_handle", StringComparison.Ordinal))
			{
				lock (NetworkGuardTelemetryBackoff.sLock)
				{
					return NetworkGuardTelemetryBackoff.ShouldSend(ref NetworkGuardTelemetryBackoff.sUnknownHandleTimer, ref NetworkGuardTelemetryBackoff.sUnknownHandleDelayMilliseconds);
				}
			}
			return true;
		}

		private static bool ShouldSend(ref Stopwatch timer, ref int delayMilliseconds)
		{
			if (timer == null)
			{
				timer = Stopwatch.StartNew();
				delayMilliseconds = InitialDelayMilliseconds;
				return true;
			}

			long elapsedMilliseconds = timer.ElapsedMilliseconds;
			if (elapsedMilliseconds < (long)delayMilliseconds)
			{
				return false;
			}

			timer.Reset();
			timer.Start();
			if (elapsedMilliseconds >= QuietResetMilliseconds)
			{
				delayMilliseconds = InitialDelayMilliseconds;
			}
			else if (delayMilliseconds <= 0)
			{
				delayMilliseconds = InitialDelayMilliseconds;
			}
			else if (delayMilliseconds >= MaximumDelayMilliseconds / 2)
			{
				delayMilliseconds = MaximumDelayMilliseconds;
			}
			else
			{
				delayMilliseconds *= 2;
			}
			return true;
		}
	}
}
