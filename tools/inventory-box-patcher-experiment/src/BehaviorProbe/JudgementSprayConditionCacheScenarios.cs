using System;
using System.Collections;
using System.Reflection;

internal static class JudgementSprayConditionCacheScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        JudgementSprayConditionCacheHarness harness =
            new JudgementSprayConditionCacheHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "judgement_spray.empty_condition_cache",
            harness.EmptyConditionCache());
        report.Add(
            "judgement_spray.cached_condition_identity",
            harness.CachedConditionIdentity());
    }
}

internal sealed class JudgementSprayConditionCacheHarness
{
    private readonly Type conditionCollectionType;
    private readonly Type queueType;
    private readonly MethodInfo enqueue;
    private readonly MethodInfo dequeue;
    private readonly MethodInfo manualTake;
    private readonly MethodInfo runtimeEnsure;

    internal JudgementSprayConditionCacheHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        conditionCollectionType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.ConditionCollection",
            true);
        queueType = typeof(System.Collections.Generic.Queue<>).MakeGenericType(
            conditionCollectionType);
        enqueue = RequireQueueMethod("Enqueue", new Type[] { conditionCollectionType });
        dequeue = RequireQueueMethod("Dequeue", Type.EmptyTypes);

        Type judgementSpray = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.JudgementSpray",
            true);
        manualTake = judgementSpray.GetMethod(
            "CommunityPatchTakeConditionCollectionLocked",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { queueType },
            null);

        runtimeEnsure = null;
        if (runtimePatchEnabled)
        {
            Type patchType = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
                .Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.JudgementSprayConditionCachePatch",
                    false);
            runtimeEnsure = patchType == null
                ? null
                : patchType.GetMethod(
                    "EnsureConditionCollection",
                    BindingFlags.Static | BindingFlags.Public);
        }
    }

    internal ScenarioResult EmptyConditionCache()
    {
        object queue = Activator.CreateInstance(queueType);
        try
        {
            object result = Take(queue);
            bool returned = result != null &&
                conditionCollectionType.IsInstanceOfType(result);
            return new ScenarioResult(
                returned,
                "returned:" + returned + ",count:" + Count(queue),
                "returned:True,count:0");
        }
        catch (InvalidOperationException)
        {
            return new ScenarioResult(
                false,
                "exception:InvalidOperationException",
                "returned:True,count:0");
        }
    }

    internal ScenarioResult CachedConditionIdentity()
    {
        object queue = Activator.CreateInstance(queueType);
        object cached = Activator.CreateInstance(conditionCollectionType);
        enqueue.Invoke(queue, new object[] { cached });
        object result = Take(queue);
        bool same = Object.ReferenceEquals(cached, result);
        return new ScenarioResult(
            same && Count(queue) == 0,
            "same:" + same + ",count:" + Count(queue),
            "same:True,count:0");
    }

    private object Take(object queue)
    {
        if (manualTake != null)
            return InvokeStatic(manualTake, queue);
        if (runtimeEnsure != null)
            InvokeStatic(runtimeEnsure, queue);
        return InvokeInstance(dequeue, queue);
    }

    private int Count(object queue)
    {
        return ((ICollection)queue).Count;
    }

    private MethodInfo RequireQueueMethod(string name, Type[] parameters)
    {
        MethodInfo method = queueType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            parameters,
            null);
        if (method == null)
            throw new MissingMethodException(queueType.FullName, name);
        return method;
    }

    private static object InvokeStatic(MethodInfo method, object argument)
    {
        try
        {
            return method.Invoke(null, new object[] { argument });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object InvokeInstance(MethodInfo method, object target)
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
}
