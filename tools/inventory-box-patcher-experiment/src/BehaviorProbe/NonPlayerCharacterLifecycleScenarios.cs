using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class NonPlayerCharacterLifecycleScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        NonPlayerCharacterLifecycleHarness harness =
            new NonPlayerCharacterLifecycleHarness(magicka);
        report.Add(
            "npc_lifecycle.deinitialize",
            harness.Deinitialize());
    }
}

internal sealed class NonPlayerCharacterLifecycleHarness
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic;

    private readonly Type npcType;
    private readonly Type agentType;
    private readonly FieldInfo agentField;
    private readonly FieldInfo controllerField;
    private readonly FieldInfo clipsField;
    private readonly FieldInfo gibsField;
    private readonly FieldInfo modelField;
    private readonly FieldInfo templateField;
    private readonly FieldInfo bodyField;
    private readonly ConstructorInfo characterBodyConstructor;
    private readonly MethodInfo deinitialize;
    private readonly ConstructorInfo agentConstructor;
    private readonly EventInfo animationLoopedEvent;
    private readonly EventInfo crossfadeFinishedEvent;
    private readonly FieldInfo animationLoopedField;
    private readonly FieldInfo crossfadeFinishedField;

    internal NonPlayerCharacterLifecycleHarness(Assembly magicka)
    {
        npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        for (Type current = npcType;
            current != null;
            current = current.BaseType)
            RuntimeHelpers.RunClassConstructor(current.TypeHandle);

        agentType = magicka.GetType("Magicka.AI.Agent", true);
        Type character = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        agentField = RequireDeclaredField(npcType, "mAI");
        controllerField = RequireDeclaredField(
            character,
            "mAnimationController");
        clipsField = RequireDeclaredField(character, "mAnimationClips");
        gibsField = RequireDeclaredField(character, "mGibs");
        modelField = RequireDeclaredField(character, "mModel");
        templateField = RequireDeclaredField(character, "mTemplate");
        bodyField = RuntimeReflection.RequireField(npcType, "mBody");
        PropertyInfo characterBody = character.GetProperty(
            "CharacterBody",
            InstanceMembers);
        characterBodyConstructor = characterBody == null
            ? null
            : characterBody.PropertyType.GetConstructor(
                InstanceMembers,
                null,
                new Type[] { character },
                null);

        deinitialize = npcType.GetMethod(
            "Deinitialize",
            InstanceMembers | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        agentConstructor = agentType.GetConstructor(
            InstanceMembers,
            null,
            new Type[] { npcType },
            null);
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
        if (deinitialize == null || agentConstructor == null ||
            characterBodyConstructor == null)
            throw new MissingMethodException(
                npcType.FullName,
                "reusable lifecycle contract");
    }

    internal ScenarioResult Deinitialize()
    {
        ResetCaches();
        object npc = FormatterServices.GetUninitializedObject(npcType);
        GC.SuppressFinalize(npc);
        PrepareBaseState(npc);

        object agent = agentConstructor.Invoke(new object[] { npc });
        agentField.SetValue(npc, agent);
        MethodInfo resetAgent = agentType.GetMethod(
            "Reset",
            InstanceMembers,
            null,
            Type.EmptyTypes,
            null);
        if (resetAgent == null)
            throw new MissingMethodException(agentType.FullName, "Reset");
        resetAgent.Invoke(agent, new object[0]);
        RuntimeReflection.WriteField(agent, "mPriorityTarget", npc);

        object oldController = Activator.CreateInstance(
            controllerField.FieldType);
        Subscribe(oldController, animationLoopedEvent, npc, "OnAnimationLooped");
        Subscribe(
            oldController,
            crossfadeFinishedEvent,
            npc,
            "OnCrossfadeFinished");
        controllerField.SetValue(npc, oldController);

        clipsField.SetValue(
            npc,
            Array.CreateInstance(clipsField.FieldType.GetElementType(), 1));
        IList gibs = Activator.CreateInstance(gibsField.FieldType) as IList;
        if (gibs == null)
            throw new InvalidOperationException("Character gib list is unavailable.");
        Type gibType = gibsField.FieldType.GetGenericArguments()[0];
        gibs.Add(FormatterServices.GetUninitializedObject(gibType));
        gibsField.SetValue(npc, gibs);
        object model = FormatterServices.GetUninitializedObject(
            modelField.FieldType);
        GC.SuppressFinalize(model);
        modelField.SetValue(npc, model);
        templateField.SetValue(
            npc,
            FormatterServices.GetUninitializedObject(templateField.FieldType));

        Exception failure = Invoke(deinitialize, npc);
        object newController = controllerField.GetValue(npc);
        bool released =
            failure == null &&
            RuntimeReflection.ReadField(agent, "mPriorityTarget") == null &&
            clipsField.GetValue(npc) == null &&
            gibs.Count == 0 &&
            modelField.GetValue(npc) == null &&
            templateField.GetValue(npc) == null &&
            newController != null &&
            !Object.ReferenceEquals(oldController, newController) &&
            !HasHandler(animationLoopedField, oldController, npc) &&
            !HasHandler(crossfadeFinishedField, oldController, npc) &&
            HasSingleHandler(animationLoopedField, newController, npc) &&
            HasSingleHandler(crossfadeFinishedField, newController, npc);

        return new ScenarioResult(
            released,
            "exception:" + ExceptionName(failure) +
                ",target:" + ExceptionTarget(failure) +
                ",stack:" + ExceptionStack(failure) +
                ",state:" + (released ? "released" : "retained"),
            "exception:none,target:none,stack:none,state:released");
    }

    private void PrepareBaseState(object npc)
    {
        SetCollection(npc, "mHitList");
        RuntimeReflection.WriteField(npc, "mLastDamageIndex", -1);
        SetEmptyArray(npc, "mEquipment");
        SetEmptyArray(npc, "mStatusEffectCues");
        SetEmptyArray(npc, "mAttachedSoundCues");
        SetEmptyArray(npc, "mAttachedEffects");
        SetCollection(npc, "mAuras");
        SetCollection(npc, "mBuffs");
        SetCollection(npc, "mBuffEffects");
        SetCollection(npc, "mBuffDecals");
        SetEmptyArray(npc, "mCurrentActions");
        SetCollection(npc, "mDeadActions");
        object body = characterBodyConstructor.Invoke(new object[] { npc });
        bodyField.SetValue(npc, body);
        SetInactiveEffectFields(npc);
    }

    private void ResetCaches()
    {
        FieldInfo npcCache = RequireDeclaredField(npcType, "sCache");
        ResetList(npcCache);

        Type entity = npcType.BaseType.BaseType;
        FieldInfo uniqueEntities = RequireDeclaredField(
            entity,
            "mUniqueEntities");
        if (uniqueEntities.GetValue(null) == null)
            uniqueEntities.SetValue(
                null,
                Activator.CreateInstance(uniqueEntities.FieldType));
    }

    private static void ResetList(FieldInfo field)
    {
        IList list = field.GetValue(null) as IList;
        if (list == null)
        {
            list = Activator.CreateInstance(field.FieldType) as IList;
            field.SetValue(null, list);
        }
        else
        {
            list.Clear();
        }
    }

    private static void SetCollection(object target, string name)
    {
        FieldInfo field = RuntimeReflection.RequireField(
            target.GetType(),
            name);
        ConstructorInfo constructor = field.FieldType.GetConstructor(
            InstanceMembers,
            null,
            Type.EmptyTypes,
            null);
        object value;
        if (constructor != null)
        {
            value = constructor.Invoke(null);
        }
        else
        {
            constructor = field.FieldType.GetConstructor(
                InstanceMembers,
                null,
                new Type[] { typeof(int) },
                null);
            if (constructor == null)
                throw new MissingMethodException(
                    field.FieldType.FullName,
                    ".ctor");
            value = constructor.Invoke(new object[] { 32 });
        }
        field.SetValue(target, value);
    }

    private static void SetEmptyArray(object target, string name)
    {
        FieldInfo field = RuntimeReflection.RequireField(
            target.GetType(),
            name);
        Type element = field.FieldType.GetElementType();
        if (element == null)
            throw new MissingMemberException(target.GetType().FullName, name);
        field.SetValue(target, Array.CreateInstance(element, 0));
    }

    private static void SetInactiveEffectFields(object target)
    {
        for (Type current = target.GetType();
            current != null;
            current = current.BaseType)
        {
            FieldInfo[] fields = current.GetFields(
                InstanceMembers | BindingFlags.DeclaredOnly);
            for (int index = 0; index < fields.Length; index++)
            {
                FieldInfo field = fields[index];
                if (field.FieldType.FullName !=
                    "Magicka.Graphics.VisualEffectReference")
                    continue;
                object effect = Activator.CreateInstance(field.FieldType);
                RuntimeReflection.WriteField(effect, "ID", -1);
                field.SetValue(target, effect);
            }
        }
    }

    private static void Subscribe(
        object controller,
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
            controller,
            Delegate.CreateDelegate(eventInfo.EventHandlerType, target, method));
    }

    private static bool HasHandler(
        FieldInfo eventField,
        object source,
        object expectedTarget)
    {
        Delegate handlers = eventField.GetValue(source) as Delegate;
        if (handlers == null)
            return false;
        Delegate[] calls = handlers.GetInvocationList();
        for (int index = 0; index < calls.Length; index++)
        {
            if (Object.ReferenceEquals(calls[index].Target, expectedTarget))
                return true;
        }
        return false;
    }

    private static bool HasSingleHandler(
        FieldInfo eventField,
        object source,
        object expectedTarget)
    {
        Delegate handlers = eventField.GetValue(source) as Delegate;
        if (handlers == null)
            return false;
        Delegate[] calls = handlers.GetInvocationList();
        return calls.Length == 1 &&
            Object.ReferenceEquals(calls[0].Target, expectedTarget);
    }

    private static FieldInfo RequireDeclaredField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static EventInfo RequireEvent(Type type, string name)
    {
        EventInfo eventInfo = type.GetEvent(
            name,
            InstanceMembers);
        if (eventInfo == null)
            throw new MissingMemberException(type.FullName, name);
        return eventInfo;
    }

    private static Exception Invoke(MethodInfo method, object target)
    {
        try
        {
            method.Invoke(target, new object[0]);
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

    private static string ExceptionTarget(Exception exception)
    {
        if (exception == null || exception.TargetSite == null)
            return "none";
        return exception.TargetSite.DeclaringType.FullName + "." +
            exception.TargetSite.Name;
    }

    private static string ExceptionStack(Exception exception)
    {
        if (exception == null || String.IsNullOrEmpty(exception.StackTrace))
            return "none";
        return exception.StackTrace.Replace('\r', ' ').Replace('\n', '>');
    }
}
