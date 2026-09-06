using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class EntityManagerTransitionScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        EntityManagerTransitionHarness harness = new EntityManagerTransitionHarness(magicka);
        report.Add("entity_query.bodyless_entry", harness.QueryBodylessEntry());
        report.Add("entity_query.null_entry", harness.QueryNullEntry());
        report.Add("entity_query.empty_grid", harness.QueryEmptyGrid());
        report.Add("entity_clear.stale_grid", harness.ClearStaleGrid());
        report.Add("entity_clear.empty_grid", harness.ClearEmptyGrid());
    }
}

internal sealed class EntityManagerTransitionHarness
{
    private readonly Type managerType;
    private readonly Type entityType;
    private readonly Type characterType;
    private readonly Type shieldType;
    private readonly Type vectorType;
    private readonly Type entityListType;
    private readonly Type staticEntityListType;
    private readonly MethodInfo getEntities;
    private readonly MethodInfo clearAndStore;

    internal EntityManagerTransitionHarness(Assembly magicka)
    {
        managerType = magicka.GetType("Magicka.GameLogic.Entities.EntityManager", true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        characterType = magicka.GetType("Magicka.GameLogic.Entities.NonPlayerCharacter", true);
        shieldType = magicka.GetType("Magicka.GameLogic.Entities.Shield", true);
        entityListType = typeof(System.Collections.Generic.List<>).MakeGenericType(entityType);
        staticEntityListType = magicka.GetType("Magicka.StaticObjectList`1", true)
            .MakeGenericType(entityType);
        getEntities = FindMethod("GetEntities", 4);
        clearAndStore = FindMethod("ClearAndStore", 1);
        vectorType = getEntities.GetParameters()[0].ParameterType;
    }

    internal ScenarioResult QueryBodylessEntry()
    {
        object candidate = FormatterServices.GetUninitializedObject(characterType);
        GC.SuppressFinalize(candidate);
        return InvokeQuery(NewManager(candidate));
    }

    internal ScenarioResult QueryNullEntry()
    {
        return InvokeQuery(NewManager(DBNull.Value));
    }

    internal ScenarioResult QueryEmptyGrid()
    {
        return InvokeQuery(NewManager(null));
    }

    internal ScenarioResult ClearStaleGrid()
    {
        return InvokeClear(NewManager(DBNull.Value));
    }

    internal ScenarioResult ClearEmptyGrid()
    {
        return InvokeClear(NewManager(null));
    }

    private object NewManager(object candidateMarker)
    {
        Array grid = Array.CreateInstance(entityListType, 16, 16);
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
                grid.SetValue(Activator.CreateInstance(entityListType), x, y);
        }
        if (candidateMarker != null)
        {
            object candidate = candidateMarker == DBNull.Value ? null : candidateMarker;
            entityListType.GetMethod("Add").Invoke(
                grid.GetValue(0, 0),
                new object[] { candidate });
        }

        object manager = FormatterServices.GetUninitializedObject(managerType);
        RuntimeReflection.WriteField(manager, "mQuadGrid", grid);
        RuntimeReflection.WriteField(
            manager,
            "mQuaryLists",
            Activator.CreateInstance(
                typeof(System.Collections.Generic.Queue<>).MakeGenericType(entityListType)));
        RuntimeReflection.WriteField(
            manager,
            "mShields",
            Activator.CreateInstance(
                typeof(System.Collections.Generic.List<>).MakeGenericType(shieldType)));

        RuntimeReflection.WriteField(manager, "mEntities", Activator.CreateInstance(
            staticEntityListType,
            new object[] { 512 }));
        return manager;
    }

    private ScenarioResult InvokeQuery(object manager)
    {
        try
        {
            object result = getEntities.Invoke(
                manager,
                new object[] { Activator.CreateInstance(vectorType), 1f, false, false });
            int count = (int)result.GetType().GetProperty("Count").GetValue(result, null);
            return new ScenarioResult(count == 0, "count:" + count, "count:0");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "count:0");
        }
    }

    private ScenarioResult InvokeClear(object manager)
    {
        try
        {
            clearAndStore.Invoke(manager, new object[] { null });
            Array grid = (Array)RuntimeReflection.ReadField(manager, "mQuadGrid");
            object cell = grid.GetValue(0, 0);
            int count = (int)cell.GetType().GetProperty("Count").GetValue(cell, null);
            return new ScenarioResult(count == 0, "count:" + count, "count:0");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "count:0");
        }
    }

    private MethodInfo FindMethod(string name, int parameterCount)
    {
        return Array.Find(
            managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.Name == name &&
                method.GetParameters().Length == parameterCount);
    }
}
