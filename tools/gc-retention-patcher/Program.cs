using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

const string EntityTypeName = "Magicka.GameLogic.Entities.Entity";
const string PlayStateTypeName = "Magicka.GameLogic.GameStates.PlayState";
const string GameStateTypeName = "Magicka.GameLogic.GameStates.GameState";
const string RegistryTypeName = "Magicka.GcDiagnostics.RetentionRegistry";
const string PayloadContractId =
    "magicka-community-patch-payload-0.0.55-r1";

if (args.Length == 3
    && args[0] == "--normalize-self-references")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    (int directReferences, int totalReferences) =
        NormalizeSelfReferencesOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": rebound " + directReferences
        + " directly self-scoped TypeRef roots ("
        + totalReferences + " rows including nested types)");
    return 0;
}

if (args.Length == 4
    && args[0] == "--restore-interface-order")
{
    string referencePath = Path.GetFullPath(args[1]);
    string inputPath = Path.GetFullPath(args[2]);
    string outputPath = Path.GetFullPath(args[3]);
    int reorderedTypes = RestoreOriginalInterfaceOrder(
        referencePath,
        inputPath,
        outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored interface order on " + reorderedTypes + " types");
    return 0;
}

if (args.Length == 4
    && args[0] == "--restore-local-rename-bodies")
{
    string referencePath = Path.GetFullPath(args[1]);
    string inputPath = Path.GetFullPath(args[2]);
    string outputPath = Path.GetFullPath(args[3]);
    int restoredMethods = RestoreLocalRenameMethodBodies(
        referencePath,
        inputPath,
        outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored " + restoredMethods
        + " local-rename-only method bodies");
    return 0;
}

if (args.Length == 4
    && args[0] == "--restore-lock-lowering")
{
    string referencePath = Path.GetFullPath(args[1]);
    string inputPath = Path.GetFullPath(args[2]);
    string outputPath = Path.GetFullPath(args[3]);
    (int restoredMethods, int restoredSites) = RestoreOriginalLockLowering(
        referencePath,
        inputPath,
        outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored direct lock lowering at " + restoredSites
        + " sites in " + restoredMethods + " methods");
    return 0;
}

if (args.Length == 4
    && args[0] == "--restore-recompiled-method-bodies")
{
    string referencePath = Path.GetFullPath(args[1]);
    string inputPath = Path.GetFullPath(args[2]);
    string outputPath = Path.GetFullPath(args[3]);
    int restoredMethods = RestoreRecompiledMethodBodies(
        referencePath,
        inputPath,
        outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored " + restoredMethods
        + " semantically unchanged recompiled method bodies");
    return 0;
}

if (args.Length == 4
    && args[0] == "--restore-physics-manager-clear")
{
    string referencePath = Path.GetFullPath(args[1]);
    string inputPath = Path.GetFullPath(args[2]);
    string outputPath = Path.GetFullPath(args[3]);
    RestorePhysicsManagerClearOnly(referencePath, inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored reusable PhysicsManager.Clear semantics");
    return 0;
}

if (args.Length == 4
    && args[0] == "--restore-network-client-methods")
{
    string referencePath = Path.GetFullPath(args[1]);
    string inputPath = Path.GetFullPath(args[2]);
    string outputPath = Path.GetFullPath(args[3]);
    int restoredMethods = RestoreNetworkClientMethodsOnly(
        referencePath,
        inputPath,
        outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored NetworkClient IL in " + restoredMethods + " methods");
    return 0;
}

if (args.Length == 3
    && args[0] == "--repair-network-server-handler-order")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    RepairNetworkServerHandlerOrderOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored CLR-compatible NetworkServer exception-handler order");
    return 0;
}

if (args.Length == 3
    && args[0] == "--repair-network-server-forced-sync-exit")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    RepairNetworkServerForcedSyncExitOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": restored the NetworkServer forced-sync case exit");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-game-thread-affinity-null")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchGameThreadAffinityNullOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": skipped unavailable ProcessThread entries during startup");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-array-equals-null")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchArrayEqualsNullOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": treated unavailable byte arrays as unequal");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-avatar-find-interactable-null")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchAvatarFindInteractableNullOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": skipped interaction lookup after scene teardown");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-character-cast-spell-gamer-null")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchCharacterCastSpellGamerNullOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": skipped optional spell statistics for a detached Gamer");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-character-select-disposed-icon")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchCharacterSelectDisposedIconOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": skipped disposed character-select controller icons");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-network-client-ruleset-teardown")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchNetworkClientRulesetTeardownOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": dropped late RulesetUpdate packets after play-state teardown");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-graphics-startup-errors")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchGraphicsStartupErrorsOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": added actionable graphics startup errors");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-gc-diagnostics-startup-check")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchGcDiagnosticsStartupCheckOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": added the GC diagnostics payload startup check");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-level-hash-missing-file")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchLevelHashMissingFileOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": reported missing level hash inputs without crash telemetry");
    return 0;
}

if (args.Length == 4
    && args[0] == "--patch-polygon-payload-contract")
{
    string contractMagickaPath = Path.GetFullPath(args[1]);
    string contractPolygonHeadPath = Path.GetFullPath(args[2]);
    string contractOutputDirectory = Path.GetFullPath(args[3]);
    PatchPolygonPayloadContractOnly(
        contractMagickaPath,
        contractPolygonHeadPath,
        contractOutputDirectory);
    Console.WriteLine(
        "Magicka.exe and PolygonHead.dll: added the shared payload contract");
    return 0;
}

if (args.Length == 3
    && args[0] == "--diagnose-character-entity-update")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    DiagnoseCharacterEntityUpdateOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": diagnosed incoming EntityUpdateMessage Character flags");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-telemetry-game-integrity")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchTelemetryGameIntegrityOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": added the game integrity state to telemetry");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-character-template-static-caches")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchCharacterTemplateStaticCachesOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": released static CharacterTemplate caches");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-polygon-light-scene-detach")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchPolygonHeadLightSceneDetachOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": repaired Light scene detachment");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-warlord-ability-diagnostic")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchWarlordAbilityDiagnosticOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": added Warlord primary-ability diagnostic");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-railgun-parent-cycle")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchRailgunParentCycleOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": prevented Railgun parent cycles");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-jormungandr-null-target")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchJormungandrNullTargetOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": guarded Jormungandr emergence without a target");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-judgement-spray-condition-cache")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchJudgementSprayConditionCacheOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": recovered JudgementSpray condition-cache exhaustion");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-rain-scene-detach")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchRainSceneDetachOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": detached Rain and Thunderstorm cast references");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-shadow-blobs-scene-detach")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchShadowBlobsSceneDetachOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": detached ShadowBlobs from its unloaded scene");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-meteor-shower-remove-references")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchMeteorShowerRemoveReferencesOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": released MeteorShower singleton references");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-blizzard-remove-references")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchBlizzardRemoveReferencesOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": released Blizzard singleton references");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-controller-avatar-detach")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchControllerAvatarDetachOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": detached cleared Avatars from player controllers");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-gc-event-patch-version")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchGcEventPatchVersionOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": added the patch version to all telemetry events");
    return 0;
}

if (args.Length == 3
    && args[0] == "--repair-mono-telemetry-startup")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    RepairMonoTelemetryStartupOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": repaired Mono telemetry startup compatibility");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-player-game-deinitialize")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchPlayerGameDeinitializeOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": released Player UI level references");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-entity-collision-callback-cleanup")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchEntityCollisionCallbackCleanupOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": cleared disposed entity collision callbacks");
    return 0;
}

if (args.Length == 3
    && args[0] == "--repair-entity-physics-lifecycle")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    RepairEntityPhysicsLifecycleOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": centralized Entity physics teardown and pooled reuse cleanup");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-entity-manager-quadgrid-lifecycle")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchEntityManagerQuadGridLifecycleOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": cleared stale QuadGrid entries and skipped bodyless queries");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-animated-level-part-detached-body")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchAnimatedLevelPartDetachedBodyOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": removed detached moving-platform entity registrations");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-ai-detached-targets")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchAiDetachedTargetsOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": rejected detached AI targets before physics reads");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-network-pickup-detached-target")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchNetworkPickupDetachedTargetOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": dropped network pickups for detached targets");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-character-template-playstate-transition")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchCharacterTemplatePlayStateTransitionOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": guarded template fallback during PlayState transitions");
    return 0;
}

if (args.Length == 3
    && args[0] == "--patch-invalid-audio-locator")
{
    string inputPath = Path.GetFullPath(args[1]);
    string outputPath = Path.GetFullPath(args[2]);
    PatchInvalidAudioLocatorOnly(inputPath, outputPath);
    Console.WriteLine(
        Path.GetFileName(inputPath)
        + ": removed invalid XACT audio locators after index failures");
    return 0;
}

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: RetentionPatcher <Magicka.exe> <PolygonHead.dll>"
        + " <Magicka.GcDiagnostics.dll> <output-directory>\n"
        + "   or: RetentionPatcher"
        + " --normalize-self-references"
        + " <assembly> <output-assembly>\n"
        + "   or: RetentionPatcher"
        + " --restore-interface-order"
        + " <reference-assembly> <assembly> <output-assembly>\n"
        + "   or: RetentionPatcher"
        + " --restore-local-rename-bodies"
        + " <reference-assembly> <assembly> <output-assembly>\n"
        + "   or: RetentionPatcher"
        + " --restore-lock-lowering"
        + " <reference-assembly> <assembly> <output-assembly>\n"
        + "   or: RetentionPatcher"
        + " --restore-recompiled-method-bodies"
        + " <reference-assembly> <assembly> <output-assembly>\n"
        + "   or: RetentionPatcher"
        + " --restore-physics-manager-clear"
        + " <reference-assembly> <assembly> <output-assembly>\n"
        + "   or: RetentionPatcher"
        + " --patch-character-template-static-caches"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-game-thread-affinity-null"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-array-equals-null"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-avatar-find-interactable-null"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-character-cast-spell-gamer-null"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-character-select-disposed-icon"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-network-client-ruleset-teardown"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-graphics-startup-errors"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-gc-diagnostics-startup-check"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-level-hash-missing-file"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-polygon-payload-contract"
        + " <Magicka.exe> <PolygonHead.dll> <output-directory>\n"
        + "   or: RetentionPatcher"
        + " --diagnose-character-entity-update"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-telemetry-game-integrity"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-polygon-light-scene-detach"
        + " <PolygonHead.dll> <output-PolygonHead.dll>\n"
        + "   or: RetentionPatcher"
        + " --patch-warlord-ability-diagnostic"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-railgun-parent-cycle"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-jormungandr-null-target"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-judgement-spray-condition-cache"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-rain-scene-detach"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-shadow-blobs-scene-detach"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-meteor-shower-remove-references"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-blizzard-remove-references"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-controller-avatar-detach"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-gc-event-patch-version"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-player-game-deinitialize"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-entity-collision-callback-cleanup"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --repair-entity-physics-lifecycle"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-entity-manager-quadgrid-lifecycle"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-animated-level-part-detached-body"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-ai-detached-targets"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-network-pickup-detached-target"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-character-template-playstate-transition"
        + " <Magicka.exe> <output-Magicka.exe>\n"
        + "   or: RetentionPatcher"
        + " --patch-invalid-audio-locator"
        + " <Magicka.exe> <output-Magicka.exe>");
    return 2;
}

string magickaPath = Path.GetFullPath(args[0]);
string polygonHeadPath = Path.GetFullPath(args[1]);
string diagnosticsPath = Path.GetFullPath(args[2]);
string outputDirectory = Path.GetFullPath(args[3]);

Directory.CreateDirectory(outputDirectory);

using AssemblyDefinition diagnostics = ReadAssembly(diagnosticsPath);
HelperMethods helperMethods = LoadHelperMethods(diagnostics);

PatchReport magickaReport = PatchMagicka(
    magickaPath,
    Path.Combine(outputDirectory, Path.GetFileName(magickaPath)),
    helperMethods);
PatchReport polygonHeadReport = PatchPolygonHead(
    polygonHeadPath,
    Path.Combine(outputDirectory, Path.GetFileName(polygonHeadPath)),
    helperMethods);

Console.WriteLine(magickaReport);
Console.WriteLine(polygonHeadReport);
return 0;

static PatchReport PatchMagicka(
    string inputPath,
    string outputPath,
    HelperMethods sourceHelpers)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    EnsureNotAlreadyPatched(assembly);
    ModuleDefinition module = assembly.MainModule;
    HelperMethods helpers = sourceHelpers.ImportInto(module);
    Dictionary<string, TypeDefinition> types = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);

    RepairParadoxStoreWorkerDelegate(module, types);
    RepairClr2CollectionLocks(types);
    RepairCharacterTemplateStaticCaches(module, types);
    PatchWarlordAbilityDiagnostic(module, types);
    RepairRailgunParentCycles(module, types);
    RepairJormungandrNullTarget(types);
    RepairJudgementSprayConditionCache(module, types);
    RepairRainSceneDetach(types);
    RepairShadowBlobsSceneDetach(types);
    RepairMeteorShowerRemoveReferences(types);
    RepairBlizzardRemoveReferences(types);
    RepairControllerAvatarDetach(types);
    RepairGcEventPatchVersion(module, types);
    RepairPlayerGameDeinitialize(types);
    RepairEntityCollisionCallbackCleanup(module, types);

    int registrations = 0;
    int activeHooks = 0;
    int residentActiveHooks = 0;
    int deactivatedHooks = 0;
    int collectHooks = 0;
    int detachHooks = 0;
    int checkpointHooks = 0;

    TypeDefinition entityType = RequireType(types, EntityTypeName);
    RepairAvatarCacheExpansionBranch(
        RequireMethod(
            RequireType(types, "Magicka.GameLogic.Entities.Avatar"),
            "GetFromCache",
            parameterCount: 1));
    foreach (MethodDefinition constructor in entityType.Methods.Where(
                 method => method.IsConstructor && !method.IsStatic && method.HasBody))
    {
        registrations += InstrumentSelfAtReturns(
            constructor,
            helpers.Register,
            EntityTypeName + "..ctor");
    }

    foreach (TypeDefinition type in types.Values.Where(
                 type => IsSameModuleSubclassOf(type, EntityTypeName, types)))
    {
        foreach (MethodDefinition method in type.Methods.Where(
                     method => !method.IsStatic
                               && method.HasBody
                               && method.ReturnType.FullName == "System.Void"))
        {
            string lifecycle = type.FullName + "." + method.Name;
            if (method.Name == "Initialize")
            {
                activeHooks += InstrumentSelfAtReturns(
                    method,
                    helpers.MarkActive,
                    lifecycle);
            }
            else if (string.Equals(
                         method.Name, "Deinitialize", StringComparison.OrdinalIgnoreCase))
            {
                deactivatedHooks += InstrumentSelfAtEntry(
                    method,
                    helpers.MarkDeactivated,
                    lifecycle);
            }
            else if (method.Name == "Dispose")
            {
                collectHooks += InstrumentSelfAtEntry(
                    method,
                    helpers.MarkMustCollect,
                    lifecycle);
            }
        }
    }

    HashSet<string> poolSinkNames = new HashSet<string>(
        new[] { "ReturnToCache", "AddToCache", "ReturnGib" },
        StringComparer.Ordinal);
    MethodDefinition[] poolSinks = types.Values
        .SelectMany(type => type.Methods)
        .Where(method =>
            method.IsStatic
            && method.HasBody
            && method.ReturnType.FullName == "System.Void"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName
                == method.DeclaringType.FullName
            && poolSinkNames.Contains(method.Name))
        .OrderBy(method => method.FullName, StringComparer.Ordinal)
        .ToArray();
    if (poolSinks.Length < 8)
    {
        throw new InvalidOperationException(
            "Expected at least eight same-type static pool sinks, found "
            + poolSinks.Length);
    }

    HashSet<string> pooledHolderTypes = new HashSet<string>(
        StringComparer.Ordinal);
    foreach (MethodDefinition method in poolSinks)
    {
        string typeName = method.DeclaringType.FullName;
        pooledHolderTypes.Add(typeName);
        detachHooks += InstrumentFirstArgumentAtReturns(
            method,
            helpers.MarkMustDetach,
            typeName + "." + method.Name);
    }

    int directCacheInsertions = 0;
    foreach (TypeDefinition type in types.Values)
    {
        foreach (MethodDefinition method in type.Methods.Where(
                     method => method.HasBody))
        {
            int hooks = InstrumentCacheInsertionSites(
                method,
                helpers.MarkMustDetach,
                type.FullName + "." + method.Name + ".CacheInsert");
            if (hooks != 0)
            {
                pooledHolderTypes.Add(type.FullName);
                directCacheInsertions += hooks;
                detachHooks += hooks;
            }
        }
    }

    if (directCacheInsertions < 20)
    {
        throw new InvalidOperationException(
            "Expected at least twenty direct instance cache insertions, found "
            + directCacheInsertions);
    }

    string[] residentPoolTypeNames =
    [
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TornadoEntity",
        "Magicka.GameLogic.Entities.SpellMine",
        "Magicka.GameLogic.Entities.MissileEntity",
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField",
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.VortexEntity",
        "Magicka.GameLogic.Entities.Items.Item",
        "Magicka.GameLogic.Entities.SprayEntity",
        "Magicka.GameLogic.Entities.ElementalEgg",
    ];
    HashSet<string> residentPoolTypes = new HashSet<string>(
        residentPoolTypeNames,
        StringComparer.Ordinal);
    foreach (string typeName in residentPoolTypeNames)
    {
        TypeDefinition type = RequireType(types, typeName);
        pooledHolderTypes.Add(typeName);
        _ = RequireMethod(
            type,
            "Deinitialize",
            parameterCount: 0);
    }

    int cacheSourceHooks = 0;
    foreach (string typeName in pooledHolderTypes.OrderBy(
                 name => name,
                 StringComparer.Ordinal))
    {
        TypeDefinition type = RequireType(types, typeName);
        foreach (MethodDefinition method in type.Methods.Where(
                     method => method.IsStatic
                               && method.HasBody
                               && method.ReturnType.FullName == type.FullName
                               && ReadsCacheOrPoolField(method)))
        {
            bool isResidentPool = residentPoolTypes.Contains(type.FullName);
            int hooks = InstrumentReturnValueAtReturns(
                method,
                isResidentPool
                    ? helpers.MarkResidentActive
                    : helpers.MarkActive,
                type.FullName + "." + method.Name);
            cacheSourceHooks += hooks;
            if (isResidentPool)
            {
                residentActiveHooks += hooks;
            }
            else
            {
                activeHooks += hooks;
            }
        }

        if (IsSameModuleSubclassOf(type, EntityTypeName, types))
        {
            continue;
        }

        foreach (MethodDefinition method in type.Methods.Where(
                     method => !method.IsStatic
                               && method.HasBody
                               && IsReuseEntryMethod(method.Name)))
        {
            activeHooks += InstrumentSelfAtEntry(
                method,
                helpers.MarkActive,
                type.FullName + "." + method.Name);
        }
    }

    const string avatarTypeName = "Magicka.GameLogic.Entities.Avatar";
    MethodDefinition avatarMissileSource = RequireMethod(
        RequireType(types, avatarTypeName),
        "GetMissileInstance",
        parameterCount: 0);
    if (avatarMissileSource.ReturnType.FullName
        != "Magicka.GameLogic.Entities.MissileEntity")
    {
        throw new InvalidOperationException(
            "Unexpected Avatar.GetMissileInstance return type: "
            + avatarMissileSource.ReturnType.FullName);
    }

    int avatarMissileHooks = InstrumentReturnValueAtReturns(
        avatarMissileSource,
        helpers.MarkResidentActive,
        avatarTypeName + ".GetMissileInstance");
    cacheSourceHooks += avatarMissileHooks;
    residentActiveHooks += avatarMissileHooks;

    if (cacheSourceHooks == 0)
    {
        throw new InvalidOperationException(
            "Expected at least one cache-source return hook.");
    }

    const string cthulhuMistTypeName =
        "Magicka.GameLogic.Entities.Bosses.CthulhuMist";
    deactivatedHooks += InstrumentSelfAtEntry(
        RequireMethod(
            RequireType(types, cthulhuMistTypeName),
            "Deactivate",
            parameterCount: 0),
        helpers.MarkDeactivated,
        cthulhuMistTypeName + ".Deactivate");

    const string itemTypeName = "Magicka.GameLogic.Entities.Items.Item";
    MethodDefinition itemReinitialize = RequireMethod(
        RequireType(types, itemTypeName),
        "Reinitialize",
        parameterCount: 1);
    activeHooks += InstrumentSelfAtReturns(
        itemReinitialize,
        helpers.MarkActive,
        itemTypeName + ".Reinitialize");

    string[] complexTypes =
    [
        PlayStateTypeName,
        "Magicka.Levels.Level",
        "Magicka.Levels.GameScene",
        "Magicka.Levels.LevelModel",
        "Magicka.GameLogic.Entities.CharacterTemplate",
        "Magicka.GameLogic.Entities.PhysicsEntityTemplate",
    ];

    foreach (string typeName in complexTypes)
    {
        TypeDefinition type = RequireType(types, typeName);
        foreach (MethodDefinition constructor in type.Methods.Where(
                     method => method.IsConstructor
                               && !method.IsStatic
                               && method.HasBody))
        {
            if (typeName == PlayStateTypeName)
            {
                registrations += InstrumentSelfAfterBaseConstructor(
                    constructor,
                    helpers.BeginEpoch,
                    PlayStateTypeName + "..ctor");
            }
            else
            {
                registrations += InstrumentSelfAtReturns(
                    constructor,
                    helpers.Register,
                    typeName + "..ctor");
            }
        }

        foreach (MethodDefinition dispose in type.Methods.Where(
                     method => method.Name == "Dispose"
                               && !method.IsStatic
                               && method.HasBody
                               && method.ReturnType.FullName == "System.Void"))
        {
            collectHooks += InstrumentSelfAtEntry(
                dispose,
                helpers.MarkMustCollect,
                typeName + ".Dispose");
        }
    }

    TypeDefinition playState = RequireType(types, PlayStateTypeName);
    MethodDefinition playStateDispose = RequireMethod(
        playState,
        "Dispose",
        parameterCount: 0);
    TypeDefinition gameState = RequireType(types, GameStateTypeName);
    MethodDefinition sceneGetter = RequireMethod(
        gameState,
        "get_Scene",
        parameterCount: 0);
    collectHooks += InstrumentRelatedAtEntry(
        playStateDispose,
        module.ImportReference(sceneGetter),
        helpers.MarkMustCollect,
        PlayStateTypeName + ".Dispose.Scene");
    checkpointHooks += InstrumentCheckpointAtReturns(
        playStateDispose,
        helpers.Checkpoint,
        PlayStateTypeName + ".Dispose");

    WriteAssembly(assembly, outputPath);
    return new PatchReport(
        Path.GetFileName(inputPath),
        registrations,
        activeHooks,
        residentActiveHooks,
        collectHooks,
        deactivatedHooks,
        detachHooks,
        checkpointHooks);
}

static PatchReport PatchPolygonHead(
    string inputPath,
    string outputPath,
    HelperMethods sourceHelpers)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    EnsureNotAlreadyPatched(assembly);
    ModuleDefinition module = assembly.MainModule;
    HelperMethods helpers = sourceHelpers.ImportInto(module);
    Dictionary<string, TypeDefinition> types = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);

    RepairLightSceneDetach(types);

    int registrations = 0;
    int collectHooks = 0;
    TypeDefinition biTreeModel = RequireType(
        types,
        "PolygonHead.Models.BiTreeModel");
    foreach (MethodDefinition constructor in biTreeModel.Methods.Where(
                 method => method.IsConstructor && !method.IsStatic && method.HasBody))
    {
        registrations += InstrumentSelfAtReturns(
            constructor,
            helpers.Register,
            "PolygonHead.Models.BiTreeModel..ctor");
    }

    foreach (MethodDefinition dispose in biTreeModel.Methods.Where(
                 method => method.Name == "Dispose"
                           && !method.IsStatic
                           && method.HasBody
                           && method.ReturnType.FullName == "System.Void"))
    {
        collectHooks += InstrumentSelfAtEntry(
            dispose,
            helpers.MarkMustCollect,
            "PolygonHead.Models.BiTreeModel.Dispose");
    }

    WriteAssembly(assembly, outputPath);
    return new PatchReport(
        Path.GetFileName(inputPath),
        registrations,
        ActiveHooks: 0,
        ResidentActiveHooks: 0,
        collectHooks,
        DeactivatedHooks: 0,
        DetachHooks: 0,
        CheckpointHooks: 0);
}

static void PatchCharacterTemplateStaticCachesOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairCharacterTemplateStaticCaches(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static (int DirectReferences, int TotalReferences) NormalizeSelfReferencesOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    return WriteAssembly(assembly, outputPath);
}

static int RestoreOriginalInterfaceOrder(
    string referencePath,
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition reference = ReadAssembly(referencePath);
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> targetTypes = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    int reorderedTypes = 0;
    foreach (TypeDefinition referenceType in AllTypes(reference.MainModule))
    {
        if (!targetTypes.TryGetValue(
                referenceType.FullName,
                out TypeDefinition? targetType))
        {
            continue;
        }

        string[] referenceOrder = referenceType.Interfaces
            .Select(item => item.InterfaceType.FullName)
            .ToArray();
        string[] targetOrder = targetType.Interfaces
            .Select(item => item.InterfaceType.FullName)
            .ToArray();
        if (referenceOrder.SequenceEqual(targetOrder, StringComparer.Ordinal))
        {
            continue;
        }

        string[] sortedReference = referenceOrder
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] sortedTarget = targetOrder
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (!sortedReference.SequenceEqual(sortedTarget, StringComparer.Ordinal))
        {
            continue;
        }

        Dictionary<string, Queue<InterfaceImplementation>> implementations =
            targetType.Interfaces
                .GroupBy(
                    item => item.InterfaceType.FullName,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => new Queue<InterfaceImplementation>(group),
                    StringComparer.Ordinal);
        targetType.Interfaces.Clear();
        foreach (string interfaceName in referenceOrder)
        {
            targetType.Interfaces.Add(implementations[interfaceName].Dequeue());
        }

        reorderedTypes++;
    }

    WriteAssembly(assembly, outputPath);
    return reorderedTypes;
}

static (int Methods, int Sites) RestoreOriginalLockLowering(
    string referencePath,
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition reference = ReadAssembly(referencePath);
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> referenceTypes = AllTypes(
            reference.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    int restoredMethods = 0;
    int restoredSites = 0;
    foreach (TypeDefinition targetType in AllTypes(assembly.MainModule))
    {
        if (!referenceTypes.TryGetValue(
                targetType.FullName,
                out TypeDefinition? referenceType))
        {
            continue;
        }

        foreach (MethodDefinition targetMethod in targetType.Methods)
        {
            if (!targetMethod.HasBody)
            {
                continue;
            }

            MethodDefinition? referenceMethod = referenceType.Methods
                .SingleOrDefault(method =>
                    method.Name == targetMethod.Name
                    && method.GenericParameters.Count
                        == targetMethod.GenericParameters.Count
                    && method.ReturnType.FullName
                        == targetMethod.ReturnType.FullName
                    && method.Parameters.Select(
                            parameter => parameter.ParameterType.FullName)
                        .SequenceEqual(
                            targetMethod.Parameters.Select(
                                parameter => parameter.ParameterType.FullName),
                            StringComparer.Ordinal));
            if (referenceMethod is null
                || !referenceMethod.HasBody
                || FindCachedLockEntries(referenceMethod).Count != 0)
            {
                continue;
            }

            IReadOnlyList<(Instruction Store, Instruction Load)>
                cachedEntries = FindCachedLockEntries(targetMethod);
            if (cachedEntries.Count == 0)
            {
                continue;
            }

            foreach ((Instruction store, Instruction load) in cachedEntries)
            {
                OpCode storeOpCode = store.OpCode;
                object? storeOperand = store.Operand;
                store.OpCode = OpCodes.Dup;
                store.Operand = null;
                load.OpCode = storeOpCode;
                load.Operand = storeOperand;
            }

            if (targetType.FullName
                    == "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell"
                && targetMethod.Name == "SpawnMissile"
                && targetMethod.Parameters.Count == 9)
            {
                RestoreProjectileFirstLockEntry(targetMethod);
            }

            targetMethod.Body.MaxStackSize = Math.Max(
                targetMethod.Body.MaxStackSize,
                referenceMethod.Body.MaxStackSize);
            restoredMethods++;
            restoredSites += cachedEntries.Count;
        }
    }

    WriteAssembly(assembly, outputPath);
    return (restoredMethods, restoredSites);
}

static void RestoreProjectileFirstLockEntry(MethodDefinition method)
{
    IList<Instruction> instructions = method.Body.Instructions;
    int enterIndex = instructions.ToList().FindIndex(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.Threading.Monitor"
        && called.Name == "Enter");
    if (enterIndex < 5
        || instructions[enterIndex - 5].OpCode != OpCodes.Ldsfld
        || instructions[enterIndex - 4].OpCode != OpCodes.Ldnull
        || StoredVariable(instructions[enterIndex - 3], method.Body) is null
        || instructions[enterIndex - 2].OpCode != OpCodes.Dup
        || StoredVariable(instructions[enterIndex - 1], method.Body) is null)
    {
        throw new InvalidOperationException(
            "Unexpected ProjectileSpell.SpawnMissile first lock entry.");
    }

    object field = instructions[enterIndex - 5].Operand;
    OpCode conditionStoreOpCode = instructions[enterIndex - 3].OpCode;
    object? conditionStoreOperand = instructions[enterIndex - 3].Operand;
    instructions[enterIndex - 5].OpCode = OpCodes.Ldnull;
    instructions[enterIndex - 5].Operand = null;
    instructions[enterIndex - 4].OpCode = conditionStoreOpCode;
    instructions[enterIndex - 4].Operand = conditionStoreOperand;
    instructions[enterIndex - 3].OpCode = OpCodes.Ldsfld;
    instructions[enterIndex - 3].Operand = field;
}

static IReadOnlyList<(Instruction Store, Instruction Load)>
    FindCachedLockEntries(MethodDefinition method)
{
    List<(Instruction Store, Instruction Load)> entries = [];
    IList<Instruction> instructions = method.Body.Instructions;
    for (int index = 2; index < instructions.Count; index++)
    {
        if (instructions[index].Operand is not MethodReference called
            || called.DeclaringType.FullName != "System.Threading.Monitor"
            || called.Name != "Enter")
        {
            continue;
        }

        VariableDefinition? loaded = LoadedVariable(
            instructions[index - 1],
            method.Body);
        VariableDefinition? stored = StoredVariable(
            instructions[index - 2],
            method.Body);
        if (loaded is not null && loaded == stored)
        {
            entries.Add((instructions[index - 2], instructions[index - 1]));
        }
    }
    return entries;
}

static VariableDefinition? LoadedVariable(
    Instruction instruction,
    MethodBody body)
{
    return instruction.OpCode.Code switch
    {
        Code.Ldloc_0 => body.Variables[0],
        Code.Ldloc_1 => body.Variables[1],
        Code.Ldloc_2 => body.Variables[2],
        Code.Ldloc_3 => body.Variables[3],
        Code.Ldloc or Code.Ldloc_S =>
            instruction.Operand as VariableDefinition,
        _ => null,
    };
}

static VariableDefinition? StoredVariable(
    Instruction instruction,
    MethodBody body)
{
    return instruction.OpCode.Code switch
    {
        Code.Stloc_0 => body.Variables[0],
        Code.Stloc_1 => body.Variables[1],
        Code.Stloc_2 => body.Variables[2],
        Code.Stloc_3 => body.Variables[3],
        Code.Stloc or Code.Stloc_S =>
            instruction.Operand as VariableDefinition,
        _ => null,
    };
}

static int RestoreRecompiledMethodBodies(
    string referencePath,
    string inputPath,
    string outputPath)
{
    (string TypeName, string MethodName, int ParameterCount)[] targets =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities"
                + ".GreaseLump",
            "OnCollision",
            2),
        ("Magicka.GameLogic.UI.SpellWheel", ".ctor", 2),
        ("Magicka.GameLogic.UI.Tome", "OnLoggedIn", 2),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "Draw",
            2),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "ControllerMouseMove",
            4),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "HitPackList",
            2),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "LevelValidation",
            0),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "PreloadTextures",
            0),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "DefaultAvatars",
            0),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "UpdateAvailableLevels",
            1),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "UpdateLevelDescriptions",
            0),
        (
            "Magicka.GameLogic.GameStates.Menu.Main"
                + ".SubMenuCharacterSelect",
            "OnLevelChange",
            3),
        (
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            "AnimationEnd",
            1),
        ("Magicka.GameLogic.GameStates.PlayState", "UpdateMiscA", 2),
        (
            "Magicka.GameLogic.GameStates.PlayState/State",
            "ApplyState",
            1),
        ("Magicka.GameLogic.GameStates.PlayState", "SyncPlayers", 1),
        ("Magicka.GameLogic.GameStates.PlayState", "SyncSpells", 1),
        (
            "Magicka.GameLogic.GameStates.PlayState",
            "SendLatestCheckpoint",
            1),
        (
            "Magicka.GameLogic.GameStates.PlayState",
            "get_IsGameEnded",
            0),
    ];

    using AssemblyDefinition reference = ReadAssembly(referencePath);
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> referenceTypes = AllTypes(
            reference.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    Dictionary<string, TypeDefinition> targetTypes = AllTypes(
            assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    BodyReferencePool referencePool = new BodyReferencePool(
        assembly.MainModule);
    foreach ((string typeName, string methodName, int parameterCount) in
             targets)
    {
        MethodDefinition sourceMethod = RequireMethod(
            RequireType(referenceTypes, typeName),
            methodName,
            parameterCount);
        MethodDefinition targetMethod = FindMatchingMethod(
            RequireType(targetTypes, typeName),
            sourceMethod);
        CloneMethodBody(
            sourceMethod,
            targetMethod,
            assembly.MainModule,
            targetTypes,
            referencePool);
    }

    TypeDefinition sourceShield = RequireType(
        referenceTypes,
        "Magicka.GameLogic.Entities.Shield");
    TypeDefinition targetShield = RequireType(
        targetTypes,
        "Magicka.GameLogic.Entities.Shield");
    MethodDefinition[] shieldDamageMethods = sourceShield.Methods
        .Where(method => method.Name == "InternalDamage"
            && method.Parameters.Count == 5)
        .ToArray();
    if (shieldDamageMethods.Length != 2)
    {
        throw new InvalidOperationException(
            "Expected both original Shield.InternalDamage overloads.");
    }
    foreach (MethodDefinition sourceMethod in shieldDamageMethods)
    {
        CloneMethodBody(
            sourceMethod,
            FindMatchingMethod(targetShield, sourceMethod),
            assembly.MainModule,
            targetTypes,
            referencePool);
    }

    RestoreShieldConstructor(
        sourceShield,
        targetShield,
        assembly.MainModule,
        targetTypes,
        referencePool);
    RestoreShieldInitialize(
        sourceShield,
        targetShield,
        assembly.MainModule,
        targetTypes,
        referencePool);

    TypeDefinition sourceMissile = RequireType(
        referenceTypes,
        "Magicka.GameLogic.Entities.MissileEntity");
    TypeDefinition targetMissile = RequireType(
        targetTypes,
        "Magicka.GameLogic.Entities.MissileEntity");
    foreach ((string methodName, int parameterCount) in new[]
             {
                 ("Update", 2),
                 ("OnCollision", 4),
             })
    {
        MethodDefinition sourceMethod = RequireMethod(
            sourceMissile,
            methodName,
            parameterCount);
        MethodDefinition targetMethod = FindMatchingMethod(
            targetMissile,
            sourceMethod);
        CloneMethodBody(
            sourceMethod,
            targetMethod,
            assembly.MainModule,
            targetTypes,
            referencePool);
        InsertEmptyMissileTargetHandle(targetMethod);
    }

    int playStateRestores = RestoreRecentPlayStateMethods(
        referenceTypes,
        targetTypes,
        assembly.MainModule,
        referencePool);
    int iconRendererRestores = RestoreIconRendererMethods(
        referenceTypes,
        targetTypes,
        assembly.MainModule,
        referencePool);
    int networkServerRestores = RestoreNetworkServerMethods(
        referencePath,
        inputPath,
        referenceTypes,
        targetTypes,
        assembly.MainModule,
        referencePool);
    int networkClientRestores = RestoreNetworkClientMethods(
        inputPath,
        referenceTypes,
        targetTypes,
        assembly.MainModule,
        referencePool);

    WriteAssembly(assembly, outputPath);
    return targets.Length + shieldDamageMethods.Length + 4
        + playStateRestores + iconRendererRestores + networkServerRestores
        + networkClientRestores;
}

static int RestoreNetworkClientMethods(
    string semanticPath,
    IReadOnlyDictionary<string, TypeDefinition> referenceTypes,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    ModuleDefinition targetModule,
    BodyReferencePool referencePool)
{
    const string TypeName = "Magicka.Network.NetworkClient";
    TypeDefinition originalType = RequireType(referenceTypes, TypeName);
    TypeDefinition targetType = RequireType(targetTypes, TypeName);
    using AssemblyDefinition semanticAssembly = ReadAssembly(semanticPath);
    Dictionary<string, TypeDefinition> semanticTypes = AllTypes(
            semanticAssembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    TypeDefinition semanticType = RequireType(semanticTypes, TypeName);

    MethodDefinition originalRead = RequireMethod(originalType, "ReadMessage", 2);
    MethodDefinition semanticRead = RequireMethod(semanticType, "ReadMessage", 2);
    MethodDefinition targetRead = FindMatchingMethod(targetType, originalRead);
    CloneMethodBody(
        originalRead,
        targetRead,
        targetModule,
        targetTypes,
        referencePool);
    foreach (string messageType in new[]
             {
                 "Magicka.Network.TriggerActionMessage",
                 "Magicka.Network.EntityUpdateMessage",
                 "Magicka.Network.CharacterActionMessage",
                 "Magicka.Network.SpawnPlayerMessage",
                 "Magicka.Network.SpawnMissileMessage",
                 "Magicka.Network.SpawnShieldMessage",
                 "Magicka.Network.SpawnBarrierMessage",
                 "Magicka.Network.SpawnWaveMessage",
                 "Magicka.Network.SpawnMineMessage",
                 "Magicka.Network.SpawnVortexMessage",
                 "Magicka.Network.EntityRemoveMessage",
                 "Magicka.Network.CharacterDieMessage",
                 "Magicka.Network.MissileEntityEventMessage",
                 "Magicka.Network.DamageRequestMessage",
             })
    {
        ReplaceMessageCaseFragments(
            semanticRead,
            targetRead,
            messageType,
            targetModule,
            targetTypes,
            referencePool,
            useMainTryExit: false);
    }

    MethodDefinition originalInitializer = originalType.Methods.Single(
        method => method.Name == ".cctor");
    CloneMethodBody(
        originalInitializer,
        FindMatchingMethod(targetType, originalInitializer),
        targetModule,
        targetTypes,
        referencePool);

    MethodDefinition? authenticationHelper = targetType.Methods.SingleOrDefault(
        method => method.Name == "InitiateAuthentication");
    if (authenticationHelper is not null
        && AllTypes(targetModule).SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions
            : Enumerable.Empty<Instruction>())
        .Any(instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == authenticationHelper.DeclaringType.FullName
            && called.FullName == authenticationHelper.FullName))
    {
        throw new InvalidOperationException(
            "Cannot remove referenced NetworkClient authentication helper.");
    }
    if (authenticationHelper is not null)
    {
        targetType.Methods.Remove(authenticationHelper);
    }

    targetRead.Body.MaxStackSize = Math.Max(
        targetRead.Body.MaxStackSize,
        semanticRead.Body.MaxStackSize);
    ExpandShortBranches(targetRead);
    targetRead.Body.OptimizeMacros();
    return 2;
}

static int RestoreNetworkClientMethodsOnly(
    string referencePath,
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition reference = ReadAssembly(referencePath);
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> referenceTypes = AllTypes(
            reference.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    Dictionary<string, TypeDefinition> targetTypes = AllTypes(
            assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    BodyReferencePool referencePool = new BodyReferencePool(
        assembly.MainModule);
    int restored = RestoreNetworkClientMethods(
        inputPath,
        referenceTypes,
        targetTypes,
        assembly.MainModule,
        referencePool);
    WriteAssembly(assembly, outputPath);
    return restored;
}

static int RestoreNetworkServerMethods(
    string referencePath,
    string semanticPath,
    IReadOnlyDictionary<string, TypeDefinition> referenceTypes,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    ModuleDefinition targetModule,
    BodyReferencePool referencePool)
{
    const string TypeName = "Magicka.Network.NetworkServer";
    TypeDefinition originalType = RequireType(referenceTypes, TypeName);
    TypeDefinition targetType = RequireType(targetTypes, TypeName);
    using AssemblyDefinition semanticAssembly = ReadAssembly(semanticPath);
    Dictionary<string, TypeDefinition> semanticTypes = AllTypes(
            semanticAssembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    TypeDefinition semanticType = RequireType(semanticTypes, TypeName);

    MethodDefinition originalRead = RequireMethod(originalType, "ReadMessage", 2);
    MethodDefinition semanticRead = RequireMethod(semanticType, "ReadMessage", 2);
    MethodDefinition targetRead = FindMatchingMethod(targetType, originalRead);
    NeutralizeOriginalNullReferenceHandler(originalRead);
    CloneMethodBody(
        originalRead,
        targetRead,
        targetModule,
        targetTypes,
        referencePool);
    foreach (string messageType in new[]
             {
                 "Magicka.Network.SpawnShieldRequestMessage",
                 "Magicka.Network.SpawnBarrierRequestMessage",
                 "Magicka.Network.SpawnWaveRequestMessage",
                 "Magicka.Network.SpawnMineRequestMessage",
                 "Magicka.Network.SpawnVortexMessage",
                 "Magicka.Network.DamageRequestMessage",
                 "Magicka.Network.EntityUpdateMessage",
                 "Magicka.Network.CharacterActionMessage",
                 "Magicka.Network.SpawnMissileMessage",
                 "Magicka.Network.MissileEntityEventMessage",
                 "Magicka.Network.RequestForcedPlayerStatusSync",
             })
    {
        ReplaceMessageCaseFragments(
            semanticRead,
            targetRead,
            messageType,
            targetModule,
            targetTypes,
            referencePool);
    }
    ReplaceNullReferenceHandler(
        semanticRead,
        targetRead,
        targetModule,
        targetTypes,
        referencePool);
    RestoreNetworkServerForcedSyncExit(targetRead);

    RestoreHotjoinBroadcastMethod(
        originalType,
        semanticType,
        targetType,
        targetModule,
        targetTypes,
        referencePool);
    MethodDefinition semanticForcedSync = semanticType.Methods.Single(method =>
        method.Name == "SendForcedSyncMessageToClient"
        && method.Parameters.Count == 2
        && method.Parameters[0].ParameterType.FullName == "System.Int32");
    CloneMethodBody(
        semanticForcedSync,
        FindMatchingMethod(targetType, semanticForcedSync),
        targetModule,
        targetTypes,
        referencePool);

    foreach (string helperName in new[]
             {
                 "AuthenticateSteamUser",
                 "SendCheckpointRaw",
             })
    {
        MethodDefinition? helper = targetType.Methods.SingleOrDefault(
            method => method.Name == helperName);
        if (helper is null)
        {
            continue;
        }
        if (AllTypes(targetModule).SelectMany(type => type.Methods)
            .SelectMany(method => method.HasBody
                ? method.Body.Instructions
                : Enumerable.Empty<Instruction>())
            .Any(instruction => instruction.Operand is MethodReference called
                && called.DeclaringType.FullName == helper.DeclaringType.FullName
                && called.FullName == helper.FullName))
        {
            throw new InvalidOperationException(
                "Cannot remove referenced NetworkServer helper " + helperName);
        }
        targetType.Methods.Remove(helper);
    }

    targetRead.Body.MaxStackSize = Math.Max(
        targetRead.Body.MaxStackSize,
        semanticRead.Body.MaxStackSize);
    OrderExceptionHandlersByNesting(targetRead);
    ExpandShortBranches(targetRead);
    targetRead.Body.OptimizeMacros();
    return 3;
}

static void RepairNetworkServerHandlerOrderOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    TypeDefinition networkServer = AllTypes(assembly.MainModule).Single(type =>
        type.FullName == "Magicka.Network.NetworkServer");
    MethodDefinition readMessage = RequireMethod(
        networkServer,
        "ReadMessage",
        parameterCount: 2);
    OrderExceptionHandlersByNesting(readMessage);
    WriteAssembly(assembly, outputPath);
}

static void RepairNetworkServerForcedSyncExitOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    TypeDefinition networkServer = AllTypes(assembly.MainModule).Single(type =>
        type.FullName == "Magicka.Network.NetworkServer");
    MethodDefinition readMessage = RequireMethod(
        networkServer,
        "ReadMessage",
        parameterCount: 2);
    RestoreNetworkServerForcedSyncExit(readMessage);
    WriteAssembly(assembly, outputPath);
}

static void RestoreNetworkServerForcedSyncExit(MethodDefinition readMessage)
{
    Instruction[] instructions = readMessage.Body.Instructions.ToArray();
    Instruction[] sendCalls = instructions.Where(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "Magicka.Network.NetworkServer"
            && called.Name == "SendForcedSyncMessageToClient"
            && called.Parameters.Count == 2
            && called.Parameters[0].ParameterType.FullName
                == "SteamWrapper.SteamID"
            && called.Parameters[1].ParameterType.FullName == "System.Boolean")
        .ToArray();
    if (sendCalls.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one SteamID forced-sync send in NetworkServer.ReadMessage;"
            + " found " + sendCalls.Length + ".");
    }

    Instruction? branch = NextNonNop(sendCalls[0]);
    if (branch?.OpCode.FlowControl != FlowControl.Branch
        || branch.Operand is not Instruction)
    {
        branch = Instruction.Create(OpCodes.Br, MainTryExit(readMessage));
        readMessage.Body.GetILProcessor().InsertAfter(sendCalls[0], branch);
        return;
    }

    branch.OpCode = OpCodes.Br;
    branch.Operand = MainTryExit(readMessage);
}

static Instruction? NextNonNop(Instruction instruction)
{
    Instruction? next = instruction.Next;
    while (next?.OpCode == OpCodes.Nop)
    {
        next = next.Next;
    }
    return next;
}

static void OrderExceptionHandlersByNesting(MethodDefinition method)
{
    List<Instruction> instructions = method.Body.Instructions.ToList();
    ExceptionHandler[] original = method.Body.ExceptionHandlers.ToArray();
    ExceptionHandler[] ordered = original
        .Select((handler, index) => new { Handler = handler, Index = index })
        .OrderByDescending(item =>
            ExceptionHandlerNestingDepth(item.Handler, original, instructions))
        .ThenBy(item => item.Index)
        .Select(item => item.Handler)
        .ToArray();
    if (original.SequenceEqual(ordered))
    {
        return;
    }
    method.Body.ExceptionHandlers.Clear();
    foreach (ExceptionHandler handler in ordered)
    {
        method.Body.ExceptionHandlers.Add(handler);
    }
}

static int ExceptionHandlerNestingDepth(
    ExceptionHandler candidate,
    IReadOnlyCollection<ExceptionHandler> handlers,
    List<Instruction> instructions)
{
    int start = instructions.IndexOf(candidate.TryStart);
    int end = instructions.IndexOf(candidate.TryEnd);
    return handlers.Count(container =>
    {
        if (container == candidate)
        {
            return false;
        }
        int containerStart = instructions.IndexOf(container.TryStart);
        int containerEnd = instructions.IndexOf(container.TryEnd);
        return containerStart <= start
            && end <= containerEnd
            && (containerStart < start || end < containerEnd);
    });
}

static void ExpandShortBranches(MethodDefinition method)
{
    foreach (Instruction instruction in method.Body.Instructions)
    {
        instruction.OpCode = instruction.OpCode.Code switch
        {
            Code.Br_S => OpCodes.Br,
            Code.Brfalse_S => OpCodes.Brfalse,
            Code.Brtrue_S => OpCodes.Brtrue,
            Code.Beq_S => OpCodes.Beq,
            Code.Bge_S => OpCodes.Bge,
            Code.Bge_Un_S => OpCodes.Bge_Un,
            Code.Bgt_S => OpCodes.Bgt,
            Code.Bgt_Un_S => OpCodes.Bgt_Un,
            Code.Ble_S => OpCodes.Ble,
            Code.Ble_Un_S => OpCodes.Ble_Un,
            Code.Blt_S => OpCodes.Blt,
            Code.Blt_Un_S => OpCodes.Blt_Un,
            Code.Bne_Un_S => OpCodes.Bne_Un,
            Code.Leave_S => OpCodes.Leave,
            _ => instruction.OpCode,
        };
    }
}

static void NeutralizeOriginalNullReferenceHandler(MethodDefinition method)
{
    ExceptionHandler handler = method.Body.ExceptionHandlers.Single(candidate =>
        candidate.HandlerType == ExceptionHandlerType.Catch
        && candidate.CatchType?.FullName == "System.NullReferenceException");
    ExceptionHandler preceding = method.Body.ExceptionHandlers.Single(candidate =>
        candidate.HandlerType == ExceptionHandlerType.Catch
        && candidate.CatchType?.FullName == "System.IO.IOException");
    Instruction ret = method.Body.Instructions[^1];
    List<Instruction> obsolete = [];
    for (Instruction? instruction = handler.HandlerStart;
         instruction is not null && instruction != handler.HandlerEnd;
         instruction = instruction.Next)
    {
        obsolete.Add(instruction);
    }
    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction instruction in obsolete)
    {
        processor.Remove(instruction);
    }
    Instruction pop = Instruction.Create(OpCodes.Pop);
    Instruction leave = Instruction.Create(OpCodes.Leave, ret);
    processor.InsertBefore(ret, pop);
    processor.InsertBefore(ret, leave);
    handler.HandlerStart = pop;
    handler.HandlerEnd = ret;
    preceding.HandlerEnd = pop;
}

static void RestoreHotjoinBroadcastMethod(
    TypeDefinition originalType,
    TypeDefinition semanticType,
    TypeDefinition targetType,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool)
{
    MethodDefinition original = originalType.Methods.Single(method =>
        method.Name == "SendMessage"
        && method.Parameters.Count == 2
        && method.Parameters[1].ParameterType.FullName
            == "SteamWrapper.P2PSend");
    MethodDefinition semantic = FindMatchingMethod(semanticType, original);
    MethodDefinition target = FindMatchingMethod(targetType, original);
    CloneMethodBody(
        original,
        target,
        targetModule,
        targetTypes,
        referencePool);

    MethodReference report = RequireBodyCall(
        semantic,
        "Magicka.CommunityPatch.NetworkLifecycleCompatibility",
        "ReportHotjoinBroadcastContinue");
    MethodReference getCount = RequireBodyCall(
        target,
        "System.Collections.Generic.List`1<Magicka.Network.NetworkServer/Connection>",
        "get_Count");
    FieldReference clients = targetType.Fields.Single(
        field => field.Name == "mClients");
    FieldReference sendable = target.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<FieldReference>()
        .Single(field => field.DeclaringType.FullName
            == "Magicka.Network.CachedMessage" && field.Name == "Sendable");
    Instruction add = target.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "Magicka.GameLogic.Player"
        && called.Name == "AddSyncMessage");
    Instruction oldExit = add.Next ?? throw new InvalidOperationException(
        "Missing original hotjoin exit.");
    if (oldExit.OpCode.Code is not Code.Leave and not Code.Leave_S)
    {
        throw new InvalidOperationException("Unexpected original hotjoin exit.");
    }
    VariableDefinition cached = target.Body.Variables.Single(variable =>
        variable.VariableType.FullName == "Magicka.Network.CachedMessage");
    VariableDefinition index = target.Body.Variables[1];
    Instruction loopIncrement = target.Body.Instructions
        .Skip(target.Body.Instructions.IndexOf(oldExit) + 1)
        .First(instruction => LoadedVariable(instruction, target.Body) == index
            && instruction.Next?.OpCode == OpCodes.Ldc_I4_1
            && instruction.Next.Next?.OpCode == OpCodes.Add);
    ILProcessor processor = target.Body.GetILProcessor();
    oldExit.OpCode = OpCodes.Ldloca;
    oldExit.Operand = cached;
    foreach (Instruction instruction in new[]
             {
                 Instruction.Create(OpCodes.Ldfld, sendable),
                 Instruction.Create(OpCodes.Ldloc, index),
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldfld, clients),
                 Instruction.Create(OpCodes.Callvirt, getCount),
                 Instruction.Create(OpCodes.Call, referencePool.RequireMethod(report)),
                 Instruction.Create(OpCodes.Br, loopIncrement),
             })
    {
        processor.InsertAfter(oldExit, instruction);
        oldExit = instruction;
    }
    target.Body.MaxStackSize = Math.Max(target.Body.MaxStackSize, 3);
}

static void ReplaceMessageCaseFragments(
    MethodDefinition semantic,
    MethodDefinition target,
    string messageType,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool,
    bool useMainTryExit = true)
{
    Instruction[] semanticStarts = FindMessageCaseStarts(semantic, messageType);
    Instruction[] targetStarts = FindMessageCaseStarts(target, messageType);
    if (semanticStarts.Length != targetStarts.Length || semanticStarts.Length == 0)
    {
        throw new InvalidOperationException(
            $"Unexpected {messageType} case count: semantic "
            + semanticStarts.Length + ", original " + targetStarts.Length);
    }
    for (int index = 0; index < semanticStarts.Length; index++)
    {
        CloneCaseFragment(
            semantic,
            target,
            semanticStarts[index],
            targetStarts[index],
            targetModule,
            targetTypes,
            referencePool,
            useMainTryExit);
    }
}

static Instruction[] FindMessageCaseStarts(
    MethodDefinition method,
    string messageType)
{
    List<Instruction> instructions = method.Body.Instructions.ToList();
    return instructions.Where(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == messageType
            && called.Name == "Read")
        .Select(read =>
        {
            int readIndex = instructions.IndexOf(read);
            int initIndex = instructions.FindLastIndex(
                readIndex - 1,
                Math.Min(8, readIndex),
                instruction => instruction.OpCode == OpCodes.Initobj
                    && instruction.Operand is TypeReference type
                    && type.FullName == messageType);
            if (initIndex < 1)
            {
                throw new InvalidOperationException(
                    "Message initializer not found for " + messageType);
            }
            int start = initIndex - 1;
            while (start > 0
                   && instructions[start].OpCode == OpCodes.Dup
                   && LoadedVariable(instructions[start - 1], method.Body)
                       is not null)
            {
                start--;
            }
            return instructions[start];
        })
        .ToArray();
}

static void CloneCaseFragment(
    MethodDefinition semantic,
    MethodDefinition target,
    Instruction semanticStart,
    Instruction targetStart,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool,
    bool useMainTryExit)
{
    Instruction semanticExit = useMainTryExit
        ? MainTryExit(semantic)
        : semantic.Body.Instructions[^1];
    Instruction targetExit = useMainTryExit
        ? MainTryExit(target)
        : target.Body.Instructions[^1];
    HashSet<Instruction> replacedFragment = ReachableFragment(
        target,
        targetStart,
        targetExit);
    HashSet<Instruction> fragment = ReachableFragment(
        semantic,
        semanticStart,
        semanticExit);
    CloneFragment(
        semantic,
        target,
        fragment,
        semanticStart,
        targetStart,
        semanticExit,
        targetExit,
        targetModule,
        targetTypes,
        referencePool,
        replaceHandler: null,
        reusableVariables: ReferencedVariables(target, replacedFragment));
    RemoveReplacedFragment(target, replacedFragment);
}

static void ReplaceNullReferenceHandler(
    MethodDefinition semantic,
    MethodDefinition target,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool)
{
    ExceptionHandler semanticIoHandler = semantic.Body.ExceptionHandlers.Single(
        handler => handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.IO.IOException");
    ExceptionHandler semanticHandler = semantic.Body.ExceptionHandlers.Single(
        handler => handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.NullReferenceException"
            && handler.TryStart == semanticIoHandler.TryStart);
    ExceptionHandler targetIoHandler = target.Body.ExceptionHandlers.Single(
        handler => handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.IO.IOException");
    ExceptionHandler targetHandler = target.Body.ExceptionHandlers.Single(
        handler => handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName == "System.NullReferenceException"
            && handler.TryStart == targetIoHandler.TryStart);
    HashSet<Instruction> replacedFragment = InstructionsInRange(
        target,
        targetHandler.HandlerStart,
        targetHandler.HandlerEnd);
    HashSet<Instruction> fragment = InstructionsInRange(
        semantic,
        semanticHandler.HandlerStart,
        semanticHandler.HandlerEnd);
    CloneFragment(
        semantic,
        target,
        fragment,
        semanticHandler.HandlerStart,
        targetHandler.HandlerStart,
        semantic.Body.Instructions[^1],
        target.Body.Instructions[^1],
        targetModule,
        targetTypes,
        referencePool,
        targetHandler,
        ReferencedVariables(target, replacedFragment));
    targetIoHandler.HandlerEnd = targetHandler.HandlerStart;
    RemoveReplacedFragment(target, replacedFragment);
}

static void RemoveReplacedFragment(
    MethodDefinition method,
    HashSet<Instruction> fragment)
{
    foreach (ExceptionHandler handler in method.Body.ExceptionHandlers
                 .Where(handler => fragment.Contains(handler.TryStart))
                 .ToArray())
    {
        method.Body.ExceptionHandlers.Remove(handler);
    }
    foreach (Instruction instruction in method.Body.Instructions
                 .Where(instruction => !fragment.Contains(instruction)))
    {
        bool referencesRemoved = instruction.Operand switch
        {
            Instruction target => fragment.Contains(target),
            Instruction[] targets => targets.Any(fragment.Contains),
            _ => false,
        };
        if (referencesRemoved)
        {
            throw new InvalidOperationException(
                "Live instruction still references a replaced fragment at IL_"
                + instruction.Offset.ToString("x4") + ": "
                + instruction.OpCode + " " + instruction.Operand);
        }
    }
    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction instruction in method.Body.Instructions
                 .Where(fragment.Contains)
                 .ToArray())
    {
        processor.Remove(instruction);
    }
}

static Instruction MainTryExit(MethodDefinition method)
{
    ExceptionHandler ioHandler = method.Body.ExceptionHandlers.Single(handler =>
        handler.HandlerType == ExceptionHandlerType.Catch
        && handler.CatchType?.FullName == "System.IO.IOException");
    return ioHandler.TryEnd.Previous ?? throw new InvalidOperationException(
        "Main ReadMessage leave was not found.");
}

static HashSet<Instruction> ReachableFragment(
    MethodDefinition method,
    Instruction start,
    Instruction exit)
{
    HashSet<Instruction> result = [];
    Queue<Instruction> pending = new();
    pending.Enqueue(start);
    while (pending.Count != 0)
    {
        Instruction instruction = pending.Dequeue();
        if (instruction == exit
            || instruction == method.Body.Instructions[^1]
            || !result.Add(instruction))
        {
            continue;
        }
        if (instruction.Operand is Instruction branch)
        {
            pending.Enqueue(branch);
        }
        else if (instruction.Operand is Instruction[] branches)
        {
            foreach (Instruction target in branches)
            {
                pending.Enqueue(target);
            }
        }
        if (instruction.OpCode.FlowControl is not FlowControl.Branch
            and not FlowControl.Return
            and not FlowControl.Throw
            && instruction.Next is not null)
        {
            pending.Enqueue(instruction.Next);
        }
    }
    foreach (ExceptionHandler handler in method.Body.ExceptionHandlers
                 .Where(handler => handler.TryStart != method.Body.Instructions[10]
                     && result.Contains(handler.TryStart)))
    {
        result.UnionWith(InstructionsInRange(
            method,
            handler.HandlerStart,
            handler.HandlerEnd));
    }
    return result;
}

static HashSet<Instruction> InstructionsInRange(
    MethodDefinition method,
    Instruction start,
    Instruction? end)
{
    HashSet<Instruction> result = [];
    for (Instruction? instruction = start;
         instruction is not null && instruction != end;
         instruction = instruction.Next)
    {
        result.Add(instruction);
    }
    return result;
}

static IReadOnlyList<VariableDefinition> ReferencedVariables(
    MethodDefinition method,
    IEnumerable<Instruction> instructions)
{
    List<VariableDefinition> result = [];
    foreach (Instruction instruction in instructions)
    {
        VariableDefinition? variable = instruction.Operand as VariableDefinition
            ?? instruction.OpCode.Code switch
            {
                Code.Ldloc_0 or Code.Stloc_0 => method.Body.Variables[0],
                Code.Ldloc_1 or Code.Stloc_1 => method.Body.Variables[1],
                Code.Ldloc_2 or Code.Stloc_2 => method.Body.Variables[2],
                Code.Ldloc_3 or Code.Stloc_3 => method.Body.Variables[3],
                _ => null,
            };
        if (variable is not null && !result.Contains(variable))
        {
            result.Add(variable);
        }
    }
    return result;
}

static void CloneFragment(
    MethodDefinition sourceMethod,
    MethodDefinition targetMethod,
    HashSet<Instruction> sourceFragment,
    Instruction sourceStart,
    Instruction replacedTargetStart,
    Instruction sourceExit,
    Instruction targetExit,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool,
    ExceptionHandler? replaceHandler,
    IReadOnlyList<VariableDefinition> reusableVariables)
{
    Dictionary<string, TypeDefinition> sourceMethodTypes = AllTypes(
            sourceMethod.Module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    VariableDefinition? sourceClosure = sourceMethod.Body.Variables.SingleOrDefault(
            variable => sourceMethodTypes.TryGetValue(
                variable.VariableType.FullName,
                out TypeDefinition? type)
            && type.Fields.Any(field => field.Name == "<>4__this"));
    VariableDefinition? targetClosure = targetMethod.Body.Variables.SingleOrDefault(
            variable => targetTypes.TryGetValue(
                variable.VariableType.FullName,
                out TypeDefinition? type)
            && type.Fields.Any(field => field.Name == "<>4__this"));
    TypeDefinition? sourceClosureType = sourceClosure is null
        ? null
        : sourceMethodTypes[sourceClosure.VariableType.FullName];
    TypeDefinition? targetClosureType = targetClosure is null
        ? null
        : targetTypes[targetClosure.VariableType.FullName];
    VariableDefinition sourcePacket = sourceMethod.Body.Variables.First(
        variable => variable.VariableType.FullName == "Magicka.Network.PacketType");
    VariableDefinition targetPacket = targetMethod.Body.Variables.First(
        variable => variable.VariableType.FullName == "Magicka.Network.PacketType");

    Dictionary<VariableDefinition, VariableDefinition> variables = new()
    {
        [sourcePacket] = targetPacket,
    };
    if (sourceClosure is not null && targetClosure is not null)
    {
        variables[sourceClosure] = targetClosure;
    }
    VariableDefinition[] sourceStateFlags = sourceMethod.Body.Variables
        .Where(variable => variable.VariableType.FullName == "System.Boolean")
        .Take(2)
        .ToArray();
    VariableDefinition[] targetStateFlags = targetMethod.Body.Variables
        .Where(variable => variable.VariableType.FullName == "System.Boolean")
        .Take(2)
        .ToArray();
    if (sourceStateFlags.Length == 2 && targetStateFlags.Length == 2)
    {
        variables[sourceStateFlags[0]] = targetStateFlags[0];
        variables[sourceStateFlags[1]] = targetStateFlags[1];
    }
    HashSet<VariableDefinition> claimedVariables = variables.Values.ToHashSet();
    foreach (VariableDefinition sourceVariable in ReferencedVariables(
                 sourceMethod,
                 sourceFragment))
    {
        if (variables.ContainsKey(sourceVariable))
        {
            continue;
        }
        VariableDefinition? targetVariable = reusableVariables.FirstOrDefault(
            candidate => !claimedVariables.Contains(candidate)
                && candidate.VariableType.FullName
                    == sourceVariable.VariableType.FullName);
        if (targetVariable is null)
        {
            targetVariable = new VariableDefinition(
                ImportBodyType(
                    sourceVariable.VariableType,
                    targetMethod,
                    targetModule,
                    targetTypes,
                    referencePool));
            targetMethod.Body.Variables.Add(targetVariable);
        }
        variables[sourceVariable] = targetVariable;
        claimedVariables.Add(targetVariable);
    }

    Dictionary<Instruction, Instruction> instructions = [];
    foreach (Instruction sourceInstruction in sourceMethod.Body.Instructions
                 .Where(sourceFragment.Contains))
    {
        Instruction clone = Instruction.Create(OpCodes.Nop);
        clone.OpCode = sourceInstruction.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3
                => OpCodes.Ldloc,
            Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3
                => OpCodes.Stloc,
            _ => sourceInstruction.OpCode,
        };
        instructions[sourceInstruction] = clone;
        targetMethod.Body.GetILProcessor().InsertBefore(
            replacedTargetStart,
            clone);
    }
    Instruction fragmentEnd = Instruction.Create(OpCodes.Nop);
    targetMethod.Body.GetILProcessor().InsertBefore(
        replacedTargetStart,
        fragmentEnd);
    Instruction clonedStart = instructions[sourceStart];
    HashSet<Instruction> sourceBoundaries = sourceMethod.Body.ExceptionHandlers
        .Where(handler => sourceFragment.Contains(handler.TryStart))
        .SelectMany(handler => new[] { handler.TryEnd, handler.HandlerEnd })
        .Where(instruction => instruction is not null)
        .Cast<Instruction>()
        .ToHashSet();

    object? ImportFragmentOperand(object? operand) => operand switch
    {
        null => null,
        Instruction instruction when instruction == sourceExit => targetExit,
        Instruction instruction when instructions.TryGetValue(
            instruction, out Instruction? clone) => clone,
        Instruction instruction when instruction == sourceMethod.Body.Instructions[^1]
            => targetMethod.Body.Instructions[^1],
        Instruction instruction when sourceBoundaries.Contains(instruction)
            => fragmentEnd,
        Instruction instruction => throw new InvalidOperationException(
            "Fragment branch escaped to IL_" + instruction.Offset.ToString("x4")),
        Instruction[] branches => branches.Select(branch =>
            (Instruction)(ImportFragmentOperand(branch)
                ?? throw new InvalidOperationException("Null branch"))).ToArray(),
        VariableDefinition variable => variables.TryGetValue(
            variable, out VariableDefinition? mapped)
                ? mapped
                : throw new InvalidOperationException(
                    "Unmapped fragment variable V" + variable.Index),
        ParameterDefinition parameter => targetMethod.Parameters[
            sourceMethod.Parameters.IndexOf(parameter)],
        FieldDefinition field when sourceClosureType is not null
                && targetClosureType is not null
                && field.DeclaringType == sourceClosureType
            => targetClosureType.Fields.Single(candidate =>
                candidate.Name == field.Name
                && candidate.FieldType.FullName == field.FieldType.FullName),
        FieldReference field when sourceClosureType is not null
                && targetClosureType is not null
                && field.DeclaringType.FullName == sourceClosureType.FullName
            => targetClosureType.Fields.Single(candidate =>
                candidate.Name == field.Name
                && candidate.FieldType.FullName == field.FieldType.FullName),
        MethodDefinition method => FindMatchingMethod(
            RequireType(targetTypes, method.DeclaringType.FullName), method),
        FieldDefinition field => RequireType(
                targetTypes,
                field.DeclaringType.FullName)
            .Fields.Single(candidate => candidate.Name == field.Name
                && candidate.FieldType.FullName == field.FieldType.FullName),
        TypeDefinition type => RequireType(targetTypes, type.FullName),
        MethodReference method => referencePool.RequireMethod(method),
        FieldReference field => referencePool.RequireField(field),
        TypeReference type => ImportBodyType(
            type,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool),
        CallSite callSite => referencePool.RequireCallSite(callSite),
        _ => operand,
    };
    foreach (Instruction sourceInstruction in sourceFragment)
    {
        object? sourceOperand = sourceInstruction.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => sourceMethod.Body.Variables[0],
            Code.Ldloc_1 or Code.Stloc_1 => sourceMethod.Body.Variables[1],
            Code.Ldloc_2 or Code.Stloc_2 => sourceMethod.Body.Variables[2],
            Code.Ldloc_3 or Code.Stloc_3 => sourceMethod.Body.Variables[3],
            _ => sourceInstruction.Operand,
        };
        instructions[sourceInstruction].Operand = ImportFragmentOperand(
            sourceOperand);
    }

    if (replaceHandler is null)
    {
        int incoming = 0;
        foreach (Instruction instruction in targetMethod.Body.Instructions
                     .Where(instruction => !instructions.Values.Contains(instruction)))
        {
            if (instruction.Operand == replacedTargetStart)
            {
                instruction.Operand = clonedStart;
                incoming++;
            }
            else if (instruction.Operand is Instruction[] targets)
            {
                for (int index = 0; index < targets.Length; index++)
                {
                    if (targets[index] == replacedTargetStart)
                    {
                        targets[index] = clonedStart;
                        incoming++;
                    }
                }
            }
        }
        if (incoming == 0)
        {
            throw new InvalidOperationException(
                "No incoming branch found for original case IL_"
                + replacedTargetStart.Offset.ToString("x4"));
        }
        foreach (ExceptionHandler handler in targetMethod.Body.ExceptionHandlers)
        {
            if (handler.TryStart == replacedTargetStart)
            {
                handler.TryStart = clonedStart;
            }
            if (handler.TryEnd == replacedTargetStart)
            {
                handler.TryEnd = clonedStart;
            }
            if (handler.HandlerStart == replacedTargetStart)
            {
                handler.HandlerStart = clonedStart;
            }
            if (handler.HandlerEnd == replacedTargetStart)
            {
                handler.HandlerEnd = clonedStart;
            }
            if (handler.FilterStart == replacedTargetStart)
            {
                handler.FilterStart = clonedStart;
            }
        }
    }
    else
    {
        replaceHandler.HandlerStart = clonedStart;
        replaceHandler.HandlerEnd = targetMethod.Body.Instructions[^1];
    }

    foreach (ExceptionHandler sourceHandler in sourceMethod.Body.ExceptionHandlers
                 .Where(handler => sourceFragment.Contains(handler.TryStart)))
    {
        targetMethod.Body.ExceptionHandlers.Add(new ExceptionHandler(
            sourceHandler.HandlerType)
        {
            TryStart = (Instruction)ImportFragmentOperand(sourceHandler.TryStart)!,
            TryEnd = (Instruction)ImportFragmentOperand(sourceHandler.TryEnd)!,
            HandlerStart = (Instruction)ImportFragmentOperand(
                sourceHandler.HandlerStart)!,
            HandlerEnd = (Instruction)ImportFragmentOperand(
                sourceHandler.HandlerEnd)!,
            FilterStart = sourceHandler.FilterStart is null
                ? null
                : (Instruction)ImportFragmentOperand(sourceHandler.FilterStart)!,
            CatchType = sourceHandler.CatchType is null
                ? null
                : ImportBodyType(
                    sourceHandler.CatchType,
                    targetMethod,
                    targetModule,
                    targetTypes,
                    referencePool),
        });
    }
    OrderExceptionHandlersByNesting(targetMethod);
}

static int RestoreIconRendererMethods(
    IReadOnlyDictionary<string, TypeDefinition> referenceTypes,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    ModuleDefinition targetModule,
    BodyReferencePool referencePool)
{
    const string IconRendererType = "Magicka.GameLogic.UI.IconRenderer";
    TypeDefinition sourceRenderer = RequireType(
        referenceTypes,
        IconRendererType);
    TypeDefinition targetRenderer = RequireType(targetTypes, IconRendererType);

    foreach ((string methodName, int parameterCount) in new[]
             {
                 (".ctor", 2),
                 ("Initialize", 1),
             })
    {
        MethodDefinition sourceMethod = RequireMethod(
            sourceRenderer,
            methodName,
            parameterCount);
        RemovePlayStateFieldStores(
            sourceMethod,
            IconRendererType,
            expectedCount: 1);
        CloneMethodBody(
            sourceMethod,
            FindMatchingMethod(targetRenderer, sourceMethod),
            targetModule,
            targetTypes,
            referencePool);
    }

    MethodDefinition sourceUpdate = RequireMethod(
        sourceRenderer,
        "Update",
        2);
    CloneMethodBody(
        sourceUpdate,
        FindMatchingMethod(targetRenderer, sourceUpdate),
        targetModule,
        targetTypes,
        referencePool);

    MethodDefinition sourceTomeMagick = RequireMethod(
        sourceRenderer,
        "set_TomeMagick",
        1);
    MethodDefinition targetTomeMagick = FindMatchingMethod(
        targetRenderer,
        sourceTomeMagick);
    MethodReference recentPlayState = RequireBodyCall(
        targetTomeMagick,
        "Magicka.GameLogic.GameStates.PlayState",
        "get_RecentPlayState");
    ReplacePlayStateFieldLoads(
        sourceTomeMagick,
        IconRendererType,
        recentPlayState,
        expectedCount: 1);
    CloneMethodBody(
        sourceTomeMagick,
        targetTomeMagick,
        targetModule,
        targetTypes,
        referencePool);

    const string RenderDataType = IconRendererType + "/RenderData";
    TypeDefinition sourceRenderData = RequireType(referenceTypes, RenderDataType);
    TypeDefinition targetRenderData = RequireType(targetTypes, RenderDataType);
    MethodDefinition sourceDraw = RequireMethod(sourceRenderData, "Draw", 1);
    MethodDefinition targetDraw = FindMatchingMethod(targetRenderData, sourceDraw);
    MethodReference adjustPosition = RequireBodyCall(
        targetDraw,
        "PolygonHead.CommunityPatch.InGameUiRenderScale",
        "AdjustProjectedPosition");
    FieldDefinition position = targetRenderData.Fields.Single(
        field => field.Name == "mPosition"
            && field.FieldType.FullName == "Microsoft.Xna.Framework.Vector2");
    CloneMethodBody(
        sourceDraw,
        targetDraw,
        targetModule,
        targetTypes,
        referencePool);
    ILProcessor processor = targetDraw.Body.GetILProcessor();
    Instruction first = targetDraw.Body.Instructions[0];
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldflda, position));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, adjustPosition));
    targetDraw.Body.MaxStackSize = Math.Max(targetDraw.Body.MaxStackSize, 2);

    return 5;
}

static int RestoreRecentPlayStateMethods(
    IReadOnlyDictionary<string, TypeDefinition> referenceTypes,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    ModuleDefinition targetModule,
    BodyReferencePool referencePool)
{
    const string SpawnSlimeType =
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SpawnSlime";
    const string SpawnSlimeOverkillType =
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities"
        + ".SpawnSlimeOverkill";
    const string GreaseType =
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease";

    TypeDefinition sourceSpawnSlime = RequireType(
        referenceTypes,
        SpawnSlimeType);
    TypeDefinition targetSpawnSlime = RequireType(
        targetTypes,
        SpawnSlimeType);
    MethodDefinition targetCreateEntities = RequireMethod(
        targetSpawnSlime,
        "CreateEntities",
        1);
    MethodReference recentPlayState = RequireBodyCall(
        targetCreateEntities,
        "Magicka.GameLogic.GameStates.PlayState",
        "get_RecentPlayState");

    MethodDefinition sourceExecute = RequireMethod(
        sourceSpawnSlime,
        "Execute",
        3);
    RemovePlayStateFieldStores(
        sourceExecute,
        SpawnSlimeType,
        expectedCount: 1);
    CloneMethodBody(
        sourceExecute,
        FindMatchingMethod(targetSpawnSlime, sourceExecute),
        targetModule,
        targetTypes,
        referencePool);

    foreach ((string methodName, int parameterCount) in new[]
             {
                 ("CreateEntities", 1),
                 ("SpawnSlimes", 2),
             })
    {
        MethodDefinition sourceMethod = RequireMethod(
            sourceSpawnSlime,
            methodName,
            parameterCount);
        ReplacePlayStateFieldLoads(
            sourceMethod,
            SpawnSlimeType,
            recentPlayState,
            expectedCount: 1);
        CloneMethodBody(
            sourceMethod,
            FindMatchingMethod(targetSpawnSlime, sourceMethod),
            targetModule,
            targetTypes,
            referencePool);
    }

    TypeDefinition sourceOverkill = RequireType(
        referenceTypes,
        SpawnSlimeOverkillType);
    TypeDefinition targetOverkill = RequireType(
        targetTypes,
        SpawnSlimeOverkillType);
    MethodDefinition sourceOverkillExecute = RequireMethod(
        sourceOverkill,
        "Execute",
        3);
    RemovePlayStateFieldStores(
        sourceOverkillExecute,
        SpawnSlimeType,
        expectedCount: 1);
    CloneMethodBody(
        sourceOverkillExecute,
        FindMatchingMethod(targetOverkill, sourceOverkillExecute),
        targetModule,
        targetTypes,
        referencePool);

    TypeDefinition sourceGrease = RequireType(referenceTypes, GreaseType);
    TypeDefinition targetGrease = RequireType(targetTypes, GreaseType);
    MethodDefinition sourceGreaseUpdate = RequireMethod(
        sourceGrease,
        "Update",
        2);
    ReplacePlayStateFieldLoads(
        sourceGreaseUpdate,
        GreaseType,
        recentPlayState,
        expectedCount: 4);
    CloneMethodBody(
        sourceGreaseUpdate,
        FindMatchingMethod(targetGrease, sourceGreaseUpdate),
        targetModule,
        targetTypes,
        referencePool);

    return 5;
}

static void RemovePlayStateFieldStores(
    MethodDefinition method,
    string declaringType,
    int expectedCount)
{
    Instruction[] stores = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == declaringType
            && field.Name == "mPlayState")
        .ToArray();
    if (stores.Length != expectedCount
        || stores.Any(store => store.Previous?.Previous is null))
    {
        throw new InvalidOperationException(
            "Unexpected PlayState field stores in " + method.FullName);
    }

    foreach (Instruction store in stores)
    {
        Instruction value = store.Previous!;
        Instruction owner = value.Previous!;
        if (owner.OpCode.Code != Code.Ldarg_0
            || value.OpCode.Code is not Code.Ldarg
            and not Code.Ldarg_S
            and not Code.Ldarg_1
            and not Code.Ldarg_2
            and not Code.Ldarg_3)
        {
            throw new InvalidOperationException(
                "Unexpected PlayState assignment shape in " + method.FullName);
        }
        RemoveUntargetedInstructions(method, owner, value, store);
    }
}

static void ReplacePlayStateFieldLoads(
    MethodDefinition method,
    string declaringType,
    MethodReference recentPlayState,
    int expectedCount)
{
    Instruction[] loads = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ldfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == declaringType
            && field.Name == "mPlayState")
        .ToArray();
    if (loads.Length != expectedCount
        || loads.Any(load => load.Previous?.OpCode.Code != Code.Ldarg_0))
    {
        throw new InvalidOperationException(
            "Unexpected PlayState field loads in " + method.FullName);
    }

    foreach (Instruction load in loads)
    {
        load.Previous!.OpCode = OpCodes.Nop;
        load.Previous.Operand = null;
        load.OpCode = OpCodes.Call;
        load.Operand = recentPlayState;
    }
}

static void RemoveUntargetedInstructions(
    MethodDefinition method,
    params Instruction[] removed)
{
    HashSet<Instruction> targets = method.Body.Instructions
        .SelectMany(instruction => instruction.Operand switch
        {
            Instruction target => [target],
            Instruction[] many => many,
            _ => [],
        })
        .Concat(method.Body.ExceptionHandlers.SelectMany(handler => new[]
        {
            handler.TryStart,
            handler.TryEnd,
            handler.HandlerStart,
            handler.HandlerEnd,
            handler.FilterStart,
        }).Where(instruction => instruction is not null)!)
        .ToHashSet();
    if (removed.Any(targets.Contains))
    {
        throw new InvalidOperationException(
            "Cannot remove a targeted instruction from " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction instruction in removed)
    {
        processor.Remove(instruction);
    }
}

static void RestoreShieldConstructor(
    TypeDefinition sourceShield,
    TypeDefinition targetShield,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool)
{
    MethodDefinition sourceMethod = RequireMethod(sourceShield, ".ctor", 1);
    MethodDefinition targetMethod = FindMatchingMethod(targetShield, sourceMethod);
    MethodReference gameInstance = RequireBodyCall(
        targetMethod,
        "Magicka.Game",
        "get_Instance");
    MethodReference gameContent = RequireBodyCall(
        targetMethod,
        "Microsoft.Xna.Framework.Game",
        "get_Content");
    CloneMethodBody(
        sourceMethod,
        targetMethod,
        targetModule,
        targetTypes,
        referencePool);

    Instruction[] playStateContentCalls = targetMethod.Body.Instructions
        .Where(instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "Magicka.GameLogic.GameStates.PlayState"
            && called.Name == "get_Content")
        .ToArray();
    if (playStateContentCalls.Length != 2
        || playStateContentCalls.Any(call =>
            call.Previous is null
            || call.Previous.OpCode.Code != Code.Ldarg_1))
    {
        throw new InvalidOperationException(
            "Unexpected original Shield content accesses.");
    }

    foreach (Instruction contentCall in playStateContentCalls)
    {
        contentCall.Previous!.OpCode = OpCodes.Call;
        contentCall.Previous.Operand = gameInstance;
        contentCall.OpCode = OpCodes.Callvirt;
        contentCall.Operand = gameContent;
    }
}

static void RestoreShieldInitialize(
    TypeDefinition sourceShield,
    TypeDefinition targetShield,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool)
{
    MethodDefinition sourceMethod = RequireMethod(sourceShield, "Initialize", 7);
    MethodDefinition targetMethod = FindMatchingMethod(targetShield, sourceMethod);
    MethodReference markActive = RequireBodyCall(
        targetMethod,
        "Magicka.GcDiagnostics.RetentionRegistry",
        "MarkActive");
    CloneMethodBody(
        sourceMethod,
        targetMethod,
        targetModule,
        targetTypes,
        referencePool);
    int hooks = InstrumentSelfAtReturns(
        targetMethod,
        markActive,
        "Magicka.GameLogic.Entities.Shield.Initialize");
    if (hooks != 1)
    {
        throw new InvalidOperationException(
            "Expected one Shield.Initialize return hook.");
    }
}

static void InsertEmptyMissileTargetHandle(MethodDefinition method)
{
    Instruction[] handleStores = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName
                == "Magicka.Network.MissileEntityEventMessage"
            && field.Name == "Handle")
        .ToArray();
    FieldReference[] targetHandleFields = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName
                == "Magicka.Network.MissileEntityEventMessage"
            && field.Name == "TargetHandle")
        .Select(instruction => (FieldReference)instruction.Operand)
        .DistinctBy(field => field.FullName)
        .ToArray();
    if (handleStores.Length != 1 || targetHandleFields.Length != 1)
    {
        throw new InvalidOperationException(
            "Unexpected missile event-message field stores in "
            + method.FullName);
    }

    Instruction handleStore = handleStores[0];
    Instruction? addressLoad = handleStore.Previous;
    while (addressLoad is not null
           && addressLoad.OpCode.Code is not Code.Ldloca
           and not Code.Ldloca_S)
    {
        addressLoad = addressLoad.Previous;
    }
    if (addressLoad?.Operand is not VariableDefinition message)
    {
        throw new InvalidOperationException(
            "Missile event-message address load was not found in "
            + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    Instruction loadAddress = Instruction.Create(OpCodes.Ldloca, message);
    Instruction emptyHandle = Instruction.Create(OpCodes.Ldc_I4, 65535);
    Instruction storeTarget = Instruction.Create(
        OpCodes.Stfld,
        targetHandleFields[0]);
    processor.InsertAfter(handleStore, loadAddress);
    processor.InsertAfter(loadAddress, emptyHandle);
    processor.InsertAfter(emptyHandle, storeTarget);
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 2);
}

static MethodReference RequireBodyCall(
    MethodDefinition method,
    string declaringType,
    string methodName)
{
    MethodReference[] matches = method.Body.Instructions
        .Where(instruction => instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == declaringType
            && called.Name == methodName)
        .Select(instruction => (MethodReference)instruction.Operand)
        .DistinctBy(called => called.FullName)
        .ToArray();
    return matches.Length == 1
        ? matches[0]
        : throw new InvalidOperationException(
            $"Expected one {declaringType}::{methodName} reference in "
            + method.FullName);
}

static int RestoreLocalRenameMethodBodies(
    string referencePath,
    string inputPath,
    string outputPath)
{
    (string TypeName, string MethodName, int ParameterCount)[] targets =
    [
        ("Magicka.GameLogic.GameStates.PlayState", "SyncEntities", 1),
        ("Magicka.GameLogic.GameStates.PlayState", "HandleWorldSync", 0),
        ("Magicka.GameLogic.Spells.ArcaneBlade/RenderData", "Draw", 2),
        ("Magicka.GameLogic.Entities.Items.Item", "Update", 2),
        ("Magicka.GameLogic.Entities.Items.Item", "MjolnirStrike", 1),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal"
                + "/PortalEntity/RenderData",
            "Draw",
            2),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Revive"
                + "/GodRayRenderData",
            "Draw",
            2),
        (
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
            "DrawAvatars",
            2),
    ];

    using AssemblyDefinition reference = ReadAssembly(referencePath);
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> referenceTypes = AllTypes(
            reference.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    Dictionary<string, TypeDefinition> targetTypes = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    BodyReferencePool referencePool = new BodyReferencePool(
        assembly.MainModule);
    foreach ((string typeName, string methodName, int parameterCount) in targets)
    {
        MethodDefinition sourceMethod = RequireMethod(
            RequireType(referenceTypes, typeName),
            methodName,
            parameterCount);
        MethodDefinition targetMethod = FindMatchingMethod(
            RequireType(targetTypes, typeName),
            sourceMethod);
        CloneMethodBody(
            sourceMethod,
            targetMethod,
            assembly.MainModule,
            targetTypes,
            referencePool);
    }

    WriteAssembly(assembly, outputPath);
    return targets.Length;
}

static MethodDefinition FindMatchingMethod(
    TypeDefinition targetType,
    MethodReference sourceMethod)
{
    MethodDefinition? match = targetType.Methods.SingleOrDefault(method =>
        method.Name == sourceMethod.Name
        && method.GenericParameters.Count == sourceMethod.GenericParameters.Count
        && method.ReturnType.FullName == sourceMethod.ReturnType.FullName
        && method.Parameters.Select(parameter => parameter.ParameterType.FullName)
            .SequenceEqual(
                sourceMethod.Parameters.Select(
                    parameter => parameter.ParameterType.FullName),
                StringComparer.Ordinal));
    return match ?? throw new InvalidOperationException(
        "No matching target method for " + sourceMethod.FullName
        + " on " + targetType.FullName
        + "; signature candidates: "
        + string.Join(", ", targetType.Methods.Where(method =>
                method.ReturnType.FullName == sourceMethod.ReturnType.FullName
                && method.Parameters.Select(
                        parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(
                        sourceMethod.Parameters.Select(
                            parameter => parameter.ParameterType.FullName),
                        StringComparer.Ordinal))
            .Select(method => method.Name)));
}

static void CloneMethodBody(
    MethodDefinition sourceMethod,
    MethodDefinition targetMethod,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool)
{
    if (!sourceMethod.HasBody || !targetMethod.HasBody)
    {
        throw new InvalidOperationException(
            "Cannot clone a missing method body: " + sourceMethod.FullName);
    }

    MethodBody sourceBody = sourceMethod.Body;
    MethodBody targetBody = new MethodBody(targetMethod)
    {
        InitLocals = sourceBody.InitLocals,
        MaxStackSize = sourceBody.MaxStackSize,
    };
    Dictionary<VariableDefinition, VariableDefinition> variables = [];
    foreach (VariableDefinition sourceVariable in sourceBody.Variables)
    {
        VariableDefinition targetVariable = new VariableDefinition(
            ImportBodyType(
                sourceVariable.VariableType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool));
        targetBody.Variables.Add(targetVariable);
        variables[sourceVariable] = targetVariable;
    }

    Dictionary<Instruction, Instruction> instructions = [];
    foreach (Instruction sourceInstruction in sourceBody.Instructions)
    {
        Instruction targetInstruction = Instruction.Create(OpCodes.Nop);
        targetInstruction.OpCode = sourceInstruction.OpCode;
        targetBody.Instructions.Add(targetInstruction);
        instructions[sourceInstruction] = targetInstruction;
    }

    foreach (Instruction sourceInstruction in sourceBody.Instructions)
    {
        instructions[sourceInstruction].Operand = ImportBodyOperand(
            sourceInstruction.Operand,
            sourceMethod,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool,
            variables,
            instructions);
    }

    foreach (ExceptionHandler sourceHandler in sourceBody.ExceptionHandlers)
    {
        targetBody.ExceptionHandlers.Add(new ExceptionHandler(
            sourceHandler.HandlerType)
        {
            TryStart = MapInstruction(sourceHandler.TryStart, instructions),
            TryEnd = MapInstruction(sourceHandler.TryEnd, instructions),
            HandlerStart = MapInstruction(
                sourceHandler.HandlerStart,
                instructions),
            HandlerEnd = MapInstruction(sourceHandler.HandlerEnd, instructions),
            FilterStart = MapInstruction(
                sourceHandler.FilterStart,
                instructions),
            CatchType = sourceHandler.CatchType is null
                ? null
                : ImportBodyType(
                    sourceHandler.CatchType,
                    targetMethod,
                    targetModule,
                    targetTypes,
                    referencePool),
        });
    }

    targetMethod.Body = targetBody;
}

static object? ImportBodyOperand(
    object? operand,
    MethodDefinition sourceMethod,
    MethodDefinition targetMethod,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool,
    IReadOnlyDictionary<VariableDefinition, VariableDefinition> variables,
    IReadOnlyDictionary<Instruction, Instruction> instructions)
{
    return operand switch
    {
        null => null,
        Instruction instruction => instructions[instruction],
        Instruction[] targets => targets.Select(instruction =>
            instructions[instruction]).ToArray(),
        VariableDefinition variable => variables[variable],
        ParameterDefinition parameter => targetMethod.Parameters[
            sourceMethod.Parameters.IndexOf(parameter)],
        MethodDefinition method => FindMatchingMethod(
            RequireType(targetTypes, method.DeclaringType.FullName),
            method),
        FieldDefinition field => RequireType(
                targetTypes,
                field.DeclaringType.FullName)
            .Fields.Single(candidate =>
                candidate.Name == field.Name
                && candidate.FieldType.FullName == field.FieldType.FullName),
        TypeDefinition type => RequireType(targetTypes, type.FullName),
        MethodReference method => referencePool.RequireMethod(method),
        FieldReference field => referencePool.RequireField(field),
        TypeReference type => ImportBodyType(
            type,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool),
        CallSite callSite => referencePool.RequireCallSite(callSite),
        _ => operand,
    };
}

static TypeReference ImportBodyType(
    TypeReference type,
    MethodDefinition targetMethod,
    ModuleDefinition targetModule,
    IReadOnlyDictionary<string, TypeDefinition> targetTypes,
    BodyReferencePool referencePool)
{
    if (targetTypes.TryGetValue(type.FullName, out TypeDefinition? targetType))
    {
        return targetType;
    }

    if (type is GenericParameter genericParameter)
    {
        IGenericParameterProvider targetOwner = genericParameter.Type
            == GenericParameterType.Method
                ? targetMethod
                : RequireType(
                    targetTypes,
                    ((TypeReference)genericParameter.Owner).FullName);
        return targetOwner.GenericParameters[genericParameter.Position];
    }

    if (type is ArrayType array)
    {
        return new ArrayType(
            ImportBodyType(
                array.ElementType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool),
            array.Rank);
    }
    if (type is ByReferenceType byReference)
    {
        return new ByReferenceType(ImportBodyType(
            byReference.ElementType,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool));
    }
    if (type is PointerType pointer)
    {
        return new PointerType(ImportBodyType(
            pointer.ElementType,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool));
    }
    if (type is PinnedType pinned)
    {
        return new PinnedType(ImportBodyType(
            pinned.ElementType,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool));
    }
    if (type is SentinelType sentinel)
    {
        return new SentinelType(ImportBodyType(
            sentinel.ElementType,
            targetMethod,
            targetModule,
            targetTypes,
            referencePool));
    }
    if (type is OptionalModifierType optionalModifier)
    {
        return new OptionalModifierType(
            ImportBodyType(
                optionalModifier.ModifierType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool),
            ImportBodyType(
                optionalModifier.ElementType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool));
    }
    if (type is RequiredModifierType requiredModifier)
    {
        return new RequiredModifierType(
            ImportBodyType(
                requiredModifier.ModifierType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool),
            ImportBodyType(
                requiredModifier.ElementType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool));
    }
    if (type is GenericInstanceType genericInstance)
    {
        GenericInstanceType imported = new GenericInstanceType(
            ImportBodyType(
                genericInstance.ElementType,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool));
        foreach (TypeReference argument in genericInstance.GenericArguments)
        {
            imported.GenericArguments.Add(ImportBodyType(
                argument,
                targetMethod,
                targetModule,
                targetTypes,
                referencePool));
        }
        return imported;
    }

    return referencePool.RequireType(type);
}

static Instruction? MapInstruction(
    Instruction? instruction,
    IReadOnlyDictionary<Instruction, Instruction> instructions)
{
    return instruction is null ? null : instructions[instruction];
}

static void PatchWarlordAbilityDiagnosticOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    PatchWarlordAbilityDiagnostic(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void PatchRailgunParentCycleOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairRailgunParentCycles(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void PatchJormungandrNullTargetOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairJormungandrNullTarget(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchGameThreadAffinityNullOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    ModuleDefinition module = assembly.MainModule;
    Dictionary<string, TypeDefinition> types = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    MethodDefinition constructor = RequireType(types, "Magicka.Game")
        .Methods.Single(method => method.IsConstructor
                                  && !method.IsStatic
                                  && method.Parameters.Count == 0);
    constructor.Body.SimplifyMacros();
    Instruction getCurrent = constructor.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Callvirt
        && instruction.Operand is MethodReference method
        && method.DeclaringType.FullName == "System.Collections.IEnumerator"
        && method.Name == "get_Current");
    Instruction castThread = getCurrent.Next;
    Instruction storeThread = castThread.Next;
    if (castThread.OpCode != OpCodes.Castclass
        || castThread.Operand is not TypeReference castType
        || castType.FullName != "System.Diagnostics.ProcessThread"
        || storeThread.OpCode != OpCodes.Stloc
        || storeThread.Operand is not VariableDefinition threadLocal)
    {
        throw new InvalidOperationException(
            "Unexpected ProcessThread assignment in Magicka.Game..ctor.");
    }

    Instruction moveNextCall = constructor.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Callvirt
        && instruction.Operand is MethodReference method
        && method.DeclaringType.FullName == "System.Collections.IEnumerator"
        && method.Name == "MoveNext");
    Instruction continueLoop = moveNextCall.Previous;
    if (continueLoop.OpCode != OpCodes.Ldloc
        || continueLoop.Operand is not VariableDefinition enumeratorLocal)
    {
        throw new InvalidOperationException(
            "Unexpected ProcessThread loop continuation in Magicka.Game..ctor.");
    }
    ILProcessor il = constructor.Body.GetILProcessor();
    Instruction loadThread = Instruction.Create(OpCodes.Ldloc, threadLocal);
    Instruction skipNullThread = Instruction.Create(OpCodes.Brfalse, continueLoop);
    il.InsertAfter(storeThread, loadThread);
    il.InsertAfter(loadThread, skipNullThread);
    constructor.Body.OptimizeMacros();

    WriteAssembly(assembly, outputPath);
}

static void PatchArrayEqualsNullOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairArrayEqualsNull(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairArrayEqualsNull(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    MethodDefinition arrayEquals = RequireMethod(
        RequireType(types, "Magicka.Helper"),
        "ArrayEquals",
        parameterCount: 2);
    if (arrayEquals.Parameters.Any(parameter =>
            parameter.ParameterType.FullName != "System.Byte[]")
        || arrayEquals.ReturnType.FullName != "System.Boolean")
    {
        throw new InvalidOperationException(
            "Unexpected Helper.ArrayEquals signature.");
    }

    Instruction originalEntry = arrayEquals.Body.Instructions[0];
    Instruction returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);
    ILProcessor processor = arrayEquals.Body.GetILProcessor();
    processor.InsertBefore(originalEntry, Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(
        originalEntry,
        Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.InsertBefore(originalEntry, Instruction.Create(OpCodes.Ldarg_1));
    processor.InsertBefore(
        originalEntry,
        Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(returnFalse);
    processor.Append(Instruction.Create(OpCodes.Ret));
    arrayEquals.Body.MaxStackSize = Math.Max(arrayEquals.Body.MaxStackSize, 1);
}

static void PatchAvatarFindInteractableNullOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairAvatarFindInteractableNull(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairAvatarFindInteractableNull(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    MethodDefinition findInteractable = RequireMethod(
        RequireType(types, "Magicka.GameLogic.Entities.Avatar"),
        "FindInteractable",
        parameterCount: 1);
    Instruction[] instructions = findInteractable.Body.Instructions.ToArray();
    if (instructions.Length < 7
        || instructions[0].OpCode != OpCodes.Ldarg_0
        || instructions[1].OpCode != OpCodes.Ldfld
        || instructions[1].Operand is not FieldReference playStateField
        || playStateField.FullName
            != "Magicka.GameLogic.GameStates.PlayState Magicka.GameLogic.Entities.Entity::mPlayState"
        || instructions[2].Operand is not MethodReference getLevel
        || getLevel.Name != "get_Level"
        || instructions[3].Operand is not MethodReference getCurrentScene
        || getCurrentScene.Name != "get_CurrentScene"
        || instructions[4].Operand is not MethodReference getTriggers
        || getTriggers.Name != "get_Triggers"
        || instructions[5].OpCode != OpCodes.Stloc_0)
    {
        throw new InvalidOperationException(
            "Unexpected Avatar.FindInteractable entry.");
    }

    VariableDefinition playState = new VariableDefinition(
        RequireType(types, "Magicka.GameLogic.GameStates.PlayState"));
    VariableDefinition level = new VariableDefinition(
        RequireType(types, "Magicka.Levels.Level"));
    VariableDefinition scene = new VariableDefinition(
        RequireType(types, "Magicka.Levels.GameScene"));
    findInteractable.Body.Variables.Add(playState);
    findInteractable.Body.Variables.Add(level);
    findInteractable.Body.Variables.Add(scene);
    findInteractable.Body.InitLocals = true;

    Instruction originalBody = instructions[6];
    Instruction returnNull = Instruction.Create(OpCodes.Ldnull);
    ILProcessor processor = findInteractable.Body.GetILProcessor();
    foreach (Instruction instruction in instructions.Take(6))
    {
        processor.Remove(instruction);
    }

    foreach (Instruction instruction in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldfld, playStateField),
                 Instruction.Create(OpCodes.Stloc, playState),
                 Instruction.Create(OpCodes.Ldloc, playState),
                 Instruction.Create(OpCodes.Brfalse, returnNull),
                 Instruction.Create(OpCodes.Ldloc, playState),
                 Instruction.Create(OpCodes.Callvirt, getLevel),
                 Instruction.Create(OpCodes.Stloc, level),
                 Instruction.Create(OpCodes.Ldloc, level),
                 Instruction.Create(OpCodes.Brfalse, returnNull),
                 Instruction.Create(OpCodes.Ldloc, level),
                 Instruction.Create(OpCodes.Callvirt, getCurrentScene),
                 Instruction.Create(OpCodes.Stloc, scene),
                 Instruction.Create(OpCodes.Ldloc, scene),
                 Instruction.Create(OpCodes.Brfalse, returnNull),
                 Instruction.Create(OpCodes.Ldloc, scene),
                 Instruction.Create(OpCodes.Callvirt, getTriggers),
                 Instruction.Create(OpCodes.Stloc_0),
                 Instruction.Create(OpCodes.Ldloc_0),
                 Instruction.Create(OpCodes.Brfalse, returnNull),
             })
    {
        processor.InsertBefore(originalBody, instruction);
    }
    processor.Append(returnNull);
    processor.Append(Instruction.Create(OpCodes.Ret));
    findInteractable.Body.MaxStackSize = Math.Max(
        findInteractable.Body.MaxStackSize,
        1);
}

static void PatchCharacterCastSpellGamerNullOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairCharacterCastSpellGamerNull(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairCharacterCastSpellGamerNull(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition guards = RequireType(
        types,
        "Magicka.CommunityPatch.RuntimeCompatibilityGuards");
    if (guards.Methods.Any(method =>
            method.Name == "CanRecordLocalSpellUsage"))
    {
        throw new InvalidOperationException(
            "RuntimeCompatibilityGuards.CanRecordLocalSpellUsage already exists.");
    }
    TypeDefinition gamerType = RequireType(types, "Magicka.Gamers.Gamer");
    TypeDefinition networkGamerType = RequireType(
        types,
        "Magicka.Gamers.NetworkGamer");
    MethodDefinition canRecord = new MethodDefinition(
        "CanRecordLocalSpellUsage",
        MethodAttributes.Public
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        guards.Module.TypeSystem.Boolean);
    canRecord.Parameters.Add(new ParameterDefinition(
        "gamer",
        ParameterAttributes.None,
        gamerType));
    ILProcessor helperProcessor = canRecord.Body.GetILProcessor();
    Instruction returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);
    helperProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    helperProcessor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    helperProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    helperProcessor.Append(Instruction.Create(OpCodes.Isinst, networkGamerType));
    helperProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    helperProcessor.Append(Instruction.Create(OpCodes.Ceq));
    helperProcessor.Append(Instruction.Create(OpCodes.Ret));
    helperProcessor.Append(returnFalse);
    helperProcessor.Append(Instruction.Create(OpCodes.Ret));
    canRecord.Body.MaxStackSize = 2;
    guards.Methods.Add(canRecord);

    MethodDefinition castSpell = RequireMethod(
        RequireType(types, "Magicka.GameLogic.Entities.Character"),
        "CastSpell",
        parameterCount: 2);
    Instruction[] instructions = castSpell.Body.Instructions.ToArray();
    Instruction usedElements = instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "Magicka.GameLogic.Profile"
        && called.Name == "UsedElements");
    int usedElementsIndex = Array.IndexOf(instructions, usedElements);
    Instruction getGamer = instructions.Take(usedElementsIndex)
        .Last(instruction => instruction.Operand is MethodReference called
                             && called.DeclaringType.FullName
                                 == "Magicka.GameLogic.Player"
                             && called.Name == "get_Gamer");
    Instruction isNetworkGamer = getGamer.Next
        ?? throw new InvalidOperationException(
            "Character.CastSpell Gamer check has no successor.");
    if (isNetworkGamer.OpCode != OpCodes.Isinst
        || isNetworkGamer.Operand is not TypeReference networkGamer
        || networkGamer.FullName != "Magicka.Gamers.NetworkGamer"
        || isNetworkGamer.Next is not Instruction skipStatisticsBranch
        || skipStatisticsBranch.OpCode.FlowControl != FlowControl.Cond_Branch
        || skipStatisticsBranch.Operand is not Instruction skipStatistics
        || Array.IndexOf(instructions, skipStatistics) <= usedElementsIndex)
    {
        throw new InvalidOperationException(
            "Unexpected Character.CastSpell statistics Gamer check.");
    }

    isNetworkGamer.OpCode = OpCodes.Call;
    isNetworkGamer.Operand = canRecord;
    skipStatisticsBranch.OpCode = skipStatisticsBranch.OpCode == OpCodes.Brtrue_S
        ? OpCodes.Brfalse_S
        : OpCodes.Brfalse;
}

static void PatchCharacterSelectDisposedIconOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairCharacterSelectDisposedIcon(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairCharacterSelectDisposedIcon(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    MethodDefinition drawWidget = RequireMethod(
        RequireType(
            types,
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect"),
        "DrawWidget",
        parameterCount: 1);
    TypeDefinition imageType = RequireType(
        types,
        "Magicka.GameLogic.UI.UISystem.Image");
    MethodDefinition getTexture = RequireMethod(
        imageType,
        "get_Texture",
        parameterCount: 0);
    MethodReference isDisposed = types.Values
        .SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions.ToArray()
            : Array.Empty<Instruction>())
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.DeclaringType.FullName
                         == "Microsoft.Xna.Framework.Graphics.GraphicsResource"
                         && method.Name == "get_IsDisposed");

    VariableDefinition image = new VariableDefinition(imageType);
    VariableDefinition texture = new VariableDefinition(
        drawWidget.Module.ImportReference(getTexture.ReturnType));
    drawWidget.Body.Variables.Add(image);
    drawWidget.Body.Variables.Add(texture);
    drawWidget.Body.InitLocals = true;

    Instruction originalBody = drawWidget.Body.Instructions[0];
    Instruction skipDraw = Instruction.Create(OpCodes.Ret);
    ILProcessor processor = drawWidget.Body.GetILProcessor();
    foreach (Instruction instruction in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_1),
                 Instruction.Create(OpCodes.Isinst, imageType),
                 Instruction.Create(OpCodes.Stloc, image),
                 Instruction.Create(OpCodes.Ldloc, image),
                 Instruction.Create(OpCodes.Brfalse, originalBody),
                 Instruction.Create(OpCodes.Ldloc, image),
                 Instruction.Create(OpCodes.Callvirt, getTexture),
                 Instruction.Create(OpCodes.Stloc, texture),
                 Instruction.Create(OpCodes.Ldloc, texture),
                 Instruction.Create(OpCodes.Brfalse, skipDraw),
                 Instruction.Create(OpCodes.Ldloc, texture),
                 Instruction.Create(OpCodes.Callvirt, isDisposed),
                 Instruction.Create(OpCodes.Brtrue, skipDraw),
             })
    {
        processor.InsertBefore(originalBody, instruction);
    }
    processor.Append(skipDraw);
    drawWidget.Body.MaxStackSize = Math.Max(drawWidget.Body.MaxStackSize, 1);
}

static void PatchNetworkClientRulesetTeardownOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairNetworkClientRulesetTeardown(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairNetworkClientRulesetTeardown(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition compatibility = RequireType(
        types,
        "Magicka.CommunityPatch.NetworkLifecycleCompatibility");
    if (compatibility.Methods.Any(method =>
            method.Name == "ApplyRulesetUpdate"))
    {
        throw new InvalidOperationException(
            "NetworkLifecycleCompatibility.ApplyRulesetUpdate already exists.");
    }
    TypeDefinition rulesetMessage = RequireType(
        types,
        "Magicka.Network.RulesetMessage");
    TypeDefinition playState = RequireType(
        types,
        "Magicka.GameLogic.GameStates.PlayState");
    TypeDefinition level = RequireType(types, "Magicka.Levels.Level");
    TypeDefinition gameScene = RequireType(types, "Magicka.Levels.GameScene");
    TypeDefinition ruleset = RequireType(types, "Magicka.Levels.IRuleset");

    MethodDefinition getRecentPlayState = RequireMethod(
        playState,
        "get_RecentPlayState",
        parameterCount: 0);
    MethodDefinition getLevel = RequireMethod(
        playState,
        "get_Level",
        parameterCount: 0);
    MethodDefinition getCurrentScene = RequireMethod(
        level,
        "get_CurrentScene",
        parameterCount: 0);
    MethodDefinition getRuleSet = RequireMethod(
        gameScene,
        "get_RuleSet",
        parameterCount: 0);
    MethodDefinition networkUpdate = RequireMethod(
        ruleset,
        "NetworkUpdate",
        parameterCount: 1);
    MethodDefinition sendDrop = RequireMethod(
        RequireType(types, "Magicka.CommunityPatch.PatchTelemetry"),
        "SendNetworkGuardDrop",
        parameterCount: 6);

    MethodDefinition helper = new MethodDefinition(
        "ApplyRulesetUpdate",
        MethodAttributes.Assembly
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        compatibility.Module.TypeSystem.Void);
    helper.Parameters.Add(new ParameterDefinition(
        "playState",
        ParameterAttributes.None,
        playState));
    helper.Parameters.Add(new ParameterDefinition(
        "iMsg",
        ParameterAttributes.None,
        new ByReferenceType(rulesetMessage)));
    VariableDefinition capturedLevel = new VariableDefinition(level);
    VariableDefinition capturedScene = new VariableDefinition(gameScene);
    VariableDefinition capturedRuleset = new VariableDefinition(ruleset);
    helper.Body.Variables.Add(capturedLevel);
    helper.Body.Variables.Add(capturedScene);
    helper.Body.Variables.Add(capturedRuleset);
    helper.Body.InitLocals = true;
    ILProcessor helperProcessor = helper.Body.GetILProcessor();
    Instruction drop = Instruction.Create(OpCodes.Ldstr, "client");
    foreach (Instruction instruction in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Brfalse, drop),
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Callvirt, getLevel),
                 Instruction.Create(OpCodes.Stloc, capturedLevel),
                 Instruction.Create(OpCodes.Ldloc, capturedLevel),
                 Instruction.Create(OpCodes.Brfalse, drop),
                 Instruction.Create(OpCodes.Ldloc, capturedLevel),
                 Instruction.Create(OpCodes.Callvirt, getCurrentScene),
                 Instruction.Create(OpCodes.Stloc, capturedScene),
                 Instruction.Create(OpCodes.Ldloc, capturedScene),
                 Instruction.Create(OpCodes.Brfalse, drop),
                 Instruction.Create(OpCodes.Ldloc, capturedScene),
                 Instruction.Create(OpCodes.Callvirt, getRuleSet),
                 Instruction.Create(OpCodes.Stloc, capturedRuleset),
                 Instruction.Create(OpCodes.Ldloc, capturedRuleset),
                 Instruction.Create(OpCodes.Brfalse, drop),
                 Instruction.Create(OpCodes.Ldloc, capturedRuleset),
                 Instruction.Create(OpCodes.Ldarg_1),
                 Instruction.Create(OpCodes.Callvirt, networkUpdate),
                 Instruction.Create(OpCodes.Ret),
             })
    {
        helperProcessor.Append(instruction);
    }
    helperProcessor.Append(drop);
    helperProcessor.Append(Instruction.Create(OpCodes.Ldstr, "RulesetUpdate"));
    helperProcessor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    helperProcessor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "ruleset_update_ignored_not_ready"));
    helperProcessor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    helperProcessor.Append(Instruction.Create(OpCodes.Call, sendDrop));
    helperProcessor.Append(Instruction.Create(OpCodes.Ret));
    helper.Body.MaxStackSize = 6;
    compatibility.Methods.Add(helper);

    MethodDefinition readMessage = RequireMethod(
        RequireType(types, "Magicka.Network.NetworkClient"),
        "ReadMessage",
        parameterCount: 2);
    Instruction updateCall = readMessage.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "Magicka.Levels.IRuleset"
        && called.Name == "NetworkUpdate");
    Instruction messageLoad = updateCall.Previous
        ?? throw new InvalidOperationException(
            "RulesetUpdate message load is missing.");
    if (messageLoad.OpCode.Code is not Code.Ldloca and not Code.Ldloca_S
        || messageLoad.Operand is not VariableDefinition messageVariable)
    {
        throw new InvalidOperationException(
            "Unexpected RulesetUpdate message load.");
    }
    Instruction getRuleSetCall = messageLoad.Previous!;
    Instruction getCurrentSceneCall = getRuleSetCall.Previous!;
    Instruction getLevelCall = getCurrentSceneCall.Previous!;
    Instruction playStateLoad = getLevelCall.Previous!;
    if (playStateLoad.Operand is not VariableDefinition playStateVariable
        || getLevelCall.Operand is not MethodReference existingGetLevel
        || existingGetLevel.Name != "get_Level"
        || getCurrentSceneCall.Operand is not MethodReference existingGetScene
        || existingGetScene.Name != "get_CurrentScene"
        || getRuleSetCall.Operand is not MethodReference existingGetRuleset
        || existingGetRuleset.Name != "get_RuleSet")
    {
        throw new InvalidOperationException(
            "Unexpected NetworkClient RulesetUpdate call chain.");
    }
    Instruction storePlayState = readMessage.Body.Instructions
        .Take(readMessage.Body.Instructions.IndexOf(playStateLoad))
        .Last(instruction => instruction.Operand == playStateVariable
                             && instruction.OpCode.Code
                                 is Code.Stloc or Code.Stloc_S);
    Instruction recentPlayStateCall = storePlayState.Previous!;
    if (recentPlayStateCall.Operand is not MethodReference existingRecent
        || existingRecent.Name != "get_RecentPlayState")
    {
        throw new InvalidOperationException(
            "RulesetUpdate RecentPlayState snapshot was not found.");
    }

    getLevelCall.OpCode = OpCodes.Ldloca;
    getLevelCall.Operand = messageVariable;
    getCurrentSceneCall.OpCode = OpCodes.Call;
    getCurrentSceneCall.Operand = helper;
    foreach (Instruction instruction in new[]
             {
                 getRuleSetCall,
                 messageLoad,
                 updateCall,
             })
    {
        instruction.OpCode = OpCodes.Nop;
        instruction.Operand = null;
    }
}

static void PatchGraphicsStartupErrorsOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairGraphicsStartupErrors(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void RepairGraphicsStartupErrors(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    _ = module;
    MethodDefinition gameConstructor = RequireType(types, "Magicka.Game")
        .Methods.Single(method => method.IsConstructor
                                  && !method.IsStatic
                                  && method.Parameters.Count == 0);
    Instruction applyCall = gameConstructor.Body.Instructions.Single(
        instruction => instruction.Operand is MethodReference called
                       && called.DeclaringType.FullName
                           == "Microsoft.Xna.Framework.GraphicsDeviceManager"
                       && called.Name == "ApplyChanges");
    MethodDefinition writeReport = RequireMethod(
        RequireType(types, "Magicka.Program"),
        "WriteReport",
        parameterCount: 2);
    MethodReference messageBox = types.Values
        .SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions.ToArray()
            : Array.Empty<Instruction>())
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.DeclaringType.FullName
                         == "System.Windows.Forms.MessageBox"
                         && method.Name == "Show"
                         && method.Parameters.Count == 4
                         && method.Parameters.All(parameter =>
                             parameter.ParameterType.FullName
                                 != "System.Windows.Forms.IWin32Window"));
    MethodReference[] existingCalls = types.Values
        .SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions.ToArray()
            : Array.Empty<Instruction>())
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .ToArray();
    MethodReference getType = existingCalls.First(method =>
        method.DeclaringType.FullName == "System.Object"
        && method.Name == "GetType"
        && method.Parameters.Count == 0);
    MethodReference getFullName = existingCalls.First(method =>
        method.DeclaringType.FullName == "System.Type"
        && method.Name == "get_FullName"
        && method.Parameters.Count == 0);
    MethodReference stringEquality = existingCalls.First(method =>
        method.DeclaringType.FullName == "System.String"
        && method.Name == "op_Equality"
        && method.Parameters.Count == 2);
    MethodReference getExceptionObject = existingCalls.First(method =>
        method.DeclaringType.FullName == "System.UnhandledExceptionEventArgs"
        && method.Name == "get_ExceptionObject"
        && method.Parameters.Count == 0);
    MethodReference objectToString = existingCalls.First(method =>
        method.DeclaringType.FullName == "System.Object"
        && method.Name == "ToString"
        && method.Parameters.Count == 0);
    MethodReference stringContains = existingCalls.First(method =>
        method.DeclaringType.FullName == "System.String"
        && method.Name == "Contains"
        && method.Parameters.Count == 1);
    TypeReference argumentException = existingCalls
        .Select(method => method.DeclaringType)
        .First(type => type.FullName == "System.ArgumentException");

    Instruction originalBody = writeReport.Body.Instructions[0];
    ILProcessor processor = writeReport.Body.GetILProcessor();
    Instruction hasException = Instruction.Create(
        OpCodes.Isinst,
        argumentException);
    Instruction checkNoSuitable = Instruction.Create(
        OpCodes.Ldarg_1);
    Instruction[] guardInstructions =
    [
        Instruction.Create(OpCodes.Ldarg_1),
        Instruction.Create(OpCodes.Callvirt, getExceptionObject),
        Instruction.Create(OpCodes.Dup),
        Instruction.Create(OpCodes.Brtrue, hasException),
        Instruction.Create(OpCodes.Pop),
        Instruction.Create(OpCodes.Br, originalBody),
        hasException,
        Instruction.Create(OpCodes.Brfalse, checkNoSuitable),
        Instruction.Create(OpCodes.Ldarg_1),
        Instruction.Create(OpCodes.Callvirt, getExceptionObject),
        Instruction.Create(OpCodes.Callvirt, objectToString),
        Instruction.Create(OpCodes.Ldstr, "Microsoft.Xna.Framework"),
        Instruction.Create(OpCodes.Callvirt, stringContains),
        Instruction.Create(OpCodes.Brfalse, originalBody),
        Instruction.Create(
            OpCodes.Ldstr,
            "Magicka could not map the selected graphics adapter to a monitor. "
            + "Update the graphics driver, disconnect virtual displays, and avoid Remote Desktop. "
            + "On Linux or Proton, also verify the display and Proton configuration."),
        Instruction.Create(OpCodes.Ldstr, "Magicka graphics startup error"),
        Instruction.Create(OpCodes.Ldc_I4_0),
        Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16),
        Instruction.Create(OpCodes.Call, messageBox),
        Instruction.Create(OpCodes.Pop),
        Instruction.Create(OpCodes.Br, originalBody),
        checkNoSuitable,
        Instruction.Create(OpCodes.Callvirt, getExceptionObject),
        Instruction.Create(OpCodes.Callvirt, getType),
        Instruction.Create(OpCodes.Callvirt, getFullName),
        Instruction.Create(
            OpCodes.Ldstr,
            "Microsoft.Xna.Framework.NoSuitableGraphicsDeviceException"),
        Instruction.Create(OpCodes.Call, stringEquality),
        Instruction.Create(OpCodes.Brfalse, originalBody),
        Instruction.Create(
            OpCodes.Ldstr,
            "Magicka could not find a suitable graphics device. "
            + "Install a supported graphics driver and verify the DirectX/XNA runtime. "
            + "On Linux or Proton, also verify the selected Proton version and display configuration."),
        Instruction.Create(OpCodes.Ldstr, "Magicka graphics startup error"),
        Instruction.Create(OpCodes.Ldc_I4_0),
        Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16),
        Instruction.Create(OpCodes.Call, messageBox),
        Instruction.Create(OpCodes.Pop),
        Instruction.Create(OpCodes.Br, originalBody),
    ];
    foreach (Instruction instruction in guardInstructions)
    {
        processor.InsertBefore(originalBody, instruction);
    }
    writeReport.Body.MaxStackSize = Math.Max(
        writeReport.Body.MaxStackSize,
        4);
}

static void PatchGcDiagnosticsStartupCheckOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairGcDiagnosticsStartupCheck(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairGcDiagnosticsStartupCheck(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    MethodDefinition main = RequireMethod(
        RequireType(types, "Magicka.Program"),
        "Main",
        parameterCount: 1);
    MethodReference[] calls = types.Values
        .SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions.ToArray()
            : Array.Empty<Instruction>())
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .ToArray();
    MethodReference setCurrentDirectory = calls.First(method =>
        method.DeclaringType.FullName == "System.IO.Directory"
        && method.Name == "SetCurrentDirectory"
        && method.Parameters.Count == 1);
    Instruction setDirectoryCall = main.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.FullName == setCurrentDirectory.FullName);
    Instruction originalNext = setDirectoryCall.Next
        ?? throw new InvalidOperationException(
            "Program.Main has no code after SetCurrentDirectory.");
    MethodReference executablePath = calls.First(method =>
        method.DeclaringType.FullName == "System.Windows.Forms.Application"
        && method.Name == "get_ExecutablePath");
    MethodReference getDirectoryName = calls.First(method =>
        method.DeclaringType.FullName == "System.IO.Path"
        && method.Name == "GetDirectoryName");
    MethodReference combine = calls.First(method =>
        method.DeclaringType.FullName == "System.IO.Path"
        && method.Name == "Combine"
        && method.Parameters.Count == 2);
    MethodReference fileExists = calls.First(method =>
        method.DeclaringType.FullName == "System.IO.File"
        && method.Name == "Exists"
        && method.Parameters.Count == 1);
    MethodReference messageBox = calls.First(method =>
        method.DeclaringType.FullName == "System.Windows.Forms.MessageBox"
        && method.Name == "Show"
        && method.Parameters.Count == 4
        && method.Parameters.All(parameter =>
            parameter.ParameterType.FullName
                != "System.Windows.Forms.IWin32Window"));
    ILProcessor processor = main.Body.GetILProcessor();
    foreach (Instruction instruction in new[]
             {
                 Instruction.Create(OpCodes.Call, executablePath),
                 Instruction.Create(OpCodes.Call, getDirectoryName),
                 Instruction.Create(OpCodes.Ldstr, "Magicka.GcDiagnostics.dll"),
                 Instruction.Create(OpCodes.Call, combine),
                 Instruction.Create(OpCodes.Call, fileExists),
                 Instruction.Create(OpCodes.Brtrue, originalNext),
                 Instruction.Create(
                     OpCodes.Ldstr,
                     "Magicka.GcDiagnostics.dll is missing from the game folder. "
                     + "Please include this file from the Community Patch or reinstall the complete patch. "
                     + "It is used to find the remaining memory leaks and out-of-memory errors."),
                 Instruction.Create(
                     OpCodes.Ldstr,
                     "Incomplete Community Patch installation"),
                 Instruction.Create(OpCodes.Ldc_I4_0),
                 Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16),
                 Instruction.Create(OpCodes.Call, messageBox),
                 Instruction.Create(OpCodes.Pop),
                 Instruction.Create(OpCodes.Ldc_I4_1),
                 Instruction.Create(OpCodes.Ret),
             })
    {
        processor.InsertBefore(originalNext, instruction);
    }
    main.Body.MaxStackSize = Math.Max(main.Body.MaxStackSize, 4);
}

static void PatchLevelHashMissingFileOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairLevelHashMissingFile(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void RepairLevelHashMissingFile(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    MethodDefinition computeHashes = RequireMethod(
        RequireType(types, "Magicka.Levels.Campaign.LevelManager"),
        "ComputeHashes",
        parameterCount: 0);
    if (computeHashes.Body.ExceptionHandlers.Count != 0)
    {
        AddExitToExistingLevelHashMissingFileHandler(
            module,
            computeHashes);
        return;
    }
    TypeReference fileNotFound = types.Values
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.ExceptionHandlers)
        .Select(handler => handler.CatchType)
        .OfType<TypeReference>()
        .First(type => type.FullName == "System.IO.FileNotFoundException");
    MethodReference getFileName = new MethodReference(
        "get_FileName",
        module.TypeSystem.String,
        fileNotFound)
    {
        HasThis = true,
    };
    MethodReference[] calls = types.Values
        .SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions.ToArray()
            : Array.Empty<Instruction>())
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .ToArray();
    MethodReference concat = calls.First(method =>
        method.DeclaringType.FullName == "System.String"
        && method.Name == "Concat"
        && method.Parameters.Count == 3
        && method.Parameters.All(parameter =>
            parameter.ParameterType.FullName == "System.String"));
    MethodReference messageBox = calls.First(method =>
        method.DeclaringType.FullName == "System.Windows.Forms.MessageBox"
        && method.Name == "Show"
        && method.Parameters.Count == 4
        && method.Parameters.All(parameter =>
            parameter.ParameterType.FullName
                != "System.Windows.Forms.IWin32Window"));
    MethodReference environmentExit = CreateEnvironmentExitReference(module);

    VariableDefinition exception = new VariableDefinition(fileNotFound);
    computeHashes.Body.Variables.Add(exception);
    computeHashes.Body.InitLocals = true;
    Instruction tryStart = computeHashes.Body.Instructions[0];
    Instruction originalReturn = computeHashes.Body.Instructions.Last();
    if (originalReturn.OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "Unexpected LevelManager.ComputeHashes exit.");
    }
    Instruction handlerStart = Instruction.Create(OpCodes.Stloc, exception);
    Instruction exit = Instruction.Create(OpCodes.Ret);
    originalReturn.OpCode = OpCodes.Leave;
    originalReturn.Operand = exit;
    ILProcessor processor = computeHashes.Body.GetILProcessor();
    processor.Append(handlerStart);
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "Magicka could not load this required level file:\n\n"));
    processor.Append(Instruction.Create(OpCodes.Ldloc, exception));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getFileName));
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "\n\nRestore the file or verify the game files. "
        + "Modded installations must provide every referenced level file."));
    processor.Append(Instruction.Create(OpCodes.Call, concat));
    processor.Append(Instruction.Create(OpCodes.Ldstr, "Missing level file"));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16));
    processor.Append(Instruction.Create(OpCodes.Call, messageBox));
    processor.Append(Instruction.Create(OpCodes.Pop));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    processor.Append(Instruction.Create(OpCodes.Call, environmentExit));
    processor.Append(Instruction.Create(OpCodes.Leave, exit));
    processor.Append(exit);
    computeHashes.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        TryStart = tryStart,
        TryEnd = handlerStart,
        HandlerStart = handlerStart,
        HandlerEnd = exit,
        CatchType = fileNotFound,
    });
    computeHashes.Body.MaxStackSize = Math.Max(
        computeHashes.Body.MaxStackSize,
        4);
}

static void AddExitToExistingLevelHashMissingFileHandler(
    ModuleDefinition module,
    MethodDefinition computeHashes)
{
    ExceptionHandler handler = computeHashes.Body.ExceptionHandlers.Single(
        candidate =>
            candidate.HandlerType == ExceptionHandlerType.Catch
            && candidate.CatchType?.FullName
                == "System.IO.FileNotFoundException");
    Instruction[] handlerBody = computeHashes.Body.Instructions
        .Skip(computeHashes.Body.Instructions.IndexOf(handler.HandlerStart))
        .Take(computeHashes.Body.Instructions.IndexOf(handler.HandlerEnd)
              - computeHashes.Body.Instructions.IndexOf(handler.HandlerStart))
        .ToArray();
    if (handlerBody.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Environment"
            && called.Name == "Exit"))
    {
        throw new InvalidOperationException(
            "LevelManager.ComputeHashes already exits after a missing file.");
    }
    Instruction messageBox = handlerBody.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName
            == "System.Windows.Forms.MessageBox"
        && called.Name == "Show");
    Instruction insertionPoint = messageBox.Next?.Next
        ?? throw new InvalidOperationException(
            "Missing-level dialog has no handler exit.");
    if (messageBox.Next.OpCode != OpCodes.Pop
        || (insertionPoint.OpCode != OpCodes.Leave
            && insertionPoint.OpCode != OpCodes.Leave_S))
    {
        throw new InvalidOperationException(
            "Unexpected missing-level dialog handler shape.");
    }
    ILProcessor processor = computeHashes.Body.GetILProcessor();
    processor.InsertBefore(
        insertionPoint,
        Instruction.Create(OpCodes.Ldc_I4_1));
    processor.InsertBefore(
        insertionPoint,
        Instruction.Create(
            OpCodes.Call,
            CreateEnvironmentExitReference(module)));
    computeHashes.Body.MaxStackSize = Math.Max(
        computeHashes.Body.MaxStackSize,
        4);
}

static MethodReference CreateEnvironmentExitReference(ModuleDefinition module)
{
    TypeReference environment = new TypeReference(
        "System",
        "Environment",
        module,
        module.TypeSystem.CoreLibrary);
    MethodReference exit = new MethodReference(
        "Exit",
        module.TypeSystem.Void,
        environment)
    {
        HasThis = false,
    };
    exit.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
    return exit;
}

static void PatchPolygonPayloadContractOnly(
    string magickaPath,
    string polygonHeadPath,
    string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    using AssemblyDefinition magicka = ReadAssembly(magickaPath);
    using AssemblyDefinition polygonHead = ReadAssembly(polygonHeadPath);
    Dictionary<string, TypeDefinition> magickaTypes = AllTypes(
            magicka.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);

    MethodDefinition compatibilityCheck = AddMagickaPayloadContract(
        magicka.MainModule,
        magickaTypes);
    AddPolygonHeadPayloadContract(polygonHead.MainModule);
    AddPolygonHeadStartupCheck(
        magicka.MainModule,
        magickaTypes,
        compatibilityCheck);

    WriteAssembly(
        magicka,
        Path.Combine(outputDirectory, "Magicka.exe"));
    WriteAssembly(
        polygonHead,
        Path.Combine(outputDirectory, "PolygonHead.dll"));
}

static MethodDefinition AddMagickaPayloadContract(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string TypeName = "Magicka.CommunityPatch.PayloadContract";
    if (types.ContainsKey(TypeName))
    {
        throw new InvalidOperationException(
            "Magicka payload contract already exists.");
    }

    TypeDefinition contract = CreatePayloadContractType(
        module,
        "Magicka.CommunityPatch");
    TypeReference typeType = new TypeReference(
        "System",
        "Type",
        module,
        module.TypeSystem.CoreLibrary);
    TypeReference fieldInfo = new TypeReference(
        "System.Reflection",
        "FieldInfo",
        module,
        module.TypeSystem.CoreLibrary);
    TypeReference methodInfo = new TypeReference(
        "System.Reflection",
        "MethodInfo",
        module,
        module.TypeSystem.CoreLibrary);
    TypeReference methodBase = new TypeReference(
        "System.Reflection",
        "MethodBase",
        module,
        module.TypeSystem.CoreLibrary);
    TypeReference runtimeTypeHandle = new TypeReference(
        "System",
        "RuntimeTypeHandle",
        module,
        module.TypeSystem.CoreLibrary,
        valueType: true);
    TypeReference typeArray = new ArrayType(typeType);

    MethodReference getType = new MethodReference(
        "GetType",
        typeType,
        typeType)
    {
        HasThis = false,
    };
    getType.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
    getType.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));
    MethodReference getTypeFromHandle = new MethodReference(
        "GetTypeFromHandle",
        typeType,
        typeType)
    {
        HasThis = false,
    };
    getTypeFromHandle.Parameters.Add(new ParameterDefinition(
        runtimeTypeHandle));
    MethodReference getField = CreateInstanceMethodReference(
        "GetField",
        typeType,
        fieldInfo,
        module.TypeSystem.String);
    MethodReference getRawConstant = CreateInstanceMethodReference(
        "GetRawConstantValue",
        fieldInfo,
        module.TypeSystem.Object);
    MethodReference stringEquals = new MethodReference(
        "op_Equality",
        module.TypeSystem.Boolean,
        module.TypeSystem.String)
    {
        HasThis = false,
    };
    stringEquals.Parameters.Add(new ParameterDefinition(
        module.TypeSystem.String));
    stringEquals.Parameters.Add(new ParameterDefinition(
        module.TypeSystem.String));
    MethodReference getMethod = CreateInstanceMethodReference(
        "GetMethod",
        typeType,
        methodInfo,
        module.TypeSystem.String,
        typeArray);
    MethodReference getIsStatic = CreateInstanceMethodReference(
        "get_IsStatic",
        methodBase,
        module.TypeSystem.Boolean);
    MethodReference getReturnType = CreateInstanceMethodReference(
        "get_ReturnType",
        methodInfo,
        typeType);

    TypeReference graphicsDevice = module.GetTypeReferences().First(type =>
        type.FullName
            == "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
    TypeReference renderTarget = module.GetTypeReferences().First(type =>
        type.FullName
            == "Microsoft.Xna.Framework.Graphics.RenderTarget2D");
    TypeReference depthStencil = module.GetTypeReferences().First(type =>
        type.FullName
            == "Microsoft.Xna.Framework.Graphics.DepthStencilBuffer");
    TypeReference point = module.GetTypeReferences().First(type =>
        type.FullName == "Microsoft.Xna.Framework.Point");

    MethodDefinition check = new MethodDefinition(
        "IsPolygonHeadCompatible",
        MethodAttributes.Public
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Boolean);
    contract.Methods.Add(check);
    VariableDefinition polygonContract = new VariableDefinition(typeType);
    VariableDefinition idField = new VariableDefinition(fieldInfo);
    VariableDefinition renderScale = new VariableDefinition(typeType);
    VariableDefinition begin = new VariableDefinition(methodInfo);
    VariableDefinition end = new VariableDefinition(methodInfo);
    check.Body.Variables.Add(polygonContract);
    check.Body.Variables.Add(idField);
    check.Body.Variables.Add(renderScale);
    check.Body.Variables.Add(begin);
    check.Body.Variables.Add(end);
    check.Body.InitLocals = true;

    ILProcessor processor = check.Body.GetILProcessor();
    Instruction returnFalse = Instruction.Create(OpCodes.Ldc_I4_0);
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "PolygonHead.CommunityPatch.PayloadContract, PolygonHead"));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Call, getType));
    processor.Append(Instruction.Create(OpCodes.Stloc, polygonContract));
    processor.Append(Instruction.Create(OpCodes.Ldloc, polygonContract));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldloc, polygonContract));
    processor.Append(Instruction.Create(OpCodes.Ldstr, "Id"));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getField));
    processor.Append(Instruction.Create(OpCodes.Stloc, idField));
    processor.Append(Instruction.Create(OpCodes.Ldloc, idField));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldstr, PayloadContractId));
    processor.Append(Instruction.Create(OpCodes.Ldloc, idField));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getRawConstant));
    processor.Append(Instruction.Create(
        OpCodes.Isinst,
        module.TypeSystem.String));
    processor.Append(Instruction.Create(OpCodes.Call, stringEquals));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "PolygonHead.CommunityPatch.InGameUiRenderScale, PolygonHead"));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Call, getType));
    processor.Append(Instruction.Create(OpCodes.Stloc, renderScale));
    processor.Append(Instruction.Create(OpCodes.Ldloc, renderScale));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));

    processor.Append(Instruction.Create(OpCodes.Ldloc, renderScale));
    processor.Append(Instruction.Create(OpCodes.Ldstr, "Begin"));
    AppendTypeArray(
        processor,
        typeType,
        getTypeFromHandle,
        graphicsDevice,
        renderTarget,
        point);
    processor.Append(Instruction.Create(OpCodes.Callvirt, getMethod));
    processor.Append(Instruction.Create(OpCodes.Stloc, begin));
    processor.Append(Instruction.Create(OpCodes.Ldloc, begin));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldloc, begin));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getIsStatic));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldloc, begin));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getReturnType));
    processor.Append(Instruction.Create(OpCodes.Ldtoken, point));
    processor.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));

    processor.Append(Instruction.Create(OpCodes.Ldloc, renderScale));
    processor.Append(Instruction.Create(OpCodes.Ldstr, "End"));
    AppendTypeArray(
        processor,
        typeType,
        getTypeFromHandle,
        graphicsDevice,
        renderTarget,
        depthStencil,
        point);
    processor.Append(Instruction.Create(OpCodes.Callvirt, getMethod));
    processor.Append(Instruction.Create(OpCodes.Stloc, end));
    processor.Append(Instruction.Create(OpCodes.Ldloc, end));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldloc, end));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getIsStatic));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldloc, end));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getReturnType));
    processor.Append(Instruction.Create(OpCodes.Ldtoken, module.TypeSystem.Void));
    processor.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnFalse));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    processor.Append(Instruction.Create(OpCodes.Ret));
    processor.Append(returnFalse);
    processor.Append(Instruction.Create(OpCodes.Ret));
    check.Body.MaxStackSize = 7;
    return check;
}

static void AppendTypeArray(
    ILProcessor processor,
    TypeReference typeType,
    MethodReference getTypeFromHandle,
    params TypeReference[] parameterTypes)
{
    processor.Append(Instruction.Create(
        OpCodes.Ldc_I4,
        parameterTypes.Length));
    processor.Append(Instruction.Create(OpCodes.Newarr, typeType));
    for (int index = 0; index < parameterTypes.Length; index++)
    {
        processor.Append(Instruction.Create(OpCodes.Dup));
        processor.Append(Instruction.Create(OpCodes.Ldc_I4, index));
        processor.Append(Instruction.Create(
            OpCodes.Ldtoken,
            parameterTypes[index]));
        processor.Append(Instruction.Create(
            OpCodes.Call,
            getTypeFromHandle));
        processor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    }
}

static void AddPolygonHeadPayloadContract(ModuleDefinition module)
{
    if (AllTypes(module).Any(type =>
            type.FullName
                == "PolygonHead.CommunityPatch.PayloadContract"))
    {
        throw new InvalidOperationException(
            "PolygonHead payload contract already exists.");
    }
    CreatePayloadContractType(module, "PolygonHead.CommunityPatch");
}

static TypeDefinition CreatePayloadContractType(
    ModuleDefinition module,
    string typeNamespace)
{
    TypeDefinition contract = new TypeDefinition(
        typeNamespace,
        "PayloadContract",
        TypeAttributes.Public
        | TypeAttributes.Abstract
        | TypeAttributes.Sealed
        | TypeAttributes.Class
        | TypeAttributes.BeforeFieldInit,
        module.TypeSystem.Object);
    contract.Fields.Add(new FieldDefinition(
        "Id",
        FieldAttributes.Public
        | FieldAttributes.Static
        | FieldAttributes.Literal
        | FieldAttributes.HasDefault,
        module.TypeSystem.String)
    {
        Constant = PayloadContractId,
    });
    module.Types.Add(contract);
    return contract;
}

static void AddPolygonHeadStartupCheck(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types,
    MethodDefinition compatibilityCheck)
{
    MethodDefinition main = RequireMethod(
        RequireType(types, "Magicka.Program"),
        "Main",
        parameterCount: 1);
    Instruction shaCreate = main.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName
            == "System.Security.Cryptography.SHA256"
        && called.Name == "Create");
    Instruction[] incomingBranches = main.Body.Instructions
        .Where(instruction => instruction.Operand == shaCreate)
        .ToArray();
    MethodReference messageBox = main.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method =>
            method.DeclaringType.FullName
                == "System.Windows.Forms.MessageBox"
            && method.Name == "Show"
            && method.Parameters.Count == 4
            && method.Parameters.All(parameter =>
                parameter.ParameterType.FullName
                    != "System.Windows.Forms.IWin32Window"));
    ILProcessor processor = main.Body.GetILProcessor();
    Instruction contractCall = Instruction.Create(
        OpCodes.Call,
        compatibilityCheck);
    foreach (Instruction instruction in new[]
             {
                 contractCall,
                 Instruction.Create(OpCodes.Brtrue, shaCreate),
                 Instruction.Create(
                     OpCodes.Ldstr,
                     "PolygonHead.dll does not match this Community Patch. "
                     + "Please include PolygonHead.dll from the same patch "
                     + "package or reinstall the complete patch."),
                 Instruction.Create(
                     OpCodes.Ldstr,
                     "Incomplete Community Patch installation"),
                 Instruction.Create(OpCodes.Ldc_I4_0),
                 Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16),
                 Instruction.Create(OpCodes.Call, messageBox),
                 Instruction.Create(OpCodes.Pop),
                 Instruction.Create(OpCodes.Ldc_I4_1),
                 Instruction.Create(OpCodes.Ret),
             })
    {
        processor.InsertBefore(shaCreate, instruction);
    }
    foreach (Instruction incoming in incomingBranches)
    {
        incoming.Operand = contractCall;
    }
    main.Body.MaxStackSize = Math.Max(main.Body.MaxStackSize, 4);
}

static void DiagnoseCharacterEntityUpdateOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    TypeDefinition message = RequireType(
        types, "Magicka.Network.EntityUpdateMessage");
    MethodDefinition read = RequireMethod(message, "Read", parameterCount: 1);
    Instruction throwInstruction = read.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Throw
            && instruction.Previous?.OpCode == OpCodes.Newobj
            && instruction.Previous.Operand is MethodReference constructor
            && constructor.DeclaringType.FullName
                == "System.NotImplementedException");
    Instruction createException = throwInstruction.Previous;
    createException.OpCode = OpCodes.Nop;
    createException.Operand = null;
    throwInstruction.OpCode = OpCodes.Nop;
    throwInstruction.Operand = null;

    TypeDefinition patchTelemetry = RequireType(
        types, "Magicka.CommunityPatch.PatchTelemetry");
    MethodReference sendDiagnostic = RequireMethod(
        patchTelemetry, "SendNetworkDiagnostic", parameterCount: 5);
    MethodReference format = new MethodReference(
        "Format", assembly.MainModule.TypeSystem.String,
        assembly.MainModule.TypeSystem.String)
    {
        HasThis = false,
    };
    format.Parameters.Add(new ParameterDefinition(
        assembly.MainModule.TypeSystem.String));
    format.Parameters.Add(new ParameterDefinition(
        new ArrayType(assembly.MainModule.TypeSystem.Object)));

    string[] fieldNames =
    [
        "Handle", "UDPStamp", "Features", "Position", "Direction",
        "Velocity", "Orientation", "HitPoints", "StatusEffects",
        "GenericBool", "GenericInt", "GenericFloat", "GenericUShort",
        "WanderAngle", "SelfShieldType", "SelfShieldHealth",
        "EtherealState",
    ];
    FieldDefinition[] fields = fieldNames.Select(name =>
        message.Fields.Single(field => field.Name == name)).ToArray();
    string detailsFormat = string.Join(";", fieldNames.Select(
        (name, index) => name + "={" + index + "}"));

    Instruction returnInstruction = read.Body.Instructions.Last();
    if (returnInstruction.OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "Unexpected EntityUpdateMessage.Read exit.");
    }
    Instruction[] existingExitBranches = read.Body.Instructions
        .Where(instruction => ReferenceEquals(
            instruction.Operand, returnInstruction))
        .ToArray();
    FieldDefinition features = fields.Single(field => field.Name == "Features");
    ILProcessor il = read.Body.GetILProcessor();
    List<Instruction> telemetry =
    [
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, features),
        Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)16),
        Instruction.Create(OpCodes.And),
        Instruction.Create(OpCodes.Conv_U2),
        Instruction.Create(OpCodes.Brfalse, returnInstruction),
        Instruction.Create(OpCodes.Ldstr, "network"),
        Instruction.Create(OpCodes.Ldstr, "EntityUpdate"),
        Instruction.Create(OpCodes.Ldstr, "entity_update_character_feature"),
        Instruction.Create(OpCodes.Ldstr, "Character"),
        Instruction.Create(OpCodes.Ldstr, detailsFormat),
        Instruction.Create(OpCodes.Ldc_I4, fields.Length),
        Instruction.Create(OpCodes.Newarr, assembly.MainModule.TypeSystem.Object),
    ];
    for (int index = 0; index < fields.Length; index++)
    {
        telemetry.Add(Instruction.Create(OpCodes.Dup));
        telemetry.Add(Instruction.Create(OpCodes.Ldc_I4, index));
        telemetry.Add(Instruction.Create(OpCodes.Ldarg_0));
        telemetry.Add(Instruction.Create(OpCodes.Ldfld, fields[index]));
        telemetry.Add(Instruction.Create(OpCodes.Box, fields[index].FieldType));
        telemetry.Add(Instruction.Create(OpCodes.Stelem_Ref));
    }
    telemetry.Add(Instruction.Create(OpCodes.Call, format));
    telemetry.Add(Instruction.Create(OpCodes.Call, sendDiagnostic));
    foreach (Instruction branch in existingExitBranches)
    {
        branch.Operand = telemetry[0];
    }
    foreach (Instruction instruction in telemetry)
    {
        il.InsertBefore(returnInstruction, instruction);
    }

    WriteAssembly(assembly, outputPath);
}

static void PatchTelemetryGameIntegrityOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    TypeDefinition patchTelemetry = RequireType(
        types, "Magicka.CommunityPatch.PatchTelemetry");
    if (patchTelemetry.Methods.Any(method =>
            method.Name == "GetGameIntegrityStatus"))
    {
        throw new InvalidOperationException(
            "PatchTelemetry.GetGameIntegrityStatus already exists.");
    }

    TypeDefinition hackHelper = RequireType(types, "Magicka.DRM.HackHelper");
    MethodReference getLicenseStatus = RequireMethod(
        hackHelper, "get_LicenseStatus", parameterCount: 0);
    MethodDefinition getIntegrity = new MethodDefinition(
        "GetGameIntegrityStatus",
        MethodAttributes.Private | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        assembly.MainModule.TypeSystem.String);
    patchTelemetry.Methods.Add(getIntegrity);
    ILProcessor statusIl = getIntegrity.Body.GetILProcessor();
    Instruction pendingTarget = Instruction.Create(OpCodes.Nop);
    Instruction originalTarget = Instruction.Create(OpCodes.Nop);
    Instruction original = Instruction.Create(OpCodes.Ldstr, "original");
    Instruction modded = Instruction.Create(OpCodes.Ldstr, "modded");
    Instruction unknown = Instruction.Create(OpCodes.Ldstr, "unknown");
    statusIl.Append(Instruction.Create(OpCodes.Call, getLicenseStatus));
    statusIl.Append(Instruction.Create(OpCodes.Dup));
    statusIl.Append(Instruction.Create(OpCodes.Brfalse_S, pendingTarget));
    statusIl.Append(Instruction.Create(OpCodes.Dup));
    statusIl.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    statusIl.Append(Instruction.Create(OpCodes.Beq_S, originalTarget));
    statusIl.Append(Instruction.Create(OpCodes.Ldc_I4_2));
    statusIl.Append(Instruction.Create(OpCodes.Beq_S, modded));
    statusIl.Append(Instruction.Create(OpCodes.Br_S, unknown));
    statusIl.Append(pendingTarget);
    statusIl.Append(Instruction.Create(OpCodes.Pop));
    statusIl.Append(Instruction.Create(OpCodes.Ldstr, "pending"));
    statusIl.Append(Instruction.Create(OpCodes.Ret));
    statusIl.Append(originalTarget);
    statusIl.Append(Instruction.Create(OpCodes.Pop));
    statusIl.Append(original);
    statusIl.Append(Instruction.Create(OpCodes.Ret));
    statusIl.Append(modded);
    statusIl.Append(Instruction.Create(OpCodes.Ret));
    statusIl.Append(unknown);
    statusIl.Append(Instruction.Create(OpCodes.Ret));

    MethodDefinition addCommonProperties = RequireMethod(
        patchTelemetry, "AddCommonProperties", parameterCount: 1);
    addCommonProperties.Body.SimplifyMacros();
    MethodReference dictionarySetter = addCommonProperties.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Callvirt)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.Name == "set_Item"
            && method.DeclaringType.FullName
                == addCommonProperties.Parameters[0].ParameterType.FullName
            && method.Parameters.Count == 2);
    Instruction commonReturn = addCommonProperties.Body.Instructions[^1];
    ILProcessor commonIl = addCommonProperties.Body.GetILProcessor();
    commonIl.InsertBefore(commonReturn, Instruction.Create(OpCodes.Ldarg_0));
    commonIl.InsertBefore(commonReturn, Instruction.Create(
        OpCodes.Ldstr, "game_integrity"));
    commonIl.InsertBefore(commonReturn, Instruction.Create(OpCodes.Call, getIntegrity));
    commonIl.InsertBefore(commonReturn, Instruction.Create(
        OpCodes.Callvirt, dictionarySetter));
    addCommonProperties.Body.OptimizeMacros();

    MethodDefinition sendBlocking = RequireMethod(
        patchTelemetry, "SendBlocking", parameterCount: 3);
    sendBlocking.Body.SimplifyMacros();
    Instruction buildJsonCall = sendBlocking.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference method
            && method.Name == "BuildPostHogJson"
            && method.DeclaringType.FullName == patchTelemetry.FullName);
    ILProcessor sendIl = sendBlocking.Body.GetILProcessor();
    sendIl.InsertBefore(buildJsonCall, Instruction.Create(OpCodes.Ldarg_1));
    sendIl.InsertBefore(buildJsonCall, Instruction.Create(
        OpCodes.Call, addCommonProperties));
    sendBlocking.Body.OptimizeMacros();

    WriteAssembly(assembly, outputPath);
}

static void PatchJudgementSprayConditionCacheOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairJudgementSprayConditionCache(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void PatchRainSceneDetachOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairRainSceneDetach(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchShadowBlobsSceneDetachOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairShadowBlobsSceneDetach(types);
    WriteAssembly(assembly, outputPath);
}

static void RestorePhysicsManagerClearOnly(
    string referencePath,
    string inputPath,
    string outputPath)
{
    const string PhysicsManagerTypeName = "Magicka.Physics.PhysicsManager";
    using AssemblyDefinition reference = ReadAssembly(referencePath);
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> referenceTypes = AllTypes(
            reference.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    Dictionary<string, TypeDefinition> targetTypes = AllTypes(
            assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    MethodDefinition sourceClear = RequireMethod(
        RequireType(referenceTypes, PhysicsManagerTypeName),
        "Clear",
        parameterCount: 0);
    MethodDefinition targetClear = RequireMethod(
        RequireType(targetTypes, PhysicsManagerTypeName),
        "Clear",
        parameterCount: 0);
    BodyReferencePool referencePool = new BodyReferencePool(
        assembly.MainModule);
    CloneMethodBody(
        sourceClear,
        targetClear,
        assembly.MainModule,
        targetTypes,
        referencePool);
    VerifyEntityDisposeOwnsPhysicsReferences(targetTypes);
    WriteAssembly(assembly, outputPath);
}

static void VerifyEntityDisposeOwnsPhysicsReferences(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    MethodDefinition dispose = RequireMethod(
        RequireType(types, EntityTypeName),
        "Dispose",
        parameterCount: 0);
    string[] requiredCalls =
    [
        "System.Void JigLibX.Physics.Body::set_CollisionSkin("
            + "JigLibX.Collision.CollisionSkin)",
        "System.Void JigLibX.Collision.CollisionSkin::set_Owner("
            + "JigLibX.Physics.Body)",
        "System.Void JigLibX.Collision.CollisionSkin::set_CollisionSystem("
            + "JigLibX.Collision.CollisionSystem)",
    ];
    foreach (string requiredCall in requiredCalls)
    {
        if (!dispose.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference called
                && called.FullName == requiredCall))
        {
            throw new InvalidOperationException(
                "Entity.Dispose no longer owns required physics cleanup: "
                + requiredCall);
        }
    }
    if (!dispose.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == "JigLibX.Physics.Body"
            && field.Name == "Tag"))
    {
        throw new InvalidOperationException(
            "Entity.Dispose no longer clears JigLibX.Physics.Body.Tag.");
    }
    if (!dispose.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName
                == "JigLibX.Collision.CollisionSkin"
            && called.Name == "set_Tag"))
    {
        throw new InvalidOperationException(
            "Entity.Dispose no longer clears "
            + "JigLibX.Collision.CollisionSkin.Tag.");
    }
}

static void PatchPhysicsManagerClearReferencesOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairPhysicsManagerClearReferences(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchMeteorShowerRemoveReferencesOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairMeteorShowerRemoveReferences(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchBlizzardRemoveReferencesOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairBlizzardRemoveReferences(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchControllerAvatarDetachOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairControllerAvatarDetach(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchGcEventPatchVersionOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairGcEventPatchVersion(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void RepairMonoTelemetryStartupOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairMonoTelemetryStartup(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void RepairMonoTelemetryStartup(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendAsync = RequireMethod(
        patchTelemetry,
        "SendAsync",
        parameterCount: 2);
    MethodDefinition addCommonProperties = RequireMethod(
        patchTelemetry,
        "AddCommonProperties",
        parameterCount: 1);
    MethodReference compatibleSetItem = addCommonProperties.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Callvirt)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.Name == "set_Item"
            && method.DeclaringType.FullName
                == sendAsync.Parameters[1].ParameterType.FullName
            && method.Parameters.Count == 2
            && method.Parameters[0].ParameterType is GenericParameter key
            && key.Position == 0
            && method.Parameters[1].ParameterType is GenericParameter value
            && value.Position == 1);
    Instruction patchVersionKey = sendAsync.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Ldstr
            && string.Equals(
                instruction.Operand as string,
                "patch_version",
                StringComparison.Ordinal));
    Instruction setterCall = patchVersionKey.Next?.Next
        ?? throw new InvalidOperationException(
            "PatchTelemetry.SendAsync patch-version setter was not found.");
    if (setterCall.OpCode != OpCodes.Callvirt
        || setterCall.Operand is not MethodReference setter
        || setter.Name != "set_Item")
    {
        throw new InvalidOperationException(
            "PatchTelemetry.SendAsync patch-version setter was not found.");
    }
    setterCall.Operand = compatibleSetItem;

    MethodDefinition sendStartup = RequireMethod(
        patchTelemetry,
        "SendStartup",
        parameterCount: 0);
    if (sendStartup.Body.ExceptionHandlers.Count != 0)
    {
        throw new InvalidOperationException(
            "PatchTelemetry.SendStartup already has exception handling.");
    }
    Instruction originalReturn = sendStartup.Body.Instructions[^1];
    if (originalReturn.OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "PatchTelemetry.SendStartup has an unexpected exit.");
    }
    ILProcessor processor = sendStartup.Body.GetILProcessor();
    Instruction handlerStart = Instruction.Create(OpCodes.Pop);
    Instruction finalReturn = Instruction.Create(OpCodes.Ret);
    originalReturn.OpCode = OpCodes.Leave;
    originalReturn.Operand = finalReturn;
    processor.Append(handlerStart);
    processor.Append(Instruction.Create(OpCodes.Leave, finalReturn));
    processor.Append(finalReturn);
    sendStartup.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = sendStartup.Body.Instructions[0],
        TryEnd = handlerStart,
        HandlerStart = handlerStart,
        HandlerEnd = finalReturn,
    });
}

static void PatchPlayerGameDeinitializeOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairPlayerGameDeinitialize(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchEntityCollisionCallbackCleanupOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairEntityCollisionCallbackCleanup(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void RepairEntityPhysicsLifecycleOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    ModuleDefinition module = assembly.MainModule;
    Dictionary<string, TypeDefinition> types = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);

    TypeDefinition entity = RequireType(types, EntityTypeName);
    TypeDefinition physicsEntity = RequireType(
        types,
        "Magicka.GameLogic.Entities.PhysicsEntity");
    MethodDefinition entityDispose = RequireMethod(entity, "Dispose", 0);
    MethodDefinition isDisposed = RequireMethod(entity, "get_IsDisposed", 0);
    FieldDefinition bodyField = entity.Fields.Single(field =>
        field.Name == "mBody"
        && !field.IsStatic
        && field.FieldType.FullName == "JigLibX.Physics.Body");
    FieldDefinition collisionField = entity.Fields.Single(field =>
        field.Name == "mCollision"
        && !field.IsStatic
        && field.FieldType.FullName
            == "JigLibX.Collision.CollisionSkin");

    TypeDefinition callbackCleanup = RequireType(
        types,
        "Magicka.CommunityPatch.CollisionCallbackCleanup");
    RepairCollisionCallbackCleanup(
        module,
        callbackCleanup,
        collisionField.FieldType);
    MethodDefinition clearCallbacks = RequireMethod(
        callbackCleanup,
        "Clear",
        1);

    MethodDefinition detachPhysics = AddEntityDetachPhysicsReferences(
        module,
        entity,
        entityDispose,
        bodyField,
        collisionField,
        clearCallbacks);
    ReplaceEntityDisposePhysicsCleanup(
        entityDispose,
        bodyField,
        collisionField,
        detachPhysics);

    MethodDefinition physicsInitialize = RequireMethod(
        physicsEntity,
        "Initialize",
        3);
    MethodDefinition createBody = RequireMethod(
        physicsEntity,
        "CreateBody",
        0);
    Instruction createBodyCall = physicsInitialize.Body.Instructions.Single(
        instruction => IsCallTo(instruction, createBody));
    Instruction createBodyOwner = createBodyCall.Previous
        ?? throw new InvalidOperationException(
            "PhysicsEntity.Initialize CreateBody owner is missing.");
    if (createBodyOwner.OpCode.Code != Code.Ldarg_0)
    {
        throw new InvalidOperationException(
            "PhysicsEntity.Initialize has an unexpected CreateBody call.");
    }
    ILProcessor initializeProcessor = physicsInitialize.Body.GetILProcessor();
    initializeProcessor.InsertBefore(
        createBodyOwner,
        Instruction.Create(OpCodes.Ldarg_0));
    initializeProcessor.InsertBefore(
        createBodyOwner,
        Instruction.Create(OpCodes.Call, detachPhysics));

    MethodDefinition physicsDeinitialize = RequireMethod(
        physicsEntity,
        "Deinitialize",
        0);
    MethodDefinition entityDeinitialize = RequireMethod(
        entity,
        "Deinitialize",
        0);
    Instruction baseDeinitialize = physicsDeinitialize.Body.Instructions.Single(
        instruction => IsCallTo(instruction, entityDeinitialize));
    ILProcessor deinitializeProcessor =
        physicsDeinitialize.Body.GetILProcessor();
    Instruction loadForDetach = Instruction.Create(OpCodes.Ldarg_0);
    deinitializeProcessor.InsertAfter(baseDeinitialize, loadForDetach);
    deinitializeProcessor.InsertAfter(
        loadForDetach,
        Instruction.Create(OpCodes.Call, detachPhysics));

    (string TypeName, bool CollisionBlock, bool BodyBlock)[] duplicateCleanup =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField",
            true,
            true),
        ("Magicka.GameLogic.Entities.Character", true, false),
        ("Magicka.GameLogic.Entities.ElementalEgg", true, true),
        ("Magicka.GameLogic.Entities.Fairy", false, true),
        ("Magicka.GameLogic.Entities.Gib", true, true),
        ("Magicka.GameLogic.Entities.MissileEntity", true, true),
    ];
    foreach ((string typeName, bool collisionBlock, bool bodyBlock)
             in duplicateCleanup)
    {
        MethodDefinition dispose = RequireMethod(
            RequireType(types, typeName),
            "Dispose",
            0);
        dispose.Body.SimplifyMacros();
        if (collisionBlock)
        {
            NopGuardedFieldCleanup(dispose, collisionField);
        }
        if (bodyBlock)
        {
            NopGuardedFieldCleanup(dispose, bodyField);
        }
        NopNullFieldStores(dispose, bodyField);
        NopNullFieldStores(dispose, collisionField);
    }

    string[] disposedFieldOwners =
    [
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Grease/GreaseField",
        "Magicka.GameLogic.Entities.Barrier",
        "Magicka.GameLogic.Entities.ElementalEgg",
        "Magicka.GameLogic.Entities.Fairy",
        "Magicka.GameLogic.Entities.Gib",
        "Magicka.GameLogic.Entities.MissileEntity",
        "Magicka.GameLogic.Entities.PhysicsEntity",
        "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
        "Magicka.GameLogic.Entities.Items.Item",
        "Magicka.GameLogic.Entities.NonPlayerCharacter",
        "Magicka.GameLogic.Entities.AnimatedPhysicsEntity",
    ];
    foreach (string typeName in disposedFieldOwners)
    {
        RemoveDerivedDisposedField(
            module,
            RequireType(types, typeName),
            isDisposed);
    }

    RepairItemDisposeCacheOwnership(
        module,
        RequireType(types, "Magicka.GameLogic.Entities.Items.Item"));

    TypeDefinition[] entityTypes = types.Values
        .Where(type => type.FullName != entity.FullName
            && IsSameModuleSubclassOf(type, entity.FullName, types))
        .ToArray();
    foreach (TypeDefinition type in entityTypes)
    {
        MethodDefinition? dispose = type.Methods.SingleOrDefault(method =>
            method.Name == "Dispose"
            && !method.IsStatic
            && method.Parameters.Count == 0);
        if (dispose is null)
        {
            continue;
        }
        MethodDefinition baseDispose = FindNearestBaseDispose(type, types);
        WrapDisposeBaseCallInFinally(dispose, baseDispose, isDisposed);
        dispose.Body.OptimizeMacros();
    }

    physicsInitialize.Body.OptimizeMacros();
    physicsDeinitialize.Body.OptimizeMacros();
    entityDispose.Body.OptimizeMacros();
    WriteAssembly(assembly, outputPath);
}

static void PatchEntityManagerQuadGridLifecycleOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairEntityManagerQuadGridLifecycle(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchAnimatedLevelPartDetachedBodyOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairAnimatedLevelPartDetachedBody(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchAiDetachedTargetsOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairAiDetachedTargets(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchNetworkPickupDetachedTargetOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairNetworkPickupDetachedTarget(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchCharacterTemplatePlayStateTransitionOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairCharacterTemplatePlayStateTransition(types);
    WriteAssembly(assembly, outputPath);
}

static void PatchInvalidAudioLocatorOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairInvalidAudioLocator(assembly.MainModule, types);
    WriteAssembly(assembly, outputPath);
}

static void RepairInvalidAudioLocator(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition gameScene = RequireType(types, "Magicka.Levels.GameScene");
    TypeDefinition audioLocator = RequireType(
        types,
        "Magicka.Levels.AudioLocator");
    MethodDefinition update = gameScene.Methods.Single(method =>
        method.Name == "Update"
        && !method.IsStatic
        && method.Parameters.Count == 2
        && method.Parameters[0].ParameterType.FullName
            == "PolygonHead.DataChannel");
    MethodDefinition locatorUpdate = RequireMethod(
        audioLocator,
        "Update",
        1);
    FieldDefinition sounds = gameScene.Fields.Single(field =>
        field.Name == "mSounds" && !field.IsStatic);

    update.Body.SimplifyMacros();
    if (update.Body.ExceptionHandlers.Any(handler =>
            handler.HandlerType == ExceptionHandlerType.Catch
            && handler.CatchType?.FullName
                == "System.IndexOutOfRangeException"))
    {
        throw new InvalidOperationException(
            "GameScene.Update already has an audio index handler.");
    }
    Instruction locatorCall = update.Body.Instructions.Single(
        instruction => IsCallTo(instruction, locatorUpdate));
    Instruction tryStart = update.Body.Instructions
        .TakeWhile(instruction => instruction != locatorCall)
        .Last(instruction =>
            instruction.OpCode.Code is Code.Ldarg_0 or Code.Ldarg
            && instruction.Next?.OpCode == OpCodes.Ldfld
            && instruction.Next.Operand is FieldReference field
            && field.FullName == sounds.FullName);
    Instruction normalRemove = update.Body.Instructions.Single(instruction =>
        instruction.OpCode.Code is Code.Call or Code.Callvirt
        && instruction.Operand is MethodReference called
        && called.Name == "RemoveAt"
        && called.DeclaringType.FullName == sounds.FieldType.FullName);
    Instruction indexStore = normalRemove.Next?.Next?.Next?.Next
        ?? throw new InvalidOperationException(
            "GameScene.Update sound-loop decrement is missing.");
    VariableDefinition index = StoredVariable(indexStore, update.Body)
        ?? throw new InvalidOperationException(
            "GameScene.Update sound-loop index is not a local.");
    Instruction incrementStart = indexStore.Next
        ?? throw new InvalidOperationException(
            "GameScene.Update sound-loop increment is missing.");
    Instruction noRemoveBranch = update.Body.Instructions
        .TakeWhile(instruction => instruction != normalRemove)
        .Last(instruction =>
            instruction.OpCode.Code is Code.Brfalse or Code.Brfalse_S
            && ReferenceEquals(instruction.Operand, incrementStart));

    ILProcessor processor = update.Body.GetILProcessor();
    Instruction normalLeave = Instruction.Create(
        OpCodes.Leave,
        incrementStart);
    Instruction handlerStart = Instruction.Create(OpCodes.Pop);
    Instruction[] handler =
    [
        handlerStart,
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, sounds),
        Instruction.Create(OpCodes.Ldloc, index),
        Instruction.Create(
            OpCodes.Callvirt,
            (MethodReference)normalRemove.Operand),
        Instruction.Create(OpCodes.Ldloc, index),
        Instruction.Create(OpCodes.Ldc_I4_1),
        Instruction.Create(OpCodes.Sub),
        Instruction.Create(OpCodes.Stloc, index),
        Instruction.Create(OpCodes.Leave, incrementStart),
    ];
    processor.InsertBefore(incrementStart, normalLeave);
    foreach (Instruction instruction in handler)
    {
        processor.InsertBefore(incrementStart, instruction);
    }
    noRemoveBranch.Operand = normalLeave;
    update.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        TryStart = tryStart,
        TryEnd = handlerStart,
        HandlerStart = handlerStart,
        HandlerEnd = incrementStart,
        CatchType = new TypeReference(
            "System",
            "IndexOutOfRangeException",
            module,
            module.TypeSystem.CoreLibrary),
    });
    OrderExceptionHandlersByNesting(update);
    update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 2);
    update.Body.OptimizeMacros();
}

static void RepairCharacterTemplatePlayStateTransition(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition template = RequireType(
        types,
        "Magicka.GameLogic.Entities.CharacterTemplate");
    TypeDefinition playState = RequireType(
        types,
        "Magicka.GameLogic.GameStates.PlayState");
    MethodDefinition getCachedTemplate = RequireMethod(
        template,
        "GetCachedTemplate",
        1);
    MethodDefinition getRecentPlayState = RequireMethod(
        playState,
        "get_RecentPlayState",
        0);

    getCachedTemplate.Body.SimplifyMacros();
    Instruction recentCall = getCachedTemplate.Body.Instructions.Single(
        instruction => IsCallTo(instruction, getRecentPlayState));
    Instruction contentCall = recentCall.Next
        ?? throw new InvalidOperationException(
            "CharacterTemplate fallback Content read is missing.");
    if (contentCall.OpCode.Code is not Code.Call and not Code.Callvirt
        || contentCall.Operand is not MethodReference calledContent
        || calledContent.Name != "get_Content")
    {
        throw new InvalidOperationException(
            "CharacterTemplate fallback has an unexpected Content read.");
    }
    Instruction contentValidStart = contentCall.Next
        ?? throw new InvalidOperationException(
            "CharacterTemplate fallback load is missing.");
    Instruction nullReturn = getCachedTemplate.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Ldnull
            && instruction.Next?.OpCode == OpCodes.Ret);
    ILProcessor processor = getCachedTemplate.Body.GetILProcessor();

    Instruction[] playStateGuard =
    [
        Instruction.Create(OpCodes.Dup),
        Instruction.Create(OpCodes.Brtrue, contentCall),
        Instruction.Create(OpCodes.Pop),
        Instruction.Create(OpCodes.Br, nullReturn),
    ];
    foreach (Instruction instruction in playStateGuard)
    {
        processor.InsertBefore(contentCall, instruction);
    }

    Instruction[] contentGuard =
    [
        Instruction.Create(OpCodes.Dup),
        Instruction.Create(OpCodes.Brtrue, contentValidStart),
        Instruction.Create(OpCodes.Pop),
        Instruction.Create(OpCodes.Br, nullReturn),
    ];
    foreach (Instruction instruction in contentGuard)
    {
        processor.InsertBefore(contentValidStart, instruction);
    }
    getCachedTemplate.Body.MaxStackSize = Math.Max(
        getCachedTemplate.Body.MaxStackSize,
        2);
    getCachedTemplate.Body.OptimizeMacros();
}

static void RepairNetworkPickupDetachedTarget(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition entity = RequireType(types, EntityTypeName);
    TypeDefinition avatar = RequireType(
        types,
        "Magicka.GameLogic.Entities.Avatar");
    MethodDefinition networkAction = RequireMethod(
        avatar,
        "NetworkAction",
        1);
    MethodDefinition internalPickUp = RequireMethod(
        avatar,
        "InternalPickUp",
        1);
    MethodDefinition pickUp = RequireMethod(avatar, "PickUp", 1);
    MethodDefinition getIsDisposed = RequireMethod(
        entity,
        "get_IsDisposed",
        0);
    MethodDefinition getBody = RequireMethod(entity, "get_Body", 0);

    networkAction.Body.SimplifyMacros();
    foreach (MethodDefinition target in new[] { internalPickUp, pickUp })
    {
        Instruction call = networkAction.Body.Instructions.Single(
            instruction => IsCallTo(instruction, target));
        Instruction pickableLoad = call.Previous
            ?? throw new InvalidOperationException(
                "Avatar.NetworkAction pickup argument is missing.");
        Instruction ownerLoad = pickableLoad.Previous
            ?? throw new InvalidOperationException(
                "Avatar.NetworkAction pickup owner is missing.");
        VariableDefinition pickable = LoadedVariable(
                pickableLoad,
                networkAction.Body)
            ?? throw new InvalidOperationException(
                "Avatar.NetworkAction pickup target is not a local.");
        Instruction returnTarget = call.Next
            ?? throw new InvalidOperationException(
                "Avatar.NetworkAction pickup return target is missing.");
        if (ownerLoad.OpCode.Code is not Code.Ldarg_0 and not Code.Ldarg)
        {
            throw new InvalidOperationException(
                "Avatar.NetworkAction pickup call has an unexpected owner.");
        }

        Instruction[] guard =
        [
            Instruction.Create(OpCodes.Ldloc, pickable),
            Instruction.Create(OpCodes.Brfalse, returnTarget),
            Instruction.Create(OpCodes.Ldloc, pickable),
            Instruction.Create(OpCodes.Callvirt, getIsDisposed),
            Instruction.Create(OpCodes.Brtrue, returnTarget),
            Instruction.Create(OpCodes.Ldloc, pickable),
            Instruction.Create(OpCodes.Callvirt, getBody),
            Instruction.Create(OpCodes.Brfalse, returnTarget),
        ];
        ILProcessor processor = networkAction.Body.GetILProcessor();
        foreach (Instruction instruction in guard)
        {
            processor.InsertBefore(ownerLoad, instruction);
        }
    }
    networkAction.Body.MaxStackSize = Math.Max(
        networkAction.Body.MaxStackSize,
        1);
    networkAction.Body.OptimizeMacros();
}

static void RepairAiDetachedTargets(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition idamageable = RequireType(
        types,
        "Magicka.GameLogic.Entities.IDamageable");
    TypeDefinition agent = RequireType(types, "Magicka.AI.Agent");
    TypeDefinition attackState = RequireType(
        types,
        "Magicka.AI.AgentStates.AIStateAttack");
    TypeDefinition moveState = RequireType(
        types,
        "Magicka.AI.AgentStates.AIStateMove");
    MethodDefinition getCurrentTarget = RequireMethod(
        agent,
        "get_CurrentTarget",
        0);
    MethodDefinition getBody = RequireMethod(idamageable, "get_Body", 0);

    MethodDefinition attack = RequireMethod(attackState, "OnExecute", 2);
    attack.Body.SimplifyMacros();
    Instruction attackNullCall = attack.Body.Instructions.First(instruction =>
        IsCallTo(instruction, getCurrentTarget)
        && instruction.Next?.OpCode.Code is Code.Brtrue or Code.Brtrue_S);
    Instruction attackNullBranch = attackNullCall.Next!;
    Instruction attackValidStart = (Instruction)attackNullBranch.Operand;
    Instruction attackInvalidStart = attackNullBranch.Next
        ?? throw new InvalidOperationException(
            "AIStateAttack target-null branch has no invalid path.");
    VariableDefinition attackAgent = LoadedVariable(
            attackNullCall.Previous
                ?? throw new InvalidOperationException(
                    "AIStateAttack target owner is missing."),
            attack.Body)
        ?? throw new InvalidOperationException(
            "AIStateAttack target owner is not a local.");
    Instruction attackBodyGuard = Instruction.Create(
        OpCodes.Ldloc,
        attackAgent);
    Instruction[] attackGuard =
    [
        attackBodyGuard,
        Instruction.Create(OpCodes.Callvirt, getCurrentTarget),
        Instruction.Create(OpCodes.Callvirt, getBody),
        Instruction.Create(OpCodes.Brfalse, attackInvalidStart),
    ];
    ILProcessor attackProcessor = attack.Body.GetILProcessor();
    foreach (Instruction instruction in attackGuard)
    {
        attackProcessor.InsertBefore(attackValidStart, instruction);
    }
    attackNullBranch.Operand = attackBodyGuard;
    attack.Body.MaxStackSize = Math.Max(attack.Body.MaxStackSize, 1);
    attack.Body.OptimizeMacros();

    MethodDefinition moveEnter = RequireMethod(moveState, "OnEnter", 1);
    moveEnter.Body.SimplifyMacros();
    Instruction enterPosition = moveEnter.Body.Instructions.Single(
        instruction =>
            instruction.OpCode.Code is Code.Call or Code.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == idamageable.FullName
            && called.Name == "get_Position");
    Instruction enterTargetCall = moveEnter.Body.Instructions
        .TakeWhile(instruction => instruction != enterPosition)
        .Last(instruction =>
            IsCallTo(instruction, getCurrentTarget)
            && instruction.Next?.OpCode.Code
                is Code.Brfalse or Code.Brfalse_S);
    Instruction enterTargetBranch = enterTargetCall.Next
        ?? throw new InvalidOperationException(
            "AIStateMove.OnEnter target branch is missing.");
    if (enterTargetBranch.OpCode.Code is not Code.Brfalse and not Code.Brfalse_S
        || enterTargetBranch.Operand is not Instruction enterContinue)
    {
        throw new InvalidOperationException(
            "AIStateMove.OnEnter has an unexpected target-null guard.");
    }
    VariableDefinition enterAgent = LoadedVariable(
            enterTargetCall.Previous
                ?? throw new InvalidOperationException(
                    "AIStateMove.OnEnter target owner is missing."),
            moveEnter.Body)
        ?? throw new InvalidOperationException(
            "AIStateMove.OnEnter target owner is not a local.");
    ILProcessor enterProcessor = moveEnter.Body.GetILProcessor();
    Instruction enterOwner = Instruction.Create(OpCodes.Ldloc, enterAgent);
    Instruction enterCurrentTarget = Instruction.Create(
        OpCodes.Callvirt,
        getCurrentTarget);
    Instruction enterBody = Instruction.Create(OpCodes.Callvirt, getBody);
    Instruction enterBodyBranch = Instruction.Create(
        OpCodes.Brfalse,
        enterContinue);
    enterProcessor.InsertAfter(enterTargetBranch, enterOwner);
    enterProcessor.InsertAfter(enterOwner, enterCurrentTarget);
    enterProcessor.InsertAfter(enterCurrentTarget, enterBody);
    enterProcessor.InsertAfter(enterBody, enterBodyBranch);
    moveEnter.Body.MaxStackSize = Math.Max(moveEnter.Body.MaxStackSize, 1);
    moveEnter.Body.OptimizeMacros();

    MethodDefinition moveExecute = RequireMethod(moveState, "OnExecute", 2);
    moveExecute.Body.SimplifyMacros();
    Instruction moveDeadCall = moveExecute.Body.Instructions.Single(
        instruction =>
            instruction.OpCode.Code is Code.Call or Code.Callvirt
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == idamageable.FullName
            && called.Name == "get_Dead");
    Instruction moveDeadBranch = moveDeadCall.Next
        ?? throw new InvalidOperationException(
            "AIStateMove.OnExecute Dead branch is missing.");
    if (moveDeadBranch.OpCode.Code is not Code.Brtrue and not Code.Brtrue_S
        || moveDeadBranch.Operand is not Instruction moveInvalidStart)
    {
        throw new InvalidOperationException(
            "AIStateMove.OnExecute has an unexpected Dead guard.");
    }
    VariableDefinition moveAgent = LoadedVariable(
            moveDeadCall.Previous?.Previous
                ?? throw new InvalidOperationException(
                    "AIStateMove.OnExecute target owner is missing."),
            moveExecute.Body)
        ?? throw new InvalidOperationException(
            "AIStateMove.OnExecute target owner is not a local.");
    ILProcessor moveProcessor = moveExecute.Body.GetILProcessor();
    Instruction moveOwner = Instruction.Create(OpCodes.Ldloc, moveAgent);
    Instruction moveCurrentTarget = Instruction.Create(
        OpCodes.Callvirt,
        getCurrentTarget);
    Instruction moveBody = Instruction.Create(OpCodes.Callvirt, getBody);
    Instruction moveBodyBranch = Instruction.Create(
        OpCodes.Brfalse,
        moveInvalidStart);
    moveProcessor.InsertAfter(moveDeadBranch, moveOwner);
    moveProcessor.InsertAfter(moveOwner, moveCurrentTarget);
    moveProcessor.InsertAfter(moveCurrentTarget, moveBody);
    moveProcessor.InsertAfter(moveBody, moveBodyBranch);
    moveExecute.Body.MaxStackSize = Math.Max(moveExecute.Body.MaxStackSize, 1);
    moveExecute.Body.OptimizeMacros();

    MethodDefinition chooseTarget = RequireMethod(agent, "ChooseTarget", 2);
    chooseTarget.Body.SimplifyMacros();
    MethodDefinition getDead = RequireMethod(idamageable, "get_Dead", 0);
    Instruction candidateDeadCall = chooseTarget.Body.Instructions.First(
        instruction => IsCallTo(instruction, getDead));
    Instruction candidateRejectBranch = chooseTarget.Body.Instructions
        .SkipWhile(instruction => instruction != candidateDeadCall)
        .First(instruction =>
            instruction.OpCode.Code is Code.Brtrue or Code.Brtrue_S);
    Instruction candidateReject = (Instruction)candidateRejectBranch.Operand;
    VariableDefinition candidate = LoadedVariable(
            candidateDeadCall.Previous
                ?? throw new InvalidOperationException(
                    "Agent.ChooseTarget candidate owner is missing."),
            chooseTarget.Body)
        ?? throw new InvalidOperationException(
            "Agent.ChooseTarget candidate owner is not a local.");
    ILProcessor chooseProcessor = chooseTarget.Body.GetILProcessor();
    Instruction candidateLoad = Instruction.Create(OpCodes.Ldloc, candidate);
    Instruction candidateBody = Instruction.Create(OpCodes.Callvirt, getBody);
    Instruction candidateBodyBranch = Instruction.Create(
        OpCodes.Brfalse,
        candidateReject);
    chooseProcessor.InsertAfter(candidateRejectBranch, candidateLoad);
    chooseProcessor.InsertAfter(candidateLoad, candidateBody);
    chooseProcessor.InsertAfter(candidateBody, candidateBodyBranch);
    chooseTarget.Body.MaxStackSize = Math.Max(chooseTarget.Body.MaxStackSize, 1);
    chooseTarget.Body.OptimizeMacros();
}

static void RepairAnimatedLevelPartDetachedBody(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition animatedLevelPart = RequireType(
        types,
        "Magicka.Levels.AnimatedLevelPart");
    TypeDefinition entity = RequireType(types, EntityTypeName);
    MethodDefinition update = animatedLevelPart.Methods.Single(method =>
        method.Name == "Update"
        && !method.IsStatic
        && method.Parameters.Count == 4);
    MethodDefinition getFromHandle = RequireMethod(
        entity,
        "GetFromHandle",
        1);
    MethodDefinition getBody = RequireMethod(entity, "get_Body", 0);
    FieldDefinition collidingEntities = animatedLevelPart.Fields.Single(field =>
        field.Name == "mCollidingEntities"
        && !field.IsStatic);

    update.Body.SimplifyMacros();
    Instruction handleLookup = update.Body.Instructions.Single(instruction =>
        IsCallTo(instruction, getFromHandle));
    Instruction entityStore = handleLookup.Next
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update entity store is missing.");
    VariableDefinition entityLocal = StoredVariable(entityStore, update.Body)
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update has an unexpected entity store.");
    Instruction originalEntryStart = entityStore.Next
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update entry processing is missing.");

    Instruction bodyCall = update.Body.Instructions.Single(instruction =>
        IsCallTo(instruction, getBody));
    Instruction oldBodyOwner = bodyCall.Previous
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update Body owner is missing.");
    Instruction oldBodyStore = bodyCall.Next
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update Body store is missing.");
    VariableDefinition bodyLocal = StoredVariable(oldBodyStore, update.Body)
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update has an unexpected Body store.");
    if (LoadedVariable(oldBodyOwner, update.Body) != entityLocal)
    {
        throw new InvalidOperationException(
            "AnimatedLevelPart.Update Body is not read from the resolved entity.");
    }

    Instruction removeAtCall = update.Body.Instructions.First(instruction =>
        instruction.OpCode.Code is Code.Call or Code.Callvirt
        && instruction.Operand is MethodReference called
        && called.Name == "RemoveAt"
        && called.Parameters.Count == 1
        && called.DeclaringType.FullName == collidingEntities.FieldType.FullName);
    MethodReference removeAt = (MethodReference)removeAtCall.Operand;
    VariableDefinition indexLocal = LoadedVariable(
            removeAtCall.Previous
                ?? throw new InvalidOperationException(
                    "AnimatedLevelPart.Update RemoveAt index is missing."),
            update.Body)
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update has an unexpected RemoveAt index.");
    Instruction setBodyTransform = update.Body.Instructions.Single(instruction =>
        instruction.OpCode.Code is Code.Call or Code.Callvirt
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "JigLibX.Physics.Body"
        && called.Name == "set_Transform"
        && called.Parameters.Count == 1);
    Instruction loopIncrement = setBodyTransform.Next
        ?? throw new InvalidOperationException(
            "AnimatedLevelPart.Update loop increment is missing.");
    if (LoadedVariable(loopIncrement, update.Body) != indexLocal)
    {
        throw new InvalidOperationException(
            "AnimatedLevelPart.Update has an unexpected loop increment.");
    }

    Instruction invalidStart = Instruction.Create(OpCodes.Ldarg_0);
    Instruction[] guard =
    [
        Instruction.Create(OpCodes.Ldloc, entityLocal),
        Instruction.Create(OpCodes.Brfalse, invalidStart),
        Instruction.Create(OpCodes.Ldloc, entityLocal),
        Instruction.Create(OpCodes.Callvirt, getBody),
        Instruction.Create(OpCodes.Stloc, bodyLocal),
        Instruction.Create(OpCodes.Ldloc, bodyLocal),
        Instruction.Create(OpCodes.Brtrue, originalEntryStart),
        invalidStart,
        Instruction.Create(OpCodes.Ldfld, collidingEntities),
        Instruction.Create(OpCodes.Ldloc, indexLocal),
        Instruction.Create(OpCodes.Callvirt, removeAt),
        Instruction.Create(OpCodes.Ldloc, indexLocal),
        Instruction.Create(OpCodes.Ldc_I4_1),
        Instruction.Create(OpCodes.Sub),
        Instruction.Create(OpCodes.Stloc, indexLocal),
        Instruction.Create(OpCodes.Br, loopIncrement),
    ];
    ILProcessor processor = update.Body.GetILProcessor();
    foreach (Instruction instruction in guard)
    {
        processor.InsertBefore(originalEntryStart, instruction);
    }

    oldBodyOwner.OpCode = OpCodes.Nop;
    oldBodyOwner.Operand = null;
    bodyCall.OpCode = OpCodes.Nop;
    bodyCall.Operand = null;
    oldBodyStore.OpCode = OpCodes.Nop;
    oldBodyStore.Operand = null;
    update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 2);
    update.Body.OptimizeMacros();
}

static void RepairEntityManagerQuadGridLifecycle(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition entityManager = RequireType(
        types,
        "Magicka.GameLogic.Entities.EntityManager");
    TypeDefinition entity = RequireType(types, EntityTypeName);
    MethodDefinition getEntities = entityManager.Methods.Single(method =>
        method.Name == "GetEntities"
        && !method.IsStatic
        && method.Parameters.Count == 4);
    MethodDefinition clearAndStore = RequireMethod(
        entityManager,
        "ClearAndStore",
        1);
    MethodDefinition updateQuadGrid = RequireMethod(
        entityManager,
        "UpdateQuadGrid",
        0);
    MethodDefinition getDead = RequireMethod(entity, "get_Dead", 0);
    MethodDefinition getBody = RequireMethod(entity, "get_Body", 0);

    if (getEntities.Body.Instructions.Any(instruction =>
            IsCallTo(instruction, getBody)))
    {
        throw new InvalidOperationException(
            "EntityManager.GetEntities already contains a Body guard.");
    }
    Instruction deadCall = getEntities.Body.Instructions.Single(
        instruction => IsCallTo(instruction, getDead));
    Instruction entityLoad = deadCall.Previous
        ?? throw new InvalidOperationException(
            "EntityManager.GetEntities Dead owner load is missing.");
    Instruction deadBranch = deadCall.Next
        ?? throw new InvalidOperationException(
            "EntityManager.GetEntities Dead branch is missing.");
    if (!IsLocalLoad(entityLoad)
        || deadBranch.OpCode.Code is not Code.Brtrue and not Code.Brtrue_S
        || deadBranch.Operand is not Instruction continueTarget)
    {
        throw new InvalidOperationException(
            "EntityManager.GetEntities has an unexpected Dead guard.");
    }

    Instruction[] crossingShortBranches = getEntities.Body.Instructions
        .Where(instruction =>
            instruction.OpCode.Code == Code.Blt_S
            && instruction.Offset > entityLoad.Offset
            && instruction.Operand is Instruction target
            && target.Offset <= entityLoad.Offset)
        .ToArray();
    if (crossingShortBranches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one short entity-loop back edge in"
            + " EntityManager.GetEntities, found "
            + crossingShortBranches.Length + ".");
    }
    crossingShortBranches[0].OpCode = OpCodes.Blt;

    ILProcessor getEntitiesProcessor = getEntities.Body.GetILProcessor();
    getEntitiesProcessor.InsertBefore(
        entityLoad,
        CloneLocalLoad(entityLoad));
    getEntitiesProcessor.InsertBefore(
        entityLoad,
        Instruction.Create(OpCodes.Brfalse, continueTarget));
    Instruction bodyLoad = CloneLocalLoad(entityLoad);
    Instruction bodyCall = Instruction.Create(OpCodes.Callvirt, getBody);
    Instruction bodyBranch = Instruction.Create(
        OpCodes.Brfalse,
        continueTarget);
    getEntitiesProcessor.InsertAfter(deadBranch, bodyLoad);
    getEntitiesProcessor.InsertAfter(bodyLoad, bodyCall);
    getEntitiesProcessor.InsertAfter(bodyCall, bodyBranch);

    if (clearAndStore.Body.Instructions.Any(instruction =>
            IsCallTo(instruction, updateQuadGrid)))
    {
        throw new InvalidOperationException(
            "EntityManager.ClearAndStore already refreshes the QuadGrid.");
    }
    FieldDefinition entitiesField = entityManager.Fields.Single(field =>
        field.Name == "mEntities"
        && !field.IsStatic);
    Instruction clearEntities = clearAndStore.Body.Instructions.Single(
        instruction =>
            instruction.OpCode.Code is Code.Call or Code.Callvirt
            && instruction.Operand is MethodReference called
            && called.Name == "Clear"
            && instruction.Previous?.OpCode.Code == Code.Ldfld
            && instruction.Previous.Operand is FieldReference field
            && field.FullName == entitiesField.FullName);
    ILProcessor clearProcessor = clearAndStore.Body.GetILProcessor();
    Instruction loadManager = Instruction.Create(OpCodes.Ldarg_0);
    clearProcessor.InsertAfter(clearEntities, loadManager);
    clearProcessor.InsertAfter(
        loadManager,
        Instruction.Create(OpCodes.Call, updateQuadGrid));
}

static bool IsLocalLoad(Instruction instruction)
{
    return instruction.OpCode.Code is Code.Ldloc
        or Code.Ldloc_0
        or Code.Ldloc_1
        or Code.Ldloc_2
        or Code.Ldloc_3
        or Code.Ldloc_S;
}

static Instruction CloneLocalLoad(Instruction instruction)
{
    return instruction.OpCode.Code switch
    {
        Code.Ldloc_0 => Instruction.Create(OpCodes.Ldloc_0),
        Code.Ldloc_1 => Instruction.Create(OpCodes.Ldloc_1),
        Code.Ldloc_2 => Instruction.Create(OpCodes.Ldloc_2),
        Code.Ldloc_3 => Instruction.Create(OpCodes.Ldloc_3),
        Code.Ldloc_S => Instruction.Create(
            OpCodes.Ldloc_S,
            (VariableDefinition)instruction.Operand),
        Code.Ldloc => Instruction.Create(
            OpCodes.Ldloc,
            (VariableDefinition)instruction.Operand),
        _ => throw new InvalidOperationException(
            "Instruction is not a local load."),
    };
}

static void RepairCollisionCallbackCleanup(
    ModuleDefinition module,
    TypeDefinition helper,
    TypeReference collisionSkin)
{
    FieldDefinition callbackField = helper.Fields.Single(field =>
        field.Name == "sCallbackField"
        && field.IsStatic
        && field.FieldType.FullName == "System.Reflection.FieldInfo");
    if (helper.Fields.Any(field =>
            field.Name == "sPostCollisionCallbackField"))
    {
        throw new InvalidOperationException(
            "Collision callback cleanup is already repaired.");
    }
    FieldDefinition postCallbackField = new FieldDefinition(
        "sPostCollisionCallbackField",
        FieldAttributes.Private
        | FieldAttributes.Static
        | FieldAttributes.InitOnly,
        callbackField.FieldType);
    helper.Fields.Add(postCallbackField);

    MethodDefinition initialize = helper.Methods.Single(method =>
        method.IsConstructor && method.IsStatic);
    MethodReference getTypeFromHandle = initialize.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(method => method.DeclaringType.FullName == "System.Type"
            && method.Name == "GetTypeFromHandle");
    MethodReference getField = initialize.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(method => method.DeclaringType.FullName == "System.Type"
            && method.Name == "GetField"
            && method.Parameters.Count == 2);
    MethodDefinition oldClear = RequireMethod(helper, "Clear", 1);
    MethodReference setValue = oldClear.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(method =>
            method.DeclaringType.FullName == "System.Reflection.FieldInfo"
            && method.Name == "SetValue"
            && method.Parameters.Count == 2);
    TypeReference exceptionType = initialize.Body.ExceptionHandlers
        .Concat(oldClear.Body.ExceptionHandlers)
        .Select(handler => handler.CatchType)
        .First(type => type?.FullName == "System.Exception")!;

    MethodDefinition resolveField = new MethodDefinition(
        "ResolveField",
        MethodAttributes.Private
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        callbackField.FieldType);
    resolveField.Parameters.Add(new ParameterDefinition(
        "name",
        ParameterAttributes.None,
        module.TypeSystem.String));
    VariableDefinition resolvedField = new VariableDefinition(
        callbackField.FieldType);
    resolveField.Body.Variables.Add(resolvedField);
    resolveField.Body.InitLocals = true;
    helper.Methods.Add(resolveField);
    ILProcessor resolveProcessor = resolveField.Body.GetILProcessor();
    Instruction resolveTry = Instruction.Create(OpCodes.Nop);
    Instruction resolveHandler = Instruction.Create(OpCodes.Pop);
    Instruction loadResolvedField = Instruction.Create(
        OpCodes.Ldloc,
        resolvedField);
    resolveProcessor.Append(resolveTry);
    resolveProcessor.Append(Instruction.Create(OpCodes.Ldtoken, collisionSkin));
    resolveProcessor.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));
    resolveProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    resolveProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)36));
    resolveProcessor.Append(Instruction.Create(OpCodes.Callvirt, getField));
    resolveProcessor.Append(Instruction.Create(OpCodes.Stloc, resolvedField));
    resolveProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        loadResolvedField));
    resolveProcessor.Append(resolveHandler);
    resolveProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    resolveProcessor.Append(Instruction.Create(OpCodes.Stloc, resolvedField));
    resolveProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        loadResolvedField));
    resolveProcessor.Append(loadResolvedField);
    resolveProcessor.Append(Instruction.Create(OpCodes.Ret));
    resolveField.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = exceptionType,
        TryStart = resolveTry,
        TryEnd = resolveHandler,
        HandlerStart = resolveHandler,
        HandlerEnd = loadResolvedField,
    });
    resolveField.Body.MaxStackSize = 3;

    MethodDefinition clearField = new MethodDefinition(
        "ClearField",
        MethodAttributes.Private
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    clearField.Parameters.Add(new ParameterDefinition(
        "field",
        ParameterAttributes.None,
        callbackField.FieldType));
    clearField.Parameters.Add(new ParameterDefinition(
        "skin",
        ParameterAttributes.None,
        collisionSkin));
    helper.Methods.Add(clearField);
    ILProcessor clearFieldProcessor = clearField.Body.GetILProcessor();
    Instruction clearFieldTry = Instruction.Create(OpCodes.Nop);
    Instruction clearFieldHandler = Instruction.Create(OpCodes.Pop);
    Instruction clearFieldReturn = Instruction.Create(OpCodes.Ret);
    clearFieldProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearFieldProcessor.Append(Instruction.Create(
        OpCodes.Brfalse,
        clearFieldReturn));
    clearFieldProcessor.Append(clearFieldTry);
    clearFieldProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearFieldProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    clearFieldProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    clearFieldProcessor.Append(Instruction.Create(OpCodes.Callvirt, setValue));
    clearFieldProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        clearFieldReturn));
    clearFieldProcessor.Append(clearFieldHandler);
    clearFieldProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        clearFieldReturn));
    clearFieldProcessor.Append(clearFieldReturn);
    clearField.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = exceptionType,
        TryStart = clearFieldTry,
        TryEnd = clearFieldHandler,
        HandlerStart = clearFieldHandler,
        HandlerEnd = clearFieldReturn,
    });
    clearField.Body.MaxStackSize = 3;

    initialize.Body.Instructions.Clear();
    initialize.Body.ExceptionHandlers.Clear();
    initialize.Body.Variables.Clear();
    ILProcessor initializeProcessor = initialize.Body.GetILProcessor();
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "callbackFn"));
    initializeProcessor.Append(Instruction.Create(OpCodes.Call, resolveField));
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Stsfld,
        callbackField));
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "postCollisionCallbackFn"));
    initializeProcessor.Append(Instruction.Create(OpCodes.Call, resolveField));
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Stsfld,
        postCallbackField));
    initializeProcessor.Append(Instruction.Create(OpCodes.Ret));
    initialize.Body.MaxStackSize = 1;

    oldClear.Body.Instructions.Clear();
    oldClear.Body.ExceptionHandlers.Clear();
    oldClear.Body.Variables.Clear();
    ILProcessor clearProcessor = oldClear.Body.GetILProcessor();
    Instruction clearReturn = Instruction.Create(OpCodes.Ret);
    clearProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearProcessor.Append(Instruction.Create(
        OpCodes.Brfalse,
        clearReturn));
    clearProcessor.Append(Instruction.Create(OpCodes.Ldsfld, callbackField));
    clearProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearProcessor.Append(Instruction.Create(OpCodes.Call, clearField));
    clearProcessor.Append(Instruction.Create(
        OpCodes.Ldsfld,
        postCallbackField));
    clearProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearProcessor.Append(Instruction.Create(OpCodes.Call, clearField));
    clearProcessor.Append(clearReturn);
    oldClear.Body.MaxStackSize = 2;
}

static MethodDefinition AddEntityDetachPhysicsReferences(
    ModuleDefinition module,
    TypeDefinition entity,
    MethodDefinition dispose,
    FieldDefinition bodyField,
    FieldDefinition collisionField,
    MethodDefinition clearCallbacks)
{
    if (entity.Methods.Any(method =>
            method.Name == "DetachPhysicsReferences"))
    {
        throw new InvalidOperationException(
            "Entity.DetachPhysicsReferences already exists.");
    }

    MethodReference disableBody = RequireBodyCall(
        dispose,
        "JigLibX.Physics.Body",
        "DisableBody");
    MethodReference getBodySkin = RequireBodyCall(
        dispose,
        "JigLibX.Physics.Body",
        "get_CollisionSkin");
    MethodReference setBodySkin = RequireBodyCall(
        dispose,
        "JigLibX.Physics.Body",
        "set_CollisionSkin");
    MethodReference referenceEquals = RequireBodyCall(
        dispose,
        "System.Object",
        "ReferenceEquals");
    MethodReference getCollisions = RequireBodyCall(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "get_Collisions");
    MethodReference getNonCollidables = RequireBodyCall(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "get_NonCollidables");
    MethodReference setSkinTag = RequireBodyCall(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "set_Tag");
    MethodReference setSkinOwner = RequireBodyCall(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "set_Owner");
    MethodReference setCollisionSystem = RequireBodyCall(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "set_CollisionSystem");
    MethodReference clearCollisions = dispose.Body.Instructions
        .SkipWhile(instruction => !IsCallTo(instruction, getCollisions))
        .Skip(1)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.Name == "Clear");
    MethodReference clearNonCollidables = dispose.Body.Instructions
        .SkipWhile(instruction => !IsCallTo(instruction, getNonCollidables))
        .Skip(1)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.Name == "Clear");
    FieldReference bodyTag = AllTypes(module)
        .SelectMany(type => type.Methods)
        .SelectMany(method => method.HasBody
            ? method.Body.Instructions
            : [])
        .Select(instruction => instruction.Operand)
        .OfType<FieldReference>()
        .Where(field => field.DeclaringType.FullName == "JigLibX.Physics.Body"
            && field.Name == "Tag")
        .DistinctBy(field => field.FullName)
        .Single();

    MethodDefinition detach = new MethodDefinition(
        "DetachPhysicsReferences",
        MethodAttributes.Family
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    VariableDefinition body = new VariableDefinition(bodyField.FieldType);
    VariableDefinition skin = new VariableDefinition(collisionField.FieldType);
    detach.Body.Variables.Add(body);
    detach.Body.Variables.Add(skin);
    detach.Body.InitLocals = true;
    entity.Methods.Add(detach);
    ILProcessor processor = detach.Body.GetILProcessor();
    Instruction bodyTagCleanup = Instruction.Create(OpCodes.Ldloc, body);
    Instruction skinCleanup = Instruction.Create(OpCodes.Ldloc, skin);
    Instruction clearEntityFields = Instruction.Create(OpCodes.Ldarg_0);

    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, bodyField));
    processor.Append(Instruction.Create(OpCodes.Stloc, body));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, collisionField));
    processor.Append(Instruction.Create(OpCodes.Stloc, skin));
    processor.Append(Instruction.Create(OpCodes.Ldloc, body));
    processor.Append(Instruction.Create(OpCodes.Brfalse, skinCleanup));
    processor.Append(Instruction.Create(OpCodes.Ldloc, body));
    processor.Append(Instruction.Create(OpCodes.Callvirt, disableBody));
    processor.Append(Instruction.Create(OpCodes.Ldloc, body));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getBodySkin));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    processor.Append(Instruction.Create(OpCodes.Brfalse, bodyTagCleanup));
    processor.Append(Instruction.Create(OpCodes.Ldloc, body));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Callvirt, setBodySkin));
    processor.Append(bodyTagCleanup);
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stfld, bodyTag));
    processor.Append(skinCleanup);
    processor.Append(Instruction.Create(OpCodes.Brfalse, clearEntityFields));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Call, clearCallbacks));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getCollisions));
    processor.Append(Instruction.Create(OpCodes.Callvirt, clearCollisions));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getNonCollidables));
    processor.Append(Instruction.Create(OpCodes.Callvirt, clearNonCollidables));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Callvirt, setSkinTag));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Callvirt, setSkinOwner));
    processor.Append(Instruction.Create(OpCodes.Ldloc, skin));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(
        OpCodes.Callvirt,
        setCollisionSystem));
    processor.Append(clearEntityFields);
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stfld, bodyField));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stfld, collisionField));
    processor.Append(Instruction.Create(OpCodes.Ret));
    detach.Body.MaxStackSize = 2;
    return detach;
}

static void ReplaceEntityDisposePhysicsCleanup(
    MethodDefinition dispose,
    FieldDefinition bodyField,
    FieldDefinition collisionField,
    MethodDefinition detach)
{
    dispose.Body.SimplifyMacros();
    Instruction bodyStore = dispose.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Stloc
        && instruction.Operand is VariableDefinition variable
        && variable.VariableType.FullName == bodyField.FieldType.FullName
        && instruction.Previous?.Operand is FieldReference field
        && field.FullName == bodyField.FullName);
    Instruction start = bodyStore.Previous?.Previous
        ?? throw new InvalidOperationException(
            "Entity.Dispose physics cleanup start is missing.");
    Instruction detachSystem = dispose.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == collisionField.FieldType.FullName
        && called.Name == "set_CollisionSystem");
    Instruction? end = detachSystem.Next;
    if (start.OpCode.Code is not Code.Ldarg_0 and not Code.Ldarg
        || end is null)
    {
        throw new InvalidOperationException(
            "Entity.Dispose physics cleanup has an unexpected shape.");
    }
    start.OpCode = OpCodes.Ldarg_0;
    start.Operand = null;
    Instruction call = start.Next!;
    call.OpCode = OpCodes.Call;
    call.Operand = detach;
    for (Instruction? instruction = call.Next;
         instruction is not null && instruction != end;
         instruction = instruction.Next)
    {
        instruction.OpCode = OpCodes.Nop;
        instruction.Operand = null;
    }
    VariableDefinition[] unusedPhysicsLocals = dispose.Body.Variables
        .Where(variable => variable.VariableType.FullName
            == bodyField.FieldType.FullName
            || variable.VariableType.FullName
                == collisionField.FieldType.FullName)
        .ToArray();
    foreach (VariableDefinition variable in unusedPhysicsLocals)
    {
        dispose.Body.Variables.Remove(variable);
    }
}

static void NopGuardedFieldCleanup(
    MethodDefinition method,
    FieldDefinition field)
{
    Instruction[] headers = method.Body.Instructions.Where(instruction =>
            instruction.OpCode == OpCodes.Ldfld
            && instruction.Operand is FieldReference referenced
            && referenced.FullName == field.FullName
            && instruction.Previous?.OpCode.Code
                is Code.Ldarg_0 or Code.Ldarg
            && instruction.Next?.OpCode.FlowControl == FlowControl.Cond_Branch
            && instruction.Next.Operand is Instruction)
        .ToArray();
    if (headers.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one guarded " + field.Name + " cleanup in "
            + method.FullName + ", found " + headers.Length + ".");
    }
    Instruction start = headers[0].Previous!;
    Instruction end = (Instruction)headers[0].Next!.Operand;
    for (Instruction? instruction = start;
         instruction is not null && instruction != end;
         instruction = instruction.Next)
    {
        instruction.OpCode = OpCodes.Nop;
        instruction.Operand = null;
    }
}

static void NopNullFieldStores(
    MethodDefinition method,
    FieldDefinition field)
{
    Instruction[] stores = method.Body.Instructions.Where(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference referenced
            && referenced.FullName == field.FullName
            && instruction.Previous?.OpCode == OpCodes.Ldnull
            && instruction.Previous.Previous?.OpCode.Code
                is Code.Ldarg_0 or Code.Ldarg)
        .ToArray();
    foreach (Instruction store in stores)
    {
        Instruction value = store.Previous!;
        Instruction owner = value.Previous!;
        owner.OpCode = OpCodes.Nop;
        owner.Operand = null;
        value.OpCode = OpCodes.Nop;
        value.Operand = null;
        store.OpCode = OpCodes.Nop;
        store.Operand = null;
    }
}

static void RemoveDerivedDisposedField(
    ModuleDefinition module,
    TypeDefinition type,
    MethodDefinition isDisposed)
{
    FieldDefinition field = type.Fields.Single(candidate =>
        candidate.Name == "mDisposed"
        && !candidate.IsStatic
        && candidate.FieldType.FullName == "System.Boolean");
    foreach (MethodDefinition method in AllTypes(module)
                 .SelectMany(candidate => candidate.Methods)
                 .Where(candidate => candidate.HasBody))
    {
        foreach (Instruction instruction in method.Body.Instructions.Where(
                     instruction => instruction.Operand is FieldReference used
                         && used.FullName == field.FullName).ToArray())
        {
            if (instruction.OpCode == OpCodes.Ldfld)
            {
                instruction.OpCode = OpCodes.Call;
                instruction.Operand = isDisposed;
                continue;
            }
            if (instruction.OpCode != OpCodes.Stfld
                || instruction.Previous is null
                || instruction.Previous.Previous is null
                || instruction.Previous.Previous.OpCode.Code
                    is not Code.Ldarg_0 and not Code.Ldarg)
            {
                throw new InvalidOperationException(
                    "Unexpected use of " + field.FullName + " in "
                    + method.FullName + ".");
            }
            Instruction value = instruction.Previous;
            Instruction owner = value.Previous!;
            owner.OpCode = OpCodes.Nop;
            owner.Operand = null;
            value.OpCode = OpCodes.Nop;
            value.Operand = null;
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }
    }
    type.Fields.Remove(field);
}

static void RepairItemDisposeCacheOwnership(
    ModuleDefinition module,
    TypeDefinition item)
{
    MethodDefinition dispose = RequireMethod(item, "Dispose", 0);
    FieldDefinition cache = item.Fields.Single(field =>
        field.Name == "CachedWeapons" && field.IsStatic);
    FieldDefinition typeField = RequireType(
            AllTypes(module).ToDictionary(
                type => type.FullName,
                StringComparer.Ordinal),
            "Magicka.GameLogic.Entities.Items.Pickable")
        .Fields.Single(field => field.Name == "mType" && !field.IsStatic);
    Instruction contains = dispose.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == cache.FieldType.FullName
        && called.Name == "ContainsKey");
    Instruction skipRemove = contains.Next?.Operand as Instruction
        ?? throw new InvalidOperationException(
            "Item.Dispose cache guard target is missing.");
    Instruction removeOwner = contains.Next?.Next
        ?? throw new InvalidOperationException(
            "Item.Dispose cache removal is missing.");
    MethodReference getItem = item.Methods
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Where(method => method.DeclaringType.FullName == cache.FieldType.FullName
            && method.Name == "get_Item")
        .DistinctBy(method => method.FullName)
        .Single();
    MethodReference referenceEquals = FindMethodReference(
        module,
        "System.Object",
        "ReferenceEquals",
        2,
        "System.Boolean");
    ILProcessor processor = dispose.Body.GetILProcessor();
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Ldsfld, cache));
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Ldfld, typeField));
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Callvirt, getItem));
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Call, referenceEquals));
    processor.InsertBefore(
        removeOwner,
        Instruction.Create(OpCodes.Brfalse, skipRemove));
}

static MethodDefinition FindNearestBaseDispose(
    TypeDefinition type,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeReference? current = type.BaseType;
    while (current is not null)
    {
        if (!types.TryGetValue(current.FullName, out TypeDefinition? parent))
        {
            break;
        }
        MethodDefinition? dispose = parent.Methods.SingleOrDefault(method =>
            method.Name == "Dispose"
            && !method.IsStatic
            && method.Parameters.Count == 0);
        if (dispose is not null)
        {
            return dispose;
        }
        current = parent.BaseType;
    }
    throw new InvalidOperationException(
        "No base Dispose method found for " + type.FullName + ".");
}

static bool IsCallTo(Instruction instruction, MethodReference method)
{
    return instruction.OpCode.Code is Code.Call or Code.Callvirt
        && instruction.Operand is MethodReference called
        && called.FullName == method.FullName;
}

static void WrapDisposeBaseCallInFinally(
    MethodDefinition method,
    MethodDefinition baseDispose,
    MethodDefinition isDisposed)
{
    method.Body.SimplifyMacros();
    Instruction baseCall = method.Body.Instructions.Single(instruction =>
        IsCallTo(instruction, baseDispose));
    Instruction baseOwner = baseCall.Previous
        ?? throw new InvalidOperationException(
            "Base Dispose owner is missing in " + method.FullName + ".");
    Instruction oldReturn = baseCall.Next
        ?? throw new InvalidOperationException(
            "Base Dispose return is missing in " + method.FullName + ".");
    if (baseOwner.OpCode.Code is not Code.Ldarg_0 and not Code.Ldarg
        || oldReturn.OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "Unexpected base Dispose call shape in " + method.FullName + ".");
    }

    Instruction? guardReturn = method.Body.Instructions
        .TakeWhile(instruction => instruction != baseOwner)
        .FirstOrDefault(instruction => instruction.OpCode == OpCodes.Ret);
    if (guardReturn is null)
    {
        Instruction firstCleanup = method.Body.Instructions.First();
        Instruction? lifecycleCall = method.Body.Instructions.FirstOrDefault(
            instruction => instruction.Operand is MethodReference called
                && called.DeclaringType.FullName
                    == "Magicka.GcDiagnostics.RetentionRegistry"
                && called.Name == "MarkMustCollect");
        if (lifecycleCall is not null)
        {
            firstCleanup = lifecycleCall.Next
                ?? throw new InvalidOperationException(
                    "Dispose lifecycle hook has no successor in "
                    + method.FullName + ".");
        }
        ILProcessor guardProcessor = method.Body.GetILProcessor();
        Instruction cleanupTarget = firstCleanup;
        Instruction loadThis = Instruction.Create(OpCodes.Ldarg_0);
        Instruction loadDisposed = Instruction.Create(OpCodes.Call, isDisposed);
        Instruction continueCleanup = Instruction.Create(
            OpCodes.Brfalse,
            cleanupTarget);
        guardReturn = Instruction.Create(OpCodes.Ret);
        guardProcessor.InsertBefore(cleanupTarget, loadThis);
        guardProcessor.InsertBefore(cleanupTarget, loadDisposed);
        guardProcessor.InsertBefore(cleanupTarget, continueCleanup);
        guardProcessor.InsertBefore(cleanupTarget, guardReturn);
    }
    Instruction tryStart = guardReturn.Next
        ?? throw new InvalidOperationException(
            "Dispose cleanup start is missing in " + method.FullName + ".");
    if (method.Body.Instructions.Any(instruction =>
            instruction != oldReturn
            && instruction.Operand switch
            {
                Instruction target => target == oldReturn,
                Instruction[] targets => targets.Contains(oldReturn),
                _ => false,
            }))
    {
        throw new InvalidOperationException(
            "Dispose final return is an unexpected branch target in "
            + method.FullName + ".");
    }

    ILProcessor processor = method.Body.GetILProcessor();
    Instruction handlerStart = Instruction.Create(OpCodes.Ldarg_0);
    Instruction finalReturn = Instruction.Create(OpCodes.Ret);
    baseOwner.OpCode = OpCodes.Leave;
    baseOwner.Operand = finalReturn;
    baseCall.OpCode = OpCodes.Nop;
    baseCall.Operand = null;
    oldReturn.OpCode = OpCodes.Nop;
    oldReturn.Operand = null;
    processor.Append(handlerStart);
    processor.Append(Instruction.Create(OpCodes.Call, baseDispose));
    processor.Append(Instruction.Create(OpCodes.Endfinally));
    processor.Append(finalReturn);
    method.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Finally)
    {
        TryStart = tryStart,
        TryEnd = handlerStart,
        HandlerStart = handlerStart,
        HandlerEnd = finalReturn,
    });
    OrderExceptionHandlersByNesting(method);
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 1);
}

static void RepairEntityCollisionCallbackCleanup(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string helperTypeName =
        "Magicka.CommunityPatch.CollisionCallbackCleanup";
    if (types.ContainsKey(helperTypeName))
    {
        throw new InvalidOperationException(
            "CollisionCallbackCleanup already exists.");
    }

    TypeDefinition entity = RequireType(types, EntityTypeName);
    FieldDefinition collision = entity.Fields.Single(field =>
        field.Name == "mCollision"
        && !field.IsStatic
        && field.FieldType.FullName
            == "JigLibX.Collision.CollisionSkin");
    MethodDefinition dispose = RequireMethod(
        entity,
        "Dispose",
        parameterCount: 0);
    VariableDefinition collisionLocal = dispose.Body.Variables.Single(variable =>
        variable.VariableType.FullName == collision.FieldType.FullName);
    Instruction clearLists = dispose.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Callvirt
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == collision.FieldType.FullName
        && called.Name == "get_Collisions");
    Instruction clearListsOwner = clearLists.Previous
        ?? throw new InvalidOperationException(
            "Entity.Dispose collision-list load is missing.");

    MethodReference getTypeFromHandle = FindMethodReference(
        module,
        "System.Type",
        "GetTypeFromHandle",
        parameterCount: 1,
        returnType: "System.Type");
    TypeReference typeType = getTypeFromHandle.ReturnType;
    TypeReference fieldInfo = new TypeReference(
        "System.Reflection",
        "FieldInfo",
        module,
        module.TypeSystem.CoreLibrary);
    TypeReference bindingFlags = new TypeReference(
        "System.Reflection",
        "BindingFlags",
        module,
        module.TypeSystem.CoreLibrary,
        true);
    MethodReference getField = CreateInstanceMethodReference(
        "GetField",
        typeType,
        fieldInfo,
        module.TypeSystem.String,
        bindingFlags);
    MethodReference setValue = CreateInstanceMethodReference(
        "SetValue",
        fieldInfo,
        module.TypeSystem.Void,
        module.TypeSystem.Object,
        module.TypeSystem.Object);
    TypeReference exceptionType = AllTypes(module)
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.ExceptionHandlers)
        .Select(handler => handler.CatchType)
        .First(type => type != null && type.FullName == "System.Exception")!;

    TypeDefinition helper = new TypeDefinition(
        "Magicka.CommunityPatch",
        "CollisionCallbackCleanup",
        TypeAttributes.NotPublic
        | TypeAttributes.Abstract
        | TypeAttributes.Sealed
        | TypeAttributes.Class,
        module.TypeSystem.Object);
    module.Types.Add(helper);
    FieldDefinition callbackField = new FieldDefinition(
        "sCallbackField",
        FieldAttributes.Private
        | FieldAttributes.Static
        | FieldAttributes.InitOnly,
        fieldInfo);
    helper.Fields.Add(callbackField);

    MethodDefinition initialize = new MethodDefinition(
        ".cctor",
        MethodAttributes.Private
        | MethodAttributes.Static
        | MethodAttributes.HideBySig
        | MethodAttributes.SpecialName
        | MethodAttributes.RTSpecialName,
        module.TypeSystem.Void);
    helper.Methods.Add(initialize);
    ILProcessor initializeProcessor = initialize.Body.GetILProcessor();
    Instruction initializeTry = Instruction.Create(OpCodes.Nop);
    Instruction initializeHandler = Instruction.Create(OpCodes.Pop);
    Instruction initializeReturn = Instruction.Create(OpCodes.Ret);
    initializeProcessor.Append(initializeTry);
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Ldtoken,
        collision.FieldType));
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Call,
        getTypeFromHandle));
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "callbackFn"));
    initializeProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)36));
    initializeProcessor.Append(Instruction.Create(OpCodes.Callvirt, getField));
    initializeProcessor.Append(Instruction.Create(OpCodes.Stsfld, callbackField));
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        initializeReturn));
    initializeProcessor.Append(initializeHandler);
    initializeProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        initializeReturn));
    initializeProcessor.Append(initializeReturn);
    initialize.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = exceptionType,
        TryStart = initializeTry,
        TryEnd = initializeHandler,
        HandlerStart = initializeHandler,
        HandlerEnd = initializeReturn,
    });
    initialize.Body.MaxStackSize = 3;

    MethodDefinition clear = new MethodDefinition(
        "Clear",
        MethodAttributes.Assembly
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    clear.Parameters.Add(new ParameterDefinition(
        "skin",
        ParameterAttributes.None,
        collision.FieldType));
    helper.Methods.Add(clear);
    ILProcessor clearProcessor = clear.Body.GetILProcessor();
    Instruction clearTry = Instruction.Create(OpCodes.Nop);
    Instruction clearReturn = Instruction.Create(OpCodes.Ret);
    Instruction noCallbackField = Instruction.Create(
        OpCodes.Leave,
        clearReturn);
    Instruction clearHandler = Instruction.Create(OpCodes.Pop);
    clearProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearProcessor.Append(Instruction.Create(OpCodes.Brfalse, clearReturn));
    clearProcessor.Append(clearTry);
    clearProcessor.Append(Instruction.Create(OpCodes.Ldsfld, callbackField));
    clearProcessor.Append(Instruction.Create(OpCodes.Brfalse, noCallbackField));
    clearProcessor.Append(Instruction.Create(OpCodes.Ldsfld, callbackField));
    clearProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    clearProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    clearProcessor.Append(Instruction.Create(OpCodes.Callvirt, setValue));
    clearProcessor.Append(noCallbackField);
    clearProcessor.Append(clearHandler);
    clearProcessor.Append(Instruction.Create(OpCodes.Leave, clearReturn));
    clearProcessor.Append(clearReturn);
    clear.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = exceptionType,
        TryStart = clearTry,
        TryEnd = clearHandler,
        HandlerStart = clearHandler,
        HandlerEnd = clearReturn,
    });
    clear.Body.MaxStackSize = 3;

    ILProcessor disposeProcessor = dispose.Body.GetILProcessor();
    disposeProcessor.InsertBefore(
        clearListsOwner,
        Instruction.Create(OpCodes.Ldloc, collisionLocal));
    disposeProcessor.InsertBefore(
        clearListsOwner,
        Instruction.Create(OpCodes.Call, clear));
    dispose.Body.MaxStackSize = Math.Max(dispose.Body.MaxStackSize, 3);
}

static void RepairPlayerGameDeinitialize(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition player = RequireType(types, "Magicka.GameLogic.Player");
    TypeDefinition textBox = RequireType(types, "Magicka.Graphics.TextBox");
    TypeDefinition notifierButton = RequireType(
        types,
        "Magicka.Graphics.NotifierButton");
    FieldDefinition obtainedTextBox = player.Fields.Single(field =>
        field.Name == "mObtainedTextBox"
        && !field.IsStatic
        && field.FieldType.FullName == textBox.FullName);
    MethodDefinition releaseLevelReferences = RequireMethod(
        textBox,
        "ReleaseLevelReferences",
        parameterCount: 0);
    FieldDefinition notifier = player.Fields.Single(field =>
        field.Name == "mNotifierButton"
        && !field.IsStatic
        && field.FieldType.FullName == notifierButton.FullName);
    if (notifierButton.Methods.Any(method =>
            method.Name == "ReleaseLevelReferences"))
    {
        throw new InvalidOperationException(
            "NotifierButton.ReleaseLevelReferences already exists.");
    }

    FieldDefinition notifierOwner = notifierButton.Fields.Single(field =>
        field.Name == "mOwner"
        && !field.IsStatic
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.Entity");
    FieldDefinition notifierDialog = notifierButton.Fields.Single(field =>
        field.Name == "mDialogAttach"
        && !field.IsStatic
        && field.FieldType.FullName == textBox.FullName);
    FieldDefinition notifierAlpha = notifierButton.Fields.Single(field =>
        field.Name == "mAlpha"
        && !field.IsStatic
        && field.FieldType.FullName == "System.Single");
    FieldDefinition notifierTargetAlpha = notifierButton.Fields.Single(field =>
        field.Name == "mTargetAlpha"
        && !field.IsStatic
        && field.FieldType.FullName == "System.Single");
    MethodDefinition releaseNotifierReferences = new MethodDefinition(
        "ReleaseLevelReferences",
        MethodAttributes.Assembly
        | MethodAttributes.HideBySig,
        notifierButton.Module.TypeSystem.Void);
    notifierButton.Methods.Add(releaseNotifierReferences);
    ILProcessor releaseProcessor =
        releaseNotifierReferences.Body.GetILProcessor();
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldc_R4, 0f));
    releaseProcessor.Append(Instruction.Create(OpCodes.Stfld, notifierAlpha));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldc_R4, 0f));
    releaseProcessor.Append(Instruction.Create(
        OpCodes.Stfld,
        notifierTargetAlpha));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    releaseProcessor.Append(Instruction.Create(OpCodes.Stfld, notifierOwner));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    releaseProcessor.Append(Instruction.Create(OpCodes.Stfld, notifierDialog));
    releaseProcessor.Append(Instruction.Create(OpCodes.Ret));
    releaseNotifierReferences.Body.MaxStackSize = 2;

    MethodDefinition deinitializeGame = RequireMethod(
        player,
        "DeinitializeGame",
        parameterCount: 0);
    bool emptyOriginal = deinitializeGame.HasBody
        && deinitializeGame.Body.Instructions.Count == 1
        && deinitializeGame.Body.Instructions[0].OpCode == OpCodes.Ret;
    bool textBoxOnlyPatch = deinitializeGame.HasBody
        && deinitializeGame.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.FullName == releaseLevelReferences.FullName)
        && !deinitializeGame.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == notifierButton.FullName);
    if (!emptyOriginal && !textBoxOnlyPatch)
    {
        throw new InvalidOperationException(
            "Expected the original or text-box-only Player.DeinitializeGame"
            + " method; the cleanup may already exist or the game method"
            + " changed.");
    }

    MethodBody body = deinitializeGame.Body;
    body.Instructions.Clear();
    body.ExceptionHandlers.Clear();
    body.Variables.Clear();
    ILProcessor processor = body.GetILProcessor();
    Instruction notifierCheck = Instruction.Create(OpCodes.Ldarg_0);
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, obtainedTextBox));
    processor.Append(Instruction.Create(OpCodes.Brfalse, notifierCheck));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, obtainedTextBox));
    processor.Append(Instruction.Create(
        OpCodes.Callvirt,
        releaseLevelReferences));
    processor.Append(notifierCheck);
    processor.Append(Instruction.Create(OpCodes.Ldfld, notifier));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnInstruction));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, notifier));
    processor.Append(Instruction.Create(
        OpCodes.Callvirt,
        releaseNotifierReferences));
    processor.Append(returnInstruction);
    body.MaxStackSize = 1;
}

static void RepairGcEventPatchVersion(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendAsync = RequireMethod(
        patchTelemetry,
        "SendAsync",
        parameterCount: 2);
    MethodDefinition getPatchVersion = RequireMethod(
        patchTelemetry,
        "GetPatchVersion",
        parameterCount: 0);
    TypeReference propertiesType = sendAsync.Parameters[1].ParameterType;
    Instruction queueStateCreation = sendAsync.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is MethodReference constructor
            && constructor.Name == ".ctor"
            && constructor.DeclaringType.FullName
                == "Magicka.CommunityPatch.PatchTelemetry/TelemetrySendState");
    Instruction[] existingPatchVersionKeys = sendAsync.Body.Instructions
        .Where(instruction =>
            instruction.OpCode == OpCodes.Ldstr
            && string.Equals(
                instruction.Operand as string,
                "patch_version",
                StringComparison.Ordinal))
        .ToArray();
    if (existingPatchVersionKeys.Length == 1)
    {
        Instruction patchStart = existingPatchVersionKeys[0].Previous
            ?? throw new InvalidOperationException(
                "PatchTelemetry.SendAsync patch-version load is missing.");
        if (patchStart.OpCode != OpCodes.Ldarg_1)
        {
            throw new InvalidOperationException(
                "Unexpected PatchTelemetry.SendAsync patch-version block.");
        }

        Instruction[] bypasses = sendAsync.Body.Instructions
            .Where(instruction =>
                ReferenceEquals(instruction.Operand, queueStateCreation))
            .ToArray();
        if (bypasses.Length != 1
            || (bypasses[0].OpCode != OpCodes.Brfalse
                && bypasses[0].OpCode != OpCodes.Brfalse_S))
        {
            throw new InvalidOperationException(
                "Expected one disabled-check branch bypassing the"
                + " SendAsync patch-version block, found "
                + bypasses.Length + ".");
        }

        bypasses[0].Operand = patchStart;
        return;
    }
    if (existingPatchVersionKeys.Length != 0)
    {
        throw new InvalidOperationException(
            "Expected at most one SendAsync patch_version key, found "
            + existingPatchVersionKeys.Length + ".");
    }

    MethodDefinition addCommonProperties = RequireMethod(
        patchTelemetry,
        "AddCommonProperties",
        parameterCount: 1);
    MethodReference setItem = addCommonProperties.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Callvirt)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .First(method => method.Name == "set_Item"
            && method.DeclaringType.FullName == propertiesType.FullName
            && method.Parameters.Count == 2
            && method.Parameters[0].ParameterType is GenericParameter key
            && key.Position == 0
            && method.Parameters[1].ParameterType is GenericParameter value
            && value.Position == 1);
    ILProcessor processor = sendAsync.Body.GetILProcessor();
    Instruction patchStartInstruction = Instruction.Create(OpCodes.Ldarg_1);
    processor.InsertBefore(queueStateCreation, patchStartInstruction);
    processor.InsertBefore(
        queueStateCreation,
        Instruction.Create(OpCodes.Ldstr, "patch_version"));
    processor.InsertBefore(
        queueStateCreation,
        Instruction.Create(OpCodes.Call, getPatchVersion));
    processor.InsertBefore(
        queueStateCreation,
        Instruction.Create(OpCodes.Callvirt, setItem));
    foreach (Instruction branch in sendAsync.Body.Instructions.Where(
                 instruction => ReferenceEquals(
                     instruction.Operand,
                     queueStateCreation)))
    {
        branch.Operand = patchStartInstruction;
    }
    sendAsync.Body.MaxStackSize = Math.Max(sendAsync.Body.MaxStackSize, 3);
}

static void RepairRainSceneDetach(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition rain = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Rain");
    TypeDefinition thunderstorm = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderstorm");
    TypeDefinition gameScene = RequireType(types, "Magicka.Levels.GameScene");
    FieldDefinition sceneField = rain.Fields.Single(field =>
        field.Name == "mScene"
        && !field.IsStatic
        && field.FieldType.FullName == gameScene.FullName);
    FieldDefinition casterField = rain.Fields.Single(field =>
        field.Name == "mCaster"
        && !field.IsStatic
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.ISpellCaster");
    FieldDefinition ownerField = thunderstorm.Fields.Single(field =>
        field.Name == "mOwner"
        && !field.IsStatic
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.ISpellCaster");
    FieldDefinition rainField = thunderstorm.Fields.Single(field =>
        field.Name == "mRain"
        && !field.IsStatic
        && field.FieldType.FullName == rain.FullName);
    MethodDefinition rainOnRemove = RequireMethod(
        rain,
        "OnRemove",
        parameterCount: 0);
    MethodDefinition thunderstormOnRemove = RequireMethod(
        thunderstorm,
        "OnRemove",
        parameterCount: 0);
    MethodDefinition setLightTargetIntensity = RequireMethod(
        gameScene,
        "set_LightTargetIntensity",
        parameterCount: 1);

    Instruction[] rainInstructions = rainOnRemove.Body.Instructions.ToArray();
    int setterIndex = Array.FindIndex(
        rainInstructions,
        instruction => IsMethodCall(instruction, setLightTargetIntensity));
    if (setterIndex < 3
        || rainInstructions[setterIndex - 3].OpCode != OpCodes.Ldarg_0
        || !IsFieldLoad(rainInstructions[setterIndex - 2], sceneField)
        || rainInstructions[setterIndex - 1].OpCode != OpCodes.Ldc_R4
        || rainInstructions[setterIndex - 1].Operand is not float intensity
        || intensity != 1f
        || setterIndex + 1 >= rainInstructions.Length
        || rainInstructions[setterIndex + 1].OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "Unexpected Rain.OnRemove scene-light restoration; the detach"
            + " repair may already exist or the game method changed.");
    }

    ILProcessor rainProcessor = rainOnRemove.Body.GetILProcessor();
    for (int index = setterIndex; index >= setterIndex - 3; index--)
    {
        rainProcessor.Remove(rainInstructions[index]);
    }

    Instruction rainReturn = rainInstructions[setterIndex + 1];
    VariableDefinition oldScene = new VariableDefinition(gameScene);
    rainOnRemove.Body.Variables.Add(oldScene);
    rainOnRemove.Body.InitLocals = true;
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldarg_0));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldfld, sceneField));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Stloc, oldScene));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldarg_0));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldnull));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Stfld, sceneField));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldarg_0));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldnull));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Stfld, casterField));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldloc, oldScene));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Brfalse, rainReturn));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldloc, oldScene));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Ldc_R4, 1f));
    rainProcessor.InsertBefore(
        rainReturn,
        Instruction.Create(OpCodes.Callvirt, setLightTargetIntensity));
    rainOnRemove.Body.MaxStackSize = Math.Max(
        rainOnRemove.Body.MaxStackSize,
        2);

    if (thunderstormOnRemove.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && (field.FullName == ownerField.FullName
                || field.FullName == rainField.FullName)))
    {
        throw new InvalidOperationException(
            "Thunderstorm.OnRemove already resets an owner or Rain field.");
    }

    Instruction thunderstormReturn = RequireSingleReturn(thunderstormOnRemove);
    ILProcessor thunderstormProcessor =
        thunderstormOnRemove.Body.GetILProcessor();
    thunderstormProcessor.InsertBefore(
        thunderstormReturn,
        Instruction.Create(OpCodes.Ldarg_0));
    thunderstormProcessor.InsertBefore(
        thunderstormReturn,
        Instruction.Create(OpCodes.Ldnull));
    thunderstormProcessor.InsertBefore(
        thunderstormReturn,
        Instruction.Create(OpCodes.Stfld, ownerField));
    thunderstormOnRemove.Body.MaxStackSize = Math.Max(
        thunderstormOnRemove.Body.MaxStackSize,
        2);
}

static void RepairShadowBlobsSceneDetach(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition shadowBlobs = RequireType(
        types,
        "Magicka.GameLogic.UI.ShadowBlobs");
    FieldDefinition sceneField = shadowBlobs.Fields.Single(field =>
        field.Name == "mScene"
        && !field.IsStatic
        && field.FieldType.FullName == "PolygonHead.Scene");
    TypeReference scene = sceneField.FieldType;
    if (shadowBlobs.Methods.Any(method =>
            method.Name == "CommunityPatchDetachScene"))
    {
        throw new InvalidOperationException(
            "ShadowBlobs scene detach already exists.");
    }

    MethodDefinition detachScene = new MethodDefinition(
        "CommunityPatchDetachScene",
        MethodAttributes.Assembly | MethodAttributes.HideBySig,
        shadowBlobs.Module.TypeSystem.Void);
    detachScene.Parameters.Add(new ParameterDefinition(
        "expected",
        ParameterAttributes.None,
        scene));
    shadowBlobs.Methods.Add(detachScene);
    ILProcessor detachProcessor = detachScene.Body.GetILProcessor();
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);
    detachProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldfld, sceneField));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    detachProcessor.Append(Instruction.Create(OpCodes.Bne_Un, returnInstruction));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    detachProcessor.Append(Instruction.Create(OpCodes.Stfld, sceneField));
    detachProcessor.Append(returnInstruction);
    detachScene.Body.MaxStackSize = 2;

    MethodDefinition getInstance = RequireMethod(
        shadowBlobs,
        "get_Instance",
        parameterCount: 0);
    TypeDefinition playState = RequireType(types, PlayStateTypeName);
    MethodDefinition dispose = RequireMethod(
        playState,
        "Dispose",
        parameterCount: 0);
    FieldDefinition playStateScene = RequireType(types, GameStateTypeName)
        .Fields.Single(field =>
            field.Name == "mScene"
            && !field.IsStatic
            && field.FieldType.FullName == scene.FullName);
    Instruction clearPlayStateScene = dispose.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference stored
            && stored.FullName == playStateScene.FullName);
    Instruction loadPlayState = clearPlayStateScene.Previous?.Previous
        ?? throw new InvalidOperationException(
            "PlayState.Dispose scene reset is incomplete.");
    if (loadPlayState.OpCode != OpCodes.Ldarg_0
        || clearPlayStateScene.Previous?.OpCode != OpCodes.Ldnull)
    {
        throw new InvalidOperationException(
            "Unexpected PlayState.Dispose scene reset.");
    }

    ILProcessor disposeProcessor = dispose.Body.GetILProcessor();
    disposeProcessor.InsertBefore(
        loadPlayState,
        Instruction.Create(OpCodes.Call, getInstance));
    disposeProcessor.InsertBefore(
        loadPlayState,
        Instruction.Create(OpCodes.Ldarg_0));
    disposeProcessor.InsertBefore(
        loadPlayState,
        Instruction.Create(OpCodes.Ldfld, playStateScene));
    disposeProcessor.InsertBefore(
        loadPlayState,
        Instruction.Create(OpCodes.Callvirt, detachScene));
    dispose.Body.MaxStackSize = Math.Max(dispose.Body.MaxStackSize, 2);
}

static void RepairPhysicsManagerClearReferences(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition physicsManager = RequireType(
        types,
        "Magicka.Physics.PhysicsManager");
    MethodDefinition clear = RequireMethod(
        physicsManager,
        "Clear",
        parameterCount: 0);
    MethodDefinition dispose = RequireMethod(
        physicsManager,
        "Dispose",
        parameterCount: 0);
    if (clear.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference field
            && field.DeclaringType.FullName == "JigLibX.Physics.Body"
            && field.Name == "Tag"))
    {
        throw new InvalidOperationException(
            "PhysicsManager.Clear body cleanup already exists.");
    }

    VariableDefinition simulator = clear.Body.Variables.Single(variable =>
        variable.VariableType.FullName
            == "JigLibX.Physics.PhysicsSystem");
    VariableDefinition bodyList = clear.Body.Variables.Single(variable =>
        variable.VariableType.FullName
            == "System.Collections.Generic.List`1<JigLibX.Physics.Body>");
    VariableDefinition skinList = clear.Body.Variables.Single(variable =>
        variable.VariableType.FullName
            == "System.Collections.Generic.List`1<"
               + "JigLibX.Collision.CollisionSkin>");
    VariableDefinition body = clear.Body.Variables.Single(variable =>
        variable.VariableType.FullName == "JigLibX.Physics.Body");
    VariableDefinition skin = clear.Body.Variables.Single(variable =>
        variable.VariableType.FullName
            == "JigLibX.Collision.CollisionSkin");

    MethodReference bodySetCollisionSkin = dispose.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(method =>
            method.DeclaringType.FullName == "JigLibX.Physics.Body"
            && method.Name == "set_CollisionSkin");
    FieldReference bodyTag = dispose.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Stfld)
        .Select(instruction => instruction.Operand)
        .OfType<FieldReference>()
        .Single(field =>
            field.DeclaringType.FullName == "JigLibX.Physics.Body"
            && field.Name == "Tag");
    MethodReference setOwner = RequireCallReference(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "set_Owner");
    MethodReference setCollisionSystem = RequireCallReference(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "set_CollisionSystem");
    MethodReference setTag = RequireCallReference(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "set_Tag");
    MethodReference getCollisions = RequireCallReference(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "get_Collisions");
    MethodReference getNonCollidables = RequireCallReference(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "get_NonCollidables");
    MethodReference removeAllPrimitives = RequireCallReference(
        dispose,
        "JigLibX.Collision.CollisionSkin",
        "RemoveAllPrimitives");
    Instruction disposeGetCollisions = dispose.Body.Instructions.Single(
        instruction => instruction.Operand is MethodReference called
            && called.FullName == getCollisions.FullName);
    MethodReference clearCollisionList = disposeGetCollisions.Next?.Operand
        as MethodReference
        ?? throw new InvalidOperationException(
            "PhysicsManager.Dispose collision-list clear is missing.");
    Instruction disposeGetNonCollidables = dispose.Body.Instructions.Single(
        instruction => instruction.Operand is MethodReference called
            && called.FullName == getNonCollidables.FullName);
    MethodReference clearNonCollidables = disposeGetNonCollidables.Next?.Operand
        as MethodReference
        ?? throw new InvalidOperationException(
            "PhysicsManager.Dispose non-collidable clear is missing.");

    Instruction skinSnapshotStore = clear.Body.Instructions.Single(
        instruction => StoredVariable(instruction, clear.Body) == skinList
            && instruction.Previous?.OpCode == OpCodes.Newobj);
    Instruction skinListConstructor = skinSnapshotStore.Previous!;
    Instruction getCollisionSkins = skinListConstructor.Previous
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear skin snapshot is incomplete.");
    Instruction getCollisionSystem = getCollisionSkins.Previous
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear collision system load is missing.");
    Instruction loadSimulator = getCollisionSystem.Previous
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear simulator load is missing.");
    if (LoadedVariable(loadSimulator, clear.Body) != simulator
        || getCollisionSystem.Operand is not MethodReference
        || getCollisionSkins.Operand is not MethodReference
        || skinListConstructor.Operand is not MethodReference)
    {
        throw new InvalidOperationException(
            "Unexpected PhysicsManager.Clear skin snapshot.");
    }

    Instruction bodySnapshotStore = clear.Body.Instructions.Single(
        instruction => StoredVariable(instruction, clear.Body) == bodyList
            && instruction.Previous?.OpCode == OpCodes.Newobj);
    ILProcessor processor = clear.Body.GetILProcessor();
    Instruction snapshotCursor = bodySnapshotStore;
    Instruction[] earlySkinSnapshot =
    [
        Instruction.Create(OpCodes.Ldloc, simulator),
        Instruction.Create(
            OpCodes.Callvirt,
            (MethodReference)getCollisionSystem.Operand),
        Instruction.Create(
            OpCodes.Callvirt,
            (MethodReference)getCollisionSkins.Operand),
        Instruction.Create(
            OpCodes.Newobj,
            (MethodReference)skinListConstructor.Operand),
        Instruction.Create(OpCodes.Stloc, skinList),
    ];
    foreach (Instruction instruction in earlySkinSnapshot)
    {
        processor.InsertAfter(snapshotCursor, instruction);
        snapshotCursor = instruction;
    }
    foreach (Instruction instruction in new[]
             {
                 loadSimulator,
                 getCollisionSystem,
                 getCollisionSkins,
                 skinListConstructor,
                 skinSnapshotStore,
             })
    {
        instruction.OpCode = OpCodes.Nop;
        instruction.Operand = null;
    }

    Instruction containsBody = clear.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.Name == "Contains"
        && called.Parameters.Count == 1
        && called.DeclaringType.FullName
            == "System.Collections.ObjectModel.ReadOnlyCollection`1<"
               + "JigLibX.Physics.Body>");
    Instruction bodySkip = containsBody.Next?.Operand as Instruction
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear body-loop branch is missing.");
    Instruction[] bodyCleanup =
    [
        Instruction.Create(OpCodes.Ldloc, body),
        Instruction.Create(OpCodes.Ldnull),
        Instruction.Create(OpCodes.Callvirt, bodySetCollisionSkin),
        Instruction.Create(OpCodes.Ldloc, body),
        Instruction.Create(OpCodes.Ldnull),
        Instruction.Create(OpCodes.Stfld, bodyTag),
    ];
    containsBody.Next!.Operand = bodyCleanup[0];
    foreach (Instruction instruction in bodyCleanup)
    {
        processor.InsertBefore(bodySkip, instruction);
    }

    Instruction removeSkin = clear.Body.Instructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.DeclaringType.FullName
            == "JigLibX.Collision.CollisionSystem"
        && called.Name == "RemoveCollisionSkin");
    Instruction removeSkinStart = removeSkin.Previous?.Previous?.Previous
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear collision-skin removal is incomplete.");
    Instruction[] skinCleanup =
    [
        Instruction.Create(OpCodes.Ldloc, skin),
        Instruction.Create(OpCodes.Ldnull),
        Instruction.Create(OpCodes.Callvirt, setOwner),
        Instruction.Create(OpCodes.Ldloc, skin),
        Instruction.Create(OpCodes.Ldnull),
        Instruction.Create(OpCodes.Callvirt, setCollisionSystem),
        Instruction.Create(OpCodes.Ldloc, skin),
        Instruction.Create(OpCodes.Ldnull),
        Instruction.Create(OpCodes.Callvirt, setTag),
        Instruction.Create(OpCodes.Ldloc, skin),
        Instruction.Create(OpCodes.Callvirt, getCollisions),
        Instruction.Create(OpCodes.Callvirt, clearCollisionList),
        Instruction.Create(OpCodes.Ldloc, skin),
        Instruction.Create(OpCodes.Callvirt, getNonCollidables),
        Instruction.Create(OpCodes.Callvirt, clearNonCollidables),
        Instruction.Create(OpCodes.Ldloc, skin),
        Instruction.Create(OpCodes.Callvirt, removeAllPrimitives),
    ];
    foreach (Instruction instruction in skinCleanup)
    {
        processor.InsertBefore(removeSkinStart, instruction);
    }

    Instruction oldGetCollisions = clear.Body.Instructions.Single(
        instruction => instruction.Operand is MethodReference called
            && called.FullName == getCollisions.FullName
            && !skinCleanup.Contains(instruction));
    Instruction oldLoadSkin = oldGetCollisions.Previous
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear collision-list load is missing.");
    Instruction oldClearCollisions = oldGetCollisions.Next
        ?? throw new InvalidOperationException(
            "PhysicsManager.Clear collision-list clear is missing.");
    foreach (Instruction instruction in new[]
             {
                 oldLoadSkin,
                 oldGetCollisions,
                 oldClearCollisions,
             })
    {
        instruction.OpCode = OpCodes.Nop;
        instruction.Operand = null;
    }
    clear.Body.MaxStackSize = Math.Max(clear.Body.MaxStackSize, 2);
}

static MethodReference RequireCallReference(
    MethodDefinition method,
    string declaringType,
    string name)
{
    MethodReference[] matches = method.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Where(called =>
            called.DeclaringType.FullName == declaringType
            && called.Name == name)
        .DistinctBy(called => called.FullName)
        .ToArray();
    if (matches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one " + declaringType + "." + name
            + " reference in " + method.FullName
            + ", found " + matches.Length + ".");
    }

    return matches[0];
}

static void RepairMeteorShowerRemoveReferences(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition meteorShower = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MeteorShower");
    MethodDefinition onRemove = RequireMethod(
        meteorShower,
        "OnRemove",
        parameterCount: 0);
    FieldDefinition ttl = meteorShower.Fields.Single(field =>
        field.Name == "mTTL" && field.FieldType.FullName == "System.Single");
    FieldDefinition scene = meteorShower.Fields.Single(field =>
        field.Name == "mScene"
        && field.FieldType.FullName == "Magicka.Levels.GameScene");
    FieldDefinition owner = meteorShower.Fields.Single(field =>
        field.Name == "mOwner"
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.ISpellCaster");
    FieldDefinition rumble = meteorShower.Fields.Single(field =>
        field.Name == "mRumble"
        && field.FieldType.FullName
            == "Microsoft.Xna.Framework.Audio.Cue");
    if (onRemove.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference stored
            && (stored.FullName == scene.FullName
                || stored.FullName == owner.FullName
                || stored.FullName == rumble.FullName)))
    {
        throw new InvalidOperationException(
            "MeteorShower.OnRemove reference cleanup already exists.");
    }

    MethodReference setLightTargetIntensity = RequireCallReference(
        onRemove,
        "Magicka.Levels.GameScene",
        "set_LightTargetIntensity");
    MethodReference getIsStopping = RequireCallReference(
        onRemove,
        "Microsoft.Xna.Framework.Audio.Cue",
        "get_IsStopping");
    MethodReference stop = RequireCallReference(
        onRemove,
        "Microsoft.Xna.Framework.Audio.Cue",
        "Stop");

    MethodBody body = onRemove.Body;
    body.Instructions.Clear();
    body.ExceptionHandlers.Clear();
    body.Variables.Clear();
    body.InitLocals = true;
    body.MaxStackSize = 2;
    VariableDefinition oldScene = new VariableDefinition(scene.FieldType);
    VariableDefinition oldRumble = new VariableDefinition(rumble.FieldType);
    body.Variables.Add(oldScene);
    body.Variables.Add(oldRumble);
    ILProcessor processor = body.GetILProcessor();
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);

    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldc_R4, 0f));
    processor.Append(Instruction.Create(OpCodes.Stfld, ttl));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, scene));
    processor.Append(Instruction.Create(OpCodes.Stloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, rumble));
    processor.Append(Instruction.Create(OpCodes.Stloc, oldRumble));
    foreach (FieldDefinition field in new[] { scene, owner, rumble })
    {
        processor.Append(Instruction.Create(OpCodes.Ldarg_0));
        processor.Append(Instruction.Create(OpCodes.Ldnull));
        processor.Append(Instruction.Create(OpCodes.Stfld, field));
    }
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Ldc_R4, 1f));
    processor.Append(Instruction.Create(
        OpCodes.Callvirt,
        setLightTargetIntensity));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldRumble));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnInstruction));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldRumble));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getIsStopping));
    processor.Append(Instruction.Create(OpCodes.Brtrue, returnInstruction));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldRumble));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, stop));
    processor.Append(returnInstruction);
}

static void RepairBlizzardRemoveReferences(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition blizzard = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Blizzard");
    MethodDefinition onRemove = RequireMethod(
        blizzard,
        "OnRemove",
        parameterCount: 0);
    FieldDefinition ttl = blizzard.Fields.Single(field =>
        field.Name == "mTTL" && field.FieldType.FullName == "System.Single");
    FieldDefinition scene = blizzard.Fields.Single(field =>
        field.Name == "mScene"
        && field.FieldType.FullName == "Magicka.Levels.GameScene");
    FieldDefinition caster = blizzard.Fields.Single(field =>
        field.Name == "mCaster"
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.ISpellCaster");
    FieldDefinition ambience = blizzard.Fields.Single(field =>
        field.Name == "mAmbience"
        && field.FieldType.FullName
            == "Microsoft.Xna.Framework.Audio.Cue");
    if (onRemove.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stfld
            && instruction.Operand is FieldReference stored
            && (stored.FullName == scene.FullName
                || stored.FullName == caster.FullName
                || stored.FullName == ambience.FullName)))
    {
        throw new InvalidOperationException(
            "Blizzard.OnRemove reference cleanup already exists.");
    }

    MethodReference stop = RequireCallReference(
        onRemove,
        "Microsoft.Xna.Framework.Audio.Cue",
        "Stop");
    MethodBody body = onRemove.Body;
    body.Instructions.Clear();
    body.ExceptionHandlers.Clear();
    body.Variables.Clear();
    body.InitLocals = true;
    body.MaxStackSize = 2;
    VariableDefinition oldAmbience = new VariableDefinition(
        ambience.FieldType);
    body.Variables.Add(oldAmbience);
    ILProcessor processor = body.GetILProcessor();
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);

    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldc_R4, 0f));
    processor.Append(Instruction.Create(OpCodes.Stfld, ttl));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, ambience));
    processor.Append(Instruction.Create(OpCodes.Stloc, oldAmbience));
    foreach (FieldDefinition field in new[] { scene, caster, ambience })
    {
        processor.Append(Instruction.Create(OpCodes.Ldarg_0));
        processor.Append(Instruction.Create(OpCodes.Ldnull));
        processor.Append(Instruction.Create(OpCodes.Stfld, field));
    }
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldAmbience));
    processor.Append(Instruction.Create(OpCodes.Brfalse, returnInstruction));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldAmbience));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, stop));
    processor.Append(returnInstruction);
}

static void RepairControllerAvatarDetach(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition avatar = RequireType(
        types,
        "Magicka.GameLogic.Entities.Avatar");
    TypeDefinition controller = RequireType(
        types,
        "Magicka.GameLogic.Controls.Controller");
    FieldDefinition controllerAvatar = controller.Fields.Single(field =>
        field.Name == "mAvatar"
        && !field.IsStatic
        && field.FieldType.FullName == avatar.FullName);
    if (controller.Methods.Any(method =>
            method.Name == "CommunityPatchDetachAvatar"))
    {
        throw new InvalidOperationException(
            "Controller Avatar detach already exists.");
    }

    MethodDefinition detachAvatar = new MethodDefinition(
        "CommunityPatchDetachAvatar",
        MethodAttributes.Assembly | MethodAttributes.HideBySig,
        controller.Module.TypeSystem.Void);
    detachAvatar.Parameters.Add(new ParameterDefinition(
        "expected",
        ParameterAttributes.None,
        avatar));
    controller.Methods.Add(detachAvatar);
    ILProcessor detachProcessor = detachAvatar.Body.GetILProcessor();
    Instruction detachReturn = Instruction.Create(OpCodes.Ret);
    detachProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldfld, controllerAvatar));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    detachProcessor.Append(Instruction.Create(OpCodes.Bne_Un, detachReturn));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    detachProcessor.Append(Instruction.Create(OpCodes.Ldnull));
    detachProcessor.Append(Instruction.Create(OpCodes.Stfld, controllerAvatar));
    detachProcessor.Append(detachReturn);
    detachAvatar.Body.MaxStackSize = 2;

    TypeDefinition player = RequireType(types, "Magicka.GameLogic.Player");
    FieldDefinition playerAvatar = player.Fields.Single(field =>
        field.Name == "mAvatar"
        && !field.IsStatic
        && field.FieldType.FullName == "System.WeakReference");
    MethodDefinition getAvatar = RequireMethod(
        player,
        "get_Avatar",
        parameterCount: 0);
    MethodDefinition setAvatar = RequireMethod(
        player,
        "set_Avatar",
        parameterCount: 1);
    MethodDefinition getController = RequireMethod(
        player,
        "get_Controller",
        parameterCount: 0);
    MethodReference getWeakTarget = RequireCallReference(
        getAvatar,
        "System.WeakReference",
        "get_Target");
    if (setAvatar.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference called
            && called.FullName == detachAvatar.FullName))
    {
        throw new InvalidOperationException(
            "Player.Avatar already detaches the controller reference.");
    }

    VariableDefinition previousAvatar = new VariableDefinition(avatar);
    VariableDefinition currentController = new VariableDefinition(controller);
    setAvatar.Body.Variables.Add(previousAvatar);
    setAvatar.Body.Variables.Add(currentController);
    setAvatar.Body.InitLocals = true;
    Instruction originalStart = setAvatar.Body.Instructions[0];
    ILProcessor setterProcessor = setAvatar.Body.GetILProcessor();
    Instruction[] detachOldAvatar =
    [
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, playerAvatar),
        Instruction.Create(OpCodes.Callvirt, getWeakTarget),
        Instruction.Create(OpCodes.Isinst, avatar),
        Instruction.Create(OpCodes.Stloc, previousAvatar),
        Instruction.Create(OpCodes.Ldarg_1),
        Instruction.Create(OpCodes.Brtrue, originalStart),
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Call, getController),
        Instruction.Create(OpCodes.Stloc, currentController),
        Instruction.Create(OpCodes.Ldloc, currentController),
        Instruction.Create(OpCodes.Brfalse, originalStart),
        Instruction.Create(OpCodes.Ldloc, currentController),
        Instruction.Create(OpCodes.Ldloc, previousAvatar),
        Instruction.Create(OpCodes.Callvirt, detachAvatar),
    ];
    foreach (Instruction instruction in detachOldAvatar)
    {
        setterProcessor.InsertBefore(originalStart, instruction);
    }
    setAvatar.Body.MaxStackSize = Math.Max(setAvatar.Body.MaxStackSize, 2);
}

static void RepairJudgementSprayConditionCache(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string helperName =
        "CommunityPatchTakeConditionCollectionLocked";
    TypeDefinition judgementSpray = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.JudgementSpray");
    TypeDefinition projectileSpell = RequireType(
        types,
        "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell");
    TypeDefinition conditionCollection = RequireType(
        types,
        "Magicka.GameLogic.Entities.Items.ConditionCollection");
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");
    if (judgementSpray.Methods.Any(method => method.Name == helperName))
    {
        throw new InvalidOperationException(
            "JudgementSpray condition-cache repair already exists.");
    }

    FieldDefinition cache = projectileSpell.Fields.Single(field =>
        field.Name == "sCachedConditions"
        && field.IsStatic
        && field.FieldType.FullName
            == "System.Collections.Generic.Queue`1<"
               + conditionCollection.FullName + ">");
    MethodDefinition spawnProjectile = RequireMethod(
        judgementSpray,
        "SpawnProjectile",
        parameterCount: 5);
    Instruction[] dequeueInstructions = spawnProjectile.Body.Instructions
        .Where(instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodReference called
            && called.Name == "Dequeue"
            && called.Parameters.Count == 0
            && called.DeclaringType.FullName == cache.FieldType.FullName)
        .ToArray();
    if (dequeueInstructions.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one JudgementSpray condition-cache dequeue, found "
            + dequeueInstructions.Length + ".");
    }

    Instruction dequeueInstruction = dequeueInstructions[0];
    Instruction? cacheLoad = dequeueInstruction.Previous;
    if (cacheLoad is null
        || cacheLoad.OpCode != OpCodes.Ldsfld
        || cacheLoad.Operand is not FieldReference loadedCache
        || loadedCache.FullName != cache.FullName)
    {
        throw new InvalidOperationException(
            "Unexpected JudgementSpray condition-cache dequeue shape.");
    }

    MethodReference dequeue = (MethodReference)dequeueInstruction.Operand;
    MethodReference getCount = FindMethodReference(
        module,
        cache.FieldType.FullName,
        "get_Count",
        parameterCount: 0,
        returnType: "System.Int32");
    MethodDefinition constructor = RequireMethod(
        conditionCollection,
        ".ctor",
        parameterCount: 0);
    MethodDefinition sendRuntimeGuard = RequireMethod(
        patchTelemetry,
        "SendRuntimeGuard",
        parameterCount: 6);

    MethodDefinition takeConditions = new MethodDefinition(
        helperName,
        MethodAttributes.Private
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        conditionCollection);
    takeConditions.Parameters.Add(new ParameterDefinition(
        "cache",
        ParameterAttributes.None,
        cache.FieldType));
    judgementSpray.Methods.Add(takeConditions);

    ILProcessor helperProcessor = takeConditions.Body.GetILProcessor();
    Instruction dequeueCached = Instruction.Create(OpCodes.Ldarg_0);
    helperProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    helperProcessor.Append(Instruction.Create(OpCodes.Callvirt, getCount));
    helperProcessor.Append(Instruction.Create(OpCodes.Brtrue, dequeueCached));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "magicka_patch_runtime_recovery"));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "judgement_spray_condition_cache_empty_recovered"));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "ProjectileSpell.sCachedConditions"));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        judgementSpray.FullName));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "Allocated a replacement ConditionCollection and continued"
        + " projectile spawn."));
    helperProcessor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    helperProcessor.Append(Instruction.Create(
        OpCodes.Call,
        sendRuntimeGuard));
    helperProcessor.Append(Instruction.Create(OpCodes.Newobj, constructor));
    helperProcessor.Append(Instruction.Create(OpCodes.Ret));
    helperProcessor.Append(dequeueCached);
    helperProcessor.Append(Instruction.Create(OpCodes.Callvirt, dequeue));
    helperProcessor.Append(Instruction.Create(OpCodes.Ret));
    takeConditions.Body.MaxStackSize = 6;

    dequeueInstruction.OpCode = OpCodes.Call;
    dequeueInstruction.Operand = takeConditions;
}

static void RepairJormungandrNullTarget(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition jormungandr = RequireType(
        types,
        "Magicka.GameLogic.Entities.Bosses.Jormungandr");
    TypeDefinition undergroundState = RequireType(
        types,
        "Magicka.GameLogic.Entities.Bosses.Jormungandr/UndergroundState");
    FieldDefinition targetField = jormungandr.Fields.Single(field =>
        field.Name == "mTarget"
        && !field.IsStatic
        && field.FieldType.FullName
            == "Magicka.GameLogic.Entities.Character");
    MethodDefinition selectTarget = RequireMethod(
        jormungandr,
        "SelectTarget",
        parameterCount: 1);
    MethodDefinition update = RequireMethod(
        undergroundState,
        "OnUpdate",
        parameterCount: 2);

    Instruction[] instructions = update.Body.Instructions.ToArray();
    Instruction[] targetSelections = instructions.Where(instruction =>
            IsMethodCall(instruction, selectTarget))
        .ToArray();
    if (targetSelections.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one Jormungandr underground target selection, found "
            + targetSelections.Length + ".");
    }

    Instruction selection = targetSelections[0];
    Instruction continueInstruction = selection.Next
        ?? throw new InvalidOperationException(
            "Jormungandr target selection has no following instruction.");
    if (continueInstruction.OpCode != OpCodes.Ldc_R4
        || continueInstruction.Operand is not float warningStrength
        || warningStrength != 0.5f)
    {
        throw new InvalidOperationException(
            "Unexpected Jormungandr post-selection instruction; the null-target"
            + " guard may already exist or the game method changed.");
    }

    ILProcessor processor = update.Body.GetILProcessor();
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Ldarg_2));
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Ldfld, targetField));
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Brtrue, continueInstruction));
    processor.InsertBefore(
        continueInstruction,
        Instruction.Create(OpCodes.Ret));
    update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 1);
}

static void RepairRailgunParentCycles(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string cycleCheckName =
        "CommunityPatchWouldCreateParentCycle";
    const string reportName =
        "CommunityPatchReportParentCycleRecovery";

    TypeDefinition railgun = RequireType(
        types,
        "Magicka.GameLogic.Spells.Railgun");
    if (railgun.Methods.Any(method =>
            method.Name == cycleCheckName
            || method.Name == reportName))
    {
        throw new InvalidOperationException(
            "Railgun parent-cycle repair already exists.");
    }

    FieldDefinition parentsField = railgun.Fields.Single(field =>
        field.Name == "mParents"
        && field.FieldType.FullName
            == "System.Collections.Generic.List`1<"
               + railgun.FullName + ">");
    FieldDefinition lockTraversalActive = new FieldDefinition(
        "mCommunityPatchLockAllActive",
        FieldAttributes.Private,
        module.TypeSystem.Boolean);
    railgun.Fields.Add(lockTraversalActive);
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");
    MethodDefinition sendRuntimeGuard = RequireMethod(
        patchTelemetry,
        "SendRuntimeGuard",
        parameterCount: 6);

    MethodReference getParentCount = FindMethodReference(
        module,
        parentsField.FieldType.FullName,
        "get_Count",
        parameterCount: 0,
        returnType: "System.Int32");
    MethodReference getParentItem = railgun.Methods
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Where(method =>
            method.DeclaringType.FullName == parentsField.FieldType.FullName
            && method.Name == "get_Item"
            && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "System.Int32")
        .GroupBy(method => method.FullName, StringComparer.Ordinal)
        .Select(group => group.First())
        .Single();
    MethodReference referenceEquals = FindMethodReference(
        module,
        "System.Object",
        "ReferenceEquals",
        parameterCount: 2,
        returnType: "System.Boolean");
    MethodReference stringBuilderConstructor = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        ".ctor",
        parameterCount: 0,
        returnType: "System.Void");
    MethodReference appendString = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        "Append",
        parameterCount: 1,
        returnType: "System.Text.StringBuilder",
        parameterType: "System.String");
    TypeReference stringBuilderType = appendString.DeclaringType;
    MethodReference appendInteger = CreateInstanceMethodReference(
        "Append",
        stringBuilderType,
        stringBuilderType,
        module.TypeSystem.Int32);
    MethodReference stringBuilderToString = CreateInstanceMethodReference(
        "ToString",
        stringBuilderType,
        module.TypeSystem.String);

    MethodDefinition reportRecovery = new MethodDefinition(
        reportName,
        MethodAttributes.Private
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "reason",
        ParameterAttributes.None,
        module.TypeSystem.String));
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "visitedCount",
        ParameterAttributes.None,
        module.TypeSystem.Int32));
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "pendingCount",
        ParameterAttributes.None,
        module.TypeSystem.Int32));
    reportRecovery.Parameters.Add(new ParameterDefinition(
        "candidateParentCount",
        ParameterAttributes.None,
        module.TypeSystem.Int32));
    railgun.Methods.Add(reportRecovery);

    VariableDefinition details = new VariableDefinition(stringBuilderType);
    reportRecovery.Body.Variables.Add(details);
    reportRecovery.Body.InitLocals = true;
    ILProcessor reportProcessor = reportRecovery.Body.GetILProcessor();
    Instruction reportTryStart = Instruction.Create(OpCodes.Nop);
    Instruction reportHandlerStart = Instruction.Create(OpCodes.Pop);
    Instruction reportReturn = Instruction.Create(OpCodes.Ret);
    reportProcessor.Append(reportTryStart);
    reportProcessor.Append(Instruction.Create(
        OpCodes.Newobj,
        stringBuilderConstructor));
    reportProcessor.Append(Instruction.Create(OpCodes.Stloc, details));
    AppendStringBuilderText(
        reportProcessor,
        details,
        appendString,
        "visited_count=");
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    reportProcessor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    reportProcessor.Append(Instruction.Create(OpCodes.Pop));
    AppendStringBuilderText(
        reportProcessor,
        details,
        appendString,
        ";pending_count=");
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_2));
    reportProcessor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    reportProcessor.Append(Instruction.Create(OpCodes.Pop));
    AppendStringBuilderText(
        reportProcessor,
        details,
        appendString,
        ";candidate_parent_count=");
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_3));
    reportProcessor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    reportProcessor.Append(Instruction.Create(OpCodes.Pop));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "magicka_patch_runtime_recovery"));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "Railgun.mParents"));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "Magicka.GameLogic.Spells.Railgun"));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldloc, details));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Callvirt,
        stringBuilderToString));
    reportProcessor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    reportProcessor.Append(Instruction.Create(OpCodes.Call, sendRuntimeGuard));
    reportProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        reportReturn));
    reportProcessor.Append(reportHandlerStart);
    reportProcessor.Append(Instruction.Create(
        OpCodes.Leave,
        reportReturn));
    reportProcessor.Append(reportReturn);
    reportRecovery.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = reportTryStart,
        TryEnd = reportHandlerStart,
        HandlerStart = reportHandlerStart,
        HandlerEnd = reportReturn,
    });
    reportRecovery.Body.MaxStackSize = 6;

    MethodDefinition cycleCheck = new MethodDefinition(
        cycleCheckName,
        MethodAttributes.Private | MethodAttributes.HideBySig,
        module.TypeSystem.Boolean);
    cycleCheck.Parameters.Add(new ParameterDefinition(
        "candidate",
        ParameterAttributes.None,
        railgun));
    railgun.Methods.Add(cycleCheck);

    ArrayType railgunArray = new ArrayType(railgun);
    VariableDefinition pending = new VariableDefinition(railgunArray);
    VariableDefinition visited = new VariableDefinition(railgunArray);
    VariableDefinition pendingCount = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition visitedCount = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition current = new VariableDefinition(railgun);
    VariableDefinition visitedIndex = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition parent = new VariableDefinition(railgun);
    VariableDefinition parentIndex = new VariableDefinition(module.TypeSystem.Int32);
    VariableDefinition result = new VariableDefinition(module.TypeSystem.Boolean);
    cycleCheck.Body.Variables.Add(pending);
    cycleCheck.Body.Variables.Add(visited);
    cycleCheck.Body.Variables.Add(pendingCount);
    cycleCheck.Body.Variables.Add(visitedCount);
    cycleCheck.Body.Variables.Add(current);
    cycleCheck.Body.Variables.Add(visitedIndex);
    cycleCheck.Body.Variables.Add(parent);
    cycleCheck.Body.Variables.Add(parentIndex);
    cycleCheck.Body.Variables.Add(result);
    cycleCheck.Body.InitLocals = true;

    ILProcessor cycleProcessor = cycleCheck.Body.GetILProcessor();
    Instruction cycleTryStart = Instruction.Create(OpCodes.Nop);
    Instruction candidateValid = Instruction.Create(OpCodes.Ldc_I4, 256);
    Instruction outerCondition = Instruction.Create(OpCodes.Ldloc, pendingCount);
    Instruction outerBody = Instruction.Create(OpCodes.Ldloc, pendingCount);
    Instruction outerContinue = Instruction.Create(OpCodes.Nop);
    Instruction visitedCondition = Instruction.Create(OpCodes.Ldloc, visitedIndex);
    Instruction visitedBody = Instruction.Create(OpCodes.Ldloc, visited);
    Instruction currentUnique = Instruction.Create(OpCodes.Ldloc, current);
    Instruction parentCondition = Instruction.Create(OpCodes.Ldloc, parentIndex);
    Instruction parentBody = Instruction.Create(OpCodes.Ldloc, current);
    Instruction parentContinue = Instruction.Create(OpCodes.Ldloc, parentIndex);
    Instruction cycleDetected = Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_prevented");
    Instruction traversalLimit = Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_check_limit_reached");
    Instruction safeCompletion = Instruction.Create(OpCodes.Ldc_I4_0);
    Instruction cycleHandlerStart = Instruction.Create(OpCodes.Pop);
    Instruction cycleReturn = Instruction.Create(OpCodes.Ldloc, result);

    cycleProcessor.Append(cycleTryStart);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brtrue,
        candidateValid));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_check_failed"));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(candidateValid);
    cycleProcessor.Append(Instruction.Create(OpCodes.Newarr, railgun));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, 256));
    cycleProcessor.Append(Instruction.Create(OpCodes.Newarr, railgun));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visited));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, outerCondition));

    cycleProcessor.Append(outerBody);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Sub));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brfalse,
        outerContinue));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, visitedCondition));

    cycleProcessor.Append(visitedBody);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brtrue,
        outerContinue));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedIndex));
    cycleProcessor.Append(visitedCondition);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Blt, visitedBody));

    cycleProcessor.Append(currentUnique);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brtrue,
        cycleDetected));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, 256));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Bge,
        traversalLimit));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visited));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, parentIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, parentCondition));

    cycleProcessor.Append(parentBody);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, parentIndex));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentItem));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, parent));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, parent));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Brfalse,
        parentContinue));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4, 256));
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Bge,
        traversalLimit));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pending));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, parent));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stelem_Ref));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, pendingCount));
    cycleProcessor.Append(parentContinue);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Add));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, parentIndex));
    cycleProcessor.Append(parentCondition);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, current));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Blt, parentBody));

    cycleProcessor.Append(outerContinue);
    cycleProcessor.Append(outerCondition);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Bgt, outerBody));
    cycleProcessor.Append(Instruction.Create(OpCodes.Br, safeCompletion));

    cycleProcessor.Append(cycleDetected);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(traversalLimit);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, visitedCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldloc, pendingCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldarg_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldfld, parentsField));
    cycleProcessor.Append(Instruction.Create(OpCodes.Callvirt, getParentCount));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(safeCompletion);
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));

    cycleProcessor.Append(cycleHandlerStart);
    cycleProcessor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "railgun_parent_cycle_check_failed"));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    cycleProcessor.Append(Instruction.Create(OpCodes.Call, reportRecovery));
    cycleProcessor.Append(Instruction.Create(OpCodes.Ldc_I4_1));
    cycleProcessor.Append(Instruction.Create(OpCodes.Stloc, result));
    cycleProcessor.Append(Instruction.Create(OpCodes.Leave, cycleReturn));
    cycleProcessor.Append(cycleReturn);
    cycleProcessor.Append(Instruction.Create(OpCodes.Ret));
    cycleCheck.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = cycleTryStart,
        TryEnd = cycleHandlerStart,
        HandlerStart = cycleHandlerStart,
        HandlerEnd = cycleReturn,
    });
    cycleCheck.Body.MaxStackSize = 4;

    MethodDefinition lockAll = RequireMethod(
        railgun,
        "LockAll",
        parameterCount: 0);
    Instruction originalLockAllStart = lockAll.Body.Instructions[0];
    Instruction originalLockAllReturn = RequireSingleReturn(lockAll);
    ILProcessor lockProcessor = lockAll.Body.GetILProcessor();
    Instruction beginLockTraversal = Instruction.Create(OpCodes.Ldarg_0);
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ldarg_0));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ldfld, lockTraversalActive));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Brfalse, beginLockTraversal));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ret));
    lockProcessor.InsertBefore(originalLockAllStart, beginLockTraversal);
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Ldc_I4_1));
    lockProcessor.InsertBefore(
        originalLockAllStart,
        Instruction.Create(OpCodes.Stfld, lockTraversalActive));
    lockProcessor.InsertBefore(
        originalLockAllReturn,
        Instruction.Create(OpCodes.Ldarg_0));
    lockProcessor.InsertBefore(
        originalLockAllReturn,
        Instruction.Create(OpCodes.Ldc_I4_0));
    lockProcessor.InsertBefore(
        originalLockAllReturn,
        Instruction.Create(OpCodes.Stfld, lockTraversalActive));
    lockAll.Body.MaxStackSize = Math.Max(lockAll.Body.MaxStackSize, 2);

    MethodDefinition update = RequireMethod(
        railgun,
        "Update",
        parameterCount: 2);
    Instruction[] updateInstructions = update.Body.Instructions.ToArray();
    Instruction parentAdd = updateInstructions.Single(instruction =>
        instruction.Operand is MethodReference called
        && called.Name == "Add"
        && called.DeclaringType.FullName == parentsField.FieldType.FullName
        && updateInstructions
            .Skip(Math.Max(0, Array.IndexOf(updateInstructions, instruction) - 4))
            .Take(4)
            .Any(previous =>
                previous.OpCode == OpCodes.Ldfld
                && previous.Operand is FieldReference field
                && field.FullName == parentsField.FullName));
    int parentAddIndex = Array.IndexOf(updateInstructions, parentAdd);
    VariableDefinition selectedCandidate = updateInstructions
        .Take(parentAddIndex)
        .Reverse()
        .Select(instruction => instruction.Operand)
        .OfType<VariableDefinition>()
        .First(variable => variable.VariableType.FullName == railgun.FullName);
    Instruction selectedStore = updateInstructions
        .Take(parentAddIndex)
        .Where(instruction =>
            instruction.Operand == selectedCandidate
            && (instruction.OpCode == OpCodes.Stloc
                || instruction.OpCode == OpCodes.Stloc_S))
        .Last();
    int selectedStoreIndex = Array.IndexOf(updateInstructions, selectedStore);
    if (selectedStoreIndex == 0
        || updateInstructions[selectedStoreIndex - 1].Operand
            is not VariableDefinition activeCandidate
        || activeCandidate.VariableType.FullName != railgun.FullName)
    {
        throw new InvalidOperationException(
            "Could not identify the active Railgun candidate local.");
    }

    Instruction loopContinue = updateInstructions[selectedStoreIndex + 1];
    Instruction geometryGuard = updateInstructions
        .Take(selectedStoreIndex)
        .Where(instruction => instruction.Operand == loopContinue)
        .Last();
    int geometryGuardIndex = Array.IndexOf(updateInstructions, geometryGuard);
    Instruction mutationStart = updateInstructions[geometryGuardIndex + 1];
    if (mutationStart.OpCode != OpCodes.Ldnull)
    {
        throw new InvalidOperationException(
            "Unexpected Railgun candidate mutation start.");
    }

    ILProcessor updateProcessor = update.Body.GetILProcessor();
    foreach (Instruction injected in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Ldloc, activeCandidate),
                 Instruction.Create(OpCodes.Call, cycleCheck),
                 Instruction.Create(OpCodes.Brtrue, loopContinue),
             })
    {
        updateProcessor.InsertBefore(mutationStart, injected);
    }
    update.Body.MaxStackSize = Math.Max(update.Body.MaxStackSize, 2);
}

static void PatchWarlordAbilityDiagnostic(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    const string helperTypeName =
        "Magicka.CommunityPatch.WarlordAbilityDiagnostic";
    if (types.ContainsKey(helperTypeName))
    {
        throw new InvalidOperationException(
            helperTypeName + " already exists.");
    }

    TypeDefinition characterTemplate = RequireType(
        types,
        "Magicka.GameLogic.Entities.CharacterTemplate");
    TypeDefinition ability = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.Ability");
    TypeDefinition melee = RequireType(
        types,
        "Magicka.GameLogic.Entities.Abilities.Melee");
    TypeDefinition warlord = RequireType(
        types,
        "Magicka.GameLogic.Entities.Bosses.WarlordCharacter");
    TypeDefinition nonPlayerCharacter = RequireType(
        types,
        "Magicka.GameLogic.Entities.NonPlayerCharacter");
    TypeDefinition patchTelemetry = RequireType(
        types,
        "Magicka.CommunityPatch.PatchTelemetry");

    FieldDefinition disposedField = characterTemplate.Fields.Single(field =>
        field.Name == "mDisposed"
        && !field.IsStatic
        && field.FieldType.FullName == "System.Boolean");
    if (characterTemplate.Methods.Any(method =>
            method.Name == "CommunityPatchIsDisposed"
            && method.Parameters.Count == 0))
    {
        throw new InvalidOperationException(
            "CharacterTemplate.CommunityPatchIsDisposed already exists.");
    }

    MethodDefinition isDisposed = new MethodDefinition(
        "CommunityPatchIsDisposed",
        MethodAttributes.Assembly | MethodAttributes.HideBySig,
        module.TypeSystem.Boolean);
    characterTemplate.Methods.Add(isDisposed);
    ILProcessor disposedProcessor = isDisposed.Body.GetILProcessor();
    disposedProcessor.Append(Instruction.Create(OpCodes.Ldarg_0));
    disposedProcessor.Append(Instruction.Create(OpCodes.Ldfld, disposedField));
    disposedProcessor.Append(Instruction.Create(OpCodes.Ret));
    isDisposed.Body.MaxStackSize = 1;

    MethodDefinition getTemplateAbilities = RequireMethod(
        characterTemplate,
        "get_Abilities",
        parameterCount: 0);
    MethodDefinition getTemplateId = RequireMethod(
        characterTemplate,
        "get_ID",
        parameterCount: 0);
    MethodDefinition getTemplateName = RequireMethod(
        characterTemplate,
        "get_Name",
        parameterCount: 0);
    MethodDefinition getNpcAbilities = RequireMethod(
        nonPlayerCharacter,
        "get_Abilities",
        parameterCount: 0);
    MethodDefinition sendRuntimeGuard = RequireMethod(
        patchTelemetry,
        "SendRuntimeGuard",
        parameterCount: 6);

    MethodReference getObjectType = FindMethodReference(
        module,
        "System.Object",
        "GetType",
        parameterCount: 0,
        returnType: "System.Type");
    MethodReference getTypeFullName = FindMethodReference(
        module,
        "System.Type",
        "get_FullName",
        parameterCount: 0,
        returnType: "System.String");
    MethodReference referenceEquals = FindMethodReference(
        module,
        "System.Object",
        "ReferenceEquals",
        parameterCount: 2,
        returnType: "System.Boolean");
    MethodReference stringBuilderConstructor = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        ".ctor",
        parameterCount: 0,
        returnType: "System.Void");
    MethodReference appendString = FindMethodReference(
        module,
        "System.Text.StringBuilder",
        "Append",
        parameterCount: 1,
        returnType: "System.Text.StringBuilder",
        parameterType: "System.String");
    TypeReference stringBuilderType = appendString.DeclaringType;
    MethodReference stringBuilderToString = CreateInstanceMethodReference(
        "ToString",
        stringBuilderType,
        module.TypeSystem.String);
    MethodReference appendBoolean = CreateInstanceMethodReference(
        "Append",
        stringBuilderType,
        stringBuilderType,
        module.TypeSystem.Boolean);
    MethodReference appendInteger = CreateInstanceMethodReference(
        "Append",
        stringBuilderType,
        stringBuilderType,
        module.TypeSystem.Int32);

    TypeDefinition helperType = new TypeDefinition(
        "Magicka.CommunityPatch",
        "WarlordAbilityDiagnostic",
        TypeAttributes.NotPublic
        | TypeAttributes.Abstract
        | TypeAttributes.Sealed
        | TypeAttributes.BeforeFieldInit
        | TypeAttributes.Class,
        module.TypeSystem.Object);
    module.Types.Add(helperType);
    MethodDefinition inspect = new MethodDefinition(
        "Inspect",
        MethodAttributes.Assembly
        | MethodAttributes.Static
        | MethodAttributes.HideBySig,
        module.TypeSystem.Void);
    inspect.Parameters.Add(new ParameterDefinition(
        "template",
        ParameterAttributes.None,
        characterTemplate));
    inspect.Parameters.Add(new ParameterDefinition(
        "abilities",
        ParameterAttributes.None,
        getNpcAbilities.ReturnType));
    helperType.Methods.Add(inspect);

    VariableDefinition primaryAbility = new VariableDefinition(ability);
    VariableDefinition primaryType = new VariableDefinition(module.TypeSystem.String);
    VariableDefinition details = new VariableDefinition(stringBuilderType);
    inspect.Body.Variables.Add(primaryAbility);
    inspect.Body.Variables.Add(primaryType);
    inspect.Body.Variables.Add(details);
    inspect.Body.InitLocals = true;
    ILProcessor processor = inspect.Body.GetILProcessor();

    Instruction tryStart = Instruction.Create(OpCodes.Nop);
    Instruction inspectPrimary = Instruction.Create(OpCodes.Ldloc, primaryAbility);
    Instruction invalidPrimary = Instruction.Create(OpCodes.Ldloc, primaryAbility);
    Instruction readPrimaryType = Instruction.Create(OpCodes.Ldloc, primaryAbility);
    Instruction primaryTypeReady = Instruction.Create(OpCodes.Newobj, stringBuilderConstructor);
    Instruction appendEmptyTemplateId = Instruction.Create(OpCodes.Ldloc, details);
    Instruction templateIdReady = Instruction.Create(OpCodes.Ldloc, details);
    Instruction templateNotDisposed = Instruction.Create(OpCodes.Ldc_I4_0);
    Instruction emptyTemplateName = Instruction.Create(OpCodes.Ldstr, string.Empty);
    Instruction sendDiagnostic = Instruction.Create(OpCodes.Call, sendRuntimeGuard);
    Instruction handlerStart = Instruction.Create(OpCodes.Pop);
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);

    processor.Append(tryStart);
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryAbility));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Brfalse, inspectPrimary));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldlen));
    processor.Append(Instruction.Create(OpCodes.Conv_I4));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Ble, inspectPrimary));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldc_I4_0));
    processor.Append(Instruction.Create(OpCodes.Ldelem_Ref));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryAbility));
    processor.Append(inspectPrimary);
    processor.Append(Instruction.Create(OpCodes.Isinst, melee));
    processor.Append(Instruction.Create(OpCodes.Brfalse, invalidPrimary));
    processor.Append(Instruction.Create(OpCodes.Leave, returnInstruction));

    processor.Append(invalidPrimary);
    processor.Append(Instruction.Create(OpCodes.Brtrue, readPrimaryType));
    processor.Append(Instruction.Create(OpCodes.Ldstr, "null"));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryType));
    processor.Append(Instruction.Create(OpCodes.Br, primaryTypeReady));
    processor.Append(readPrimaryType);
    processor.Append(Instruction.Create(OpCodes.Callvirt, getObjectType));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTypeFullName));
    processor.Append(Instruction.Create(OpCodes.Stloc, primaryType));

    processor.Append(primaryTypeReady);
    processor.Append(Instruction.Create(OpCodes.Stloc, details));
    AppendStringBuilderText(processor, details, appendString, "template_null=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendBoolean));
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";template_disposed=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Brfalse, templateNotDisposed));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Call, isDisposed));
    Instruction appendDisposed = Instruction.Create(OpCodes.Callvirt, appendBoolean);
    processor.Append(Instruction.Create(OpCodes.Br, appendDisposed));
    processor.Append(templateNotDisposed);
    processor.Append(appendDisposed);
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";template_id=");
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Brfalse, appendEmptyTemplateId));
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTemplateId));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendInteger));
    processor.Append(Instruction.Create(OpCodes.Pop));
    processor.Append(Instruction.Create(OpCodes.Br, templateIdReady));
    processor.Append(appendEmptyTemplateId);
    processor.Append(Instruction.Create(OpCodes.Ldstr, string.Empty));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendString));
    processor.Append(Instruction.Create(OpCodes.Pop));

    processor.Append(templateIdReady);
    processor.Append(Instruction.Create(OpCodes.Ldstr, ";abilities_null="));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendString));
    processor.Append(Instruction.Create(OpCodes.Pop));
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendBoolean));
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";ability_count=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    Instruction appendZeroLength = Instruction.Create(OpCodes.Ldc_I4_0);
    Instruction appendLength = Instruction.Create(OpCodes.Callvirt, appendInteger);
    processor.Append(Instruction.Create(OpCodes.Brfalse, appendZeroLength));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldlen));
    processor.Append(Instruction.Create(OpCodes.Conv_I4));
    processor.Append(Instruction.Create(OpCodes.Br, appendLength));
    processor.Append(appendZeroLength);
    processor.Append(appendLength);
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(processor, details, appendString, ";primary_null=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldloc, primaryAbility));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Ceq));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendBoolean));
    processor.Append(Instruction.Create(OpCodes.Pop));

    AppendStringBuilderText(
        processor,
        details,
        appendString,
        ";shares_template_abilities=");
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    Instruction noSharedTemplateAbilities = Instruction.Create(OpCodes.Ldc_I4_0);
    processor.Append(Instruction.Create(
        OpCodes.Brfalse,
        noSharedTemplateAbilities));
    processor.Append(Instruction.Create(OpCodes.Ldarg_1));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTemplateAbilities));
    processor.Append(Instruction.Create(OpCodes.Call, referenceEquals));
    Instruction appendSharesArray = Instruction.Create(OpCodes.Callvirt, appendBoolean);
    processor.Append(Instruction.Create(OpCodes.Br, appendSharesArray));
    processor.Append(noSharedTemplateAbilities);
    processor.Append(appendSharesArray);
    processor.Append(Instruction.Create(OpCodes.Pop));

    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "magicka_patch_warlord_ability_diagnostic"));
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "warlord_primary_ability_not_melee"));
    processor.Append(Instruction.Create(
        OpCodes.Ldstr,
        "NonPlayerCharacter.Abilities"));
    processor.Append(Instruction.Create(OpCodes.Ldloc, primaryType));
    processor.Append(Instruction.Create(OpCodes.Ldloc, details));
    processor.Append(Instruction.Create(OpCodes.Callvirt, stringBuilderToString));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Brfalse, emptyTemplateName));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, getTemplateName));
    processor.Append(Instruction.Create(OpCodes.Br, sendDiagnostic));
    processor.Append(emptyTemplateName);
    processor.Append(sendDiagnostic);
    processor.Append(Instruction.Create(OpCodes.Leave, returnInstruction));
    processor.Append(handlerStart);
    processor.Append(Instruction.Create(OpCodes.Leave, returnInstruction));
    processor.Append(returnInstruction);
    inspect.Body.ExceptionHandlers.Add(new ExceptionHandler(
        ExceptionHandlerType.Catch)
    {
        CatchType = module.TypeSystem.Object,
        TryStart = tryStart,
        TryEnd = handlerStart,
        HandlerStart = handlerStart,
        HandlerEnd = returnInstruction,
    });
    inspect.Body.MaxStackSize = 6;

    MethodDefinition applyTemplate = RequireMethod(
        warlord,
        "ApplyTemplate",
        parameterCount: 2);
    Instruction baseApply = applyTemplate.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.Name == "ApplyTemplate"
        && called.DeclaringType.FullName == nonPlayerCharacter.FullName);
    Instruction originalCast = applyTemplate.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Isinst
        && instruction.Operand is TypeReference type
        && type.FullName == melee.FullName);
    if (applyTemplate.Body.Instructions.IndexOf(baseApply)
        >= applyTemplate.Body.Instructions.IndexOf(originalCast))
    {
        throw new InvalidOperationException(
            "Unexpected WarlordCharacter.ApplyTemplate instruction order.");
    }

    ILProcessor applyProcessor = applyTemplate.Body.GetILProcessor();
    Instruction cursor = baseApply;
    foreach (Instruction injected in new[]
             {
                 Instruction.Create(OpCodes.Ldarg_1),
                 Instruction.Create(OpCodes.Ldarg_0),
                 Instruction.Create(OpCodes.Call, getNpcAbilities),
                 Instruction.Create(OpCodes.Call, inspect),
             })
    {
        applyProcessor.InsertAfter(cursor, injected);
        cursor = injected;
    }
    applyTemplate.Body.MaxStackSize = Math.Max(
        applyTemplate.Body.MaxStackSize,
        2);
}

static void AppendStringBuilderText(
    ILProcessor processor,
    VariableDefinition builder,
    MethodReference appendString,
    string text)
{
    processor.Append(Instruction.Create(OpCodes.Ldloc, builder));
    processor.Append(Instruction.Create(OpCodes.Ldstr, text));
    processor.Append(Instruction.Create(OpCodes.Callvirt, appendString));
    processor.Append(Instruction.Create(OpCodes.Pop));
}

static MethodReference FindMethodReference(
    ModuleDefinition module,
    string declaringType,
    string name,
    int parameterCount,
    string returnType,
    string? parameterType = null)
{
    MethodReference[] matches = AllTypes(module)
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Where(method => method.DeclaringType.FullName == declaringType
            && method.Name == name
            && method.Parameters.Count == parameterCount
            && method.ReturnType.FullName == returnType
            && (parameterType == null
                || (method.Parameters.Count == 1
                    && method.Parameters[0].ParameterType.FullName
                        == parameterType)))
        .GroupBy(method => method.FullName, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToArray();
    if (matches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one referenced method " + declaringType + "." + name
            + ", found " + matches.Length + ".");
    }
    return matches[0];
}

static MethodReference CreateInstanceMethodReference(
    string name,
    TypeReference declaringType,
    TypeReference returnType,
    params TypeReference[] parameterTypes)
{
    MethodReference method = new MethodReference(
        name,
        returnType,
        declaringType)
    {
        HasThis = true,
        CallingConvention = MethodCallingConvention.Default,
    };
    foreach (TypeReference parameterType in parameterTypes)
    {
        method.Parameters.Add(new ParameterDefinition(parameterType));
    }
    return method;
}

static void RepairCharacterTemplateStaticCaches(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    (string TypeName, string FieldName)[] existingCacheOwners =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonSpirit",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonFlamer",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonCross",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonZombie",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonBug",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonUndead",
            "sTemplates"),
    ];
    foreach ((string typeName, string fieldName) in existingCacheOwners)
    {
        TypeDefinition owner = RequireType(types, typeName);
        FieldDefinition field = RequireStaticCharacterTemplateField(
            owner,
            fieldName);
        MethodDefinition disposeCache = RequireMethod(
            owner,
            "DisposeCache",
            parameterCount: 0);
        AppendStaticFieldReset(disposeCache, field);
    }

    (string TypeName, string FieldName)[] newCacheOwners =
    [
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MutateBeastman",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.OtherworldlyDischarge",
            "sTemplate"),
        (
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonElemental",
            "sTemplate"),
    ];
    List<MethodDefinition> addedDisposeMethods = new List<MethodDefinition>();
    foreach ((string typeName, string fieldName) in newCacheOwners)
    {
        TypeDefinition owner = RequireType(types, typeName);
        FieldDefinition field = RequireStaticCharacterTemplateField(
            owner,
            fieldName);
        if (owner.Methods.Any(method =>
                method.Name == "DisposeCache"
                && method.Parameters.Count == 0))
        {
            throw new InvalidOperationException(
                owner.FullName + ".DisposeCache already exists.");
        }

        MethodDefinition disposeCache = new MethodDefinition(
            "DisposeCache",
            MethodAttributes.Assembly
            | MethodAttributes.Static
            | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        owner.Methods.Add(disposeCache);
        ILProcessor processor = disposeCache.Body.GetILProcessor();
        processor.Append(Instruction.Create(OpCodes.Ldnull));
        processor.Append(Instruction.Create(OpCodes.Stsfld, field));
        processor.Append(Instruction.Create(OpCodes.Ret));
        disposeCache.Body.MaxStackSize = 1;
        addedDisposeMethods.Add(disposeCache);
    }

    TypeDefinition magick = RequireType(
        types,
        "Magicka.GameLogic.Spells.Magick");
    MethodDefinition disposeMagicks = RequireMethod(
        magick,
        "DisposeMagicks",
        parameterCount: 0);
    Instruction disposeMagicksReturn = RequireSingleReturn(disposeMagicks);
    ILProcessor magickProcessor = disposeMagicks.Body.GetILProcessor();
    foreach (MethodDefinition disposeCache in addedDisposeMethods)
    {
        magickProcessor.InsertBefore(
            disposeMagicksReturn,
            Instruction.Create(OpCodes.Call, disposeCache));
    }

    TypeDefinition characterTemplate = RequireType(
        types,
        "Magicka.GameLogic.Entities.CharacterTemplate");
    FieldDefinition avatarTemplates = characterTemplate.Fields.Single(field =>
        field.Name == "sCachedAvatarTemplates"
        && field.IsStatic
        && field.FieldType.FullName
            == "System.Collections.Generic.Dictionary`2<System.String,"
               + "Magicka.GameLogic.Entities.CharacterTemplate>");
    MethodDefinition initializeAvatarCache = RequireMethod(
        characterTemplate,
        "InitialisePlayerAvatarCache",
        parameterCount: 1);
    MethodReference clearAvatarTemplates = initializeAvatarCache.Body.Instructions
        .Select(instruction => instruction.Operand)
        .OfType<MethodReference>()
        .Single(method =>
            method.Name == "Clear"
            && method.Parameters.Count == 0
            && method.DeclaringType.FullName == avatarTemplates.FieldType.FullName);
    MethodDefinition clearCache = RequireMethod(
        characterTemplate,
        "ClearCache",
        parameterCount: 0);
    if (clearCache.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Ldsfld
            && instruction.Operand is FieldReference field
            && field.FullName == avatarTemplates.FullName))
    {
        throw new InvalidOperationException(
            "CharacterTemplate.ClearCache already clears avatar templates.");
    }

    Instruction clearCacheReturn = RequireSingleReturn(clearCache);
    ILProcessor clearCacheProcessor = clearCache.Body.GetILProcessor();
    clearCacheProcessor.InsertBefore(
        clearCacheReturn,
        Instruction.Create(OpCodes.Ldsfld, avatarTemplates));
    clearCacheProcessor.InsertBefore(
        clearCacheReturn,
        Instruction.Create(OpCodes.Callvirt, clearAvatarTemplates));
    clearCache.Body.MaxStackSize = Math.Max(clearCache.Body.MaxStackSize, 1);
}

static FieldDefinition RequireStaticCharacterTemplateField(
    TypeDefinition owner,
    string fieldName)
{
    FieldDefinition field = owner.Fields.Single(candidate =>
        candidate.Name == fieldName
        && candidate.IsStatic
        && (candidate.FieldType.FullName
                == "Magicka.GameLogic.Entities.CharacterTemplate"
            || candidate.FieldType.FullName
                == "Magicka.GameLogic.Entities.CharacterTemplate[]"));
    return field;
}

static void AppendStaticFieldReset(
    MethodDefinition method,
    FieldDefinition field)
{
    if (!method.IsStatic || !method.HasBody)
    {
        throw new InvalidOperationException(
            "Expected a static cache teardown method: " + method.FullName);
    }

    if (method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Stsfld
            && instruction.Operand is FieldReference stored
            && stored.FullName == field.FullName))
    {
        throw new InvalidOperationException(
            method.FullName + " already resets " + field.FullName + ".");
    }

    Instruction returnInstruction = RequireSingleReturn(method);
    ILProcessor processor = method.Body.GetILProcessor();
    processor.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldnull));
    processor.InsertBefore(
        returnInstruction,
        Instruction.Create(OpCodes.Stsfld, field));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 1);
}

static Instruction RequireSingleReturn(MethodDefinition method)
{
    Instruction[] returns = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ret)
        .ToArray();
    if (returns.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one return in " + method.FullName
            + ", found " + returns.Length + ".");
    }

    return returns[0];
}

static void PatchPolygonHeadLightSceneDetachOnly(
    string inputPath,
    string outputPath)
{
    using AssemblyDefinition assembly = ReadAssembly(inputPath);
    Dictionary<string, TypeDefinition> types = AllTypes(assembly.MainModule)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    RepairLightSceneDetach(types);
    WriteAssembly(assembly, outputPath);
}

static void RepairLightSceneDetach(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition light = RequireType(types, "PolygonHead.Lights.Light");
    TypeDefinition scene = RequireType(types, "PolygonHead.Scene");
    FieldDefinition sceneField = light.Fields.Single(
        field => field.Name == "mScene"
                 && field.FieldType.FullName == scene.FullName);
    MethodDefinition onRemove = RequireMethod(
        light,
        "OnRemove",
        parameterCount: 0);
    MethodDefinition disable = RequireMethod(
        light,
        "Disable",
        parameterCount: 2);
    MethodDefinition update = RequireMethod(
        light,
        "Update",
        parameterCount: 4);
    MethodDefinition removeLight = RequireMethod(
        scene,
        "RemoveLight",
        parameterCount: 1);

    if (!onRemove.HasBody
        || onRemove.Body.Instructions.Count != 1
        || onRemove.Body.Instructions[0].OpCode != OpCodes.Ret)
    {
        throw new InvalidOperationException(
            "Expected an empty PolygonHead.Lights.Light.OnRemove method.");
    }

    RemoveSceneRemovalAfterOnRemove(
        disable,
        onRemove,
        sceneField,
        removeLight);
    RemoveSceneRemovalAfterOnRemove(
        update,
        onRemove,
        sceneField,
        removeLight);

    MethodBody body = onRemove.Body;
    body.Instructions.Clear();
    body.ExceptionHandlers.Clear();
    body.Variables.Clear();
    body.InitLocals = true;
    body.MaxStackSize = 2;

    VariableDefinition oldScene = new VariableDefinition(scene);
    body.Variables.Add(oldScene);
    ILProcessor processor = body.GetILProcessor();
    Instruction returnInstruction = Instruction.Create(OpCodes.Ret);
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldfld, sceneField));
    processor.Append(Instruction.Create(OpCodes.Stloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Ldnull));
    processor.Append(Instruction.Create(OpCodes.Stfld, sceneField));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Brfalse_S, returnInstruction));
    processor.Append(Instruction.Create(OpCodes.Ldloc, oldScene));
    processor.Append(Instruction.Create(OpCodes.Ldarg_0));
    processor.Append(Instruction.Create(OpCodes.Callvirt, removeLight));
    processor.Append(returnInstruction);
}

static void RemoveSceneRemovalAfterOnRemove(
    MethodDefinition method,
    MethodDefinition onRemove,
    FieldDefinition sceneField,
    MethodDefinition removeLight)
{
    Instruction[] instructions = method.Body.Instructions.ToArray();
    List<Instruction[]> matches = new List<Instruction[]>();
    for (int index = 0; index + 4 < instructions.Length; index++)
    {
        if (!IsMethodCall(instructions[index], onRemove)
            || instructions[index + 1].OpCode != OpCodes.Ldarg_0
            || !IsFieldLoad(instructions[index + 2], sceneField)
            || instructions[index + 3].OpCode != OpCodes.Ldarg_0
            || !IsMethodCall(instructions[index + 4], removeLight))
        {
            continue;
        }

        matches.Add(
        [
            instructions[index + 1],
            instructions[index + 2],
            instructions[index + 3],
            instructions[index + 4],
        ]);
    }

    if (matches.Count != 1)
    {
        throw new InvalidOperationException(
            "Expected one post-OnRemove scene removal in "
            + method.FullName + ", found " + matches.Count + ".");
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction instruction in matches[0].Reverse())
    {
        processor.Remove(instruction);
    }
}

static bool IsMethodCall(
    Instruction instruction,
    MethodDefinition expected)
{
    return (instruction.OpCode == OpCodes.Call
            || instruction.OpCode == OpCodes.Callvirt)
           && instruction.Operand is MethodReference called
           && called.FullName == expected.FullName;
}

static bool IsFieldLoad(
    Instruction instruction,
    FieldDefinition expected)
{
    return instruction.OpCode == OpCodes.Ldfld
           && instruction.Operand is FieldReference field
           && field.FullName == expected.FullName;
}

static AssemblyDefinition ReadAssembly(string path)
{
    return AssemblyDefinition.ReadAssembly(
        path,
        new ReaderParameters
        {
            InMemory = true,
            ReadSymbols = false,
        });
}

static (int DirectReferences, int TotalReferences) WriteAssembly(
    AssemblyDefinition assembly,
    string outputPath)
{
    (int directReferences, int totalReferences) = RebindSelfReferences(assembly);
    string temporaryPath = outputPath + ".tmp";
    if (File.Exists(temporaryPath))
    {
        File.Delete(temporaryPath);
    }

    assembly.Write(
        temporaryPath,
        new WriterParameters { WriteSymbols = false });
    File.Move(temporaryPath, outputPath, overwrite: true);
    return (directReferences, totalReferences);
}

static (int DirectReferences, int TotalReferences) RebindSelfReferences(
    AssemblyDefinition assembly)
{
    ModuleDefinition module = assembly.MainModule;
    AssemblyNameReference[] selfReferences = module.AssemblyReferences
        .Where(reference => string.Equals(
            reference.FullName,
            assembly.Name.FullName,
            StringComparison.Ordinal))
        .ToArray();
    if (selfReferences.Length == 0)
    {
        return (0, 0);
    }

    Dictionary<string, TypeDefinition> localTypes = AllTypes(module)
        .ToDictionary(type => type.FullName, StringComparer.Ordinal);
    int directReferences = 0;
    int totalReferences = 0;
    foreach (AssemblyNameReference selfReference in selfReferences)
    {
        ExportedType[] exportedTypes = module.ExportedTypes
            .Where(type => type.Scope == selfReference)
            .ToArray();
        if (exportedTypes.Length != 0)
        {
            throw new InvalidOperationException(
                assembly.Name.Name + " has exported types scoped through its"
                + " self-reference: "
                + string.Join(", ", exportedTypes.Select(type => type.FullName)));
        }

        TypeReference[] typeReferences = module.GetTypeReferences()
            .Where(type => type.Scope == selfReference)
            .ToArray();
        foreach (TypeReference typeReference in typeReferences)
        {
            if (!localTypes.ContainsKey(typeReference.FullName))
            {
                throw new InvalidOperationException(
                    "Self-scoped type reference does not resolve to a local type: "
                    + typeReference.FullName);
            }

            typeReference.Scope = module;
            totalReferences++;
            if (typeReference.DeclaringType is null)
            {
                directReferences++;
            }
        }

        module.AssemblyReferences.Remove(selfReference);
    }

    AssemblyNameReference[] remainingSelfReferences = module.AssemblyReferences
        .Where(reference => string.Equals(
            reference.FullName,
            assembly.Name.FullName,
            StringComparison.Ordinal))
        .ToArray();
    if (remainingSelfReferences.Length != 0)
    {
        throw new InvalidOperationException(
            assembly.Name.Name + " still references its own assembly identity.");
    }

    return (directReferences, totalReferences);
}

static void EnsureNotAlreadyPatched(AssemblyDefinition assembly)
{
    if (assembly.MainModule.AssemblyReferences.Any(
            reference => reference.Name == "Magicka.GcDiagnostics"))
    {
        throw new InvalidOperationException(
            assembly.Name.Name + " is already instrumented.");
    }
}

static TypeDefinition RequireType(
    IReadOnlyDictionary<string, TypeDefinition> types,
    string fullName)
{
    if (!types.TryGetValue(fullName, out TypeDefinition? type))
    {
        throw new InvalidOperationException("Required type not found: " + fullName);
    }

    return type;
}

static MethodDefinition RequireMethod(
    TypeDefinition type,
    string name,
    int parameterCount)
{
    MethodDefinition[] methods = type.Methods.Where(
            method => method.Name == name
                      && method.Parameters.Count == parameterCount)
        .ToArray();
    if (methods.Length != 1)
    {
        throw new InvalidOperationException(
            $"Expected one {type.FullName}.{name} method, found {methods.Length}.");
    }

    return methods[0];
}

static bool IsSameModuleSubclassOf(
    TypeDefinition type,
    string baseTypeName,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeReference? current = type;
    while (current is not null)
    {
        if (current.FullName == baseTypeName)
        {
            return true;
        }

        if (!types.TryGetValue(current.FullName, out TypeDefinition? definition))
        {
            return false;
        }

        current = definition.BaseType;
    }

    return false;
}

static int InstrumentCacheInsertionSites(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    Instruction[] originalInstructions = method.Body.Instructions.ToArray();
    List<Instruction> cacheCalls = new List<Instruction>();

    for (int index = 0; index < originalInstructions.Length; index++)
    {
        Instruction instruction = originalInstructions[index];
        if (instruction.Operand is not MethodReference called
            || (called.Name != "Add"
                && called.Name != "Enqueue"
                && called.Name != "Push"))
        {
            continue;
        }

        if (MatchesOwnerCacheInsertion(
                originalInstructions,
                index,
                method))
        {
            cacheCalls.Add(instruction);
        }
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction cacheCall in cacheCalls)
    {
        Instruction cursor = cacheCall;
        Instruction loadSelf = Instruction.Create(OpCodes.Ldarg_0);
        processor.InsertAfter(cursor, loadSelf);
        cursor = loadSelf;
        Instruction loadLifecycle = Instruction.Create(OpCodes.Ldstr, lifecycle);
        processor.InsertAfter(cursor, loadLifecycle);
        cursor = loadLifecycle;
        processor.InsertAfter(cursor, Instruction.Create(OpCodes.Call, helper));
    }

    if (cacheCalls.Count != 0)
    {
        method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 3);
    }

    return cacheCalls.Count;
}

static bool MatchesOwnerCacheInsertion(
    IReadOnlyList<Instruction> instructions,
    int callIndex,
    MethodDefinition method)
{
    if (callIndex < 2)
    {
        return false;
    }

    Instruction directCacheLoad = instructions[callIndex - 2];
    Instruction directOwnerLoad = instructions[callIndex - 1];
    if (directCacheLoad.OpCode == OpCodes.Ldsfld
        && directCacheLoad.Operand is FieldReference directCacheField
        && IsCacheOrPoolField(directCacheField.Name)
        && CacheElementMatchesOwner(
            directCacheField,
            method.DeclaringType)
        && directOwnerLoad.OpCode == OpCodes.Ldarg_0)
    {
        return true;
    }

    if (!method.IsStatic
        || method.Parameters.Count == 0
        || method.Parameters[0].ParameterType.FullName
            != method.DeclaringType.FullName)
    {
        return false;
    }

    int firstIndex = Math.Max(0, callIndex - 8);
    int cacheLoadIndex = -1;
    int capturedOwnerIndex = -1;
    for (int index = firstIndex; index < callIndex; index++)
    {
        Instruction instruction = instructions[index];
        if (instruction.OpCode == OpCodes.Ldsfld
            && instruction.Operand is FieldReference cacheField
            && IsCacheOrPoolField(cacheField.Name)
            && CacheElementMatchesOwner(
                cacheField,
                method.DeclaringType))
        {
            cacheLoadIndex = index;
        }

        if (instruction.OpCode == OpCodes.Ldfld
            && instruction.Operand is FieldReference capturedField
            && capturedField.FieldType.FullName
                == method.DeclaringType.FullName)
        {
            capturedOwnerIndex = index;
        }
    }

    return cacheLoadIndex >= firstIndex
           && capturedOwnerIndex > cacheLoadIndex
           && capturedOwnerIndex < callIndex;
}

static bool IsCacheOrPoolField(string fieldName)
{
    return (fieldName.Contains("cache", StringComparison.OrdinalIgnoreCase)
            || fieldName.Contains("pool", StringComparison.OrdinalIgnoreCase))
           && !fieldName.Contains("active", StringComparison.OrdinalIgnoreCase);
}

static bool CacheElementMatchesOwner(
    FieldReference field,
    TypeDefinition owner)
{
    GenericInstanceType? collection = field.FieldType as GenericInstanceType;
    return collection is not null
           && collection.GenericArguments.Any(
               argument => argument.FullName == owner.FullName);
}

static bool ReadsCacheOrPoolField(MethodDefinition method)
{
    return method.Body.Instructions.Any(
        instruction => (instruction.OpCode == OpCodes.Ldsfld
                        || instruction.OpCode == OpCodes.Ldsflda)
                       && instruction.Operand is FieldReference field
                       && IsCacheOrPoolField(field.Name));
}

static void RepairAvatarCacheExpansionBranch(MethodDefinition method)
{
    Instruction[] unresolvedShortBranches = method.Body.Instructions.Where(
            instruction => instruction.OpCode == OpCodes.Br_S
                           && instruction.Operand is null)
        .ToArray();
    if (unresolvedShortBranches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one overflowed short branch in " + method.FullName
            + ", found " + unresolvedShortBranches.Length + ".");
    }

    Instruction[] commonExitBranches = method.Body.Instructions.Where(
            instruction => instruction.OpCode == OpCodes.Br_S
                           && instruction.Operand is Instruction target
                           && target.OpCode == OpCodes.Leave_S)
        .ToArray();
    if (commonExitBranches.Length != 1)
    {
        throw new InvalidOperationException(
            "Expected one resolved short branch to the common exit in "
            + method.FullName + ", found " + commonExitBranches.Length + ".");
    }

    unresolvedShortBranches[0].OpCode = OpCodes.Br;
    unresolvedShortBranches[0].Operand = commonExitBranches[0].Operand;
}

static void RepairParadoxStoreWorkerDelegate(
    ModuleDefinition module,
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    TypeDefinition guards = RequireType(
        types,
        "Magicka.CommunityPatch.RuntimeCompatibilityGuards");
    MethodDefinition queue = RequireMethod(
        guards,
        "QueueParadoxStorePriceUpdate",
        parameterCount: 1);
    MethodDefinition worker = RequireMethod(
        guards,
        "RunParadoxStorePriceUpdate",
        parameterCount: 1);
    MethodDefinition update = RequireMethod(
        RequireType(
            types,
            "Magicka.CoreFramework.GameSystem.Store.StoreItemDatabase"),
        "UpdateParadoxItems",
        parameterCount: 0);

    if (queue.Parameters[0].ParameterType.FullName != "System.Action")
    {
        throw new InvalidOperationException(
            "Unexpected Paradox price worker delegate: "
            + queue.Parameters[0].ParameterType.FullName);
    }

    AssemblyNameReference mscorlib20 = module.AssemblyReferences.Single(
        reference => reference.Name == "mscorlib"
                     && reference.Version == new Version(2, 0, 0, 0));
    TypeReference threadStart = new TypeReference(
        "System.Threading",
        "ThreadStart",
        module,
        mscorlib20,
        valueType: false);

    Instruction workerCast = worker.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Castclass
        && instruction.Operand is TypeReference type
        && type.FullName == "System.Action");
    Instruction workerInvoke = worker.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Callvirt
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == "System.Action"
        && called.Name == "Invoke");
    Instruction actionConstructor = update.Body.Instructions.Single(
        instruction => instruction.OpCode == OpCodes.Newobj
                       && instruction.Operand is MethodReference called
                       && called.DeclaringType.FullName == "System.Action");
    Instruction queueCall = update.Body.Instructions.Single(instruction =>
        instruction.OpCode == OpCodes.Call
        && instruction.Operand is MethodReference called
        && called.DeclaringType.FullName == guards.FullName
        && called.Name == queue.Name);

    queue.Parameters[0].ParameterType = threadStart;
    workerCast.Operand = threadStart;
    workerInvoke.Operand = CreateDelegateMethod(
        module,
        threadStart,
        (MethodReference)workerInvoke.Operand);
    actionConstructor.Operand = CreateDelegateMethod(
        module,
        threadStart,
        (MethodReference)actionConstructor.Operand);
    ((MethodReference)queueCall.Operand).Parameters[0].ParameterType = threadStart;
}

static MethodReference CreateDelegateMethod(
    ModuleDefinition module,
    TypeReference delegateType,
    MethodReference source)
{
    MethodReference replacement = new MethodReference(
        source.Name,
        module.ImportReference(source.ReturnType),
        delegateType)
    {
        HasThis = source.HasThis,
        ExplicitThis = source.ExplicitThis,
        CallingConvention = source.CallingConvention,
    };
    foreach (ParameterDefinition parameter in source.Parameters)
    {
        replacement.Parameters.Add(new ParameterDefinition(
            module.ImportReference(parameter.ParameterType)));
    }

    return replacement;
}

static void RepairClr2CollectionLocks(
    IReadOnlyDictionary<string, TypeDefinition> types)
{
    (string TypeName, string MethodName, int ParameterCount)[] lockMethods =
    [
        ("Magicka.StaticList`1", "Add", 1),
        ("Magicka.StaticList`1", "Insert", 2),
        ("Magicka.StaticWeakList`1", "Add", 1),
        ("Magicka.StaticWeakList`1", "Insert", 2),
        ("Magicka.StaticWeakList`1", "Expand", 1),
    ];
    foreach ((string typeName, string methodName, int parameterCount) in lockMethods)
    {
        TypeDefinition collection = RequireType(types, typeName);
        MethodDefinition method = RequireMethod(
            collection,
            methodName,
            parameterCount);
        Instruction enterCall = method.Body.Instructions.Single(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodReference called
            && called.DeclaringType.FullName == "System.Threading.Monitor"
            && called.Name == "Enter"
            && called.Parameters.Count == 2);
        Instruction loadTakenAddress = enterCall.Previous;
        if ((loadTakenAddress.OpCode != OpCodes.Ldloca
             && loadTakenAddress.OpCode != OpCodes.Ldloca_S)
            || loadTakenAddress.Operand is not VariableDefinition taken)
        {
            throw new InvalidOperationException(
                "Expected a lock-taken local before Monitor.Enter in "
                + method.FullName);
        }

        MethodReference oldEnter = (MethodReference)enterCall.Operand;
        MethodReference clr2Enter = new MethodReference(
            oldEnter.Name,
            oldEnter.ReturnType,
            oldEnter.DeclaringType)
        {
            HasThis = false,
            CallingConvention = MethodCallingConvention.Default,
        };
        clr2Enter.Parameters.Add(new ParameterDefinition(
            oldEnter.Parameters[0].ParameterType));

        ILProcessor processor = method.Body.GetILProcessor();
        processor.Remove(loadTakenAddress);
        enterCall.Operand = clr2Enter;
        Instruction acquired = Instruction.Create(OpCodes.Ldc_I4_1);
        processor.InsertAfter(enterCall, acquired);
        processor.InsertAfter(acquired, Instruction.Create(OpCodes.Stloc, taken));
    }
}

static bool IsReuseEntryMethod(string methodName)
{
    return methodName == "Execute"
           || methodName == "Reinitialize"
           || methodName.StartsWith(
               "Cast",
               StringComparison.Ordinal)
           || methodName.StartsWith(
               "Initialize",
               StringComparison.Ordinal);
}

static int InstrumentReturnValueAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Dup),
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentSelfAtEntry(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    if (!method.HasBody || method.Body.Instructions.Count == 0)
    {
        throw new InvalidOperationException(
            "Cannot instrument empty method " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    Instruction first = method.Body.Instructions[0];
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, lifecycle));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, helper));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 3);
    return 1;
}

static int InstrumentSelfAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentFirstArgumentAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentRelatedAtEntry(
    MethodDefinition method,
    MethodReference getter,
    MethodReference helper,
    string lifecycle)
{
    if (!method.HasBody || method.Body.Instructions.Count == 0)
    {
        throw new InvalidOperationException(
            "Cannot instrument empty method " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    Instruction first = method.Body.Instructions[0];
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, getter));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, lifecycle));
    processor.InsertBefore(first, Instruction.Create(OpCodes.Call, helper));
    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 4);
    return 1;
}

static int InstrumentCheckpointAtReturns(
    MethodDefinition method,
    MethodReference helper,
    string lifecycle)
{
    return InstrumentReturns(
        method,
        ret =>
        [
            Instruction.Create(OpCodes.Ldstr, lifecycle),
            Instruction.Create(OpCodes.Call, helper),
        ]);
}

static int InstrumentSelfAfterBaseConstructor(
    MethodDefinition constructor,
    MethodReference helper,
    string lifecycle)
{
    Instruction? baseCall = constructor.Body.Instructions.FirstOrDefault(
        instruction => instruction.OpCode == OpCodes.Call
                       && instruction.Operand is MethodReference called
                       && called.Name == ".ctor"
                       && called.DeclaringType.FullName
                           != constructor.DeclaringType.FullName);
    if (baseCall is null)
    {
        throw new InvalidOperationException(
            "Base constructor call not found in " + constructor.FullName);
    }

    ILProcessor processor = constructor.Body.GetILProcessor();
    Instruction loadThis = Instruction.Create(OpCodes.Ldarg_0);
    Instruction loadLifecycle = Instruction.Create(OpCodes.Ldstr, lifecycle);
    Instruction callHelper = Instruction.Create(OpCodes.Call, helper);
    processor.InsertAfter(baseCall, loadThis);
    processor.InsertAfter(loadThis, loadLifecycle);
    processor.InsertAfter(loadLifecycle, callHelper);
    constructor.Body.MaxStackSize = Math.Max(constructor.Body.MaxStackSize, 2);
    return 1;
}

static int InstrumentReturns(
    MethodDefinition method,
    Func<Instruction, IReadOnlyList<Instruction>> createInstructions)
{
    Instruction[] returns = method.Body.Instructions
        .Where(instruction => instruction.OpCode == OpCodes.Ret)
        .ToArray();
    if (returns.Length == 0)
    {
        throw new InvalidOperationException(
            "No return instruction found in " + method.FullName);
    }

    ILProcessor processor = method.Body.GetILProcessor();
    foreach (Instruction originalReturn in returns)
    {
        IReadOnlyList<Instruction> injected = createInstructions(originalReturn);
        if (injected.Count == 0)
        {
            continue;
        }

        originalReturn.OpCode = injected[0].OpCode;
        originalReturn.Operand = injected[0].Operand;
        Instruction previous = originalReturn;
        for (int index = 1; index < injected.Count; index++)
        {
            processor.InsertAfter(previous, injected[index]);
            previous = injected[index];
        }

        Instruction replacementReturn = Instruction.Create(OpCodes.Ret);
        processor.InsertAfter(previous, replacementReturn);
    }

    method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 4);
    return returns.Length;
}

static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
{
    return module.Types.SelectMany(Flatten);
}

static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;
    foreach (TypeDefinition nested in type.NestedTypes)
    {
        foreach (TypeDefinition descendant in Flatten(nested))
        {
            yield return descendant;
        }
    }
}

static HelperMethods LoadHelperMethods(AssemblyDefinition diagnostics)
{
    TypeDefinition registry = diagnostics.MainModule.GetType(RegistryTypeName)
        ?? throw new InvalidOperationException(
            "Runtime registry type not found: " + RegistryTypeName);
    return new HelperMethods(
        RequireRuntimeMethod(registry, "BeginEpoch"),
        RequireRuntimeMethod(registry, "Register"),
        RequireRuntimeMethod(registry, "MarkActive"),
        RequireRuntimeMethod(registry, "MarkResidentActive"),
        RequireRuntimeMethod(registry, "MarkDeactivated"),
        RequireRuntimeMethod(registry, "MarkMustCollect"),
        RequireRuntimeMethod(registry, "MarkMustDetach"),
        RequireRuntimeMethod(registry, "Checkpoint"));
}

static MethodDefinition RequireRuntimeMethod(
    TypeDefinition registry,
    string name)
{
    MethodDefinition[] methods = registry.Methods.Where(
            method => method.Name == name)
        .ToArray();
    if (methods.Length != 1)
    {
        throw new InvalidOperationException(
            $"Expected one runtime method {name}, found {methods.Length}.");
    }

    return methods[0];
}

sealed class BodyReferencePool
{
    private readonly Dictionary<string, TypeReference> types =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, FieldReference> fields =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, MethodReference> methods =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, CallSite> callSites =
        new(StringComparer.Ordinal);

    public BodyReferencePool(ModuleDefinition module)
    {
        foreach (TypeDefinition type in module.Types.SelectMany(FlattenTypes))
        {
            AddType(type);
            foreach (FieldDefinition field in type.Fields)
            {
                AddField(field);
            }
            foreach (MethodDefinition method in type.Methods)
            {
                AddMethod(method);
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (VariableDefinition variable in method.Body.Variables)
                {
                    AddType(variable.VariableType);
                }
                foreach (ExceptionHandler handler in
                         method.Body.ExceptionHandlers)
                {
                    if (handler.CatchType is not null)
                    {
                        AddType(handler.CatchType);
                    }
                }
                foreach (Instruction instruction in method.Body.Instructions)
                {
                    AddOperand(instruction.Operand);
                }
            }
        }

        foreach (TypeReference type in module.GetTypeReferences())
        {
            AddType(type);
        }
        foreach (MemberReference member in module.GetMemberReferences())
        {
            AddOperand(member);
        }
    }

    public TypeReference RequireType(TypeReference source)
    {
        return types.TryGetValue(source.FullName, out TypeReference? target)
            ? target
            : throw MissingReference("type", source.FullName);
    }

    public FieldReference RequireField(FieldReference source)
    {
        return fields.TryGetValue(source.FullName, out FieldReference? target)
            ? target
            : throw MissingReference("field", source.FullName);
    }

    public MethodReference RequireMethod(MethodReference source)
    {
        string key = MethodKey(source);
        return methods.TryGetValue(key, out MethodReference? target)
            ? target
            : throw MissingReference("method", key);
    }

    public CallSite RequireCallSite(CallSite source)
    {
        string key = CallSiteKey(source);
        return callSites.TryGetValue(key, out CallSite? target)
            ? target
            : throw MissingReference("call site", key);
    }

    private void AddOperand(object? operand)
    {
        switch (operand)
        {
            case MethodReference method:
                AddMethod(method);
                break;
            case FieldReference field:
                AddField(field);
                break;
            case TypeReference type:
                AddType(type);
                break;
            case CallSite callSite:
                callSites.TryAdd(CallSiteKey(callSite), callSite);
                break;
        }
    }

    private static IEnumerable<TypeDefinition> FlattenTypes(
        TypeDefinition type)
    {
        yield return type;
        foreach (TypeDefinition nested in type.NestedTypes)
        {
            foreach (TypeDefinition descendant in FlattenTypes(nested))
            {
                yield return descendant;
            }
        }
    }

    private void AddType(TypeReference type)
    {
        types.TryAdd(type.FullName, type);
    }

    private void AddField(FieldReference field)
    {
        fields.TryAdd(field.FullName, field);
    }

    private void AddMethod(MethodReference method)
    {
        methods.TryAdd(MethodKey(method), method);
    }

    private static string MethodKey(MethodReference method)
    {
        return method.FullName
               + "|this=" + method.HasThis
               + "|explicit=" + method.ExplicitThis
               + "|call=" + method.CallingConvention
               + "|generic=" + method.GenericParameters.Count;
    }

    private static string CallSiteKey(CallSite callSite)
    {
        return callSite.ReturnType.FullName
               + " (" + string.Join(",", callSite.Parameters.Select(
                   parameter => parameter.ParameterType.FullName)) + ")"
               + "|this=" + callSite.HasThis
               + "|explicit=" + callSite.ExplicitThis
               + "|call=" + callSite.CallingConvention;
    }

    private static InvalidOperationException MissingReference(
        string kind,
        string identity)
    {
        return new InvalidOperationException(
            "No existing target " + kind + " reference matches " + identity);
    }
}

sealed record HelperMethods(
    MethodReference BeginEpoch,
    MethodReference Register,
    MethodReference MarkActive,
    MethodReference MarkResidentActive,
    MethodReference MarkDeactivated,
    MethodReference MarkMustCollect,
    MethodReference MarkMustDetach,
    MethodReference Checkpoint)
{
    public HelperMethods ImportInto(ModuleDefinition module)
    {
        return new HelperMethods(
            module.ImportReference(BeginEpoch),
            module.ImportReference(Register),
            module.ImportReference(MarkActive),
            module.ImportReference(MarkResidentActive),
            module.ImportReference(MarkDeactivated),
            module.ImportReference(MarkMustCollect),
            module.ImportReference(MarkMustDetach),
            module.ImportReference(Checkpoint));
    }
}

sealed record PatchReport(
    string Assembly,
    int Registrations,
    int ActiveHooks,
    int ResidentActiveHooks,
    int CollectHooks,
    int DeactivatedHooks,
    int DetachHooks,
    int CheckpointHooks)
{
    public override string ToString()
    {
        return $"{Assembly}: register={Registrations}, active={ActiveHooks},"
               + $" resident-active={ResidentActiveHooks},"
               + $" collect={CollectHooks}, deactivated={DeactivatedHooks},"
               + $" detach={DetachHooks},"
               + $" checkpoint={CheckpointHooks}";
    }
}
