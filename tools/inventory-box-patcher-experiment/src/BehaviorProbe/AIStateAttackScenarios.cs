using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AIStateAttackScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AIStateAttackHarness harness = new AIStateAttackHarness(magicka);
        report.Add("ai_attack.bodyless_target", harness.ExecuteWithBodylessTarget());
        report.Add("ai_attack.missing_target", harness.ExecuteWithMissingTarget());
        report.Add("ai_attack.invalid_owner", harness.RejectInvalidOwner());
    }
}

internal sealed class AIStateAttackHarness
{
    private readonly Type stateType;
    private readonly Type agentType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type entanglementType;
    private readonly Type aiStateType;
    private readonly Type damageableType;
    private readonly MethodInfo onExecute;
    private readonly object state;

    internal AIStateAttackHarness(Assembly magicka)
    {
        stateType = magicka.GetType("Magicka.AI.AgentStates.AIStateAttack", true);
        agentType = magicka.GetType("Magicka.AI.Agent", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        entanglementType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entanglement",
            true);
        aiStateType = magicka.GetType("Magicka.AI.AgentStates.IAIState", true);
        damageableType = magicka.GetType(
            "Magicka.GameLogic.Entities.IDamageable",
            true);
        onExecute = stateType.GetMethod(
            "OnExecute",
            BindingFlags.Instance | BindingFlags.Public);
        state = stateType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
    }

    internal ScenarioResult ExecuteWithBodylessTarget()
    {
        object agent = NewAgent(true);
        try
        {
            Invoke(agent);
            int targetCount = CollectionCount(RuntimeReflection.ReadField(agent, "mTargets"));
            int stateCount = CollectionCount(RuntimeReflection.ReadField(agent, "mStates"));
            string actual = "targets:" + targetCount + ",states:" + stateCount;
            return new ScenarioResult(
                targetCount == 0 && stateCount == 1,
                actual,
                "targets:0,states:1");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "targets:0,states:1");
        }
    }

    internal ScenarioResult ExecuteWithMissingTarget()
    {
        object agent = NewAgent(false);
        try
        {
            Invoke(agent);
            int stateCount = CollectionCount(RuntimeReflection.ReadField(agent, "mStates"));
            return new ScenarioResult(
                stateCount == 1,
                "states:" + stateCount,
                "states:1");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "states:1");
        }
    }

    internal ScenarioResult RejectInvalidOwner()
    {
        try
        {
            onExecute.Invoke(state, new object[] { null, 0f });
            return new ScenarioResult(false, "completed", typeof(NotImplementedException).FullName);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(
                inner is NotImplementedException,
                inner.GetType().FullName,
                typeof(NotImplementedException).FullName);
        }
    }

    private object NewAgent(bool includeTarget)
    {
        object owner = FormatterServices.GetUninitializedObject(nonPlayerCharacterType);
        object entanglement = FormatterServices.GetUninitializedObject(entanglementType);
        RuntimeReflection.WriteField(owner, "mEntaglement", entanglement);

        object agent = FormatterServices.GetUninitializedObject(agentType);
        RuntimeReflection.WriteField(agent, "mOwner", owner);
        RuntimeReflection.WriteField(agent, "mStates", NewStack(aiStateType, state, state));
        RuntimeReflection.WriteField(agent, "mStateAge", NewList(typeof(float), 0f, 0f));

        object target = includeTarget
            ? FormatterServices.GetUninitializedObject(nonPlayerCharacterType)
            : null;
        RuntimeReflection.WriteField(
            agent,
            "mTargets",
            includeTarget ? NewStack(damageableType, target) : NewStack(damageableType));
        RuntimeReflection.WriteField(
            agent,
            "mTargetAges",
            includeTarget ? NewList(typeof(float), 0f) : NewList(typeof(float)));
        return agent;
    }

    private void Invoke(object agent)
    {
        onExecute.Invoke(state, new object[] { agent, 0f });
    }

    private static object NewStack(Type elementType, params object[] values)
    {
        object stack = Activator.CreateInstance(typeof(System.Collections.Generic.Stack<>).MakeGenericType(elementType));
        MethodInfo push = stack.GetType().GetMethod("Push");
        for (int index = 0; index < values.Length; index++)
        {
            push.Invoke(stack, new object[] { values[index] });
        }
        return stack;
    }

    private static object NewList(Type elementType, params object[] values)
    {
        object list = Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(elementType));
        MethodInfo add = list.GetType().GetMethod("Add");
        for (int index = 0; index < values.Length; index++)
        {
            add.Invoke(list, new object[] { values[index] });
        }
        return list;
    }

    private static int CollectionCount(object collection)
    {
        return (int)collection.GetType().GetProperty("Count").GetValue(collection, null);
    }
}
