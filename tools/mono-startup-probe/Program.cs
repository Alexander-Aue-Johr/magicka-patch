using System;
using System.Reflection;

internal static class MonoStartupProbe
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: MonoStartupProbe <Magicka.exe>");
            return 2;
        }

        Assembly assembly = Assembly.LoadFrom(args[0]);
        Type telemetry = assembly.GetType(
            "Magicka.CommunityPatch.PatchTelemetry",
            true);
        MethodInfo sendStartup = telemetry.GetMethod(
            "SendStartup",
            BindingFlags.Public | BindingFlags.Static);
        if (sendStartup == null)
        {
            throw new MissingMethodException(telemetry.FullName, "SendStartup");
        }

        sendStartup.Invoke(null, null);
        return 0;
    }
}
