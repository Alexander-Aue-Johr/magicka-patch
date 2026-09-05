using System;

namespace Magicka.CommunityPatch
{
	internal static class CommunityPatchInfo
	{
		public static string DisplayName
		{
			get
			{
				return string.Concat(new string[]
				{
					CommunityPatchInfo.Name,
					" ",
					CommunityPatchInfo.Version,
					" by ",
					CommunityPatchInfo.Author
				});
			}
		}

		public static string FullVersionText
		{
			get
			{
				string text = CommunityPatchInfo.DisplayName + " - " + CommunityPatchInfo.Credits;
				PatchUpdateManager.CheckForUpdatesInBackground();
				string availableVersion = PatchUpdateManager.GetAvailableVersion();
				if (!string.IsNullOrEmpty(availableVersion))
				{
					text = text + " - Update available: " + availableVersion;
				}
				return text;
			}
		}

		public static string Version
		{
			get
			{
				return "0.0.59";
			}
		}

		public static string Name
		{
			get
			{
				return "Community Patch";
			}
		}

		public static string Author
		{
			get
			{
				return "Alexander Aue-Johr";
			}
		}

		public static string Credits
		{
			get
			{
				return "Special Thanks to SonofKalas, Sadness, PurpleHeartE54, Skappnil, Aggravating-Sky8697 and Economics-Simulator";
			}
		}

		public static string TelemetryUserAgent
		{
			get
			{
				return "MagickaPatchTelemetry/" + CommunityPatchInfo.Version;
			}
		}

		public static string PatreonUrl
		{
			get
			{
				return "https://www.patreon.com/c/alexander_aue_johr/membership";
			}
		}

		public static string LatestReleaseApiUrl
		{
			get
			{
				return "https://api.github.com/repos/Alexander-Aue-Johr/magicka-patch/releases/latest";
			}
		}

		public static string LatestReleasePageUrl
		{
			get
			{
				return "https://github.com/Alexander-Aue-Johr/magicka-patch/releases/latest";
			}
		}

		public static string ToolFileName
		{
			get
			{
				return "MagickaPatchTool.exe";
			}
		}

		public static string[] PatreonSupporters
		{
			get
			{
				return new string[]
				{
					"SonofKalas",
					"Sadness",
					"Tonno7",
					"Torsten Caninenberg",
					"PurpleHeartE54"
				};
			}
		}
	}
}
