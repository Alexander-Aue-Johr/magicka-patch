using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Magicka.CommunityPatch
{
	internal static class PatchTelemetry
	{
		public static void SendStartup()
		{
			AnimationClipCompatibility.InitializeSession();
			PatchTelemetry.SendAsync("magicka_patch_start", new Dictionary<string, string>
			{
				{
					"patch_name",
					CommunityPatchInfo.Name
				},
				{
					"patch_version",
					PatchTelemetry.GetPatchVersion()
				},
				{
					"game_version",
					PatchTelemetry.Safe(Application.ProductVersion)
				},
				{
					"os",
					PatchTelemetry.Safe(Environment.OSVersion.ToString())
				}
			});
		}

		internal static void SendAsync(string eventName, Dictionary<string, string> properties)
		{
			if (PatchTelemetry.IsDisabled())
			{
				return;
			}
			PatchTelemetry.TelemetrySendState telemetrySendState = new PatchTelemetry.TelemetrySendState();
			telemetrySendState.EventName = eventName;
			telemetrySendState.Properties = properties;
			telemetrySendState.TimeoutMs = 1200;
			ThreadPool.QueueUserWorkItem(new WaitCallback(PatchTelemetry.SendAsyncWorker), telemetrySendState);
		}

		private static void SendBlocking(string eventName, Dictionary<string, string> properties, int timeoutMs)
		{
			try
			{
				if (!PatchTelemetry.IsDisabled())
				{
					try
					{
						ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
					}
					catch
					{
					}
					string s = PatchTelemetry.BuildPostHogJson(eventName, properties);
					byte[] bytes = Encoding.UTF8.GetBytes(s);
					HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://eu.i.posthog.com/capture/");
					httpWebRequest.Method = "POST";
					httpWebRequest.ContentType = "application/json";
					httpWebRequest.UserAgent = CommunityPatchInfo.TelemetryUserAgent;
					httpWebRequest.Timeout = timeoutMs;
					httpWebRequest.ReadWriteTimeout = timeoutMs;
					httpWebRequest.ContentLength = (long)bytes.Length;
					using (Stream requestStream = httpWebRequest.GetRequestStream())
					{
						requestStream.Write(bytes, 0, bytes.Length);
					}
					using ((HttpWebResponse)httpWebRequest.GetResponse())
					{
					}
				}
			}
			catch
			{
			}
		}

		private static string BuildPostHogJson(string eventName, Dictionary<string, string> properties)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("{");
			stringBuilder.Append("\"api_key\":\"").Append(PatchTelemetry.Json("phc_vbVuHJdtwsf2gzBY36KcLo8btGZY4D6foFGqtxbkfog8")).Append("\",");
			stringBuilder.Append("\"event\":\"").Append(PatchTelemetry.Json(eventName)).Append("\",");
			stringBuilder.Append("\"properties\":{");
			stringBuilder.Append("\"distinct_id\":\"").Append(PatchTelemetry.Json(PatchTelemetry.GetDistinctId())).Append("\",");
			stringBuilder.Append("\"$process_person_profile\":false");
			foreach (KeyValuePair<string, string> keyValuePair in properties)
			{
				stringBuilder.Append(",");
				stringBuilder.Append("\"").Append(PatchTelemetry.Json(keyValuePair.Key)).Append("\":");
				PatchTelemetry.CommunityPatchAppendPropertyValue(stringBuilder, keyValuePair.Key, keyValuePair.Value);
			}
			stringBuilder.Append("}");
			stringBuilder.Append("}");
			return stringBuilder.ToString();
		}

		internal static void CommunityPatchRecordKeyboardElementSelection()
		{
			Interlocked.Increment(ref PatchTelemetry.sKeyboardElementSelectionCount);
		}

		internal static void CommunityPatchRecordControllerElementSelection()
		{
			Interlocked.Increment(ref PatchTelemetry.sControllerElementSelectionCount);
		}

		internal static void CommunityPatchAddElementSelectionProperties(Dictionary<string, string> properties)
		{
			long keyboard = Interlocked.CompareExchange(ref PatchTelemetry.sKeyboardElementSelectionCount, 0L, 0L);
			long controller = Interlocked.CompareExchange(ref PatchTelemetry.sControllerElementSelectionCount, 0L, 0L);
			double total = (double)keyboard + (double)controller;
			double controllerRatio = total > 0.0 ? (double)controller / total : 0.0;
			properties["keyboard_element_selection_count"] = keyboard.ToString(CultureInfo.InvariantCulture);
			properties["controller_element_selection_count"] = controller.ToString(CultureInfo.InvariantCulture);
			properties["controller_element_selection_ratio"] = controllerRatio.ToString("R", CultureInfo.InvariantCulture);
		}

		private static void CommunityPatchAppendPropertyValue(StringBuilder builder, string key, string value)
		{
			double numericValue;
			bool numericSessionProperty = key == "keyboard_element_selection_count" ||
				key == "controller_element_selection_count" ||
				key == "controller_element_selection_ratio";
			if (numericSessionProperty &&
				double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue) &&
				!double.IsNaN(numericValue) && !double.IsInfinity(numericValue))
			{
				builder.Append(value);
				return;
			}
			builder.Append("\"").Append(PatchTelemetry.Json(value)).Append("\"");
		}

		private static string GetDistinctId()
		{
			object obj = PatchTelemetry.sLock;
			string result;
			lock (obj)
			{
				if (!string.IsNullOrEmpty(PatchTelemetry.sDistinctId))
				{
					result = PatchTelemetry.sDistinctId;
				}
				else
				{
					string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MagickaPatch");
					string path = Path.Combine(text, "telemetry_id.txt");
					try
					{
						if (File.Exists(path))
						{
							PatchTelemetry.sDistinctId = File.ReadAllText(path).Trim();
							if (!string.IsNullOrEmpty(PatchTelemetry.sDistinctId))
							{
								return PatchTelemetry.sDistinctId;
							}
						}
						if (!Directory.Exists(text))
						{
							Directory.CreateDirectory(text);
						}
						PatchTelemetry.sDistinctId = Guid.NewGuid().ToString("N");
						File.WriteAllText(path, PatchTelemetry.sDistinctId);
						result = PatchTelemetry.sDistinctId;
					}
					catch
					{
						PatchTelemetry.sDistinctId = "ephemeral_" + Guid.NewGuid().ToString("N");
						result = PatchTelemetry.sDistinctId;
					}
				}
			}
			return result;
		}

		private static bool IsDisabled()
		{
			try
			{
				if (!File.Exists(PatchSettings.SettingsPath))
				{
					return false;
				}
				return !PatchSettings.Load().UsageSharing;
			}
			catch
			{
				return false;
			}
		}

		private static bool AreCrashReportsDisabled()
		{
			try
			{
				if (PatchTelemetry.IsDisabled())
				{
					return true;
				}
				if (!File.Exists(PatchSettings.SettingsPath))
				{
					return false;
				}
				return !PatchSettings.Load().CrashReports;
			}
			catch
			{
				return false;
			}
		}

		private static string HashShort(string value)
		{
			string result;
			try
			{
				using (SHA256 sha = SHA256.Create())
				{
					byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
					byte[] array = sha.ComputeHash(bytes);
					StringBuilder stringBuilder = new StringBuilder();
					for (int i = 0; i < 6; i++)
					{
						stringBuilder.Append(array[i].ToString("x2"));
					}
					result = stringBuilder.ToString();
				}
			}
			catch
			{
				result = "hash_failed";
			}
			return result;
		}

		private static string Safe(string value)
		{
			if (value == null)
			{
				return "";
			}
			if (value.Length > 200)
			{
				return value.Substring(0, 200);
			}
			return value;
		}

		private static string Json(string value)
		{
			if (value == null)
			{
				return "";
			}
			StringBuilder stringBuilder = new StringBuilder();
			int i = 0;
			while (i < value.Length)
			{
				char c = value[i];
				switch (c)
				{
				case '\b':
					stringBuilder.Append("\\b");
					break;
				case '\t':
					stringBuilder.Append("\\t");
					break;
				case '\n':
					stringBuilder.Append("\\n");
					break;
				case '\v':
					goto IL_B0;
				case '\f':
					stringBuilder.Append("\\f");
					break;
				case '\r':
					stringBuilder.Append("\\r");
					break;
				default:
					if (c != '"')
					{
						if (c != '\\')
						{
							goto IL_B0;
						}
						stringBuilder.Append("\\\\");
					}
					else
					{
						stringBuilder.Append("\\\"");
					}
					break;
				}
				IL_AA:
				i++;
				continue;
				IL_B0:
				if (c < ' ')
				{
					stringBuilder.Append("\\u");
					StringBuilder stringBuilder2 = stringBuilder;
					int num = (int)c;
					stringBuilder2.Append(num.ToString("x4"));
					goto IL_AA;
				}
				stringBuilder.Append(c);
				goto IL_AA;
			}
			return stringBuilder.ToString();
		}

		private static void SendAsyncWorker(object stateObject)
		{
			try
			{
				PatchTelemetry.TelemetrySendState telemetrySendState = stateObject as PatchTelemetry.TelemetrySendState;
				if (telemetrySendState != null)
				{
					PatchTelemetry.SendBlocking(telemetrySendState.EventName, telemetrySendState.Properties, telemetrySendState.TimeoutMs);
				}
			}
			catch
			{
			}
		}

		private static string GetPatchVersion()
		{
			return CommunityPatchInfo.Version;
		}

		public static void SendCrash(Exception exception, string threadName, string crashReport)
		{
			if (PatchTelemetry.AreCrashReportsDisabled())
			{
				return;
			}
			string value = (exception != null) ? exception.GetType().Name : "UnknownException";
			string value2 = (exception != null) ? PatchTelemetry.HashShort(exception.ToString()) : "unknown";
			Dictionary<string, string> properties = new Dictionary<string, string>
			{
				{
					"patch_name",
					CommunityPatchInfo.Name
				},
				{
					"patch_version",
					PatchTelemetry.GetPatchVersion()
				},
				{
					"game_version",
					PatchTelemetry.Safe(Application.ProductVersion)
				},
				{
					"os",
					PatchTelemetry.Safe(Environment.OSVersion.ToString())
				},
				{
					"exception_type",
					PatchTelemetry.Safe(value)
				},
				{
					"exception_hash",
					PatchTelemetry.Safe(value2)
				},
				{
					"thread",
					PatchTelemetry.Safe(string.IsNullOrEmpty(threadName) ? "UnNamed" : threadName)
				},
				{
					"crash_report",
					PatchTelemetry.SafeReport(crashReport)
				},
				{
					"crash_report_length",
					PatchTelemetry.GetLengthString(crashReport)
				}
			};
			PatchTelemetry.CommunityPatchAddElementSelectionProperties(properties);
			PatchTelemetry.SendBlocking("magicka_patch_crash_report_written", properties, 1800);
		}

		private static string SafeReport(string value)
		{
			if (value == null)
			{
				return "";
			}
			return value;
		}

		private static string GetLengthString(string value)
		{
			if (value == null)
			{
				return "0";
			}
			return value.Length.ToString();
		}

		public static void SendNetworkGuardDrop(string side, string packetType, string senderSteamId, string senderName, string reason, string details)
		{
			try
			{
				if (!NetworkGuardTelemetryBackoff.ShouldSend(reason))
				{
					return;
				}
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["side"] = PatchTelemetry.Safe(side);
				dictionary["packet_type"] = PatchTelemetry.Safe(packetType);
				dictionary["sender_steam_id"] = PatchTelemetry.Safe(senderSteamId);
				dictionary["sender_name"] = PatchTelemetry.Safe(senderName);
				dictionary["reason"] = PatchTelemetry.Safe(reason);
				dictionary["details"] = PatchTelemetry.SafeLong(details);
				dictionary["details_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(details));
				PatchTelemetry.SendAsync("magicka_patch_network_guard_drop", dictionary);
			}
			catch
			{
			}
		}

		public static void SendNetworkGuardException(string side, string packetType, string senderSteamId, string senderName, string reason, string details, Exception exception)
		{
			try
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["side"] = PatchTelemetry.Safe(side);
				dictionary["packet_type"] = PatchTelemetry.Safe(packetType);
				dictionary["sender_steam_id"] = PatchTelemetry.Safe(senderSteamId);
				dictionary["sender_name"] = PatchTelemetry.Safe(senderName);
				dictionary["reason"] = PatchTelemetry.Safe(reason);
				dictionary["details"] = PatchTelemetry.SafeLong(details);
				dictionary["details_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(details));
				if (exception != null)
				{
					dictionary["exception_type"] = PatchTelemetry.Safe(exception.GetType().FullName);
					dictionary["exception_message"] = PatchTelemetry.Safe(exception.Message);
					dictionary["exception_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(exception.ToString()));
				}
				else
				{
					dictionary["exception_type"] = "";
					dictionary["exception_message"] = "";
					dictionary["exception_hash"] = "unknown";
				}
				PatchTelemetry.SendAsync("magicka_patch_network_guard_exception", dictionary);
			}
			catch
			{
			}
		}

		public static void SendRuntimeGuard(string eventName, string guard, string collection, string objectType, string details, string assetName)
		{
			try
			{
				PatchTelemetry.SendRuntimeGuardCore(eventName, guard, collection, objectType, details, assetName);
			}
			catch
			{
			}
		}

		private static void SendRuntimeGuardCore(string eventName, string guard, string collection, string objectType, string details, string assetName)
		{
			if (!NetworkGuardTelemetryBackoff.ShouldSend(guard))
			{
				return;
			}
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			PatchTelemetry.AddCommonProperties(dictionary);
			dictionary["guard"] = PatchTelemetry.Safe(guard);
			dictionary["collection"] = PatchTelemetry.Safe(collection);
			dictionary["object_type"] = PatchTelemetry.Safe(objectType);
			dictionary["details"] = PatchTelemetry.SafeLong(details);
			if (!string.IsNullOrEmpty(assetName))
			{
				dictionary["asset_name"] = PatchTelemetry.Safe(assetName);
			}
			PatchTelemetry.SendAsync(eventName, dictionary);
		}

		internal static void AddCommonProperties(Dictionary<string, string> properties)
		{
			properties["patch_name"] = CommunityPatchInfo.Name;
			properties["patch_version"] = PatchTelemetry.GetPatchVersion();
			properties["game_version"] = PatchTelemetry.Safe(Application.ProductVersion);
			properties["os"] = PatchTelemetry.Safe(Environment.OSVersion.ToString());
		}

		private static string SafeLong(string value)
		{
			if (value == null)
			{
				return "";
			}
			if (value.Length > 1000)
			{
				return value.Substring(0, 1000);
			}
			return value;
		}

		public static void SendGameClosedNormally()
		{
			Dictionary<string, string> properties = new Dictionary<string, string>
			{
				{
					"patch_name",
					CommunityPatchInfo.Name
				},
				{
					"patch_version",
					PatchTelemetry.GetPatchVersion()
				},
				{
					"game_version",
					PatchTelemetry.Safe(Application.ProductVersion)
				},
				{
					"os",
					PatchTelemetry.Safe(Environment.OSVersion.ToString())
				}
			};
			PatchTelemetry.CommunityPatchAddElementSelectionProperties(properties);
			PatchTelemetry.SendBlocking("magicka_patch_game_closed_normally", properties, 1500);
		}

		public static void SendTypingTextGuardException(string reason, char[] text, int charIndex, int visibleCharacters, int primitiveCount, float nextChar, float typeSpeed, Exception exception)
		{
			try
			{
				string text2 = (text != null) ? new string(text) : "";
				int num = charIndex - 80;
				if (num < 0)
				{
					num = 0;
				}
				if (num > text2.Length)
				{
					num = text2.Length;
				}
				int num2 = text2.Length - num;
				if (num2 > 160)
				{
					num2 = 160;
				}
				string value = "";
				if (num2 > 0)
				{
					value = text2.Substring(num, num2);
				}
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["reason"] = PatchTelemetry.Safe(reason);
				dictionary["text_length"] = text2.Length.ToString();
				dictionary["text_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(text2));
				dictionary["char_index"] = charIndex.ToString();
				dictionary["visible_characters"] = visibleCharacters.ToString();
				dictionary["primitive_count"] = primitiveCount.ToString();
				dictionary["expected_visible_characters"] = (primitiveCount / 2).ToString();
				dictionary["next_char"] = nextChar.ToString(CultureInfo.InvariantCulture);
				dictionary["type_speed"] = typeSpeed.ToString(CultureInfo.InvariantCulture);
				dictionary["text_context_start"] = num.ToString();
				dictionary["text_context"] = PatchTelemetry.SafeLong(value);
				if (exception != null)
				{
					dictionary["exception_type"] = PatchTelemetry.Safe(exception.GetType().FullName);
					dictionary["exception_message"] = PatchTelemetry.Safe(exception.Message);
					dictionary["exception_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(exception.ToString()));
				}
				else
				{
					dictionary["exception_type"] = "";
					dictionary["exception_message"] = "";
					dictionary["exception_hash"] = "unknown";
				}
				PatchTelemetry.SendAsync("magicka_patch_typing_text_guard_exception", dictionary);
			}
			catch
			{
			}
		}

		public static void SendNetworkPlayStateWaitDelayed(int networkPlayerCount, int playersInPlayState, long waitedMilliseconds)
		{
			try
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["network_player_count"] = networkPlayerCount.ToString(CultureInfo.InvariantCulture);
				dictionary["players_in_play_state"] = playersInPlayState.ToString(CultureInfo.InvariantCulture);
				dictionary["waited_ms"] = waitedMilliseconds.ToString(CultureInfo.InvariantCulture);
				PatchTelemetry.SendAsync("magicka_patch_network_playstate_wait_delayed", dictionary);
			}
			catch
			{
			}
		}

		public static void SendNetworkPlayStateWaitCompleted(int networkPlayerCount, long waitedMilliseconds, int waitIterations, bool delayedEventSent)
		{
			try
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["network_player_count"] = networkPlayerCount.ToString(CultureInfo.InvariantCulture);
				dictionary["waited_ms"] = waitedMilliseconds.ToString(CultureInfo.InvariantCulture);
				dictionary["wait_iterations"] = waitIterations.ToString(CultureInfo.InvariantCulture);
				dictionary["delayed_event_sent"] = delayedEventSent.ToString().ToLowerInvariant();
				PatchTelemetry.SendAsync("magicka_patch_network_playstate_wait_completed", dictionary);
			}
			catch
			{
			}
		}

		public static void SendNetworkAvatarDisposeException(Exception exception, string currentPlayStateType)
		{
			try
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["current_play_state_type"] = PatchTelemetry.Safe(currentPlayStateType);
				if (exception != null)
				{
					dictionary["exception_type"] = PatchTelemetry.Safe(exception.GetType().FullName);
					dictionary["exception_message"] = PatchTelemetry.Safe(exception.Message);
					dictionary["exception_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(exception.ToString()));
				}
				else
				{
					dictionary["exception_type"] = "";
					dictionary["exception_message"] = "";
					dictionary["exception_hash"] = "unknown";
				}
				PatchTelemetry.SendAsync("magicka_patch_network_avatar_dispose_exception", dictionary);
			}
			catch
			{
			}
		}

		private const string PostHogApiKey = "phc_vbVuHJdtwsf2gzBY36KcLo8btGZY4D6foFGqtxbkfog8";

		private const string PostHogEndpoint = "https://eu.i.posthog.com/capture/";

		private const int StartupTimeoutMs = 1200;

		private const int CrashTimeoutMs = 1800;

		private static readonly object sLock = new object();

		private static string sDistinctId;

		private static long sKeyboardElementSelectionCount;

		private static long sControllerElementSelectionCount;

		private sealed class TelemetrySendState
		{
			public string EventName;

			public Dictionary<string, string> Properties;

			public int TimeoutMs;
		}
	}
}
