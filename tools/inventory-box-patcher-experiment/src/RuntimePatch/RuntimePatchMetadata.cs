namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimePatchMetadata
    {
        public const string Name = "Community Patch";
        public const string Version = "0.0.60";
        public const string Author = "Alexander Aue-Johr";
        public const string Credits =
            "Special Thanks to SonofKalas, Sadness, PurpleHeartE54, " +
            "Skappnil, Aggravating-Sky8697 and Economics-Simulator";
        public const string ToolFileName = "MagickaPatchTool.exe";
        public const string LatestReleaseApiUrl =
            "https://api.github.com/repos/Alexander-Aue-Johr/magicka-patch/releases/latest";
        public const string LatestReleasePageUrl =
            "https://github.com/Alexander-Aue-Johr/magicka-patch/releases/latest";
        public const string PatreonUrl =
            "https://www.patreon.com/c/alexander_aue_johr/membership";
        public const string TelemetryUserAgent =
            "MagickaPatchTelemetry/0.0.60";

        public static string DisplayName
        {
            get { return Name + " " + Version + " by " + Author; }
        }

        public static string FullVersionText
        {
            get
            {
                string text = DisplayName + " - " + Credits;
                RuntimePatchUpdateManager.CheckForUpdatesInBackground();
                string available = RuntimePatchUpdateManager.GetAvailableVersion();
                if (!System.String.IsNullOrEmpty(available))
                    text += " - Update available: " + available;
                return text;
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
