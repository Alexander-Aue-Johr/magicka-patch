using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PortalTeleportQueueScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        PortalTeleportQueueHarness harness = new PortalTeleportQueueHarness(magicka);
        report.Add("portal_queue.null_then_bodyless", harness.NullThenBodyless());
        report.Add("portal_queue.bodyless_then_null", harness.BodylessThenNull());
        report.Add("portal_queue.empty", harness.Empty());
    }
}

internal sealed class PortalTeleportQueueHarness
{
    private readonly Type portalEntityType;
    private readonly Type entityType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type bodyType;
    private readonly Type queueType;
    private readonly MethodInfo update;

    internal PortalTeleportQueueHarness(Assembly magicka)
    {
        portalEntityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal+PortalEntity",
            true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        queueType = typeof(System.Collections.Generic.Queue<>).MakeGenericType(entityType);
        update = Array.Find(
            portalEntityType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "Update" && method.GetParameters().Length == 2);
    }

    internal ScenarioResult NullThenBodyless()
    {
        return Invoke(new object[] { null, NewBodylessEntity() });
    }

    internal ScenarioResult BodylessThenNull()
    {
        return Invoke(new object[] { NewBodylessEntity(), null });
    }

    internal ScenarioResult Empty()
    {
        return Invoke(new object[0]);
    }

    private object NewBodylessEntity()
    {
        object entity = FormatterServices.GetUninitializedObject(nonPlayerCharacterType);
        GC.SuppressFinalize(entity);
        return entity;
    }

    private ScenarioResult Invoke(object[] entries)
    {
        object portal = FormatterServices.GetUninitializedObject(portalEntityType);
        GC.SuppressFinalize(portal);
        object body = Activator.CreateInstance(bodyType);
        object queue = Activator.CreateInstance(queueType);
        MethodInfo enqueue = queueType.GetMethod("Enqueue");
        for (int index = 0; index < entries.Length; index++)
            enqueue.Invoke(queue, new object[] { entries[index] });

        RuntimeReflection.WriteField(portal, "mBody", body);
        RuntimeReflection.WriteField(portal, "mTeleportQueue", queue);
        try
        {
            ParameterInfo[] parameters = update.GetParameters();
            update.Invoke(portal, new object[] {
                Activator.CreateInstance(parameters[0].ParameterType),
                0f
            });
        }
        catch (TargetInvocationException)
        {
        }

        int count = (int)queueType.GetProperty("Count").GetValue(queue, null);
        return new ScenarioResult(count == 0, "count:" + count, "count:0");
    }
}
