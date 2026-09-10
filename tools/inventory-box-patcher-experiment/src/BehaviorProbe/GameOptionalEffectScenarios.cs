using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class GameOptionalEffectScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The optional startup-effect policy targets the current version.";
            report.AddNotApplicable("game_effects.optional_removed", reason);
            report.AddNotApplicable("game_effects.required_preserved", reason);
            return;
        }

        Type game = magicka.GetType("Magicka.Game", true);
        MethodInfo loadContent = game.GetMethod(
            "LoadContent",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null);
        List<CodeInstruction> body = Decode(loadContent);
        if (runtimePatchEnabled)
        {
            MethodInfo transpiler = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.GameOptionalEffectPatch",
                    true).GetMethod("Transpiler");
            body = new List<CodeInstruction>(
                (IEnumerable<CodeInstruction>)transpiler.Invoke(
                    null, new object[] { body }));
        }

        int optional = CountConstructors(
            body,
            "PolygonHead.Effects.RenderDeferredEffect",
            "Magicka.Graphics.Effects.EntangleEffect");
        report.Add(
            "game_effects.optional_removed",
            Result(optional == 0, optional.ToString(), "0"));

        int required = CountConstructors(
            body,
            "PolygonHead.Effects.AdditiveEffect",
            "XNAnimation.Effects.SkinnedModelBasicEffect",
            "Magicka.Graphics.Effects.SkinnedModelSkeletonEffect",
            "Magicka.Graphics.Effects.SkinnedShieldEffect");
        report.Add(
            "game_effects.required_preserved",
            Result(required == 4, required.ToString(), "4"));
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadGameLoadContent", typeof(void), Type.EmptyTypes,
            typeof(GameOptionalEffectScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int CountConstructors(
        List<CodeInstruction> body, params string[] typeNames)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            ConstructorInfo constructor = body[index].operand as ConstructorInfo;
            if (constructor == null || constructor.DeclaringType == null)
                continue;
            for (int typeIndex = 0; typeIndex < typeNames.Length; typeIndex++)
                if (constructor.DeclaringType.FullName == typeNames[typeIndex])
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
