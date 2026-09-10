using System;
using System.Reflection;
using System.Threading;

namespace Magicka.CommunityPatch.Runtime
{
    public static class Bootstrap
    {
        private static int applied;

        public static void Apply()
        {
            RuntimeSteamApiPreflight.EnsureAccessible();
            Apply(Assembly.GetEntryAssembly());
        }

        public static void Apply(string[] arguments)
        {
            ProgramArgumentSanitizer.Sanitize(arguments);
            RuntimeSteamApiPreflight.EnsureAccessible();
            Apply(Assembly.GetEntryAssembly());
        }

        public static void Apply(Assembly targetAssembly)
        {
            if (Interlocked.CompareExchange(ref applied, 1, 0) != 0)
                return;

            try
            {
                SystemLibraryPreload.PreloadSystemLibraries();
                RuntimePayloadContract.EnsureCompatible(targetAssembly);
                RuntimePatchPlan.ApplyTo(targetAssembly);
                RuntimePatchUpdateManager.CheckForUpdatesInBackground();
                RuntimePatchTelemetry.SendStartup();
            }
            catch (Exception exception)
            {
                RuntimePatchAudit.WriteFailure(targetAssembly, exception);
                throw;
            }
        }
    }
}
