using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Magicka.CommunityPatch.Runtime
{
	public sealed class RuntimePatchSettings
	{
		public static string GameDirectory
		{
			get
			{
				try
				{
					string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
					if (!string.IsNullOrEmpty(baseDirectory))
					{
						return baseDirectory.TrimEnd(new char[]
						{
							Path.DirectorySeparatorChar,
							Path.AltDirectorySeparatorChar
						});
					}
				}
				catch
				{
				}
				string result;
				try
				{
					result = Directory.GetCurrentDirectory();
				}
				catch
				{
					result = ".";
				}
				return result;
			}
		}

		public static string CommunityPatchDirectory
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.GameDirectory, "CommunityPatch");
			}
		}

		public static string SettingsPath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "patch-settings.ini");
			}
		}

		public static string EventLogPath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "event-log.jsonl");
			}
		}

		public static string EventSentStatePath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "event-log.sent");
			}
		}

		public static string AnonymousIdPath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "anonymous-id.txt");
			}
		}

		public static string PendingUpdatePath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "pending-update.ini");
			}
		}

		public static string LatestVersionPath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "latest-version.txt");
			}
		}

		public static string DownloadDirectory
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.CommunityPatchDirectory, "download");
			}
		}

		public static string ToolPath
		{
			get
			{
				return Path.Combine(RuntimePatchSettings.GameDirectory, RuntimePatchMetadata.ToolFileName);
			}
		}

		public static RuntimePatchSettings Load()
		{
			return LoadFrom(RuntimePatchSettings.SettingsPath);
		}

		public static RuntimePatchSettings LoadFrom(string settingsPath)
		{
			RuntimePatchSettings patchSettings = new RuntimePatchSettings();
			patchSettings.UsageSharing = true;
			patchSettings.CrashReports = true;
			patchSettings.CheckForUpdates = true;
			patchSettings.AutoUpdate = false;
			patchSettings.UseMagicka1ControllerScheme = false;
			patchSettings.Version = RuntimePatchMetadata.Version;
			patchSettings.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
			patchSettings.CreatedUtc = string.Empty;
			patchSettings.SkippedVersion = string.Empty;
			if (!File.Exists(settingsPath))
			{
				return patchSettings;
			}
			try
			{
				string[] array = File.ReadAllLines(settingsPath, Encoding.UTF8);
				for (int i = 0; i < array.Length; i++)
				{
					string text = array[i].Trim();
					if (text.Length != 0 && !text.StartsWith("#") && !text.StartsWith("["))
					{
						int num = text.IndexOf('=');
						if (num > 0)
						{
							string text2 = text.Substring(0, num).Trim();
							string text3 = text.Substring(num + 1).Trim();
							if (text2.Equals("use_magicka_1_controller_scheme", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.UseMagicka1ControllerScheme = RuntimePatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("version", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.Version = text3;
							}
							else if (text2.Equals("usage_sharing", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.UsageSharing = RuntimePatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("crash_reports", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.CrashReports = RuntimePatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("auto_update", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.AutoUpdate = RuntimePatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("check_for_updates", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.CheckForUpdates = RuntimePatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("language", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.Language = text3;
							}
							else if (text2.Equals("skipped_version", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.SkippedVersion = text3;
							}
							else if (text2.Equals("created_utc", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.CreatedUtc = text3;
							}
						}
					}
				}
			}
			catch
			{
			}
			return patchSettings;
		}

		public void Save()
		{
			SaveTo(RuntimePatchSettings.SettingsPath);
		}

		public void SaveTo(string settingsPath)
		{
			try
			{
				string directory = Path.GetDirectoryName(settingsPath);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("[MagickaCommunityPatch]");
				stringBuilder.AppendLine("version=" + RuntimePatchSettings.Safe(this.Version));
				stringBuilder.AppendLine("usage_sharing=" + this.UsageSharing.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("crash_reports=" + this.CrashReports.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("check_for_updates=" + this.CheckForUpdates.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("auto_update=" + this.AutoUpdate.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("use_magicka_1_controller_scheme=" + this.UseMagicka1ControllerScheme.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("language=" + RuntimePatchSettings.Safe(this.Language));
				stringBuilder.AppendLine("skipped_version=" + RuntimePatchSettings.Safe(this.SkippedVersion));
				stringBuilder.AppendLine("created_utc=" + RuntimePatchSettings.Safe(this.CreatedUtc));
				stringBuilder.AppendLine("event_log=CommunityPatch\\event-log.jsonl");
				File.WriteAllText(settingsPath, stringBuilder.ToString(), Encoding.UTF8);
			}
			catch
			{
			}
		}

		public static void SaveSkippedVersion(string version)
		{
			try
			{
				RuntimePatchSettings patchSettings = RuntimePatchSettings.Load();
				patchSettings.SkippedVersion = (version ?? string.Empty);
				patchSettings.Save();
			}
			catch
			{
			}
		}

		private static bool ParseBool(string value)
		{
			return value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1");
		}

		private static string Safe(string value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
		}

		public bool UsageSharing;

		public bool CrashReports;

		public bool CheckForUpdates;

		public bool AutoUpdate;

		public bool UseMagicka1ControllerScheme;

		public string Version;

		public string Language;

		public string SkippedVersion;

		public string CreatedUtc;
	}
}
