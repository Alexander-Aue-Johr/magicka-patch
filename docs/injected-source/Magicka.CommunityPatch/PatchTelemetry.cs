using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Magicka.CommunityPatch
{
	// Token: 0x0200082E RID: 2094
	internal static class PatchTelemetry
	{
		// Token: 0x06003E8A RID: 16010 RVA: 0x001D5730 File Offset: 0x001D3930
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

		// Token: 0x06003E8B RID: 16011 RVA: 0x001D579C File Offset: 0x001D399C
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

		// Token: 0x06003E8C RID: 16012 RVA: 0x001D57E4 File Offset: 0x001D39E4
		private static void SendBlocking(string eventName, Dictionary<string, string> properties, int timeoutMs)
		{
			try
			{
				if (!PatchTelemetry.IsDisabled())
				{
					try
					{
						ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
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

		// Token: 0x06003E8D RID: 16013 RVA: 0x001D58E0 File Offset: 0x001D3AE0
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

		// Token: 0x06003E8E RID: 16014 RVA: 0x001D5A34 File Offset: 0x001D3C34
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

		// Token: 0x06003E8F RID: 16015 RVA: 0x001D5B3C File Offset: 0x001D3D3C
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

		// Token: 0x06003E90 RID: 16016 RVA: 0x001D5B6C File Offset: 0x001D3D6C
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

		// Token: 0x06003E91 RID: 16017 RVA: 0x0002B0FE File Offset: 0x000292FE
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

		// Token: 0x06003E92 RID: 16018 RVA: 0x001D5C0C File Offset: 0x001D3E0C
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

		// Token: 0x06003E94 RID: 16020 RVA: 0x001D5D10 File Offset: 0x001D3F10
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

		// Token: 0x06003E95 RID: 16021 RVA: 0x0002B130 File Offset: 0x00029330
		private static string GetPatchVersion()
		{
			return CommunityPatchInfo.Version;
		}

		// Token: 0x06003E96 RID: 16022 RVA: 0x001D5D54 File Offset: 0x001D3F54
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

		// Token: 0x06003E97 RID: 16023 RVA: 0x0002B137 File Offset: 0x00029337
		private static string SafeReport(string value)
		{
			if (value == null)
			{
				return "";
			}
			return value;
		}

		// Token: 0x06003E98 RID: 16024 RVA: 0x001D5E54 File Offset: 0x001D4054
		private static string GetLengthString(string value)
		{
			if (value == null)
			{
				return "0";
			}
			return value.Length.ToString();
		}

		// Token: 0x06003E99 RID: 16025 RVA: 0x001D5E78 File Offset: 0x001D4078
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

		// Token: 0x06003E9A RID: 16026 RVA: 0x001D5F30 File Offset: 0x001D4130
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

		// Token: 0x06003E9B RID: 16027 RVA: 0x001D607C File Offset: 0x001D427C
		private static void AddCommonProperties(Dictionary<string, string> properties)
		{
			properties["patch_name"] = CommunityPatchInfo.Name;
			properties["patch_version"] = PatchTelemetry.GetPatchVersion();
			properties["game_version"] = PatchTelemetry.Safe(Application.ProductVersion);
			properties["os"] = PatchTelemetry.Safe(Environment.OSVersion.ToString());
		}

		// Token: 0x06003E9C RID: 16028 RVA: 0x0002B143 File Offset: 0x00029343
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

		// Token: 0x0400470C RID: 18188
		private const string PostHogApiKey = "phc_vbVuHJdtwsf2gzBY36KcLo8btGZY4D6foFGqtxbkfog8";

		// Token: 0x0400470D RID: 18189
		private const string PostHogEndpoint = "https://eu.i.posthog.com/capture/";

		// Token: 0x0400470E RID: 18190
		private const int StartupTimeoutMs = 1200;

		// Token: 0x0400470F RID: 18191
		private const int CrashTimeoutMs = 1800;

		// Token: 0x04004710 RID: 18192
		private static readonly object sLock = new object();

		// Token: 0x04004711 RID: 18193
		private static string sDistinctId;

		// Token: 0x0200082F RID: 2095
		private sealed class TelemetrySendState
		{
			// Token: 0x04004712 RID: 18194
			public string EventName;

			// Token: 0x04004713 RID: 18195
			public Dictionary<string, string> Properties;

			// Token: 0x04004714 RID: 18196
			public int TimeoutMs;
		}
	}
}
