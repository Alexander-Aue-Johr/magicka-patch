using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class CharacterNetworkGripScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        CharacterNetworkGripHarness harness =
            new CharacterNetworkGripHarness(magicka);
        report.Add("character_grip.missing_target", harness.MissingTarget());
        report.Add(
            "character_grip.actor_body_missing",
            harness.ActorBodyMissing());
        report.Add(
            "character_grip.target_body_missing",
            harness.TargetBodyMissing());
        report.Add(
            "character_grip.controller_missing",
            harness.ControllerMissing());
        report.Add("character_grip.non_grip", harness.NonGrip());
    }
}

internal sealed class CharacterNetworkGripHarness
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic;

    private readonly Type entityType;
    private readonly Type npcType;
    private readonly Type messageType;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo bodyField;
    private readonly FieldInfo entanglementField;
    private readonly FieldInfo grippedCharacterField;
    private readonly FieldInfo gripperField;
    private readonly FieldInfo gripTypeField;
    private readonly FieldInfo blockField;
    private readonly FieldInfo actionField;
    private readonly FieldInfo targetHandleField;
    private readonly FieldInfo firstIntegerField;
    private readonly FieldInfo secondIntegerField;
    private readonly FieldInfo thirdIntegerField;
    private readonly MethodInfo networkAction;

    internal CharacterNetworkGripHarness(Assembly magicka)
    {
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        Type characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        messageType = magicka.GetType(
            "Magicka.Network.CharacterActionMessage",
            true);

        RuntimeHelpers.RunClassConstructor(entityType.TypeHandle);
        RuntimeHelpers.RunClassConstructor(characterType.TypeHandle);
        instancesField = RuntimeReflection.RequireField(entityType, "mInstances");
        bodyField = RuntimeReflection.RequireField(characterType, "mBody");
        entanglementField = RuntimeReflection.RequireField(
            characterType,
            "mEntaglement");
        grippedCharacterField = RuntimeReflection.RequireField(
            characterType,
            "mGrippedCharacter");
        gripperField = RuntimeReflection.RequireField(characterType, "mGripper");
        gripTypeField = RuntimeReflection.RequireField(
            characterType,
            "mGripType");
        blockField = RuntimeReflection.RequireField(characterType, "mBlock");
        actionField = RuntimeReflection.RequireField(messageType, "Action");
        targetHandleField = RuntimeReflection.RequireField(
            messageType,
            "TargetHandle");
        firstIntegerField = RuntimeReflection.RequireField(messageType, "Param0I");
        secondIntegerField = RuntimeReflection.RequireField(messageType, "Param1I");
        thirdIntegerField = RuntimeReflection.RequireField(messageType, "Param2I");
        networkAction = characterType.GetMethod(
            "NetworkAction",
            InstanceMembers | BindingFlags.DeclaredOnly,
            null,
            new Type[] { messageType.MakeByRefType() },
            null);
        if (networkAction == null)
            throw new MissingMethodException(characterType.FullName, "NetworkAction");
    }

    internal ScenarioResult MissingTarget()
    {
        return InvalidGrip(false, false, "Hold", -1, false);
    }

    internal ScenarioResult ActorBodyMissing()
    {
        return InvalidGrip(true, false, "Hold", -1, true);
    }

    internal ScenarioResult TargetBodyMissing()
    {
        return InvalidGrip(false, true, "Pickup", -1, true);
    }

    internal ScenarioResult ControllerMissing()
    {
        return InvalidGrip(true, true, "Pickup", 0, true);
    }

    internal ScenarioResult NonGrip()
    {
        ResetInstances();
        object actor = NewCharacter();
        blockField.SetValue(actor, false);
        object message = Activator.CreateInstance(messageType);
        actionField.SetValue(
            message,
            Enum.Parse(actionField.FieldType, "Block"));
        firstIntegerField.SetValue(message, 1);
        Exception failure = Invoke(actor, message);
        bool applied = failure == null && (bool)blockField.GetValue(actor);
        return new ScenarioResult(
            applied,
            "exception:" + ExceptionName(failure) + ",state:" +
                (applied ? "applied" : "ignored"),
            "exception:none,state:applied");
    }

    private ScenarioResult InvalidGrip(
        bool actorBody,
        bool targetBody,
        string gripType,
        int jointIndex,
        bool addTarget)
    {
        IList instances = ResetInstances();
        object actor = NewCharacter();
        object target = addTarget ? NewCharacter() : null;
        if (actorBody)
            bodyField.SetValue(actor, Activator.CreateInstance(bodyField.FieldType));
        if (targetBody)
            bodyField.SetValue(target, Activator.CreateInstance(bodyField.FieldType));
        if (target != null)
            instances.Add(target);

        object initialGripType = gripTypeField.GetValue(actor);
        object message = Activator.CreateInstance(messageType);
        actionField.SetValue(
            message,
            Enum.Parse(actionField.FieldType, "Grip"));
        targetHandleField.SetValue(message, Convert.ToUInt16(0));
        firstIntegerField.SetValue(
            message,
            Convert.ToInt32(Enum.Parse(gripTypeField.FieldType, gripType)));
        secondIntegerField.SetValue(message, jointIndex);
        thirdIntegerField.SetValue(message, 3);

        Exception failure = Invoke(actor, message);
        bool unchanged =
            failure == null &&
            grippedCharacterField.GetValue(actor) == null &&
            (target == null || gripperField.GetValue(target) == null) &&
            Object.Equals(gripTypeField.GetValue(actor), initialGripType);
        return new ScenarioResult(
            unchanged,
            "exception:" + ExceptionName(failure) + ",state:" +
                (unchanged ? "rejected" : "changed"),
            "exception:none,state:rejected");
    }

    private object NewCharacter()
    {
        object character = FormatterServices.GetUninitializedObject(npcType);
        GC.SuppressFinalize(character);
        object entanglement = FormatterServices.GetUninitializedObject(
            entanglementField.FieldType);
        GC.SuppressFinalize(entanglement);
        entanglementField.SetValue(character, entanglement);
        return character;
    }

    private IList ResetInstances()
    {
        IList instances = instancesField.GetValue(null) as IList;
        if (instances == null)
            throw new InvalidOperationException("Entity instance list is unavailable.");
        instances.Clear();
        return instances;
    }

    private Exception Invoke(object actor, object message)
    {
        try
        {
            networkAction.Invoke(actor, new object[] { message });
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

    private static string ExceptionName(Exception exception)
    {
        return exception == null ? "none" : exception.GetType().FullName;
    }
}
