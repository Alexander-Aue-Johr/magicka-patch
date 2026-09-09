using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class CharacterTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        CharacterTeardownHarness harness = new CharacterTeardownHarness(
            magicka,
            runtimePatchEnabled);
        report.Add(
            "character_teardown.final_references",
            harness.ReleaseFinalReferences());
    }
}

internal sealed class CharacterTeardownHarness
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic;

    private readonly Type npcType;
    private readonly Type characterType;
    private readonly Type entityType;
    private readonly Type attachmentType;
    private readonly Type itemType;
    private readonly bool runtimePatchEnabled;
    private readonly FieldInfo controllerField;
    private readonly FieldInfo animationLoopedField;
    private readonly FieldInfo crossfadeFinishedField;
    private readonly EventInfo animationLoopedEvent;
    private readonly EventInfo crossfadeFinishedEvent;
    private readonly FieldInfo equipmentField;
    private readonly FieldInfo attachmentItemField;

    internal CharacterTeardownHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        attachmentType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Attachment",
            true);
        itemType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Item",
            true);
        this.runtimePatchEnabled = runtimePatchEnabled;

        for (Type current = npcType;
            current != null;
            current = current.BaseType)
            RuntimeHelpers.RunClassConstructor(current.TypeHandle);

        controllerField = RequireCharacterField("mAnimationController");
        Type controllerType = controllerField.FieldType;
        animationLoopedEvent = RequireEvent(
            controllerType,
            "AnimationLooped");
        crossfadeFinishedEvent = RequireEvent(
            controllerType,
            "CrossfadeFinished");
        animationLoopedField = RuntimeReflection.RequireField(
            controllerType,
            "AnimationLooped");
        crossfadeFinishedField = RuntimeReflection.RequireField(
            controllerType,
            "CrossfadeFinished");
        equipmentField = RequireCharacterField("mEquipment");
        attachmentItemField = attachmentType.GetField(
            "mItem",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (attachmentItemField == null)
            throw new MissingFieldException(attachmentType.FullName, "mItem");
    }

    internal ScenarioResult ReleaseFinalReferences()
    {
        PrepareStaticState();
        object npc = NewUninitialized(npcType);
        object related = NewUninitialized(npcType);
        PrepareNpcBase(npc);
        PrepareNpcBase(related);

        object controller = Activator.CreateInstance(controllerField.FieldType);
        Subscribe(controller, animationLoopedEvent, npc, "OnAnimationLooped");
        Subscribe(
            controller,
            crossfadeFinishedEvent,
            npc,
            "OnCrossfadeFinished");
        controllerField.SetValue(npc, controller);

        object attachment = NewUninitialized(attachmentType);
        object item = NewUninitialized(itemType);
        attachmentItemField.SetValue(attachment, item);
        Array equipment = Array.CreateInstance(attachmentType, 1);
        equipment.SetValue(attachment, 0);
        equipmentField.SetValue(npc, equipment);

        RuntimeReflection.WriteField(npc, "mGrippedCharacter", related);
        RuntimeReflection.WriteField(npc, "mGripper", related);
        RuntimeReflection.WriteField(related, "mGripper", npc);
        RuntimeReflection.WriteField(related, "mGrippedCharacter", npc);
        RuntimeReflection.WriteField(npc, "mFearedBy", related);
        RuntimeReflection.WriteField(npc, "mBloatKiller", related);
        RuntimeReflection.WriteField(npc, "mLastAttacker", related);
        RuntimeReflection.WriteField(npc, "mCharmOwner", related);

        Array summons = Array.CreateInstance(npcType, 1);
        summons.SetValue(related, 0);
        RuntimeReflection.WriteField(npc, "mCurrentSummons", summons);
        RuntimeReflection.WriteField(npc, "mNumCurrentSummons", 1);
        RuntimeReflection.WriteField(npc, "mNumCurrentUndeadSummons", 1);
        RuntimeReflection.WriteField(npc, "mNumCurrentFlamerSummons", 1);

        IList executed = SetList(npc, "mExecutedActions");
        executed.Add(true);
        IList dead = SetList(npc, "mDeadActions");
        dead.Add(true);
        IList gibs = SetList(npc, "mGibs");
        AddDefault(gibs);

        object template = NewUninitialized(
            RequireCharacterField("mTemplate").FieldType);
        object model = NewUninitialized(
            RequireCharacterField("mModel").FieldType);
        RuntimeReflection.WriteField(npc, "mTemplate", template);
        RuntimeReflection.WriteField(npc, "mModel", model);
        SetEmptyArray(npc, "mRenderData");
        SetEmptyArray(npc, "mAnimationClips");
        SetEmptyArray(npc, "mCurrentActions");
        SetEmptyArray(npc, "mResistances");
        SetEmptyArray(npc, "mAttachedSounds");

        Exception failure = runtimePatchEnabled
            ? InvokeRuntimeCleanup(npc)
            : InvokeManualDispose(npc);

        bool released =
            failure == null &&
            controllerField.GetValue(npc) == null &&
            !HasHandler(animationLoopedField, controller, npc) &&
            !HasHandler(crossfadeFinishedField, controller, npc) &&
            attachmentItemField.GetValue(attachment) == null &&
            RuntimeReflection.ReadField(npc, "mGrippedCharacter") == null &&
            RuntimeReflection.ReadField(npc, "mGripper") == null &&
            RuntimeReflection.ReadField(related, "mGripper") == null &&
            RuntimeReflection.ReadField(
                related,
                "mGrippedCharacter") == null &&
            RuntimeReflection.ReadField(npc, "mFearedBy") == null &&
            RuntimeReflection.ReadField(npc, "mBloatKiller") == null &&
            RuntimeReflection.ReadField(npc, "mLastAttacker") == null &&
            RuntimeReflection.ReadField(npc, "mCharmOwner") == null &&
            summons.GetValue(0) == null &&
            (int)RuntimeReflection.ReadField(
                npc,
                "mNumCurrentSummons") == 0 &&
            (int)RuntimeReflection.ReadField(
                npc,
                "mNumCurrentUndeadSummons") == 0 &&
            (int)RuntimeReflection.ReadField(
                npc,
                "mNumCurrentFlamerSummons") == 0 &&
            executed.Count == 0 &&
            dead.Count == 0 &&
            gibs.Count == 0 &&
            RuntimeReflection.ReadField(npc, "mTemplate") == null &&
            RuntimeReflection.ReadField(npc, "mModel") == null &&
            RuntimeReflection.ReadField(npc, "mRenderData") == null &&
            RuntimeReflection.ReadField(npc, "mAnimationClips") == null &&
            RuntimeReflection.ReadField(npc, "mCurrentActions") == null &&
            RuntimeReflection.ReadField(npc, "mResistances") == null &&
            RuntimeReflection.ReadField(npc, "mAttachedSounds") == null;

        return new ScenarioResult(
            released,
            "exception:" + ExceptionName(failure) + ",state:" +
                (released ? "released" : "retained"),
            "exception:none,state:released");
    }

    private void PrepareStaticState()
    {
        FieldInfo npcCache = RuntimeReflection.RequireField(npcType, "sCache");
        IList cache = npcCache.GetValue(null) as IList;
        if (cache == null)
        {
            cache = Activator.CreateInstance(npcCache.FieldType) as IList;
            npcCache.SetValue(null, cache);
        }
        else
        {
            cache.Clear();
        }

        FieldInfo unique = RuntimeReflection.RequireField(
            entityType,
            "mUniqueEntities");
        if (unique.GetValue(null) == null)
            unique.SetValue(null, Activator.CreateInstance(unique.FieldType));
    }

    private void PrepareNpcBase(object npc)
    {
        SetInactiveEffect(npc, "mSummonedEffect");
        SetEmptyArray(npc, "mStatusEffectCues");
        SetEmptyArray(npc, "mAttachedSoundCues");
        SetEmptyArray(npc, "mAttachedEffects");
        SetList(npc, "mAuras");
        SetList(npc, "mBuffs");
        SetList(npc, "mBuffEffects");
        SetList(npc, "mBuffDecals");
        RuntimeReflection.WriteField(npc, "mLastDamageIndex", -1);
    }

    private Exception InvokeRuntimeCleanup(object npc)
    {
        Type patch = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.CharacterTeardownPatch",
            false);
        MethodInfo cleanup = patch == null
            ? null
            : patch.GetMethod(
                "CleanupFinal",
                BindingFlags.Static | BindingFlags.Public);
        return cleanup == null
            ? new MissingMethodException("CharacterTeardownPatch.CleanupFinal")
            : Invoke(cleanup, null, new object[] { npc });
    }

    private Exception InvokeManualDispose(object npc)
    {
        MethodInfo dispose = npcType.GetMethod(
            "Dispose",
            InstanceMembers | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        return dispose == null
            ? new MissingMethodException(npcType.FullName, "Dispose")
            : Invoke(dispose, npc, new object[0]);
    }

    private FieldInfo RequireCharacterField(string name)
    {
        FieldInfo field = characterType.GetField(
            name,
            InstanceMembers | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(characterType.FullName, name);
        return field;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }

    private static IList SetList(object target, string name)
    {
        FieldInfo field = RuntimeReflection.RequireField(target.GetType(), name);
        IList list = Activator.CreateInstance(field.FieldType) as IList;
        if (list == null)
            throw new InvalidOperationException(name + " is not a list.");
        field.SetValue(target, list);
        return list;
    }

    private static void AddDefault(IList list)
    {
        Type element = list.GetType().GetGenericArguments()[0];
        list.Add(element.IsValueType
            ? Activator.CreateInstance(element)
            : NewUninitialized(element));
    }

    private static void SetEmptyArray(object target, string name)
    {
        FieldInfo field = RuntimeReflection.RequireField(target.GetType(), name);
        field.SetValue(
            target,
            Array.CreateInstance(field.FieldType.GetElementType(), 0));
    }

    private static void SetInactiveEffect(object target, string name)
    {
        FieldInfo field = RuntimeReflection.RequireField(target.GetType(), name);
        object effect = Activator.CreateInstance(field.FieldType);
        RuntimeReflection.WriteField(effect, "ID", -1);
        field.SetValue(target, effect);
    }

    private static EventInfo RequireEvent(Type type, string name)
    {
        EventInfo eventInfo = type.GetEvent(name, InstanceMembers);
        if (eventInfo == null)
            throw new MissingMemberException(type.FullName, name);
        return eventInfo;
    }

    private static void Subscribe(
        object source,
        EventInfo eventInfo,
        object target,
        string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            InstanceMembers);
        if (method == null)
            throw new MissingMethodException(target.GetType().FullName, methodName);
        eventInfo.AddEventHandler(
            source,
            Delegate.CreateDelegate(eventInfo.EventHandlerType, target, method));
    }

    private static bool HasHandler(
        FieldInfo eventField,
        object source,
        object target)
    {
        Delegate handlers = eventField.GetValue(source) as Delegate;
        if (handlers == null)
            return false;
        Delegate[] calls = handlers.GetInvocationList();
        for (int index = 0; index < calls.Length; index++)
        {
            if (Object.ReferenceEquals(calls[index].Target, target))
                return true;
        }
        return false;
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

    private static string ExceptionName(Exception exception)
    {
        return exception == null ? "none" : exception.GetType().FullName;
    }
}
