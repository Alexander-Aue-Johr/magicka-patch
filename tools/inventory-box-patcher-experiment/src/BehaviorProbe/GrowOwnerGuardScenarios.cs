using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class GrowOwnerGuardScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        GrowOwnerGuardHarness harness = new GrowOwnerGuardHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("grow.orphaned_owner", harness.OrphanedOwner());
        report.Add("grow.owner_present", harness.OwnerPresent());
    }
}

internal sealed class GrowOwnerGuardHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.GrowOwnerGuardPatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly Type growType;
    private readonly Type characterType;
    private readonly Type ownerType;
    private readonly Type dataChannelType;
    private readonly FieldInfo ownerField;
    private readonly FieldInfo ttlField;
    private readonly FieldInfo animationTimeField;
    private readonly MethodInfo update;

    internal GrowOwnerGuardHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        growType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grow",
            true);
        characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        dataChannelType = RuntimeReflection.FindLoadedType(
            "PolygonHead.DataChannel");
        ownerField = RequireField(growType, "mOwner", characterType);
        ttlField = RequireField(growType, "mTTL", typeof(float));
        animationTimeField = RequireField(
            growType,
            "mAnimationTime",
            typeof(float));
        update = growType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { dataChannelType, typeof(float) },
            null);
        if (update == null || update.ReturnType != typeof(void))
            throw new MissingMethodException(growType.FullName, "Update");
    }

    internal ScenarioResult OrphanedOwner()
    {
        object grow = NewUninitialized(growType);
        ownerField.SetValue(grow, null);
        ttlField.SetValue(grow, 5f);
        animationTimeField.SetValue(grow, 1f);

        string outcome;
        try
        {
            update.Invoke(
                grow,
                new object[] { Enum.ToObject(dataChannelType, 0), 0.25f });
            outcome = "returned";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            outcome = "threw:" + inner.GetType().Name;
        }

        float ttl = Convert.ToSingle(ttlField.GetValue(grow));
        float animationTime = Convert.ToSingle(
            animationTimeField.GetValue(grow));
        string actual = outcome + ",ttl:" + ttl +
            ",animation:" + animationTime;
        const string expected = "returned,ttl:0,animation:0";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult OwnerPresent()
    {
        object grow = NewUninitialized(growType);
        object owner = NewUninitialized(ownerType);
        ownerField.SetValue(grow, owner);
        ttlField.SetValue(grow, 5f);
        animationTimeField.SetValue(grow, 1f);

        bool continueOriginal = true;
        if (runtimePatchEnabled)
        {
            Type patch = Type.GetType(PatchTypeName, true);
            MethodInfo prefix = patch.GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
            if (prefix == null)
                throw new MissingMethodException(patch.FullName, "Prefix");
            continueOriginal = (bool)prefix.Invoke(
                null,
                new object[] { grow });
        }

        bool ownerPreserved = Object.ReferenceEquals(
            ownerField.GetValue(grow),
            owner);
        float ttl = Convert.ToSingle(ttlField.GetValue(grow));
        float animationTime = Convert.ToSingle(
            animationTimeField.GetValue(grow));
        string actual = "continue:" + continueOriginal +
            ",owner:" + ownerPreserved +
            ",ttl:" + ttl +
            ",animation:" + animationTime;
        const string expected =
            "continue:True,owner:True,ttl:5,animation:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private static object NewUninitialized(Type type)
    {
        return FormatterServices.GetUninitializedObject(type);
    }

    private static FieldInfo RequireField(
        Type type,
        string name,
        Type fieldType)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != fieldType)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
}
