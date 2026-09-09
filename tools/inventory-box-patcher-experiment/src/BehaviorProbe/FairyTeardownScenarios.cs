using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class FairyTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Type fairy = magicka.GetType(
            "Magicka.GameLogic.Entities.Fairy",
            false);
        Type avatar = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            false);
        Type npc = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            false);
        if (fairy == null || avatar == null || npc == null)
        {
            report.AddNotApplicable(
                "fairy_teardown.listed",
                "Fairy owner types are not present in this version");
            report.AddNotApplicable(
                "fairy_teardown.avatar_owned",
                "Fairy owner types are not present in this version");
            report.AddNotApplicable(
                "fairy_teardown.npc_owned",
                "Fairy owner types are not present in this version");
            return;
        }

        report.Add(
            "fairy_teardown.listed",
            RunCase(magicka, runtimePatchEnabled, fairy, fairy, null));
        report.Add(
            "fairy_teardown.avatar_owned",
            RunCase(magicka, runtimePatchEnabled, fairy, avatar, "mFairy"));
        report.Add(
            "fairy_teardown.npc_owned",
            RunCase(magicka, runtimePatchEnabled, fairy, npc, "mFairy"));
    }

    private static ScenarioResult RunCase(
        Assembly magicka,
        bool runtimePatchEnabled,
        Type fairyType,
        Type listedType,
        string fairyFieldName)
    {
        FieldInfo ownerField = RequireField(fairyType, "mOwner");
        FieldInfo activeField = RequireField(fairyType, "mActive");
        FieldInfo greetingField = RequireField(
            fairyType,
            "mLastDialogGreeting");
        FieldInfo tipField = RequireField(fairyType, "mLastDialogTip");
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        if (!ownerField.FieldType.IsAssignableFrom(ownerType))
            throw new InvalidOperationException(
                "Avatar no longer satisfies the Fairy owner contract.");
        object owner = FormatterServices.GetUninitializedObject(ownerType);
        object fairy = FormatterServices.GetUninitializedObject(fairyType);
        ownerField.SetValue(fairy, owner);
        activeField.SetValue(fairy, true);
        greetingField.SetValue(fairy, -1);
        tipField.SetValue(fairy, -1);

        object listed = fairy;
        if (fairyFieldName != null)
        {
            listed = FormatterServices.GetUninitializedObject(listedType);
            RequireField(listedType, fairyFieldName).SetValue(listed, fairy);
        }
        IList entities = new ArrayList();
        entities.Add(listed);

        Exception failure = null;
        if (runtimePatchEnabled)
        {
            Type patch = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                    "Magicka.CommunityPatch.Runtime.FairyTeardownPatch",
                    false);
            MethodInfo cleanup = patch == null
                ? null
                : patch.GetMethod(
                    "CleanupEntities",
                    BindingFlags.Static | BindingFlags.Public);
            if (cleanup != null)
                failure = Invoke(cleanup, null, new object[] { entities });
        }
        else
        {
            MethodInfo dispose = fairyType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose != null)
                failure = Invoke(dispose, fairy, new object[0]);
        }

        bool released = ownerField.GetValue(fairy) == null;
        bool inactive = !(bool)activeField.GetValue(fairy);
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && released && inactive,
            "exception:" + exception + ",owner_released:" + released +
                ",inactive:" + inactive,
            "exception:none,owner_released:True,inactive:True");
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
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
