using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class PropBossTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type propBoss = magicka.GetType(
            "Magicka.GameLogic.Entities.Bosses.PropBoss",
            false);
        Type entity = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            false);
        if (propBoss == null || entity == null)
        {
            report.AddNotApplicable(
                "prop_boss.level_teardown",
                "PropBoss or Entity is not present in this version");
            return;
        }

        FieldInfo typeField = propBoss.GetField(
            "mType",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        MethodInfo clearHandles = entity.GetMethod(
            "ClearHandles",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        FieldInfo instancesField = entity.GetField(
            "mInstances",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (typeField == null || typeField.FieldType != typeof(string) ||
            clearHandles == null || instancesField == null)
            throw new MissingMemberException(
                "PropBoss teardown behavior contract is incomplete.");

        object instance = FormatterServices.GetUninitializedObject(propBoss);
        typeField.SetValue(instance, "Data/PhysicsEntities/TestBoss");
        Exception failure = null;
        if (runtimePatchEnabled)
        {
            IList instances = instancesField.GetValue(null) as IList;
            if (instances == null)
                throw new InvalidOperationException(
                    "Entity handle list is unavailable.");
            instances.Clear();
            instances.Add(instance);
            failure = Invoke(clearHandles, null, new object[0]);
        }
        else
        {
            MethodInfo dispose = propBoss.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose != null)
                failure = Invoke(dispose, instance, new object[0]);
        }

        bool released = typeField.GetValue(instance) == null;
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        report.Add(
            "prop_boss.level_teardown",
            new ScenarioResult(
                failure == null && released,
                "exception:" + exception + ",type_released:" + released,
                "exception:none,type_released:True"));
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
