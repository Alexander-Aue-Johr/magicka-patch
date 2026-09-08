using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class SystemLibraryPreloadScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        SystemLibraryPreloadHarness harness =
            new SystemLibraryPreloadHarness(magicka, runtimePatchEnabled);
        report.Add("system_library_preload.startup", harness.Startup());
        report.Add(
            "system_library_preload.windows_paths",
            harness.WindowsPaths());
        report.Add(
            "system_library_preload.non_windows",
            harness.NonWindows());
    }
}

internal sealed class SystemLibraryPreloadHarness
{
    private const string TestSystemDirectory = @"C:\Windows\SysWOW64";
    private readonly bool runtimePatchEnabled;
    private readonly bool manualPreload;

    internal SystemLibraryPreloadHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type programType = magicka.GetType("Magicka.Program", true);
        MethodInfo main = programType.GetMethod(
            "Main",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new Type[] { typeof(string[]) },
            null);
        if (main == null)
            throw new MissingMethodException(programType.FullName, "Main");
        manualPreload = Calls(main, "PreloadSystemProxyLibraries");
    }

    internal ScenarioResult Startup()
    {
        bool actual = runtimePatchEnabled
            ? RuntimeBootstrapCallsPreload()
            : manualPreload;
        return Result(actual, true);
    }

    internal ScenarioResult WindowsPaths()
    {
        bool actual;
        if (runtimePatchEnabled)
        {
            string[] paths = RuntimePaths(TestSystemDirectory, true);
            actual = paths != null && paths.Length == 3 &&
                paths[0] == Path.Combine(TestSystemDirectory, "version.dll") &&
                paths[1] == Path.Combine(TestSystemDirectory, "winmm.dll") &&
                paths[2] == Path.Combine(TestSystemDirectory, "winhttp.dll");
        }
        else
        {
            actual = manualPreload;
        }
        return Result(actual, true);
    }

    internal ScenarioResult NonWindows()
    {
        bool actual = !runtimePatchEnabled ||
            RuntimePaths(TestSystemDirectory, false).Length == 0;
        return Result(actual, true);
    }

    private static bool RuntimeBootstrapCallsPreload()
    {
        Type bootstrap = FindLoadedType(
            "Magicka.CommunityPatch.Runtime.Bootstrap");
        if (bootstrap == null)
            return false;
        MethodInfo apply = bootstrap.GetMethod(
            "Apply",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(Assembly) },
            null);
        return apply != null && Calls(apply, "PreloadSystemLibraries");
    }

    private static string[] RuntimePaths(
        string systemDirectory,
        bool isWindows)
    {
        Type type = FindLoadedType(
            "Magicka.CommunityPatch.Runtime.SystemLibraryPreload");
        MethodInfo method = type == null
            ? null
            : type.GetMethod(
                "GetAbsolutePaths",
                BindingFlags.Static | BindingFlags.Public);
        if (method == null)
            return new string[0];
        return (string[])method.Invoke(
            null,
            new object[] { systemDirectory, isWindows });
    }

    private static bool Calls(MethodBase method, string name)
    {
        List<CodeInstruction> instructions = ReadInstructions(method);
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodBase called = instructions[index].operand as MethodBase;
            if (called != null && called.Name == name)
                return true;
        }
        return false;
    }

    private static List<CodeInstruction> ReadInstructions(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "InspectSystemLibraryPreload",
            typeof(void),
            Type.EmptyTypes,
            typeof(SystemLibraryPreloadHarness),
            true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static ScenarioResult Result(bool actual, bool expected)
    {
        return new ScenarioResult(
            actual == expected,
            actual.ToString(),
            expected.ToString());
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int index = 0; index < assemblies.Length; index++)
        {
            Type type = assemblies[index].GetType(fullName, false);
            if (type != null)
                return type;
        }
        return null;
    }
}
