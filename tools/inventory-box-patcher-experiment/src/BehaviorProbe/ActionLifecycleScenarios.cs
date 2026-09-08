using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ActionLifecycleScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        ActionLifecycleHarness harness = new ActionLifecycleHarness(magicka);
        report.Add(
            "action_lifecycle.clear_references",
            harness.ClearReferences());
        report.Add(
            "action_lifecycle.state_reset_tag",
            harness.StateResetTag());
        report.Add(
            "action_lifecycle.empty_clear",
            harness.EmptyClear());
    }
}

internal sealed class ActionLifecycleHarness
{
    private readonly Type actionType;
    private readonly Type concreteActionType;
    private readonly Type stateType;
    private readonly Type triggerType;
    private readonly Type sceneType;
    private readonly FieldInfo instancesField;
    private readonly FieldInfo queueField;
    private readonly FieldInfo delayField;
    private readonly FieldInfo triggerField;
    private readonly FieldInfo sceneField;
    private readonly PropertyInfo tagProperty;
    private readonly MethodInfo clearInstances;
    private readonly MethodInfo stateReset;

    internal ActionLifecycleHarness(Assembly magicka)
    {
        actionType = magicka.GetType(
            "Magicka.Levels.Triggers.Actions.Action",
            true);
        concreteActionType = magicka.GetType(
            "Magicka.Levels.Triggers.Actions.SetDialogHint",
            true);
        stateType = actionType.GetNestedType(
            "State",
            BindingFlags.Public | BindingFlags.NonPublic);
        triggerType = magicka.GetType(
            "Magicka.Levels.Triggers.Trigger",
            true);
        sceneType = magicka.GetType("Magicka.Levels.GameScene", true);
        if (stateType == null)
            throw new TypeLoadException(actionType.FullName + "+State");

        instancesField = RuntimeReflection.RequireField(
            actionType,
            "sInstances");
        queueField = RuntimeReflection.RequireField(actionType, "mQueue");
        delayField = RuntimeReflection.RequireField(
            actionType,
            "mDelayCountdown");
        triggerField = RuntimeReflection.RequireField(
            actionType,
            "mTrigger");
        sceneField = RuntimeReflection.RequireField(actionType, "mScene");
        tagProperty = actionType.GetProperty(
            "Tag",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (tagProperty == null || !tagProperty.CanRead ||
            !tagProperty.CanWrite)
            throw new MissingMemberException(actionType.FullName, "Tag");

        clearInstances = actionType.GetMethod(
            "ClearInstances",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        stateReset = stateType.GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { actionType },
            null);
        if (clearInstances == null)
            throw new MissingMethodException(
                actionType.FullName,
                "ClearInstances");
        if (stateReset == null)
            throw new MissingMethodException(stateType.FullName, "Reset");
    }

    internal ScenarioResult ClearReferences()
    {
        object action = NewUninitialized(concreteActionType);
        object trigger = NewUninitialized(triggerType);
        object scene = NewUninitialized(sceneType);
        object tag = new object();
        queueField.SetValue(action, 3);
        delayField.SetValue(action, 9f);
        triggerField.SetValue(action, trigger);
        sceneField.SetValue(action, scene);
        tagProperty.SetValue(action, tag, null);
        IList instances = NewActionList();
        instances.Add(action);
        instances.Add(null);
        instancesField.SetValue(null, instances);

        Exception failure = Invoke(clearInstances, null, new object[0]);
        bool passed = failure == null && instances.Count == 0 &&
            (int)queueField.GetValue(action) == 0 &&
            triggerField.GetValue(action) == null &&
            sceneField.GetValue(action) == null &&
            Object.ReferenceEquals(tagProperty.GetValue(action, null), tag);
        string actual = "exception:" + ExceptionName(failure) +
            ",count:" + instances.Count +
            ",queue:" + queueField.GetValue(action) +
            ",trigger:" + NullState(triggerField.GetValue(action)) +
            ",scene:" + NullState(sceneField.GetValue(action)) +
            ",tag:" +
                (Object.ReferenceEquals(tagProperty.GetValue(action, null), tag)
                    ? "preserved"
                    : "changed");
        return new ScenarioResult(
            passed,
            actual,
            "exception:none,count:0,queue:0,trigger:null,scene:null,tag:preserved");
    }

    internal ScenarioResult StateResetTag()
    {
        object action = NewUninitialized(concreteActionType);
        object tag = new object();
        queueField.SetValue(action, 4);
        delayField.SetValue(action, 7f);
        tagProperty.SetValue(action, tag, null);
        object state = Activator.CreateInstance(
            stateType,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new object[] { action },
            null);

        Exception failure = Invoke(
            stateReset,
            state,
            new object[] { action });
        bool passed = failure == null &&
            (int)queueField.GetValue(action) == 0 &&
            (float)delayField.GetValue(action) == 0f &&
            tagProperty.GetValue(action, null) == null;
        string actual = "exception:" + ExceptionName(failure) +
            ",queue:" + queueField.GetValue(action) +
            ",delay:" + delayField.GetValue(action) +
            ",tag:" + NullState(tagProperty.GetValue(action, null));
        return new ScenarioResult(
            passed,
            actual,
            "exception:none,queue:0,delay:0,tag:null");
    }

    internal ScenarioResult EmptyClear()
    {
        IList instances = NewActionList();
        instancesField.SetValue(null, instances);
        Exception failure = Invoke(clearInstances, null, new object[0]);
        return new ScenarioResult(
            failure == null && instances.Count == 0,
            "exception:" + ExceptionName(failure) +
                ",count:" + instances.Count,
            "exception:none,count:0");
    }

    private IList NewActionList()
    {
        return (IList)Activator.CreateInstance(instancesField.FieldType);
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
    }

    private static string ExceptionName(Exception exception)
    {
        return exception == null ? "none" : exception.GetType().FullName;
    }

    private static string NullState(object value)
    {
        return value == null ? "null" : "set";
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
