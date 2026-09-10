using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Magicka.CommunityPatch.Runtime;

internal static class Program
{
    private static Assembly originalAssembly;

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

        AppDomain.CurrentDomain.AssemblyResolve += ResolveOriginalAssembly;
        originalAssembly = Assembly.LoadFrom(originalPath);
        Bootstrap.Apply(originalAssembly);
        PrepareProjectileSpawn();

        string auditPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "magicka-runtime-patch-audit.txt");
        bool auditPassed = File.Exists(auditPath) &&
            File.ReadAllText(auditPath).Contains("result=PASS");

        Console.WriteLine("original_registration=" + (auditPassed ? "PASS" : "FAIL"));
        return auditPassed ? 0 : 1;
    }

    private static void PrepareProjectileSpawn()
    {
        Type projectileSpell = originalAssembly.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            true);
        MethodInfo[] methods = projectileSpell.GetMethods(
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name != "SpawnMissile" ||
                methods[index].GetParameters().Length != 9)
                continue;
            RuntimeHelpers.PrepareMethod(methods[index].MethodHandle);
            return;
        }
        throw new MissingMethodException(projectileSpell.FullName, "SpawnMissile");
    }

    private static Assembly ResolveOriginalAssembly(object sender, ResolveEventArgs arguments)
    {
        if (originalAssembly != null &&
            new AssemblyName(arguments.Name).Name == originalAssembly.GetName().Name)
            return originalAssembly;

        string dependencyPath = Path.Combine(
            Path.GetDirectoryName(originalAssembly.Location),
            new AssemblyName(arguments.Name).Name + ".dll");
        return File.Exists(dependencyPath) ? Assembly.LoadFrom(dependencyPath) : null;
    }
}
