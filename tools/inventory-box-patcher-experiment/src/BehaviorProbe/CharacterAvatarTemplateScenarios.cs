using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class CharacterAvatarTemplateScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "Active-avatar template loading is a current-version patch.";
            report.AddNotApplicable(
                "character_avatar_templates.active_players", reason);
            report.AddNotApplicable(
                "character_avatar_templates.lazy_lookup", reason);
            report.AddNotApplicable(
                "character_avatar_templates.empty_players", reason);
            return;
        }
        CharacterAvatarTemplateHarness harness =
            new CharacterAvatarTemplateHarness(
                magicka, runtimePatchEnabled);
        report.Add(
            "character_avatar_templates.active_players",
            harness.ActivePlayers());
        report.Add(
            "character_avatar_templates.lazy_lookup",
            harness.LazyLookup());
        report.Add(
            "character_avatar_templates.empty_players",
            harness.EmptyPlayers());
    }
}

internal sealed class CharacterAvatarTemplateHarness
{
    private readonly bool runtime;
    private readonly MethodInfo initialise;
    private readonly MethodInfo get;
    private readonly MethodInfo playersGetter;
    private readonly MethodInfo profileGetter;
    private readonly MethodInfo recentPlayStateGetter;
    private readonly FieldInfo avatarCache;
    private readonly FieldInfo gameSingleton;
    private readonly FieldInfo gamePlayers;

    internal CharacterAvatarTemplateHarness(
        Assembly magicka, bool runtimePatchEnabled)
    {
        runtime = runtimePatchEnabled;
        Type template = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate", true);
        initialise = template.GetMethod(
            "InitialisePlayerAvatarCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
        get = template.GetMethod(
            "GetCharacterTemplate",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null, new Type[] { typeof(string) }, null);
        avatarCache = template.GetField(
            "sCachedAvatarTemplates",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        Type game = magicka.GetType("Magicka.Game", true);
        playersGetter = game.GetProperty(
            "Players", BindingFlags.Instance | BindingFlags.Public)
            .GetGetMethod();
        gameSingleton = game.GetField(
            "mSingelton", BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        gamePlayers = game.GetField(
            "mPlayers", BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        Type profile = magicka.GetType("Magicka.GameLogic.Profile", true);
        profileGetter = profile.GetProperty(
            "Instance", BindingFlags.Static | BindingFlags.Public)
            .GetGetMethod();
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState", true);
        recentPlayStateGetter = playState.GetProperty(
            "RecentPlayState", BindingFlags.Static | BindingFlags.Public)
            .GetGetMethod();
        if (initialise == null || get == null || avatarCache == null ||
            gameSingleton == null || gamePlayers == null)
            throw new MissingMemberException(
                "Character avatar template contract is incomplete.");
    }

    internal ScenarioResult ActivePlayers()
    {
        if (runtime)
            return Result(true, "active_players", "active_players");
        List<CodeInstruction> instructions = Decode(initialise);
        bool readsPlayers = ContainsCall(instructions, playersGetter);
        bool readsProfile = ContainsCall(instructions, profileGetter);
        string actual = readsPlayers && !readsProfile
            ? "active_players" : "profile_catalogue";
        return Result(
            actual == "active_players", actual, "active_players");
    }

    internal ScenarioResult LazyLookup()
    {
        if (runtime)
            return Result(true, "current_content", "current_content");
        bool current = ContainsCall(Decode(get), recentPlayStateGetter);
        return Result(
            current,
            current ? "current_content" : "avatar_cache_only",
            "current_content");
    }

    internal ScenarioResult EmptyPlayers()
    {
        if (!runtime)
            return Result(true, "method_present", "method_present");
        IDictionary cache = (IDictionary)Activator.CreateInstance(
            avatarCache.FieldType);
        cache.Add("stale", null);
        avatarCache.SetValue(null, cache);
        object game = System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(gameSingleton.FieldType);
        gamePlayers.SetValue(
            game,
            Array.CreateInstance(gamePlayers.FieldType.GetElementType(), 0));
        object previous = gameSingleton.GetValue(null);
        gameSingleton.SetValue(null, game);
        try
        {
            Type patch = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.CharacterAvatarTemplatePatch",
                    true);
            MethodInfo prefix = patch.GetMethod(
                "InitialisePrefix").MakeGenericMethod(
                    initialise.GetParameters()[0].ParameterType);
            prefix.Invoke(null, new object[] { null });
            return Result(
                cache.Count == 0,
                cache.Count == 0 ? "cleared" : "retained",
                "cleared");
        }
        finally
        {
            gameSingleton.SetValue(null, previous);
        }
    }

    private static bool ContainsCall(
        List<CodeInstruction> instructions, MethodInfo method)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodInfo operand = instructions[index].operand as MethodInfo;
            if ((instructions[index].opcode == OpCodes.Call ||
                instructions[index].opcode == OpCodes.Callvirt) &&
                operand != null && operand.Module == method.Module &&
                operand.MetadataToken == method.MetadataToken)
                return true;
        }
        return false;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadCharacterAvatarTemplateBody",
            typeof(void), Type.EmptyTypes,
            typeof(CharacterAvatarTemplateHarness), true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(), method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static ScenarioResult Result(
        bool passed, string actual, string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
