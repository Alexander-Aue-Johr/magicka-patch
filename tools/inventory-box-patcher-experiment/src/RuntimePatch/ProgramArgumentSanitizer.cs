using System;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ProgramArgumentSanitizer
    {
        public static void Sanitize(string[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
                return;

            int lastIndex = arguments.Length - 1;
            string argument = arguments[lastIndex];
            if (RequiresFollowingValue(argument))
                arguments[lastIndex] = string.Empty;
        }

        private static bool RequiresFollowingValue(string argument)
        {
            if (argument == null)
                return false;
            return argument.IndexOf(
                    "+connect_lobby",
                    StringComparison.Ordinal) >= 0 ||
                argument.IndexOf("+connect", StringComparison.Ordinal) >= 0 ||
                argument.IndexOf("+password", StringComparison.Ordinal) >= 0;
        }
    }
}
