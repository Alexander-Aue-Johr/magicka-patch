using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Magicka.CommunityPatch
{
	// Token: 0x02000832 RID: 2098
	internal sealed class PatchSettings
	{
		// Token: 0x17000E19 RID: 3609
		// (get) Token: 0x06003E9E RID: 16030 RVA: 0x001D60D8 File Offset: 0x001D42D8
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

		// Token: 0x17000E1A RID: 3610
		// (get) Token: 0x06003E9F RID: 16031 RVA: 0x0002B169 File Offset: 0x00029369
		public static string CommunityPatchDirectory
		{
			get
			{
				return Path.Combine(PatchSettings.GameDirectory, "CommunityPatch");
			}
		}

		// Token: 0x17000E1B RID: 3611
		// (get) Token: 0x06003EA0 RID: 16032 RVA: 0x0002B17A File Offset: 0x0002937A
		public static string SettingsPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "patch-settings.ini");
			}
		}

		// Token: 0x17000E1C RID: 3612
		// (get) Token: 0x06003EA1 RID: 16033 RVA: 0x0002B18B File Offset: 0x0002938B
		public static string EventLogPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "event-log.jsonl");
			}
		}

		// Token: 0x17000E1D RID: 3613
		// (get) Token: 0x06003EA2 RID: 16034 RVA: 0x0002B19C File Offset: 0x0002939C
		public static string EventSentStatePath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "event-log.sent");
			}
		}

		// Token: 0x17000E1E RID: 3614
		// (get) Token: 0x06003EA3 RID: 16035 RVA: 0x0002B1AD File Offset: 0x000293AD
		public static string AnonymousIdPath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "anonymous-id.txt");
			}
		}

		// Token: 0x17000E1F RID: 3615
		// (get) Token: 0x06003EA4 RID: 16036 RVA: 0x0002B1BE File Offset: 0x000293BE
		public static string PendingUpdatePath
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "pending-update.ini");
			}
		}

		// Token: 0x17000E20 RID: 3616
		// (get) Token: 0x06003EA5 RID: 16037 RVA: 0x0002B1CF File Offset: 0x000293CF
		public static string DownloadDirectory
		{
			get
			{
				return Path.Combine(PatchSettings.CommunityPatchDirectory, "download");
			}
		}

		// Token: 0x17000E21 RID: 3617
		// (get) Token: 0x06003EA6 RID: 16038 RVA: 0x0002B1E0 File Offset: 0x000293E0
		public static string ToolPath
		{
			get
			{
				return Path.Combine(PatchSettings.GameDirectory, CommunityPatchInfo.ToolFileName);
			}
		}

		// Token: 0x06003EA7 RID: 16039 RVA: 0x001D614C File Offset: 0x001D434C
		public static PatchSettings Load()
		{
			PatchSettings patchSettings = new PatchSettings();
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
							if (text2.Equals("version", StringComparison.OrdinalIgnoreCase))
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

		// Token: 0x06003EA8 RID: 16040 RVA: 0x001D6314 File Offset: 0x001D4514
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
				stringBuilder.AppendLine("auto_update=" + this.AutoUpdate.ToString().ToLowerInvariant());
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

		// Token: 0x06003EA9 RID: 16041 RVA: 0x001D6454 File Offset: 0x001D4654
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

		// Token: 0x06003EAA RID: 16042 RVA: 0x0002B1F1 File Offset: 0x000293F1
		private static bool ParseBool(string value)
		{
			return value != null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1");
		}

		// Token: 0x06003EAB RID: 16043 RVA: 0x0002B221 File Offset: 0x00029421
		private static string Safe(string value)
		{
			if (value == null)
			{
				return string.Empty;
			}
			return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
		}

		// Token: 0x04004716 RID: 18198
		public bool UsageSharing;

		// Token: 0x04004717 RID: 18199
		public bool CrashReports;

		// Token: 0x04004718 RID: 18200
		public bool AutoUpdate;

		// Token: 0x04004719 RID: 18201
		public string Version;

		// Token: 0x0400471A RID: 18202
		public string Language;

		// Token: 0x0400471B RID: 18203
		public string SkippedVersion;

		// Token: 0x0400471C RID: 18204
		public string CreatedUtc;
	}
}
