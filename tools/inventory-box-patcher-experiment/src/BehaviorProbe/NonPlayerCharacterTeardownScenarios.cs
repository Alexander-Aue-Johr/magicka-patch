using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

internal static class NonPlayerCharacterTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        NonPlayerCharacterTeardownHarness harness =
            new NonPlayerCharacterTeardownHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("npc_teardown.derived_state", harness.ReleaseDerivedState());
    }
}

internal sealed class NonPlayerCharacterTeardownHarness
{
    private const BindingFlags DeclaredInstance =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Type npcType;
    private readonly Type agentType;
    private readonly Type fairyType;
    private readonly Type abilityType;
    private readonly bool runtimePatchEnabled;
    private readonly ConstructorInfo agentConstructor;
    private readonly FieldInfo cacheField;

    internal NonPlayerCharacterTeardownHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        RuntimeHelpers.RunClassConstructor(npcType.TypeHandle);
        agentType = magicka.GetType("Magicka.AI.Agent", true);
        fairyType = magicka.GetType(
            "Magicka.GameLogic.Entities.Fairy",
            true);
        abilityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.Ability",
            true);
        this.runtimePatchEnabled = runtimePatchEnabled;
        agentConstructor = agentType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic,
            null,
            new Type[] { npcType },
            null);
        cacheField = RuntimeReflection.RequireField(npcType, "sCache");
        if (cacheField.GetValue(null) == null)
            cacheField.SetValue(
                null,
                Activator.CreateInstance(cacheField.FieldType));
        if (agentConstructor == null)
            throw new MissingMethodException(npcType.FullName, ".ctor");
    }

    internal ScenarioResult ReleaseDerivedState()
    {
        object npc = FormatterServices.GetUninitializedObject(npcType);
        GC.SuppressFinalize(npc);
        object agent = agentConstructor.Invoke(new object[] { npc });
        object fairy = FormatterServices.GetUninitializedObject(fairyType);
        GC.SuppressFinalize(fairy);
        SetEffectInactive(fairy, "mEffectRef");
        SetEffectInactive(fairy, "mCrashEffectRef");
        RuntimeReflection.WriteField(fairy, "mLastDialogGreeting", -1);
        RuntimeReflection.WriteField(fairy, "mLastDialogTip", -1);
        RuntimeReflection.WriteField(fairy, "mOwner", npc);

        RuntimeReflection.WriteField(npc, "mAI", agent);
        RuntimeReflection.WriteField(npc, "mFairy", fairy);
        RuntimeReflection.WriteField(
            npc,
            "mAbilities",
            Array.CreateInstance(abilityType, 1));
        RuntimeReflection.WriteField(npc, "mSummoned", true);
        RuntimeReflection.WriteField(npc, "mUndeadSummon", true);
        RuntimeReflection.WriteField(npc, "mFlamerSummon", true);
        SetEffectInactive(npc, "mSummonedEffect");

        IList cache = cacheField.GetValue(null) as IList;
        if (cache == null)
            throw new InvalidOperationException("NPC cache is unavailable.");
        cache.Clear();
        cache.Add(npc);

        Exception failure;
        if (runtimePatchEnabled)
        {
            Type patch = typeof(
                Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.NonPlayerCharacterTeardownPatch",
                false);
            MethodInfo cleanup = patch == null
                ? null
                : patch.GetMethod(
                    "CleanupFinal",
                    BindingFlags.Static | BindingFlags.Public);
            failure = cleanup == null
                ? new MissingMethodException(
                    "NonPlayerCharacterTeardownPatch.CleanupFinal")
                : Invoke(cleanup, null, new object[] { npc });
        }
        else
        {
            MethodInfo dispose = npcType.GetMethod(
                "Dispose",
                DeclaredInstance,
                null,
                Type.EmptyTypes,
                null);
            failure = dispose == null
                ? new MissingMethodException(npcType.FullName, "Dispose")
                : Invoke(dispose, npc, new object[0]);
        }

        bool released =
            !cache.Contains(npc) &&
            RuntimeReflection.ReadField(npc, "mAI") == null &&
            RuntimeReflection.ReadField(npc, "mFairy") == null &&
            RuntimeReflection.ReadField(npc, "mSummonMaster") == null &&
            RuntimeReflection.ReadField(npc, "mAbilities") == null &&
            !(bool)RuntimeReflection.ReadField(npc, "mSummoned") &&
            !(bool)RuntimeReflection.ReadField(npc, "mUndeadSummon") &&
            !(bool)RuntimeReflection.ReadField(npc, "mFlamerSummon") &&
            RuntimeReflection.ReadField(agent, "mOwner") == null &&
            RuntimeReflection.ReadField(fairy, "mOwner") == null;
        cache.Clear();
        string exception = failure == null
            ? "none"
            : failure.GetType().FullName;
        return new ScenarioResult(
            failure == null && released,
            "exception:" + exception + ",state:" +
                (released ? "released" : "retained"),
            "exception:none,state:released");
    }

    private static void SetEffectInactive(object target, string fieldName)
    {
        FieldInfo field = RuntimeReflection.RequireField(
            target.GetType(),
            fieldName);
        object effect = Activator.CreateInstance(field.FieldType);
        RuntimeReflection.WriteField(effect, "ID", -1);
        field.SetValue(target, effect);
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
