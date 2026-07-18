using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Magicka.CommunityPatch
{
	internal sealed class PatchSettings
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
				return Path.Combine(PatchSettings.GameDirectory, "CommunityPatch");
			}
		}

		public static string SettingsPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "patch-settings.ini");
			}
		}

		public static string EventLogPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "event-log.jsonl");
			}
		}

		public static string EventSentStatePath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "event-log.sent");
			}
		}

		public static string AnonymousIdPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "anonymous-id.txt");
			}
		}

		public static string PendingUpdatePath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "pending-update.ini");
			}
		}

		public static string LatestVersionPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "latest-version.txt");
			}
		}

		public static string DownloadDirectory
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "download");
			}
		}

		public static string ToolPath
		{
			get
			{
				return Path.Combine(PatchSettings.GameDirectory, CommunityPatchInfo.ToolFileName);
			}
		}

		public static PatchSettings Load()
		{
			PatchSettings patchSettings = new PatchSettings();
			patchSettings.UsageSharing = true;
			patchSettings.CrashReports = true;
			patchSettings.CheckForUpdates = true;
			patchSettings.AutoUpdate = false;
			patchSettings.UseMagicka1ControllerScheme = false;
			patchSettings.Version = CommunityPatchInfo.Version;
			patchSettings.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
			patchSettings.CreatedUtc = string.Empty;
			patchSettings.SkippedVersion = string.Empty;
			string settingsPath = PatchSettings.SettingsPath;
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
								patchSettings.UseMagicka1ControllerScheme = PatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("version", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.Version = text3;
							}
							else if (text2.Equals("usage_sharing", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.UsageSharing = PatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("crash_reports", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.CrashReports = PatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("auto_update", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.AutoUpdate = PatchSettings.ParseBool(text3);
							}
							else if (text2.Equals("check_for_updates", StringComparison.OrdinalIgnoreCase))
							{
								patchSettings.CheckForUpdates = PatchSettings.ParseBool(text3);
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
			try
			{
				Directory.CreateDirectory(PatchSettings.CommunityPatchDirectory);
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("[MagickaCommunityPatch]");
				stringBuilder.AppendLine("version=" + PatchSettings.Safe(this.Version));
				stringBuilder.AppendLine("usage_sharing=" + this.UsageSharing.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("crash_reports=" + this.CrashReports.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("check_for_updates=" + this.CheckForUpdates.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("auto_update=" + this.AutoUpdate.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("use_magicka_1_controller_scheme=" + this.UseMagicka1ControllerScheme.ToString().ToLowerInvariant());
				stringBuilder.AppendLine("language=" + PatchSettings.Safe(this.Language));
				stringBuilder.AppendLine("skipped_version=" + PatchSettings.Safe(this.SkippedVersion));
				stringBuilder.AppendLine("created_utc=" + PatchSettings.Safe(this.CreatedUtc));
				stringBuilder.AppendLine("event_log=CommunityPatch\\event-log.jsonl");
				File.WriteAllText(PatchSettings.SettingsPath, stringBuilder.ToString(), Encoding.UTF8);
			}
			catch
			{
			}
		}

		public static void SaveSkippedVersion(string version)
		{
			try
			{
				PatchSettings patchSettings = PatchSettings.Load();
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
