using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Magicka.CommunityPatch.Runtime
{
	public static class RuntimePatchUpdateManager
	{
		public static void CheckForUpdatesInBackground()
		{
			try
			{
				if (RuntimePatchSettings.Load().CheckForUpdates)
				{
					if (Interlocked.Exchange(ref RuntimePatchUpdateManager.sCheckStarted, 1) == 0)
					{
						ThreadPool.QueueUserWorkItem(delegate(object state)
						{
							RuntimePatchUpdateManager.CheckWorker();
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
				RuntimePatchUpdateManager.PendingUpdate pendingUpdate = RuntimePatchUpdateManager.PendingUpdate.Load();
				if (pendingUpdate.IsValid)
				{
					if (File.Exists(RuntimePatchSettings.ToolPath))
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
						}), RuntimePatchMetadata.DisplayName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
						if (dialogResult == DialogResult.Yes)
						{
							RuntimePatchUpdateManager.StartTool("--apply-update", pendingUpdate.Source, pendingUpdate.Version);
						}
						else if (dialogResult == DialogResult.Cancel)
						{
							RuntimePatchSettings.SaveSkippedVersion(pendingUpdate.Version);
							RuntimePatchUpdateManager.TryDelete(RuntimePatchSettings.PendingUpdatePath);
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
				RuntimePatchUpdateManager.PendingUpdate pendingUpdate = RuntimePatchUpdateManager.PendingUpdate.Load();
				if (pendingUpdate.IsValid)
				{
					if (File.Exists(RuntimePatchSettings.ToolPath))
					{
						RuntimePatchUpdateManager.StartTool("--offer-pending-update", pendingUpdate.Source, pendingUpdate.Version);
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
				RuntimePatchSettings patchSettings = RuntimePatchSettings.Load();
				if (patchSettings.CheckForUpdates)
				{
					string text = RuntimePatchUpdateManager.DownloadString(RuntimePatchMetadata.LatestReleaseApiUrl, 4500);
					string text2;
					string text3 = RuntimePatchUpdateManager.FindFilesOnlyAssetUrl(text, out text2);
					if (!RuntimePatchUpdateManager.IsNullOrWhiteSpaceCompat(text2) && !RuntimePatchUpdateManager.IsNullOrWhiteSpaceCompat(text3))
					{
						text2 = RuntimePatchUpdateManager.NormalizeVersion(text2);
						if (RuntimePatchUpdateManager.IsNewerVersion(text2, RuntimePatchMetadata.Version))
						{
							RuntimePatchUpdateManager.SaveAvailableVersion(text2);
							if (patchSettings.AutoUpdate && File.Exists(RuntimePatchSettings.ToolPath) && (RuntimePatchUpdateManager.IsNullOrWhiteSpaceCompat(patchSettings.SkippedVersion) || !RuntimePatchUpdateManager.NormalizeVersion(patchSettings.SkippedVersion).Equals(text2, StringComparison.OrdinalIgnoreCase)))
							{
								Directory.CreateDirectory(RuntimePatchSettings.DownloadDirectory);
								string text4 = RuntimePatchUpdateManager.DownloadFilesOnlyAsset(text3, text2);
								if (!RuntimePatchUpdateManager.IsNullOrWhiteSpaceCompat(text4))
								{
									new RuntimePatchUpdateManager.PendingUpdate
									{
										Version = text2,
										Source = text4
									}.Save();
								}
							}
						}
						else
						{
							RuntimePatchUpdateManager.SaveAvailableVersion(string.Empty);
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
				if (!string.IsNullOrEmpty(RuntimePatchUpdateManager.sAvailableVersion))
				{
					return RuntimePatchUpdateManager.sAvailableVersion;
				}
				if (File.Exists(RuntimePatchSettings.LatestVersionPath))
				{
					string text = RuntimePatchUpdateManager.NormalizeVersion(File.ReadAllText(RuntimePatchSettings.LatestVersionPath).Trim());
					if (RuntimePatchUpdateManager.IsNewerVersion(text, RuntimePatchMetadata.Version))
					{
						RuntimePatchUpdateManager.sAvailableVersion = text;
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
			RuntimePatchUpdateManager.sAvailableVersion = version ?? string.Empty;
			try
			{
				Directory.CreateDirectory(RuntimePatchSettings.CommunityPatchDirectory);
				File.WriteAllText(RuntimePatchSettings.LatestVersionPath, RuntimePatchUpdateManager.sAvailableVersion);
			}
			catch
			{
			}
		}

		private static string DownloadFilesOnlyAsset(string assetUrl, string latestVersion)
		{
			string text = Path.Combine(RuntimePatchSettings.DownloadDirectory, RuntimePatchUpdateManager.SafeFileName(latestVersion));
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, "magicka-community-patch-" + RuntimePatchUpdateManager.NormalizeVersion(latestVersion) + "-files-only.zip");
			if (!File.Exists(text2) || new FileInfo(text2).Length == 0L)
			{
				RuntimePatchUpdateManager.DownloadFile(assetUrl, text2, 20000);
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
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)RuntimePatchUpdateManager.CreateRequest(url, timeoutMs).GetResponse())
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
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)RuntimePatchUpdateManager.CreateRequest(url, timeoutMs).GetResponse())
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
				ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
			}
			catch
			{
			}
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
			httpWebRequest.UserAgent = RuntimePatchMetadata.TelemetryUserAgent;
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
				string text = RuntimePatchUpdateManager.UnescapeJson(match.Groups["name"].Value);
				Match match2 = Regex.Match(text, "^magicka-community-patch-(?<version>\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.-]+)?)-files-only\\.zip$", RegexOptions.IgnoreCase);
				if (match2.Success)
				{
					version = match2.Groups["version"].Value;
					return RuntimePatchUpdateManager.UnescapeJson(match.Groups["url"].Value);
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
			if (RuntimePatchUpdateManager.TryParseVersion(candidate, out v) && RuntimePatchUpdateManager.TryParseVersion(current, out v2))
			{
				return v > v2;
			}
			return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
		}

		private static bool TryParseVersion(string value, out Version version)
		{
			version = null;
			value = RuntimePatchUpdateManager.NormalizeVersion(value);
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
			ProcessStartInfo processStartInfo = new ProcessStartInfo(RuntimePatchSettings.ToolPath);
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
					RuntimePatchUpdateManager.Quote(mode),
					" ",
					RuntimePatchUpdateManager.Quote(RuntimePatchSettings.GameDirectory),
					" ",
					RuntimePatchUpdateManager.Quote(version),
					" ",
					RuntimePatchUpdateManager.Quote(source),
					" --wait-pid ",
					num.ToString(CultureInfo.InvariantCulture)
				});
			}
			else
			{
				processStartInfo.Arguments = string.Concat(new string[]
				{
					RuntimePatchUpdateManager.Quote(mode),
					" ",
					RuntimePatchUpdateManager.Quote(source),
					" ",
					RuntimePatchUpdateManager.Quote(RuntimePatchSettings.GameDirectory),
					" ",
					RuntimePatchUpdateManager.Quote(version),
					" --wait-pid ",
					num.ToString(CultureInfo.InvariantCulture)
				});
			}
			processStartInfo.WorkingDirectory = RuntimePatchSettings.GameDirectory;
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
					return !RuntimePatchUpdateManager.IsNullOrWhiteSpaceCompat(this.Version) && !RuntimePatchUpdateManager.IsNullOrWhiteSpaceCompat(this.Source) && (File.Exists(this.Source) || Directory.Exists(this.Source));
				}
			}

			public static RuntimePatchUpdateManager.PendingUpdate Load()
			{
				RuntimePatchUpdateManager.PendingUpdate pendingUpdate = new RuntimePatchUpdateManager.PendingUpdate();
				try
				{
					if (!File.Exists(RuntimePatchSettings.PendingUpdatePath))
					{
						return pendingUpdate;
					}
					string[] array = File.ReadAllLines(RuntimePatchSettings.PendingUpdatePath);
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
					Directory.CreateDirectory(RuntimePatchSettings.CommunityPatchDirectory);
					string contents = string.Concat(new string[]
					{
						"version=",
						this.Version ?? string.Empty,
						Environment.NewLine,
						"source=",
						this.Source ?? string.Empty,
						Environment.NewLine
					});
					File.WriteAllText(RuntimePatchSettings.PendingUpdatePath, contents);
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

