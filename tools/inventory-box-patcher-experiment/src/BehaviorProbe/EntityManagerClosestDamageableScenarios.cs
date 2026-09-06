using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class EntityManagerClosestDamageableScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        EntityManagerClosestDamageableHarness harness =
            new EntityManagerClosestDamageableHarness(magicka);
        report.Add("closest_damageable.bodyless_candidate", harness.BodylessCandidate());
        report.Add("closest_damageable.null_candidate", harness.NullCandidate());
        report.Add("closest_damageable.empty_grid", harness.EmptyGrid());
    }
}

internal sealed class EntityManagerClosestDamageableHarness
{
    private readonly Type managerType;
    private readonly Type entityType;
    private readonly Type characterType;
    private readonly Type shieldType;
    private readonly Type vectorType;
    private readonly MethodInfo getClosest;

    internal EntityManagerClosestDamageableHarness(Assembly magicka)
    {
        managerType = magicka.GetType("Magicka.GameLogic.Entities.EntityManager", true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        characterType = magicka.GetType("Magicka.GameLogic.Entities.NonPlayerCharacter", true);
        shieldType = magicka.GetType("Magicka.GameLogic.Entities.Shield", true);
        getClosest = Array.Find(
            managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method => method.Name == "GetClosestIDamageable" &&
                method.GetParameters().Length == 4);
        vectorType = getClosest.GetParameters()[1].ParameterType;
    }

    internal ScenarioResult BodylessCandidate()
    {
        object candidate = FormatterServices.GetUninitializedObject(characterType);
        GC.SuppressFinalize(candidate);
        return Invoke(NewManager(candidate));
    }

    internal ScenarioResult NullCandidate()
    {
        return Invoke(NewManager(DBNull.Value));
    }

    internal ScenarioResult EmptyGrid()
    {
        return Invoke(NewManager(null));
    }

    private object NewManager(object candidateMarker)
    {
        Type entityListType = typeof(System.Collections.Generic.List<>)
            .MakeGenericType(entityType);
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
        Type shieldListType = typeof(System.Collections.Generic.List<>)
            .MakeGenericType(shieldType);
        RuntimeReflection.WriteField(
            manager,
            "mShields",
            Activator.CreateInstance(shieldListType));
        return manager;
    }

    private ScenarioResult Invoke(object manager)
    {
        try
        {
            object center = Activator.CreateInstance(vectorType);
            object result = getClosest.Invoke(
                manager,
                new object[] { null, center, 1f, false });
            string actual = result == null ? "null" : result.GetType().FullName;
            return new ScenarioResult(result == null, actual, "null");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "null");
        }
    }
}
