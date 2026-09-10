using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class GameSparksRetirementScenarios
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The retired GameSparks lifecycle exists only in the current version.";
            report.AddNotApplicable("gamesparks.initialize_removed", reason);
            report.AddNotApplicable("gamesparks.update_removed", reason);
            report.AddNotApplicable("gamesparks.end_run_removed", reason);
            report.AddNotApplicable("gamesparks.paradox_preserved", reason);
            return;
        }

        Harness harness = new Harness(magicka, runtimePatchEnabled);
        report.Add("gamesparks.initialize_removed", harness.Removed("Initialize", null));
        report.Add("gamesparks.update_removed", harness.Removed("Update", "System.Single"));
        report.Add("gamesparks.end_run_removed", harness.Removed("EndRun", null));
        report.Add("gamesparks.paradox_preserved", harness.ParadoxPreserved());
    }

    private sealed class Harness
    {
        private readonly Type game;
        private readonly bool runtime;
        private readonly MethodInfo transpiler;

        internal Harness(Assembly magicka, bool runtimePatchEnabled)
        {
            game = magicka.GetType("Magicka.Game", true);
            runtime = runtimePatchEnabled;
            transpiler = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType(
                    "Magicka.CommunityPatch.Runtime.GameSparksRetirementPatch",
                    true).GetMethod("Transpiler");
        }

        internal ScenarioResult Removed(string name, string parameterType)
        {
            List<CodeInstruction> body = Body(Find(name, parameterType));
            int count = CountCalls(
                body, "Magicka.WebTools.GameSparks.GameSparksServices");
            return Result(count == 0, count.ToString(), "0");
        }

        internal ScenarioResult ParadoxPreserved()
        {
            int count = 0;
            count += CountCalls(
                Body(Find("Initialize", null)),
                "Magicka.WebTools.Paradox.ParadoxServices");
            count += CountCalls(
                Body(Find("Update", "System.Single")),
                "Magicka.WebTools.Paradox.ParadoxServices");
            count += CountCalls(
                Body(Find("EndRun", null)),
                "Magicka.WebTools.Paradox.ParadoxServices");
            return Result(count == 3, count.ToString(), "3");
        }

        private MethodInfo Find(string name, string parameterType)
        {
            MethodInfo[] methods = game.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == name &&
                    ((parameterType == null && parameters.Length == 0) ||
                     (parameterType != null && parameters.Length == 1 &&
                      parameters[0].ParameterType.FullName == parameterType)))
                    return method;
            }
            throw new MissingMethodException(game.FullName, name);
        }

        private List<CodeInstruction> Body(MethodInfo method)
        {
            List<CodeInstruction> body = Decode(method);
            if (!runtime)
                return body;
            return new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { body }));
        }

        private static List<CodeInstruction> Decode(MethodBase method)
        {
            DynamicMethod target = new DynamicMethod(
                "ReadGameLifecycleBody", typeof(void), Type.EmptyTypes,
                typeof(GameSparksRetirementScenarios), true);
            List<ILInstruction> source = MethodBodyReader.GetInstructions(
                target.GetILGenerator(), method);
            List<CodeInstruction> result =
                new List<CodeInstruction>(source.Count);
            for (int index = 0; index < source.Count; index++)
                result.Add(source[index].GetCodeInstruction());
            return result;
        }

        private static int CountCalls(
            List<CodeInstruction> body, string declaringType)
        {
            int count = 0;
            for (int index = 0; index < body.Count; index++)
            {
                MethodBase method = body[index].operand as MethodBase;
                if (method != null && method.DeclaringType != null &&
                    method.DeclaringType.FullName == declaringType)
                    count++;
            }
            return count;
        }

        private static ScenarioResult Result(
            bool passed, string actual, string expected)
        {
            return new ScenarioResult(passed, actual, expected);
        }
    }
}
