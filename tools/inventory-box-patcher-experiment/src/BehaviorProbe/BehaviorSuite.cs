using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class BehaviorSuite
{
    internal static BehaviorReport Run(Assembly magicka, bool runtimePatchEnabled)
    {
        BehaviorReport report = new BehaviorReport();
        TimeWarpScenarios.Run(magicka, report);
        AvatarFindInteractableScenarios.Run(magicka, report);
        AvatarNetworkPickupScenarios.Run(magicka, report);
        AIStateAttackScenarios.Run(magicka, report);
        AIStateMoveScenarios.Run(magicka, report);
        AgentChooseTargetScenarios.Run(magicka, report);
        EntityManagerClosestDamageableScenarios.Run(magicka, report);
        EntityManagerTransitionScenarios.Run(magicka, report);
        EntityStateStorageScenarios.Run(magicka, report);
        HelperArrayEqualsScenarios.Run(magicka, report);
        InventoryBoxScenarios.Run(magicka, report);
        WidescreenSafeAreaScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TutorialManagerPlayStateScenarios.Run(magicka, report);
        EtherealCloneScenarios.Run(magicka, report);
        BreakBarriersScenarios.Run(magicka, report);
        TeslaFieldScenarios.Run(magicka, report);
        GenericHealthBarScenarios.Run(magicka, report);
        SpellWheelPlayStateScenarios.Run(magicka, report);
        EarthQuakePlayStateScenarios.Run(magicka, report);
        ArrowRainPlayStateScenarios.Run(magicka, report);
        GreaseTrailScenarios.Run(magicka, report);
        SpellEffectPlayStateScenarios.Run(magicka, report);
        EffectManagerScenarios.Run(magicka, report);
        MagickCameraScenarios.Run(magicka, report);
        BossHealthBarScenarios.Run(magicka, report);
        LoadingScreenScenarios.Run(magicka, report);
        HUDManagerScenarios.Run(magicka, report);
        MachineScenarios.Run(magicka, report);
        JormungandrScenarios.Run(magicka, report);
        PlayStateScenarios.Run(magicka, report);
        PortalTeleportQueueScenarios.Run(magicka, report);
        VersusRulesetScenarios.Run(magicka, report);
        PackLicenseScenarios.Run(magicka, runtimePatchEnabled, report);
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
        ConfuseWhoFactionScenarios.Run(magicka, report);
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
        ParadoxAccountLifetimeScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GiveOrderKhanScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ChallengeScoreScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        CharacterSelectWidgetScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        AmbientAudioLocatorScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        StaticCollectionGrowthScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        RailgunParentCycleScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        AnimationClipCompatibilityScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        CharacterSpellUsageScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GraphicsStartupErrorScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        MissingLevelFileScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        MouseResolutionScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        BorderlessPresentationScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ProcessThreadAffinityScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SystemLibraryPreloadScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ProgramArgumentScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        KeyboardMouseClearScenarios.Run(magicka, report);
        KeyboardMouseInteractableScenarios.Run(magicka, report);
        RadialBlurLifetimeScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SummonPhoenixPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        VladPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        NapalmPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ThunderboltPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        EntanglementEffectScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
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
        LightningBoltCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ElementalEggCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ItemPickableCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ActionLifecycleScenarios.Run(magicka, report);
        DialogManagerLevelCleanupScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        EntityPhysicsCleanupScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TypingTextScenarios.Run(magicka, report);
        NetworkServerLateUdpScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        NetworkServerEnterSyncScenarios.Run(magicka, report);
        NetworkClientRulesetScenarios.Run(magicka, report);
        MissileEntityNetworkEventScenarios.Run(magicka, report);
        NetworkServerHotjoinScenarios.Run(magicka, report);
        NetworkServerForcedSyncScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TriggerActionAuthorityScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TriggerActionLifecycleScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        JudgementSprayConditionCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        BlizzardCleanupScenarios.Run(magicka, report);
        BlizzardPlayStateScenarios.Run(magicka, report);
        WeatherPlayStateScenarios.Run(magicka, report);
        InGameMenuPlayStateScenarios.Run(magicka, report);
        InGameMenuStackCleanupScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        InGameMenuMagicksScenarios.Run(magicka, report);
        AnimatedLevelPartCollisionScenarios.Run(magicka, report);
        AnimatedLevelPartDisposeScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        DynamicLightCacheScenarios.Run(magicka, report);
        MeteorShowerScenarios.Run(magicka, report);
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
