using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class SteamApiPreflightScenarios
{
    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        bool manual = HasManualOpen(magicka);
        bool absolute = manual;
        bool accessible = manual;
        bool ordered = manual;
        if (runtime)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "magicka-runtime-steam-api-probe");
            Directory.CreateDirectory(directory);
            string path = Magicka.CommunityPatch.Runtime
                .RuntimeSteamApiPreflight.GetPath(directory);
            File.WriteAllBytes(path, new byte[] { 1 });
            absolute = String.Equals(path,
                Path.Combine(directory, "steam_api.dll"),
                StringComparison.OrdinalIgnoreCase);
            accessible = Magicka.CommunityPatch.Runtime
                .RuntimeSteamApiPreflight.CanOpen(path);
            File.Delete(path);
            Directory.Delete(directory);
            ordered = RuntimeOrderIsCorrect();
        }
        report.Add("steam_api.absolute_path", new ScenarioResult(absolute,
            absolute ? "absolute" : "relative", "absolute"));
        report.Add("steam_api.accessible_preflight", new ScenarioResult(accessible,
            accessible ? "verified" : "unchecked", "verified"));
        report.Add("steam_api.bootstrap_order", new ScenarioResult(ordered,
            ordered ? "before_patches" : "missing", "before_patches"));
    }

    private static bool HasManualOpen(Assembly magicka)
    {
        Type program = magicka.GetType("Magicka.Program", true);
        MethodInfo main = program.GetMethod("Main",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.Public | BindingFlags.DeclaredOnly);
        List<CodeInstruction> body = Decode(main, "ReadSteamMain");
        for (int index = 0; index < body.Count; index++)
        {
            MethodInfo called = body[index].operand as MethodInfo;
            if (called != null && called.Name == "OpenSteamApi")
                return true;
        }
        return false;
    }

    private static bool RuntimeOrderIsCorrect()
    {
        MethodInfo apply = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
            .GetMethod("Apply", new Type[] { typeof(string[]) });
        List<CodeInstruction> body = Decode(apply, "ReadBootstrapApply");
        int preflight = -1;
        int applyAssembly = -1;
        for (int index = 0; index < body.Count; index++)
        {
            MethodInfo called = body[index].operand as MethodInfo;
            if (called == null) continue;
            if (called.Name == "EnsureAccessible") preflight = index;
            if (called.Name == "Apply" && called.GetParameters().Length == 1 &&
                called.GetParameters()[0].ParameterType == typeof(Assembly))
                applyAssembly = index;
        }
        return preflight >= 0 && applyAssembly > preflight;
    }

    private static List<CodeInstruction> Decode(MethodBase method, string name)
    {
        DynamicMethod target = new DynamicMethod(name, typeof(void),
            Type.EmptyTypes, typeof(SteamApiPreflightScenarios), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }
}
