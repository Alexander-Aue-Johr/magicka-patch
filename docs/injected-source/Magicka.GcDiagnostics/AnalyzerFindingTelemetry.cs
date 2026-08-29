using System;
using System.Globalization;
using System.Text;

namespace Magicka.GcDiagnostics
{
    internal static class AnalyzerFindingTelemetry
    {
        public static bool TryAppendFinding(
            StringBuilder findings,
            string value,
            int characterLimit,
            out int occurrenceCount)
        {
            string displayValue = FormatFinding(value, out occurrenceCount);
            int separatorLength = findings.Length == 0 ? 0 : 3;
            if (findings.Length + separatorLength + displayValue.Length
                > characterLimit)
            {
                return false;
            }

            if (separatorLength != 0)
            {
                findings.Append(" | ");
            }

            findings.Append(displayValue);
            return true;
        }

        public static string FormatFinding(
            string value,
            out int occurrenceCount)
        {
            occurrenceCount = 1;
            int separatorCount = 0;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '\t')
                {
                    separatorCount++;
                }
            }

            int lastSeparator = value.LastIndexOf('\t');
            if (separatorCount < 5 || lastSeparator <= 0)
            {
                return value;
            }

            int parsedCount;
            if (!int.TryParse(
                    value.Substring(lastSeparator + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsedCount)
                || parsedCount <= 0)
            {
                return value;
            }

            occurrenceCount = parsedCount;
            string finding = value.Substring(0, lastSeparator);
            return parsedCount == 1
                ? finding
                : finding + " x"
                  + parsedCount.ToString(CultureInfo.InvariantCulture);
        }
    }
}
