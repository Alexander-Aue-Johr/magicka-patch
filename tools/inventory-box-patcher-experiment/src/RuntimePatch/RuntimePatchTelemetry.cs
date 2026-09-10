using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Magicka.CommunityPatch.Runtime
{
    public static class RuntimePatchTelemetry
    {
        private const string Endpoint = "https://eu.i.posthog.com/capture/";
        private const string ApiKey =
            "phc_vbVuHJdtwsf2gzBY36KcLo8btGZY4D6foFGqtxbkfog8";
        private static readonly object Sync = new object();
        private static string distinctId;
        private static long keyboardElementSelectionCount;
        private static long controllerElementSelectionCount;

        public static void RecordKeyboardElementSelection()
        {
            Interlocked.Increment(ref keyboardElementSelectionCount);
        }

        public static void RecordControllerElementSelection()
        {
            Interlocked.Increment(ref controllerElementSelectionCount);
        }

        public static void SendStartup()
        {
            SendAsync("magicka_patch_start", CommonProperties());
        }

        public static void SendGameClosedNormally()
        {
            Dictionary<string, string> properties = CommonProperties();
            RuntimeTelemetryContext.AddProperties(properties);
            SendBlocking(
                "magicka_patch_game_closed_normally",
                properties,
                1500);
        }

        public static void SendCrash(
            Exception exception,
            string threadName,
            string crashReport)
        {
            if (CrashReportsDisabled())
                return;
            Dictionary<string, string> properties = CommonProperties();
            properties["exception_type"] = Safe(exception == null
                ? "UnknownException"
                : exception.GetType().Name);
            properties["exception_hash"] = exception == null
                ? "unknown"
                : HashShort(exception.ToString());
            properties["thread"] = Safe(String.IsNullOrEmpty(threadName)
                ? "UnNamed"
                : threadName);
            properties["crash_report"] = crashReport ?? String.Empty;
            properties["crash_report_length"] =
                (crashReport == null ? 0 : crashReport.Length).ToString(
                    CultureInfo.InvariantCulture);
            RuntimeTelemetryContext.AddProperties(properties);
            SendBlocking(
                "magicka_patch_crash_report_written",
                properties,
                1800);
        }

        public static void SendCrashFromReportPath(
            Exception exception,
            string threadName,
            string reportPath)
        {
            string report = String.Empty;
            try
            {
                if (!String.IsNullOrEmpty(reportPath))
                    report = File.ReadAllText(reportPath);
            }
            catch
            {
            }
            SendCrash(exception, threadName, report);
        }

        public static void SendRuntimeGuard(
            string eventName,
            string guard,
            string collection,
            string objectType,
            string details,
            string assetName)
        {
            try
            {
                int skipped;
                if (!RuntimeTelemetryBackoff.TryBeginSend(
                    guard,
                    objectType ?? String.Empty,
                    out skipped))
                    return;
                Dictionary<string, string> properties = CommonProperties();
                properties["guard"] = Safe(guard);
                properties["collection"] = Safe(collection);
                properties["object_type"] = Safe(objectType);
                properties["details"] = SafeLong(details);
                properties["skipped_count"] = skipped.ToString(
                    CultureInfo.InvariantCulture);
                if (!String.IsNullOrEmpty(assetName))
                    properties["asset_name"] = Safe(assetName);
                SendAsync(eventName, properties);
            }
            catch
            {
            }
        }

        public static void SendAnimationClipMissing(
            string assetName,
            string clipKey,
            string animationName,
            int animationValue,
            int availableClipCount)
        {
            try
            {
                int skipped;
                if (!RuntimeTelemetryBackoff.TryBeginSend(
                    "animation_clip_missing",
                    Safe(assetName) + "|" + Safe(clipKey) + "|" +
                        animationValue.ToString(CultureInfo.InvariantCulture),
                    out skipped))
                    return;
                Dictionary<string, string> properties = CommonProperties();
                properties["asset_name"] = Safe(assetName);
                properties["clip_key"] = Safe(clipKey);
                properties["animation_name"] = Safe(animationName);
                properties["animation_value"] = animationValue.ToString(
                    CultureInfo.InvariantCulture);
                properties["available_clip_count"] =
                    availableClipCount.ToString(CultureInfo.InvariantCulture);
                properties["skipped_count"] = skipped.ToString(
                    CultureInfo.InvariantCulture);
                SendAsync("magicka_patch_animation_clip_missing", properties);
            }
            catch
            {
            }
        }

        public static void SendTypingTextGuardException(
            string reason,
            char[] text,
            int charIndex,
            int visibleCharacters,
            int primitiveCount,
            float nextChar,
            float typeSpeed,
            Exception exception)
        {
            try
            {
                string fullText = text == null
                    ? String.Empty
                    : new String(text);
                int contextStart = Math.Max(0, charIndex - 80);
                if (contextStart > fullText.Length)
                    contextStart = fullText.Length;
                int contextLength = Math.Min(
                    160,
                    fullText.Length - contextStart);
                int skipped;
                if (!RuntimeTelemetryBackoff.TryBeginSend(
                    reason,
                    exception == null
                        ? String.Empty
                        : exception.GetType().FullName,
                    out skipped))
                    return;
                Dictionary<string, string> properties = CommonProperties();
                properties["reason"] = Safe(reason);
                properties["text_length"] = fullText.Length.ToString(
                    CultureInfo.InvariantCulture);
                properties["text_hash"] = HashShort(fullText);
                properties["char_index"] = charIndex.ToString(
                    CultureInfo.InvariantCulture);
                properties["visible_characters"] = visibleCharacters.ToString(
                    CultureInfo.InvariantCulture);
                properties["primitive_count"] = primitiveCount.ToString(
                    CultureInfo.InvariantCulture);
                properties["expected_visible_characters"] =
                    (primitiveCount / 2).ToString(CultureInfo.InvariantCulture);
                properties["next_char"] = nextChar.ToString(
                    CultureInfo.InvariantCulture);
                properties["type_speed"] = typeSpeed.ToString(
                    CultureInfo.InvariantCulture);
                properties["text_context_start"] = contextStart.ToString(
                    CultureInfo.InvariantCulture);
                properties["text_context"] = contextLength == 0
                    ? String.Empty
                    : SafeLong(fullText.Substring(
                        contextStart,
                        contextLength));
                properties["exception_type"] = exception == null
                    ? String.Empty
                    : Safe(exception.GetType().FullName);
                properties["exception_message"] = exception == null
                    ? String.Empty
                    : Safe(exception.Message);
                properties["exception_hash"] = exception == null
                    ? "unknown"
                    : HashShort(exception.ToString());
                properties["skipped_count"] = skipped.ToString(
                    CultureInfo.InvariantCulture);
                SendAsync(
                    "magicka_patch_typing_text_guard_exception",
                    properties);
            }
            catch
            {
            }
        }

        public static void SendNetworkGuardDrop(
            string side,
            string packetType,
            string senderSteamId,
            string senderName,
            string reason,
            string details)
        {
            try
            {
                int skipped;
                if (!RuntimeTelemetryBackoff.TryBeginSend(
                    reason,
                    String.Empty,
                    out skipped))
                    return;
                Dictionary<string, string> properties = CommonProperties();
                properties["side"] = Safe(side);
                properties["packet_type"] = Safe(packetType);
                properties["sender_steam_id"] = Safe(senderSteamId);
                properties["sender_name"] = Safe(senderName);
                properties["reason"] = Safe(reason);
                properties["details"] = SafeLong(details);
                properties["details_hash"] = HashShort(details);
                properties["skipped_count"] = skipped.ToString(
                    CultureInfo.InvariantCulture);
                SendAsync("magicka_patch_network_guard_drop", properties);
            }
            catch
            {
            }
        }

        public static void SendNetworkDiagnostic(
            string side,
            string subsystem,
            string reason,
            string similarityKey,
            string details)
        {
            try
            {
                int skipped;
                if (!RuntimeTelemetryBackoff.TryBeginSend(
                    reason,
                    similarityKey,
                    out skipped))
                    return;
                Dictionary<string, string> properties = CommonProperties();
                properties["side"] = Safe(side);
                properties["subsystem"] = Safe(subsystem);
                properties["reason"] = Safe(reason);
                properties["similarity_key"] = Safe(similarityKey);
                properties["details"] = SafeLong(details);
                properties["details_hash"] = HashShort(details);
                properties["skipped_count"] = skipped.ToString(
                    CultureInfo.InvariantCulture);
                SendAsync("magicka_patch_network_diagnostic", properties);
            }
            catch
            {
            }
        }

        public static void SendNetworkGuardException(
            string side,
            string packetType,
            string senderSteamId,
            string senderName,
            string reason,
            string details,
            Exception exception)
        {
            try
            {
                int skipped;
                if (!RuntimeTelemetryBackoff.TryBeginSend(
                    reason,
                    String.Empty,
                    out skipped))
                    return;
                Dictionary<string, string> properties = CommonProperties();
                properties["side"] = Safe(side);
                properties["packet_type"] = Safe(packetType);
                properties["sender_steam_id"] = Safe(senderSteamId);
                properties["sender_name"] = Safe(senderName);
                properties["reason"] = Safe(reason);
                properties["details"] = SafeLong(details);
                properties["details_hash"] = HashShort(details);
                properties["skipped_count"] = skipped.ToString(
                    CultureInfo.InvariantCulture);
                properties["exception_type"] = exception == null
                    ? String.Empty
                    : Safe(exception.GetType().FullName);
                properties["exception_message"] = exception == null
                    ? String.Empty
                    : Safe(exception.Message);
                properties["exception_hash"] = exception == null
                    ? "unknown"
                    : HashShort(exception.ToString());
                SendAsync(
                    "magicka_patch_network_guard_exception",
                    properties);
            }
            catch
            {
            }
        }

        public static void SendKhanKillPlaneFallback()
        {
            try
            {
                SendAsync("magicka_patch_khan_killplane_fallback",
                    new Dictionary<string, string>());
            }
            catch
            {
            }
        }

        public static string BuildPayloadForValidation(
            string eventName,
            Dictionary<string, string> properties)
        {
            return BuildPostHogJson(eventName, properties, "validation");
        }

        private static void SendAsync(
            string eventName,
            Dictionary<string, string> properties)
        {
            if (TelemetryDisabled() || IsValidationProcess())
                return;
            SendState state = new SendState();
            state.EventName = eventName;
            state.Properties = properties;
            state.TimeoutMilliseconds = 1200;
            ThreadPool.QueueUserWorkItem(SendWorker, state);
        }

        private static void SendWorker(object value)
        {
            try
            {
                SendState state = value as SendState;
                if (state != null)
                {
                    if (state.EventName == "magicka_patch_start")
                        RuntimeOriginalBackupAudit.AddTelemetryProperties(
                            state.Properties);
                    SendBlocking(
                        state.EventName,
                        state.Properties,
                        state.TimeoutMilliseconds);
                }
            }
            catch
            {
            }
        }

        private static void SendBlocking(
            string eventName,
            Dictionary<string, string> properties,
            int timeoutMilliseconds)
        {
            try
            {
                if (TelemetryDisabled() || IsValidationProcess())
                    return;
                try
                {
                    ServicePointManager.SecurityProtocol |=
                        (SecurityProtocolType)3072;
                }
                catch
                {
                }
                string json = BuildPostHogJson(
                    eventName,
                    properties,
                    GetDistinctId());
                byte[] body = Encoding.UTF8.GetBytes(json);
                HttpWebRequest request =
                    (HttpWebRequest)WebRequest.Create(Endpoint);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.UserAgent = RuntimePatchMetadata.TelemetryUserAgent;
                request.Timeout = timeoutMilliseconds;
                request.ReadWriteTimeout = timeoutMilliseconds;
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);
                using ((HttpWebResponse)request.GetResponse())
                {
                }
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> CommonProperties()
        {
            Dictionary<string, string> properties =
                new Dictionary<string, string>();
            properties["patch_name"] = RuntimePatchMetadata.Name;
            properties["patch_version"] = RuntimePatchMetadata.Version;
            properties["game_version"] = GameVersion();
            properties["os"] = Safe(Environment.OSVersion.ToString());
            properties["game_integrity"] = GameIntegrity();
            long keyboard = Interlocked.CompareExchange(
                ref keyboardElementSelectionCount,
                0,
                0);
            long controller = Interlocked.CompareExchange(
                ref controllerElementSelectionCount,
                0,
                0);
            double total = (double)keyboard + (double)controller;
            properties["keyboard_element_selection_count"] =
                keyboard.ToString(CultureInfo.InvariantCulture);
            properties["controller_element_selection_count"] =
                controller.ToString(CultureInfo.InvariantCulture);
            properties["controller_element_selection_ratio"] =
                (total > 0.0 ? (double)controller / total : 0.0).ToString(
                    "R",
                    CultureInfo.InvariantCulture);
            return properties;
        }

        private static string BuildPostHogJson(
            string eventName,
            Dictionary<string, string> properties,
            string identifier)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"api_key\":\"").Append(Json(ApiKey));
            builder.Append("\",\"event\":\"").Append(Json(eventName));
            builder.Append("\",\"properties\":{");
            builder.Append("\"distinct_id\":\"").Append(Json(identifier));
            builder.Append("\",\"$process_person_profile\":false");
            foreach (KeyValuePair<string, string> property in properties)
            {
                builder.Append(",\"").Append(Json(property.Key));
                builder.Append("\":");
                AppendValue(builder, property.Key, property.Value);
            }
            builder.Append("}}");
            return builder.ToString();
        }

        private static void AppendValue(
            StringBuilder builder,
            string key,
            string value)
        {
            double number;
            if ((key == "skipped_count" ||
                 key == "keyboard_element_selection_count" ||
                 key == "controller_element_selection_count" ||
                 key == "controller_element_selection_ratio") &&
                Double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number) &&
                !Double.IsNaN(number) && !Double.IsInfinity(number))
            {
                builder.Append(value);
                return;
            }
            builder.Append('"').Append(Json(value)).Append('"');
        }

        private static string GameVersion()
        {
            try
            {
                Assembly entry = Assembly.GetEntryAssembly();
                if (entry != null)
                    return Safe(entry.GetName().Version.ToString());
            }
            catch
            {
            }
            return String.Empty;
        }

        private static string GameIntegrity()
        {
            try
            {
                Type helper = Type.GetType(
                    "Magicka.DRM.HackHelper, Magicka",
                    false);
                PropertyInfo status = helper == null
                    ? null
                    : helper.GetProperty(
                        "LicenseStatus",
                        BindingFlags.Static | BindingFlags.Public |
                            BindingFlags.NonPublic);
                object value = status == null
                    ? null
                    : status.GetValue(null, null);
                string name = value == null ? String.Empty : value.ToString();
                if (name == "Pending")
                    return "pending";
                if (name == "Valid")
                    return "original";
                if (name == "Hacked")
                    return "modded";
            }
            catch
            {
            }
            return "unknown";
        }

        private static string GetDistinctId()
        {
            lock (Sync)
            {
                if (!String.IsNullOrEmpty(distinctId))
                    return distinctId;
                try
                {
                    string directory = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.ApplicationData),
                        "MagickaPatch");
                    string path = Path.Combine(
                        directory,
                        "telemetry_id.txt");
                    if (File.Exists(path))
                        distinctId = File.ReadAllText(path).Trim();
                    if (String.IsNullOrEmpty(distinctId))
                    {
                        Directory.CreateDirectory(directory);
                        distinctId = Guid.NewGuid().ToString("N");
                        File.WriteAllText(path, distinctId);
                    }
                }
                catch
                {
                    distinctId = "ephemeral_" + Guid.NewGuid().ToString("N");
                }
                return distinctId;
            }
        }

        private static bool TelemetryDisabled()
        {
            return !RuntimePatchSettings.Load().UsageSharing;
        }

        private static bool IsValidationProcess()
        {
            try
            {
                Assembly entry = Assembly.GetEntryAssembly();
                string name = entry == null
                    ? String.Empty
                    : entry.GetName().Name;
                return name == "BehaviorProbe" ||
                    name == "RuntimeRegistrationProbe";
            }
            catch
            {
                return false;
            }
        }

        private static bool CrashReportsDisabled()
        {
            return TelemetryDisabled() ||
                !RuntimePatchSettings.Load().CrashReports;
        }

        private static string Safe(string value)
        {
            if (value == null)
                return String.Empty;
            return value.Length > 200 ? value.Substring(0, 200) : value;
        }

        private static string SafeLong(string value)
        {
            if (value == null)
                return String.Empty;
            return value.Length > 1000 ? value.Substring(0, 1000) : value;
        }

        private static string HashShort(string value)
        {
            try
            {
                using (SHA256 hasher = SHA256.Create())
                {
                    byte[] hash = hasher.ComputeHash(
                        Encoding.UTF8.GetBytes(value ?? String.Empty));
                    StringBuilder builder = new StringBuilder(12);
                    for (int index = 0; index < 6; index++)
                        builder.Append(hash[index].ToString("x2"));
                    return builder.ToString();
                }
            }
            catch
            {
                return "hash_failed";
            }
        }

        private static string Json(string value)
        {
            if (value == null)
                return String.Empty;
            StringBuilder builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '"')
                    builder.Append("\\\"");
                else if (character == '\\')
                    builder.Append("\\\\");
                else if (character == '\b')
                    builder.Append("\\b");
                else if (character == '\f')
                    builder.Append("\\f");
                else if (character == '\n')
                    builder.Append("\\n");
                else if (character == '\r')
                    builder.Append("\\r");
                else if (character == '\t')
                    builder.Append("\\t");
                else if (character < ' ')
                    builder.Append("\\u").Append(
                        ((int)character).ToString("x4"));
                else
                    builder.Append(character);
            }
            return builder.ToString();
        }

        private sealed class SendState
        {
            internal string EventName;
            internal Dictionary<string, string> Properties;
            internal int TimeoutMilliseconds;
        }
    }
}
