using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class VersusRulesetScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        if (magicka.GetName().Version < new Version(1, 10))
        {
            const string reason =
                "The legacy Gamer initializer requires live graphics and content";
            report.AddNotApplicable("versus_revive.missing_avatar", reason);
            report.AddNotApplicable("versus_revive.missing_requested_avatar", reason);
            report.AddNotApplicable("versus_revive.available_avatar", reason);
            return;
        }
        VersusRulesetHarness harness = new VersusRulesetHarness(magicka);
        report.Add("versus_revive.missing_avatar", harness.MissingAvatar(null));
        report.Add("versus_revive.missing_requested_avatar", harness.MissingAvatar((ushort)7));
        report.Add("versus_revive.available_avatar", harness.AvailableAvatarContinues());
    }
}

internal sealed class VersusRulesetHarness
{
    private readonly Assembly magicka;
    private readonly Type avatarType;
    private readonly Type characterTemplateType;
    private readonly Type gameType;
    private readonly Type gameSceneType;
    private readonly Type gamerType;
    private readonly Type levelType;
    private readonly Type playerType;
    private readonly Type rulesetType;
    private readonly MethodInfo revivePlayer;
    private readonly object ruleset;

    internal VersusRulesetHarness(Assembly magicka)
    {
        this.magicka = magicka;
        avatarType = magicka.GetType("Magicka.GameLogic.Entities.Avatar", true);
        characterTemplateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        gameType = magicka.GetType("Magicka.Game", true);
        gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        gamerType = magicka.GetType("Magicka.Gamers.Gamer", true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        playerType = magicka.GetType("Magicka.GameLogic.Player", true);
        rulesetType = magicka.GetType("Magicka.Levels.Versus.DeathMatch", true);
        revivePlayer = FindRevivePlayer(rulesetType.BaseType);
        ruleset = NewUninitialized(rulesetType);
        ConfigureRuleset();
        InstallDependencyStubs();
    }

    internal ScenarioResult MissingAvatar(ushort? handle)
    {
        VersusReviveProbe.AvatarResult = null;
        VersusReviveProbe.ThrowOnInitialize = false;
        return Invoke(handle, "returned:0");
    }

    internal ScenarioResult AvailableAvatarContinues()
    {
        VersusReviveProbe.AvatarResult = NewUninitialized(avatarType);
        VersusReviveProbe.ThrowOnInitialize = true;
        try
        {
            InvokeRaw(null);
            return new ScenarioResult(false, "returned", "initialize_reached");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException;
            while (inner != null && inner.InnerException != null)
                inner = inner.InnerException;
            bool reached = inner is VersusReviveProbeReachedException;
            return new ScenarioResult(
                reached,
                reached ? "initialize_reached" : inner.GetType().FullName,
                "initialize_reached");
        }
        finally
        {
            VersusReviveProbe.ThrowOnInitialize = false;
            VersusReviveProbe.AvatarResult = null;
        }
    }

    private ScenarioResult Invoke(ushort? handle, string expected)
    {
        try
        {
            object result = InvokeRaw(handle);
            string actual = "returned:" + result;
            return new ScenarioResult(actual == expected, actual, expected);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException;
            while (inner != null && inner.InnerException != null)
                inner = inner.InnerException;
            return new ScenarioResult(false, inner.GetType().FullName, expected);
        }
    }

    private object InvokeRaw(ushort? handle)
    {
        object matrix = Activator.CreateInstance(
            RuntimeReflection.FindLoadedType("Microsoft.Xna.Framework.Matrix"));
        return revivePlayer.Invoke(ruleset, new object[] { 0, 0, matrix, handle });
    }

    private void ConfigureRuleset()
    {
        ConfigureHeadlessGame();
        object scene = NewUninitialized(gameSceneType);
        object level = NewUninitialized(levelType);
        RuntimeReflection.WriteField(scene, "mLevel", level);
        RuntimeReflection.WriteField(ruleset, "mScene", scene);

        object player = NewUninitialized(playerType);
        RuntimeReflection.WriteField(player, "mGamer", NewUninitialized(gamerType));
        Array players = Array.CreateInstance(playerType, 1);
        players.SetValue(player, 0);
        RuntimeReflection.WriteField(ruleset, "mPlayers", players);

        VersusReviveProbe.CharacterTemplateResult = NewUninitialized(characterTemplateType);
    }

    private void ConfigureHeadlessGame()
    {
        Type serviceContainerType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.GameServiceContainer");
        Type contentManagerType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Content.ContentManager");
        object services = Activator.CreateInstance(serviceContainerType);
        object content = Activator.CreateInstance(
            contentManagerType,
            new object[] { services });
        object game = NewUninitialized(gameType);
        RuntimeReflection.WriteField(game, "content", content);
        RuntimeReflection.RequireField(gameType, "mSingelton").SetValue(null, game);
    }

    private void InstallDependencyStubs()
    {
        HarmonyInstance harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-versus-revive");
        harmony.Patch(
            FindFreeze(),
            new HarmonyMethod(typeof(VersusReviveProbe).GetMethod("SkipOriginal")),
            null,
            null);
        harmony.Patch(
            characterTemplateType.GetMethod(
                "GetCachedTemplate",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { typeof(int) },
                null),
            new HarmonyMethod(ClosePrefix("CharacterTemplatePrefix", characterTemplateType)),
            null,
            null);

        MethodInfo[] cacheMethods = avatarType.GetMethods(
            BindingFlags.Static | BindingFlags.Public);
        for (int index = 0; index < cacheMethods.Length; index++)
        {
            if (cacheMethods[index].Name == "GetFromCache")
            {
                harmony.Patch(
                    cacheMethods[index],
                    new HarmonyMethod(ClosePrefix("AvatarPrefix", avatarType)),
                    null,
                    null);
            }
        }

        harmony.Patch(
            FindAvatarInitialize(),
            new HarmonyMethod(typeof(VersusReviveProbe).GetMethod("InitializePrefix")),
            null,
            null);
    }

    private MethodInfo FindFreeze()
    {
        Type liquidType = magicka.GetType("Magicka.Levels.Liquid", true);
        MethodInfo[] methods = liquidType.GetMethods(BindingFlags.Static | BindingFlags.Public);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name == "Freeze" &&
                parameters.Length == 6 &&
                parameters[5].ParameterType.IsByRef &&
                parameters[5].ParameterType.GetElementType().FullName ==
                    "Magicka.GameLogic.Damage")
                return methods[index];
        }
        throw new MissingMethodException(liquidType.FullName, "Freeze");
    }

    private MethodInfo FindAvatarInitialize()
    {
        MethodInfo[] methods = avatarType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name == "Initialize" &&
                parameters.Length == 3 &&
                parameters[0].ParameterType == characterTemplateType &&
                parameters[2].ParameterType == typeof(int))
                return methods[index];
        }
        throw new MissingMethodException(avatarType.FullName, "Initialize");
    }

    private static MethodInfo FindRevivePlayer(Type type)
    {
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "RevivePlayer" &&
                methods[index].ReturnType == typeof(ushort) &&
                methods[index].GetParameters().Length == 4)
                return methods[index];
        }
        throw new MissingMethodException(type.FullName, "RevivePlayer");
    }

    private static MethodInfo ClosePrefix(string name, Type resultType)
    {
        MethodInfo method = typeof(VersusReviveProbe).GetMethod(name);
        return method.MakeGenericMethod(new Type[] { resultType });
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class VersusReviveProbe
{
    public static object CharacterTemplateResult;
    public static object AvatarResult;
    public static bool ThrowOnInitialize;

    public static bool SkipOriginal()
    {
        return false;
    }

    public static bool CharacterTemplatePrefix<TTemplate>(ref TTemplate __result)
    {
        __result = (TTemplate)CharacterTemplateResult;
        return false;
    }

    public static bool AvatarPrefix<TAvatar>(ref TAvatar __result)
    {
        __result = AvatarResult == null ? default(TAvatar) : (TAvatar)AvatarResult;
        return false;
    }

    public static bool InitializePrefix()
    {
        if (ThrowOnInitialize)
            throw new VersusReviveProbeReachedException();
        return true;
    }
}

internal sealed class VersusReviveProbeReachedException : Exception
{
}
