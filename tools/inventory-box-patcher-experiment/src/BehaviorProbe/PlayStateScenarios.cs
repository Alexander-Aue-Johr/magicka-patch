using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PlayStateScenarios
{
    private static readonly string[] ScenarioNames =
    {
        "play_state.ordinary_message",
        "play_state.other_action",
        "play_state.missing_spawn",
        "play_state.non_npc_spawn",
        "play_state.same_state_spawn",
        "play_state.foreign_state_spawn"
    };

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        if (magicka.GetType("Magicka.Network.WorldSyncMessage", false) == null)
        {
            for (int index = 0; index < ScenarioNames.Length; index++)
                report.AddNotApplicable(ScenarioNames[index], "WorldSyncMessage is not present.");
            return;
        }

        PlayStateHarness harness = new PlayStateHarness(magicka);
        report.Add(ScenarioNames[0], harness.OrdinaryMessageIsQueued());
        report.Add(ScenarioNames[1], harness.OtherActionIsQueued());
        report.Add(ScenarioNames[2], harness.MissingSpawnIsDropped());
        report.Add(ScenarioNames[3], harness.NonNpcSpawnIsDropped());
        report.Add(ScenarioNames[4], harness.SameStateSpawnIsQueued());
        report.Add(ScenarioNames[5], harness.ForeignStateSpawnIsDropped());
    }
}

internal sealed class PlayStateHarness
{
    private readonly Type playStateType;
    private readonly Type messageType;
    private readonly Type triggerType;
    private readonly Type entityType;
    private readonly Type nonPlayerCharacterType;
    private readonly Type nonNpcEntityType;
    private readonly MethodInfo addMessage;
    private readonly FieldInfo instances;

    internal PlayStateHarness(Assembly magicka)
    {
        playStateType = magicka.GetType("Magicka.GameLogic.GameStates.PlayState", true);
        messageType = magicka.GetType("Magicka.Network.WorldSyncMessage", true);
        triggerType = magicka.GetType("Magicka.Network.TriggerActionMessage", true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        nonPlayerCharacterType = magicka.GetType("Magicka.GameLogic.Entities.NonPlayerCharacter", true);
        nonNpcEntityType = magicka.GetType(
            "Magicka.GameLogic.Entities.SprayEntity",
            true);
        addMessage = playStateType.GetMethod("AddWorldSyncMessage");
        instances = RuntimeReflection.RequireField(entityType, "mInstances");
    }

    internal ScenarioResult OrdinaryMessageIsQueued()
    {
        object playState = CreatePlayState();
        object message = CreateMessage(FirstEnumValueOtherThan("MessageType", "Message"), null, 0);
        Add(playState, message);
        return QueueCountResult(playState, 1);
    }

    internal ScenarioResult OtherActionIsQueued()
    {
        object playState = CreatePlayState();
        object message = CreateMessage("Message", FirstEnumValueOtherThan("ActionType", "SpawnNPC"), 0);
        Add(playState, message);
        return QueueCountResult(playState, 1);
    }

    internal ScenarioResult MissingSpawnIsDropped()
    {
        ClearEntities();
        object playState = CreatePlayState();
        Add(playState, CreateMessage("Message", "SpawnNPC", 42));
        return QueueCountResult(playState, 0);
    }

    internal ScenarioResult SameStateSpawnIsQueued()
    {
        ClearEntities();
        object playState = CreatePlayState();
        RegisterNpc(playState);
        Add(playState, CreateMessage("Message", "SpawnNPC", 0));
        return QueueCountResult(playState, 1);
    }

    internal ScenarioResult NonNpcSpawnIsDropped()
    {
        ClearEntities();
        object playState = CreatePlayState();
        RegisterEntity(nonNpcEntityType, playState);
        Add(playState, CreateMessage("Message", "SpawnNPC", 0));
        return QueueCountResult(playState, 0);
    }

    internal ScenarioResult ForeignStateSpawnIsDropped()
    {
        ClearEntities();
        object playState = CreatePlayState();
        RegisterEntity(nonPlayerCharacterType, CreatePlayState());
        Add(playState, CreateMessage("Message", "SpawnNPC", 0));
        return QueueCountResult(playState, 0);
    }

    private object CreatePlayState()
    {
        object playState = FormatterServices.GetUninitializedObject(playStateType);
        FieldInfo queueField = RuntimeReflection.RequireField(playStateType, "mWorldSyncMessageQueue");
        Type queueType = typeof(Queue<>).MakeGenericType(messageType);
        queueField.SetValue(playState, Activator.CreateInstance(queueType));
        return playState;
    }

    private object CreateMessage(string messageName, string actionName, ushort handle)
    {
        object message = Activator.CreateInstance(messageType);
        FieldInfo messageTypeField = RuntimeReflection.RequireField(messageType, "MessageType");
        messageTypeField.SetValue(message, Enum.Parse(messageTypeField.FieldType, messageName));
        if (actionName == null)
            return message;

        object trigger = Activator.CreateInstance(triggerType);
        FieldInfo actionType = RuntimeReflection.RequireField(triggerType, "ActionType");
        actionType.SetValue(trigger, Enum.Parse(actionType.FieldType, actionName));
        RuntimeReflection.RequireField(triggerType, "Handle").SetValue(trigger, handle);
        RuntimeReflection.RequireField(messageType, "TriggerMessage").SetValue(message, trigger);
        return message;
    }

    private string FirstEnumValueOtherThan(string fieldName, string excluded)
    {
        Type owner = fieldName == "MessageType" ? messageType : triggerType;
        Type enumType = RuntimeReflection.RequireField(owner, fieldName).FieldType;
        string[] names = Enum.GetNames(enumType);
        for (int index = 0; index < names.Length; index++)
        {
            if (names[index] != excluded)
                return names[index];
        }
        throw new InvalidOperationException("No control value exists for " + enumType.FullName + ".");
    }

    private void Add(object playState, object message)
    {
        addMessage.Invoke(playState, new object[] { message });
    }

    private void RegisterNpc(object playState)
    {
        RegisterEntity(nonPlayerCharacterType, playState);
    }

    private void RegisterEntity(Type type, object playState)
    {
        object entity = FormatterServices.GetUninitializedObject(type);
        RuntimeReflection.RequireField(entityType, "mPlayState").SetValue(entity, playState);
        ((IList)instances.GetValue(null)).Add(entity);
    }

    private void ClearEntities()
    {
        ((IList)instances.GetValue(null)).Clear();
    }

    private ScenarioResult QueueCountResult(object playState, int expected)
    {
        object queue = RuntimeReflection.RequireField(
            playStateType,
            "mWorldSyncMessageQueue").GetValue(playState);
        int actual = (int)queue.GetType().GetProperty("Count").GetValue(queue, null);
        return new ScenarioResult(actual == expected, actual.ToString(), expected.ToString());
    }
}
