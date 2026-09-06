using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class BehaviorSuite
{
    internal static BehaviorReport Run(Assembly magicka, bool runtimePatchEnabled)
    {
        BehaviorReport report = new BehaviorReport();
        AvatarFindInteractableScenarios.Run(magicka, report);
        AIStateAttackScenarios.Run(magicka, report);
        AIStateMoveScenarios.Run(magicka, report);
        AgentChooseTargetScenarios.Run(magicka, report);
        EntityManagerClosestDamageableScenarios.Run(magicka, report);
        EntityManagerTransitionScenarios.Run(magicka, report);
        EntityStateStorageScenarios.Run(magicka, report);
        HelperArrayEqualsScenarios.Run(magicka, report);
        InventoryBoxScenarios.Run(magicka, report);
        MagickCameraScenarios.Run(magicka, report);
        BossHealthBarScenarios.Run(magicka, report);
        LoadingScreenScenarios.Run(magicka, report);
        HUDManagerScenarios.Run(magicka, report);
        MachineScenarios.Run(magicka, report);
        JormungandrScenarios.Run(magicka, report);
        PlayStateScenarios.Run(magicka, report);
        PortalTeleportQueueScenarios.Run(magicka, report);
        VersusRulesetScenarios.Run(magicka, report);
        PackLicenseScenarios.Run(magicka, report);
        FlashScenarios.Run(magicka, report);
        SummonPlayStateScenarios.Run(magicka, report);
        UndeadSummonNetworkScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SummonCrossScenarios.Run(magicka, report);
        SpawnSlimeScenarios.Run(magicka, report);
        PoisonSprayScenarios.Run(magicka, report);
        ChillyBlastScenarios.Run(magicka, report);
        StarGazeScenarios.Run(magicka, report);
        ChargeAbilityScenarios.Run(magicka, report);
        ActiveBuffCacheScenarios.Run(magicka, report);
        EntityUpdateMessageScenarios.Run(magicka, runtimePatchEnabled, report);
        DrinkBloodScenarios.Run(magicka, report);
        RandomMineScenarios.Run(magicka, report);
        StarfallScenarios.Run(magicka, report);
        DrainLifeScenarios.Run(magicka, report);
        SubMenuMainScenarios.Run(magicka, report);
        CompanyStateScenarios.Run(magicka, report);
        ControlManagerScenarios.Run(magicka, report);
        DirectInputCompatibilityScenarios.Run(magicka, report);
        InteractableHighlightScenarios.Run(magicka, report);
        AudioManagerScenarios.Run(magicka, report);
        DeflectionAuraScenarios.Run(magicka, report);
        MenuImageTextItemScenarios.Run(magicka, runtimePatchEnabled, report);
        ParadoxPopupScenarios.Run(magicka, runtimePatchEnabled, report);
        LanguageManagerScenarios.Run(magicka, report);
        DialogLayoutScenarios.Run(magicka, runtimePatchEnabled, report);
        ShadowBlobsSceneScenarios.Run(magicka, runtimePatchEnabled, report);
        PlayerControllerAvatarScenarios.Run(magicka, report);
        PlayerTextBoxCleanupScenarios.Run(magicka, report);
        PlayerNotifierCleanupScenarios.Run(magicka, report);
        ChantSpellCleanupScenarios.Run(magicka, report);
        StaticLevelPoolCleanupScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        JudgementSprayConditionCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        BlizzardCleanupScenarios.Run(magicka, report);
        AnimatedLevelPartCollisionScenarios.Run(magicka, report);
        return report;
    }
}

internal sealed class BehaviorReport
{
    private readonly List<KeyValuePair<string, ScenarioResult>> scenarios =
        new List<KeyValuePair<string, ScenarioResult>>();

    internal void Add(string name, ScenarioResult result)
    {
        scenarios.Add(new KeyValuePair<string, ScenarioResult>(name, result));
    }

    internal void AddNotApplicable(string name, string reason)
    {
        Add(name, ScenarioResult.NotApplicable(reason));
    }

    internal void WriteTo(TextWriter output)
    {
        for (int index = 0; index < scenarios.Count; index++)
        {
            KeyValuePair<string, ScenarioResult> scenario = scenarios[index];
            output.WriteLine("scenario." + scenario.Key + "=" + scenario.Value.Status);
            output.WriteLine("detail." + scenario.Key + "=actual:" + scenario.Value.Actual +
                "|expected:" + scenario.Value.Expected);
        }
    }
}

internal sealed class ScenarioResult
{
    internal string Status { get; private set; }
    internal string Actual { get; private set; }
    internal string Expected { get; private set; }

    internal ScenarioResult(bool passed, string actual, string expected)
    {
        Status = passed ? "PASS" : "FAIL";
        Actual = actual;
        Expected = expected;
    }

    private ScenarioResult(string status, string actual, string expected)
    {
        Status = status;
        Actual = actual;
        Expected = expected;
    }

    internal static ScenarioResult NotApplicable(string reason)
    {
        return new ScenarioResult("NOT_APPLICABLE", reason, "not available in this version");
    }
}
