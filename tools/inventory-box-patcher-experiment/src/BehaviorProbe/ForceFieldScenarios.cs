using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class ForceFieldScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        ForceFieldHarness.InstallProbe(magicka);
    }

    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ForceFieldHarness harness = new ForceFieldHarness(
            magicka,
            runtimePatchEnabled);
        try
        {
            report.Add(
                "force_field.current_play_state",
                harness.CurrentPlayState());
            report.Add(
                "force_field.final_cleanup",
                harness.FinalCleanup());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class ForceFieldHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-force-field";
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Type forceFieldType;
    private readonly Type playStateType;
    private readonly Type dataChannelType;
    private readonly Type renderDataType;
    private readonly FieldInfo legacyPlayStateField;
    private readonly FieldInfo recentPlayStateField;
    private readonly FieldInfo renderDataField;
    private readonly FieldInfo collisionField;
    private readonly FieldInfo collisionCallbackField;
    private readonly PropertyInfo skinTagProperty;
    private readonly MethodInfo updateMethod;
    private readonly bool runtimePatchEnabled;
    private readonly HarmonyInstance harmony;

    internal ForceFieldHarness(Assembly magicka, bool applyRuntimePatch)
    {
        forceFieldType = magicka.GetType("Magicka.Levels.ForceField", true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        renderDataType = forceFieldType.GetNestedType(
            "RenderData",
            BindingFlags.NonPublic);
        if (renderDataType == null)
            throw new TypeLoadException(forceFieldType.FullName + "+RenderData");

        legacyPlayStateField = forceFieldType.GetField(
            "mPlayState",
            InstanceMembers);
        recentPlayStateField = RuntimeReflection.RequireField(
            playStateType,
            "sRecentPlayState");
        renderDataField = RequireField("mRenderData");
        collisionField = RequireField("mCollision");
        collisionCallbackField = RuntimeReflection.RequireField(
            collisionField.FieldType,
            "postCollisionCallbackFn");
        skinTagProperty = collisionField.FieldType.GetProperty(
            "Tag",
            BindingFlags.Instance | BindingFlags.Public);
        updateMethod = forceFieldType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (skinTagProperty == null || updateMethod == null)
            throw new MissingMemberException("ForceField test contract changed.");
        runtimePatchEnabled = applyRuntimePatch;
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        ForceFieldProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult CurrentPlayState()
    {
        object forceField = CreateForceField(false);
        object currentScene = NewUninitialized(
            RuntimeReflection.FindLoadedType("PolygonHead.Scene"));
        object staleScene = NewUninitialized(currentScene.GetType());
        object current = NewPlayState(currentScene);
        object stale = NewPlayState(staleScene);
        recentPlayStateField.SetValue(null, current);
        if (legacyPlayStateField != null)
            legacyPlayStateField.SetValue(forceField, stale);
        ForceFieldProbe.Reset();
        ForceFieldProbe.Enabled = true;

        string exception = Invoke(
            updateMethod,
            forceField,
            new object[] { Enum.ToObject(dataChannelType, 0), 0.1f });
        ForceFieldProbe.Enabled = false;
        bool currentUsed = Object.ReferenceEquals(
            ForceFieldProbe.ObservedScene,
            currentScene);
        bool passed = exception == "none" &&
            ForceFieldProbe.AddCalls == 1 && currentUsed;
        return new ScenarioResult(
            passed,
            "completed:" + (exception == "none") +
                ",add_calls:" + ForceFieldProbe.AddCalls +
                ",scene:" + (currentUsed ? "current" : "stale") +
                ",exception:" + exception,
            "completed:True,add_calls:1,scene:current,exception:none");
    }

    internal ScenarioResult FinalCleanup()
    {
        object forceField = CreateForceField(true);
        object skin = collisionField.GetValue(forceField);
        object renderData = ((Array)renderDataField.GetValue(forceField))
            .GetValue(0);
        FieldInfo nestedPoints = RequireField(renderDataType, "CollPoints");

        string exception = "none";
        if (runtimePatchEnabled)
        {
            exception = InvokeRuntimeCleanup(forceField);
        }
        else
        {
            MethodInfo dispose = forceFieldType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose != null)
            {
                exception = Invoke(dispose, forceField, null);
                if (exception == "none")
                    exception = Invoke(dispose, forceField, null);
            }
        }

        bool referencesReleased =
            collisionField.GetValue(forceField) == null &&
            renderDataField.GetValue(forceField) == null &&
            RequireField("mCollPoints").GetValue(forceField) == null &&
            RequireField("mVertices").GetValue(forceField) == null &&
            RequireField("mIndices").GetValue(forceField) == null &&
            RequireField("mDeclaration").GetValue(forceField) == null &&
            (legacyPlayStateField == null ||
                legacyPlayStateField.GetValue(forceField) == null) &&
            nestedPoints.GetValue(renderData) == null;
        bool skinReleased = skinTagProperty.GetValue(skin, null) == null &&
            collisionCallbackField.GetValue(skin) == null;
        bool passed = exception == "none" &&
            referencesReleased && skinReleased;
        return new ScenarioResult(
            passed,
            "completed:" + (exception == "none") +
                ",references:" +
                (referencesReleased ? "released" : "retained") +
                ",skin:" + (skinReleased ? "released" : "retained") +
                ",exception:" + exception,
            "completed:True,references:released,skin:released," +
                "exception:none");
    }

    internal static void InstallProbe(Assembly magicka)
    {
        Type forceField = magicka.GetType("Magicka.Levels.ForceField", true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        PropertyInfo sceneProperty = playState.GetProperty(
            "Scene",
            BindingFlags.Instance | BindingFlags.Public);
        Type sceneType = sceneProperty == null
            ? RuntimeReflection.FindLoadedType("PolygonHead.Scene")
            : sceneProperty.PropertyType;
        MethodInfo addPostEffect = null;
        MethodInfo[] methods = sceneType.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < methods.Length; index++)
        {
            if (methods[index].Name == "AddPostEffect" &&
                methods[index].GetParameters().Length == 2)
            {
                addPostEffect = methods[index];
                break;
            }
        }
        if (addPostEffect == null)
            throw new MissingMethodException(sceneType.FullName, "AddPostEffect");
        HarmonyInstance.Create(HarmonyOwner).Patch(
            addPostEffect,
            new HarmonyMethod(
                typeof(ForceFieldProbe).GetMethod("AddPostEffectPrefix")),
            null,
            null);
    }

    private object CreateForceField(bool includeCollision)
    {
        object forceField = NewUninitialized(forceFieldType);
        RequireField("mTTL").SetValue(forceField, 1f);
        RequireField("mCollPoints").SetValue(
            forceField,
            Array.CreateInstance(
                RequireField("mCollPoints").FieldType.GetElementType(),
                32));
        Array renderData = Array.CreateInstance(renderDataType, 3);
        for (int index = 0; index < renderData.Length; index++)
        {
            object entry = NewUninitialized(renderDataType);
            RequireField(renderDataType, "CollPoints").SetValue(
                entry,
                Array.CreateInstance(
                    RequireField(renderDataType, "CollPoints")
                        .FieldType.GetElementType(),
                    32));
            renderData.SetValue(entry, index);
        }
        renderDataField.SetValue(forceField, renderData);
        RequireField("mVertices").SetValue(forceField, null);
        RequireField("mIndices").SetValue(forceField, null);
        RequireField("mDeclaration").SetValue(forceField, null);

        if (includeCollision)
        {
            object skin = Activator.CreateInstance(collisionField.FieldType);
            skinTagProperty.SetValue(skin, forceField, null);
            MethodInfo callback = forceFieldType.GetMethod(
                "mCollision_postCollisionCallbackFn",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            Delegate handler = Delegate.CreateDelegate(
                collisionCallbackField.FieldType,
                forceField,
                callback);
            collisionCallbackField.SetValue(skin, handler);
            collisionField.SetValue(forceField, skin);
            if (legacyPlayStateField != null)
                legacyPlayStateField.SetValue(
                    forceField,
                    NewUninitialized(playStateType));
        }
        else
        {
            collisionField.SetValue(forceField, null);
        }
        return forceField;
    }

    private object NewPlayState(object scene)
    {
        object state = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(state, "mScene", scene);
        return state;
    }

    private string InvokeRuntimeCleanup(object forceField)
    {
        Type patch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
            .Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.ForceFieldLifecyclePatch",
                false);
        MethodInfo cleanup = patch == null
            ? null
            : patch.GetMethod(
                "Cleanup",
                BindingFlags.Static | BindingFlags.Public);
        return cleanup == null
            ? typeof(MissingMethodException).FullName
            : Invoke(cleanup, null, new object[] { forceField });
    }

    private FieldInfo RequireField(string name)
    {
        return RequireField(forceFieldType, name);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, InstanceMembers);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static string Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
        catch (Exception exception)
        {
            return exception.GetType().FullName;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class ForceFieldProbe
{
    public static bool Enabled;
    public static int AddCalls;
    public static object ObservedScene;

    public static void Reset()
    {
        Enabled = false;
        AddCalls = 0;
        ObservedScene = null;
    }

    public static bool AddPostEffectPrefix(object __instance)
    {
        if (!Enabled)
            return true;
        AddCalls++;
        ObservedScene = __instance;
        return false;
    }
}
