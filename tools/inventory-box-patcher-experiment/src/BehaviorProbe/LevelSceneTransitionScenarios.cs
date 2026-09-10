using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class LevelSceneTransitionScenarios
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
                "The serialized render transition is a current-version patch.";
            report.AddNotApplicable("level_transition.direct_and_immediate", reason);
            report.AddNotApplicable("level_transition.animated", reason);
            report.AddNotApplicable("level_transition.load_before_publish", reason);
            report.AddNotApplicable("level_transition.control_flow", reason);
            return;
        }
        Harness harness = new Harness(magicka, runtimePatchEnabled);
        report.Add("level_transition.direct_and_immediate", harness.GoTo());
        report.Add("level_transition.animated", harness.Finish());
        report.Add("level_transition.load_before_publish", harness.LoadOrder());
        report.Add("level_transition.control_flow", harness.Control());
    }

    private sealed class Harness
    {
        private readonly bool runtime;
        private readonly Type level;
        private readonly Type helper;
        private readonly MethodInfo goTo;
        private readonly MethodInfo finish;
        private readonly MethodInfo change;

        internal Harness(Assembly magicka, bool runtime)
        {
            this.runtime = runtime;
            level = magicka.GetType("Magicka.Levels.Level", true);
            MethodInfo[] methods = level.GetMethods(Members);
            goTo = Find(methods, "GoToScene", 6);
            finish = Find(methods, "TransitionFinish", 1);
            change = Find(methods, "ChangeScene", 0);
            helper = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType(
                    "Magicka.CommunityPatch.Runtime.LevelSceneTransitionPatch",
                    true);
        }

        internal ScenarioResult GoTo()
        {
            List<CodeInstruction> body = Body(goTo, "GoToTranspiler");
            bool passed = runtime
                ? CountReferences(body, "WaitForCurrentPlayStateBusy") == 1 &&
                    CountReferences(body, "AwaitDisabledRendering") == 1 &&
                    CountReferences(body, "QueueAwaitDisabledRendering") == 1 &&
                    CountReferences(body, "QueueEnableRendering") == 1
                : CountReferences(body, "Sleep") >= 1 &&
                    CountReferences(body, "AwaitDisabledRendering") == 2 &&
                    CountReferences(body, "EnableRendering") == 1;
            return Bool(passed);
        }

        internal ScenarioResult Finish()
        {
            List<CodeInstruction> body = Body(finish, "FinishTranspiler");
            bool passed = (runtime
                ? CountReferences(body, "QueueAwaitDisabledRendering") == 1 &&
                    CountReferences(body, "QueueEnableRendering") == 1
                : CountReferences(body, "AwaitDisabledRendering") == 1 &&
                    CountReferences(body, "EnableRendering") == 1);
            return Bool(passed);
        }

        internal ScenarioResult LoadOrder()
        {
            List<CodeInstruction> body = Body(change, "ChangeTranspiler");
            int load = FindReference(body, "LoadLevel");
            FieldInfo current = level.GetField("mCurrentScene", Members);
            int publish = -1;
            for (int index = 0; index < body.Count; index++)
                if (body[index].opcode == OpCodes.Stfld &&
                    Object.Equals(body[index].operand, current))
                {
                    publish = index;
                    break;
                }
            return new ScenarioResult(
                load >= 0 && publish >= 0 && load < publish,
                load + "<" + publish,
                "load_before_publish");
        }

        internal ScenarioResult Control()
        {
            List<CodeInstruction> body = Body(change, "ChangeTranspiler");
            bool passed = CountReferences(body, "LoadLevel") == 1 &&
                CountReferences(body, "Initialize") >= 1;
            return Bool(passed);
        }

        private List<CodeInstruction> Body(MethodInfo method, string transpiler)
        {
            List<CodeInstruction> body = Decode(method);
            if (!runtime)
                return body;
            return new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)helper.GetMethod(transpiler)
                    .Invoke(null, new object[] { body }));
        }

        private static MethodInfo Find(MethodInfo[] methods, string name, int count)
        {
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == name &&
                    methods[index].GetParameters().Length == count)
                    return methods[index];
            throw new MissingMethodException(name);
        }

        private static List<CodeInstruction> Decode(MethodBase method)
        {
            DynamicMethod target = new DynamicMethod(
                "ReadLevelTransitionBody", typeof(void), Type.EmptyTypes,
                typeof(LevelSceneTransitionScenarios), true);
            List<ILInstruction> source = MethodBodyReader.GetInstructions(
                target.GetILGenerator(), method);
            List<CodeInstruction> result =
                new List<CodeInstruction>(source.Count);
            for (int index = 0; index < source.Count; index++)
                result.Add(source[index].GetCodeInstruction());
            return result;
        }

        private static int CountReferences(
            List<CodeInstruction> body, string name)
        {
            int count = 0;
            for (int index = 0; index < body.Count; index++)
            {
                MethodBase method = body[index].operand as MethodBase;
                if (method != null && method.Name == name)
                    count++;
            }
            return count;
        }

        private static int FindReference(
            List<CodeInstruction> body, string name)
        {
            for (int index = 0; index < body.Count; index++)
            {
                MethodBase method = body[index].operand as MethodBase;
                if (method != null && method.Name == name)
                    return index;
            }
            return -1;
        }

        private static ScenarioResult Bool(bool passed)
        {
            return new ScenarioResult(passed,
                passed ? "serialized" : "unserialized", "serialized");
        }
    }
}
