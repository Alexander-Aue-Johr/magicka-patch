using System;
using System.IO;
using System.Reflection;
using Magicka.InventoryBoxRuntimePatch;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        if (arguments.Length != 1)
        {
            Console.Error.WriteLine("usage: RuntimeRegistrationProbe <original Magicka.exe>");
            return 2;
        }

        string originalPath = Path.GetFullPath(arguments[0]);
        if (!File.Exists(originalPath))
        {
            Console.Error.WriteLine("Original executable does not exist: " + originalPath);
            return 3;
        }

        Assembly originalAssembly = Assembly.LoadFrom(originalPath);
        Bootstrap.Apply(originalAssembly);

        string auditPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "inventory-box-runtime-audit.txt");
        bool auditPassed = File.Exists(auditPath) &&
            File.ReadAllText(auditPath).Contains("result=PASS");

        Console.WriteLine("original_registration=" + (auditPassed ? "PASS" : "FAIL"));
        return auditPassed ? 0 : 1;
    }
}
