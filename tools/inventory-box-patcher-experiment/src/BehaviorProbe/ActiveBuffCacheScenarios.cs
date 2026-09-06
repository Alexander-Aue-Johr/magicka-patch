using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ActiveBuffCacheScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ActiveBuffCacheHarness harness = new ActiveBuffCacheHarness(magicka);
        report.Add(
            "active_buff_cache.level_dispose",
            harness.InitializedDispose());
        report.Add(
            "active_buff_cache.uninitialized_dispose",
            harness.UninitializedDispose());
    }
}

internal sealed class ActiveBuffCacheHarness
{
    private readonly Type playStateType;
    private readonly Type hasteType;
    private readonly Type shrinkType;
    private readonly MethodInfo playStateDispose;
    private readonly MethodInfo hasteDisposeCache;
    private readonly MethodInfo shrinkDisposeCache;
    private readonly FieldInfo hasteCache;
    private readonly FieldInfo hasteActive;
    private readonly FieldInfo shrinkCache;
    private readonly FieldInfo shrinkActive;

    internal ActiveBuffCacheHarness(Assembly magicka)
    {
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        hasteType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Haste",
            true);
        shrinkType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Shrink",
            true);
        playStateDispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        hasteDisposeCache = FindDisposeCache(hasteType);
        shrinkDisposeCache = FindDisposeCache(shrinkType);
        hasteCache = RequireListField(hasteType, "sCache");
        hasteActive = RequireListField(hasteType, "sActiveHastes");
        shrinkCache = RequireListField(shrinkType, "sCache");
        shrinkActive = RequireListField(shrinkType, "sActiveCache");
        if (playStateDispose == null || playStateDispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");
    }

    internal ScenarioResult InitializedDispose()
    {
        IList[] lists = PopulateLists();
        if (hasteDisposeCache != null && shrinkDisposeCache != null)
        {
            Invoke(hasteDisposeCache, null);
            Invoke(shrinkDisposeCache, null);
        }
        else
        {
            object playState = NewUninitialized(playStateType);
            RuntimeReflection.WriteField(playState, "mInitialized", true);
            try
            {
                Invoke(playStateDispose, playState);
            }
            catch (Exception)
            {
                // Cache cleanup runs before disposal reaches live game singletons.
            }
        }

        bool released = AllCountsEqual(lists, 0);
        return new ScenarioResult(
            released,
            Describe(lists),
            "haste_cache:0,haste_active:0,shrink_cache:0,shrink_active:0");
    }

    internal ScenarioResult UninitializedDispose()
    {
        IList[] lists = PopulateLists();
        object playState = NewUninitialized(playStateType);
        RuntimeReflection.WriteField(playState, "mInitialized", false);
        Invoke(playStateDispose, playState);

        bool preserved = AllCountsEqual(lists, 1);
        return new ScenarioResult(
            preserved,
            Describe(lists),
            "haste_cache:1,haste_active:1,shrink_cache:1,shrink_active:1");
    }

    private IList[] PopulateLists()
    {
        IList[] lists = new IList[]
        {
            NewList(hasteCache, hasteType),
            NewList(hasteActive, hasteType),
            NewList(shrinkCache, shrinkType),
            NewList(shrinkActive, shrinkType)
        };
        hasteCache.SetValue(null, lists[0]);
        hasteActive.SetValue(null, lists[1]);
        shrinkCache.SetValue(null, lists[2]);
        shrinkActive.SetValue(null, lists[3]);
        return lists;
    }

    private static IList NewList(FieldInfo field, Type elementType)
    {
        IList list = (IList)Activator.CreateInstance(field.FieldType);
        list.Add(NewUninitialized(elementType));
        return list;
    }

    private static bool AllCountsEqual(IList[] lists, int expected)
    {
        for (int index = 0; index < lists.Length; index++)
        {
            if (lists[index].Count != expected)
                return false;
        }
        return true;
    }

    private static string Describe(IList[] lists)
    {
        return "haste_cache:" + lists[0].Count +
            ",haste_active:" + lists[1].Count +
            ",shrink_cache:" + lists[2].Count +
            ",shrink_active:" + lists[3].Count;
    }

    private static MethodInfo FindDisposeCache(Type type)
    {
        return type.GetMethod(
            "DisposeCache",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
    }

    private static FieldInfo RequireListField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null || !typeof(IList).IsAssignableFrom(field.FieldType))
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static void Invoke(MethodInfo method, object target)
    {
        try
        {
            method.Invoke(target, new object[0]);
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
