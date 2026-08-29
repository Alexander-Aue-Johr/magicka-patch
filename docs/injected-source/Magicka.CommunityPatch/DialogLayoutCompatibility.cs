using System;
using System.Text;

namespace Magicka.CommunityPatch
{
	internal static class DialogLayoutCompatibility
	{
		public static string RestoreDialogListBreaks(string value)
		{
			if (String.IsNullOrEmpty(value))
			{
				return value;
			}

			StringBuilder builder = null;
			int copyStart = 0;
			for (int index = 0; index + 3 < value.Length; index++)
			{
				if (value[index] != '[' ||
					(value[index + 1] != 'P' && value[index + 1] != 'p') ||
					value[index + 2] != '=')
				{
					continue;
				}

				int close = value.IndexOf(']', index + 3);
				if (close < 0)
				{
					break;
				}

				int next = close + 1;
				while (next < value.Length &&
					(value[next] == ' ' || value[next] == '\t'))
				{
					next++;
				}

				if (next >= value.Length || value[next] != '-' ||
					(next + 1 < value.Length && value[next + 1] == '-'))
				{
					index = close;
					continue;
				}

				if (builder == null)
				{
					builder = new StringBuilder(value.Length + 8);
				}
				builder.Append(value, copyStart, close - copyStart + 1);
				builder.Append('\n');
				copyStart = next;
				index = next - 1;
			}

			if (builder == null)
			{
				return value;
			}
			builder.Append(value, copyStart, value.Length - copyStart);
			return builder.ToString();
		}

		public static string RestoreElementHintBreaks(string value)
		{
			if (String.IsNullOrEmpty(value) ||
				value.IndexOf("#TYPE;") < 0 ||
				value.IndexOf("#PROP;") < 0 ||
				value.IndexOf("#OPP;") < 0)
			{
				return value;
			}

			value = value.Replace("  #TYPE;", "\n\n#TYPE;");
			value = value.Replace("  #PROP;", "\n\n#PROP;");
			value = value.Replace("  #OPP;", "\n\n#OPP;");
			return value.Replace("  ", "\n");
		}
	}
}
