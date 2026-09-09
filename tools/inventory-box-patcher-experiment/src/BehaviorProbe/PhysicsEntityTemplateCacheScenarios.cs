using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PhysicsEntityTemplateCacheScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
            true);
        if (templateType.GetField(
            "sCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null)
        {
            const string reason =
                "PhysicsEntityTemplate.sCache is not present in this Magicka version";
            report.AddNotApplicable(
                "physics_entity_template_cache.level_dispose",
                reason);
            report.AddNotApplicable(
                "physics_entity_template_cache.uninitialized_dispose",
                reason);
            return;
        }
        PhysicsEntityTemplateCacheHarness harness =
            new PhysicsEntityTemplateCacheHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "physics_entity_template_cache.level_dispose",
            harness.InitializedDispose());
        report.Add(
            "physics_entity_template_cache.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class PhysicsEntityTemplateCacheHarness
{
    private static readonly string[] OwnedFieldNames = new string[]
    {
        "mConditions",
        "mMeshVertices",
        "mMeshIndices",
        "mEffects",
        "mModel",
        "mResistances",
        "mGibs",
        "mModels",
        "mSkeleton",
        "mAnimationClips",
        "mAttachedEffects"
    };

    private readonly bool runtimePatchEnabled;
    private readonly Type playStateType;
    private readonly Type templateType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo manualClearCache;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo[] ownedFields;

    internal PhysicsEntityTemplateCacheHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
            true);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (playStateDispose == null ||
            playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");

        manualClearCache = templateType.GetMethod(
            "ClearCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        cacheField = templateType.GetField(
            "sCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (cacheField == null ||
            !typeof(IDictionary).IsAssignableFrom(cacheField.FieldType))
            throw new MissingFieldException(templateType.FullName, "sCache");

        ownedFields = new FieldInfo[OwnedFieldNames.Length];
        for (int index = 0; index < OwnedFieldNames.Length; index++)
        {
            ownedFields[index] = templateType.GetField(
                OwnedFieldNames[index],
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (ownedFields[index] == null)
                throw new MissingFieldException(
                    templateType.FullName,
                    OwnedFieldNames[index]);
        }
    }

    internal ScenarioResult InitializedDispose()
    {
        CacheState state = Populate();
        if (manualClearCache != null)
            Invoke(manualClearCache, null);
        else if (runtimePatchEnabled)
            InvokeRuntimeCleanup();
        return Result(state, true);
    }

    internal ScenarioResult UninitializedDispose()
    {
        CacheState state = Populate();
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mInitialized", false);
        Invoke(playStateDispose, playState);
        return Result(state, false);
    }

    private CacheState Populate()
    {
        IDictionary cache =
            (IDictionary)Activator.CreateInstance(cacheField.FieldType);
        object template = NewUninitialized(templateType);
        object[] values = new object[ownedFields.Length];
        for (int index = 0; index < ownedFields.Length; index++)
        {
            values[index] = CreateValue(ownedFields[index]);
            ownedFields[index].SetValue(template, values[index]);
        }
        cache.Add(123, template);
        cacheField.SetValue(null, cache);
        return new CacheState(cache, template, values);
    }

    private ScenarioResult Result(CacheState state, bool expectedReleased)
    {
        int detached = 0;
        int preserved = 0;
        int clearedLists = 0;
        for (int index = 0; index < ownedFields.Length; index++)
        {
            object current = ownedFields[index].GetValue(state.Template);
            if (current == null)
                detached++;
            if (Object.ReferenceEquals(current, state.Values[index]))
                preserved++;
            IList list = state.Values[index] as IList;
            if ((ownedFields[index].Name == "mMeshVertices" ||
                ownedFields[index].Name == "mMeshIndices") &&
                list != null && list.Count == 0)
                clearedLists++;
        }

        int expectedListClears = 2;
        string actual = "cache:" + state.Cache.Count +
            ",detached:" + detached +
            ",preserved:" + preserved +
            ",cleared_lists:" + clearedLists;
        string expected = expectedReleased
            ? "cache:0,detached:11,preserved:0,cleared_lists:" +
                expectedListClears
            : "cache:1,detached:0,preserved:11,cleared_lists:0";
        bool passed = expectedReleased
            ? state.Cache.Count == 0 &&
                detached == ownedFields.Length &&
                preserved == 0 &&
                clearedLists == expectedListClears
            : state.Cache.Count == 1 &&
                detached == 0 &&
                preserved == ownedFields.Length &&
                clearedLists == 0;
        return new ScenarioResult(passed, actual, expected);
    }

    private object CreateValue(FieldInfo field)
    {
        Type type = field.FieldType;
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType(), 1);
        if (field.Name == "mMeshVertices" ||
            field.Name == "mMeshIndices")
        {
            IList list = (IList)Activator.CreateInstance(type);
            Type elementType = type.GetGenericArguments()[0];
            list.Add(elementType.IsValueType
                ? Activator.CreateInstance(elementType)
                : NewUninitialized(elementType));
            return list;
        }
        return NewUninitialized(type);
    }

    private void InvokeRuntimeCleanup()
    {
        Type cleanupType =
            typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.PhysicsEntityTemplateCachePatch",
                false);
        MethodInfo cleanup = cleanupType == null
            ? null
            : cleanupType.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public);
        if (cleanup != null)
            Invoke(cleanup, null);
    }

    private static object Invoke(MethodInfo method, object target)
    {
        try
        {
            return method.Invoke(target, new object[0]);
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

    private sealed class CacheState
    {
        internal IDictionary Cache { get; private set; }
        internal object Template { get; private set; }
        internal object[] Values { get; private set; }

        internal CacheState(
            IDictionary cache,
            object template,
            object[] values)
        {
            Cache = cache;
            Template = template;
            Values = values;
        }
    }
}
