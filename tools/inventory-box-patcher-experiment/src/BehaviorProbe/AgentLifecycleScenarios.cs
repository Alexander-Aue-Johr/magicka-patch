using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AgentLifecycleScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        AgentLifecycleHarness harness = new AgentLifecycleHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("agent_lifecycle.initialize", harness.Initialize());
        report.Add("agent_lifecycle.reset", harness.Reset());
        report.Add("agent_lifecycle.disable_dead_owner", harness.DisableDeadOwner());
        report.Add("agent_lifecycle.final_teardown", harness.FinalTeardown());
    }
}

internal sealed class AgentLifecycleHarness
{
    private const BindingFlags InstanceMethods =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Type agentType;
    private readonly Type npcType;
    private readonly Type templateType;
    private readonly Type aiManagerType;
    private readonly bool runtimePatchEnabled;
    private readonly ConstructorInfo agentConstructor;
    private readonly MethodInfo initializeMethod;
    private readonly MethodInfo resetMethod;
    private readonly MethodInfo disableMethod;
    private readonly FieldInfo aiManagerSingletonField;
    private readonly FieldInfo aiManagerAgentsField;

    internal AgentLifecycleHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        agentType = magicka.GetType("Magicka.AI.Agent", true);
        npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        aiManagerType = magicka.GetType("Magicka.AI.AIManager", true);
        this.runtimePatchEnabled = runtimePatchEnabled;

