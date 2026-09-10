using System.Collections.Generic;
using System.IO;
using System.Reflection;

internal static class BehaviorSuite
{
    internal static BehaviorReport Run(Assembly magicka, bool runtimePatchEnabled)
    {
        BehaviorReport report = new BehaviorReport();
        PayloadContractScenarios.Run(magicka, runtimePatchEnabled, report);
        TimeWarpScenarios.Run(magicka, report);
        AvatarFindInteractableScenarios.Run(magicka, report);
        AvatarNetworkPickupScenarios.Run(magicka, report);
        AvatarInventoryCloseScenarios.Run(
            magicka, runtimePatchEnabled, report);
        AIStateAttackScenarios.Run(magicka, report);
        AIStateMoveScenarios.Run(magicka, report);
        AgentChooseTargetScenarios.Run(magicka, report);
        AgentLifecycleScenarios.Run(magicka, runtimePatchEnabled, report);
        CharacterTeardownScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        CharacterNetworkGripScenarios.Run(magicka, report);
        NonPlayerCharacterTeardownScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        NonPlayerCharacterLifecycleScenarios.Run(magicka, report);
        EntityManagerClosestDamageableScenarios.Run(magicka, report);
        EntityManagerTransitionScenarios.Run(magicka, report);
        EntityManagerPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        EntityStateStorageScenarios.Run(magicka, report);
        IconRendererPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        HelperArrayEqualsScenarios.Run(magicka, report);
        InventoryBoxScenarios.Run(magicka, report);
        WidescreenSafeAreaScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        HighResolutionUiRenderScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        LevelCurrentPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        LevelSceneTransitionScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GameSparksRetirementScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GameOptionalEffectScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TomeShadowMapClearScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TomeVersionTextScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TomeSupporterScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        TomeLanguageRefreshScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ItemWeaponCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SharedContentLifetimeScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        InGameMenuScaleScenarios.Run(
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
        GreaseLifecycleScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GreaseLumpScenarios.Run(magicka, report);
        UnderGroundAttackScenarios.Run(magicka, report);
        ArcaneBladeScenarios.Run(magicka, report);
        ConflagrationPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        WavePlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SpellMineLevelPartScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        PolymorphPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        FloorStompPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        RevivePlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GrowOwnerGuardScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SpellEffectPlayStateScenarios.Run(magicka, report);
        DerivedSpellEffectPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        PoolExpansionScenarios.Run(magicka, runtimePatchEnabled, report);
        ProjectileSpawnOwnerGuardScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ProjectileSpellConditionCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ProjectileSpellMissileLifecycleScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        PropBossTeardownScenarios.Run(magicka, runtimePatchEnabled, report);
        FairyTeardownScenarios.Run(magicka, runtimePatchEnabled, report);
        BarrierTeardownScenarios.Run(magicka, runtimePatchEnabled, report);
        ShieldContentLifetimeScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GibTeardownScenarios.Run(magicka, runtimePatchEnabled, report);
        EffectManagerScenarios.Run(magicka, report);
        MagickCameraScenarios.Run(magicka, runtimePatchEnabled, report);
        BossHealthBarScenarios.Run(magicka, report);
        LoadingScreenScenarios.Run(magicka, report);
        HUDManagerScenarios.Run(magicka, report);
        MachineScenarios.Run(magicka, report);
        BossFightOrderingScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        JormungandrScenarios.Run(magicka, report);
        PlayStateScenarios.Run(magicka, report);
        PortalTeleportQueueScenarios.Run(magicka, report);
        PortalLifecycleScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
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
        ConfuseFactionScenarios.Run(magicka, report);
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
        ElementSelectionTelemetryScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ParadoxStorePriceUpdateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
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
        CharacterSelectContentUnloadScenarios.Run(
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
        SteamApiPreflightScenarios.Run(
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
        ElementalEggTeardownScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ItemPickableCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        DamageableEntityStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        SummonDeathPlayStateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        PhysicsEntityTemplateCacheScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        CharacterTemplateCacheScenarios.Run(magicka, report);
        ActionLifecycleScenarios.Run(magicka, report);
        DialogManagerLevelCleanupScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        EntityPhysicsCleanupScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        PhysicsManagerClearScenarios.Run(magicka, report);
        DamageableEntityTeardownScenarios.Run(magicka, report);
        AnimatedPhysicsEntityLifecycleScenarios.Run(magicka, report);
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
        LevelModelTeardownScenarios.Run(magicka, report);
        GameScenePlayStateScenarios.Run(magicka, report);
        GameSceneMenuControllerResetScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GameSceneLightUpdateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        GameSceneTeardownScenarios.Run(magicka, report);
        RuntimeTelemetryBackoffScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        RuntimeTelemetryContextScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        OriginalBackupAuditScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        RuntimePatchSettingsScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        RuntimePatchUpdateScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        RuntimePatchMetadataScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        WarlordAbilityDiagnosticScenarios.Run(
            magicka,
            runtimePatchEnabled,
            report);
        ForceFieldScenarios.Run(magicka, runtimePatchEnabled, report);
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
