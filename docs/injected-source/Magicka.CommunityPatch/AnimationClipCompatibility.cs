using System;
using System.Collections.Generic;
using System.Globalization;

namespace Magicka.CommunityPatch
{
	internal static class AnimationClipCompatibility
	{
		internal const int MaxMissingClipTelemetryEventsPerSession = 16;

		private static readonly object sMissingClipTelemetryLock = new object();

		private static readonly HashSet<string> sReportedMissingClipCombinations =
			new HashSet<string>(StringComparer.Ordinal);

		public static void InitializeSession()
		{
			lock (AnimationClipCompatibility.sMissingClipTelemetryLock)
			{
				// Accessing the static fields creates this process-session collection.
			}
		}

		public static object TryGetAnimationAction(Array animationSets, int animationSet, int animation)
		{
			if (animationSets == null || animationSet < 0 || animationSet >= animationSets.Length)
			{
				return null;
			}

			Array array = animationSets.GetValue(animationSet) as Array;
			if (array == null || animation < 0 || animation >= array.Length)
			{
				return null;
			}

			return array.GetValue(animation);
		}

		public static void ReportMissingClip(
			string assetName,
			string clipKey,
			string animationName,
			int animationValue,
			int availableClipCount)
		{
			try
			{
				string text = assetName ?? string.Empty;
				string text2 = clipKey ?? string.Empty;
				string text3 = animationName ?? string.Empty;
				string item = text + "\u001f" + text2 + "\u001f" + text3 + "\u001f" +
					animationValue.ToString(CultureInfo.InvariantCulture);
				int count;

				lock (AnimationClipCompatibility.sMissingClipTelemetryLock)
				{
					if (AnimationClipCompatibility.sReportedMissingClipCombinations.Count >=
						AnimationClipCompatibility.MaxMissingClipTelemetryEventsPerSession ||
						!AnimationClipCompatibility.sReportedMissingClipCombinations.Add(item))
					{
						return;
					}

					count = AnimationClipCompatibility.sReportedMissingClipCombinations.Count;
				}

				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["asset_name"] = AnimationClipCompatibility.Safe(text);
				dictionary["clip_key"] = AnimationClipCompatibility.Safe(text2);
				dictionary["animation_name"] = AnimationClipCompatibility.Safe(text3);
				dictionary["animation_value"] = animationValue.ToString(CultureInfo.InvariantCulture);
				dictionary["available_clip_count"] = availableClipCount.ToString(CultureInfo.InvariantCulture);
				dictionary["session_unique_index"] = count.ToString(CultureInfo.InvariantCulture);
				dictionary["session_event_limit"] =
					AnimationClipCompatibility.MaxMissingClipTelemetryEventsPerSession.ToString(CultureInfo.InvariantCulture);
				dictionary["dedupe_scope"] = "process_session";
				PatchTelemetry.SendAsync("magicka_patch_animation_clip_missing", dictionary);
			}
			catch
			{
			}
		}

		private static string Safe(string value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			if (value.Length > 200)
			{
				return value.Substring(0, 200);
			}
			return value;
		}
	}
}
