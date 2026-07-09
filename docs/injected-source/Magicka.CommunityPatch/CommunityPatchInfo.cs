using System;

namespace Magicka.CommunityPatch
{
	// Token: 0x0200082D RID: 2093
	internal static class CommunityPatchInfo
	{
		// Token: 0x17000E0D RID: 3597
		// (get) Token: 0x06003E7E RID: 15998 RVA: 0x0002B04A File Offset: 0x0002924A
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

		// Token: 0x17000E0E RID: 3598
		// (get) Token: 0x06003E7F RID: 15999 RVA: 0x0002B07F File Offset: 0x0002927F
		public static string FullVersionText
		{
			get
			{
				return CommunityPatchInfo.DisplayName + " - " + CommunityPatchInfo.Credits;
			}
		}

		// Token: 0x17000E0F RID: 3599
		// (get) Token: 0x06003E80 RID: 16000 RVA: 0x0002B095 File Offset: 0x00029295
		// Release-specific. Must match the version embedded in the shipped Magicka.exe.
		public static string Version
		{
			get
			{
				return "0.0.16";
			}
		}

		// Token: 0x17000E10 RID: 3600
		// (get) Token: 0x06003E81 RID: 16001 RVA: 0x0002B09C File Offset: 0x0002929C
		public static string Name
		{
			get
			{
				return "Community Patch";
			}
		}

		// Token: 0x17000E11 RID: 3601
		// (get) Token: 0x06003E82 RID: 16002 RVA: 0x0002B0A3 File Offset: 0x000292A3
		public static string Author
		{
			get
			{
				return "Alexander Aue-Johr";
			}
		}

		// Token: 0x17000E12 RID: 3602
		// (get) Token: 0x06003E83 RID: 16003 RVA: 0x0002B0AA File Offset: 0x000292AA
		public static string Credits
		{
			get
			{
				return "Special Thanks to PurpleHeartE54, Skappnil, Aggravating-Sky8697 and Economics-Simulator";
			}
		}

		// Token: 0x17000E13 RID: 3603
		// (get) Token: 0x06003E84 RID: 16004 RVA: 0x0002B0B1 File Offset: 0x000292B1
		public static string TelemetryUserAgent
		{
			get
			{
				return "MagickaPatchTelemetry/" + CommunityPatchInfo.Version;
			}
		}

		// Token: 0x17000E14 RID: 3604
		// (get) Token: 0x06003E85 RID: 16005 RVA: 0x0002B0C2 File Offset: 0x000292C2
		public static string PatreonUrl
		{
			get
			{
				return "https://www.patreon.com/c/alexander_aue_johr/membership";
			}
		}

		// Token: 0x17000E15 RID: 3605
		// (get) Token: 0x06003E86 RID: 16006 RVA: 0x0002B0C9 File Offset: 0x000292C9
		public static string LatestReleaseApiUrl
		{
			get
			{
				return "https://api.github.com/repos/Alexander-Aue-Johr/magicka-patch/releases/latest";
			}
		}

		// Token: 0x17000E16 RID: 3606
		// (get) Token: 0x06003E87 RID: 16007 RVA: 0x0002B0D0 File Offset: 0x000292D0
		public static string LatestReleasePageUrl
		{
			get
			{
				return "https://github.com/Alexander-Aue-Johr/magicka-patch/releases/latest";
			}
		}

		// Token: 0x17000E17 RID: 3607
		// (get) Token: 0x06003E88 RID: 16008 RVA: 0x0002B0D7 File Offset: 0x000292D7
		public static string ToolFileName
		{
			get
			{
				return "MagickaPatchTool.exe";
			}
		}

		// Token: 0x17000E18 RID: 3608
		// (get) Token: 0x06003E89 RID: 16009 RVA: 0x0002B0DE File Offset: 0x000292DE
		public static string[] PatreonSupporters
		{
			get
			{
				return new string[]
				{
					"Tonno7",
					"Torsten Caninenberg",
					"PurpleHeartE54"
				};
			}
		}
	}
}
