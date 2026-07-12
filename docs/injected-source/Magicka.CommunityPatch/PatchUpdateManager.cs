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
				if (PatchSettings.Load().CheckForUpdates)
				{
					if (Interlocked.Exchange(ref PatchUpdateManager.sCheckStarted, 1) == 0)
					{
						ThreadPool.QueueUserWorkItem(delegate(object state)
						{
							PatchUpdateManager.CheckWorker();
						});
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
				if (patchSettings.CheckForUpdates)
				{
					string text = PatchUpdateManager.DownloadString(CommunityPatchInfo.LatestReleaseApiUrl, 4500);
					string text2;
					string text3 = PatchUpdateManager.FindFilesOnlyAssetUrl(text, out text2);
					if (!PatchUpdateManager.IsNullOrWhiteSpaceCompat(text2) && !PatchUpdateManager.IsNullOrWhiteSpaceCompat(text3))
					{
						text2 = PatchUpdateManager.NormalizeVersion(text2);
						if (PatchUpdateManager.IsNewerVersion(text2, CommunityPatchInfo.Version))
						{
							PatchUpdateManager.SaveAvailableVersion(text2);
							if (patchSettings.AutoUpdate && File.Exists(PatchSettings.ToolPath) && (PatchUpdateManager.IsNullOrWhiteSpaceCompat(patchSettings.SkippedVersion) || !PatchUpdateManager.NormalizeVersion(patchSettings.SkippedVersion).Equals(text2, StringComparison.OrdinalIgnoreCase)))
							{
								Directory.CreateDirectory(PatchSettings.DownloadDirectory);
								string text4 = PatchUpdateManager.DownloadFilesOnlyAsset(text3, text2);
								if (!PatchUpdateManager.IsNullOrWhiteSpaceCompat(text4))
								{
									new PatchUpdateManager.PendingUpdate
									{
										Version = text2,
										Source = text4
									}.Save();
								}
							}
						}
						else
						{
							PatchUpdateManager.SaveAvailableVersion(string.Empty);
						}
					}
				}
			}
			catch
			{
			}
		}

		public static string GetAvailableVersion()
		{
			try
			{
				if (!string.IsNullOrEmpty(PatchUpdateManager.sAvailableVersion))
				{
					return PatchUpdateManager.sAvailableVersion;
				}
				if (File.Exists(PatchSettings.LatestVersionPath))
				{
					string text = PatchUpdateManager.NormalizeVersion(File.ReadAllText(PatchSettings.LatestVersionPath).Trim());
					if (PatchUpdateManager.IsNewerVersion(text, CommunityPatchInfo.Version))
					{
						PatchUpdateManager.sAvailableVersion = text;
						return text;
					}
				}
			}
			catch
			{
			}
			return string.Empty;
		}

		private static void SaveAvailableVersion(string version)
		{
			PatchUpdateManager.sAvailableVersion = version ?? string.Empty;
			try
			{
				Directory.CreateDirectory(PatchSettings.CommunityPatchDirectory);
				File.WriteAllText(PatchSettings.LatestVersionPath, PatchUpdateManager.sAvailableVersion);
			}
			catch
			{
			}
		}

		private static string DownloadFilesOnlyAsset(string assetUrl, string latestVersion)
		{
			string text = Path.Combine(PatchSettings.DownloadDirectory, PatchUpdateManager.SafeFileName(latestVersion));
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, "magicka-community-patch-" + PatchUpdateManager.NormalizeVersion(latestVersion) + "-files-only.zip");
			if (!File.Exists(text2) || new FileInfo(text2).Length == 0L)
			{
				PatchUpdateManager.DownloadFile(assetUrl, text2, 20000);
			}
			if (File.Exists(text2) && new FileInfo(text2).Length > 0L)
			{
				return text2;
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

		private static string FindFilesOnlyAssetUrl(string json, out string version)
		{
			version = string.Empty;
			if (string.IsNullOrEmpty(json))
			{
				return string.Empty;
			}
			foreach (object obj in new Regex("\"name\"\\s*:\\s*\"(?<name>(?:\\\\.|[^\"])*)\".{0,5000}?\"browser_download_url\"\\s*:\\s*\"(?<url>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline).Matches(json))
			{
				Match match = (Match)obj;
				string text = PatchUpdateManager.UnescapeJson(match.Groups["name"].Value);
				Match match2 = Regex.Match(text, "^magicka-community-patch-(?<version>\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?)-files-only\\.zip$", RegexOptions.IgnoreCase);
				if (match2.Success)
				{
					version = match2.Groups["version"].Value;
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

		private static string sAvailableVersion = string.Empty;

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
