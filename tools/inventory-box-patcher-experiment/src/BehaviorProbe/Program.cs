using System;
using System.IO;
using System.Reflection;
using Magicka.CommunityPatch.Runtime;

internal static class Program
{
    private static Assembly targetAssembly;

    private static int Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("usage: BehaviorProbe <Magicka.exe> <unpatched|runtime>");
            return 2;
        }

        string targetPath = Path.GetFullPath(arguments[0]);
        bool applyRuntimePatch = ParseMode(arguments[1]);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveTargetAssembly;
        targetAssembly = Assembly.LoadFrom(targetPath);

        if (applyRuntimePatch)
            Bootstrap.Apply(targetAssembly);

        BehaviorReport report = BehaviorSuite.Run(targetAssembly, applyRuntimePatch);
        Console.WriteLine("target=" + targetPath);
        Console.WriteLine("assembly=" + targetAssembly.FullName);
        Console.WriteLine("runtime_patch=" + (applyRuntimePatch ? "enabled" : "disabled"));
        report.WriteTo(Console.Out);
        return 0;
    }

    private static bool ParseMode(string value)
    {
        if (value == "runtime")
            return true;
        if (value == "unpatched")
            return false;
        throw new ArgumentException("Unknown probe mode: " + value);
    }

    private static Assembly ResolveTargetAssembly(object sender, ResolveEventArgs arguments)
    {
        if (targetAssembly != null &&
            new AssemblyName(arguments.Name).Name == targetAssembly.GetName().Name)
            return targetAssembly;

        string dependencyPath = Path.Combine(
            Path.GetDirectoryName(targetAssembly.Location),
            new AssemblyName(arguments.Name).Name + ".dll");
        return File.Exists(dependencyPath) ? Assembly.LoadFrom(dependencyPath) : null;
    }
}