        agentConstructor = agentType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { npcType },
            null);
        initializeMethod = agentType.GetMethod(
            "Initialize",
            InstanceMethods,
            null,
            new Type[] { npcType, templateType },
            null);
        resetMethod = agentType.GetMethod(
            "Reset",
            InstanceMethods,
            null,
            Type.EmptyTypes,
            null);
        disableMethod = agentType.GetMethod(
            "Disable",
            InstanceMethods,
            null,
            Type.EmptyTypes,
            null);
        aiManagerSingletonField = RuntimeReflection.RequireField(
            aiManagerType,
            "mSingelton");
        aiManagerAgentsField = RuntimeReflection.RequireField(
            aiManagerType,
            "mAgents");
        if (agentConstructor == null || initializeMethod == null ||
            resetMethod == null || disableMethod == null)
            throw new MissingMemberException(
                "Agent lifecycle method contract is incomplete.");
    }

    internal ScenarioResult Initialize()
    {
        object originalOwner = NewNpc(false);
        object replacementOwner = NewNpc(false);
        object agent = NewAgent(originalOwner);
        SeedTransientState(agent, originalOwner);
        object template = FormatterServices.GetUninitializedObject(templateType);
        Exception failure = Invoke(
            initializeMethod,
            agent,
            new object[] { replacementOwner, template });
        bool released =
            Object.ReferenceEquals(
                RuntimeReflection.ReadField(agent, "mOwner"),
                replacementOwner);
        return Result(failure, released, "owner_refreshed");
    }

    internal ScenarioResult Reset()
    {
        object owner = NewNpc(false);
        object agent = NewAgent(owner);
        SeedTransientState(agent, owner);
        Exception failure = Invoke(resetMethod, agent, new object[0]);
        return Result(
            failure,
            ReusableStateReleased(agent),
            "reusable_state_released");
    }

    internal ScenarioResult DisableDeadOwner()
    {
        object previousManager = aiManagerSingletonField.GetValue(null);
        try
        {
            object owner = NewNpc(true);
            object agent = NewAgent(owner);
            SeedTransientState(agent, owner);
            object manager = Activator.CreateInstance(aiManagerType, true);
            IList agents = aiManagerAgentsField.GetValue(manager) as IList;
            if (agents == null)
                throw new InvalidOperationException("AI agent list is unavailable.");
            agents.Add(agent);
            aiManagerSingletonField.SetValue(null, manager);

            Exception failure = Invoke(disableMethod, agent, new object[0]);
            bool released =
                !agents.Contains(agent) &&
                DeadOwnerStateReleased(agent);
            return Result(failure, released, "dead_owner_state_released");
        }
        finally
        {
            aiManagerSingletonField.SetValue(null, previousManager);
        }
    }

    internal ScenarioResult FinalTeardown()
    {
        object previousManager = aiManagerSingletonField.GetValue(null);
        try
        {
            object owner = NewNpc(false);
            object agent = NewAgent(owner);
            SeedTransientState(agent, owner);
            SeedFinalCollections(agent, owner);
            object manager = Activator.CreateInstance(aiManagerType, true);
            IList agents = aiManagerAgentsField.GetValue(manager) as IList;
            agents.Add(agent);
            aiManagerSingletonField.SetValue(null, manager);

            Exception failure;
            if (runtimePatchEnabled)
            {
                Type patchType = typeof(
                    Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.AgentLifecyclePatch",
                    false);
                MethodInfo cleanup = patchType == null
                    ? null
                    : patchType.GetMethod(
                        "CleanupFinal",
                        BindingFlags.Static | BindingFlags.Public);
                failure = cleanup == null
                    ? new MissingMethodException(
                        "AgentLifecyclePatch.CleanupFinal")
                    : Invoke(cleanup, null, new object[] { agent });
            }
            else
            {
                MethodInfo dispose = agentType.GetMethod(
                    "Dispose",
                    InstanceMethods,
                    null,
                    Type.EmptyTypes,
                    null);
                failure = dispose == null
                    ? new MissingMethodException(agentType.FullName, "Dispose")
                    : Invoke(dispose, agent, new object[0]);
            }

            bool released =
                !agents.Contains(agent) &&
                RuntimeReflection.ReadField(agent, "mOwner") == null &&
                RuntimeReflection.ReadField(agent, "mLeader") == null &&
                RuntimeReflection.ReadField(agent, "mEvents") == null &&
                RuntimeReflection.ReadField(agent, "mNextAbility") == null &&
                RuntimeReflection.ReadField(agent, "mBusyAbility") == null &&
                RuntimeReflection.ReadField(agent, "mLastSpellAbility") == null &&
                RuntimeReflection.ReadField(agent, "mSpellRecovery") == null &&
                RuntimeReflection.ReadField(agent, "mPriorityTarget") == null &&
                RuntimeReflection.ReadField(agent, "mLastTarget") == null &&
                CollectionIsEmpty(agent, "mTargets") &&
                CollectionIsEmpty(agent, "mTargetAges") &&
                CollectionIsEmpty(agent, "mBeenAttackedBy") &&
                CollectionIsEmpty(agent, "mPath") &&
                CollectionIsEmpty(agent, "mStates") &&
                CollectionIsEmpty(agent, "mStateAge") &&
                ArrayIsClear(agent, "mFuzzySortEntities") &&
                ArrayIsClear(agent, "mFuzzySortAbilities");
            return Result(failure, released, "all_references_released");
        }
        finally
        {
            aiManagerSingletonField.SetValue(null, previousManager);
        }
    }

    private object NewAgent(object owner)
    {
        return agentConstructor.Invoke(new object[] { owner });
    }

    private object NewNpc(bool dead)
    {
        object npc = FormatterServices.GetUninitializedObject(npcType);
        GC.SuppressFinalize(npc);
        RuntimeReflection.WriteField(npc, "mDead", dead);
        return npc;
    }

    private static void SeedTransientState(object agent, object target)
    {
        object ability = RuntimeReflection.ReadField(agent, "mSpellRecovery");
        RuntimeReflection.WriteField(agent, "mLeader", agent);
        RuntimeReflection.WriteField(agent, "mLeaderAge", 12f);
        RuntimeReflection.WriteField(agent, "mLastTarget", target);
        RuntimeReflection.WriteField(agent, "mPriorityTarget", target);
        RuntimeReflection.WriteField(agent, "mLastSpellAbility", ability);
        RuntimeReflection.WriteField(agent, "mNextAbility", ability);
        RuntimeReflection.WriteField(agent, "mBusyAbility", ability);
        RuntimeReflection.WriteField(
            agent,
            "mEvents",
            Array.CreateInstance(
                RuntimeReflection.RequireField(
                    agent.GetType(),
                    "mEvents").FieldType.GetElementType(),
                1));
        RuntimeReflection.WriteField(agent, "mEventIndex", 3);
        RuntimeReflection.WriteField(agent, "mCurrentEventDelay", 4f);
        RuntimeReflection.WriteField(agent, "mLoopEvents", true);
        SetArrayItem(agent, "mFuzzySortEntities", target);
        SetArrayItem(agent, "mFuzzySortAbilities", ability);
    }

    private static void SeedFinalCollections(object agent, object target)
    {
        Push(RuntimeReflection.ReadField(agent, "mTargets"), target);
        Add(RuntimeReflection.ReadField(agent, "mTargetAges"), 1f);
        Add(RuntimeReflection.ReadField(agent, "mPath"), null);
    }

    private static bool ReusableStateReleased(object agent)
    {
        return RuntimeReflection.ReadField(agent, "mLeader") == null &&
            Convert.ToSingle(
                RuntimeReflection.ReadField(agent, "mLeaderAge")) == 0f &&
            RuntimeReflection.ReadField(agent, "mLastTarget") == null &&
            RuntimeReflection.ReadField(agent, "mLastSpellAbility") == null &&
            RuntimeReflection.ReadField(agent, "mEvents") == null &&
            Convert.ToInt32(
                RuntimeReflection.ReadField(agent, "mEventIndex")) == 0 &&
            Convert.ToSingle(
                RuntimeReflection.ReadField(agent, "mCurrentEventDelay")) == 0f &&
            !(bool)RuntimeReflection.ReadField(agent, "mLoopEvents") &&
            ArrayIsClear(agent, "mFuzzySortEntities") &&
            ArrayIsClear(agent, "mFuzzySortAbilities");
    }

    private static bool DeadOwnerStateReleased(object agent)
    {
        return CollectionIsEmpty(agent, "mTargets") &&
            CollectionIsEmpty(agent, "mTargetAges") &&
            RuntimeReflection.ReadField(agent, "mNextAbility") == null &&
            RuntimeReflection.ReadField(agent, "mBusyAbility") == null &&
            RuntimeReflection.ReadField(agent, "mLastSpellAbility") == null &&
            RuntimeReflection.ReadField(agent, "mPriorityTarget") == null &&
            RuntimeReflection.ReadField(agent, "mLastTarget") == null &&
            RuntimeReflection.ReadField(agent, "mLeader") == null &&
            Convert.ToSingle(
                RuntimeReflection.ReadField(agent, "mLeaderAge")) == 0f &&
            ArrayIsClear(agent, "mFuzzySortEntities") &&
            ArrayIsClear(agent, "mFuzzySortAbilities");
    }

    private static bool ArrayIsClear(object target, string fieldName)
    {
        Array array = RuntimeReflection.ReadField(target, fieldName) as Array;
        if (array == null)
            return false;
        for (int index = 0; index < array.Length; index++)
        {
            if (array.GetValue(index) != null)
                return false;
        }
        return true;
    }

    private static bool CollectionIsEmpty(object target, string fieldName)
    {
        object collection = RuntimeReflection.ReadField(target, fieldName);
        PropertyInfo count = collection.GetType().GetProperty("Count");
        return count != null && (int)count.GetValue(collection, null) == 0;
    }

    private static void SetArrayItem(
        object target,
        string fieldName,
        object value)
    {
        Array array = (Array)RuntimeReflection.ReadField(target, fieldName);
        array.SetValue(value, 0);
    }

    private static void Push(object collection, object value)
    {
        collection.GetType().GetMethod("Push").Invoke(
            collection,
            new object[] { value });
    }

    private static void Add(object collection, object value)
    {
        collection.GetType().GetMethod("Add").Invoke(
            collection,
            new object[] { value });
    }

    private static ScenarioResult Result(
        Exception failure,
        bool released,
        string success)
    {
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && released,
            "exception:" + exception + ",state:" +
                (released ? success : "retained"),
            "exception:none,state:" + success);
    }

    private static Exception Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException ?? exception;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
