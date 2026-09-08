using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class BlizzardPlayStateScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        BlizzardPlayStateHarness harness = new BlizzardPlayStateHarness(magicka);
        try
        {
            report.Add(
                "blizzard.vector_current_play_state",
                harness.VectorExecute());
            report.Add(
                "blizzard.owner_current_play_state",
                harness.OwnerExecute());
            report.Add(
                "blizzard.update_current_play_state",
                harness.Update());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class BlizzardPlayStateHarness
{
    private readonly Type blizzardType;
    private readonly Type playStateType;
    private readonly Type levelType;
    private readonly Type sceneType;
    private readonly Type characterType;
    private readonly Type vectorType;
    private readonly Type dataChannelType;
    private readonly Type cameraType;
    private readonly Type polygonSceneType;
    private readonly Type renderDataType;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo blizzardSceneField;
    private readonly FieldInfo ambienceField;
    private readonly FieldInfo ttlField;
    private readonly FieldInfo coldTimerField;
    private readonly FieldInfo renderDataField;
    private readonly FieldInfo playStateLevelField;
    private readonly FieldInfo currentSceneField;
    private readonly FieldInfo gameStateSceneField;
    private readonly FieldInfo entityPlayStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo networkManagerSingletonField;
    private readonly FieldInfo spellManagerSingletonField;
    private readonly FieldInfo spellEffectsField;
    private readonly FieldInfo sceneCameraField;
    private readonly FieldInfo scenePostEffectsField;
    private readonly MethodInfo vectorExecute;
    private readonly MethodInfo ownerExecute;
    private readonly MethodInfo update;
    private readonly HarmonyInstance harmony;
    private readonly object originalRecentPlayState;
    private readonly object originalNetworkManager;
    private readonly object originalSpellManager;

    internal BlizzardPlayStateHarness(Assembly magicka)
    {
        blizzardType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        levelType = magicka.GetType("Magicka.Levels.Level", true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        Type spellCasterType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type networkManagerType = magicka.GetType(
            "Magicka.Network.NetworkManager",
            true);
        Type spellManagerType = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellManager",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        dataChannelType = RuntimeReflection.FindLoadedType("PolygonHead.DataChannel");
        cameraType = magicka.GetType("Magicka.Graphics.MagickCamera", true);
        Type cueType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Audio.Cue");
        polygonSceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        renderDataType = blizzardType.GetNestedType(
            "RenderData",
            BindingFlags.NonPublic);
        if (renderDataType == null)
            throw new TypeLoadException(blizzardType.FullName + "+RenderData");

        legacyPlayStateField = blizzardType.GetField(
            "mPlayState",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (legacyPlayStateField != null &&
            legacyPlayStateField.FieldType != playStateType)
            throw new MissingFieldException(blizzardType.FullName, "mPlayState");
        blizzardSceneField = RequireDeclaredField(
            blizzardType,
            "mScene",
            sceneType);
        ambienceField = RequireDeclaredField(blizzardType, "mAmbience", cueType);
        ttlField = RequireDeclaredField(blizzardType, "mTTL", typeof(float));
        coldTimerField = RequireDeclaredField(
            blizzardType,
            "mColdTimer",
            typeof(float));
        renderDataField = blizzardType.GetField(
            "mRenderData",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (renderDataField == null || !renderDataField.FieldType.IsArray ||
            renderDataField.FieldType.GetElementType() != renderDataType)
            throw new MissingFieldException(blizzardType.FullName, "mRenderData");

        playStateLevelField = RequireDeclaredField(
            playStateType,
            "mLevel",
            levelType);
        currentSceneField = RequireDeclaredField(
            levelType,
            "mCurrentScene",
            sceneType);
        gameStateSceneField = RequireHierarchyField(
            playStateType,
            "mScene",
            polygonSceneType);
        entityPlayStateField = RequireHierarchyField(
            characterType,
            "mPlayState",
            playStateType);
        recentPlayStateField = RequireDeclaredField(
            playStateType,
            "sRecentPlayState",
            playStateType,
            BindingFlags.Static | BindingFlags.NonPublic);
        networkManagerSingletonField = RequireDeclaredField(
            networkManagerType,
            "sSingelton",
            networkManagerType,
            BindingFlags.Static | BindingFlags.NonPublic);
        spellManagerSingletonField = RequireDeclaredField(
            spellManagerType,
            "mSingelton",
            spellManagerType,
            BindingFlags.Static | BindingFlags.NonPublic);
        spellEffectsField = spellManagerType.GetField(
            "mEffects",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (spellEffectsField == null)
            throw new MissingFieldException(spellManagerType.FullName, "mEffects");
        sceneCameraField = RequireHierarchyField(
            polygonSceneType,
            "mCamera",
            cameraType.BaseType);
        scenePostEffectsField = polygonSceneType.GetField(
            "mPostEffects",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (scenePostEffectsField == null ||
            !scenePostEffectsField.FieldType.IsArray)
            throw new MissingFieldException(polygonSceneType.FullName, "mPostEffects");

        vectorExecute = RequireMethod(
            "Execute",
            new Type[] { vectorType, playStateType },
            typeof(bool));
        ownerExecute = RequireMethod(
            "Execute",
            new Type[] { spellCasterType, playStateType },
            typeof(bool));
        update = RequireMethod(
            "Update",
            new Type[] { dataChannelType, typeof(float) },
            typeof(void));

        PropertyInfo isPlaying = cueType.GetProperty(
            "IsPlaying",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo isPlayingGetter = isPlaying == null
            ? null
            : isPlaying.GetGetMethod();
        if (isPlayingGetter == null || isPlayingGetter.ReturnType != typeof(bool))
            throw new MissingMethodException(cueType.FullName, "get_IsPlaying");

        originalRecentPlayState = recentPlayStateField.GetValue(null);
        originalNetworkManager = networkManagerSingletonField.GetValue(null);
        originalSpellManager = spellManagerSingletonField.GetValue(null);
        networkManagerSingletonField.SetValue(
            null,
            NewUninitialized(networkManagerType));
        object spellManager = NewUninitialized(spellManagerType);
        spellEffectsField.SetValue(
            spellManager,
            Activator.CreateInstance(spellEffectsField.FieldType));
        spellManagerSingletonField.SetValue(null, spellManager);

        harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-blizzard-play-state");
        harmony.Patch(
            isPlayingGetter,
            new HarmonyMethod(
                typeof(BlizzardPlayStateProbe).GetMethod("IsPlayingPrefix")),
            null,
            null);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(
            "org.magickacommunitypatch.behavior-probe-blizzard-play-state");
        recentPlayStateField.SetValue(null, originalRecentPlayState);
        networkManagerSingletonField.SetValue(null, originalNetworkManager);
        spellManagerSingletonField.SetValue(null, originalSpellManager);
    }

    internal ScenarioResult VectorExecute()
    {
        return Execute(vectorExecute, Activator.CreateInstance(vectorType));
    }

    internal ScenarioResult OwnerExecute()
    {
        object owner = NewUninitialized(characterType);
        return Execute(ownerExecute, owner);
    }

    internal ScenarioResult Update()
    {
        SceneFixture supplied = CreatePlayState();
        SceneFixture current = CreatePlayState();
        recentPlayStateField.SetValue(null, current.PlayState);
        object blizzard = CreateBlizzard();
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(blizzard, supplied.PlayState);

        Invoke(
            update,
            blizzard,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f });

        int suppliedCount = supplied.PostEffects.Count;
        int currentCount = current.PostEffects.Count;
        bool passed = suppliedCount == 0 && currentCount == 1;
        return new ScenarioResult(
            passed,
            "supplied_post_effects:" + suppliedCount +
                ",current_post_effects:" + currentCount,
            "supplied_post_effects:0,current_post_effects:1");
    }

    private ScenarioResult Execute(MethodInfo method, object firstArgument)
    {
        SceneFixture supplied = CreatePlayState();
        SceneFixture current = CreatePlayState();
        recentPlayStateField.SetValue(null, current.PlayState);
        object blizzard = CreateBlizzard();
        if (firstArgument != null && characterType.IsInstanceOfType(firstArgument))
            entityPlayStateField.SetValue(firstArgument, current.PlayState);

        object result = Invoke(
            method,
            blizzard,
            new object[] { firstArgument, supplied.PlayState });
        bool returned = result is bool && (bool)result;
        bool currentScene = Object.ReferenceEquals(
            blizzardSceneField.GetValue(blizzard),
            current.Scene);
        bool legacyReleased = legacyPlayStateField == null ||
            legacyPlayStateField.GetValue(blizzard) == null;
        bool passed = returned && currentScene && legacyReleased;
        return new ScenarioResult(
            passed,
            "returned:" + returned + ",current_scene:" + currentScene +
                ",legacy_released:" + legacyReleased,
            "returned:True,current_scene:True,legacy_released:True");
    }

    private object CreateBlizzard()
    {
        object blizzard = NewUninitialized(blizzardType);
        ttlField.SetValue(blizzard, 1f);
        coldTimerField.SetValue(blizzard, 1f);
        Type cueType = ambienceField.FieldType;
        ambienceField.SetValue(blizzard, NewUninitialized(cueType));
        Array renderData = Array.CreateInstance(renderDataType, 3);
        for (int index = 0; index < renderData.Length; index++)
            renderData.SetValue(NewUninitialized(renderDataType), index);
        renderDataField.SetValue(blizzard, renderData);
        return blizzard;
    }

    private SceneFixture CreatePlayState()
    {
        object scene = NewUninitialized(sceneType);
        object polygonScene = NewUninitialized(polygonSceneType);
        sceneCameraField.SetValue(polygonScene, NewUninitialized(cameraType));
        Array postEffects = Array.CreateInstance(
            scenePostEffectsField.FieldType.GetElementType(),
            3);
        for (int index = 0; index < postEffects.Length; index++)
        {
            object list = Activator.CreateInstance(
                scenePostEffectsField.FieldType.GetElementType());
            postEffects.SetValue(list, index);
        }
        scenePostEffectsField.SetValue(polygonScene, postEffects);

        object level = NewUninitialized(levelType);
        currentSceneField.SetValue(level, scene);
        object playState = NewUninitialized(playStateType);
        playStateLevelField.SetValue(playState, level);
        gameStateSceneField.SetValue(playState, polygonScene);
        return new SceneFixture(
            playState,
            scene,
            (IList)postEffects.GetValue(0));
    }

    private MethodInfo RequireMethod(
        string name,
        Type[] parameters,
        Type returnType)
    {
        MethodInfo method = blizzardType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            parameters,
            null);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(blizzardType.FullName, name);
        return method;
    }

    private static FieldInfo RequireDeclaredField(
        Type type,
        string name,
        Type expectedType)
    {
        return RequireDeclaredField(
            type,
            name,
            expectedType,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
    }

    private static FieldInfo RequireDeclaredField(
        Type type,
        string name,
        Type expectedType,
        BindingFlags flags)
    {
        FieldInfo field = type.GetField(
            name,
            flags | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != expectedType)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static FieldInfo RequireHierarchyField(
        Type type,
        string name,
        Type expectedType)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                continue;
            if (field.FieldType != expectedType)
                throw new MissingFieldException(current.FullName, name);
            return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static object Invoke(
        MethodInfo method,
        object instance,
        object[] arguments)
    {
        try
        {
            return method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

internal sealed class SceneFixture
{
    internal object PlayState { get; private set; }
    internal object Scene { get; private set; }
    internal IList PostEffects { get; private set; }

    internal SceneFixture(object playState, object scene, IList postEffects)
    {
        PlayState = playState;
        Scene = scene;
        PostEffects = postEffects;
    }
}

public static class BlizzardPlayStateProbe
{
    public static bool IsPlayingPrefix(ref bool __result)
    {
        __result = true;
        return false;
    }
}
