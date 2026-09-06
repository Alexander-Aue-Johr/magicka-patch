using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AIStateMoveScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AIStateMoveHarness harness = new AIStateMoveHarness(magicka);
        report.Add("ai_move.enter_bodyless_target", harness.EnterWithBodylessTarget());
        report.Add("ai_move.enter_missing_target", harness.EnterWithMissingTarget());
        report.Add("ai_move.execute_bodyless_target", harness.ExecuteWithBodylessTarget());
        report.Add("ai_move.execute_missing_target", harness.ExecuteWithMissingTarget());
    }
}

internal sealed class AIStateMoveHarness
{
    private readonly Type agentType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type characterBodyType;
    private readonly Type damageableType;
    private readonly Type aiStateType;
    private readonly Type pathNodeType;
    private readonly Type playStateType;
    private readonly Type entityManagerType;
    private readonly Type entityType;
    private readonly Type shieldType;
    private readonly object moveState;
    private readonly object attackState;
    private readonly object meleeAbility;
    private readonly MethodInfo onEnter;
    private readonly MethodInfo onExecute;

    internal AIStateMoveHarness(Assembly magicka)
    {
        Type moveStateType = magicka.GetType("Magicka.AI.AgentStates.AIStateMove", true);
        Type attackStateType = magicka.GetType("Magicka.AI.AgentStates.AIStateAttack", true);
        agentType = magicka.GetType("Magicka.AI.Agent", true);
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        characterBodyType = magicka.GetType("Magicka.Physics.CharacterBody", true);
        damageableType = magicka.GetType("Magicka.GameLogic.Entities.IDamageable", true);
        aiStateType = magicka.GetType("Magicka.AI.AgentStates.IAIState", true);
        pathNodeType = magicka.GetType("Magicka.PathFinding.PathNode", true);
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        entityManagerType = magicka.GetType("Magicka.GameLogic.Entities.EntityManager", true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        shieldType = magicka.GetType("Magicka.GameLogic.Entities.Shield", true);
        moveState = moveStateType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
        attackState = attackStateType.GetProperty(
            "Instance",
            BindingFlags.Static | BindingFlags.Public).GetValue(null, null);
        meleeAbility = FormatterServices.GetUninitializedObject(
            magicka.GetType("Magicka.GameLogic.Entities.Abilities.Melee", true));
        onEnter = moveStateType.GetMethod("OnEnter", BindingFlags.Instance | BindingFlags.Public);
        onExecute = moveStateType.GetMethod("OnExecute", BindingFlags.Instance | BindingFlags.Public);
    }

    internal ScenarioResult EnterWithBodylessTarget()
    {
        return InvokeEnter(NewAgent(true, true));
    }

    internal ScenarioResult EnterWithMissingTarget()
    {
        return InvokeEnter(NewAgent(false, true));
    }

    internal ScenarioResult ExecuteWithBodylessTarget()
    {
        return InvokeExecute(NewAgent(true, false));
    }

    internal ScenarioResult ExecuteWithMissingTarget()
    {
        return InvokeExecute(NewAgent(false, false));
    }

    private ScenarioResult InvokeEnter(object agent)
    {
        try
        {
            onEnter.Invoke(moveState, new object[] { agent });
            return new ScenarioResult(true, "advanced_past_target", "advanced_past_target");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            string stack = inner.StackTrace ?? string.Empty;
            bool targetPositionRead = stack.IndexOf(
                "Entity.get_Position",
                StringComparison.Ordinal) >= 0;
            return new ScenarioResult(
                !targetPositionRead,
                targetPositionRead ? "target_position_read" : "advanced_past_target",
                "advanced_past_target");
        }
    }

    private ScenarioResult InvokeExecute(object agent)
    {
        try
        {
            onExecute.Invoke(moveState, new object[] { agent, 0f });
            int states = CollectionCount(RuntimeReflection.ReadField(agent, "mStates"));
            return new ScenarioResult(
                states == 1,
                "states:" + states,
                "states:1");
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, "states:1");
        }
    }

    private object NewAgent(bool includeTarget, bool includePlayState)
    {
        object owner = FormatterServices.GetUninitializedObject(nonPlayerCharacterType);
        GC.SuppressFinalize(owner);
        object body = FormatterServices.GetUninitializedObject(characterBodyType);
        RuntimeReflection.WriteField(body, "mIsTouchingGround", true);
        RuntimeReflection.WriteField(owner, "mBody", body);
        if (includePlayState)
            RuntimeReflection.WriteField(owner, "mPlayState", NewPlayState());

        object agent = FormatterServices.GetUninitializedObject(agentType);
        RuntimeReflection.WriteField(agent, "mOwner", owner);
        RuntimeReflection.WriteField(agent, "mPath", NewList(pathNodeType));
        RuntimeReflection.WriteField(
            agent,
            "mStates",
            NewStack(aiStateType, attackState, moveState));
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
        RuntimeReflection.WriteField(agent, "mNextAbility", meleeAbility);
        return agent;
    }

    private object NewPlayState()
    {
        Type entityListType = typeof(System.Collections.Generic.List<>).MakeGenericType(entityType);
        Array grid = Array.CreateInstance(entityListType, 16, 16);
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
                grid.SetValue(Activator.CreateInstance(entityListType), x, y);
        }

        object manager = FormatterServices.GetUninitializedObject(entityManagerType);
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

        object playState = FormatterServices.GetUninitializedObject(playStateType);
        RuntimeReflection.WriteField(playState, "mEntityManager", manager);
        return playState;
    }

    private static object NewStack(Type elementType, params object[] values)
    {
        object stack = Activator.CreateInstance(
            typeof(System.Collections.Generic.Stack<>).MakeGenericType(elementType));
        MethodInfo push = stack.GetType().GetMethod("Push");
        for (int index = 0; index < values.Length; index++)
            push.Invoke(stack, new object[] { values[index] });
        return stack;
    }

    private static object NewList(Type elementType, params object[] values)
    {
        object list = Activator.CreateInstance(
            typeof(System.Collections.Generic.List<>).MakeGenericType(elementType));
        MethodInfo add = list.GetType().GetMethod("Add");
        for (int index = 0; index < values.Length; index++)
            add.Invoke(list, new object[] { values[index] });
        return list;
    }

    private static int CollectionCount(object collection)
    {
        return (int)collection.GetType().GetProperty("Count").GetValue(collection, null);
    }
}
