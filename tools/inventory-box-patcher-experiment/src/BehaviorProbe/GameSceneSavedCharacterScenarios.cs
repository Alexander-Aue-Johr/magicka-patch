using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class GameSceneSavedCharacterScenarios
{
    private const BindingFlags Members = BindingFlags.Instance |
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Run(Assembly magicka, bool runtime, BehaviorReport report)
    {
        Type scene = magicka.GetType("Magicka.Levels.GameScene", false);
        MethodInfo initialize = Find(scene, "Initialize", 3);
        MethodInfo saved = Find(scene, "AddSavedEntities", 0);
        if (initialize == null || saved == null)
        {
            AddNotApplicable(report);
            return;
        }
        List<CodeInstruction> initializeBody = Decode(initialize);
        List<CodeInstruction> savedBody = Decode(saved);
        if (runtime)
        {
            Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly
                .GetType("Magicka.CommunityPatch.Runtime.GameSceneSavedCharacterPatch", true);
            patch.GetMethod("FindInitialize", Members).Invoke(null, new object[] { magicka });
            initializeBody = NewBody(patch, "InitializeTranspiler", initializeBody);
            patch.GetMethod("FindAddSavedEntities", Members).Invoke(null, new object[] { magicka });
            savedBody = NewBody(patch, "SavedEntitiesTranspiler", savedBody);
        }

        int avatarInitializes = CountAvatarInitializes(initializeBody);
        bool templateRestore = Count(savedBody, "ReApplyTemplate", null) == 1 ||
            Count(savedBody, "ReapplyNpcTemplate", null) == 1;
        bool currentManager = Count(savedBody, "get_RecentPlayState", null) == 1;
        bool controls = Count(initializeBody, "MoveTo", null) == 1 &&
            Count(savedBody, "Enable", null) >= 1 &&
            Count(savedBody, "AddEntity", null) == 1;
        report.Add("game_scene.saved_avatar_initialize", new ScenarioResult(
            avatarInitializes >= 1, avatarInitializes.ToString(), ">=1"));
        report.Add("game_scene.saved_npc_template", new ScenarioResult(
            templateRestore, templateRestore.ToString(), "True"));
        report.Add("game_scene.saved_current_manager", new ScenarioResult(
            currentManager, currentManager.ToString(), "True"));
        report.Add("game_scene.saved_controls", new ScenarioResult(
            controls, controls.ToString(), "True"));
    }

    private static List<CodeInstruction> NewBody(Type patch, string name,
        List<CodeInstruction> body)
    {
        return new List<CodeInstruction>((IEnumerable<CodeInstruction>)
            patch.GetMethod(name).Invoke(null, new object[] { body }));
    }

    private static MethodInfo Find(Type type, string name, int parameters)
    {
        if (type == null) return null;
        MethodInfo[] methods = type.GetMethods(Members);
        for (int i = 0; i < methods.Length; i++)
            if (methods[i].Name == name &&
                methods[i].GetParameters().Length == parameters)
                return methods[i];
        return null;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod("ReadSavedCharacters", typeof(void),
            Type.EmptyTypes, typeof(GameSceneSavedCharacterScenarios), true);
        List<ILInstruction> source = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result = new List<CodeInstruction>(source.Count);
        for (int i = 0; i < source.Count; i++)
            result.Add(source[i].GetCodeInstruction());
        return result;
    }

    private static int Count(List<CodeInstruction> body, string name,
        string declaringType)
    {
        int count = 0;
        for (int i = 0; i < body.Count; i++)
        {
            MethodBase method = body[i].operand as MethodBase;
            if (method != null && method.Name == name &&
                (declaringType == null || method.DeclaringType.FullName == declaringType))
                count++;
        }
        return count;
    }

    private static int CountAvatarInitializes(List<CodeInstruction> body)
    {
        int count = 0;
        for (int i = 0; i < body.Count; i++)
        {
            MethodInfo method = body[i].operand as MethodInfo;
            if (method == null || method.Name != "Initialize")
                continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 3 && parameters[0].ParameterType.FullName ==
                "Magicka.GameLogic.Entities.CharacterTemplate")
                count++;
        }
        return count;
    }

    private static void AddNotApplicable(BehaviorReport report)
    {
        report.AddNotApplicable("game_scene.saved_avatar_initialize", "Saved-character APIs are absent.");
        report.AddNotApplicable("game_scene.saved_npc_template", "Saved-character APIs are absent.");
        report.AddNotApplicable("game_scene.saved_current_manager", "Saved-character APIs are absent.");
        report.AddNotApplicable("game_scene.saved_controls", "Saved-character APIs are absent.");
    }
}
