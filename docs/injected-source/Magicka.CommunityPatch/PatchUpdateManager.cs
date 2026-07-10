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
	internal static class PatchUpdateManager
	{
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

		private static bool IsNullOrWhiteSpaceCompat(string value)
		{
			return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
		}

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

		private static string UnescapeJson(string value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			return value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
		}

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

		private static string Quote(string value)
		{
			if (value == null)
			{
				value = string.Empty;
			}
			return "\"" + value.Replace("\"", "\\\"") + "\"";
		}

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

		private static int sCheckStarted;

		private sealed class PendingUpdate
		{
			public bool IsValid
			{
				get
				{
					return !PatchUpdateManager.IsNullOrWhiteSpaceCompat(this.Version) && !PatchUpdateManager.IsNullOrWhiteSpaceCompat(this.Source) && (File.Exists(this.Source) || Directory.Exists(this.Source));
				}
			}

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

			public string Version;

			public string Source;
		}
	}
}
