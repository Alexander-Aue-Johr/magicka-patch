using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class GameSceneContentUnloadScenarios
{
    private const BindingFlags Members = BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        Type scene = magicka.GetType("Magicka.Levels.GameScene", false);
        Type game = magicka.GetType("Magicka.Game", false);
        MethodInfo unload = scene == null ? null : scene.GetMethod(
            "UnloadContent", Members, null, Type.EmptyTypes, null);
        if (unload == null || game == null ||
            game.GetMethod("DisableRendering", Members, null,
                Type.EmptyTypes, null) == null ||
            game.GetProperty("RenderingEnabled", Members) == null)
        {
            report.AddNotApplicable("game_scene.content_unload", "The render-safe unload API is absent.");
            report.AddNotApplicable("game_scene.content_unload_control", "The render-safe unload API is absent.");
            return;
        }

        List<CodeInstruction> body = Decode(unload);
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType("Magicka.CommunityPatch.Runtime.GameSceneContentUnloadPatch", true);
            patch.GetMethod("FindTarget", Members).Invoke(null, new object[] { magicka });
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler").Invoke(null, new object[] { body }));
        }
        bool manual = Calls(body, "UnloadWhenNotInUseAnymore");
        bool runtimePrepare = Calls(body, "PrepareUnload");
        bool originalUnload = Calls(body, "Unload") || manual;
        report.Add("game_scene.content_unload", new ScenarioResult(
            manual || runtimePrepare, (manual || runtimePrepare).ToString(), "True"));
        report.Add("game_scene.content_unload_control", new ScenarioResult(
            originalUnload, originalUnload.ToString(), "True"));
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadSceneUnload", typeof(void),
            Type.EmptyTypes, typeof(GameSceneContentUnloadScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static bool Calls(List<CodeInstruction> body, string name)
    {
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method != null && method.Name == name)
                return true;
        }
        return false;
    }
}
