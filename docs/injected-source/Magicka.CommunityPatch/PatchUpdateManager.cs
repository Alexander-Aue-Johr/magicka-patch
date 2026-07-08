using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Magicka.CommunityPatch
{
	// Token: 0x02000833 RID: 2099
	internal static class PatchUpdateManager
	{
		// Token: 0x06003EAD RID: 16045 RVA: 0x001D6490 File Offset: 0x001D4690
		public static void CheckForUpdatesInBackground()
		{
			try
			{
				if (PatchSettings.Load().AutoUpdate)
				{
					if (File.Exists(PatchSettings.ToolPath))
					{
						if (Interlocked.Exchange(ref PatchUpdateManager.sCheckStarted, 1) == 0)
						{
							ThreadPool.QueueUserWorkItem(delegate(object <p0>)
							{
								PatchUpdateManager.CheckWorker();
							});
						}
					}
				}
			}
			catch
			{
			}
		}

		// Token: 0x06003EAE RID: 16046 RVA: 0x001D6504 File Offset: 0x001D4704
		public static void OfferPendingUpdateAfterGameExit()
		{
			try
			{
				PatchUpdateManager.PendingUpdate pendingUpdate = PatchUpdateManager.PendingUpdate.Load();
				if (pendingUpdate.IsValid)
				{
					if (File.Exists(PatchSettings.ToolPath))
					{
						DialogResult dialogResult = MessageBox.Show(string.Concat(new string[]
						{
							"A new Magicka Community Patch version is ready: ",
							pendingUpdate.Version,
							Environment.NewLine,
							Environment.NewLine,
							"Yes = install now",
							Environment.NewLine,
							"No = later",
							Environment.NewLine,
							"Cancel = skip this version"
						}), CommunityPatchInfo.DisplayName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
						if (dialogResult == DialogResult.Yes)
						{
							PatchUpdateManager.StartTool("--apply-update", pendingUpdate.Source, pendingUpdate.Version);
						}
						else if (dialogResult == DialogResult.Cancel)
						{
							PatchSettings.SaveSkippedVersion(pendingUpdate.Version);
							PatchUpdateManager.TryDelete(PatchSettings.PendingUpdatePath);
						}
					}
				}
			}
			catch
			{
			}
		}

		// Token: 0x06003EAF RID: 16047 RVA: 0x0002B24B File Offset: 0x0002944B
		private static bool IsNullOrWhiteSpaceCompat(string value)
		{
			return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
		}

		// Token: 0x06003EB0 RID: 16048 RVA: 0x001D65E4 File Offset: 0x001D47E4
		public static void OfferPendingUpdateAfterCrash()
		{
			try
			{
				PatchUpdateManager.PendingUpdate pendingUpdate = PatchUpdateManager.PendingUpdate.Load();
				if (pendingUpdate.IsValid)
				{
					if (File.Exists(PatchSettings.ToolPath))
					{
						PatchUpdateManager.StartTool("--offer-pending-update", pendingUpdate.Source, pendingUpdate.Version);
					}
				}
			}
			catch
			{
			}
		}

		// Token: 0x06003EB1 RID: 16049 RVA: 0x001D663C File Offset: 0x001D483C
		private static void CheckWorker()
		{
			try
			{
				PatchSettings patchSettings = PatchSettings.Load();
				if (patchSettings.AutoUpdate)
				{
					string text = PatchUpdateManager.DownloadString(CommunityPatchInfo.LatestReleaseApiUrl, 4500);
					string text2 = PatchUpdateManager.ExtractJsonString(text, "tag_name");
					if (!PatchUpdateManager.IsNullOrWhiteSpaceCompat(text2))
					{
						text2 = PatchUpdateManager.NormalizeVersion(text2);
						if (PatchUpdateManager.IsNewerVersion(text2, CommunityPatchInfo.Version))
						{
							if (PatchUpdateManager.IsNullOrWhiteSpaceCompat(patchSettings.SkippedVersion) || !PatchUpdateManager.NormalizeVersion(patchSettings.SkippedVersion).Equals(text2, StringComparison.OrdinalIgnoreCase))
							{
								Directory.CreateDirectory(PatchSettings.DownloadDirectory);
								string text3 = PatchUpdateManager.DownloadBestAsset(text, text2);
								if (!PatchUpdateManager.IsNullOrWhiteSpaceCompat(text3))
								{
									new PatchUpdateManager.PendingUpdate
									{
										Version = text2,
										Source = text3
									}.Save();
								}
							}
						}
					}
				}
			}
			catch
			{
			}
		}

		// Token: 0x06003EB2 RID: 16050 RVA: 0x001D6708 File Offset: 0x001D4908
		private static string DownloadBestAsset(string releaseJson, string latestVersion)
		{
			string text = Path.Combine(PatchSettings.DownloadDirectory, PatchUpdateManager.SafeFileName(latestVersion));
			Directory.CreateDirectory(text);
			string text2 = PatchUpdateManager.FindAssetUrl(releaseJson, "\\.zip$");
			if (!string.IsNullOrEmpty(text2))
			{
				string text3 = Path.Combine(text, "magicka-community-patch-" + PatchUpdateManager.SafeFileName(latestVersion) + ".zip");
				if (!File.Exists(text3) || new FileInfo(text3).Length == 0L)
				{
					PatchUpdateManager.DownloadFile(text2, text3, 20000);
				}
				if (File.Exists(text3) && new FileInfo(text3).Length > 0L)
				{
					return text3;
				}
			}
			string text4 = PatchUpdateManager.FindAssetUrl(releaseJson, "Magicka\\.exe$");
			string text5 = PatchUpdateManager.FindAssetUrl(releaseJson, "PolygonHead\\.dll$");
			if (!string.IsNullOrEmpty(text4) && !string.IsNullOrEmpty(text5))
			{
				string text6 = Path.Combine(text, "Magicka.exe");
				string text7 = Path.Combine(text, "PolygonHead.dll");
				if (!File.Exists(text6) || new FileInfo(text6).Length == 0L)
				{
					PatchUpdateManager.DownloadFile(text4, text6, 20000);
				}
				if (!File.Exists(text7) || new FileInfo(text7).Length == 0L)
				{
					PatchUpdateManager.DownloadFile(text5, text7, 20000);
				}
				if (File.Exists(text6) && File.Exists(text7))
				{
					return text;
				}
			}
			return string.Empty;
		}

		// Token: 0x06003EB3 RID: 16051 RVA: 0x001D6844 File Offset: 0x001D4A44
		private static string DownloadString(string url, int timeoutMs)
		{
			string result;
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)PatchUpdateManager.CreateRequest(url, timeoutMs).GetResponse())
			{
				using (Stream responseStream = httpWebResponse.GetResponseStream())
				{
					using (StreamReader streamReader = new StreamReader(responseStream))
					{
						result = streamReader.ReadToEnd();
					}
				}
			}
			return result;
		}

		// Token: 0x06003EB4 RID: 16052 RVA: 0x001D68C4 File Offset: 0x001D4AC4
		private static void DownloadFile(string url, string path, int timeoutMs)
		{
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)PatchUpdateManager.CreateRequest(url, timeoutMs).GetResponse())
			{
				using (Stream responseStream = httpWebResponse.GetResponseStream())
				{
					using (FileStream fileStream = File.Create(path))
					{
						byte[] array = new byte[32768];
						int count;
						while ((count = responseStream.Read(array, 0, array.Length)) > 0)
						{
							fileStream.Write(array, 0, count);
						}
					}
				}
			}
		}

		// Token: 0x06003EB5 RID: 16053 RVA: 0x001D6964 File Offset: 0x001D4B64
		private static HttpWebRequest CreateRequest(string url, int timeoutMs)
		{
			try
			{
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			}
			catch
			{
			}
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
			httpWebRequest.UserAgent = CommunityPatchInfo.TelemetryUserAgent;
			httpWebRequest.Timeout = timeoutMs;
			httpWebRequest.ReadWriteTimeout = timeoutMs;
			return httpWebRequest;
		}

		// Token: 0x06003EB6 RID: 16054 RVA: 0x001D69BC File Offset: 0x001D4BBC
		private static string ExtractJsonString(string json, string key)
		{
			if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
			{
				return string.Empty;
			}
			Match match = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"(?<v>(?:\\\\.|[^\\\"])*)\\\"", 1);
			if (!match.Success)
			{
				return string.Empty;
			}
			return PatchUpdateManager.UnescapeJson(match.Groups["v"].Value);
		}

		// Token: 0x06003EB7 RID: 16055 RVA: 0x001D6A24 File Offset: 0x001D4C24
		private static string FindAssetUrl(string json, string nameRegex)
		{
			if (string.IsNullOrEmpty(json))
			{
				return string.Empty;
			}
			foreach (object obj in new Regex("\"name\"\\s*:\\s*\"(?<name>(?:\\\\.|[^\"])*)\".{0,5000}?\"browser_download_url\"\\s*:\\s*\"(?<url>(?:\\\\.|[^\"])*)\"", 17).Matches(json))
			{
				Match match = (Match)obj;
				if (Regex.IsMatch(PatchUpdateManager.UnescapeJson(match.Groups["name"].Value), nameRegex, 1))
				{
					return PatchUpdateManager.UnescapeJson(match.Groups["url"].Value);
				}
			}
			return string.Empty;
		}

		// Token: 0x06003EB8 RID: 16056 RVA: 0x0002B265 File Offset: 0x00029465
		private static string UnescapeJson(string value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			return value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
		}

		// Token: 0x06003EB9 RID: 16057 RVA: 0x001D6AD8 File Offset: 0x001D4CD8
		private static bool IsNewerVersion(string candidate, string current)
		{
			Version v;
			Version v2;
			if (PatchUpdateManager.TryParseVersion(candidate, out v) && PatchUpdateManager.TryParseVersion(current, out v2))
			{
				return v > v2;
			}
			return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
		}

		// Token: 0x06003EBA RID: 16058 RVA: 0x001D6B0C File Offset: 0x001D4D0C
		private static bool TryParseVersion(string value, out Version version)
		{
			version = null;
			value = PatchUpdateManager.NormalizeVersion(value);
			bool result;
			try
			{
				version = new Version(value);
				result = true;
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x06003EBB RID: 16059 RVA: 0x0002B29E File Offset: 0x0002949E
		private static string NormalizeVersion(string value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			value = value.Trim();
			if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
			{
				value = value.Substring(1);
			}
			return value;
		}

		// Token: 0x06003EBC RID: 16060 RVA: 0x001D6B48 File Offset: 0x001D4D48
		private static void StartTool(string mode, string source, string version)
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(PatchSettings.ToolPath);
			int num = 0;
			try
			{
				num = Process.GetCurrentProcess().Id;
			}
			catch
			{
			}
			if (mode.Equals("--offer-pending-update", StringComparison.OrdinalIgnoreCase))
			{
				processStartInfo.Arguments = string.Concat(new string[]
				{
					PatchUpdateManager.Quote(mode),
					" ",
					PatchUpdateManager.Quote(PatchSettings.GameDirectory),
					" ",
					PatchUpdateManager.Quote(version),
					" ",
					PatchUpdateManager.Quote(source),
					" --wait-pid ",
					num.ToString(CultureInfo.InvariantCulture)
				});
			}
			else
			{
				processStartInfo.Arguments = string.Concat(new string[]
				{
					PatchUpdateManager.Quote(mode),
					" ",
					PatchUpdateManager.Quote(source),
					" ",
					PatchUpdateManager.Quote(PatchSettings.GameDirectory),
					" ",
					PatchUpdateManager.Quote(version),
					" --wait-pid ",
					num.ToString(CultureInfo.InvariantCulture)
				});
			}
			processStartInfo.WorkingDirectory = PatchSettings.GameDirectory;
			processStartInfo.UseShellExecute = true;
			Process.Start(processStartInfo);
		}

		// Token: 0x06003EBD RID: 16061 RVA: 0x0002B2C9 File Offset: 0x000294C9
		private static string Quote(string value)
		{
			if (value == null)
			{
				value = string.Empty;
			}
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}

		// Token: 0x06003EBE RID: 16062 RVA: 0x001D6C80 File Offset: 0x001D4E80
		private static string SafeFileName(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "unknown";
			}
			foreach (char oldChar in Path.GetInvalidFileNameChars())
			{
				value = value.Replace(oldChar, '_');
			}
			return value.Replace('.', '_');
		}

		// Token: 0x06003EBF RID: 16063 RVA: 0x001D6CC8 File Offset: 0x001D4EC8
		private static void TryDelete(string path)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
		}

		// Token: 0x0400471D RID: 18205
		private static int sCheckStarted;

		// Token: 0x02000834 RID: 2100
		private sealed class PendingUpdate
		{
			// Token: 0x17000E22 RID: 3618
			// (get) Token: 0x06003EC0 RID: 16064 RVA: 0x0002B2F4 File Offset: 0x000294F4
			public bool IsValid
			{
				get
				{
					return !PatchUpdateManager.IsNullOrWhiteSpaceCompat(this.Version) && !PatchUpdateManager.IsNullOrWhiteSpaceCompat(this.Source) && (File.Exists(this.Source) || Directory.Exists(this.Source));
				}
			}

			// Token: 0x06003EC1 RID: 16065 RVA: 0x001D6CF8 File Offset: 0x001D4EF8
			public static PatchUpdateManager.PendingUpdate Load()
			{
				PatchUpdateManager.PendingUpdate pendingUpdate = new PatchUpdateManager.PendingUpdate();
				try
				{
					if (!File.Exists(PatchSettings.PendingUpdatePath))
					{
						return pendingUpdate;
					}
					string[] array = File.ReadAllLines(PatchSettings.PendingUpdatePath);
					for (int i = 0; i < array.Length; i++)
					{
						string text = array[i].Trim();
						int num = text.IndexOf('=');
						if (num > 0)
						{
							string text2 = text.Substring(0, num).Trim();
							string text3 = text.Substring(num + 1).Trim();
							if (text2.Equals("version", StringComparison.OrdinalIgnoreCase))
							{
								pendingUpdate.Version = text3;
							}
							else if (text2.Equals("source", StringComparison.OrdinalIgnoreCase))
							{
								pendingUpdate.Source = text3;
							}
						}
					}
				}
				catch
				{
				}
				return pendingUpdate;
			}

			// Token: 0x06003EC2 RID: 16066 RVA: 0x001D6DBC File Offset: 0x001D4FBC
			public void Save()
			{
				try
				{
					Directory.CreateDirectory(PatchSettings.CommunityPatchDirectory);
					string contents = string.Concat(new string[]
					{
						"version=",
						this.Version ?? string.Empty,
						Environment.NewLine,
						"source=",
						this.Source ?? string.Empty,
						Environment.NewLine
					});
					File.WriteAllText(PatchSettings.PendingUpdatePath, contents);
				}
				catch
				{
				}
			}

			// Token: 0x0400471E RID: 18206
			public string Version;

			// Token: 0x0400471F RID: 18207
			public string Source;
		}
	}
}
