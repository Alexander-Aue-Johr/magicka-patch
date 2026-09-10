using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class GameSceneSwayDepthBufferScenarios
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.DeclaredOnly;

    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        Type scene = magicka.GetType("Magicka.Levels.GameScene", false);
        MethodInfo target = Find(scene);
        Type renderManager = magicka.GetType("PolygonHead.RenderManager", false) ??
            Type.GetType("PolygonHead.RenderManager, PolygonHead", false);
        if (target == null || renderManager == null ||
            renderManager.GetProperty("DefaultDepthStencilBuffer",
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic) == null)
        {
            report.AddNotApplicable("game_scene_sway.default_depth", "The sway depth-buffer API is absent.");
            report.AddNotApplicable("game_scene_sway.control", "The sway depth-buffer API is absent.");
            return;
        }
        List<CodeInstruction> body = Decode(target);
        if (runtimePatchEnabled)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType("Magicka.CommunityPatch.Runtime.GameSceneSwayDepthBufferPatch", true);
            patch.GetMethod("FindTarget", Members).Invoke(null, new object[] { magicka });
            body = new List<CodeInstruction>((IEnumerable<CodeInstruction>)
                patch.GetMethod("Transpiler").Invoke(null, new object[] { body }));
        }
        int canonical = Count(body, "get_DefaultDepthStencilBuffer");
        int incidental = Count(body, "get_DepthStencilBuffer");
        report.Add("game_scene_sway.default_depth", new ScenarioResult(
            canonical == 1, canonical.ToString(), "1"));
        report.Add("game_scene_sway.control", new ScenarioResult(
            canonical + incidental == 1 && Count(body, "set_DepthStencilBuffer") == 1 &&
                Count(body, "DrawSway") == 1,
            (canonical + incidental) + ":" + Count(body, "set_DepthStencilBuffer") +
                ":" + Count(body, "DrawSway"), "1:1:1"));
    }

    private static MethodInfo Find(Type scene)
    {
        if (scene == null) return null;
        MethodInfo[] methods = scene.GetMethods(Members);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == "PreRenderUpdate" &&
                methods[index].GetParameters().Length == 5)
                return methods[index];
        return null;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadSway", typeof(void),
            Type.EmptyTypes, typeof(GameSceneSwayDepthBufferScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int index = 0; index < source.Count; index++)
            result.Add(source[index].GetCodeInstruction());
        return result;
    }

    private static int Count(List<CodeInstruction> body, string name)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodBase method = body[index].operand as MethodBase;
            if (method != null && method.Name == name) count++;
        }
        return count;
    }
}
