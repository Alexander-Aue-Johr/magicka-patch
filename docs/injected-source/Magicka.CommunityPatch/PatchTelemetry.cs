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

		private static void SendAsync(string eventName, Dictionary<string, string> properties)
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
				stringBuilder.Append("\"").Append(PatchTelemetry.Json(keyValuePair.Value)).Append("\"");
			}
			stringBuilder.Append("}");
			stringBuilder.Append("}");
			return stringBuilder.ToString();
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
			bool result;
			try
			{
				result = File.Exists("telemetry_disabled.txt");
			}
			catch
			{
				result = true;
			}
			return result;
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
			string value = (exception != null) ? exception.GetType().Name : "UnknownException";
			string value2 = (exception != null) ? PatchTelemetry.HashShort(exception.ToString()) : "unknown";
			PatchTelemetry.SendBlocking("magicka_patch_crash_report_written", new Dictionary<string, string>
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
			}, 1800);
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

		public static void SendTypingTextGuardException(string reason, string text, int charIndex, int visibleCharacters, int primitiveCount, float nextChar, float typeSpeed, Exception exception)
		{
			try
			{
				Dictionary<string, string> dictionary = new Dictionary<string, string>();
				PatchTelemetry.AddCommonProperties(dictionary);
				dictionary["reason"] = PatchTelemetry.Safe(reason);
				dictionary["text_length"] = (text == null) ? "null" : text.Length.ToString(CultureInfo.InvariantCulture);
				dictionary["text_hash"] = PatchTelemetry.Safe(PatchTelemetry.HashShort(text));
				dictionary["char_index"] = charIndex.ToString(CultureInfo.InvariantCulture);
				dictionary["visible_characters"] = visibleCharacters.ToString(CultureInfo.InvariantCulture);
				dictionary["primitive_count"] = primitiveCount.ToString(CultureInfo.InvariantCulture);
				dictionary["next_char"] = nextChar.ToString(CultureInfo.InvariantCulture);
				dictionary["type_speed"] = typeSpeed.ToString(CultureInfo.InvariantCulture);
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

		private static void AddCommonProperties(Dictionary<string, string> properties)
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
			PatchTelemetry.SendBlocking("magicka_patch_game_closed_normally", new Dictionary<string, string>
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
			}, 1500);
		}

		private const string PostHogApiKey = "phc_vbVuHJdtwsf2gzBY36KcLo8btGZY4D6foFGqtxbkfog8";

		private const string PostHogEndpoint = "https://eu.i.posthog.com/capture/";

		private const int StartupTimeoutMs = 1200;

		private const int CrashTimeoutMs = 1800;

		private static readonly object sLock = new object();

		private static string sDistinctId;

		private sealed class TelemetrySendState
		{
			public string EventName;

			public Dictionary<string, string> Properties;

			public int TimeoutMs;
		}
	}
}
