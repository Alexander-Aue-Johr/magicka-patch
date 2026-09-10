using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimeTelemetryContext
    {
        private const int MaximumNavigationCharacters = 4096;
        private static readonly object Sync = new object();
        private static string navigationHistory = String.Empty;
        private static int playStateCount;
        private static int sceneTransitionCount;
        private static bool navigationTruncated;
        private static string language = String.Empty;
        private static string glyphFontSource = String.Empty;
        private static string glyphFileCount = "0";
        private static string glyphTotalBytes = "0";
        private static string glyphSha256 = String.Empty;
        private static string glyphFingerprintStatus = "not_recorded";
        private static string resolutionWidth = String.Empty;
        private static string resolutionHeight = String.Empty;
        private static string uiScalePercent = "100";

        public static void RecordPlayState(string levelPath, string levelName)
        {
            try
            {
                string relativeLevel = NormalizeLevelPath(levelPath);
                string safeLevelName = SafeLabel(levelName);
                lock (Sync)
                {
                    playStateCount++;
                    AppendUnsafe((navigationHistory.Length == 0
                        ? String.Empty
                        : " | ") + relativeLevel + " -> " + safeLevelName);
                }
            }
            catch
            {
            }
        }

        public static void RecordScene(string sceneName)
        {
            try
            {
                string safeSceneName = SafeLabel(sceneName);
                lock (Sync)
                {
                    sceneTransitionCount++;
                    AppendUnsafe(" -> " + safeSceneName);
                }
            }
            catch
            {
            }
        }

        public static void RecordMenu()
        {
            try
            {
                lock (Sync)
                {
                    if (navigationHistory.Length != 0 &&
                        !navigationHistory.EndsWith(
                            " -> Menu",
                            StringComparison.Ordinal))
                        AppendUnsafe(" -> Menu");
                }
            }
            catch
            {
            }
        }

        public static void RecordLanguage(string selectedLanguage)
        {
            string safeLanguage = SafeLabel(selectedLanguage);
            string fontSource = safeLanguage;
            string fileCount = "0";
            string totalBytes = "0";
            string fingerprint = String.Empty;
            string status = "error";
            try
            {
                string fontDirectory = Path.Combine(
                    Path.Combine(
                        Path.Combine("content", "Languages"),
                        safeLanguage),
                    "font");
                if (!Directory.Exists(fontDirectory))
                {
                    fontSource = "eng";
                    fontDirectory = Path.Combine(
                        Path.Combine(
                            Path.Combine("content", "Languages"),
                            fontSource),
                        "font");
                }
                if (!Directory.Exists(fontDirectory))
                    status = "missing";
                else
                {
                    string[] files = Directory.GetFiles(
                        fontDirectory,
                        "*.xnb",
                        SearchOption.TopDirectoryOnly);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    long byteCount = 0L;
                    StringBuilder manifest =
                        new StringBuilder(files.Length * 96);
                    for (int index = 0; index < files.Length; index++)
                    {
                        FileInfo information = new FileInfo(files[index]);
                        byteCount += information.Length;
                        byte[] fileHash;
                        using (SHA256 hasher = SHA256.Create())
                        using (FileStream stream = File.OpenRead(files[index]))
                            fileHash = hasher.ComputeHash(stream);
                        manifest.Append(information.Name.ToLowerInvariant());
                        manifest.Append(':');
                        manifest.Append(information.Length.ToString(
                            CultureInfo.InvariantCulture));
                        manifest.Append(':');
                        manifest.Append(Convert.ToBase64String(fileHash));
                        manifest.Append(';');
                    }
                    using (SHA256 hasher = SHA256.Create())
                    {
                        fingerprint = ToHex(hasher.ComputeHash(
                            Encoding.UTF8.GetBytes(manifest.ToString())));
                    }
                    fileCount = files.Length.ToString(
                        CultureInfo.InvariantCulture);
                    totalBytes = byteCount.ToString(
                        CultureInfo.InvariantCulture);
                    status = "ok";
                }
            }
            catch
            {
            }
            try
            {
                lock (Sync)
                {
                    language = safeLanguage;
                    glyphFontSource = fontSource;
                    glyphFileCount = fileCount;
                    glyphTotalBytes = totalBytes;
                    glyphSha256 = fingerprint;
                    glyphFingerprintStatus = status;
                }
            }
            catch
            {
            }
        }

        public static void RecordResolution(int width, int height)
        {
            try
            {
                lock (Sync)
                {
                    resolutionWidth = width.ToString(
                        CultureInfo.InvariantCulture);
                    resolutionHeight = height.ToString(
                        CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }
        }

        public static void RecordUiScale(float scale)
        {
            try
            {
                lock (Sync)
                {
                    uiScalePercent = ((int)(scale * 100f + 0.5f)).ToString(
                        CultureInfo.InvariantCulture);
                }
            }
            catch
            {
            }
        }

        public static void AddProperties(
            Dictionary<string, string> properties)
        {
            if (properties == null)
                return;
            try
            {
                lock (Sync)
                {
                    properties["navigation_history"] = navigationHistory;
                    properties["playstate_count"] = playStateCount.ToString(
                        CultureInfo.InvariantCulture);
                    properties["scene_transition_count"] =
                        sceneTransitionCount.ToString(
                            CultureInfo.InvariantCulture);
                    properties["navigation_history_truncated"] =
                        navigationTruncated ? "true" : "false";
                    properties["language"] = language;
                    properties["glyph_font_source"] = glyphFontSource;
                    properties["glyph_file_count"] = glyphFileCount;
                    properties["glyph_total_bytes"] = glyphTotalBytes;
                    properties["glyph_sha256"] = glyphSha256;
                    properties["glyph_fingerprint_status"] =
                        glyphFingerprintStatus;
                    properties["resolution_width"] = resolutionWidth;
                    properties["resolution_height"] = resolutionHeight;
                    properties["ui_scale_percent"] = uiScalePercent;
                }
            }
            catch
            {
            }
        }

        internal static void ResetForValidation()
        {
            lock (Sync)
            {
                navigationHistory = String.Empty;
                playStateCount = 0;
                sceneTransitionCount = 0;
                navigationTruncated = false;
                language = String.Empty;
                glyphFontSource = String.Empty;
                glyphFileCount = "0";
                glyphTotalBytes = "0";
                glyphSha256 = String.Empty;
                glyphFingerprintStatus = "not_recorded";
                resolutionWidth = String.Empty;
                resolutionHeight = String.Empty;
                uiScalePercent = "100";
            }
        }

        private static string NormalizeLevelPath(string levelPath)
        {
            if (String.IsNullOrEmpty(levelPath))
                return "(unknown level)";
            string normalized = levelPath.Replace('/', '\\');
            const string Marker = "content\\Levels\\";
            int markerIndex = normalized.IndexOf(
                Marker,
                StringComparison.OrdinalIgnoreCase);
            normalized = markerIndex >= 0
                ? normalized.Substring(markerIndex + Marker.Length)
                : Path.GetFileName(normalized);
            return SafeLabel(normalized);
        }

        private static string SafeLabel(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "(unknown)";
            string result = value.Replace('\r', ' ').Replace('\n', ' ')
                .Replace('|', '_');
            if (result.Length > 160)
                result = result.Substring(result.Length - 160);
            return result;
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
                builder.Append(bytes[index].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static void AppendUnsafe(string value)
        {
            string combined = navigationHistory + value;
            if (combined.Length > MaximumNavigationCharacters)
            {
                combined = "..." + combined.Substring(
                    combined.Length - (MaximumNavigationCharacters - 3));
                navigationTruncated = true;
            }
            navigationHistory = combined;
        }
    }
}
