using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ChallengeScoreScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ChallengeScoreHarness harness =
            new ChallengeScoreHarness(magicka, runtimePatchEnabled);
        report.Add("challenge_score.duplicate_paths", harness.DuplicatePaths());
        report.Add("challenge_score.pooled_reuse", harness.PooledReuse());
        report.Add("challenge_score.distinct_enemies", harness.DistinctEnemies());
    }
}

internal sealed class ChallengeScoreHarness
{
    private const string RuntimePatchTypeName =
        "Magicka.CommunityPatch.Runtime.ChallengeScorePatch";

    private readonly bool runtimePatchEnabled;
    private readonly Type nonPlayerCharacterType;
    private readonly MethodInfo manualTryCredit;
    private readonly FieldInfo manualCredited;
    private readonly MethodInfo runtimeTryCredit;
    private readonly MethodInfo runtimeReset;

    internal ChallengeScoreHarness(Assembly magicka, bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        nonPlayerCharacterType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        manualTryCredit = nonPlayerCharacterType.GetMethod(
            "CommunityPatchTryCreditChallengeScore",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        manualCredited = nonPlayerCharacterType.GetField(
            "mCommunityPatchChallengeScoreCredited",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

        Type patchType = typeof(Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
            RuntimePatchTypeName,
            false);
        runtimeTryCredit = patchType == null
            ? null
            : patchType.GetMethod(
                "TryCredit",
                BindingFlags.Static | BindingFlags.Public);
        runtimeReset = patchType == null
            ? null
            : patchType.GetMethod(
                "Reset",
                BindingFlags.Static | BindingFlags.Public);
    }

    internal ScenarioResult DuplicatePaths()
    {
        object enemy = NewEnemy();
        int accepted = Credit(enemy) + Credit(enemy);
        return CountResult(accepted, 1);
    }

    internal ScenarioResult PooledReuse()
    {
        object enemy = NewEnemy();
        int accepted = Credit(enemy) + Credit(enemy);
        Reset(enemy);
        accepted += Credit(enemy);
        return CountResult(accepted, 2);
    }

    internal ScenarioResult DistinctEnemies()
    {
        int accepted = Credit(NewEnemy()) + Credit(NewEnemy());
        return CountResult(accepted, 2);
    }

    private int Credit(object enemy)
    {
        if (manualTryCredit != null)
            return (bool)manualTryCredit.Invoke(enemy, new object[0]) ? 1 : 0;
        if (runtimePatchEnabled && runtimeTryCredit != null)
            return (bool)runtimeTryCredit.Invoke(
                null,
                new object[] { enemy }) ? 1 : 0;
        return 1;
    }

    private void Reset(object enemy)
    {
        if (manualCredited != null)
        {
            manualCredited.SetValue(enemy, false);
            return;
        }
        if (runtimePatchEnabled && runtimeReset != null)
            runtimeReset.Invoke(null, new object[] { enemy });
    }

    private object NewEnemy()
    {
        object enemy = FormatterServices.GetUninitializedObject(
            nonPlayerCharacterType);
        GC.SuppressFinalize(enemy);
        return enemy;
    }

    private static ScenarioResult CountResult(int actual, int expected)
    {
        return new ScenarioResult(
            actual == expected,
            "credited:" + actual,
            "credited:" + expected);
    }
}
