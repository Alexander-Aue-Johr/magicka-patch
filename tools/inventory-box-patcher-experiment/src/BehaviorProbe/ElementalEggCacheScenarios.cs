using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ElementalEggCacheScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ElementalEggCacheHarness harness =
            new ElementalEggCacheHarness(magicka, runtimePatchEnabled);
        report.Add(
            "elemental_egg_cache.level_dispose",
            harness.InitializedDispose());
        report.Add(
            "elemental_egg_cache.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class ElementalEggCacheHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly Type playStateType;
    private readonly Type eggType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo manualDisposeCache;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo templateLookupField;
    private readonly FieldInfo eggDamageMemoryField;
    private readonly FieldInfo templateModelsField;

    internal ElementalEggCacheHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        eggType = magicka.GetType(
            "Magicka.GameLogic.Entities.ElementalEgg",
            true);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (playStateDispose == null || playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");

        manualDisposeCache = eggType.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        cacheField = RequireCollectionField(eggType, "sCache", typeof(IList));
        templateLookupField = RequireCollectionField(
            eggType,
            "sTemplateLookup",
            typeof(IDictionary));
        eggDamageMemoryField = eggType.GetField(
            "mDamageMemory",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (eggDamageMemoryField == null)
            throw new MissingFieldException(eggType.FullName, "mDamageMemory");
        Type templateType =
            templateLookupField.FieldType.GetGenericArguments()[1];
        templateModelsField = templateType.GetField(
            "mModels",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (templateModelsField == null ||
            !templateModelsField.FieldType.IsArray)
            throw new MissingFieldException(templateType.FullName, "mModels");
    }

    internal ScenarioResult InitializedDispose()
    {
        CacheState state = Populate();
        if (manualDisposeCache != null)
            Invoke(manualDisposeCache, null);
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
        IList cache = (IList)Activator.CreateInstance(cacheField.FieldType);
        object egg = NewUninitialized(eggType);
        object eggDamageMemory =
            Activator.CreateInstance(eggDamageMemoryField.FieldType);
        eggDamageMemoryField.SetValue(egg, eggDamageMemory);
        cache.Add(egg);
        cacheField.SetValue(null, cache);

        IDictionary lookup =
            (IDictionary)Activator.CreateInstance(templateLookupField.FieldType);
        Type[] arguments = templateLookupField.FieldType.GetGenericArguments();
        object key = Activator.CreateInstance(arguments[0]);
        object template = NewUninitialized(arguments[1]);
        Array templateModels = Array.CreateInstance(
            templateModelsField.FieldType.GetElementType(),
            1);
        templateModelsField.SetValue(template, templateModels);
        lookup.Add(key, template);
        templateLookupField.SetValue(null, lookup);
        return new CacheState(
            cache,
            lookup,
            egg,
            template,
            eggDamageMemory,
            templateModels);
    }

    private ScenarioResult Result(CacheState state, bool expectedReleased)
    {
        bool cacheReleased = state.Cache.Count == 0;
        bool lookupReleased = state.Lookup.Count == 0;
        bool objectsPreserved =
            Object.ReferenceEquals(
                eggDamageMemoryField.GetValue(state.Egg),
                state.EggDamageMemory) &&
            Object.ReferenceEquals(
                templateModelsField.GetValue(state.Template),
                state.TemplateModels);
        string actual = "cache:" + state.Cache.Count +
            ",templates:" + state.Lookup.Count +
            ",objects:" + (objectsPreserved ? "preserved" : "changed");
        string expected = "cache:" + (expectedReleased ? 0 : 1) +
            ",templates:" + (expectedReleased ? 0 : 1) +
            ",objects:preserved";
        return new ScenarioResult(
            cacheReleased == expectedReleased &&
                lookupReleased == expectedReleased &&
                objectsPreserved,
            actual,
            expected);
    }

    private void InvokeRuntimeCleanup()
    {
        Type cleanupType =
            typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.ElementalEggCachePatch",
                false);
        MethodInfo cleanup = cleanupType == null
            ? null
            : cleanupType.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public);
        if (cleanup != null)
            Invoke(cleanup, null);
    }

    private static FieldInfo RequireCollectionField(
        Type type,
        string name,
        Type collectionInterface)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null ||
            !collectionInterface.IsAssignableFrom(field.FieldType))
            throw new MissingFieldException(type.FullName, name);
        return field;
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
        internal IList Cache { get; private set; }
        internal IDictionary Lookup { get; private set; }
        internal object Egg { get; private set; }
        internal object Template { get; private set; }
        internal object EggDamageMemory { get; private set; }
        internal Array TemplateModels { get; private set; }

        internal CacheState(
            IList cache,
            IDictionary lookup,
            object egg,
            object template,
            object eggDamageMemory,
            Array templateModels)
        {
            Cache = cache;
            Lookup = lookup;
            Egg = egg;
            Template = template;
            EggDamageMemory = eggDamageMemory;
            TemplateModels = templateModels;
        }
    }
}
