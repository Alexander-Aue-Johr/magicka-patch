import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter/scheduler.dart';
import 'package:flutter/services.dart';

import 'localization.dart';
import 'original_game_files.dart';

String? _assetPackage;
const String _buildDefaultLocale = String.fromEnvironment('APP_LOCALE');

String _assetKey(String path) =>
    _assetPackage == null ? path : 'packages/$_assetPackage/$path';

Future<void> _openExternalUrl(String url) async {
  await Process.run('cmd', <String>['/c', 'start', '', url]);
}

void main(List<String> args) {
  runMagickaPatchApp(args);
}

void runMagickaPatchApp(List<String> args,
    {bool forceUpdater = false,
    bool forceUninstaller = false,
    String? assetPackage}) {
  _assetPackage = assetPackage;
  final updaterCommand = UpdaterCommand.parse(args);
  final effectiveForceUninstaller =
      forceUninstaller || _isUninstallRequest(args);
  final localeSelection = resolveAppLocaleSelection(
      args, ui.PlatformDispatcher.instance.locale, _buildDefaultLocale);
  final startupSurface = effectiveForceUninstaller
      ? 'uninstaller'
      : (forceUpdater || updaterCommand != null)
          ? 'updater'
          : 'installer';
  unawaited(_sendInstallerStartupTelemetry(
    localeSelection: localeSelection,
    startupSurface: startupSurface,
  ));
  runApp(MagickaPatchApp(
      updaterCommand: updaterCommand,
      forceUpdater: forceUpdater,
      forceUninstaller: effectiveForceUninstaller,
      localeSelection: localeSelection));
}

class MagickaPatchApp extends StatelessWidget {
  const MagickaPatchApp(
      {super.key,
      this.updaterCommand,
      this.forceUpdater = false,
      this.forceUninstaller = false,
      this.localeSelection});

  final UpdaterCommand? updaterCommand;
  final bool forceUpdater;
  final bool forceUninstaller;
  final AppLocaleSelection? localeSelection;

  @override
  Widget build(BuildContext context) {
    final effectiveLocaleSelection = localeSelection ??
        resolveAppLocaleSelection(const <String>[],
            ui.PlatformDispatcher.instance.locale, _buildDefaultLocale);
    return MaterialApp(
      debugShowCheckedModeBanner: false,
      title: 'Magicka Community Patch',
      locale: effectiveLocaleSelection.language.locale,
      supportedLocales: AppLanguage.supportedLocales,
      localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
        AppStrings.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      theme: ThemeData.dark(useMaterial3: true),
      home: forceUninstaller
          ? const UninstallerScreen()
          : (forceUpdater || updaterCommand != null)
              ? AutoUpdaterScreen(command: updaterCommand)
              : const InstallerScreen(),
    );
  }
}

bool _isUninstallRequest(List<String> args) {
  if (_indexOfArg(args, '--uninstall') >= 0) return true;
  final executableParts = Platform.resolvedExecutable
      .split(RegExp(r'[\\/]+'))
      .where((part) => part.isNotEmpty)
      .toList();
  if (executableParts.isEmpty) return false;
  final executableName = executableParts.last.toLowerCase();
  return executableName.contains('uninstaller');
}

enum UpdaterCommandKind { applyUpdate, offerPendingUpdate }

class UpdaterCommand {
  const UpdaterCommand({
    required this.kind,
    required this.source,
    required this.gameDir,
    required this.version,
    required this.waitPid,
  });

  final UpdaterCommandKind kind;
  final String source;
  final String gameDir;
  final String version;
  final int waitPid;

  static UpdaterCommand? parse(List<String> args) {
    final apply = _indexOfArg(args, '--apply-update');
    if (apply >= 0) {
      return UpdaterCommand(
        kind: UpdaterCommandKind.applyUpdate,
        source: _arg(args, apply + 1),
        gameDir: _arg(args, apply + 2),
        version: _arg(args, apply + 3),
        waitPid: int.tryParse(_option(args, '--wait-pid')) ?? 0,
      );
    }

    final offer = _indexOfArg(args, '--offer-pending-update');
    if (offer >= 0) {
      return UpdaterCommand(
        kind: UpdaterCommandKind.offerPendingUpdate,
        gameDir: _arg(args, offer + 1),
        version: _arg(args, offer + 2),
        source: _arg(args, offer + 3),
        waitPid: int.tryParse(_option(args, '--wait-pid')) ?? 0,
      );
    }
    return null;
  }
}

int _indexOfArg(List<String> args, String key) {
  for (var i = 0; i < args.length; i++) {
    if (args[i].toLowerCase() == key.toLowerCase()) return i;
  }
  return -1;
}

String _arg(List<String> args, int index) =>
    index >= 0 && index < args.length ? args[index] : '';

String _option(List<String> args, String key) {
  final index = _indexOfArg(args, key);
  return index >= 0 ? _arg(args, index + 1) : '';
}

class AppConstants {
  static const patchVersion = '0.0.44';
  static const settingsDirectoryName = 'CommunityPatch';
  static const settingsFileName = 'patch-settings.ini';
  static const manifestFileName = 'install-manifest.ini';
  static const eventLogFileName = 'event-log.jsonl';
  static const installerFileName = 'MagickaPatchInstaller.exe';
  static const toolFileName = 'MagickaPatchTool.exe';
  static const uninstallerFileName = 'MagickaPatchUninstaller.exe';
  static const uninstallerCommandFileName = 'uninstall_magicka_patch.cmd';
  static const magickaSteamAppId = '42910';
  static const magickaSteamValidationUrl =
      'steam://validate/$magickaSteamAppId';
  static const patreonUrl =
      'https://www.patreon.com/c/alexander_aue_johr/membership';
  static const bitesquidModLoaderUrl =
      'https://steamcommunity.com/sharedfiles/filedetails/?id=3733900153';
  static const postHogApiKey =
      'phc_vbVuHJdtwsf2gzBY36KcLo8btGZY4D6foFGqtxbkfog8';
  static const postHogEndpoint = 'https://eu.i.posthog.com/capture/';
  static const telemetryEventInstallerStarted =
      'magicka_patch_installer_started';
  static const telemetryEventInstallerLanguageResolved =
      'magicka_patch_installer_language_resolved';
  static const telemetryEventInstalled = 'magicka_patch_installed';
  static const telemetryEventAutoUpdate = 'magicka_patch_auto_update';
  static const telemetryEventDirectXAlreadyInstalled =
      'magicka_patch_directx_already_installed';
  static const telemetryEventDirectXSetupMissing =
      'magicka_patch_directx_setup_missing';
  static const telemetryEventDirectXInstallPromptShown =
      'magicka_patch_directx_install_prompt_shown';
  static const telemetryEventDirectXInstallIgnored =
      'magicka_patch_directx_install_ignored';
  static const telemetryEventDirectXInstallStarted =
      'magicka_patch_directx_install_started';
  static const telemetryEventDirectXInstallSucceeded =
      'magicka_patch_directx_install_succeeded';
  static const telemetryEventDirectXInstallFailed =
      'magicka_patch_directx_install_failed';
}

const double _patreonTurbulence = 0.78;
const String _patreonSupporterDescription = 'Patreon supporter';

class SpecialThanksPerson {
  const SpecialThanksPerson({
    required this.name,
    required this.description,
    required this.accent,
    this.avatarAsset,
    this.featureAsset,
    this.featureUrl,
    this.supporter = false,
    this.prioritySupporter = false,
  });

  final String name;
  final String description;
  final Color accent;
  final String? avatarAsset;
  final String? featureAsset;
  final String? featureUrl;
  final bool supporter;
  final bool prioritySupporter;
}

const List<SpecialThanksPerson> _specialThanksPeople = <SpecialThanksPerson>[
  SpecialThanksPerson(
    name: 'SonofKalas',
    description: 'Fix Requests & Priorities Patreon supporter',
    accent: Color(0xffd99cff),
    supporter: true,
    prioritySupporter: true,
  ),
  SpecialThanksPerson(
    name: 'Aggregating-Sky8697 / pjl234678',
    description:
        'Started the original Magicka patch effort and helped get the project rolling.',
    avatarAsset: 'assets/pjl234678.png',
    accent: Color(0xffc4a15e),
  ),
  SpecialThanksPerson(
    name: 'Skappnil',
    description:
        'Helpful feedback and bug hints found during development of the Bitesquid Mod Loader.',
    avatarAsset: 'assets/Skappnil.png',
    featureAsset: 'assets/bitesquid-mod-loader.jpg',
    featureUrl: AppConstants.bitesquidModLoaderUrl,
    accent: Color(0xffab4fff),
  ),
  SpecialThanksPerson(
    name: 'PurpleHeartE54',
    description: 'Intensive playtesting',
    avatarAsset: 'assets/PurpleHeartE54.png',
    accent: Color(0xff80caff),
    supporter: true,
  ),
  SpecialThanksPerson(
    name: '莎德娜丝（Sadness）',
    description: 'Extensive bug reports, playtesting & screen sharing',
    avatarAsset: 'assets/Sadness.jpg',
    accent: Color(0xff8fc8e8),
  ),
  SpecialThanksPerson(
    name: 'Torsten Caninenberg',
    description: _patreonSupporterDescription,
    accent: Color(0xffffc86b),
    supporter: true,
  ),
  SpecialThanksPerson(
    name: 'Tonno7',
    description: _patreonSupporterDescription,
    accent: Color(0xffffc86b),
    supporter: true,
  ),
];

class InstallerScreen extends StatefulWidget {
  const InstallerScreen({super.key, this.detectGameOnStart = true});

  final bool detectGameOnStart;

  @override
  State<InstallerScreen> createState() => _InstallerScreenState();
}

class _InstallerScreenState extends State<InstallerScreen>
    with TickerProviderStateMixin {
  final TextEditingController _pathController = TextEditingController();
  late final AnimationController _pulse;
  ui.FragmentProgram? _flameProgram;
  ui.FragmentProgram? _starProgram;
  ui.FragmentProgram? _patreonHeartFlameProgram;
  ui.FragmentProgram? _patreonSparkProgram;
  StarTuningConfig _starTuning = StarTuningConfig.defaults();
  PatreonFireConfig _patreonTuning = PatreonFireConfig.defaults();
  bool _showStarTuningPanel = false;
  bool _previewStarHover = false;
  bool _previewPatreonHover = false;
  bool _usageSharing = true;
  bool _crashReports = true;
  bool _checkForUpdates = true;
  bool _patchAlreadyInstalled = false;
  bool _statusInitialized = false;
  String _patchInstallCheckDir = '';
  String _status = '';

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
        vsync: this, duration: const Duration(milliseconds: 3200))
      ..repeat();
    _pathController.addListener(_handlePathChanged);
    _loadShader();
    if (widget.detectGameOnStart) _detectQuick();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_statusInitialized) return;
    _status = AppStrings.of(context).t('ready');
    _statusInitialized = true;
  }

  Future<void> _loadShader() async {
    try {
      final flame = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/flame_button.frag'));
      if (mounted) setState(() => _flameProgram = flame);
    } catch (_) {}

    try {
      final stars = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/install_star_button.frag'));
      if (mounted) setState(() => _starProgram = stars);
    } catch (_) {}

    try {
      final patreonFlame = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/patreon_heart_flame.frag'));
      if (mounted) setState(() => _patreonHeartFlameProgram = patreonFlame);
    } catch (_) {}

    try {
      final patreonSparks = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/patreon_fire_sparks.frag'));
      if (mounted) setState(() => _patreonSparkProgram = patreonSparks);
    } catch (_) {}
  }

  @override
  void dispose() {
    _pathController.removeListener(_handlePathChanged);
    _pathController.dispose();
    _pulse.dispose();
    super.dispose();
  }

  void _handlePathChanged() {
    if (!_patchAlreadyInstalled) return;
    if (_pathController.text.trim() == _patchInstallCheckDir) return;
    setState(() {
      _patchAlreadyInstalled = false;
      _patchInstallCheckDir = '';
    });
  }

  Future<void> _detectQuick() async {
    final found = await _findMagickaDirectory();
    if (!mounted || found == null) return;
    await _useDetectedGameDirectory(found);
  }

  Future<void> _browse() async {
    final s = AppStrings.of(context);
    final result = await Process.run('powershell', <String>[
      '-Sta',
      '-NoProfile',
      '-Command',
      'Add-Type -AssemblyName System.Windows.Forms; \$d=New-Object System.Windows.Forms.FolderBrowserDialog; \$d.Description=${_psQuote(s.t('gameFolder'))}; if(\$d.ShowDialog() -eq "OK"){ \$d.SelectedPath }',
    ]);
    final path = result.stdout.toString().trim();
    if (path.isNotEmpty) {
      await _useDetectedGameDirectory(path);
    }
  }

  Future<void> _discover() async {
    setState(
        () => _status = AppStrings.of(context).t('searchingSteamLibraries'));
    final found = await _findMagickaDirectory(deep: true);
    if (!mounted) return;
    final s = AppStrings.of(context);
    if (found == null) {
      setState(() {
        _patchAlreadyInstalled = false;
        _patchInstallCheckDir = '';
        _status = s.t('invalidMagickaFolder');
      });
      return;
    }
    await _useDetectedGameDirectory(found);
  }

  Future<void> _useDetectedGameDirectory(String gameDir) async {
    final valid = _isValidMagickaDirectory(gameDir);
    var installed = false;
    if (valid) {
      installed = await _magickaExeContainsPatchVersion(
          gameDir, AppConstants.patchVersion);
    }
    if (!mounted) return;
    _patchInstallCheckDir = gameDir;
    _pathController.text = gameDir;
    setState(() {
      _patchAlreadyInstalled = installed;
      if (!installed) _patchInstallCheckDir = '';
      _status = installed
          ? AppStrings.of(context)
              .patchAlreadyInstalled(AppConstants.patchVersion)
          : AppStrings.of(context).detectedFolder(gameDir);
    });
  }

  Future<void> _install() async {
    final s = AppStrings.of(context);
    final gameDir = _pathController.text.trim();
    if (!_isValidMagickaDirectory(gameDir)) {
      _showMessage(s.t('invalidMagickaFolder'));
      return;
    }

    try {
      if (await _magickaExeContainsPatchVersion(
          gameDir, AppConstants.patchVersion)) {
        if (mounted) {
          setState(() {
            _patchAlreadyInstalled = true;
            _patchInstallCheckDir = gameDir;
            _status = s.patchAlreadyInstalled(AppConstants.patchVersion);
          });
        }
        await _startGameFromInstaller(gameDir);
        return;
      }

      final communityDir =
          Directory(_join(gameDir, AppConstants.settingsDirectoryName));
      await communityDir.create(recursive: true);
      final backupDir = Directory(_join(communityDir.path, 'backup'));
      await backupDir.create(recursive: true);

      final existingManifest = await _readIniFile(
          _join(communityDir.path, AppConstants.manifestFileName));
      final originalBackups = await _ensureVerifiedOriginalBackups(
        context,
        gameDirectory: gameDir,
        backupDirectory: backupDir.path,
        manifest: existingManifest,
      );
      if (originalBackups == null) {
        if (mounted) {
          setState(() => _status = s.t('originalFileRecoveryCancelled'));
        }
        return;
      }
      await _writePayload(gameDir, 'Magicka.exe');
      await _writePayload(gameDir, 'PolygonHead.dll');
      await _writeSettings(gameDir);
      await _writeManifest(
          gameDir, originalBackups.magicka!, originalBackups.polygonHead!);
      await _installTools(gameDir);
      await _sendPatchTelemetryEvent(
        eventName: AppConstants.telemetryEventInstalled,
        gameDir: gameDir,
        patchVersion: AppConstants.patchVersion,
      );

      setState(() => _status = s.t('thePatchWasInstalled'));
      await _showStartGameDialog(
        context,
        gameDir,
        s.t('thePatchWasInstalled'),
        flameProgram: _flameProgram,
        starProgram: _starProgram,
      );
    } catch (error) {
      setState(() => _status = s.installFailed(error));
      _showMessage(s.installFailed(error));
    }
  }

  Future<void> _startInstalledGame() async {
    final s = AppStrings.of(context);
    final gameDir = _pathController.text.trim();
    if (!_isValidMagickaDirectory(gameDir)) {
      _showMessage(s.t('invalidMagickaFolder'));
      return;
    }
    await _startGameFromInstaller(gameDir);
  }

  Future<void> _startGameFromInstaller(String gameDir) async {
    try {
      final started = await _startMagickaWithPrerequisites(context, gameDir);

      if (!started) {
        if (mounted) {
          setState(
              () => _status = AppStrings.of(context).t('magickaWasNotStarted'));
        }
        return;
      }

      if (mounted) {
        setState(() => _status = AppStrings.of(context).t('magickaWasStarted'));
      }
    } catch (error) {
      if (!mounted) return;
      final s = AppStrings.of(context);
      setState(() => _status = s.couldNotStartMagicka(error));
      await _showMessage(s.couldNotStartMagicka(error));
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final showStarTuningPanel = kDebugMode && _showStarTuningPanel;
    return Scaffold(
      backgroundColor: const Color(0xff040608),
      body: SizedBox.expand(
        child: FittedBox(
          fit: BoxFit.contain,
          child: SizedBox(
            width: showStarTuningPanel ? 1662 : 1220,
            height: 720,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                SizedBox(
                  width: 1220,
                  height: 720,
                  child: AnimatedBuilder(
                    animation: _pulse,
                    builder: (context, _) => Stack(
                      children: <Widget>[
                        const ArcaneBackdrop(),
                        const Positioned(
                            left: 0,
                            top: 0,
                            width: 226,
                            height: 720,
                            child: SidebarImage()),
                        Positioned(left: 250, top: 29, child: _Header()),
                        Positioned(
                            left: 250,
                            top: 94,
                            width: 946,
                            height: 18,
                            child: Row(children: <Widget>[
                              Icon(
                                  _patchAlreadyInstalled
                                      ? Icons.play_arrow_rounded
                                      : Icons.info_outline_rounded,
                                  color: const Color(0xffbeb19b),
                                  size: 15),
                              const SizedBox(width: 7),
                              Expanded(
                                  child: FittedBox(
                                fit: BoxFit.scaleDown,
                                alignment: Alignment.centerLeft,
                                child: Text(_status,
                                    key:
                                        const ValueKey('installer-status-text'),
                                    maxLines: 1,
                                    softWrap: false,
                                    style: const TextStyle(
                                        color: Color(0xffbeb19b),
                                        fontSize: 13)),
                              ))
                            ])),
                        Positioned(
                            left: 250,
                            top: 118,
                            width: 735,
                            height: 73,
                            child: _FolderPanel(controller: _pathController)),
                        Positioned(
                            left: 1001,
                            top: 118,
                            width: 195,
                            height: 34,
                            child: FlameButton(
                                program: null,
                                label: s.t('browse'),
                                icon: Icons.folder,
                                accent: const Color(0xffd9a04f),
                                overlayIcon: true,
                                effects: false,
                                onTap: _browse)),
                        Positioned(
                            left: 1001,
                            top: 157,
                            width: 195,
                            height: 34,
                            child: FlameButton(
                                program: null,
                                label: s.t('findAutomatically'),
                                icon: Icons.search,
                                accent: const Color(0xff3f9fff),
                                overlayIcon: true,
                                effects: false,
                                onTap: _discover)),
                        Positioned(
                            left: 250,
                            top: 210,
                            width: 946,
                            height: 236,
                            child: _TelemetryPanel(
                              usageSharing: _usageSharing,
                              crashReports: _crashReports,
                              autoUpdate: _checkForUpdates,
                              onUsageChanged: (value) =>
                                  setState(() => _usageSharing = value),
                              onCrashChanged: (value) =>
                                  setState(() => _crashReports = value),
                              onAutoUpdateChanged: (value) =>
                                  setState(() => _checkForUpdates = value),
                            )),
                        Positioned(
                            left: 250,
                            top: 470,
                            width: 946,
                            height: 150,
                            child:
                                SpecialThanksBanner(starProgram: _starProgram)),
                        Positioned(
                            left: 66,
                            top: 654,
                            width: 286,
                            height: 42,
                            child: FlameButton(
                                program: _starProgram,
                                label: _patchAlreadyInstalled
                                    ? s.t('startGame')
                                    : s.t('installPatch'),
                                icon: _patchAlreadyInstalled
                                    ? Icons.play_arrow_rounded
                                    : Icons.auto_awesome,
                                accent: const Color(0xff3f9fff),
                                starField: true,
                                overlayIcon: true,
                                starTuning: _starTuning,
                                forceHover: _previewStarHover,
                                intensity: 1.0,
                                onTap: _patchAlreadyInstalled
                                    ? _startInstalledGame
                                    : _install)),
                        Positioned(
                            left: 372,
                            top: 654,
                            width: 246,
                            height: 42,
                            child: FlameButton(
                                program: _flameProgram,
                                label: s.t('sendFeedback'),
                                icon: Icons.chat_bubble,
                                accent: const Color(0xffd9a04f),
                                overlayIcon: true,
                                onTap: _showFeedbackDialog)),
                        Positioned(
                            left: 638,
                            top: 654,
                            width: 292,
                            height: 42,
                            child: FlameButton(
                                program: _flameProgram,
                                label: s.t('supportOnPatreon'),
                                icon: Icons.favorite,
                                accent: const Color(0xffff5d2d),
                                patreon: true,
                                patreonFire: true,
                                patreonTuning: _patreonTuning,
                                patreonHeartFlameProgram:
                                    _patreonHeartFlameProgram,
                                patreonSparkProgram: _patreonSparkProgram,
                                forceHover: _previewPatreonHover,
                                onTap: _openPatreon)),
                        Positioned(
                            left: 950,
                            top: 654,
                            width: 246,
                            height: 42,
                            child: FlameButton(
                                program: _flameProgram,
                                label: s.t('cancel'),
                                icon: Icons.close,
                                accent: const Color(0xffd03f30),
                                overlayIcon: true,
                                onTap: () => exit(0))),
                        if (kDebugMode)
                          Positioned(
                              left: 1032,
                              top: 20,
                              width: 156,
                              height: 32,
                              child: _DebugTunerButton(
                                  open: showStarTuningPanel,
                                  onPressed: _openStarTuning)),
                        const Positioned.fill(
                            child: IgnorePointer(child: OrnateFrame())),
                      ],
                    ),
                  ),
                ),
                if (showStarTuningPanel)
                  SizedBox(
                    width: 442,
                    height: 720,
                    child: Padding(
                      padding: const EdgeInsets.only(left: 12),
                      child: VfxTuningPanel(
                        initialStarConfig: _starTuning,
                        initialPatreonConfig: _patreonTuning,
                        onStarChanged: (config) =>
                            setState(() => _starTuning = config),
                        onPatreonChanged: (config) =>
                            setState(() => _patreonTuning = config),
                        onPreviewChanged: (target, hover) => setState(() {
                          _previewStarHover =
                              target == VfxTuningTarget.installStars && hover;
                          _previewPatreonHover =
                              target == VfxTuningTarget.patreonFire && hover;
                        }),
                        onClose: () => setState(() {
                          _showStarTuningPanel = false;
                          _previewStarHover = false;
                          _previewPatreonHover = false;
                        }),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _showMessage(String message) async {
    if (!mounted) return;
    final s = AppStrings.of(context);
    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: const Color(0xff101315),
        title: Text(s.t('appTitle')),
        content: Text(message),
        actions: <Widget>[
          TextButton(
              onPressed: () => Navigator.pop(context), child: Text(s.t('ok')))
        ],
      ),
    );
  }

  Future<void> _showFeedbackDialog() async {
    final s = AppStrings.of(context);
    final nameController = TextEditingController();
    final subjectController = TextEditingController();
    final messageController = TextEditingController();
    try {
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          backgroundColor: const Color(0xff101315),
          title: Text(s.t('feedbackTitle')),
          content: SizedBox(
            width: 520,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                TextField(
                  controller: nameController,
                  decoration: InputDecoration(labelText: s.t('feedbackName')),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: subjectController,
                  decoration:
                      InputDecoration(labelText: s.t('feedbackSubject')),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: messageController,
                  maxLines: 7,
                  decoration:
                      InputDecoration(labelText: s.t('feedbackMessage')),
                ),
              ],
            ),
          ),
          actions: <Widget>[
            TextButton(
                onPressed: () => Navigator.pop(context),
                child: Text(s.t('cancel'))),
            FilledButton.icon(
              onPressed: () async {
                final sent = await _sendFeedback(
                  contextName: 'manual',
                  name: nameController.text,
                  subject: subjectController.text,
                  message: messageController.text,
                );
                if (!mounted) return;
                Navigator.pop(context);
                setState(() => _status =
                    sent ? s.t('feedbackSent') : s.t('feedbackNotSent'));
                await _showMessage(
                    sent ? s.t('feedbackThankYou') : s.t('feedbackFailed'));
              },
              icon: const Icon(Icons.send, size: 16),
              label: Text(s.t('feedbackSend')),
            ),
          ],
        ),
      );
    } finally {
      nameController.dispose();
      subjectController.dispose();
      messageController.dispose();
    }
  }

  Future<bool> _sendFeedback({
    required String contextName,
    required String name,
    required String subject,
    required String message,
  }) async {
    final properties = <String, Object>{
      'distinct_id': await _feedbackDistinctId(),
      r'$process_person_profile': false,
      'context': _safeTelemetryText(contextName, 100),
      'feedback': _safeTelemetryText(message, 4000),
      'source': 'flutter_installer',
      'patch_name': 'Community Patch',
      'patch_version': AppConstants.patchVersion,
      'os': _safeTelemetryText(Platform.operatingSystemVersion, 200),
    };
    _addOptionalTelemetry(properties, 'name', name, 200);
    _addOptionalTelemetry(properties, 'subject', subject, 300);

    final payload = <String, Object>{
      'api_key': AppConstants.postHogApiKey,
      'event': 'magicka_patch_feedback_${_normalizeEventPart(contextName)}',
      'properties': properties,
    };

    final client = HttpClient()
      ..connectionTimeout = const Duration(milliseconds: 2200);
    try {
      final request =
          await client.postUrl(Uri.parse(AppConstants.postHogEndpoint));
      request.headers.contentType = ContentType.json;
      request.headers.set(HttpHeaders.userAgentHeader,
          'MagickaPatchInstaller/${AppConstants.patchVersion}');
      request.write(jsonEncode(payload));
      final response =
          await request.close().timeout(const Duration(milliseconds: 4200));
      await response.drain();
      return response.statusCode >= 200 && response.statusCode < 300;
    } catch (_) {
      return false;
    } finally {
      client.close(force: true);
    }
  }

  Future<String> _feedbackDistinctId() async {
    final gameDir = _pathController.text.trim();
    final baseDir = _isValidMagickaDirectory(gameDir)
        ? _join(gameDir, AppConstants.settingsDirectoryName)
        : _join(Platform.environment['APPDATA'] ?? Directory.systemTemp.path,
            'MagickaPatch');
    final idFile = File(_join(baseDir, 'anonymous-id.txt'));
    try {
      await Directory(baseDir).create(recursive: true);
      if (await idFile.exists()) {
        final existing = (await idFile.readAsString()).trim();
        if (existing.length >= 16) return existing;
      }
      final id = _newTelemetryId();
      await idFile.writeAsString(id, flush: true);
      return id;
    } catch (_) {
      return 'ephemeral_${_newTelemetryId()}';
    }
  }

  Future<void> _openPatreon() async {
    await Process.run(
        'cmd', <String>['/c', 'start', '', AppConstants.patreonUrl]);
  }

  void _openStarTuning() {
    if (!kDebugMode) return;
    setState(() {
      _showStarTuningPanel = !_showStarTuningPanel;
      _previewStarHover = false;
      _previewPatreonHover = false;
    });
  }

  Future<String?> _findMagickaDirectory({bool deep = false}) async {
    final steamFound = await _findMagickaDirectoryFromSteam();
    if (steamFound != null) return steamFound;

    if (!Platform.isWindows) return null;

    final candidates = <String>[
      _join(
          Platform.environment['ProgramFiles(x86)'] ??
              r'C:\Program Files (x86)',
          r'Steam\steamapps\common\Magicka'),
      _join(Platform.environment['ProgramFiles'] ?? r'C:\Program Files',
          r'Steam\steamapps\common\Magicka'),
      r'C:\Steam\steamapps\common\Magicka',
      r'D:\SteamLibrary\steamapps\common\Magicka',
      r'G:\SteamLibrary\steamapps\common\Magicka',
    ];
    for (final candidate in candidates) {
      if (_isValidMagickaDirectory(candidate)) return candidate;
    }
    if (!deep) return null;
    for (final drive in <String>['C:', 'D:', 'E:', 'F:', 'G:', 'H:']) {
      for (final root in <String>['SteamLibrary', 'Steam']) {
        final candidate = _join('$drive\\$root', r'steamapps\common\Magicka');
        if (_isValidMagickaDirectory(candidate)) return candidate;
      }
    }
    return null;
  }

  Future<String?> _findMagickaDirectoryFromSteam() async {
    final steamDirs = await _findSteamDirectories();
    return findSteamAppDirectory(
      steamDirectories: steamDirs,
      appId: AppConstants.magickaSteamAppId,
      fallbackInstallDirectory: 'Magicka',
      windowsPaths: Platform.isWindows,
      isValidDirectory: _isValidMagickaDirectory,
    );
  }

  Future<List<String>> _findSteamDirectories() async {
    final dirs = <String>[];
    if (Platform.isLinux) {
      for (final candidate
          in linuxSteamDirectoryCandidates(Platform.environment)) {
        _addUniquePath(dirs, candidate);
      }
      return dirs.where((dir) => Directory(dir).existsSync()).toList();
    }
    if (!Platform.isWindows) return dirs;

    final envProgramFilesX86 =
        Platform.environment['ProgramFiles(x86)'] ?? r'C:\Program Files (x86)';
    final envProgramFiles =
        Platform.environment['ProgramFiles'] ?? r'C:\Program Files';
    _addUniquePath(dirs, _join(envProgramFilesX86, 'Steam'));
    _addUniquePath(dirs, _join(envProgramFiles, 'Steam'));
    _addUniquePath(dirs, r'C:\Steam');

    for (final registryPath in <String>[
      r'HKCU\Software\Valve\Steam',
      r'HKLM\Software\Valve\Steam',
      r'HKLM\Software\WOW6432Node\Valve\Steam',
    ]) {
      for (final valueName in <String>['SteamPath', 'InstallPath']) {
        final value = await _readRegistryString(registryPath, valueName);
        if (value != null && value.trim().isNotEmpty) {
          _addUniquePath(dirs, value.replaceAll('/', '\\'));
        }
      }
    }
    return dirs.where((dir) => Directory(dir).existsSync()).toList();
  }

  Future<String?> _readRegistryString(String key, String valueName) async {
    try {
      final result =
          await Process.run('reg', <String>['query', key, '/v', valueName]);
      if (result.exitCode != 0) return null;
      final pattern = RegExp(
          '^\\s*${RegExp.escape(valueName)}\\s+REG_\\w+\\s+(.+)\$',
          multiLine: true,
          caseSensitive: false);
      final match = pattern.firstMatch(result.stdout.toString());
      return match?.group(1)?.trim();
    } catch (_) {
      return null;
    }
  }

  bool _isValidMagickaDirectory(String path) {
    if (path.isEmpty) return false;
    return File(_join(path, 'Magicka.exe')).existsSync() &&
        File(_join(path, 'steam_api.dll')).existsSync();
  }

  Future<void> _writePayload(String gameDir, String fileName) async {
    final data = await _readPayloadBytes(fileName);
    final temp = File(_join(gameDir, '$fileName.new'));
    await temp.writeAsBytes(data, flush: true);
    await temp.copy(_join(gameDir, fileName));
    await temp.delete();
  }

  Future<List<int>> _readPayloadBytes(String fileName) async {
    try {
      final data = await rootBundle.load(_assetKey('assets/payload/$fileName'));
      return data.buffer.asUint8List(data.offsetInBytes, data.lengthInBytes);
    } catch (_) {
      final exeDir = File(Platform.resolvedExecutable).parent.path;
      final candidates = <String>[
        _join(exeDir, fileName),
        _join(exeDir, '..', fileName),
        _join(exeDir, r'..\..', fileName),
        _join(exeDir, r'data\flutter_assets\assets\payload', fileName),
        _join(exeDir, r'assets\payload', fileName),
        _join(exeDir, 'Payload', fileName),
        _join(Directory.current.path, fileName),
        _join(Directory.current.path, '..', fileName),
        _join(Directory.current.path, r'..\..', fileName),
        _join(Directory.current.path, r'assets\payload', fileName),
      ];

      for (final path in candidates) {
        final file = File(path);
        if (await file.exists()) return file.readAsBytes();
      }

      throw FileSystemException(
          'Payload file missing. Put $fileName next to the installer EXE, or under assets\\payload / Payload.',
          candidates.first);
    }
  }

  Future<void> _writeSettings(String gameDir) async {
    final path = _join(gameDir, AppConstants.settingsDirectoryName,
        AppConstants.settingsFileName);
    final existingSettings = await _readIniFile(path);
    final useMagicka1ControllerScheme =
        _parseBool(existingSettings['use_magicka_1_controller_scheme']);
    await File(path).writeAsString('''
[MagickaCommunityPatch]
version=${AppConstants.patchVersion}
usage_sharing=$_usageSharing
crash_reports=$_crashReports
check_for_updates=$_checkForUpdates
auto_update=false
use_magicka_1_controller_scheme=$useMagicka1ControllerScheme
language=${AppStrings.of(context).language.localeTag}
skipped_version=
created_utc=${DateTime.now().toUtc().toIso8601String()}
event_log=${AppConstants.settingsDirectoryName}\\${AppConstants.eventLogFileName}
''');
  }

  Future<void> _writeManifest(String gameDir, String originalMagickaBackup,
      String originalPolygonHeadBackup) async {
    final path = _join(gameDir, AppConstants.settingsDirectoryName,
        AppConstants.manifestFileName);
    await File(path).writeAsString('''
[InstallManifest]
version=${AppConstants.patchVersion}
install_utc=${DateTime.now().toUtc().toIso8601String()}
original_magicka_backup=$originalMagickaBackup
original_polygonhead_backup=$originalPolygonHeadBackup
''');
  }

  Future<void> _installTools(String gameDir) async {
    final exe = Platform.resolvedExecutable;
    await File(exe).copy(_join(gameDir, AppConstants.installerFileName));
    await File(exe).copy(_join(gameDir, AppConstants.toolFileName));
    await File(exe).copy(_join(gameDir, AppConstants.uninstallerFileName));
    await _copyFlutterRuntimeFiles(gameDir);
    await File(_join(gameDir, AppConstants.uninstallerCommandFileName))
        .writeAsString(
            '@echo off\r\n"%~dp0${AppConstants.uninstallerFileName}" --uninstall\r\n');
  }

  Future<void> _copyFlutterRuntimeFiles(String gameDir) async {
    final exeDir = File(Platform.resolvedExecutable).parent;
    final flutterDll = File(_join(exeDir.path, 'flutter_windows.dll'));
    if (await flutterDll.exists()) {
      await flutterDll.copy(_join(gameDir, 'flutter_windows.dll'));
    }

    final dataDir = Directory(_join(exeDir.path, 'data'));
    if (await dataDir.exists()) {
      await _copyDirectory(dataDir, Directory(_join(gameDir, 'data')));
    }
  }
}

class AutoUpdaterScreen extends StatefulWidget {
  const AutoUpdaterScreen({super.key, required this.command});

  final UpdaterCommand? command;

  @override
  State<AutoUpdaterScreen> createState() => _AutoUpdaterScreenState();
}

class _AutoUpdaterScreenState extends State<AutoUpdaterScreen>
    with TickerProviderStateMixin {
  late final AnimationController _pulse;
  ui.FragmentProgram? _flameProgram;
  ui.FragmentProgram? _starProgram;
  ui.FragmentProgram? _patreonHeartFlameProgram;
  ui.FragmentProgram? _patreonSparkProgram;
  final StarTuningConfig _starTuning = StarTuningConfig.defaults();
  final PatreonFireConfig _patreonTuning = PatreonFireConfig.defaults();
  bool _busy = false;
  bool _updated = false;
  bool _toolUpdateScheduled = false;
  bool _statusInitialized = false;
  String _status = '';

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
        vsync: this, duration: const Duration(milliseconds: 3200))
      ..repeat();
    _loadShader();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_statusInitialized) return;
    final s = AppStrings.of(context);
    final command = widget.command;
    if (command == null) {
      _status = s.t('noPendingUpdate');
    } else if (command.version.isNotEmpty) {
      _status = s.patchReady(command.version);
    } else {
      _status = s.t('ready');
    }
    _statusInitialized = true;
  }

  Future<void> _loadShader() async {
    try {
      final flame = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/flame_button.frag'));
      if (mounted) setState(() => _flameProgram = flame);
    } catch (_) {}

    try {
      final stars = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/install_star_button.frag'));
      if (mounted) setState(() => _starProgram = stars);
    } catch (_) {}

    try {
      final patreonFlame = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/patreon_heart_flame.frag'));
      if (mounted) setState(() => _patreonHeartFlameProgram = patreonFlame);
    } catch (_) {}

    try {
      final patreonSparks = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/patreon_fire_sparks.frag'));
      if (mounted) setState(() => _patreonSparkProgram = patreonSparks);
    } catch (_) {}
  }

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  Future<void> _updatePatch() async {
    final s = AppStrings.of(context);
    final command = widget.command;
    if (command == null) {
      await _showMessage(s.t('noPendingUpdate'));
      return;
    }
    if (_busy) return;

    setState(() {
      _busy = true;
      _status = s.t('preparingUpdate');
    });

    try {
      await _applyPreparedUpdate(command);
      await _sendPatchTelemetryEvent(
        eventName: AppConstants.telemetryEventAutoUpdate,
        gameDir: command.gameDir,
        patchVersion: _displayVersion(command),
      );
      if (!mounted) return;
      setState(() {
        _updated = true;
        _status = s.patchInstalled(_displayVersion(command));
      });
      await _showStartGameDialog(
        context,
        command.gameDir,
        s.patchInstalled(_displayVersion(command)),
        flameProgram: _flameProgram,
        starProgram: _starProgram,
      );
    } catch (error) {
      if (!mounted) return;
      setState(() => _status = s.updateFailed(error));
      await _showMessage(s.updateFailed(error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _applyPreparedUpdate(UpdaterCommand command) async {
    if (command.waitPid > 0) await _waitForProcessExit(command.waitPid);
    if (command.source.trim().isEmpty) {
      throw const FormatException('Missing update source.');
    }
    if (command.gameDir.trim().isEmpty) {
      throw const FormatException('Missing Magicka directory.');
    }

    final gameDir = Directory(command.gameDir);
    if (!gameDir.existsSync()) {
      throw FileSystemException(
          'Magicka directory does not exist.', gameDir.path);
    }

    var payloadDir = command.source;
    String? cleanupDir;
    final sourceFile = File(command.source);
    if (sourceFile.existsSync() &&
        command.source.toLowerCase().endsWith('.zip')) {
      cleanupDir = _join(Directory.systemTemp.path,
          'MagickaPatchUpdate_${DateTime.now().microsecondsSinceEpoch}');
      await Directory(cleanupDir).create(recursive: true);
      await _extractZip(command.source, cleanupDir);
      payloadDir = cleanupDir;
    }

    try {
      final magicka = await _findFile(payloadDir, 'Magicka.exe');
      final polygon = await _findFile(payloadDir, 'PolygonHead.dll');
      if (magicka == null || polygon == null) {
        throw const FormatException(
            'The update package does not contain Magicka.exe and PolygonHead.dll.');
      }

      final backupDir = Directory(_join(
          command.gameDir,
          AppConstants.settingsDirectoryName,
          'backup\\previous_patch_${_safeFileName(_displayVersion(command))}'));
      await backupDir.create(recursive: true);
      await _copyIfExists(_join(command.gameDir, 'Magicka.exe'),
          _join(backupDir.path, 'Magicka.exe'));
      await _copyIfExists(_join(command.gameDir, 'PolygonHead.dll'),
          _join(backupDir.path, 'PolygonHead.dll'));

      await File(magicka).copy(_join(command.gameDir, 'Magicka.exe'));
      await File(polygon).copy(_join(command.gameDir, 'PolygonHead.dll'));
      final toolUpdateStaged =
          await _stageOptionalToolUpdate(payloadDir, command.gameDir);
      await _updatePatchSettings(command.gameDir, _displayVersion(command));
      await _deletePendingUpdate(command.gameDir);
      if (toolUpdateStaged) await _scheduleToolUpdate(command.gameDir);
    } finally {
      if (cleanupDir != null) {
        try {
          await Directory(cleanupDir).delete(recursive: true);
        } catch (_) {}
      }
    }
  }

  Future<bool> _stageOptionalToolUpdate(
      String payloadDir, String gameDir) async {
    final tool = await _findFile(payloadDir, AppConstants.toolFileName);
    final toolDir = tool == null ? payloadDir : File(tool).parent.path;
    final installer = tool == null
        ? await _findFile(payloadDir, AppConstants.installerFileName)
        : await _findFileNear(
            toolDir, payloadDir, AppConstants.installerFileName);
    if (installer == null && tool == null) return false;

    final staging = Directory(_join(
        gameDir, AppConstants.settingsDirectoryName, 'tool-update-staging'));
    try {
      if (await staging.exists()) await staging.delete(recursive: true);
    } catch (_) {}
    await staging.create(recursive: true);

    final installerSource = File(installer ?? tool!);
    final toolSource = File(tool ?? installer!);
    await installerSource
        .copy(_join(staging.path, AppConstants.installerFileName));
    await toolSource.copy(_join(staging.path, AppConstants.toolFileName));

    final flutterDll =
        await _findFileNear(toolDir, payloadDir, 'flutter_windows.dll');
    if (flutterDll != null) {
      await File(flutterDll).copy(_join(staging.path, 'flutter_windows.dll'));
    }

    final dataDir = await _findDirectoryNear(toolDir, payloadDir, 'data');
    if (dataDir != null) {
      await _copyDirectory(
          Directory(dataDir), Directory(_join(staging.path, 'data')));
    }

    return true;
  }

  Future<void> _scheduleToolUpdate(String gameDir) async {
    if (_toolUpdateScheduled) return;
    _toolUpdateScheduled = true;

    final staging = _join(
        gameDir, AppConstants.settingsDirectoryName, 'tool-update-staging');
    final installerSource = _join(staging, AppConstants.installerFileName);
    final installerTarget = _join(gameDir, AppConstants.installerFileName);
    final toolSource = _join(staging, AppConstants.toolFileName);
    final toolTarget = _join(gameDir, AppConstants.toolFileName);
    final uninstallerTarget = _join(gameDir, AppConstants.uninstallerFileName);
    final dllSource = _join(staging, 'flutter_windows.dll');
    final dllTarget = _join(gameDir, 'flutter_windows.dll');
    final dataSource = _join(staging, 'data');
    final dataTarget = _join(gameDir, 'data');
    final currentPid = pid;

    final command = '\$pidToWait=$currentPid; '
        '\$staging=${_psQuote(staging)}; '
        '\$installerSource=${_psQuote(installerSource)}; '
        '\$installerTarget=${_psQuote(installerTarget)}; '
        '\$toolSource=${_psQuote(toolSource)}; '
        '\$toolTarget=${_psQuote(toolTarget)}; '
        '\$uninstallerTarget=${_psQuote(uninstallerTarget)}; '
        '\$dllSource=${_psQuote(dllSource)}; '
        '\$dllTarget=${_psQuote(dllTarget)}; '
        '\$dataSource=${_psQuote(dataSource)}; '
        '\$dataTarget=${_psQuote(dataTarget)}; '
        'try { Wait-Process -Id \$pidToWait -ErrorAction SilentlyContinue } catch {} '
        'Start-Sleep -Milliseconds 300; '
        'if(Test-Path -LiteralPath \$installerSource){ '
        'Copy-Item -LiteralPath \$installerSource -Destination \$installerTarget -Force -ErrorAction SilentlyContinue; '
        'Copy-Item -LiteralPath \$installerSource -Destination \$uninstallerTarget -Force -ErrorAction SilentlyContinue; '
        '} '
        'if(Test-Path -LiteralPath \$toolSource){ '
        'Copy-Item -LiteralPath \$toolSource -Destination \$toolTarget -Force -ErrorAction SilentlyContinue; '
        '} '
        'if(Test-Path -LiteralPath \$dllSource){ Copy-Item -LiteralPath \$dllSource -Destination \$dllTarget -Force -ErrorAction SilentlyContinue } '
        'if(Test-Path -LiteralPath \$dataSource){ '
        'Remove-Item -LiteralPath \$dataTarget -Recurse -Force -ErrorAction SilentlyContinue; '
        'Copy-Item -LiteralPath \$dataSource -Destination \$dataTarget -Recurse -Force -ErrorAction SilentlyContinue; '
        '} '
        'Remove-Item -LiteralPath \$staging -Recurse -Force -ErrorAction SilentlyContinue';

    try {
      await Process.start(
        'powershell',
        <String>[
          '-NoProfile',
          '-ExecutionPolicy',
          'Bypass',
          '-WindowStyle',
          'Hidden',
          '-Command',
          command,
        ],
        mode: ProcessStartMode.detached,
      );
    } catch (_) {}
  }

  Future<void> _extractZip(String zipPath, String destination) async {
    final result = await Process.run('powershell', <String>[
      '-NoProfile',
      '-ExecutionPolicy',
      'Bypass',
      '-Command',
      'Expand-Archive -LiteralPath ${_psQuote(zipPath)} -DestinationPath ${_psQuote(destination)} -Force',
    ]);
    if (result.exitCode != 0) {
      throw ProcessException(
          'powershell',
          const <String>[],
          'Could not extract update package: ${result.stderr}',
          result.exitCode);
    }
  }

  Future<String?> _findFile(String dir, String fileName) async {
    final direct = File(_join(dir, fileName));
    if (await direct.exists()) return direct.path;
    final directory = Directory(dir);
    if (!await directory.exists()) return null;
    await for (final entity
        in directory.list(recursive: true, followLinks: false)) {
      if (entity is File &&
          entity.uri.pathSegments.isNotEmpty &&
          entity.uri.pathSegments.last.toLowerCase() ==
              fileName.toLowerCase()) {
        return entity.path;
      }
    }
    return null;
  }

  Future<String?> _findFileNear(
      String preferredDir, String fallbackDir, String fileName) async {
    final preferred = File(_join(preferredDir, fileName));
    if (await preferred.exists()) return preferred.path;
    return _findFile(fallbackDir, fileName);
  }

  Future<String?> _findDirectoryNear(
      String preferredDir, String fallbackDir, String directoryName) async {
    final preferred = Directory(_join(preferredDir, directoryName));
    if (await preferred.exists()) return preferred.path;
    return _findDirectory(fallbackDir, directoryName);
  }

  Future<String?> _findDirectory(String dir, String directoryName) async {
    final direct = Directory(_join(dir, directoryName));
    if (await direct.exists()) return direct.path;
    final directory = Directory(dir);
    if (!await directory.exists()) return null;
    await for (final entity
        in directory.list(recursive: true, followLinks: false)) {
      if (entity is Directory &&
          entity.uri.pathSegments.isNotEmpty &&
          entity.uri.pathSegments
                  .where((part) => part.isNotEmpty)
                  .last
                  .toLowerCase() ==
              directoryName.toLowerCase()) {
        return entity.path;
      }
    }
    return null;
  }

  Future<void> _copyIfExists(String source, String destination) async {
    final file = File(source);
    if (await file.exists()) await file.copy(destination);
  }

  Future<void> _updatePatchSettings(String gameDir, String version) async {
    final settingsPath = _join(gameDir, AppConstants.settingsDirectoryName,
        AppConstants.settingsFileName);
    final file = File(settingsPath);
    final values = <String, String>{
      'version': version,
      'usage_sharing': 'true',
      'crash_reports': 'true',
      'check_for_updates': 'true',
      'auto_update': 'false',
      'use_magicka_1_controller_scheme': 'false',
      'language': AppStrings.of(context).language.localeTag,
      'skipped_version': '',
      'created_utc': DateTime.now().toUtc().toIso8601String(),
      'event_log':
          '${AppConstants.settingsDirectoryName}\\${AppConstants.eventLogFileName}',
    };
    final existingSettings = await _readIniFile(settingsPath);
    for (final entry in existingSettings.entries) {
      if (values.containsKey(entry.key)) values[entry.key] = entry.value;
    }
    values['version'] = version;
    values['skipped_version'] = '';
    await Directory(file.parent.path).create(recursive: true);
    await file.writeAsString('''
[MagickaCommunityPatch]
version=${values['version']}
usage_sharing=${values['usage_sharing']}
crash_reports=${values['crash_reports']}
check_for_updates=${values['check_for_updates']}
auto_update=${values['auto_update']}
use_magicka_1_controller_scheme=${values['use_magicka_1_controller_scheme']}
language=${values['language']}
skipped_version=${values['skipped_version']}
created_utc=${values['created_utc']}
event_log=${values['event_log']}
''');
  }

  Future<void> _deletePendingUpdate(String gameDir) async {
    final file = File(_join(
        gameDir, AppConstants.settingsDirectoryName, 'pending-update.ini'));
    try {
      if (await file.exists()) await file.delete();
    } catch (_) {}
  }

  Future<void> _waitForProcessExit(int pid) async {
    try {
      await Process.run('powershell', <String>[
        '-NoProfile',
        '-Command',
        'Wait-Process -Id $pid -Timeout 20 -ErrorAction SilentlyContinue',
      ]).timeout(const Duration(seconds: 22));
    } catch (_) {}
  }

  String _displayVersion(UpdaterCommand command) =>
      command.version.trim().isEmpty
          ? AppConstants.patchVersion
          : command.version.trim();

  Future<void> _showFeedbackDialog() async {
    final s = AppStrings.of(context);
    final nameController = TextEditingController();
    final subjectController = TextEditingController();
    final messageController = TextEditingController();
    try {
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          backgroundColor: const Color(0xff101315),
          title: Text(s.t('feedbackTitle')),
          content: SizedBox(
            width: 520,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: <Widget>[
                TextField(
                  controller: nameController,
                  decoration: InputDecoration(labelText: s.t('feedbackName')),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: subjectController,
                  decoration:
                      InputDecoration(labelText: s.t('feedbackSubject')),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: messageController,
                  maxLines: 7,
                  decoration:
                      InputDecoration(labelText: s.t('feedbackMessage')),
                ),
              ],
            ),
          ),
          actions: <Widget>[
            TextButton(
                onPressed: () => Navigator.pop(context),
                child: Text(s.t('cancel'))),
            FilledButton.icon(
              onPressed: () async {
                final sent = await _sendFeedback(
                  contextName: 'auto_update',
                  name: nameController.text,
                  subject: subjectController.text,
                  message: messageController.text,
                );
                if (!mounted) return;
                Navigator.pop(context);
                setState(() => _status =
                    sent ? s.t('feedbackSent') : s.t('feedbackNotSent'));
                await _showMessage(
                    sent ? s.t('feedbackThankYou') : s.t('feedbackFailed'));
              },
              icon: const Icon(Icons.send, size: 16),
              label: Text(s.t('feedbackSend')),
            ),
          ],
        ),
      );
    } finally {
      nameController.dispose();
      subjectController.dispose();
      messageController.dispose();
    }
  }

  Future<bool> _sendFeedback({
    required String contextName,
    required String name,
    required String subject,
    required String message,
  }) async {
    final gameDir = widget.command?.gameDir ?? '';
    final properties = <String, Object>{
      'distinct_id': await _feedbackDistinctId(gameDir),
      r'$process_person_profile': false,
      'context': _safeTelemetryText(contextName, 100),
      'feedback': _safeTelemetryText(message, 4000),
      'source': 'flutter_auto_updater',
      'patch_name': 'Community Patch',
      'patch_version': AppConstants.patchVersion,
      'target_version': widget.command?.version ?? '',
      'os': _safeTelemetryText(Platform.operatingSystemVersion, 200),
    };
    _addOptionalTelemetry(properties, 'name', name, 200);
    _addOptionalTelemetry(properties, 'subject', subject, 300);

    final payload = <String, Object>{
      'api_key': AppConstants.postHogApiKey,
      'event': 'magicka_patch_feedback_${_normalizeEventPart(contextName)}',
      'properties': properties,
    };

    final client = HttpClient()
      ..connectionTimeout = const Duration(milliseconds: 2200);
    try {
      final request =
          await client.postUrl(Uri.parse(AppConstants.postHogEndpoint));
      request.headers.contentType = ContentType.json;
      request.headers.set(HttpHeaders.userAgentHeader,
          'MagickaPatchAutoUpdater/${AppConstants.patchVersion}');
      request.write(jsonEncode(payload));
      final response =
          await request.close().timeout(const Duration(milliseconds: 4200));
      await response.drain();
      return response.statusCode >= 200 && response.statusCode < 300;
    } catch (_) {
      return false;
    } finally {
      client.close(force: true);
    }
  }

  Future<String> _feedbackDistinctId(String gameDir) async {
    final baseDir = gameDir.isNotEmpty
        ? _join(gameDir, AppConstants.settingsDirectoryName)
        : _join(Platform.environment['APPDATA'] ?? Directory.systemTemp.path,
            'MagickaPatch');
    final idFile = File(_join(baseDir, 'anonymous-id.txt'));
    try {
      await Directory(baseDir).create(recursive: true);
      if (await idFile.exists()) {
        final existing = (await idFile.readAsString()).trim();
        if (existing.length >= 16) return existing;
      }
      final id = _newTelemetryId();
      await idFile.writeAsString(id, flush: true);
      return id;
    } catch (_) {
      return 'ephemeral_${_newTelemetryId()}';
    }
  }

  Future<void> _openPatreon() async {
    await Process.run(
        'cmd', <String>['/c', 'start', '', AppConstants.patreonUrl]);
  }

  Future<void> _showMessage(String message) async {
    if (!mounted) return;
    final s = AppStrings.of(context);
    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: const Color(0xff101315),
        title: Text(s.t('appTitle')),
        content: Text(message),
        actions: <Widget>[
          TextButton(
              onPressed: () => Navigator.pop(context), child: Text(s.t('ok')))
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final version = widget.command == null
        ? AppConstants.patchVersion
        : _displayVersion(widget.command!);
    return Scaffold(
      backgroundColor: const Color(0xff040608),
      body: SizedBox.expand(
        child: FittedBox(
          fit: BoxFit.contain,
          child: SizedBox(
            width: 1220,
            height: 720,
            child: AnimatedBuilder(
              animation: _pulse,
              builder: (context, _) => Stack(
                children: <Widget>[
                  const ArcaneBackdrop(),
                  const Positioned(
                      left: 0,
                      top: 0,
                      width: 226,
                      height: 720,
                      child: SidebarImage()),
                  Positioned(left: 250, top: 47, child: _Header()),
                  Positioned(
                    left: 250,
                    top: 150,
                    width: 946,
                    height: 210,
                    child: ArcanePanel(
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(24, 24, 24, 22),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            Text(
                                s
                                    .t('updateTitle')
                                    .replaceAll('{version}', version),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                    color: Color(0xfff7d897),
                                    fontFamily: 'Georgia',
                                    fontSize: 30,
                                    fontWeight: FontWeight.bold)),
                            const SizedBox(height: 10),
                            Text(
                              s.t('updateBody'),
                              maxLines: 3,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                  color: Color(0xffeedfc4),
                                  fontSize: 16,
                                  height: 1.35),
                            ),
                            const Spacer(),
                            Row(
                              children: <Widget>[
                                Icon(_updated
                                    ? Icons.verified_rounded
                                    : Icons.auto_awesome),
                                const SizedBox(width: 10),
                                Expanded(
                                  child: Text(_status,
                                      overflow: TextOverflow.ellipsis,
                                      style: const TextStyle(
                                          color: Color(0xffbeb19b),
                                          fontSize: 14)),
                                ),
                              ],
                            )
                          ],
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                      left: 250,
                      top: 410,
                      width: 946,
                      height: 150,
                      child: SpecialThanksBanner(starProgram: _starProgram)),
                  Positioned(
                      left: 66,
                      top: 654,
                      width: 286,
                      height: 42,
                      child: FlameButton(
                          program: _starProgram,
                          label: _busy ? s.t('updating') : s.t('updatePatch'),
                          icon: Icons.system_update_alt_rounded,
                          accent: const Color(0xff3f9fff),
                          starField: true,
                          overlayIcon: true,
                          starTuning: _starTuning,
                          forceHover: _busy,
                          intensity: 1.0,
                          onTap: _updatePatch)),
                  Positioned(
                      left: 372,
                      top: 654,
                      width: 246,
                      height: 42,
                      child: FlameButton(
                          program: _flameProgram,
                          label: s.t('sendFeedback'),
                          icon: Icons.chat_bubble,
                          accent: const Color(0xffd9a04f),
                          overlayIcon: true,
                          onTap: _showFeedbackDialog)),
                  Positioned(
                      left: 638,
                      top: 654,
                      width: 292,
                      height: 42,
                      child: FlameButton(
                          program: _flameProgram,
                          label: s.t('supportOnPatreon'),
                          icon: Icons.favorite,
                          accent: const Color(0xffff5d2d),
                          patreon: true,
                          patreonFire: true,
                          patreonTuning: _patreonTuning,
                          patreonHeartFlameProgram: _patreonHeartFlameProgram,
                          patreonSparkProgram: _patreonSparkProgram,
                          onTap: _openPatreon)),
                  Positioned(
                      left: 950,
                      top: 654,
                      width: 246,
                      height: 42,
                      child: FlameButton(
                          program: _flameProgram,
                          label: s.t('close'),
                          icon: Icons.close,
                          accent: const Color(0xffd03f30),
                          overlayIcon: true,
                          onTap: () => exit(0))),
                  const Positioned.fill(
                      child: IgnorePointer(child: OrnateFrame())),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class UninstallerScreen extends StatefulWidget {
  const UninstallerScreen({super.key});

  @override
  State<UninstallerScreen> createState() => _UninstallerScreenState();
}

class _UninstallerScreenState extends State<UninstallerScreen>
    with TickerProviderStateMixin {
  late final AnimationController _pulse;
  ui.FragmentProgram? _flameProgram;
  ui.FragmentProgram? _starProgram;
  ui.FragmentProgram? _patreonHeartFlameProgram;
  ui.FragmentProgram? _patreonSparkProgram;
  final StarTuningConfig _starTuning = StarTuningConfig.defaults();
  final PatreonFireConfig _patreonTuning = PatreonFireConfig.defaults();
  bool _busy = false;
  bool _removed = false;
  bool _cleanupScheduled = false;
  bool _statusInitialized = false;
  String _status = '';

  String get _gameDir => File(Platform.resolvedExecutable).parent.path;

  @override
  void initState() {
    super.initState();
    _pulse = AnimationController(
        vsync: this, duration: const Duration(milliseconds: 3200))
      ..repeat();
    _loadShader();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_statusInitialized) return;
    _status = AppStrings.of(context).t('uninstallInitialStatus');
    _statusInitialized = true;
  }

  Future<void> _loadShader() async {
    try {
      final flame = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/flame_button.frag'));
      if (mounted) setState(() => _flameProgram = flame);
    } catch (_) {}

    try {
      final stars = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/install_star_button.frag'));
      if (mounted) setState(() => _starProgram = stars);
    } catch (_) {}

    try {
      final patreonFlame = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/patreon_heart_flame.frag'));
      if (mounted) setState(() => _patreonHeartFlameProgram = patreonFlame);
    } catch (_) {}

    try {
      final patreonSparks = await ui.FragmentProgram.fromAsset(
          _assetKey('shaders/patreon_fire_sparks.frag'));
      if (mounted) setState(() => _patreonSparkProgram = patreonSparks);
    } catch (_) {}
  }

  @override
  void dispose() {
    _pulse.dispose();
    super.dispose();
  }

  Future<void> _uninstall() async {
    if (_busy || _removed) return;
    final s = AppStrings.of(context);
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            backgroundColor: const Color(0xff101315),
            title: Text(s.t('uninstallConfirmTitle')),
            content: Text(s.uninstallConfirmBody(_gameDir)),
            actions: <Widget>[
              TextButton(
                  onPressed: () => Navigator.pop(context, false),
                  child: Text(s.t('cancel'))),
              FilledButton.icon(
                onPressed: () => Navigator.pop(context, true),
                icon: const Icon(Icons.delete_outline_rounded, size: 18),
                label: Text(s.t('uninstallConfirmButton')),
              ),
            ],
          ),
        ) ??
        false;
    if (!confirmed) return;

    setState(() {
      _busy = true;
      _status = s.t('restoringOriginalFiles');
    });

    try {
      final removed = await _uninstallPatch(_gameDir);
      if (!mounted) return;
      if (!removed) {
        setState(() => _status = s.t('originalFileRecoveryCancelled'));
        return;
      }
      setState(() {
        _removed = true;
        _status = s.t('thePatchWasRemoved');
      });
      await _showMessage(s.t('thePatchWasRemoved'));
    } catch (error) {
      if (!mounted) return;
      setState(() => _status = s.uninstallFailed(error));
      await _showMessage(s.uninstallFailed(error));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<bool> _uninstallPatch(String gameDir) async {
    final communityDir =
        Directory(_join(gameDir, AppConstants.settingsDirectoryName));
    final backupDir = _join(communityDir.path, 'backup');
    final manifest = await _readIniFile(
        _join(communityDir.path, AppConstants.manifestFileName));
    final originalBackups = await _ensureVerifiedOriginalBackups(
      context,
      gameDirectory: gameDir,
      backupDirectory: backupDir,
      manifest: manifest,
    );
    if (originalBackups == null) return false;

    await File(originalBackups.magicka!).copy(_join(gameDir, 'Magicka.exe'));
    await File(originalBackups.polygonHead!)
        .copy(_join(gameDir, 'PolygonHead.dll'));

    await _deleteIfExists(_join(gameDir, AppConstants.installerFileName));
    await _deleteIfExists(_join(gameDir, AppConstants.toolFileName));
    await _deleteIfExists(
        _join(gameDir, AppConstants.uninstallerCommandFileName));

    try {
      if (await communityDir.exists())
        await communityDir.delete(recursive: true);
    } catch (_) {}

    await _scheduleToolCleanup(gameDir);
    return true;
  }

  Future<void> _scheduleToolCleanup(String gameDir) async {
    if (_cleanupScheduled) return;
    _cleanupScheduled = true;

    final targets = <String>[
      _join(gameDir, AppConstants.installerFileName),
      _join(gameDir, AppConstants.toolFileName),
      _join(gameDir, AppConstants.uninstallerFileName),
      _join(gameDir, AppConstants.uninstallerCommandFileName),
      _join(gameDir, 'flutter_windows.dll'),
      _join(gameDir, 'data'),
    ];
    final currentPid = pid;
    final targetArray = targets.map(_psQuote).join(',');
    final command = '\$cleanupPid=$currentPid; \$targets=@($targetArray); '
        'try { Wait-Process -Id \$cleanupPid -ErrorAction SilentlyContinue } catch {} '
        'Start-Sleep -Milliseconds 300; '
        'for(\$i=0; \$i -lt 20; \$i++){ foreach(\$target in \$targets){ Remove-Item -LiteralPath \$target -Recurse -Force -ErrorAction SilentlyContinue }; Start-Sleep -Milliseconds 500 }';
    try {
      await Process.start(
        'powershell',
        <String>[
          '-NoProfile',
          '-ExecutionPolicy',
          'Bypass',
          '-WindowStyle',
          'Hidden',
          '-Command',
          command,
        ],
        mode: ProcessStartMode.detached,
      );
    } catch (_) {}
  }

  Future<void> _openPatreon() async {
    await Process.run(
        'cmd', <String>['/c', 'start', '', AppConstants.patreonUrl]);
  }

  Future<void> _close() async {
    if (_removed) await _scheduleToolCleanup(_gameDir);
    exit(0);
  }

  Future<void> _showMessage(String message) async {
    if (!mounted) return;
    final s = AppStrings.of(context);
    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: const Color(0xff101315),
        title: Text(s.t('appTitle')),
        content: Text(message),
        actions: <Widget>[
          TextButton(
              onPressed: () => Navigator.pop(context), child: Text(s.t('ok')))
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    return Scaffold(
      backgroundColor: const Color(0xff040608),
      body: SizedBox.expand(
        child: FittedBox(
          fit: BoxFit.contain,
          child: SizedBox(
            width: 1220,
            height: 720,
            child: AnimatedBuilder(
              animation: _pulse,
              builder: (context, _) => Stack(
                children: <Widget>[
                  const ArcaneBackdrop(),
                  const Positioned(
                      left: 0,
                      top: 0,
                      width: 226,
                      height: 720,
                      child: SidebarImage()),
                  Positioned(left: 250, top: 47, child: _Header()),
                  Positioned(
                    left: 250,
                    top: 150,
                    width: 946,
                    height: 226,
                    child: ArcanePanel(
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(24, 24, 24, 22),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: <Widget>[
                            Text(s.t('uninstallTitle'),
                                style: const TextStyle(
                                    color: Color(0xfff7d897),
                                    fontFamily: 'Georgia',
                                    fontSize: 30,
                                    fontWeight: FontWeight.bold)),
                            const SizedBox(height: 10),
                            Text(
                              s.t('uninstallBody'),
                              style: const TextStyle(
                                  color: Color(0xffeedfc4),
                                  fontSize: 16,
                                  height: 1.35),
                            ),
                            const SizedBox(height: 16),
                            Text(_gameDir,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                    color: Color(0xffbeb19b), fontSize: 14)),
                            const Spacer(),
                            Row(
                              children: <Widget>[
                                Icon(_removed
                                    ? Icons.verified_rounded
                                    : Icons.restore_rounded),
                                const SizedBox(width: 10),
                                Expanded(
                                  child: Text(_status,
                                      overflow: TextOverflow.ellipsis,
                                      style: const TextStyle(
                                          color: Color(0xffbeb19b),
                                          fontSize: 14)),
                                ),
                              ],
                            )
                          ],
                        ),
                      ),
                    ),
                  ),
                  Positioned(
                      left: 250,
                      top: 420,
                      width: 946,
                      height: 150,
                      child: SpecialThanksBanner(starProgram: _starProgram)),
                  Positioned(
                      left: 66,
                      top: 654,
                      width: 286,
                      height: 42,
                      child: FlameButton(
                          program: _starProgram,
                          label:
                              _busy ? s.t('removing') : s.t('uninstallPatch'),
                          icon: Icons.delete_outline_rounded,
                          accent: const Color(0xffd03f30),
                          starField: true,
                          overlayIcon: true,
                          starTuning: _starTuning,
                          forceHover: _busy,
                          intensity: 1.0,
                          onTap: _uninstall)),
                  Positioned(
                      left: 466,
                      top: 654,
                      width: 292,
                      height: 42,
                      child: FlameButton(
                          program: _flameProgram,
                          label: s.t('supportOnPatreon'),
                          icon: Icons.favorite,
                          accent: const Color(0xffff5d2d),
                          patreon: true,
                          patreonFire: true,
                          patreonTuning: _patreonTuning,
                          patreonHeartFlameProgram: _patreonHeartFlameProgram,
                          patreonSparkProgram: _patreonSparkProgram,
                          onTap: _openPatreon)),
                  Positioned(
                      left: 910,
                      top: 654,
                      width: 286,
                      height: 42,
                      child: FlameButton(
                          program: _flameProgram,
                          label: s.t('close'),
                          icon: Icons.close,
                          accent: const Color(0xffd03f30),
                          overlayIcon: true,
                          onTap: _close)),
                  const Positioned.fill(
                      child: IgnorePointer(child: OrnateFrame())),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _DebugTunerButton extends StatelessWidget {
  const _DebugTunerButton({required this.open, required this.onPressed});

  final bool open;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    return OutlinedButton.icon(
      onPressed: onPressed,
      icon: const Icon(Icons.tune, size: 15),
      label: Text(open ? 'Hide tuner' : 'VFX tuner',
          overflow: TextOverflow.ellipsis),
      style: OutlinedButton.styleFrom(
        foregroundColor: const Color(0xfff7d897),
        side: const BorderSide(color: Color(0xff845025)),
        backgroundColor: const Color(0xdd080b0d),
        padding: const EdgeInsets.symmetric(horizontal: 8),
        textStyle: const TextStyle(fontSize: 11, fontWeight: FontWeight.bold),
      ),
    );
  }
}

class StarTuningConfig {
  const StarTuningConfig({required this.normal, required this.hover});

  factory StarTuningConfig.defaults() {
    return const StarTuningConfig(
      normal: StarTuningMode(
        spawnWidth: 1.000,
        spawnHeight: 1.000,
        spawnRate: 20.0,
        radiusMin: 0.75,
        radiusMax: 2.20,
        hueMin: 205.0,
        hueMax: 322.0,
        speedMin: 30.0,
        speedMax: 62.0,
        lifetimeMin: 1.05,
        lifetimeMax: 1.75,
        brightnessMin: 0.45,
        brightnessMax: 0.77,
        rotationMin: -2.80,
        rotationMax: 2.80,
        tipLengthMin: 0.50,
        tipLengthMax: 5.40,
        centerRadiusMin: 0.82,
        centerRadiusMax: 1.18,
        accentMix: 0.30,
      ),
      hover: StarTuningMode(
        spawnWidth: 1.000,
        spawnHeight: 1.000,
        spawnRate: 68.0,
        radiusMin: 0.75,
        radiusMax: 8.00,
        hueMin: 205.0,
        hueMax: 322.0,
        speedMin: 30.0,
        speedMax: 62.0,
        lifetimeMin: 1.15,
        lifetimeMax: 2.10,
        brightnessMin: 0.45,
        brightnessMax: 0.77,
        rotationMin: -2.80,
        rotationMax: 2.80,
        tipLengthMin: 0.50,
        tipLengthMax: 8.50,
        centerRadiusMin: 0.15,
        centerRadiusMax: 0.85,
        accentMix: 0.33,
      ),
    );
  }

  final StarTuningMode normal;
  final StarTuningMode hover;

  StarTuningConfig copyWith({StarTuningMode? normal, StarTuningMode? hover}) {
    return StarTuningConfig(
        normal: normal ?? this.normal, hover: hover ?? this.hover);
  }

  String toDartLiteral() {
    return '''
const StarTuningConfig(
  normal: ${normal.toDartLiteral()},
  hover: ${hover.toDartLiteral()},
)''';
  }
}

class StarTuningMode {
  const StarTuningMode({
    required this.spawnWidth,
    required this.spawnHeight,
    required this.spawnRate,
    required this.radiusMin,
    required this.radiusMax,
    required this.hueMin,
    required this.hueMax,
    required this.speedMin,
    required this.speedMax,
    required this.lifetimeMin,
    required this.lifetimeMax,
    required this.brightnessMin,
    required this.brightnessMax,
    required this.rotationMin,
    required this.rotationMax,
    required this.tipLengthMin,
    required this.tipLengthMax,
    required this.centerRadiusMin,
    required this.centerRadiusMax,
    required this.accentMix,
  });

  final double spawnWidth;
  final double spawnHeight;
  final double spawnRate;
  final double radiusMin;
  final double radiusMax;
  final double hueMin;
  final double hueMax;
  final double speedMin;
  final double speedMax;
  final double lifetimeMin;
  final double lifetimeMax;
  final double brightnessMin;
  final double brightnessMax;
  final double rotationMin;
  final double rotationMax;
  final double tipLengthMin;
  final double tipLengthMax;
  final double centerRadiusMin;
  final double centerRadiusMax;
  final double accentMix;

  StarTuningMode copyWith({
    double? spawnWidth,
    double? spawnHeight,
    double? spawnRate,
    double? radiusMin,
    double? radiusMax,
    double? hueMin,
    double? hueMax,
    double? speedMin,
    double? speedMax,
    double? lifetimeMin,
    double? lifetimeMax,
    double? brightnessMin,
    double? brightnessMax,
    double? rotationMin,
    double? rotationMax,
    double? tipLengthMin,
    double? tipLengthMax,
    double? centerRadiusMin,
    double? centerRadiusMax,
    double? accentMix,
  }) {
    return StarTuningMode(
      spawnWidth: spawnWidth ?? this.spawnWidth,
      spawnHeight: spawnHeight ?? this.spawnHeight,
      spawnRate: spawnRate ?? this.spawnRate,
      radiusMin: radiusMin ?? this.radiusMin,
      radiusMax: radiusMax ?? this.radiusMax,
      hueMin: hueMin ?? this.hueMin,
      hueMax: hueMax ?? this.hueMax,
      speedMin: speedMin ?? this.speedMin,
      speedMax: speedMax ?? this.speedMax,
      lifetimeMin: lifetimeMin ?? this.lifetimeMin,
      lifetimeMax: lifetimeMax ?? this.lifetimeMax,
      brightnessMin: brightnessMin ?? this.brightnessMin,
      brightnessMax: brightnessMax ?? this.brightnessMax,
      rotationMin: rotationMin ?? this.rotationMin,
      rotationMax: rotationMax ?? this.rotationMax,
      tipLengthMin: tipLengthMin ?? this.tipLengthMin,
      tipLengthMax: tipLengthMax ?? this.tipLengthMax,
      centerRadiusMin: centerRadiusMin ?? this.centerRadiusMin,
      centerRadiusMax: centerRadiusMax ?? this.centerRadiusMax,
      accentMix: accentMix ?? this.accentMix,
    );
  }

  String toDartLiteral() {
    return '''
StarTuningMode(
    spawnWidth: ${_fixed(spawnWidth, 3)},
    spawnHeight: ${_fixed(spawnHeight, 3)},
    spawnRate: ${_fixed(spawnRate, 1)},
    radiusMin: ${_fixed(radiusMin, 2)},
    radiusMax: ${_fixed(radiusMax, 2)},
    hueMin: ${_fixed(hueMin, 1)},
    hueMax: ${_fixed(hueMax, 1)},
    speedMin: ${_fixed(speedMin, 1)},
    speedMax: ${_fixed(speedMax, 1)},
    lifetimeMin: ${_fixed(lifetimeMin, 2)},
    lifetimeMax: ${_fixed(lifetimeMax, 2)},
    brightnessMin: ${_fixed(brightnessMin, 2)},
    brightnessMax: ${_fixed(brightnessMax, 2)},
    rotationMin: ${_fixed(rotationMin, 2)},
    rotationMax: ${_fixed(rotationMax, 2)},
    tipLengthMin: ${_fixed(tipLengthMin, 2)},
    tipLengthMax: ${_fixed(tipLengthMax, 2)},
    centerRadiusMin: ${_fixed(centerRadiusMin, 2)},
    centerRadiusMax: ${_fixed(centerRadiusMax, 2)},
    accentMix: ${_fixed(accentMix, 2)},
  )''';
  }
}

class PatreonFireConfig {
  const PatreonFireConfig({required this.normal, required this.hover});

  factory PatreonFireConfig.defaults() {
    return const PatreonFireConfig(
      normal: PatreonFireMode(
        heartX: 0.205,
        heartY: 0.520,
        heartSize: 0.82,
        heartGlow: 0.00,
        heartBloom: 0.00,
        flameHeight: 14.00,
        flameWidth: 0.98,
        flameOriginY: 1.800,
        flameIntensity: 0.91,
        edgeFlameHeight: 280.0,
        edgeFlameY: -0.130,
        edgeFlameIntensity: 0.41,
        sideFlameHeight: 11.75,
        sideFlameWidth: 0.02,
        sideFlameX: 0.497,
        sideFlameOriginY: 1.420,
        sideFlameIntensity: 0.34,
        sparkSpawnRate: 34.0,
        sparkSpeedMin: 90.0,
        sparkSpeedMax: 220.0,
        sparkSizeMin: 1.30,
        sparkSizeMax: 6.00,
        sparkSpread: 1.16,
        sparkOriginY: 5.000,
        sparkBottomCrop: 0.310,
        sparkOpacity: 0.00,
        sparkMotionX: -0.14,
        sparkMotionY: -3.00,
        sparkSmoke: 0.22,
        sparkBloom: 0.00,
        sparkLayerSize: 1.60,
        sparkLayerAlpha: 0.70,
        sparkLayers: 13.0,
        buttonGlow: 1.20,
      ),
      hover: PatreonFireMode(
        heartX: 0.205,
        heartY: 0.520,
        heartSize: 1.00,
        heartGlow: 1.55,
        heartBloom: 1.65,
        flameHeight: 14.00,
        flameWidth: 0.98,
        flameOriginY: 1.800,
        flameIntensity: 3.19,
        edgeFlameHeight: 280.0,
        edgeFlameY: -0.130,
        edgeFlameIntensity: 2.25,
        sideFlameHeight: 11.75,
        sideFlameWidth: 0.02,
        sideFlameX: 0.497,
        sideFlameOriginY: 1.420,
        sideFlameIntensity: 1.45,
        sparkSpawnRate: 34.0,
        sparkSpeedMin: 90.0,
        sparkSpeedMax: 220.0,
        sparkSizeMin: 1.30,
        sparkSizeMax: 6.00,
        sparkSpread: 1.16,
        sparkOriginY: 5.000,
        sparkBottomCrop: 0.310,
        sparkOpacity: 1.00,
        sparkMotionX: -0.14,
        sparkMotionY: -3.00,
        sparkSmoke: 0.22,
        sparkBloom: 0.00,
        sparkLayerSize: 1.60,
        sparkLayerAlpha: 0.70,
        sparkLayers: 13.0,
        buttonGlow: 1.20,
      ),
    );
  }

  final PatreonFireMode normal;
  final PatreonFireMode hover;

  PatreonFireConfig copyWith(
      {PatreonFireMode? normal, PatreonFireMode? hover}) {
    return PatreonFireConfig(
        normal: normal ?? this.normal, hover: hover ?? this.hover);
  }

  String toDartLiteral() {
    return '''
const PatreonFireConfig(
  normal: ${normal.toDartLiteral()},
  hover: ${hover.toDartLiteral()},
)''';
  }
}

class PatreonFireMode {
  const PatreonFireMode({
    required this.heartX,
    required this.heartY,
    required this.heartSize,
    required this.heartGlow,
    required this.heartBloom,
    required this.flameHeight,
    required this.flameWidth,
    required this.flameOriginY,
    required this.flameIntensity,
    required this.edgeFlameHeight,
    required this.edgeFlameY,
    required this.edgeFlameIntensity,
    required this.sideFlameHeight,
    required this.sideFlameWidth,
    required this.sideFlameX,
    required this.sideFlameOriginY,
    required this.sideFlameIntensity,
    required this.sparkSpawnRate,
    required this.sparkSpeedMin,
    required this.sparkSpeedMax,
    required this.sparkSizeMin,
    required this.sparkSizeMax,
    required this.sparkSpread,
    required this.sparkOriginY,
    required this.sparkBottomCrop,
    required this.sparkOpacity,
    required this.sparkMotionX,
    required this.sparkMotionY,
    required this.sparkSmoke,
    required this.sparkBloom,
    required this.sparkLayerSize,
    required this.sparkLayerAlpha,
    required this.sparkLayers,
    required this.buttonGlow,
  });

  final double heartX;
  final double heartY;
  final double heartSize;
  final double heartGlow;
  final double heartBloom;
  final double flameHeight;
  final double flameWidth;
  final double flameOriginY;
  final double flameIntensity;
  final double edgeFlameHeight;
  final double edgeFlameY;
  final double edgeFlameIntensity;
  final double sideFlameHeight;
  final double sideFlameWidth;
  final double sideFlameX;
  final double sideFlameOriginY;
  final double sideFlameIntensity;
  final double sparkSpawnRate;
  final double sparkSpeedMin;
  final double sparkSpeedMax;
  final double sparkSizeMin;
  final double sparkSizeMax;
  final double sparkSpread;
  final double sparkOriginY;
  final double sparkBottomCrop;
  final double sparkOpacity;
  final double sparkMotionX;
  final double sparkMotionY;
  final double sparkSmoke;
  final double sparkBloom;
  final double sparkLayerSize;
  final double sparkLayerAlpha;
  final double sparkLayers;
  final double buttonGlow;

  PatreonFireMode copyWith({
    double? heartX,
    double? heartY,
    double? heartSize,
    double? heartGlow,
    double? heartBloom,
    double? flameHeight,
    double? flameWidth,
    double? flameOriginY,
    double? flameIntensity,
    double? edgeFlameHeight,
    double? edgeFlameY,
    double? edgeFlameIntensity,
    double? sideFlameHeight,
    double? sideFlameWidth,
    double? sideFlameX,
    double? sideFlameOriginY,
    double? sideFlameIntensity,
    double? sparkSpawnRate,
    double? sparkSpeedMin,
    double? sparkSpeedMax,
    double? sparkSizeMin,
    double? sparkSizeMax,
    double? sparkSpread,
    double? sparkOriginY,
    double? sparkBottomCrop,
    double? sparkOpacity,
    double? sparkMotionX,
    double? sparkMotionY,
    double? sparkSmoke,
    double? sparkBloom,
    double? sparkLayerSize,
    double? sparkLayerAlpha,
    double? sparkLayers,
    double? buttonGlow,
  }) {
    return PatreonFireMode(
      heartX: heartX ?? this.heartX,
      heartY: heartY ?? this.heartY,
      heartSize: heartSize ?? this.heartSize,
      heartGlow: heartGlow ?? this.heartGlow,
      heartBloom: heartBloom ?? this.heartBloom,
      flameHeight: flameHeight ?? this.flameHeight,
      flameWidth: flameWidth ?? this.flameWidth,
      flameOriginY: flameOriginY ?? this.flameOriginY,
      flameIntensity: flameIntensity ?? this.flameIntensity,
      edgeFlameHeight: edgeFlameHeight ?? this.edgeFlameHeight,
      edgeFlameY: edgeFlameY ?? this.edgeFlameY,
      edgeFlameIntensity: edgeFlameIntensity ?? this.edgeFlameIntensity,
      sideFlameHeight: sideFlameHeight ?? this.sideFlameHeight,
      sideFlameWidth: sideFlameWidth ?? this.sideFlameWidth,
      sideFlameX: sideFlameX ?? this.sideFlameX,
      sideFlameOriginY: sideFlameOriginY ?? this.sideFlameOriginY,
      sideFlameIntensity: sideFlameIntensity ?? this.sideFlameIntensity,
      sparkSpawnRate: sparkSpawnRate ?? this.sparkSpawnRate,
      sparkSpeedMin: sparkSpeedMin ?? this.sparkSpeedMin,
      sparkSpeedMax: sparkSpeedMax ?? this.sparkSpeedMax,
      sparkSizeMin: sparkSizeMin ?? this.sparkSizeMin,
      sparkSizeMax: sparkSizeMax ?? this.sparkSizeMax,
      sparkSpread: sparkSpread ?? this.sparkSpread,
      sparkOriginY: sparkOriginY ?? this.sparkOriginY,
      sparkBottomCrop: sparkBottomCrop ?? this.sparkBottomCrop,
      sparkOpacity: sparkOpacity ?? this.sparkOpacity,
      sparkMotionX: sparkMotionX ?? this.sparkMotionX,
      sparkMotionY: sparkMotionY ?? this.sparkMotionY,
      sparkSmoke: sparkSmoke ?? this.sparkSmoke,
      sparkBloom: sparkBloom ?? this.sparkBloom,
      sparkLayerSize: sparkLayerSize ?? this.sparkLayerSize,
      sparkLayerAlpha: sparkLayerAlpha ?? this.sparkLayerAlpha,
      sparkLayers: sparkLayers ?? this.sparkLayers,
      buttonGlow: buttonGlow ?? this.buttonGlow,
    );
  }

  static PatreonFireMode lerp(
      PatreonFireMode from, PatreonFireMode to, double t) {
    final amount = t.clamp(0.0, 1.0).toDouble();
    double mix(double a, double b) => a + (b - a) * amount;

    return PatreonFireMode(
      heartX: mix(from.heartX, to.heartX),
      heartY: mix(from.heartY, to.heartY),
      heartSize: mix(from.heartSize, to.heartSize),
      heartGlow: mix(from.heartGlow, to.heartGlow),
      heartBloom: mix(from.heartBloom, to.heartBloom),
      flameHeight: mix(from.flameHeight, to.flameHeight),
      flameWidth: mix(from.flameWidth, to.flameWidth),
      flameOriginY: mix(from.flameOriginY, to.flameOriginY),
      flameIntensity: mix(from.flameIntensity, to.flameIntensity),
      edgeFlameHeight: mix(from.edgeFlameHeight, to.edgeFlameHeight),
      edgeFlameY: mix(from.edgeFlameY, to.edgeFlameY),
      edgeFlameIntensity: mix(from.edgeFlameIntensity, to.edgeFlameIntensity),
      sideFlameHeight: mix(from.sideFlameHeight, to.sideFlameHeight),
      sideFlameWidth: mix(from.sideFlameWidth, to.sideFlameWidth),
      sideFlameX: mix(from.sideFlameX, to.sideFlameX),
      sideFlameOriginY: mix(from.sideFlameOriginY, to.sideFlameOriginY),
      sideFlameIntensity: mix(from.sideFlameIntensity, to.sideFlameIntensity),
      sparkSpawnRate: mix(from.sparkSpawnRate, to.sparkSpawnRate),
      sparkSpeedMin: mix(from.sparkSpeedMin, to.sparkSpeedMin),
      sparkSpeedMax: mix(from.sparkSpeedMax, to.sparkSpeedMax),
      sparkSizeMin: mix(from.sparkSizeMin, to.sparkSizeMin),
      sparkSizeMax: mix(from.sparkSizeMax, to.sparkSizeMax),
      sparkSpread: mix(from.sparkSpread, to.sparkSpread),
      sparkOriginY: mix(from.sparkOriginY, to.sparkOriginY),
      sparkBottomCrop: mix(from.sparkBottomCrop, to.sparkBottomCrop),
      sparkOpacity: mix(from.sparkOpacity, to.sparkOpacity),
      sparkMotionX: mix(from.sparkMotionX, to.sparkMotionX),
      sparkMotionY: mix(from.sparkMotionY, to.sparkMotionY),
      sparkSmoke: mix(from.sparkSmoke, to.sparkSmoke),
      sparkBloom: mix(from.sparkBloom, to.sparkBloom),
      sparkLayerSize: mix(from.sparkLayerSize, to.sparkLayerSize),
      sparkLayerAlpha: mix(from.sparkLayerAlpha, to.sparkLayerAlpha),
      sparkLayers: mix(from.sparkLayers, to.sparkLayers),
      buttonGlow: mix(from.buttonGlow, to.buttonGlow),
    );
  }

  String toDartLiteral() {
    return '''
PatreonFireMode(
    heartX: ${_fixed(heartX, 3)},
    heartY: ${_fixed(heartY, 3)},
    heartSize: ${_fixed(heartSize, 2)},
    heartGlow: ${_fixed(heartGlow, 2)},
    heartBloom: ${_fixed(heartBloom, 2)},
    flameHeight: ${_fixed(flameHeight, 2)},
    flameWidth: ${_fixed(flameWidth, 2)},
    flameOriginY: ${_fixed(flameOriginY, 3)},
    flameIntensity: ${_fixed(flameIntensity, 2)},
    edgeFlameHeight: ${_fixed(edgeFlameHeight, 1)},
    edgeFlameY: ${_fixed(edgeFlameY, 3)},
    edgeFlameIntensity: ${_fixed(edgeFlameIntensity, 2)},
    sideFlameHeight: ${_fixed(sideFlameHeight, 2)},
    sideFlameWidth: ${_fixed(sideFlameWidth, 2)},
    sideFlameX: ${_fixed(sideFlameX, 3)},
    sideFlameOriginY: ${_fixed(sideFlameOriginY, 3)},
    sideFlameIntensity: ${_fixed(sideFlameIntensity, 2)},
    sparkSpawnRate: ${_fixed(sparkSpawnRate, 1)},
    sparkSpeedMin: ${_fixed(sparkSpeedMin, 1)},
    sparkSpeedMax: ${_fixed(sparkSpeedMax, 1)},
    sparkSizeMin: ${_fixed(sparkSizeMin, 2)},
    sparkSizeMax: ${_fixed(sparkSizeMax, 2)},
    sparkSpread: ${_fixed(sparkSpread, 2)},
    sparkOriginY: ${_fixed(sparkOriginY, 3)},
    sparkBottomCrop: ${_fixed(sparkBottomCrop, 3)},
    sparkOpacity: ${_fixed(sparkOpacity, 2)},
    sparkMotionX: ${_fixed(sparkMotionX, 2)},
    sparkMotionY: ${_fixed(sparkMotionY, 2)},
    sparkSmoke: ${_fixed(sparkSmoke, 2)},
    sparkBloom: ${_fixed(sparkBloom, 2)},
    sparkLayerSize: ${_fixed(sparkLayerSize, 2)},
    sparkLayerAlpha: ${_fixed(sparkLayerAlpha, 2)},
    sparkLayers: ${_fixed(sparkLayers, 1)},
    buttonGlow: ${_fixed(buttonGlow, 2)},
  )''';
  }
}

enum VfxTuningTarget { installStars, patreonFire }

class VfxTuningPanel extends StatefulWidget {
  const VfxTuningPanel({
    super.key,
    required this.initialStarConfig,
    required this.initialPatreonConfig,
    required this.onStarChanged,
    required this.onPatreonChanged,
    required this.onPreviewChanged,
    required this.onClose,
  });

  final StarTuningConfig initialStarConfig;
  final PatreonFireConfig initialPatreonConfig;
  final ValueChanged<StarTuningConfig> onStarChanged;
  final ValueChanged<PatreonFireConfig> onPatreonChanged;
  final void Function(VfxTuningTarget target, bool hover) onPreviewChanged;
  final VoidCallback onClose;

  @override
  State<VfxTuningPanel> createState() => _VfxTuningPanelState();
}

class _VfxTuningPanelState extends State<VfxTuningPanel>
    with TickerProviderStateMixin {
  late StarTuningConfig _starConfig;
  late PatreonFireConfig _patreonConfig;
  late final TabController _targetController;
  late final TabController _modeController;
  bool _reportedPreviewHover = false;
  VfxTuningTarget _reportedPreviewTarget = VfxTuningTarget.installStars;

  @override
  void initState() {
    super.initState();
    _starConfig = widget.initialStarConfig;
    _patreonConfig = widget.initialPatreonConfig;
    _targetController = TabController(length: 2, vsync: this)
      ..addListener(_reportPreview);
    _modeController = TabController(length: 2, vsync: this)
      ..addListener(_reportPreview);
  }

  @override
  void dispose() {
    _targetController.dispose();
    _modeController.dispose();
    super.dispose();
  }

  VfxTuningTarget get _target => _targetController.index == 0
      ? VfxTuningTarget.installStars
      : VfxTuningTarget.patreonFire;

  void _reportPreview() {
    final target = _target;
    final previewHover = _modeController.index == 1;
    if (_reportedPreviewTarget == target &&
        _reportedPreviewHover == previewHover) return;
    if (mounted) {
      setState(() {
        _reportedPreviewTarget = target;
        _reportedPreviewHover = previewHover;
      });
    } else {
      _reportedPreviewTarget = target;
      _reportedPreviewHover = previewHover;
    }
    widget.onPreviewChanged(target, previewHover);
  }

  void _setStarConfig(StarTuningConfig config) {
    setState(() => _starConfig = config);
    widget.onStarChanged(config);
  }

  void _setPatreonConfig(PatreonFireConfig config) {
    setState(() => _patreonConfig = config);
    widget.onPatreonChanged(config);
  }

  Future<void> _copyConfig() async {
    final text = _target == VfxTuningTarget.installStars
        ? _starConfig.toDartLiteral()
        : _patreonConfig.toDartLiteral();
    await Clipboard.setData(ClipboardData(text: text));
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(const SnackBar(content: Text('VFX tuning copied.')));
  }

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        color: const Color(0xff080b0d),
        border: Border.all(color: const Color(0xff845025)),
        borderRadius: BorderRadius.circular(6),
        boxShadow: <BoxShadow>[
          BoxShadow(
              color: Colors.black.withValues(alpha: 0.45),
              blurRadius: 18,
              offset: const Offset(-4, 0))
        ],
      ),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Row(
              children: <Widget>[
                const Expanded(
                    child: Text('VFX Tuning',
                        style: TextStyle(
                            color: Color(0xfff7d897),
                            fontFamily: 'Georgia',
                            fontSize: 19,
                            fontWeight: FontWeight.bold))),
                IconButton(
                    onPressed: widget.onClose, icon: const Icon(Icons.close)),
              ],
            ),
            Row(
              children: <Widget>[
                Expanded(
                    child: TextButton.icon(
                        onPressed: _resetActive,
                        icon: const Icon(Icons.restart_alt),
                        label: const Text('Reset'))),
                const SizedBox(width: 8),
                Expanded(
                    child: FilledButton.icon(
                        onPressed: _copyConfig,
                        icon: const Icon(Icons.copy, size: 16),
                        label: const Text('Copy'))),
              ],
            ),
            const SizedBox(height: 10),
            Expanded(
              child: Column(
                children: <Widget>[
                  TabBar(
                    controller: _targetController,
                    labelColor: const Color(0xfff7d897),
                    unselectedLabelColor: const Color(0xffbeb19b),
                    indicatorColor: const Color(0xffff5d2d),
                    tabs: const <Widget>[
                      Tab(text: 'Install'),
                      Tab(text: 'Patreon')
                    ],
                  ),
                  const SizedBox(height: 8),
                  TabBar(
                    controller: _modeController,
                    labelColor: const Color(0xfff7d897),
                    unselectedLabelColor: const Color(0xffbeb19b),
                    indicatorColor: const Color(0xff80caff),
                    tabs: const <Widget>[
                      Tab(text: 'Normal'),
                      Tab(text: 'Hover')
                    ],
                  ),
                  const SizedBox(height: 8),
                  Expanded(
                    child: TabBarView(
                      controller: _targetController,
                      children: <Widget>[
                        _StarModeTuningTabView(
                            config: _starConfig,
                            modeController: _modeController,
                            onChanged: _setStarConfig),
                        _PatreonModeTuningTabView(
                            config: _patreonConfig,
                            modeController: _modeController,
                            onChanged: _setPatreonConfig),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 10),
            Container(
              height: 92,
              decoration: BoxDecoration(
                  color: const Color(0xff050709),
                  border: Border.all(color: const Color(0xff3b2b18)),
                  borderRadius: BorderRadius.circular(6)),
              padding: const EdgeInsets.all(8),
              child: SingleChildScrollView(
                  child: SelectableText(
                      (_target == VfxTuningTarget.installStars
                          ? _starConfig.toDartLiteral()
                          : _patreonConfig.toDartLiteral()),
                      style: const TextStyle(
                          color: Color(0xffbeb19b),
                          fontSize: 9.5,
                          fontFamily: 'Consolas'))),
            ),
          ],
        ),
      ),
    );
  }

  void _resetActive() {
    if (_target == VfxTuningTarget.installStars) {
      _setStarConfig(StarTuningConfig.defaults());
    } else {
      _setPatreonConfig(PatreonFireConfig.defaults());
    }
  }
}

class _StarModeTuningTabView extends StatelessWidget {
  const _StarModeTuningTabView(
      {required this.config,
      required this.modeController,
      required this.onChanged});

  final StarTuningConfig config;
  final TabController modeController;
  final ValueChanged<StarTuningConfig> onChanged;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: modeController,
      builder: (context, _) {
        final hover = modeController.index == 1;
        return _StarModeTuningPanel(
          title: hover ? 'Hover' : 'Normal',
          values: hover ? config.hover : config.normal,
          onChanged: (values) => onChanged(hover
              ? config.copyWith(hover: values)
              : config.copyWith(normal: values)),
        );
      },
    );
  }
}

class _PatreonModeTuningTabView extends StatelessWidget {
  const _PatreonModeTuningTabView(
      {required this.config,
      required this.modeController,
      required this.onChanged});

  final PatreonFireConfig config;
  final TabController modeController;
  final ValueChanged<PatreonFireConfig> onChanged;

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: modeController,
      builder: (context, _) {
        final hover = modeController.index == 1;
        return _PatreonModeTuningPanel(
          title: hover ? 'Hover' : 'Normal',
          values: hover ? config.hover : config.normal,
          onChanged: (values) => onChanged(hover
              ? config.copyWith(hover: values)
              : config.copyWith(normal: values)),
        );
      },
    );
  }
}

class _StarModeTuningPanel extends StatelessWidget {
  const _StarModeTuningPanel(
      {required this.title, required this.values, required this.onChanged});

  final String title;
  final StarTuningMode values;
  final ValueChanged<StarTuningMode> onChanged;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
          color: const Color(0xff101315),
          border: Border.all(color: const Color(0xff513719)),
          borderRadius: BorderRadius.circular(6)),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Text(title,
                style: const TextStyle(
                    color: Color(0xfff7d897),
                    fontFamily: 'Georgia',
                    fontSize: 17,
                    fontWeight: FontWeight.bold)),
            const SizedBox(height: 8),
            Expanded(
              child: ListView(
                children: <Widget>[
                  _singleSlider(
                      'Spawn width',
                      values.spawnWidth,
                      0.02,
                      1.00,
                      98,
                      _percent(values.spawnWidth),
                      (value) => onChanged(values.copyWith(spawnWidth: value))),
                  _singleSlider(
                      'Spawn height',
                      values.spawnHeight,
                      0.02,
                      1.00,
                      98,
                      _percent(values.spawnHeight),
                      (value) =>
                          onChanged(values.copyWith(spawnHeight: value))),
                  _singleSlider(
                      'Spawn rate',
                      values.spawnRate,
                      1,
                      120,
                      119,
                      '${_fixed(values.spawnRate, 0)} /s',
                      (value) => onChanged(values.copyWith(spawnRate: value))),
                  _rangeSlider(
                      'Star size',
                      values.radiusMin,
                      values.radiusMax,
                      0.20,
                      8.00,
                      156,
                      (value) => _fixed(value, 2),
                      (range) => onChanged(values.copyWith(
                          radiusMin: range.start, radiusMax: range.end))),
                  _rangeSlider(
                      'Color hue',
                      values.hueMin,
                      values.hueMax,
                      0,
                      360,
                      360,
                      (value) => '${_fixed(value, 0)} deg',
                      (range) => onChanged(values.copyWith(
                          hueMin: range.start, hueMax: range.end))),
                  _rangeSlider(
                      'Speed',
                      values.speedMin,
                      values.speedMax,
                      1,
                      280,
                      279,
                      (value) => '${_fixed(value, 0)} px/s',
                      (range) => onChanged(values.copyWith(
                          speedMin: range.start, speedMax: range.end))),
                  _rangeSlider(
                      'Lifetime',
                      values.lifetimeMin,
                      values.lifetimeMax,
                      0.20,
                      4.00,
                      190,
                      (value) => '${_fixed(value, 2)} s',
                      (range) => onChanged(values.copyWith(
                          lifetimeMin: range.start, lifetimeMax: range.end))),
                  _rangeSlider(
                      'Brightness',
                      values.brightnessMin,
                      values.brightnessMax,
                      0.05,
                      1.60,
                      155,
                      (value) => _fixed(value, 2),
                      (range) => onChanged(values.copyWith(
                          brightnessMin: range.start,
                          brightnessMax: range.end))),
                  _rangeSlider(
                      'Rotation speed',
                      values.rotationMin,
                      values.rotationMax,
                      -8.00,
                      8.00,
                      160,
                      (value) => '${_fixed(value, 2)} rad/s',
                      (range) => onChanged(values.copyWith(
                          rotationMin: range.start, rotationMax: range.end))),
                  _rangeSlider(
                      'Tip length',
                      values.tipLengthMin,
                      values.tipLengthMax,
                      0.50,
                      60.00,
                      238,
                      (value) => '${_fixed(value, 2)} x',
                      (range) => onChanged(values.copyWith(
                          tipLengthMin: range.start, tipLengthMax: range.end))),
                  _rangeSlider(
                      'Center radius',
                      values.centerRadiusMin,
                      values.centerRadiusMax,
                      0.15,
                      3.00,
                      114,
                      (value) => '${_fixed(value, 2)} x',
                      (range) => onChanged(values.copyWith(
                          centerRadiusMin: range.start,
                          centerRadiusMax: range.end))),
                  _singleSlider(
                      'Accent mix',
                      values.accentMix,
                      0.00,
                      1.00,
                      100,
                      _percent(values.accentMix),
                      (value) => onChanged(values.copyWith(accentMix: value))),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _singleSlider(String label, double value, double min, double max,
      int divisions, String display, ValueChanged<double> onChanged) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Row(children: <Widget>[
            Expanded(
                child: Text(label,
                    style: const TextStyle(
                        color: Color(0xffeedfc4),
                        fontWeight: FontWeight.bold,
                        fontSize: 12))),
            Text(display,
                style: const TextStyle(
                    color: Color(0xff80caff),
                    fontSize: 12,
                    fontFeatures: <ui.FontFeature>[
                      ui.FontFeature.tabularFigures()
                    ])),
          ]),
          Slider(
              value: value.clamp(min, max).toDouble(),
              min: min,
              max: max,
              divisions: divisions,
              onChanged: onChanged),
        ],
      ),
    );
  }

  Widget _rangeSlider(
      String label,
      double start,
      double end,
      double min,
      double max,
      int divisions,
      String Function(double value) format,
      ValueChanged<RangeValues> onChanged) {
    final normalizedStart = start.clamp(min, max).toDouble();
    final normalizedEnd = end.clamp(min, max).toDouble();
    final range = RangeValues(math.min(normalizedStart, normalizedEnd),
        math.max(normalizedStart, normalizedEnd));
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Row(children: <Widget>[
            Expanded(
                child: Text(label,
                    style: const TextStyle(
                        color: Color(0xffeedfc4),
                        fontWeight: FontWeight.bold,
                        fontSize: 12))),
            Text('${format(range.start)} - ${format(range.end)}',
                style: const TextStyle(
                    color: Color(0xff80caff),
                    fontSize: 12,
                    fontFeatures: <ui.FontFeature>[
                      ui.FontFeature.tabularFigures()
                    ])),
          ]),
          RangeSlider(
              values: range,
              min: min,
              max: max,
              divisions: divisions,
              labels: RangeLabels(format(range.start), format(range.end)),
              onChanged: onChanged),
        ],
      ),
    );
  }
}

class _PatreonModeTuningPanel extends StatelessWidget {
  const _PatreonModeTuningPanel(
      {required this.title, required this.values, required this.onChanged});

  final String title;
  final PatreonFireMode values;
  final ValueChanged<PatreonFireMode> onChanged;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
          color: const Color(0xff101315),
          border: Border.all(color: const Color(0xff513719)),
          borderRadius: BorderRadius.circular(6)),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Text(title,
                style: const TextStyle(
                    color: Color(0xfff7d897),
                    fontFamily: 'Georgia',
                    fontSize: 17,
                    fontWeight: FontWeight.bold)),
            const SizedBox(height: 8),
            Expanded(
              child: ListView(
                children: <Widget>[
                  _singleSlider(
                      'Heart X',
                      values.heartX,
                      0.05,
                      0.40,
                      140,
                      _percent(values.heartX),
                      (value) => onChanged(values.copyWith(heartX: value))),
                  _singleSlider(
                      'Heart Y',
                      values.heartY,
                      0.20,
                      0.80,
                      120,
                      _percent(values.heartY),
                      (value) => onChanged(values.copyWith(heartY: value))),
                  _singleSlider(
                      'Heart size',
                      values.heartSize,
                      0.25,
                      1.35,
                      110,
                      _fixed(values.heartSize, 2),
                      (value) => onChanged(values.copyWith(heartSize: value))),
                  _singleSlider(
                      'Heart glow',
                      values.heartGlow,
                      0.00,
                      1.80,
                      180,
                      _fixed(values.heartGlow, 2),
                      (value) => onChanged(values.copyWith(heartGlow: value))),
                  _singleSlider(
                      'Heart bloom',
                      values.heartBloom,
                      0.00,
                      2.20,
                      220,
                      _fixed(values.heartBloom, 2),
                      (value) => onChanged(values.copyWith(heartBloom: value))),
                  _singleSlider(
                      'Heart flame width',
                      values.flameWidth,
                      0.20,
                      5.00,
                      240,
                      _fixed(values.flameWidth, 2),
                      (value) => onChanged(values.copyWith(flameWidth: value))),
                  _singleSlider(
                      'Heart flame origin Y',
                      values.flameOriginY,
                      0.05,
                      1.80,
                      350,
                      _percent(values.flameOriginY),
                      (value) =>
                          onChanged(values.copyWith(flameOriginY: value))),
                  _singleSlider(
                      'Heart flame height',
                      values.flameHeight,
                      0.10,
                      20.00,
                      398,
                      _fixed(values.flameHeight, 2),
                      (value) =>
                          onChanged(values.copyWith(flameHeight: value))),
                  _singleSlider(
                      'Heart flame power',
                      values.flameIntensity,
                      0.00,
                      4.00,
                      400,
                      _fixed(values.flameIntensity, 2),
                      (value) =>
                          onChanged(values.copyWith(flameIntensity: value))),
                  _singleSlider(
                      'Edge flame height',
                      values.edgeFlameHeight,
                      0,
                      280,
                      280,
                      '${_fixed(values.edgeFlameHeight, 1)} px',
                      (value) =>
                          onChanged(values.copyWith(edgeFlameHeight: value))),
                  _singleSlider(
                      'Edge flame Y',
                      values.edgeFlameY,
                      -2.00,
                      2.00,
                      400,
                      _percent(values.edgeFlameY),
                      (value) => onChanged(values.copyWith(edgeFlameY: value))),
                  _singleSlider(
                      'Edge flame power',
                      values.edgeFlameIntensity,
                      0.00,
                      4.00,
                      400,
                      _fixed(values.edgeFlameIntensity, 2),
                      (value) => onChanged(
                          values.copyWith(edgeFlameIntensity: value))),
                  _singleSlider(
                      'Side flame width',
                      values.sideFlameWidth,
                      0.02,
                      5.00,
                      498,
                      _fixed(values.sideFlameWidth, 2),
                      (value) =>
                          onChanged(values.copyWith(sideFlameWidth: value))),
                  _singleSlider(
                      'Side flame X distance',
                      values.sideFlameX,
                      0.00,
                      0.75,
                      300,
                      _percent(values.sideFlameX),
                      (value) => onChanged(values.copyWith(sideFlameX: value))),
                  _singleSlider(
                      'Side flame origin Y',
                      values.sideFlameOriginY,
                      0.05,
                      1.80,
                      350,
                      _percent(values.sideFlameOriginY),
                      (value) =>
                          onChanged(values.copyWith(sideFlameOriginY: value))),
                  _singleSlider(
                      'Side flame height',
                      values.sideFlameHeight,
                      0.10,
                      20.00,
                      398,
                      _fixed(values.sideFlameHeight, 2),
                      (value) =>
                          onChanged(values.copyWith(sideFlameHeight: value))),
                  _singleSlider(
                      'Side flame power',
                      values.sideFlameIntensity,
                      0.00,
                      4.00,
                      400,
                      _fixed(values.sideFlameIntensity, 2),
                      (value) => onChanged(
                          values.copyWith(sideFlameIntensity: value))),
                  _singleSlider(
                      'Spark rate',
                      values.sparkSpawnRate,
                      0,
                      90,
                      90,
                      '${_fixed(values.sparkSpawnRate, 0)} /s',
                      (value) =>
                          onChanged(values.copyWith(sparkSpawnRate: value))),
                  _rangeSlider(
                      'Spark speed',
                      values.sparkSpeedMin,
                      values.sparkSpeedMax,
                      1,
                      320,
                      319,
                      (value) => '${_fixed(value, 0)} px/s',
                      (range) => onChanged(values.copyWith(
                          sparkSpeedMin: range.start,
                          sparkSpeedMax: range.end))),
                  _rangeSlider(
                      'Spark size',
                      values.sparkSizeMin,
                      values.sparkSizeMax,
                      0.20,
                      6.00,
                      116,
                      (value) => '${_fixed(value, 2)} px',
                      (range) => onChanged(values.copyWith(
                          sparkSizeMin: range.start, sparkSizeMax: range.end))),
                  _singleSlider(
                      'Spark spread',
                      values.sparkSpread,
                      0.00,
                      2.20,
                      220,
                      _fixed(values.sparkSpread, 2),
                      (value) =>
                          onChanged(values.copyWith(sparkSpread: value))),
                  _singleSlider(
                      'Spark origin Y',
                      values.sparkOriginY,
                      -0.60,
                      5.00,
                      560,
                      _percent(values.sparkOriginY),
                      (value) =>
                          onChanged(values.copyWith(sparkOriginY: value))),
                  _singleSlider(
                      'Spark bottom crop',
                      values.sparkBottomCrop,
                      0.00,
                      0.85,
                      170,
                      _percent(values.sparkBottomCrop),
                      (value) =>
                          onChanged(values.copyWith(sparkBottomCrop: value))),
                  _singleSlider(
                      'Spark opacity',
                      values.sparkOpacity,
                      0.00,
                      1.50,
                      150,
                      _percent(values.sparkOpacity),
                      (value) =>
                          onChanged(values.copyWith(sparkOpacity: value))),
                  _singleSlider(
                      'Spark motion X',
                      values.sparkMotionX,
                      -2.00,
                      2.00,
                      400,
                      _fixed(values.sparkMotionX, 2),
                      (value) =>
                          onChanged(values.copyWith(sparkMotionX: value))),
                  _singleSlider(
                      'Spark motion Y',
                      values.sparkMotionY,
                      -3.00,
                      1.00,
                      400,
                      _fixed(values.sparkMotionY, 2),
                      (value) =>
                          onChanged(values.copyWith(sparkMotionY: value))),
                  _singleSlider(
                      'Spark smoke',
                      values.sparkSmoke,
                      0.00,
                      2.00,
                      200,
                      _fixed(values.sparkSmoke, 2),
                      (value) => onChanged(values.copyWith(sparkSmoke: value))),
                  _singleSlider(
                      'Spark bloom',
                      values.sparkBloom,
                      0.00,
                      3.00,
                      300,
                      _fixed(values.sparkBloom, 2),
                      (value) => onChanged(values.copyWith(sparkBloom: value))),
                  _singleSlider(
                      'Spark layer size',
                      values.sparkLayerSize,
                      0.80,
                      1.60,
                      160,
                      _fixed(values.sparkLayerSize, 2),
                      (value) =>
                          onChanged(values.copyWith(sparkLayerSize: value))),
                  _singleSlider(
                      'Spark layer alpha',
                      values.sparkLayerAlpha,
                      0.35,
                      0.98,
                      126,
                      _fixed(values.sparkLayerAlpha, 2),
                      (value) =>
                          onChanged(values.copyWith(sparkLayerAlpha: value))),
                  _singleSlider(
                      'Spark layers',
                      values.sparkLayers,
                      1.00,
                      24.00,
                      23,
                      _fixed(values.sparkLayers, 0),
                      (value) => onChanged(
                          values.copyWith(sparkLayers: value.roundToDouble()))),
                  _singleSlider(
                      'Button glow',
                      values.buttonGlow,
                      0.00,
                      1.20,
                      120,
                      _fixed(values.buttonGlow, 2),
                      (value) => onChanged(values.copyWith(buttonGlow: value))),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _singleSlider(String label, double value, double min, double max,
      int divisions, String display, ValueChanged<double> onChanged) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Row(children: <Widget>[
            Expanded(
                child: Text(label,
                    style: const TextStyle(
                        color: Color(0xffeedfc4),
                        fontWeight: FontWeight.bold,
                        fontSize: 12))),
            Text(display,
                style: const TextStyle(
                    color: Color(0xff80caff),
                    fontSize: 12,
                    fontFeatures: <ui.FontFeature>[
                      ui.FontFeature.tabularFigures()
                    ])),
          ]),
          Slider(
              value: value.clamp(min, max).toDouble(),
              min: min,
              max: max,
              divisions: divisions,
              onChanged: onChanged),
        ],
      ),
    );
  }

  Widget _rangeSlider(
      String label,
      double start,
      double end,
      double min,
      double max,
      int divisions,
      String Function(double value) format,
      ValueChanged<RangeValues> onChanged) {
    final normalizedStart = start.clamp(min, max).toDouble();
    final normalizedEnd = end.clamp(min, max).toDouble();
    final range = RangeValues(math.min(normalizedStart, normalizedEnd),
        math.max(normalizedStart, normalizedEnd));
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: <Widget>[
          Row(children: <Widget>[
            Expanded(
                child: Text(label,
                    style: const TextStyle(
                        color: Color(0xffeedfc4),
                        fontWeight: FontWeight.bold,
                        fontSize: 12))),
            Text('${format(range.start)} - ${format(range.end)}',
                style: const TextStyle(
                    color: Color(0xff80caff),
                    fontSize: 12,
                    fontFeatures: <ui.FontFeature>[
                      ui.FontFeature.tabularFigures()
                    ])),
          ]),
          RangeSlider(
              values: range,
              min: min,
              max: max,
              divisions: divisions,
              labels: RangeLabels(format(range.start), format(range.end)),
              onChanged: onChanged),
        ],
      ),
    );
  }
}

String _fixed(double value, int digits) => value.toStringAsFixed(digits);

String _percent(double value) => '${(value * 100).toStringAsFixed(0)}%';

String _safeTelemetryText(String? value, int max) {
  if (value == null) return '';
  final safe = value.replaceAll('\u0000', '');
  if (safe.length > max) return '${safe.substring(0, max)}...';
  return safe;
}

void _addOptionalTelemetry(
    Map<String, Object> properties, String key, String? value, int max) {
  final safe = _safeTelemetryText(value, max).trim();
  if (safe.isNotEmpty) properties[key] = safe;
}

Future<void> _sendInstallerStartupTelemetry({
  required AppLocaleSelection localeSelection,
  required String startupSurface,
}) async {
  await _sendInstallerTelemetryEvent(
    eventName: AppConstants.telemetryEventInstallerStarted,
    localeSelection: localeSelection,
    startupSurface: startupSurface,
  );
  await _sendInstallerTelemetryEvent(
    eventName: AppConstants.telemetryEventInstallerLanguageResolved,
    localeSelection: localeSelection,
    startupSurface: startupSurface,
  );
}

Future<void> _sendInstallerTelemetryEvent({
  required String eventName,
  required AppLocaleSelection localeSelection,
  required String startupSurface,
}) async {
  try {
    final safeVersion = _safeTelemetryText(AppConstants.patchVersion, 100);
    final properties = <String, Object>{
      'distinct_id': await _patchTelemetryDistinctId(),
      r'$process_person_profile': false,
      'patch_name': 'Community Patch',
      'patch_version': safeVersion,
      'app_surface': startupSurface,
      'locale': localeSelection.resolvedLocaleTag,
      'locale_country': localeSelection.language.countryName,
      'locale_source': localeSelection.source,
      'system_locale': localeSelection.systemLocaleTag,
      'os': _safeTelemetryText(Platform.operatingSystemVersion, 200),
    };
    _addOptionalTelemetry(
        properties, 'requested_locale', localeSelection.requestedLocale, 80);

    final payload = <String, Object>{
      'api_key': AppConstants.postHogApiKey,
      'event': eventName,
      'properties': properties,
    };

    final client = HttpClient()
      ..connectionTimeout = const Duration(milliseconds: 1200);
    try {
      final request =
          await client.postUrl(Uri.parse(AppConstants.postHogEndpoint));
      request.headers.contentType = ContentType.json;
      request.headers.set(
          HttpHeaders.userAgentHeader, 'MagickaPatchInstaller/$safeVersion');
      request.write(jsonEncode(payload));
      final response =
          await request.close().timeout(const Duration(milliseconds: 1500));
      await response.drain();
    } finally {
      client.close(force: true);
    }
  } catch (_) {}
}

Future<void> _sendPatchTelemetryEvent({
  required String eventName,
  required String gameDir,
  required String patchVersion,
  Map<String, Object>? properties,
}) async {
  try {
    if (!await _isUsageSharingEnabled(gameDir)) return;

    final safeVersion = _safeTelemetryText(patchVersion, 100);
    final eventProperties = <String, Object>{
      'distinct_id': await _patchTelemetryDistinctId(),
      r'$process_person_profile': false,
      'patch_name': 'Community Patch',
      'patch_version': safeVersion,
      'game_version': '',
      'os': _safeTelemetryText(Platform.operatingSystemVersion, 200),
    };
    if (properties != null) {
      eventProperties.addAll(properties);
    }

    final payload = <String, Object>{
      'api_key': AppConstants.postHogApiKey,
      'event': eventName,
      'properties': eventProperties,
    };

    final client = HttpClient()
      ..connectionTimeout = const Duration(milliseconds: 1200);
    try {
      final request =
          await client.postUrl(Uri.parse(AppConstants.postHogEndpoint));
      request.headers.contentType = ContentType.json;
      request.headers.set(
          HttpHeaders.userAgentHeader, 'MagickaPatchTelemetry/$safeVersion');
      request.write(jsonEncode(payload));
      final response =
          await request.close().timeout(const Duration(milliseconds: 1500));
      await response.drain();
    } finally {
      client.close(force: true);
    }
  } catch (_) {}
}

Future<bool> _isUsageSharingEnabled(String gameDir) async {
  if (gameDir.trim().isEmpty) return false;
  final settingsPath = _join(gameDir, AppConstants.settingsDirectoryName,
      AppConstants.settingsFileName);
  if (!await File(settingsPath).exists()) return true;
  final values = await _readIniFile(settingsPath);
  if (!values.containsKey('usage_sharing')) return true;
  return _parseBool(values['usage_sharing']);
}

Future<String> _patchTelemetryDistinctId() async {
  final baseDir = _join(
      Platform.environment['APPDATA'] ?? Directory.systemTemp.path,
      'MagickaPatch');
  final idFile = File(_join(baseDir, 'telemetry_id.txt'));
  try {
    await Directory(baseDir).create(recursive: true);
    if (await idFile.exists()) {
      final existing = (await idFile.readAsString()).trim();
      if (existing.isNotEmpty) return existing;
    }
    final id = _newGuidLikeTelemetryId();
    await idFile.writeAsString(id, flush: true);
    return id;
  } catch (_) {
    return 'ephemeral_${_newGuidLikeTelemetryId()}';
  }
}

String _newGuidLikeTelemetryId() {
  try {
    final random = math.Random.secure();
    return List<String>.generate(
      16,
      (_) => random.nextInt(256).toRadixString(16).padLeft(2, '0'),
    ).join();
  } catch (_) {
    return _newTelemetryId().replaceAll('_', '');
  }
}

bool _parseBool(String? value) {
  final normalized = (value ?? '').trim().toLowerCase();
  return normalized == 'true' || normalized == 'yes' || normalized == '1';
}

String _normalizeEventPart(String? value) {
  final raw = (value == null || value.trim().isEmpty) ? 'unknown' : value;
  final buffer = StringBuffer();
  for (final code in raw.toLowerCase().codeUnits) {
    final isLetter = code >= 97 && code <= 122;
    final isDigit = code >= 48 && code <= 57;
    buffer.write(isLetter || isDigit ? String.fromCharCode(code) : '_');
  }
  final normalized = buffer
      .toString()
      .replaceAll(RegExp(r'_+'), '_')
      .replaceAll(RegExp(r'^_|_$'), '');
  return normalized.isEmpty ? 'unknown' : normalized;
}

String _newTelemetryId() {
  final random = math.Random();
  final time = DateTime.now().microsecondsSinceEpoch.toRadixString(16);
  final suffix = random.nextInt(0x7fffffff).toRadixString(16);
  return '${time}_$suffix';
}

String _join(String a, String b, [String? c]) {
  return joinPathForPlatform(
    a,
    b,
    c: c,
    windowsPaths: Platform.isWindows,
  );
}

void _addUniquePath(List<String> paths, String path) {
  _addUniquePathForPlatform(paths, path, windowsPaths: Platform.isWindows);
}

String joinPathForPlatform(
  String a,
  String b, {
  String? c,
  required bool windowsPaths,
}) {
  final separator = windowsPaths ? r'\' : '/';
  String normalize(String value) =>
      value.trim().replaceAll(windowsPaths ? '/' : r'\', separator);
  String trimEnd(String value) {
    while (value.length > 1 && value.endsWith(separator)) {
      value = value.substring(0, value.length - 1);
    }
    return value;
  }

  String trimStart(String value) {
    while (value.startsWith(separator)) {
      value = value.substring(1);
    }
    return value;
  }

  final first = trimEnd(normalize(a));
  final second = trimStart(normalize(b));
  final joined = '$first$separator$second';
  if (c == null) return joined;
  return joinPathForPlatform(
    joined,
    c,
    windowsPaths: windowsPaths,
  );
}

List<String> linuxSteamDirectoryCandidates(Map<String, String> environment) {
  final paths = <String>[];
  void add(String path) {
    _addUniquePathForPlatform(paths, path, windowsPaths: false);
  }

  final home = environment['HOME']?.trim() ?? '';
  final xdgDataHome = environment['XDG_DATA_HOME']?.trim() ?? '';
  final compatClient =
      environment['STEAM_COMPAT_CLIENT_INSTALL_PATH']?.trim() ?? '';
  if (compatClient.isNotEmpty) add(compatClient);
  if (xdgDataHome.isNotEmpty) {
    add(joinPathForPlatform(
      xdgDataHome,
      'Steam',
      windowsPaths: false,
    ));
  }
  if (home.isNotEmpty) {
    add(joinPathForPlatform(
      home,
      '.local/share/Steam',
      windowsPaths: false,
    ));
    add(joinPathForPlatform(
      home,
      '.steam/steam',
      windowsPaths: false,
    ));
    add(joinPathForPlatform(
      home,
      '.steam/root',
      windowsPaths: false,
    ));
    add(joinPathForPlatform(
      home,
      '.var/app/com.valvesoftware.Steam/.local/share/Steam',
      windowsPaths: false,
    ));
    add(joinPathForPlatform(
      home,
      'snap/steam/common/.local/share/Steam',
      windowsPaths: false,
    ));
  }
  return paths;
}

Future<String?> findSteamAppDirectory({
  required List<String> steamDirectories,
  required String appId,
  required String fallbackInstallDirectory,
  required bool windowsPaths,
  required bool Function(String path) isValidDirectory,
}) async {
  final libraryDirectories = <String>[];
  for (final steamDirectory in steamDirectories) {
    _addUniquePathForPlatform(
      libraryDirectories,
      steamDirectory,
      windowsPaths: windowsPaths,
    );
    final libraryFile = File(joinPathForPlatform(
      steamDirectory,
      r'steamapps\libraryfolders.vdf',
      windowsPaths: windowsPaths,
    ));
    if (!await libraryFile.exists()) continue;
    try {
      final values = _readValveKeyValues(await libraryFile.readAsString());
      for (final path in values['path'] ?? const <String>[]) {
        _addUniquePathForPlatform(
          libraryDirectories,
          path,
          windowsPaths: windowsPaths,
        );
      }
      values.forEach((key, paths) {
        if (int.tryParse(key) == null) return;
        for (final path in paths) {
          if (path.contains(r'\') || path.contains('/')) {
            _addUniquePathForPlatform(
              libraryDirectories,
              path,
              windowsPaths: windowsPaths,
            );
          }
        }
      });
    } catch (_) {}
  }

  for (final libraryDirectory in libraryDirectories) {
    final manifest = File(joinPathForPlatform(
      libraryDirectory,
      'steamapps',
      c: 'appmanifest_$appId.acf',
      windowsPaths: windowsPaths,
    ));
    if (await manifest.exists()) {
      try {
        final values = _readValveKeyValues(await manifest.readAsString());
        for (final installDirectory
            in values['installdir'] ?? const <String>[]) {
          final candidate = joinPathForPlatform(
            libraryDirectory,
            'steamapps/common',
            c: installDirectory,
            windowsPaths: windowsPaths,
          );
          if (isValidDirectory(candidate)) return candidate;
        }
      } catch (_) {}
    }

    final candidate = joinPathForPlatform(
      libraryDirectory,
      'steamapps/common',
      c: fallbackInstallDirectory,
      windowsPaths: windowsPaths,
    );
    if (isValidDirectory(candidate)) return candidate;
  }
  return null;
}

void _addUniquePathForPlatform(
  List<String> paths,
  String path, {
  required bool windowsPaths,
}) {
  final normalized = path
      .trim()
      .replaceAll(windowsPaths ? '/' : r'\', windowsPaths ? r'\' : '/');
  if (normalized.isEmpty) return;
  final comparable = windowsPaths ? normalized.toLowerCase() : normalized;
  for (final existing in paths) {
    final existingComparable = windowsPaths ? existing.toLowerCase() : existing;
    if (existingComparable == comparable) return;
  }
  paths.add(normalized);
}

Map<String, List<String>> _readValveKeyValues(String text) {
  final values = <String, List<String>>{};
  final pattern = RegExp(r'"([^"]+)"\s*"((?:\\.|[^"])*)"');
  for (final match in pattern.allMatches(text)) {
    final key = _decodeValveString(match.group(1)!).toLowerCase();
    final value = _decodeValveString(match.group(2)!);
    values.putIfAbsent(key, () => <String>[]).add(value);
  }
  return values;
}

String _decodeValveString(String value) =>
    value.replaceAll(r'\\', '\\').replaceAll(r'\"', '"').replaceAll(r'\/', '/');

String _safeFileName(String value) {
  if (value.trim().isEmpty) return 'unknown';
  return value.replaceAll(RegExp(r'[<>:"/\\|?*]'), '_').replaceAll('.', '_');
}

String _psQuote(String value) => "'${value.replaceAll("'", "''")}'";

Future<Map<String, String>> _readIniFile(String path) async {
  final file = File(path);
  if (!await file.exists()) return <String, String>{};
  final values = <String, String>{};
  try {
    final lines = await file.readAsLines();
    for (final raw in lines) {
      final line = raw.trim();
      if (line.isEmpty || line.startsWith('#') || line.startsWith('[')) {
        continue;
      }
      final index = line.indexOf('=');
      if (index <= 0) continue;
      values[line.substring(0, index).trim().toLowerCase()] =
          line.substring(index + 1).trim();
    }
  } catch (_) {}
  return values;
}

Future<bool> _magickaExeContainsPatchVersion(
    String gameDir, String version) async {
  if (gameDir.trim().isEmpty || version.trim().isEmpty) return false;
  try {
    final exe = File(_join(gameDir, 'Magicka.exe'));
    if (!await exe.exists()) return false;
    final bytes = await exe.readAsBytes();
    final needle = _utf16LeBytes(version);
    return _countBytePattern(bytes, needle) == 1;
  } catch (_) {
    return false;
  }
}

List<int> _utf16LeBytes(String value) {
  final bytes = <int>[];
  for (final codeUnit in value.codeUnits) {
    bytes.add(codeUnit & 0xff);
    bytes.add((codeUnit >> 8) & 0xff);
  }
  return bytes;
}

int _countBytePattern(List<int> haystack, List<int> needle) {
  if (needle.isEmpty || haystack.length < needle.length) return 0;
  var count = 0;
  for (var i = 0; i <= haystack.length - needle.length; i++) {
    var matched = true;
    for (var j = 0; j < needle.length; j++) {
      if (haystack[i + j] != needle[j]) {
        matched = false;
        break;
      }
    }
    if (matched) count++;
  }
  return count;
}

Future<void> _deleteIfExists(String path) async {
  try {
    final file = File(path);
    if (await file.exists()) await file.delete();
  } catch (_) {}
}

Future<OriginalBackupFiles?> _ensureVerifiedOriginalBackups(
  BuildContext context, {
  required String gameDirectory,
  required String backupDirectory,
  required Map<String, String> manifest,
  OriginalGameFileStore store = const OriginalGameFileStore(),
  Future<void> Function(String url)? openUrl,
}) async {
  Future<OriginalBackupFiles> resolve() => store.resolve(
        gameDirectory: gameDirectory,
        backupDirectory: backupDirectory,
        manifestMagickaPath: manifest['original_magicka_backup'],
        manifestPolygonHeadPath: manifest['original_polygonhead_backup'],
      );

  var backups = await resolve();
  if (backups.complete) return backups;
  if (!context.mounted) return null;

  final strings = AppStrings.of(context);
  final confirmed = await showDialog<bool>(
        context: context,
        barrierDismissible: false,
        builder: (dialogContext) => AlertDialog(
          backgroundColor: const Color(0xff101315),
          title: Text(strings.t('originalFilesRequiredTitle')),
          content: SizedBox(
            width: 520,
            child: Text(strings.t('originalFilesRequiredBody')),
          ),
          actions: <Widget>[
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: Text(strings.t('cancel')),
            ),
            FilledButton.icon(
              onPressed: () => Navigator.pop(dialogContext, true),
              icon: const Icon(Icons.verified_rounded, size: 18),
              label: Text(strings.t('validateWithSteam')),
            ),
          ],
        ),
      ) ??
      false;
  if (!confirmed) return null;

  await (openUrl ?? _openExternalUrl)(AppConstants.magickaSteamValidationUrl);
  var validationWasNotReady = false;
  while (context.mounted) {
    final checkAgain = await showDialog<bool>(
          context: context,
          barrierDismissible: false,
          builder: (dialogContext) => AlertDialog(
            backgroundColor: const Color(0xff101315),
            title: Text(strings.t(validationWasNotReady
                ? 'steamValidationNotReadyTitle'
                : 'steamValidationStartedTitle')),
            content: SizedBox(
              width: 520,
              child: Text(strings.t(validationWasNotReady
                  ? 'steamValidationNotReadyBody'
                  : 'steamValidationStartedBody')),
            ),
            actions: <Widget>[
              TextButton(
                onPressed: () => Navigator.pop(dialogContext, false),
                child: Text(strings.t('cancel')),
              ),
              FilledButton.icon(
                onPressed: () => Navigator.pop(dialogContext, true),
                icon: const Icon(Icons.refresh_rounded, size: 18),
                label: Text(strings.t('checkOriginalFilesAgain')),
              ),
            ],
          ),
        ) ??
        false;
    if (!checkAgain) return null;

    backups = await resolve();
    if (backups.complete) return backups;
    validationWasNotReady = true;
  }
  return null;
}

Future<void> _copyDirectory(Directory source, Directory destination) async {
  await destination.create(recursive: true);
  await for (final entity
      in source.list(recursive: false, followLinks: false)) {
    final parts = entity.path
        .split(RegExp(r'[\\/]+'))
        .where((part) => part.isNotEmpty)
        .toList();
    final name = parts.isEmpty ? '' : parts.last;
    if (name.isEmpty) continue;
    final targetPath = _join(destination.path, name);
    if (entity is Directory) {
      await _copyDirectory(entity, Directory(targetPath));
    } else if (entity is File) {
      await entity.copy(targetPath);
    }
  }
}

Future<void> _showStartGameDialog(
  BuildContext context,
  String gameDir,
  String message, {
  ui.FragmentProgram? flameProgram,
  ui.FragmentProgram? starProgram,
}) async {
  final start = await showDialog<bool>(
        context: context,
        builder: (context) => _StartGameDialog(
          message: message,
          flameProgram: flameProgram,
          starProgram: starProgram,
        ),
      ) ??
      false;

  if (!start) return;
  try {
    await _startMagickaWithPrerequisites(context, gameDir);
  } catch (error) {
    if (!context.mounted) return;
    final s = AppStrings.of(context);
    await showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        backgroundColor: const Color(0xff101315),
        title: Text(s.t('appTitle')),
        content: Text(s.couldNotStartMagicka(error)),
        actions: <Widget>[
          TextButton(
              onPressed: () => Navigator.pop(context), child: Text(s.t('ok')))
        ],
      ),
    );
  }
}

class _StartGameDialog extends StatelessWidget {
  const _StartGameDialog({
    required this.message,
    required this.flameProgram,
    required this.starProgram,
  });

  final String message;
  final ui.FragmentProgram? flameProgram;
  final ui.FragmentProgram? starProgram;

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final media = MediaQuery.sizeOf(context);
    final dialogWidth = math.min(math.max(520.0, media.width - 64), 760.0);

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.all(32),
      child: SizedBox(
        width: dialogWidth,
        child: ArcanePanel(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(28, 24, 28, 26),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    const SizedBox(
                      width: 58,
                      height: 58,
                      child: ArcaneIconBadge(
                        icon: Icons.verified_rounded,
                        accent: Color(0xff3f9fff),
                      ),
                    ),
                    const SizedBox(width: 18),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: <Widget>[
                          Text(
                            s.t('appTitle'),
                            style: TextStyle(
                              color: Color(0xfff7d897),
                              fontFamily: 'Georgia',
                              fontSize: 28,
                              fontWeight: FontWeight.bold,
                              shadows: <Shadow>[
                                Shadow(color: Colors.black, blurRadius: 3)
                              ],
                            ),
                          ),
                          const SizedBox(height: 5),
                          Text(
                            message,
                            style: const TextStyle(
                              color: Color(0xffeedfc4),
                              fontSize: 16,
                              height: 1.25,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 22),
                SectionHeading(text: s.t('readyToPlay')),
                const SizedBox(height: 18),
                SizedBox(
                  height: 104,
                  child: Stack(
                    fit: StackFit.expand,
                    children: <Widget>[
                      const ArcaneCardSurface(accent: Color(0xff80caff)),
                      const Positioned(
                        left: 20,
                        top: 20,
                        width: 42,
                        height: 42,
                        child: ArcaneIconBadge(
                          icon: Icons.play_arrow_rounded,
                          accent: Color(0xff80caff),
                        ),
                      ),
                      Positioned(
                        left: 78,
                        top: 20,
                        right: 20,
                        child: Text(
                          s.t('startMagickaNow'),
                          style: const TextStyle(
                            color: Color(0xfff7d897),
                            fontFamily: 'Georgia',
                            fontSize: 20,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                      ),
                      Positioned(
                        left: 78,
                        top: 52,
                        right: 22,
                        child: Text(
                          s.t('startMagickaBody'),
                          style: const TextStyle(
                            color: Color(0xffeedfc4),
                            fontSize: 14,
                            height: 1.25,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 24),
                Row(
                  children: <Widget>[
                    SizedBox(
                      width: 210,
                      height: 44,
                      child: FlameButton(
                        program: flameProgram,
                        label: s.t('close'),
                        icon: Icons.close,
                        accent: const Color(0xffd03f30),
                        overlayIcon: true,
                        onTap: () => Navigator.pop(context, false),
                      ),
                    ),
                    const Spacer(),
                    SizedBox(
                      width: 274,
                      height: 44,
                      child: FlameButton(
                        program: starProgram,
                        label: s.t('startGame'),
                        icon: Icons.play_arrow_rounded,
                        accent: const Color(0xff3f9fff),
                        starField: true,
                        overlayIcon: true,
                        intensity: 1.0,
                        onTap: () => Navigator.pop(context, true),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

Future<bool> _startMagickaWithPrerequisites(
    BuildContext context, String gameDir) async {
  final ready = await _ensureManagedDirectInput(context, gameDir);
  if (!ready) return false;

  await _startMagicka(gameDir);
  return true;
}

Future<bool> _ensureManagedDirectInput(
    BuildContext context, String gameDir) async {
  final s = AppStrings.of(context);
  if (await _isManagedDirectInputInstalled()) {
    await _sendDirectXTelemetryEvent(
      eventName: AppConstants.telemetryEventDirectXAlreadyInstalled,
      gameDir: gameDir,
      installedBefore: true,
    );
    return true;
  }

  final directXSetup = _findMagickaDirectXSetup(gameDir);

  if (directXSetup == null) {
    await _sendDirectXTelemetryEvent(
      eventName: AppConstants.telemetryEventDirectXSetupMissing,
      gameDir: gameDir,
      installedBefore: false,
      reason: 'setup_not_found',
    );

    if (!context.mounted) return false;

    await showDialog<bool>(
      context: context,
      builder: (context) => _DirectXRedistDialog(
        title: s.t('directXMissingTitle'),
        heading: s.t('directXUnavailableHeading'),
        body: s.t('directXUnavailableBody'),
        cardTitle: s.t('directXInstallerNotFound'),
        cardBody: s.t('directXInstallerNotFoundBody'),
        icon: Icons.extension_off_rounded,
        accent: const Color(0xffd03f30),
        primaryLabel: s.t('close'),
        primaryIcon: Icons.close_rounded,
      ),
    );

    return false;
  }

  if (!context.mounted) return false;

  await _sendDirectXTelemetryEvent(
    eventName: AppConstants.telemetryEventDirectXInstallPromptShown,
    gameDir: gameDir,
    setupPath: directXSetup,
    installedBefore: false,
  );

  final install = await showDialog<bool>(
        context: context,
        barrierDismissible: false,
        builder: (context) => _DirectXRedistDialog(
          title: s.t('directXMissingTitle'),
          heading: s.t('directXInstallHeading'),
          body: s.t('directXInstallBody'),
          cardTitle: s.t('directXSetupFound'),
          cardBody: s.t('directXSetupFoundBody'),
          icon: Icons.system_update_alt_rounded,
          accent: const Color(0xff3f9fff),
          primaryLabel: s.t('installDirectX'),
          primaryIcon: Icons.system_update_alt_rounded,
          primaryResult: true,
          secondaryLabel: s.t('notNow'),
          secondaryIcon: Icons.close_rounded,
          secondaryResult: false,
        ),
      ) ??
      false;

  if (!install) {
    await _sendDirectXTelemetryEvent(
      eventName: AppConstants.telemetryEventDirectXInstallIgnored,
      gameDir: gameDir,
      setupPath: directXSetup,
      installedBefore: false,
      reason: 'user_declined',
    );
    return false;
  }

  await _sendDirectXTelemetryEvent(
    eventName: AppConstants.telemetryEventDirectXInstallStarted,
    gameDir: gameDir,
    setupPath: directXSetup,
    installedBefore: false,
  );

  int? setupExitCode;
  var setupProcessFailed = false;
  try {
    setupExitCode = await _runDirectXSetupElevated(directXSetup);
  } catch (_) {
    setupProcessFailed = true;
  }

  // Give Windows and the Global Assembly Cache a brief moment to finish.
  await Future<void>.delayed(const Duration(milliseconds: 400));

  if (await _isManagedDirectInputInstalled()) {
    await _sendDirectXTelemetryEvent(
      eventName: AppConstants.telemetryEventDirectXInstallSucceeded,
      gameDir: gameDir,
      setupPath: directXSetup,
      setupExitCode: setupExitCode,
      installedBefore: false,
      installedAfter: true,
    );
    return true;
  }

  await _sendDirectXTelemetryEvent(
    eventName: AppConstants.telemetryEventDirectXInstallFailed,
    gameDir: gameDir,
    setupPath: directXSetup,
    setupExitCode: setupExitCode,
    installedBefore: false,
    installedAfter: false,
    reason: setupProcessFailed
        ? 'setup_process_error'
        : setupExitCode == 1223
            ? 'elevation_cancelled_or_setup_blocked'
            : 'component_still_missing',
  );

  if (!context.mounted) return false;

  await showDialog<bool>(
    context: context,
    builder: (context) => _DirectXRedistDialog(
      title: s.t('directXIncompleteTitle'),
      heading: s.t('directXIncompleteHeading'),
      body: s.t('directXIncompleteBody'),
      cardTitle: s.t('directXInstallDidNotComplete'),
      cardBody: s.t('directXInstallDidNotCompleteBody'),
      icon: Icons.warning_rounded,
      accent: const Color(0xffd03f30),
      primaryLabel: s.t('close'),
      primaryIcon: Icons.close_rounded,
    ),
  );

  return false;
}

class _DirectXRedistDialog extends StatelessWidget {
  const _DirectXRedistDialog({
    required this.title,
    required this.heading,
    required this.body,
    required this.cardTitle,
    required this.cardBody,
    required this.icon,
    required this.accent,
    required this.primaryLabel,
    required this.primaryIcon,
    this.primaryResult = false,
    this.secondaryLabel,
    this.secondaryIcon,
    this.secondaryResult,
  });

  final String title;
  final String heading;
  final String body;
  final String cardTitle;
  final String cardBody;
  final IconData icon;
  final Color accent;
  final String primaryLabel;
  final IconData primaryIcon;
  final bool primaryResult;
  final String? secondaryLabel;
  final IconData? secondaryIcon;
  final bool? secondaryResult;

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final media = MediaQuery.sizeOf(context);
    final dialogWidth = math.min(math.max(640.0, media.width - 64), 820.0);

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.all(32),
      child: ConstrainedBox(
        constraints: BoxConstraints(maxHeight: media.height - 64),
        child: SingleChildScrollView(
          child: SizedBox(
            width: dialogWidth,
            child: ArcanePanel(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(30, 26, 30, 28),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                        SizedBox(
                          width: 64,
                          height: 64,
                          child: ArcaneIconBadge(icon: icon, accent: accent),
                        ),
                        const SizedBox(width: 20),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: <Widget>[
                              Text(
                                title,
                                style: const TextStyle(
                                  color: Color(0xfff7d897),
                                  fontFamily: 'Georgia',
                                  fontSize: 28,
                                  fontWeight: FontWeight.bold,
                                  shadows: <Shadow>[
                                    Shadow(color: Colors.black, blurRadius: 3)
                                  ],
                                ),
                              ),
                              const SizedBox(height: 7),
                              Text(
                                body,
                                style: const TextStyle(
                                  color: Color(0xffeedfc4),
                                  fontSize: 16,
                                  height: 1.3,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 24),
                    SectionHeading(text: s.t('directXSection')),
                    const SizedBox(height: 18),
                    SizedBox(
                      height: 174,
                      child: Stack(
                        fit: StackFit.expand,
                        children: <Widget>[
                          ArcaneCardSurface(accent: accent),
                          Positioned(
                            left: 20,
                            top: 22,
                            width: 44,
                            height: 44,
                            child: ArcaneIconBadge(icon: icon, accent: accent),
                          ),
                          Positioned(
                            left: 82,
                            top: 22,
                            right: 24,
                            child: Text(
                              heading,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                color: Color(0xfff7d897),
                                fontFamily: 'Georgia',
                                fontSize: 21,
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ),
                          Positioned(
                            left: 82,
                            top: 58,
                            right: 24,
                            child: Text(
                              cardTitle,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                color: Color.lerp(
                                    const Color(0xfff7d897), accent, 0.28),
                                fontSize: 14,
                                fontWeight: FontWeight.bold,
                                height: 1.25,
                              ),
                            ),
                          ),
                          Positioned(
                            left: 82,
                            top: 82,
                            right: 24,
                            bottom: 20,
                            child: Text(
                              cardBody,
                              maxLines: 4,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                color: Color(0xffeedfc4),
                                fontSize: 14,
                                height: 1.28,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 26),
                    Row(
                      children: <Widget>[
                        if (secondaryLabel != null)
                          SizedBox(
                            width: 210,
                            height: 44,
                            child: FlameButton(
                              program: null,
                              label: secondaryLabel!,
                              icon: secondaryIcon ?? Icons.close_rounded,
                              accent: const Color(0xffd03f30),
                              overlayIcon: true,
                              effects: false,
                              onTap: () =>
                                  Navigator.pop(context, secondaryResult),
                            ),
                          ),
                        if (secondaryLabel != null) const Spacer(),
                        SizedBox(
                          width: secondaryLabel == null ? 210 : 274,
                          height: 44,
                          child: FlameButton(
                            program: null,
                            label: primaryLabel,
                            icon: primaryIcon,
                            accent: accent,
                            overlayIcon: true,
                            effects: false,
                            onTap: () => Navigator.pop(context, primaryResult),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

String? _findMagickaDirectXSetup(String gameDir) {
  final candidates = <String>[
    _join(gameDir, r'Dependencies\directx_feb2010\DXSETUP.exe'),
    _join(gameDir, r'_CommonRedist\DirectX\Jun2010\DXSETUP.exe'),
  ];

  for (final candidate in candidates) {
    if (File(candidate).existsSync()) {
      return candidate;
    }
  }

  return null;
}

Future<void> _sendDirectXTelemetryEvent({
  required String eventName,
  required String gameDir,
  String? setupPath,
  int? setupExitCode,
  bool? installedBefore,
  bool? installedAfter,
  String? reason,
}) async {
  final properties = <String, Object>{
    'directx_component': 'Managed DirectX 1.1',
    'directx_assembly': 'Microsoft.DirectX.DirectInput',
  };
  if (setupPath != null) {
    properties['directx_setup_source'] = _directXSetupSource(setupPath);
  }
  if (setupExitCode != null) {
    properties['directx_setup_exit_code'] = setupExitCode;
  }
  if (installedBefore != null) {
    properties['directx_installed_before'] = installedBefore;
  }
  if (installedAfter != null) {
    properties['directx_installed_after'] = installedAfter;
  }
  _addOptionalTelemetry(properties, 'directx_reason', reason, 120);

  await _sendPatchTelemetryEvent(
    eventName: eventName,
    gameDir: gameDir,
    patchVersion: AppConstants.patchVersion,
    properties: properties,
  );
}

String _directXSetupSource(String setupPath) {
  final normalized = setupPath.replaceAll('/', r'\').toLowerCase();
  if (normalized.contains(r'\dependencies\directx_feb2010\dxsetup.exe')) {
    return 'dependencies_directx_feb2010';
  }
  if (normalized.contains(r'\_commonredist\directx\jun2010\dxsetup.exe')) {
    return 'steam_common_redist_directx_jun2010';
  }
  return 'unknown';
}

Future<int> _runDirectXSetupElevated(String setupPath) async {
  final workingDirectory = File(setupPath).parent.path;
  final command = '''
try {
  \$process = Start-Process `
    -FilePath ${_psQuote(setupPath)} `
    -WorkingDirectory ${_psQuote(workingDirectory)} `
    -Verb RunAs `
    -Wait `
    -PassThru

  exit \$process.ExitCode
}
catch {
  exit 1223
}
''';

  final result = await Process.run(
    'powershell',
    <String>[
      '-NoProfile',
      '-ExecutionPolicy',
      'Bypass',
      '-WindowStyle',
      'Hidden',
      '-Command',
      command,
    ],
  );

  // The actual success criterion is the GAC check performed afterwards.
  // This also handles unusual but harmless installer exit codes.
  if (result.exitCode == 1223) {
    return result.exitCode;
  }
  return result.exitCode;
}

Future<bool> _isManagedDirectInputInstalled() async {
  final windowsDirectory = Platform.environment['WINDIR'] ?? r'C:\Windows';

  const assemblyName = 'Microsoft.DirectX.DirectInput';
  const assemblyFileName = 'Microsoft.DirectX.DirectInput.dll';
  const requiredVersion = '1.0.2902.0';
  const publicKeyToken = '31bf3856ad364e35';

  final gacRoots = <String>[
    _join(windowsDirectory, r'assembly\GAC'),
    _join(windowsDirectory, r'assembly\GAC_32'),
    _join(windowsDirectory, r'assembly\GAC_MSIL'),
    _join(windowsDirectory, r'Microsoft.NET\assembly\GAC_32'),
    _join(windowsDirectory, r'Microsoft.NET\assembly\GAC_MSIL'),
  ];

  for (final gacRoot in gacRoots) {
    final assemblyDirectory = Directory(_join(gacRoot, assemblyName));

    try {
      if (!await assemblyDirectory.exists()) continue;

      await for (final entity in assemblyDirectory.list(
        recursive: false,
        followLinks: false,
      )) {
        if (entity is! Directory) continue;

        final parts = entity.path
            .split(RegExp(r'[\\/]+'))
            .where((part) => part.isNotEmpty)
            .toList();

        if (parts.isEmpty) continue;

        final versionDirectoryName = parts.last.toLowerCase();

        if (!versionDirectoryName.contains(requiredVersion) ||
            !versionDirectoryName.contains(publicKeyToken)) {
          continue;
        }

        final assemblyFile = File(_join(entity.path, assemblyFileName));

        if (await assemblyFile.exists()) {
          return true;
        }
      }
    } catch (_) {
      // Try the next possible GAC directory.
    }
  }

  return false;
}

Future<void> _startMagicka(String gameDir) async {
  final exe = _join(gameDir, 'Magicka.exe');
  if (!File(exe).existsSync()) {
    throw FileSystemException('Magicka.exe was not found.', exe);
  }
  await Process.start(
    exe,
    const <String>[],
    workingDirectory: gameDir,
    mode: ProcessStartMode.detached,
  );
}

class FlameButton extends StatefulWidget {
  const FlameButton({
    super.key,
    required this.program,
    required this.label,
    required this.icon,
    required this.accent,
    required this.onTap,
    this.patreon = false,
    this.starField = false,
    this.starTuning,
    this.patreonFire = false,
    this.patreonTuning,
    this.patreonHeartFlameProgram,
    this.patreonSparkProgram,
    this.forceHover = false,
    this.overlayIcon = false,
    this.effects = true,
    this.intensity,
  });

  final ui.FragmentProgram? program;
  final String label;
  final IconData icon;
  final Color accent;
  final VoidCallback onTap;
  final bool patreon;
  final bool starField;
  final StarTuningConfig? starTuning;
  final bool patreonFire;
  final PatreonFireConfig? patreonTuning;
  final ui.FragmentProgram? patreonHeartFlameProgram;
  final ui.FragmentProgram? patreonSparkProgram;
  final bool forceHover;
  final bool overlayIcon;
  final bool effects;
  final double? intensity;

  @override
  State<FlameButton> createState() => _FlameButtonState();
}

class _FlameButtonState extends State<FlameButton>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  bool _hovered = false;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
        vsync: this, duration: const Duration(milliseconds: 1800))
      ..repeat();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: SystemMouseCursors.click,
      child: GestureDetector(
        onTap: widget.onTap,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, _) {
            final effectiveHovered = _hovered || widget.forceHover;
            final elapsedSeconds =
                (_controller.lastElapsedDuration?.inMicroseconds ?? 0) /
                    1000000.0;
            final painter = FlameButtonPainter(
              program: widget.program,
              time: widget.starField
                  ? elapsedSeconds
                  : _controller.value * math.pi * 2,
              hovered: effectiveHovered,
              accent: widget.accent,
              intensity: widget.intensity ?? (widget.patreon ? 1.0 : 0.55),
              starField: widget.starField,
            );

            return CustomPaint(
              painter: widget.starField || widget.patreonFire || !widget.effects
                  ? null
                  : painter,
              child: Stack(
                fit: StackFit.expand,
                clipBehavior: Clip.none,
                children: <Widget>[
                  if (!widget.patreonFire)
                    Positioned.fill(
                        child: ArcaneButtonSurface(
                            accent: widget.accent,
                            highlighted: effectiveHovered,
                            starField: widget.starField)),
                  if (widget.starField)
                    Positioned.fill(
                        child: IgnorePointer(
                            child: InstallStarLayer(
                                program: widget.program,
                                hovered: effectiveHovered,
                                accent: widget.accent,
                                tuning: widget.starTuning ??
                                    StarTuningConfig.defaults()))),
                  if (widget.overlayIcon)
                    Positioned(
                        left: 16,
                        top: 0,
                        bottom: 0,
                        width: 28,
                        child: Icon(widget.icon,
                            color: const Color(0xfffff0c8), size: 22)),
                  if (widget.patreonFire)
                    Positioned(
                      left: -80,
                      top: -1320,
                      right: -80,
                      bottom: -320,
                      child: IgnorePointer(
                        child: PatreonFireLayer(
                          hovered: effectiveHovered,
                          tuning: widget.patreonTuning ??
                              PatreonFireConfig.defaults(),
                          heartFlameProgram: widget.patreonHeartFlameProgram,
                          sparkProgram: widget.patreonSparkProgram,
                          buttonInsets:
                              const EdgeInsets.fromLTRB(80, 1320, 80, 320),
                        ),
                      ),
                    ),
                  Padding(
                    padding: EdgeInsets.only(
                        left: widget.overlayIcon ? 54 : 88, right: 16, top: 1),
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: FittedBox(
                        fit: BoxFit.scaleDown,
                        alignment: Alignment.centerLeft,
                        child: Text(widget.label,
                            maxLines: 1,
                            softWrap: false,
                            style: const TextStyle(
                                color: Color(0xfff7d897),
                                fontSize: 16,
                                fontFamily: 'Georgia',
                                shadows: <Shadow>[
                                  Shadow(color: Colors.black, blurRadius: 3)
                                ])),
                      ),
                    ),
                  ),
                  if (effectiveHovered)
                    Positioned.fill(
                        child: IgnorePointer(
                            child: DecoratedBox(
                                decoration: BoxDecoration(
                                    border: Border.all(
                                        color: const Color(0xffffd897),
                                        width: 1),
                                    borderRadius: BorderRadius.circular(6))))),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

class ArcaneButtonSurface extends StatelessWidget {
  const ArcaneButtonSurface({
    super.key,
    required this.accent,
    required this.highlighted,
    this.starField = false,
  });

  final Color accent;
  final bool highlighted;
  final bool starField;

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      painter: ArcaneButtonSurfacePainter(
          accent: accent, highlighted: highlighted, starField: starField),
    );
  }
}

class ArcaneButtonSurfacePainter extends CustomPainter {
  ArcaneButtonSurfacePainter({
    required this.accent,
    required this.highlighted,
    required this.starField,
  });

  final Color accent;
  final bool highlighted;
  final bool starField;

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final rrect =
        RRect.fromRectAndRadius(rect.deflate(1), const Radius.circular(6));
    final base = Paint()
      ..shader = ui.Gradient.linear(
        rect.topLeft,
        rect.bottomRight,
        <Color>[
          Color.lerp(const Color(0xff101314), accent, starField ? 0.28 : 0.10)!,
          const Color(0xff090909),
          Color.lerp(
              const Color(0xff20110b), accent, highlighted ? 0.30 : 0.16)!,
        ],
        <double>[0.0, 0.55, 1.0],
      );
    canvas.drawRRect(rrect, base);

    final glow = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.radial(
        Offset(size.width * 0.28, size.height * 0.50),
        size.width * 0.65,
        <Color>[
          accent.withValues(
              alpha: (highlighted ? 0.34 : 0.18) + (starField ? 0.10 : 0.0)),
          const Color(0x00000000),
        ],
      );
    canvas.drawRRect(rrect, glow);

    final topLine = Paint()
      ..shader = ui.Gradient.linear(
        Offset.zero,
        Offset(size.width, 0),
        <Color>[
          const Color(0x00ffd897),
          const Color(0xffffd897).withValues(alpha: highlighted ? 0.75 : 0.45),
          const Color(0x00ffd897),
        ],
        <double>[0.0, 0.50, 1.0],
      )
      ..strokeWidth = 1.1;
    canvas.drawLine(const Offset(12, 2), Offset(size.width - 12, 2), topLine);

    final border = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = highlighted ? 1.45 : 1.0
      ..color = Color.lerp(
              const Color(0xff845025), accent, highlighted ? 0.42 : 0.18)!
          .withValues(alpha: highlighted ? 0.95 : 0.78);
    canvas.drawRRect(rrect, border);

    final inner = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.7
      ..color = const Color(0xffffd897).withValues(alpha: 0.22);
    canvas.drawRRect(rrect.deflate(3), inner);

    final corner = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = const Color(0xffd9a04f).withValues(alpha: 0.76);
    canvas.drawLine(const Offset(3, 9), const Offset(3, 3), corner);
    canvas.drawLine(const Offset(3, 3), const Offset(9, 3), corner);
    canvas.drawLine(
        Offset(size.width - 3, 9), Offset(size.width - 3, 3), corner);
    canvas.drawLine(
        Offset(size.width - 3, 3), Offset(size.width - 9, 3), corner);
    canvas.drawLine(
        Offset(3, size.height - 9), Offset(3, size.height - 3), corner);
    canvas.drawLine(
        Offset(3, size.height - 3), Offset(9, size.height - 3), corner);
    canvas.drawLine(Offset(size.width - 3, size.height - 9),
        Offset(size.width - 3, size.height - 3), corner);
    canvas.drawLine(Offset(size.width - 3, size.height - 3),
        Offset(size.width - 9, size.height - 3), corner);
  }

  @override
  bool shouldRepaint(covariant ArcaneButtonSurfacePainter oldDelegate) {
    return oldDelegate.accent != accent ||
        oldDelegate.highlighted != highlighted ||
        oldDelegate.starField != starField;
  }
}

class FlameButtonPainter extends CustomPainter {
  FlameButtonPainter(
      {required this.program,
      required this.time,
      required this.hovered,
      required this.accent,
      required this.intensity,
      this.starField = false});

  final ui.FragmentProgram? program;
  final double time;
  final bool hovered;
  final Color accent;
  final double intensity;
  final bool starField;

  @override
  void paint(Canvas canvas, Size size) {
    final rrect =
        RRect.fromRectAndRadius(Offset.zero & size, const Radius.circular(8));
    if (program == null) {
      if (starField) {
        _paintFallbackStars(canvas, size);
        return;
      }
      final paint = Paint()
        ..color = accent.withValues(alpha: hovered ? 0.28 : 0.12)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 12);
      canvas.drawRRect(rrect.inflate(hovered ? 13 : 2), paint);
      return;
    }
    final shader = program!.fragmentShader()
      ..setFloat(0, size.width)
      ..setFloat(1, size.height)
      ..setFloat(2, time)
      ..setFloat(3, hovered ? 1.0 : 0.0)
      ..setFloat(4, intensity)
      ..setFloat(5, accent.r)
      ..setFloat(6, accent.g)
      ..setFloat(7, accent.b);
    final rect = starField
        ? Rect.fromLTRB(-40, -88, size.width + 40, size.height + 16)
        : Offset.zero & size;
    final paint = Paint()..shader = shader;
    if (starField) paint.blendMode = BlendMode.plus;
    canvas.drawRect(rect, paint);
  }

  void _paintFallbackStars(Canvas canvas, Size size) {
    final paint = Paint()..blendMode = BlendMode.plus;
    final rnd = math.Random(24191);
    final hover = hovered ? 1.0 : 0.0;
    final top = hovered ? -86.0 : 0.0;
    final height = hovered ? size.height + 96.0 : size.height;
    for (var i = 0; i < 46; i++) {
      final rx = rnd.nextDouble();
      final ry = rnd.nextDouble();
      final drift = (rnd.nextDouble() - 0.5) * 40.0 * hover;
      final progress = (ry + time * (0.018 + hover * 0.12)) % 1.0;
      final distance = hovered ? 1.0 - progress : 1.0;
      final x = -10.0 + rx * (size.width + 20.0) + drift * progress;
      final y = top + height * (1.0 - progress);
      final radius = (0.9 + rnd.nextDouble() * 2.2) * (0.35 + 0.65 * distance);
      final alpha = (0.30 + rnd.nextDouble() * 0.70) *
          (hovered ? distance * distance : 0.82);
      final color = Color.lerp(const Color(0xff1d6dff), const Color(0xffff38ef),
              rnd.nextDouble())!
          .withValues(alpha: alpha);
      paint.color = color;
      canvas.drawCircle(Offset(x, y), radius, paint);
      if (rnd.nextDouble() > 0.62) {
        paint.strokeWidth = math.max(0.6, radius * 0.38);
        canvas.drawLine(
            Offset(x - radius * 5, y), Offset(x + radius * 5, y), paint);
        canvas.drawLine(
            Offset(x, y - radius * 5), Offset(x, y + radius * 5), paint);
      }
    }
  }

  @override
  bool shouldRepaint(covariant FlameButtonPainter oldDelegate) {
    return oldDelegate.time != time ||
        oldDelegate.hovered != hovered ||
        oldDelegate.program != program ||
        oldDelegate.accent != accent ||
        oldDelegate.starField != starField;
  }
}

class PatreonFireLayer extends StatefulWidget {
  const PatreonFireLayer({
    super.key,
    required this.hovered,
    required this.tuning,
    this.heartFlameProgram,
    this.sparkProgram,
    this.buttonInsets = EdgeInsets.zero,
  });

  final bool hovered;
  final PatreonFireConfig tuning;
  final ui.FragmentProgram? heartFlameProgram;
  final ui.FragmentProgram? sparkProgram;
  final EdgeInsets buttonInsets;

  @override
  State<PatreonFireLayer> createState() => _PatreonFireLayerState();
}

class _PatreonFireLayerState extends State<PatreonFireLayer>
    with SingleTickerProviderStateMixin {
  late final Ticker _ticker;
  final List<_PatreonFireParticle> _particles = <_PatreonFireParticle>[];
  final math.Random _random = math.Random(0xf17e);
  Duration? _lastElapsed;
  double _sparkDebt = 0;
  double _time = 0;
  double _hoverTransition = 0;

  static const double _designWidth = 292;
  static const double _designHeight = 42;
  static const double _hoverTransitionSeconds = 1.0;

  @override
  void initState() {
    super.initState();
    _ticker = createTicker(_tick)..start();
  }

  @override
  void dispose() {
    _ticker.dispose();
    super.dispose();
  }

  void _tick(Duration elapsed) {
    final previous = _lastElapsed;
    _lastElapsed = elapsed;
    final dt = previous == null
        ? 1 / 60
        : ((elapsed - previous).inMicroseconds / 1000000)
            .clamp(0.0, 0.05)
            .toDouble();
    _time += dt;
    _updateHoverTransition(dt);
    _advance(dt);
    if (mounted) setState(() {});
  }

  void _updateHoverTransition(double dt) {
    final step = dt / _hoverTransitionSeconds;
    if (widget.hovered) {
      _hoverTransition = math.min(1.0, _hoverTransition + step);
    } else {
      _hoverTransition = math.max(0.0, _hoverTransition - step);
    }
  }

  double get _hoverAmount {
    final t = _hoverTransition.clamp(0.0, 1.0).toDouble();
    return t * t * (3.0 - 2.0 * t);
  }

  bool get _overflowEnabled => true;

  void _advance(double dt) {
    final mode = PatreonFireMode.lerp(
        widget.tuning.normal, widget.tuning.hover, _hoverAmount);
    _sparkDebt += mode.sparkSpawnRate * dt;

    if (widget.sparkProgram != null) {
      _sparkDebt = _sparkDebt % 1;
    } else {
      while (
          mode.sparkOpacity > 0 && _sparkDebt >= 1 && _particles.length < 320) {
        _particles.add(_createSpark(mode, _overflowEnabled));
        _sparkDebt -= 1;
      }
    }

    for (final particle in _particles) {
      particle.age += dt;
      final wobble =
          math.sin(particle.phase + particle.age * particle.wobbleSpeed) *
              _patreonTurbulence *
              18.0;
      particle.position = Offset(
        particle.position.dx +
            (particle.velocity.dx + wobble) * dt / _designWidth,
        particle.position.dy + particle.velocity.dy * dt / _designHeight,
      );
    }
    _particles.removeWhere((particle) => particle.age >= particle.lifetime);
  }

  _PatreonFireParticle _createSpark(PatreonFireMode mode, bool hovered) {
    final speed = _range(mode.sparkSpeedMin, mode.sparkSpeedMax);
    final x = 0.05 + _random.nextDouble() * 0.90;
    final edgeBias = _random.nextDouble();
    final origin = Offset(
      x,
      edgeBias < 0.72
          ? mode.sparkOriginY + (_random.nextDouble() - 0.5) * 0.12
          : mode.heartY - mode.heartSize * 0.15,
    );
    return _PatreonFireParticle(
      position: origin,
      velocity: Offset((_random.nextDouble() - 0.5) * speed * mode.sparkSpread,
          -speed * _range(0.55, 1.20)),
      lifetime: _range(1.49, 3.20),
      size: _range(mode.sparkSizeMin, mode.sparkSizeMax),
      stretch: _range(3.5, 8.0),
      color: Color.lerp(const Color(0xffffe4a0), const Color(0xffff3c0a),
          _random.nextDouble())!,
      intensity: _range(0.65, 1.25),
      phase: _random.nextDouble() * math.pi * 2,
      wobbleSpeed: _range(5.0, 13.0),
      allowOutside: hovered,
    );
  }

  double _range(double min, double max) {
    if (max <= min) return min;
    return min + _random.nextDouble() * (max - min);
  }

  @override
  Widget build(BuildContext context) {
    final hoverAmount = _hoverAmount;
    final mode = PatreonFireMode.lerp(
        widget.tuning.normal, widget.tuning.hover, hoverAmount);
    return SizedBox.expand(
      child: CustomPaint(
        painter: _PatreonFirePainter(
          particles: _particles,
          mode: mode,
          time: _time,
          heartFlameProgram: widget.heartFlameProgram,
          sparkProgram: widget.sparkProgram,
          buttonInsets: widget.buttonInsets,
          overflowEnabled: _overflowEnabled,
        ),
      ),
    );
  }
}

class _PatreonFireParticle {
  _PatreonFireParticle({
    required this.position,
    required this.velocity,
    required this.lifetime,
    required this.size,
    required this.stretch,
    required this.color,
    required this.intensity,
    required this.phase,
    required this.wobbleSpeed,
    required this.allowOutside,
  });

  Offset position;
  final Offset velocity;
  final double lifetime;
  final double size;
  final double stretch;
  final Color color;
  final double intensity;
  final double phase;
  final double wobbleSpeed;
  final bool allowOutside;
  double age = 0;
}

class _PatreonFirePainter extends CustomPainter {
  _PatreonFirePainter({
    required this.particles,
    required this.mode,
    required this.time,
    required this.heartFlameProgram,
    required this.sparkProgram,
    required this.buttonInsets,
    required this.overflowEnabled,
  });

  final List<_PatreonFireParticle> particles;
  final PatreonFireMode mode;
  final double time;
  final ui.FragmentProgram? heartFlameProgram;
  final ui.FragmentProgram? sparkProgram;
  final EdgeInsets buttonInsets;
  final bool overflowEnabled;

  @override
  void paint(Canvas canvas, Size size) {
    final buttonRect = Rect.fromLTRB(
      buttonInsets.left,
      buttonInsets.top,
      size.width - buttonInsets.right,
      size.height - buttonInsets.bottom,
    );
    if (buttonRect.width <= 0 || buttonRect.height <= 0) return;
    final clip = RRect.fromRectAndRadius(buttonRect, const Radius.circular(6));

    _drawButtonBase(canvas, buttonRect);
    _drawButtonGlow(canvas, buttonRect);
    _drawEdgeFlame(canvas, buttonRect, clip);
    _drawHeartBloom(canvas, buttonRect);
    _drawHeartShaderFlame(canvas, buttonRect, clip);
    _drawHeartCore(canvas, buttonRect);
    _drawSparkShader(canvas, buttonRect, clip);

    for (final particle in particles) {
      _drawParticle(canvas, buttonRect, clip, particle);
    }
  }

  void _drawButtonBase(Canvas canvas, Rect buttonRect) {
    final rrect = RRect.fromRectAndRadius(buttonRect, const Radius.circular(6));
    final base = Paint()
      ..shader = ui.Gradient.linear(
        buttonRect.topLeft,
        buttonRect.bottomRight,
        <Color>[
          const Color(0xff441005),
          const Color(0xff130706),
          const Color(0xff5b1607),
        ],
        <double>[0.0, 0.52, 1.0],
      );
    canvas.drawRRect(rrect, base);

    final ember = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.radial(
        Offset(buttonRect.left + buttonRect.width * mode.heartX,
            buttonRect.center.dy),
        buttonRect.height * 1.45,
        <Color>[
          Color(0xffff5a20).withValues(alpha: 0.30 + 0.16 * mode.buttonGlow),
          Color(0xffff260b).withValues(alpha: 0.10 + 0.10 * mode.buttonGlow),
          const Color(0x00000000),
        ],
        <double>[0.0, 0.52, 1.0],
      );
    canvas.drawRRect(rrect, ember);

    final border = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.2
      ..color =
          Color(0xffff9a3d).withValues(alpha: 0.60 + 0.22 * mode.buttonGlow);
    canvas.drawRRect(rrect.deflate(0.6), border);
  }

  void _drawButtonGlow(Canvas canvas, Rect buttonRect) {
    if (mode.buttonGlow <= 0) return;
    final rrect = RRect.fromRectAndRadius(buttonRect, const Radius.circular(7));
    final paint = Paint()
      ..blendMode = BlendMode.plus
      ..color = Color(0xffff4b19).withValues(alpha: 0.16 * mode.buttonGlow)
      ..maskFilter =
          MaskFilter.blur(BlurStyle.normal, 12 + mode.buttonGlow * 10);
    canvas.drawRRect(rrect.inflate(4 + mode.buttonGlow * 8), paint);
  }

  void _drawEdgeFlame(Canvas canvas, Rect buttonRect, RRect clip) {
    final edgeActive = mode.edgeFlameHeight > 0 && mode.edgeFlameIntensity > 0;
    final sideActive = mode.sideFlameHeight > 0 && mode.sideFlameIntensity > 0;
    if (!edgeActive && !sideActive) return;
    if (heartFlameProgram != null) {
      final height = mode.edgeFlameHeight;
      final insetHeight = buttonRect.height * 0.42;
      final horizontalPad = buttonRect.width * 0.10;
      final edgeBaseY = buttonRect.top + buttonRect.height * mode.edgeFlameY;
      final topFlameRect = Rect.fromLTWH(
        buttonRect.left - horizontalPad,
        edgeBaseY - height,
        buttonRect.width + horizontalPad * 2,
        height + insetHeight,
      );
      final sideWidth = buttonRect.height * (0.75 + mode.sideFlameWidth * 1.10);
      final sideHeight =
          buttonRect.height * (0.55 + mode.sideFlameHeight * 1.35);
      final sideBaseY =
          buttonRect.top + buttonRect.height * mode.sideFlameOriginY;
      final sideDistance = buttonRect.width * mode.sideFlameX;
      final leftCenterX = buttonRect.center.dx - sideDistance;
      final rightCenterX = buttonRect.center.dx + sideDistance;
      final leftFlameRect = Rect.fromLTWH(
        leftCenterX - sideWidth * 0.5,
        sideBaseY - sideHeight,
        sideWidth,
        sideHeight,
      );
      final rightFlameRect = Rect.fromLTWH(
        rightCenterX - sideWidth * 0.5,
        sideBaseY - sideHeight,
        sideWidth,
        sideHeight,
      );

      if (!overflowEnabled) {
        canvas.save();
        canvas.clipRRect(clip);
      }

      void drawShaderFlame(Rect rect, double timeOffset, double intensity) {
        if (rect.width <= 0 || rect.height <= 0) return;
        final shader = heartFlameProgram!.fragmentShader()
          ..setFloat(0, rect.width)
          ..setFloat(1, rect.height)
          ..setFloat(2, time + timeOffset)
          ..setFloat(3, 0.0)
          ..setFloat(4, 7.0 + _patreonTurbulence * 2.6)
          ..setFloat(5, (0.38 + intensity * 0.95).clamp(0.0, 5.0).toDouble());
        final paint = Paint()
          ..shader = shader
          ..blendMode = BlendMode.plus;
        canvas.save();
        canvas.translate(rect.left, rect.top);
        canvas.drawRect(Offset.zero & rect.size, paint);
        canvas.restore();
      }

      if (edgeActive) {
        drawShaderFlame(topFlameRect, 17.0, mode.edgeFlameIntensity);
      }
      if (sideActive) {
        drawShaderFlame(leftFlameRect, 31.0, mode.sideFlameIntensity);
        drawShaderFlame(rightFlameRect, 43.0, mode.sideFlameIntensity);
      }

      if (!overflowEnabled) {
        canvas.restore();
      }
      return;
    }

    if (!edgeActive) return;
    final height = mode.edgeFlameHeight;
    final edgeBaseY = buttonRect.top + buttonRect.height * mode.edgeFlameY;
    final power = mode.edgeFlameIntensity.clamp(0.0, 1.0).toDouble();
    final path = Path()..moveTo(buttonRect.left - 6, edgeBaseY + 6);
    const segments = 18;
    for (var i = 0; i <= segments; i++) {
      final x = buttonRect.left + buttonRect.width * i / segments;
      final wave = 0.45 +
          0.55 *
              math.sin(time * (4.0 + mode.edgeFlameIntensity * 5.0) + i * 1.73);
      final flicker = 0.5 + 0.5 * math.sin(time * 7.7 + i * 3.1);
      final y = edgeBaseY + 5 - height * (0.35 + wave * 0.45 + flicker * 0.20);
      path.lineTo(x, y);
    }
    path
      ..lineTo(buttonRect.right + 6, edgeBaseY + 8)
      ..close();
    final paint = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.linear(
        Offset(buttonRect.left, edgeBaseY - height),
        Offset(buttonRect.left, edgeBaseY + 10),
        <Color>[
          Color(0xfffff2b0).withValues(alpha: 0.34 * power),
          Color(0xffffbf3f).withValues(alpha: 0.78 * power),
          Color(0xffff2a10).withValues(alpha: 0.58 * power),
          const Color(0x00000000),
        ],
        <double>[0.0, 0.28, 0.68, 1.0],
      )
      ..maskFilter = MaskFilter.blur(
          BlurStyle.normal, 2.5 + mode.edgeFlameIntensity * 3.5);
    if (!overflowEnabled) {
      canvas.save();
      canvas.clipRRect(clip);
    }
    canvas.drawPath(path, paint);
    if (!overflowEnabled) {
      canvas.restore();
    }
  }

  void _drawHeartBloom(Canvas canvas, Rect buttonRect) {
    final center = Offset(buttonRect.left + buttonRect.width * mode.heartX,
        buttonRect.top + buttonRect.height * mode.heartY);
    final scale = buttonRect.height * mode.heartSize * 0.52;
    final path = _heartPath(center, scale);
    final cover = Paint()
      ..color = const Color(0xff3b0906).withValues(alpha: 0.92);
    canvas.drawPath(path, cover);

    final bloomPaint = Paint()
      ..blendMode = BlendMode.plus
      ..color = Color(0xffff351f).withValues(alpha: 0.24 * mode.heartBloom)
      ..maskFilter =
          MaskFilter.blur(BlurStyle.normal, 10 + mode.heartBloom * 10);
    canvas.drawPath(path, bloomPaint);
  }

  void _drawHeartShaderFlame(Canvas canvas, Rect buttonRect, RRect clip) {
    if (heartFlameProgram == null || mode.flameIntensity <= 0) return;

    final center = Offset(buttonRect.left + buttonRect.width * mode.heartX,
        buttonRect.top + buttonRect.height * mode.heartY);
    final heartScale = buttonRect.height * mode.heartSize * 0.52;
    final flameWidth = heartScale * (1.45 + mode.flameWidth * 2.20);
    final flameHeight = buttonRect.height * (0.55 + mode.flameHeight * 1.35);
    final origin = Offset(
        center.dx, buttonRect.top + buttonRect.height * mode.flameOriginY);
    final flameRect = Rect.fromLTWH(
      origin.dx - flameWidth * 0.5,
      origin.dy - flameHeight,
      flameWidth,
      flameHeight,
    );

    if (!overflowEnabled) {
      canvas.save();
      canvas.clipRRect(clip);
    }

    final shader = heartFlameProgram!.fragmentShader()
      ..setFloat(0, flameRect.width)
      ..setFloat(1, flameRect.height)
      ..setFloat(2, time)
      ..setFloat(3, 0.0)
      ..setFloat(4, 6.0 + _patreonTurbulence * 3.0)
      ..setFloat(
          5, (0.72 + mode.flameIntensity * 0.72).clamp(0.0, 5.0).toDouble());
    final paint = Paint()
      ..shader = shader
      ..blendMode = BlendMode.plus;
    canvas.save();
    canvas.translate(flameRect.left, flameRect.top);
    canvas.drawRect(Offset.zero & flameRect.size, paint);
    canvas.restore();

    if (!overflowEnabled) {
      canvas.restore();
    }
  }

  void _drawHeartCore(Canvas canvas, Rect buttonRect) {
    final center = Offset(buttonRect.left + buttonRect.width * mode.heartX,
        buttonRect.top + buttonRect.height * mode.heartY);
    final scale = buttonRect.height * mode.heartSize * 0.52;
    final path = _heartPath(center, scale);
    final fill = Paint()
      ..shader = ui.Gradient.radial(
        center.translate(-scale * 0.20, -scale * 0.18),
        scale * 1.45,
        <Color>[
          Color.lerp(const Color(0xfffff0b8), const Color(0xffff4724), 0.35)!
              .withValues(alpha: 0.96),
          const Color(0xffff3720).withValues(alpha: 0.94),
          const Color(0xff8a0c08).withValues(alpha: 0.98),
        ],
        <double>[0.0, 0.48, 1.0],
      );
    canvas.drawPath(path, fill);

    final shine = Paint()
      ..blendMode = BlendMode.plus
      ..color = Colors.white.withValues(alpha: 0.16 + 0.12 * mode.heartGlow)
      ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 2);
    canvas.drawOval(
        Rect.fromCenter(
            center: center.translate(-scale * 0.32, -scale * 0.28),
            width: scale * 0.45,
            height: scale * 0.22),
        shine);

    final rim = Paint()
      ..blendMode = BlendMode.plus
      ..style = PaintingStyle.stroke
      ..strokeWidth = math.max(1.0, scale * 0.08)
      ..color = Color(0xffffb45e).withValues(alpha: 0.36 * mode.heartGlow)
      ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 1.4);
    canvas.drawPath(path, rim);
  }

  void _drawSparkShader(Canvas canvas, Rect buttonRect, RRect clip) {
    final sparkOpacity = mode.sparkOpacity.clamp(0.0, 1.50).toDouble();
    if (sparkProgram == null || mode.sparkSpawnRate <= 0 || sparkOpacity <= 0) {
      return;
    }

    final origin = Offset(buttonRect.center.dx,
        buttonRect.top + buttonRect.height * mode.sparkOriginY);
    final sparkWidth = buttonRect.width * (0.34 + mode.sparkSpread * 0.58);
    final originDrop =
        math.max(0.0, mode.sparkOriginY - 1.0) * buttonRect.height;
    final sparkHeight =
        buttonRect.height * (2.00 + mode.flameHeight * 0.78) + originDrop;
    final bottomRoom = buttonRect.height * 0.45 + originDrop * 0.20;
    final sparkRect = Rect.fromLTWH(
      origin.dx - sparkWidth * 0.5,
      origin.dy - sparkHeight,
      sparkWidth,
      sparkHeight + bottomRoom,
    );

    if (!overflowEnabled) {
      canvas.save();
      canvas.clipRRect(clip);
    }

    final averageSpeed = (mode.sparkSpeedMin + mode.sparkSpeedMax) * 0.5;
    final averageSize = (mode.sparkSizeMin + mode.sparkSizeMax) * 0.5;
    final shader = sparkProgram!.fragmentShader()
      ..setFloat(0, sparkRect.width)
      ..setFloat(1, sparkRect.height)
      ..setFloat(2, time)
      ..setFloat(3, 0.0)
      ..setFloat(4, (averageSpeed / 150.0).clamp(0.08, 2.80).toDouble())
      ..setFloat(5, (mode.sparkSpawnRate / 90.0).clamp(0.0, 1.0).toDouble())
      ..setFloat(6, (averageSize / 3.0).clamp(0.05, 2.60).toDouble())
      ..setFloat(7, mode.sparkSpread.clamp(0.0, 3.20).toDouble())
      ..setFloat(8, _patreonTurbulence.clamp(0.0, 2.00).toDouble())
      ..setFloat(
          9,
          ((0.52 + mode.sparkSpawnRate / 120.0) * sparkOpacity)
              .clamp(0.0, 2.50)
              .toDouble())
      ..setFloat(10, mode.sparkBottomCrop.clamp(0.0, 0.95).toDouble())
      ..setFloat(11, mode.sparkMotionX.clamp(-4.00, 4.00).toDouble())
      ..setFloat(12, mode.sparkMotionY.clamp(-4.00, 4.00).toDouble())
      ..setFloat(13, mode.sparkSmoke.clamp(0.0, 4.00).toDouble())
      ..setFloat(14, mode.sparkBloom.clamp(0.0, 5.00).toDouble())
      ..setFloat(15, mode.sparkLayerSize.clamp(0.50, 2.50).toDouble())
      ..setFloat(16, mode.sparkLayerAlpha.clamp(0.20, 0.99).toDouble())
      ..setFloat(17, mode.sparkLayers.clamp(1.00, 24.00).toDouble());
    final paint = Paint()
      ..shader = shader
      ..blendMode = BlendMode.plus;

    canvas.save();
    canvas.translate(sparkRect.left, sparkRect.top);
    canvas.drawRect(Offset.zero & sparkRect.size, paint);
    canvas.restore();

    if (!overflowEnabled) {
      canvas.restore();
    }
  }

  void _drawParticle(Canvas canvas, Rect buttonRect, RRect clip,
      _PatreonFireParticle particle) {
    final life = (particle.age / particle.lifetime).clamp(0.0, 1.0).toDouble();
    final visibility = mode.sparkOpacity.clamp(0.0, 1.50).toDouble();
    final alpha = ((1.0 - _smoothstep(0.58, 1.0, life)) *
            _smoothstep(0.0, 0.10, life) *
            particle.intensity *
            visibility)
        .clamp(0.0, 1.0)
        .toDouble();
    if (alpha <= 0.01) return;

    final position = Offset(
        buttonRect.left + particle.position.dx * buttonRect.width,
        buttonRect.top + particle.position.dy * buttonRect.height);
    if (!particle.allowOutside) {
      canvas.save();
      canvas.clipRRect(clip);
    }

    _drawSpark(canvas, position, particle, alpha, life);

    if (!particle.allowOutside) {
      canvas.restore();
    }
  }

  void _drawSpark(Canvas canvas, Offset position, _PatreonFireParticle particle,
      double alpha, double life) {
    final paint = Paint()
      ..blendMode = BlendMode.plus
      ..strokeCap = StrokeCap.round
      ..strokeWidth = math.max(0.55, particle.size * (1.0 - life * 0.35))
      ..color = particle.color.withValues(alpha: alpha);
    final velocity = particle.velocity;
    final length = particle.size * particle.stretch;
    final angle = math.atan2(velocity.dy, velocity.dx);
    final trail = Offset(math.cos(angle), math.sin(angle)) * -length;
    canvas.drawLine(position, position + trail, paint);
    paint
      ..style = PaintingStyle.fill
      ..color = Color.lerp(particle.color, Colors.white, 0.50)!
          .withValues(alpha: alpha);
    canvas.drawCircle(position, particle.size * (1.0 - life * 0.45), paint);
  }

  Path _heartPath(Offset center, double scale) {
    return Path()
      ..moveTo(center.dx, center.dy + scale * 0.82)
      ..cubicTo(
          center.dx - scale * 1.18,
          center.dy + scale * 0.05,
          center.dx - scale * 1.06,
          center.dy - scale * 0.82,
          center.dx - scale * 0.42,
          center.dy - scale * 0.72)
      ..cubicTo(
          center.dx - scale * 0.14,
          center.dy - scale * 0.68,
          center.dx - scale * 0.03,
          center.dy - scale * 0.46,
          center.dx,
          center.dy - scale * 0.32)
      ..cubicTo(
          center.dx + scale * 0.03,
          center.dy - scale * 0.46,
          center.dx + scale * 0.14,
          center.dy - scale * 0.68,
          center.dx + scale * 0.42,
          center.dy - scale * 0.72)
      ..cubicTo(
          center.dx + scale * 1.06,
          center.dy - scale * 0.82,
          center.dx + scale * 1.18,
          center.dy + scale * 0.05,
          center.dx,
          center.dy + scale * 0.82)
      ..close();
  }

  double _smoothstep(double edge0, double edge1, double x) {
    if (edge0 == edge1) return x < edge0 ? 0.0 : 1.0;
    final t = ((x - edge0) / (edge1 - edge0)).clamp(0.0, 1.0).toDouble();
    return t * t * (3 - 2 * t);
  }

  @override
  bool shouldRepaint(covariant _PatreonFirePainter oldDelegate) => true;
}

class InstallStarLayer extends StatefulWidget {
  const InstallStarLayer(
      {super.key,
      required this.program,
      required this.hovered,
      required this.accent,
      required this.tuning});

  final ui.FragmentProgram? program;
  final bool hovered;
  final Color accent;
  final StarTuningConfig tuning;

  @override
  State<InstallStarLayer> createState() => _InstallStarLayerState();
}

class _InstallStarLayerState extends State<InstallStarLayer>
    with SingleTickerProviderStateMixin {
  late final Ticker _ticker;
  final List<_InstallStarParticle> _particles = <_InstallStarParticle>[];
  final math.Random _random = math.Random(0x5a7e11);
  Duration? _lastElapsed;
  double _spawnDebt = 0;

  @override
  void initState() {
    super.initState();
    _ticker = createTicker(_tick)..start();
  }

  @override
  void dispose() {
    _ticker.dispose();
    super.dispose();
  }

  void _tick(Duration elapsed) {
    final previous = _lastElapsed;
    _lastElapsed = elapsed;
    final dt = previous == null
        ? 1 / 60
        : ((elapsed - previous).inMicroseconds / 1000000)
            .clamp(0.0, 0.05)
            .toDouble();
    _advance(dt);
    if (mounted) setState(() {});
  }

  void _advance(double dt) {
    final mode = widget.hovered ? widget.tuning.hover : widget.tuning.normal;
    final spawnRate = mode.spawnRate;
    const maxParticles = 180;
    _spawnDebt += spawnRate * dt;

    while (_spawnDebt >= 1 && _particles.length < maxParticles) {
      _particles.add(_createParticle(widget.hovered));
      _spawnDebt -= 1;
    }
    if (_particles.length >= maxParticles) {
      _spawnDebt = math.min(_spawnDebt, 1.0);
    }

    for (final particle in _particles) {
      particle.age += dt;
    }
    _particles.removeWhere((particle) => particle.age >= particle.lifetime);
  }

  _InstallStarParticle _createParticle(bool hovered) {
    final mode = hovered ? widget.tuning.hover : widget.tuning.normal;
    final angle = _random.nextDouble() * math.pi * 2;
    final direction = Offset(math.cos(angle), math.sin(angle));
    final origin = Offset(
      0.5 + (_random.nextDouble() - 0.5) * mode.spawnWidth,
      0.5 + (_random.nextDouble() - 0.5) * mode.spawnHeight,
    );
    final hue = _randomRange(mode.hueMin, mode.hueMax);
    final baseColor = HSLColor.fromAHSL(1.0, hue, 0.86, 0.60).toColor();
    final color = Color.lerp(baseColor, widget.accent, mode.accentMix)!;

    return _InstallStarParticle(
      origin: origin,
      direction: direction,
      speed: _randomRange(mode.speedMin, mode.speedMax),
      lifetime: _randomRange(mode.lifetimeMin, mode.lifetimeMax),
      radius: _randomRange(mode.radiusMin, mode.radiusMax),
      color: color,
      allowOutside: hovered,
      brightness: _randomRange(mode.brightnessMin, mode.brightnessMax),
      phase: _random.nextDouble() * math.pi * 2,
      spin: _randomRange(mode.rotationMin, mode.rotationMax),
      tipLength: _randomRange(mode.tipLengthMin, mode.tipLengthMax),
      centerRadius: _randomRange(mode.centerRadiusMin, mode.centerRadiusMax),
    );
  }

  double _randomRange(double min, double max) {
    if (max <= min) return min;
    return min + _random.nextDouble() * (max - min);
  }

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
        painter: _InstallStarPainter(
            program: widget.program, particles: _particles));
  }
}

class _InstallStarParticle {
  _InstallStarParticle({
    required this.origin,
    required this.direction,
    required this.speed,
    required this.lifetime,
    required this.radius,
    required this.color,
    required this.allowOutside,
    required this.brightness,
    required this.phase,
    required this.spin,
    required this.tipLength,
    required this.centerRadius,
  });

  final Offset origin;
  final Offset direction;
  final double speed;
  final double lifetime;
  final double radius;
  final Color color;
  final bool allowOutside;
  final double brightness;
  final double phase;
  final double spin;
  final double tipLength;
  final double centerRadius;
  double age = 0;
}

class _InstallStarPainter extends CustomPainter {
  _InstallStarPainter({required this.program, required this.particles});

  final ui.FragmentProgram? program;
  final List<_InstallStarParticle> particles;

  @override
  void paint(Canvas canvas, Size size) {
    if (particles.isEmpty) return;

    final rect = Offset.zero & size;
    final clip = RRect.fromRectAndRadius(rect, const Radius.circular(6));

    for (final particle in particles) {
      final origin = Offset(
          particle.origin.dx * size.width, particle.origin.dy * size.height);
      final edgeDistance =
          _distanceToEdge(rect.deflate(2), origin, particle.direction);
      final distance = particle.speed * particle.age;
      final position = origin + particle.direction * distance;
      final life =
          (particle.age / particle.lifetime).clamp(0.0, 1.0).toDouble();
      final lifeFade = 1.0 - _smoothstep(0.60, 1.0, life);
      final birthFade = _smoothstep(0.0, 0.12, life);
      final edgeFade = particle.allowOutside
          ? 1.0
          : 1.0 -
              _smoothstep(edgeDistance * 0.42, edgeDistance * 0.82, distance);
      final alpha = (lifeFade * birthFade * edgeFade * particle.brightness)
          .clamp(0.0, 1.0)
          .toDouble();
      if (alpha <= 0.01) continue;

      final edgeRatio = edgeDistance <= 0
          ? 1.0
          : (distance / edgeDistance).clamp(0.0, 1.0).toDouble();
      final shrink = particle.allowOutside
          ? 1.0 - 0.30 * _smoothstep(0.40, 1.0, life)
          : 1.0 - 0.66 * _smoothstep(0.20, 0.78, edgeRatio);
      final radius = math.max(0.25, particle.radius * shrink);

      if (!particle.allowOutside) {
        canvas.save();
        canvas.clipRRect(clip);
      }

      _drawStar(
        canvas,
        position,
        radius,
        particle.color,
        alpha,
        particle.phase + particle.spin * particle.age,
        particle.tipLength,
        particle.centerRadius,
        particle.brightness,
      );

      if (!particle.allowOutside) {
        canvas.restore();
      }
    }
  }

  void _drawStar(
      Canvas canvas,
      Offset center,
      double radius,
      Color color,
      double alpha,
      double angle,
      double tipLength,
      double centerRadius,
      double brightness) {
    if (program != null) {
      _drawShaderStar(canvas, center, radius, color, alpha, angle, tipLength,
          centerRadius, brightness);
      return;
    }

    final flareLength = radius * (1.65 + tipLength * 0.86);
    final diagonalLength = flareLength * 0.48;
    final coreRadius = math.max(0.35, radius * centerRadius * 0.82);
    final softGlow = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.radial(
        center,
        flareLength * 0.86,
        <Color>[
          Color.lerp(color, Colors.white, 0.58)!
              .withValues(alpha: alpha * 0.36),
          color.withValues(alpha: alpha * 0.20),
          const Color(0x00000000),
        ],
        const <double>[0.0, 0.34, 1.0],
      );
    canvas.drawCircle(center, flareLength * 0.86, softGlow);

    final rayPaint = Paint()
      ..blendMode = BlendMode.plus
      ..color =
          Color.lerp(color, Colors.white, 0.42)!.withValues(alpha: alpha * 0.82)
      ..strokeCap = StrokeCap.round
      ..strokeWidth = math.max(0.42, radius * 0.34)
      ..maskFilter = MaskFilter.blur(BlurStyle.normal, radius * 0.10);

    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(angle);
    canvas.drawLine(Offset(-flareLength, 0), Offset(flareLength, 0), rayPaint);
    canvas.drawLine(Offset(0, -flareLength * 0.72),
        Offset(0, flareLength * 0.72), rayPaint);
    rayPaint
      ..color = color.withValues(alpha: alpha * 0.42)
      ..strokeWidth = math.max(0.24, radius * 0.19)
      ..maskFilter = MaskFilter.blur(BlurStyle.normal, radius * 0.05);
    canvas.rotate(math.pi / 4);
    canvas.drawLine(
        Offset(-diagonalLength, 0), Offset(diagonalLength, 0), rayPaint);
    canvas.drawLine(
        Offset(0, -diagonalLength), Offset(0, diagonalLength), rayPaint);
    canvas.restore();

    final haloPaint = Paint()
      ..blendMode = BlendMode.plus
      ..style = PaintingStyle.stroke
      ..strokeWidth = math.max(0.26, radius * 0.15)
      ..color = color.withValues(alpha: alpha * 0.28);
    canvas.drawCircle(center, coreRadius * 2.2, haloPaint);

    final corePaint = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.radial(
        center,
        coreRadius * 2.3,
        <Color>[
          Colors.white.withValues(alpha: alpha),
          Color.lerp(color, Colors.white, 0.38)!
              .withValues(alpha: alpha * 0.86),
          color.withValues(alpha: alpha * 0.10),
        ],
        const <double>[0.0, 0.42, 1.0],
      );
    canvas.drawCircle(center, coreRadius * 2.3, corePaint);
  }

  void _drawShaderStar(
      Canvas canvas,
      Offset center,
      double radius,
      Color color,
      double alpha,
      double angle,
      double tipLength,
      double centerRadius,
      double brightness) {
    final side =
        (radius * (12.0 + tipLength * 5.5)).clamp(12.0, 420.0).toDouble();
    final shader = program!.fragmentShader()
      ..setFloat(0, side)
      ..setFloat(1, side)
      ..setFloat(2, angle + brightness * 11.0)
      ..setFloat(3, alpha)
      ..setFloat(4, (0.20 + centerRadius * 0.04).clamp(0.10, 0.48).toDouble())
      ..setFloat(
          5, (0.018 + centerRadius * 0.019).clamp(0.006, 0.070).toDouble())
      ..setFloat(6, (0.38 + tipLength * 0.12).clamp(0.10, 7.00).toDouble())
      ..setFloat(7, (900.0 + tipLength * 115.0).clamp(500.0, 9000.0).toDouble())
      ..setFloat(8, (1.0 + brightness * 0.75).clamp(0.4, 3.0).toDouble())
      ..setFloat(9, (0.12 + brightness * 0.17).clamp(0.0, 0.45).toDouble())
      ..setFloat(10, color.r)
      ..setFloat(11, color.g)
      ..setFloat(12, color.b);

    final paint = Paint()
      ..shader = shader
      ..blendMode = BlendMode.plus;
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(angle);
    canvas.translate(-side * 0.5, -side * 0.5);
    canvas.drawRect(Offset.zero & Size(side, side), paint);
    canvas.restore();
  }

  double _distanceToEdge(Rect rect, Offset origin, Offset direction) {
    var distance = double.infinity;
    if (direction.dx > 0.0001) {
      distance = math.min(distance, (rect.right - origin.dx) / direction.dx);
    } else if (direction.dx < -0.0001) {
      distance = math.min(distance, (rect.left - origin.dx) / direction.dx);
    }
    if (direction.dy > 0.0001) {
      distance = math.min(distance, (rect.bottom - origin.dy) / direction.dy);
    } else if (direction.dy < -0.0001) {
      distance = math.min(distance, (rect.top - origin.dy) / direction.dy);
    }
    if (!distance.isFinite || distance < 1)
      return math.max(rect.width, rect.height);
    return distance;
  }

  double _smoothstep(double edge0, double edge1, double x) {
    if (edge0 == edge1) return x < edge0 ? 0.0 : 1.0;
    final t = ((x - edge0) / (edge1 - edge0)).clamp(0.0, 1.0).toDouble();
    return t * t * (3 - 2 * t);
  }

  @override
  bool shouldRepaint(covariant _InstallStarPainter oldDelegate) => true;
}

class ArcaneBackdrop extends StatelessWidget {
  const ArcaneBackdrop({super.key});

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: <Color>[Color(0xff0d1214), Color(0xff040608)],
        ),
      ),
      child:
          CustomPaint(painter: SparkPainter(), child: const SizedBox.expand()),
    );
  }
}

class SparkPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final rnd = math.Random(42910);
    final paint = Paint();
    for (var i = 0; i < 48; i++) {
      paint.color = <Color>[
        const Color(0xff80caff),
        const Color(0xffe0a956),
        const Color(0xffab4fff)
      ][rnd.nextInt(3)]
          .withValues(alpha: 0.25 + rnd.nextDouble() * 0.35);
      canvas.drawCircle(
          Offset(240 + rnd.nextDouble() * (size.width - 260),
              18 + rnd.nextDouble() * (size.height - 40)),
          1.0 + rnd.nextDouble() * 2.2,
          paint);
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class SidebarImage extends StatelessWidget {
  const SidebarImage({super.key});

  @override
  Widget build(BuildContext context) {
    return Stack(
      fit: StackFit.expand,
      children: <Widget>[
        Image.asset(_assetKey('assets/magicka-workshop-sidebar.png'),
            fit: BoxFit.cover),
        DecoratedBox(
            decoration: BoxDecoration(
                gradient: LinearGradient(colors: <Color>[
          Colors.transparent,
          Colors.black.withValues(alpha: 0.88)
        ]))),
        const Align(
            alignment: Alignment.centerRight,
            child: ColoredBox(
                color: Color(0xaa245b80), child: SizedBox(width: 1))),
      ],
    );
  }
}

class _Header extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    return SizedBox(
      width: 760,
      height: 60,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          SizedBox(
            width: 760,
            height: 39,
            child: FittedBox(
              fit: BoxFit.scaleDown,
              alignment: Alignment.centerLeft,
              child: Text(s.t('appHeader'),
                  maxLines: 1,
                  softWrap: false,
                  style: const TextStyle(
                      color: Color(0xfff7d897),
                      fontFamily: 'Georgia',
                      fontSize: 37,
                      height: 1.05,
                      letterSpacing: 0,
                      shadows: <Shadow>[
                        Shadow(
                            color: Colors.black,
                            offset: Offset(2, 2),
                            blurRadius: 2)
                      ])),
            ),
          ),
          const SizedBox(height: 2),
          SizedBox(
            width: 760,
            height: 19,
            child: FittedBox(
              fit: BoxFit.scaleDown,
              alignment: Alignment.centerLeft,
              child: Text(s.t('appSubtitle'),
                  key: const ValueKey('installer-header-subtitle'),
                  maxLines: 1,
                  softWrap: false,
                  style: const TextStyle(
                      color: Color(0xffeedfc4),
                      fontFamily: 'Georgia',
                      fontSize: 18,
                      height: 1.05)),
            ),
          ),
        ],
      ),
    );
  }
}

class _FolderPanel extends StatelessWidget {
  const _FolderPanel({required this.controller});
  final TextEditingController controller;

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    return ArcanePanel(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 6),
        child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(s.t('gameFolder'),
                  style: const TextStyle(
                      color: Color(0xfff7d897), fontWeight: FontWeight.bold)),
              const SizedBox(height: 4),
              SizedBox(
                height: 34,
                child: TextField(
                  controller: controller,
                  textAlignVertical: TextAlignVertical.center,
                  style: const TextStyle(
                      color: Color(0xffeedfc4),
                      fontFamily: 'Georgia',
                      fontSize: 15),
                  decoration: InputDecoration(
                    isDense: true,
                    contentPadding: const EdgeInsets.fromLTRB(14, 6, 14, 6),
                    filled: true,
                    fillColor: const Color(0xff0a0f10),
                    enabledBorder: OutlineInputBorder(
                        borderSide: const BorderSide(color: Color(0xff845025)),
                        borderRadius: BorderRadius.circular(4)),
                    focusedBorder: OutlineInputBorder(
                        borderSide: const BorderSide(color: Color(0xff80caff)),
                        borderRadius: BorderRadius.circular(4)),
                  ),
                ),
              ),
            ]),
      ),
    );
  }
}

class _TelemetryPanel extends StatelessWidget {
  const _TelemetryPanel({
    required this.usageSharing,
    required this.crashReports,
    required this.autoUpdate,
    required this.onUsageChanged,
    required this.onCrashChanged,
    required this.onAutoUpdateChanged,
  });

  final bool usageSharing;
  final bool crashReports;
  final bool autoUpdate;
  final ValueChanged<bool> onUsageChanged;
  final ValueChanged<bool> onCrashChanged;
  final ValueChanged<bool> onAutoUpdateChanged;

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    return ArcanePanel(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 16, 21),
        child: Column(
          children: <Widget>[
            Row(children: <Widget>[
              ArcaneCheck(value: usageSharing, onChanged: onUsageChanged),
              const SizedBox(width: 12),
              Expanded(
                  child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: <Widget>[
                    Text(s.t('telemetryIntroTitle'),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                            color: Color(0xffeedfc4),
                            fontWeight: FontWeight.bold,
                            fontSize: 15)),
                    Text(s.t('telemetryIntroBody'),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                            color: Color(0xffbeb19b), fontSize: 13)),
                  ])),
            ]),
            const SizedBox(height: 15),
            Row(children: <Widget>[
              EventCard(
                  icon: Icons.play_arrow_rounded,
                  title: s.t('eventGameStarted'),
                  body: s.t('eventGameStartedBody'),
                  accent: const Color(0xffab4fff)),
              const SizedBox(width: 11),
              EventCard(
                  icon: Icons.verified_rounded,
                  title: s.t('eventGameClosed'),
                  body: s.t('eventGameClosedBody'),
                  accent: const Color(0xff5bdf64)),
              const SizedBox(width: 11),
              EventCard(
                  icon: Icons.file_download_done_rounded,
                  title: s.t('eventPatchInstalled'),
                  body: s.t('eventPatchInstalledBody'),
                  accent: const Color(0xff80caff)),
              const SizedBox(width: 11),
              EventCard(
                  icon: Icons.autorenew_rounded,
                  title: s.t('eventAutoUpdate'),
                  body: s.t('eventAutoUpdateBody'),
                  accent: const Color(0xff3f9fff)),
              const SizedBox(width: 11),
              EventCard(
                  icon: Icons.warning_amber_rounded,
                  title: s.t('eventCrashReport'),
                  body: s.t('eventCrashReportBody'),
                  accent: const Color(0xffd63a20)),
            ]),
            const Spacer(),
            Row(children: <Widget>[
              ArcaneCheck(value: autoUpdate, onChanged: onAutoUpdateChanged),
              const SizedBox(width: 8),
              Text(s.t('checkForUpdates'),
                  style: const TextStyle(
                      color: Color(0xffeedfc4), fontWeight: FontWeight.bold)),
              const SizedBox(width: 28),
              ArcaneCheck(value: crashReports, onChanged: onCrashChanged),
              const SizedBox(width: 8),
              Flexible(
                  child: Text(s.t('saveErrorNotes'),
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                          color: Color(0xffeedfc4),
                          fontWeight: FontWeight.bold))),
            ]),
          ],
        ),
      ),
    );
  }
}

class ArcanePanel extends StatelessWidget {
  const ArcanePanel({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      painter: ArcanePanelPainter(),
      child: child,
    );
  }
}

class ArcanePanelPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final rrect =
        RRect.fromRectAndRadius(rect.deflate(1), const Radius.circular(6));
    final base = Paint()
      ..shader = ui.Gradient.linear(
        rect.topLeft,
        rect.bottomRight,
        <Color>[
          const Color(0xff151b1d).withValues(alpha: 0.96),
          const Color(0xff080a0b).withValues(alpha: 0.98),
          const Color(0xff17110c).withValues(alpha: 0.94),
        ],
        <double>[0.0, 0.55, 1.0],
      );
    canvas.drawRRect(rrect, base);

    final texture = Paint()
      ..color = const Color(0xffffffff).withValues(alpha: 0.025);
    for (var y = 10.0; y < size.height; y += 12) {
      canvas.drawLine(Offset(6, y), Offset(size.width - 6, y + 4), texture);
    }

    final border = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.1
      ..color = const Color(0xff8b5728).withValues(alpha: 0.88);
    canvas.drawRRect(rrect, border);

    final inner = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.7
      ..color = const Color(0xffffd897).withValues(alpha: 0.22);
    canvas.drawRRect(rrect.deflate(5), inner);
    _drawCorners(canvas, size, const Color(0xffd9a04f).withValues(alpha: 0.78));
  }

  void _drawCorners(Canvas canvas, Size size, Color color) {
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.0
      ..color = color;
    const inset = 4.0;
    const len = 14.0;
    canvas.drawLine(
        const Offset(inset, inset + len), const Offset(inset, inset), paint);
    canvas.drawLine(
        const Offset(inset, inset), const Offset(inset + len, inset), paint);
    canvas.drawLine(Offset(size.width - inset, inset + len),
        Offset(size.width - inset, inset), paint);
    canvas.drawLine(Offset(size.width - inset, inset),
        Offset(size.width - inset - len, inset), paint);
    canvas.drawLine(Offset(inset, size.height - inset - len),
        Offset(inset, size.height - inset), paint);
    canvas.drawLine(Offset(inset, size.height - inset),
        Offset(inset + len, size.height - inset), paint);
    canvas.drawLine(Offset(size.width - inset, size.height - inset - len),
        Offset(size.width - inset, size.height - inset), paint);
    canvas.drawLine(Offset(size.width - inset, size.height - inset),
        Offset(size.width - inset - len, size.height - inset), paint);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class ArcaneCheck extends StatelessWidget {
  const ArcaneCheck({super.key, required this.value, required this.onChanged});
  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onTap: () => onChanged(!value),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 120),
        width: 22,
        height: 22,
        decoration: BoxDecoration(
          color: value ? const Color(0xff0e2635) : const Color(0xff090b0c),
          borderRadius: BorderRadius.circular(4),
          border: Border.all(
              color: value ? const Color(0xff80caff) : const Color(0xff8b5728),
              width: 1.2),
          boxShadow: value
              ? <BoxShadow>[
                  BoxShadow(
                      color: const Color(0xff3f9fff).withValues(alpha: 0.30),
                      blurRadius: 8)
                ]
              : null,
        ),
        child: value
            ? const Icon(Icons.check_rounded,
                size: 17, color: Color(0xff80caff))
            : null,
      ),
    );
  }
}

class ArcaneCardSurface extends StatelessWidget {
  const ArcaneCardSurface({super.key, required this.accent});

  final Color accent;

  @override
  Widget build(BuildContext context) {
    return CustomPaint(painter: ArcaneCardPainter(accent: accent));
  }
}

class ArcaneCardPainter extends CustomPainter {
  ArcaneCardPainter({required this.accent});

  final Color accent;

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final rrect =
        RRect.fromRectAndRadius(rect.deflate(1), const Radius.circular(5));
    canvas.drawRRect(
        rrect,
        Paint()
          ..shader = ui.Gradient.linear(
            rect.topLeft,
            rect.bottomRight,
            <Color>[
              const Color(0xff151819),
              const Color(0xff090a0a),
              Color.lerp(const Color(0xff20100a), accent, 0.14)!,
            ],
            <double>[0.0, 0.58, 1.0],
          ));
    canvas.drawRRect(
        rrect,
        Paint()
          ..blendMode = BlendMode.plus
          ..shader = ui.Gradient.radial(
            Offset(size.width * 0.22, size.height * 0.20),
            size.width * 0.75,
            <Color>[accent.withValues(alpha: 0.18), const Color(0x00000000)],
          ));
    canvas.drawRRect(
        rrect,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1
          ..color = const Color(0xff8b5728).withValues(alpha: 0.86));
    canvas.drawRRect(
        rrect.deflate(4),
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 0.6
          ..color = const Color(0xffffd897).withValues(alpha: 0.18));
  }

  @override
  bool shouldRepaint(covariant ArcaneCardPainter oldDelegate) {
    return oldDelegate.accent != accent;
  }
}

class ArcaneIconBadge extends StatelessWidget {
  const ArcaneIconBadge({super.key, required this.icon, required this.accent});

  final IconData icon;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: RadialGradient(colors: <Color>[
          accent.withValues(alpha: 0.80),
          const Color(0xff101314),
          const Color(0xff050606),
        ], stops: const <double>[
          0.0,
          0.56,
          1.0
        ]),
        border: Border.all(color: const Color(0xffffd897), width: 1),
        boxShadow: <BoxShadow>[
          BoxShadow(color: accent.withValues(alpha: 0.55), blurRadius: 12)
        ],
      ),
      child: Icon(icon, color: const Color(0xfffff1c6), size: 24),
    );
  }
}

class _AutoFitText extends StatelessWidget {
  const _AutoFitText({
    super.key,
    required this.text,
    required this.style,
    required this.maxLines,
    required this.minFontSize,
  });

  final String text;
  final TextStyle style;
  final int maxLines;
  final double minFontSize;

  static final Map<Object, double> _fontSizeCache = <Object, double>{};

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(builder: (context, constraints) {
      final effectiveStyle = DefaultTextStyle.of(context).style.merge(style);
      final maxFontSize = effectiveStyle.fontSize ?? minFontSize;
      final textDirection = Directionality.of(context);
      final textScaler = MediaQuery.textScalerOf(context);
      final locale = Localizations.maybeLocaleOf(context);
      final cacheKey = (
        text,
        constraints.maxWidth,
        constraints.maxHeight,
        maxLines,
        maxFontSize,
        minFontSize,
        textDirection,
        textScaler,
        locale?.toLanguageTag(),
        effectiveStyle,
      );
      var fittedFontSize = _fontSizeCache[cacheKey];
      if (fittedFontSize == null) {
        fittedFontSize = maxFontSize;
        for (var candidate = maxFontSize;
            candidate >= minFontSize;
            candidate -= 0.25) {
          final painter = TextPainter(
            text: TextSpan(
                text: text,
                style: effectiveStyle.copyWith(fontSize: candidate)),
            maxLines: maxLines,
            textDirection: textDirection,
            textScaler: textScaler,
            locale: locale,
          )..layout(maxWidth: constraints.maxWidth);
          final fits = !painter.didExceedMaxLines &&
              painter.height <= constraints.maxHeight + 0.01;
          painter.dispose();
          fittedFontSize = candidate;
          if (fits) break;
        }
        _fontSizeCache[cacheKey] = fittedFontSize!;
      }
      return Text(
        text,
        maxLines: maxLines,
        overflow: TextOverflow.ellipsis,
        style: effectiveStyle.copyWith(fontSize: fittedFontSize),
      );
    });
  }
}

class EventCard extends StatelessWidget {
  const EventCard(
      {super.key,
      required this.icon,
      required this.title,
      required this.body,
      required this.accent});
  final IconData icon;
  final String title;
  final String body;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: SizedBox(
        height: 112,
        child: Stack(
          fit: StackFit.expand,
          children: <Widget>[
            ArcaneCardSurface(accent: accent),
            Positioned(
              left: 15,
              top: 15,
              width: 38,
              height: 38,
              child: ArcaneIconBadge(icon: icon, accent: accent),
            ),
            Positioned(
                left: 58,
                top: 15,
                right: 6,
                height: 42,
                child: _AutoFitText(
                    text: title,
                    maxLines: 2,
                    minFontSize: 8,
                    style: const TextStyle(
                        color: Color(0xfff7d897),
                        fontFamily: 'Georgia',
                        fontWeight: FontWeight.bold,
                        fontSize: 13,
                        height: 1.05))),
            Positioned(
                left: 10,
                top: 58,
                right: 8,
                bottom: 8,
                child: _AutoFitText(
                    text: body,
                    maxLines: 5,
                    minFontSize: 8,
                    style: const TextStyle(
                        color: Color(0xffeedfc4),
                        fontSize: 10.5,
                        height: 1.08))),
          ],
        ),
      ),
    );
  }
}

class SpecialThanksBanner extends StatefulWidget {
  const SpecialThanksBanner({super.key, required this.starProgram});

  final ui.FragmentProgram? starProgram;

  @override
  State<SpecialThanksBanner> createState() => _SpecialThanksBannerState();
}

class _SpecialThanksBannerState extends State<SpecialThanksBanner>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller =
        AnimationController(vsync: this, duration: const Duration(seconds: 74))
          ..repeat();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _showAll() async {
    await showDialog<void>(
      context: context,
      builder: (context) {
        final s = AppStrings.of(context);
        final media = MediaQuery.sizeOf(context);
        final dialogWidth = math.min(math.max(320.0, media.width - 32), 1500.0);
        final dialogHeight =
            math.min(math.max(420.0, media.height - 32), 860.0);
        return Dialog(
          backgroundColor: const Color(0xff101315),
          insetPadding: const EdgeInsets.all(16),
          child: SizedBox(
            width: dialogWidth,
            height: dialogHeight,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(26, 20, 26, 20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Row(children: <Widget>[
                    Expanded(
                      child: Text(s.t('specialThanks'),
                          style: const TextStyle(
                              color: Color(0xfff7d897),
                              fontFamily: 'Georgia',
                              fontWeight: FontWeight.bold,
                              fontSize: 28)),
                    ),
                    IconButton(
                        onPressed: () => Navigator.pop(context),
                        icon: const Icon(Icons.close))
                  ]),
                  const SizedBox(height: 14),
                  Expanded(
                    child: LayoutBuilder(
                      builder: (context, constraints) {
                        final columns = constraints.maxWidth >= 1120 ? 2 : 1;
                        final gap = columns == 2 ? 22.0 : 0.0;
                        final cardWidth = columns == 2
                            ? (constraints.maxWidth - gap) / 2
                            : constraints.maxWidth;
                        final cardHeight = math.min(
                            248.0, math.max(188.0, cardWidth * 116.0 / 360.0));
                        return SingleChildScrollView(
                          child: Wrap(
                            spacing: gap,
                            runSpacing: 22,
                            children: _specialThanksPeople
                                .map((person) => SizedBox(
                                      width: cardWidth,
                                      height: cardHeight,
                                      child: MouseRegion(
                                        cursor: SystemMouseCursors.click,
                                        child: FittedBox(
                                          fit: BoxFit.fill,
                                          child: SizedBox(
                                            width: 360,
                                            height: 116,
                                            child: SpecialThanksCard(
                                                person: person,
                                                starProgram: widget.starProgram,
                                                onTap: () =>
                                                    _showDetail(person)),
                                          ),
                                        ),
                                      ),
                                    ))
                                .toList(),
                          ),
                        );
                      },
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  Future<void> _showDetail(SpecialThanksPerson person) async {
    await showDialog<void>(
      context: context,
      builder: (context) => SpecialThanksDetailDialog(
          person: person, starProgram: widget.starProgram),
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    return Stack(children: <Widget>[
      Positioned.fill(
          child: GestureDetector(
              behavior: HitTestBehavior.opaque,
              onTap: _showAll,
              child: const ArcaneSupporterSurface())),
      Positioned(
        left: 0,
        top: 8,
        right: 0,
        height: 24,
        child: Center(
          child: ColoredBox(
            color: Color(0xee0a090a),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 26, vertical: 2),
              child: Text(s.t('specialThanksCaps'),
                  style: const TextStyle(
                      color: Color(0xfff7d897),
                      fontFamily: 'Georgia',
                      fontWeight: FontWeight.bold,
                      fontSize: 16)),
            ),
          ),
        ),
      ),
      Positioned(
        left: 14,
        top: 38,
        right: 14,
        bottom: 12,
        child: MouseRegion(
          cursor: SystemMouseCursors.click,
          onEnter: (_) => _controller.stop(),
          onExit: (_) => _controller.repeat(),
          child: GestureDetector(
            behavior: HitTestBehavior.translucent,
            onTap: _showAll,
            child: ClipRect(
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: LayoutBuilder(
                  builder: (context, constraints) {
                    const itemWidth = 312.0;
                    const gap = 18.0;
                    final loopWidth =
                        _specialThanksPeople.length * (itemWidth + gap);
                    return AnimatedBuilder(
                      animation: _controller,
                      builder: (context, _) {
                        final offset = -_controller.value * loopWidth;
                        return Transform.translate(
                          offset: Offset(offset, 0),
                          child: SizedBox(
                            width: loopWidth * 2,
                            height: constraints.maxHeight,
                            child: Stack(
                              clipBehavior: Clip.none,
                              children: <Widget>[
                                for (var copy = 0; copy < 2; copy++)
                                  for (var index = 0;
                                      index < _specialThanksPeople.length;
                                      index++)
                                    Positioned(
                                      left: copy * loopWidth +
                                          index * (itemWidth + gap),
                                      top: 2,
                                      width: itemWidth,
                                      height: constraints.maxHeight - 4,
                                      child: SpecialThanksCard(
                                          person: _specialThanksPeople[index],
                                          starProgram: widget.starProgram),
                                    ),
                              ],
                            ),
                          ),
                        );
                      },
                    );
                  },
                ),
              ),
            ),
          ),
        ),
      ),
    ]);
  }
}

class SpecialThanksCard extends StatefulWidget {
  const SpecialThanksCard(
      {super.key, required this.person, required this.starProgram, this.onTap});

  final SpecialThanksPerson person;
  final ui.FragmentProgram? starProgram;
  final VoidCallback? onTap;

  @override
  State<SpecialThanksCard> createState() => _SpecialThanksCardState();
}

class _SpecialThanksCardState extends State<SpecialThanksCard>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  bool _hovered = false;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
        vsync: this, duration: const Duration(milliseconds: 1500));
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _setHovered(bool hovered) {
    if (_hovered == hovered) return;
    setState(() => _hovered = hovered);
    if (hovered) {
      _controller.repeat();
    } else {
      _controller.stop();
      _controller.value = 0;
    }
  }

  @override
  Widget build(BuildContext context) {
    final person = widget.person;
    final s = AppStrings.of(context);
    return MouseRegion(
      onEnter: (_) => _setHovered(true),
      onExit: (_) => _setHovered(false),
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: widget.onTap,
        child: AnimatedScale(
          curve: Curves.easeOutBack,
          duration: const Duration(milliseconds: 190),
          scale: _hovered ? 1.055 : 1.0,
          child: AnimatedBuilder(
            animation: _controller,
            builder: (context, _) => Stack(
              fit: StackFit.expand,
              clipBehavior: Clip.none,
              children: <Widget>[
                ArcaneCardSurface(accent: person.accent),
                CustomPaint(
                    painter: ThanksHoverPainter(
                        program: widget.starProgram,
                        accent: person.accent,
                        time: _controller.value,
                        active: _hovered)),
                Positioned(
                  left: 14,
                  top: 13,
                  width: 62,
                  height: person.prioritySupporter ? 70 : 62,
                  child: person.avatarAsset == null
                      ? person.prioritySupporter
                          ? Align(
                              alignment: Alignment.topCenter,
                              child: PrioritySupporterBadge(active: _hovered),
                            )
                          : const SupporterBadge()
                      : DecoratedBox(
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            border: Border.all(
                                color: const Color(0xffffd897), width: 1.2),
                            boxShadow: <BoxShadow>[
                              BoxShadow(
                                  color: person.accent.withValues(
                                      alpha: _hovered ? 0.80 : 0.36),
                                  blurRadius: _hovered ? 18 : 10)
                            ],
                          ),
                          child: ClipOval(
                            child: Image.asset(_assetKey(person.avatarAsset!),
                                fit: BoxFit.cover,
                                filterQuality: FilterQuality.high),
                          ),
                        ),
                ),
                if (person.supporter)
                  CustomPaint(
                      painter: ThanksBadgeStarPainter(
                          program: widget.starProgram,
                          accent: person.accent,
                          time: _controller.value,
                          active: _hovered)),
                Positioned(
                  left: 88,
                  top: 9,
                  right: 13,
                  bottom: 4,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(person.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                              color: Color(0xfff7d897),
                              fontFamily: 'Georgia',
                              fontSize: 17)),
                      const SizedBox(height: 2),
                      Expanded(
                        child: _AutoFitText(
                            text: person.description,
                            key: ValueKey(
                                'special-thanks-description-${person.name}'),
                            maxLines: 3,
                            minFontSize: 10,
                            style: const TextStyle(
                                color: Color(0xffeedfc4),
                                fontSize: 12.5,
                                height: 1.12)),
                      ),
                      if (person.supporter) ...<Widget>[
                        const SizedBox(height: 2),
                        Align(
                          alignment: Alignment.centerLeft,
                          child: Container(
                            key:
                                ValueKey('special-thanks-badge-${person.name}'),
                            padding: const EdgeInsets.symmetric(
                                horizontal: 12, vertical: 2),
                            decoration: BoxDecoration(
                              border: Border.all(
                                  color: person.prioritySupporter
                                      ? const Color(0xffe6b6ff)
                                      : const Color(0xffe0a956)),
                              borderRadius: BorderRadius.circular(4),
                              color: person.prioritySupporter
                                  ? const Color(0x66351050)
                                  : const Color(0x332a1405),
                              boxShadow: _hovered
                                  ? <BoxShadow>[
                                      BoxShadow(
                                          color: (person.prioritySupporter
                                                  ? const Color(0xffe3a3ff)
                                                  : const Color(0xffffd897))
                                              .withValues(alpha: 0.40),
                                          blurRadius: 12)
                                    ]
                                  : null,
                            ),
                            child: Text(
                                s.t(person.prioritySupporter
                                    ? 'prioritySupporter'
                                    : 'supporter'),
                                style: TextStyle(
                                    color: person.prioritySupporter
                                        ? const Color(0xffffe8ff)
                                        : const Color(0xfff7d897),
                                    fontWeight: person.prioritySupporter
                                        ? FontWeight.w700
                                        : FontWeight.normal,
                                    letterSpacing:
                                        person.prioritySupporter ? 0.45 : null,
                                    fontSize: 11)),
                          ),
                        ),
                        const SizedBox(height: 6),
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class SpecialThanksDetailDialog extends StatefulWidget {
  const SpecialThanksDetailDialog(
      {super.key,
      required this.person,
      required this.starProgram,
      this.openUrl});

  final SpecialThanksPerson person;
  final ui.FragmentProgram? starProgram;
  final Future<void> Function(String url)? openUrl;

  @override
  State<SpecialThanksDetailDialog> createState() =>
      _SpecialThanksDetailDialogState();
}

class _SpecialThanksDetailDialogState extends State<SpecialThanksDetailDialog> {
  Offset _tilt = Offset.zero;
  bool _returningToRest = false;

  Future<void> _openFeature() async {
    final url = widget.person.featureUrl;
    if (url == null) return;
    await (widget.openUrl ?? _openExternalUrl)(url);
  }

  @override
  Widget build(BuildContext context) {
    final media = MediaQuery.sizeOf(context);
    const aspect = 360.0 / 116.0;
    const featureGap = 18.0;
    final hasFeature =
        widget.person.featureAsset != null && widget.person.featureUrl != null;
    var width = math.min(media.width - 72, 1120.0);
    var height = width / aspect;
    final maxHeight = math.min(media.height - 96, 430.0);
    var featureWidth = 0.0;
    var cardWidth = width;
    if (hasFeature) {
      featureWidth = math.min(236.0, math.max(176.0, width * 0.24));
      cardWidth = width - featureWidth - featureGap;
      height = cardWidth / aspect;
      if (height > maxHeight) {
        height = maxHeight;
        cardWidth = height * aspect;
        width = cardWidth + featureGap + featureWidth;
      }
    } else if (height > maxHeight) {
      height = maxHeight;
      width = height * aspect;
      cardWidth = width;
    }
    width = math.max(width, 360.0);
    if (!hasFeature) {
      height = width / aspect;
      cardWidth = width;
    }

    return Dialog(
      backgroundColor: Colors.transparent,
      insetPadding: const EdgeInsets.all(28),
      child: MouseRegion(
        onHover: (event) {
          final center = Offset(media.width * 0.5, media.height * 0.5);
          final dx = ((event.position.dx - center.dx) / (width * 0.5))
              .clamp(-1.0, 1.0)
              .toDouble();
          final dy = ((event.position.dy - center.dy) / (height * 0.5))
              .clamp(-1.0, 1.0)
              .toDouble();
          final nextTilt = Offset(dx, dy);
          if ((nextTilt - _tilt).distanceSquared < 0.000025 &&
              !_returningToRest) {
            return;
          }
          setState(() {
            _returningToRest = false;
            _tilt = nextTilt;
          });
        },
        onExit: (_) => setState(() {
          _returningToRest = true;
          _tilt = Offset.zero;
        }),
        child: TweenAnimationBuilder<Offset>(
          tween: Tween<Offset>(end: _tilt),
          duration: Duration(milliseconds: _returningToRest ? 720 : 120),
          curve: _returningToRest ? Curves.easeOutExpo : Curves.easeOutCubic,
          child: Stack(
            clipBehavior: Clip.none,
            children: <Widget>[
              SizedBox(
                width: width,
                height: height,
                child: Row(
                  children: <Widget>[
                    SizedBox(
                      width: cardWidth,
                      height: height,
                      child: FittedBox(
                        fit: BoxFit.fill,
                        child: SizedBox(
                          width: 360,
                          height: 116,
                          child: SpecialThanksCard(
                              person: widget.person,
                              starProgram: widget.starProgram),
                        ),
                      ),
                    ),
                    if (hasFeature) ...<Widget>[
                      const SizedBox(width: featureGap),
                      SizedBox(
                        width: featureWidth,
                        height: height,
                        child: SpecialThanksFeatureLink(
                          asset: widget.person.featureAsset!,
                          onTap: () => unawaited(_openFeature()),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
              Positioned(
                right: -18,
                top: -18,
                child: IconButton(
                    color: const Color(0xffeedfc4),
                    style: IconButton.styleFrom(
                        backgroundColor: const Color(0xdd101315)),
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.close)),
              ),
            ],
          ),
          builder: (context, tilt, child) {
            final transform = Matrix4.identity()
              ..setEntry(3, 2, 0.0018)
              ..rotateX(-tilt.dy * 0.23)
              ..rotateY(tilt.dx * 0.30);
            return Transform(
              transform: transform,
              alignment: Alignment.center,
              child: child,
            );
          },
        ),
      ),
    );
  }
}

class SpecialThanksFeatureLink extends StatefulWidget {
  const SpecialThanksFeatureLink(
      {super.key, required this.asset, required this.onTap});

  final String asset;
  final VoidCallback onTap;

  @override
  State<SpecialThanksFeatureLink> createState() =>
      _SpecialThanksFeatureLinkState();
}

class _SpecialThanksFeatureLinkState extends State<SpecialThanksFeatureLink> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      button: true,
      label: 'Open Bitesquid Mod Loader on Steam Workshop',
      child: Tooltip(
        message: 'Open Bitesquid Mod Loader on Steam Workshop',
        child: MouseRegion(
          cursor: SystemMouseCursors.click,
          onEnter: (_) => setState(() => _hovered = true),
          onExit: (_) => setState(() => _hovered = false),
          child: GestureDetector(
            key: const ValueKey<String>('skappnil-mod-loader-link'),
            behavior: HitTestBehavior.opaque,
            onTap: widget.onTap,
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 160),
              padding: const EdgeInsets.all(9),
              decoration: BoxDecoration(
                color: _hovered
                    ? const Color(0xff241735)
                    : const Color(0xee101315),
                border: Border.all(
                    color: _hovered
                        ? const Color(0xffd7a5ff)
                        : const Color(0xff76508f),
                    width: _hovered ? 1.8 : 1.2),
                borderRadius: BorderRadius.circular(7),
                boxShadow: <BoxShadow>[
                  BoxShadow(
                    color: const Color(0xffab4fff)
                        .withValues(alpha: _hovered ? 0.50 : 0.22),
                    blurRadius: _hovered ? 20 : 10,
                  ),
                ],
              ),
              child: Column(
                children: <Widget>[
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: Image.asset(
                        _assetKey(widget.asset),
                        key: const ValueKey<String>('skappnil-mod-loader-logo'),
                        fit: BoxFit.contain,
                        filterQuality: FilterQuality.high,
                      ),
                    ),
                  ),
                  const SizedBox(height: 5),
                  const Text(
                    'Bitesquid Mod Loader',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      color: Color(0xfff7d897),
                      fontFamily: 'Georgia',
                      fontWeight: FontWeight.bold,
                      fontSize: 13,
                    ),
                  ),
                  const SizedBox(height: 2),
                  const Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: <Widget>[
                      Icon(Icons.open_in_new,
                          size: 13, color: Color(0xffd7a5ff)),
                      SizedBox(width: 5),
                      Flexible(
                        child: Text(
                          'Steam Workshop',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(
                            color: Color(0xffead8f7),
                            decoration: TextDecoration.underline,
                            decorationColor: Color(0xffd7a5ff),
                            fontSize: 11.5,
                          ),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class ThanksHoverPainter extends CustomPainter {
  ThanksHoverPainter({
    required this.program,
    required this.accent,
    required this.time,
    required this.active,
  });

  final ui.FragmentProgram? program;
  final Color accent;
  final double time;
  final bool active;

  @override
  void paint(Canvas canvas, Size size) {
    if (!active) return;
    final rect = Offset.zero & size;
    final pulse = 0.5 + 0.5 * math.sin(time * math.pi * 2);
    final aura = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.radial(
        Offset(size.width * (0.36 + 0.20 * math.sin(time * math.pi * 2)),
            size.height * 0.54),
        size.width * 0.68,
        <Color>[
          accent.withValues(alpha: 0.22 + pulse * 0.10),
          const Color(0x00000000),
        ],
      );
    canvas.drawRRect(
        RRect.fromRectAndRadius(rect.deflate(1), const Radius.circular(5)),
        aura);

    final shimmerX = -size.width * 0.45 + time * size.width * 1.90;
    final glow = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.linear(
        Offset(shimmerX - 44, 0),
        Offset(shimmerX + 44, size.height),
        <Color>[
          const Color(0x00ffffff),
          Colors.white.withValues(alpha: 0.34),
          accent.withValues(alpha: 0.45),
          const Color(0x00ffffff),
        ],
        const <double>[0.0, 0.42, 0.58, 1.0],
      );
    canvas.drawRRect(
        RRect.fromRectAndRadius(rect.deflate(2), const Radius.circular(5)),
        glow);

    final veil = Paint()
      ..blendMode = BlendMode.plus
      ..shader = ui.Gradient.linear(
        Offset(0, size.height),
        Offset(size.width, 0),
        <Color>[
          const Color(0x00ffffff),
          accent.withValues(alpha: 0.10 + pulse * 0.08),
          const Color(0x00ffffff),
        ],
        const <double>[0.0, 0.52, 1.0],
      );
    canvas.drawRRect(
        RRect.fromRectAndRadius(rect.deflate(4), const Radius.circular(5)),
        veil);

    for (var i = 0; i < 9; i++) {
      final seed = i * 37.17;
      final phase = (time + i * 0.113) % 1.0;
      final x = (0.10 + 0.82 * ((math.sin(seed) * 43758.5453).abs() % 1.0)) *
          size.width;
      final y =
          (0.14 + 0.72 * ((math.sin(seed + 9.91) * 19341.913).abs() % 1.0)) *
              size.height;
      final rise = (phase - 0.5) * size.height * 0.16;
      final sparkleAlpha =
          (1.0 - (phase - 0.5).abs() * 2.0).clamp(0.0, 1.0).toDouble();
      final center = Offset(x, y - rise);
      final radius = 0.8 + (i % 3) * 0.28;
      final tipLength = 5.5 + (i % 4) * 5.0;
      final color = Color.lerp(accent, const Color(0xfffff1b4), 0.55)!;
      final alpha = sparkleAlpha * (0.40 + pulse * 0.36);
      final angle = time * math.pi * 2 + seed;
      if (program == null) {
        _drawFallbackShaderLikeStar(
            canvas, center, radius, tipLength, color, alpha, angle);
      } else {
        _drawShaderStar(
            canvas, center, radius, color, alpha, angle, tipLength, pulse);
      }
    }

    final rim = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.3 + pulse * 0.6
      ..blendMode = BlendMode.plus
      ..color = Color.lerp(accent, const Color(0xfffff3b0),
              0.45 + 0.30 * math.sin(time * math.pi * 2))!
          .withValues(alpha: 0.95);
    canvas.drawRRect(
        RRect.fromRectAndRadius(rect.deflate(1.4), const Radius.circular(5)),
        rim);
  }

  void _drawShaderStar(Canvas canvas, Offset center, double radius, Color color,
      double alpha, double angle, double tipLength, double brightness) {
    if (alpha <= 0.01) return;
    final side =
        (radius * (12.0 + tipLength * 5.5)).clamp(22.0, 180.0).toDouble();
    final shader = program!.fragmentShader()
      ..setFloat(0, side)
      ..setFloat(1, side)
      ..setFloat(2, angle + brightness * 8.0)
      ..setFloat(3, alpha)
      ..setFloat(4, 0.24)
      ..setFloat(5, 0.038)
      ..setFloat(6, (0.38 + tipLength * 0.12).clamp(0.10, 7.00).toDouble())
      ..setFloat(7, (900.0 + tipLength * 115.0).clamp(500.0, 9000.0).toDouble())
      ..setFloat(8, 1.45)
      ..setFloat(9, 0.22)
      ..setFloat(10, color.r)
      ..setFloat(11, color.g)
      ..setFloat(12, color.b);

    final paint = Paint()
      ..shader = shader
      ..blendMode = BlendMode.plus;
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(angle);
    canvas.translate(-side * 0.5, -side * 0.5);
    canvas.drawRect(Offset.zero & Size(side, side), paint);
    canvas.restore();
  }

  void _drawFallbackShaderLikeStar(Canvas canvas, Offset center, double radius,
      double tipLength, Color color, double alpha, double angle) {
    if (alpha <= 0.01) return;
    final glow = Paint()
      ..blendMode = BlendMode.plus
      ..color = color.withValues(alpha: alpha * 0.18)
      ..maskFilter = MaskFilter.blur(BlurStyle.normal, radius * 5.5);
    canvas.drawCircle(center, radius * 7.5, glow);

    final rayLength = radius * (5.5 + tipLength * 0.65);
    final ray = Paint()
      ..blendMode = BlendMode.plus
      ..strokeCap = StrokeCap.round
      ..strokeWidth = math.max(0.35, radius * 0.22)
      ..color = color.withValues(alpha: alpha);
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(angle);
    canvas.drawLine(Offset(-rayLength, 0), Offset(rayLength, 0), ray);
    canvas.drawLine(
        Offset(0, -rayLength * 0.62), Offset(0, rayLength * 0.62), ray);
    ray
      ..strokeWidth = math.max(0.25, radius * 0.14)
      ..color = color.withValues(alpha: alpha * 0.46);
    canvas.rotate(math.pi / 4);
    canvas.drawLine(
        Offset(-rayLength * 0.34, 0), Offset(rayLength * 0.34, 0), ray);
    canvas.drawLine(
        Offset(0, -rayLength * 0.34), Offset(0, rayLength * 0.34), ray);
    canvas.restore();

    final core = Paint()
      ..blendMode = BlendMode.plus
      ..color = Colors.white.withValues(alpha: alpha);
    canvas.drawCircle(center, radius * 0.58, core);
  }

  @override
  bool shouldRepaint(covariant ThanksHoverPainter oldDelegate) {
    return oldDelegate.program != program ||
        oldDelegate.accent != accent ||
        oldDelegate.time != time ||
        oldDelegate.active != active;
  }
}

class ThanksBadgeStarPainter extends CustomPainter {
  ThanksBadgeStarPainter({
    required this.program,
    required this.accent,
    required this.time,
    required this.active,
  });

  final ui.FragmentProgram? program;
  final Color accent;
  final double time;
  final bool active;

  @override
  void paint(Canvas canvas, Size size) {
    if (!active) return;
    final badgeCenter = const Offset(45, 44);
    final supporterY = size.height - 17;
    final stars = <_BadgeStar>[
      _BadgeStar(badgeCenter.translate(26, -18), 14, 13.0, 0.00),
      _BadgeStar(badgeCenter.translate(32, 10), 10, 8.0, 0.22),
      _BadgeStar(badgeCenter.translate(-18, -24), 8, 6.5, 0.42),
      _BadgeStar(Offset(114, supporterY), 9, 7.5, 0.58),
      _BadgeStar(Offset(166, supporterY - 2), 7, 5.8, 0.74),
    ];

    for (final star in stars) {
      final pulse = 0.55 + 0.45 * math.sin((time + star.phase) * math.pi * 2.0);
      final alpha = (0.34 + pulse * 0.48).clamp(0.0, 1.0).toDouble();
      final color = Color.lerp(accent, const Color(0xfffff0b0), 0.68)!;
      if (program == null) {
        _drawFallbackStar(canvas, star.center, star.radius, star.tipLength,
            color, alpha, time + star.phase);
      } else {
        _drawShaderStar(canvas, star.center, star.radius, star.tipLength, color,
            alpha, time + star.phase);
      }
    }
  }

  void _drawShaderStar(Canvas canvas, Offset center, double radius,
      double tipLength, Color color, double alpha, double phase) {
    final side = radius * (3.8 + tipLength * 0.26);
    final shader = program!.fragmentShader()
      ..setFloat(0, side)
      ..setFloat(1, side)
      ..setFloat(2, phase * math.pi * 2.0)
      ..setFloat(3, alpha)
      ..setFloat(4, 0.22)
      ..setFloat(5, 0.038)
      ..setFloat(6, (0.70 + tipLength * 0.10).clamp(0.4, 3.0).toDouble())
      ..setFloat(7, (1500.0 + tipLength * 70.0).clamp(900.0, 2800.0).toDouble())
      ..setFloat(8, 2.2)
      ..setFloat(9, 0.22)
      ..setFloat(10, color.r)
      ..setFloat(11, color.g)
      ..setFloat(12, color.b);
    final paint = Paint()
      ..shader = shader
      ..blendMode = BlendMode.plus;
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(phase * math.pi * 2.0);
    canvas.translate(-side * 0.5, -side * 0.5);
    canvas.drawRect(Offset.zero & Size(side, side), paint);
    canvas.restore();
  }

  void _drawFallbackStar(Canvas canvas, Offset center, double radius,
      double tipLength, Color color, double alpha, double phase) {
    final length = radius * (0.7 + tipLength * 0.08);
    final paint = Paint()
      ..blendMode = BlendMode.plus
      ..strokeCap = StrokeCap.round
      ..strokeWidth = math.max(0.35, radius * 0.10)
      ..color = color.withValues(alpha: alpha);
    canvas.save();
    canvas.translate(center.dx, center.dy);
    canvas.rotate(phase * math.pi * 2.0);
    canvas.drawLine(Offset(-length, 0), Offset(length, 0), paint);
    canvas.drawLine(Offset(0, -length * 0.7), Offset(0, length * 0.7), paint);
    paint
      ..strokeWidth = math.max(0.25, radius * 0.06)
      ..color = color.withValues(alpha: alpha * 0.46);
    canvas.rotate(math.pi / 4);
    canvas.drawLine(Offset(-length * 0.48, 0), Offset(length * 0.48, 0), paint);
    canvas.drawLine(Offset(0, -length * 0.48), Offset(0, length * 0.48), paint);
    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant ThanksBadgeStarPainter oldDelegate) {
    return oldDelegate.program != program ||
        oldDelegate.accent != accent ||
        oldDelegate.time != time ||
        oldDelegate.active != active;
  }
}

class _BadgeStar {
  const _BadgeStar(this.center, this.radius, this.tipLength, this.phase);

  final Offset center;
  final double radius;
  final double tipLength;
  final double phase;
}

class ArcaneSupporterSurface extends StatelessWidget {
  const ArcaneSupporterSurface({super.key});

  @override
  Widget build(BuildContext context) {
    return CustomPaint(painter: ArcaneSupporterPainter());
  }
}

class ArcaneSupporterPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final rrect =
        RRect.fromRectAndRadius(rect.deflate(1), const Radius.circular(5));
    canvas.drawRRect(
        rrect,
        Paint()
          ..shader = ui.Gradient.linear(
            rect.topLeft,
            rect.bottomRight,
            <Color>[
              const Color(0xff21080b),
              const Color(0xff130506),
              const Color(0xff2f0a0d),
            ],
            <double>[0.0, 0.52, 1.0],
          ));
    canvas.drawRRect(
        rrect,
        Paint()
          ..blendMode = BlendMode.plus
          ..shader = ui.Gradient.linear(
            Offset.zero,
            Offset(size.width, 0),
            <Color>[
              const Color(0x00d9a04f),
              const Color(0x44d9a04f),
              const Color(0x00d9a04f),
            ],
            <double>[0.0, 0.50, 1.0],
          ));
    final border = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.1
      ..color = const Color(0xff8b5728);
    canvas.drawRRect(rrect, border);
    final line = Paint()
      ..strokeWidth = 0.7
      ..color = const Color(0xffffd897).withValues(alpha: 0.26);
    canvas.drawLine(Offset(18, 14), Offset(size.width - 18, 14), line);
    canvas.drawLine(Offset(18, size.height - 12),
        Offset(size.width - 18, size.height - 12), line);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class SupporterBadge extends StatelessWidget {
  const SupporterBadge({super.key});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
        width: 54,
        height: 60,
        child: CustomPaint(painter: SupporterBadgePainter()));
  }
}

class SupporterBadgePainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final w = size.width;
    final h = size.height;
    final shield = Path()
      ..moveTo(w * 0.50, h * 0.04)
      ..lineTo(w * 0.86, h * 0.18)
      ..lineTo(w * 0.78, h * 0.72)
      ..lineTo(w * 0.50, h * 0.96)
      ..lineTo(w * 0.22, h * 0.72)
      ..lineTo(w * 0.14, h * 0.18)
      ..close();
    canvas.drawPath(
        shield,
        Paint()
          ..shader = ui.Gradient.linear(
            Offset.zero,
            Offset(w, h),
            const <Color>[
              Color(0xff3a3327),
              Color(0xff101010),
              Color(0xff4a3418),
            ],
            const <double>[0.0, 0.55, 1.0],
          ));
    canvas.drawPath(
        shield,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.4
          ..color = const Color(0xffffd897));

    final star = Path();
    for (var i = 0; i < 10; i++) {
      final angle = -math.pi / 2 + i * math.pi / 5;
      final radius = i.isEven ? w * 0.22 : w * 0.09;
      final point = Offset(w * 0.5 + math.cos(angle) * radius,
          h * 0.43 + math.sin(angle) * radius);
      if (i == 0) {
        star.moveTo(point.dx, point.dy);
      } else {
        star.lineTo(point.dx, point.dy);
      }
    }
    star.close();
    canvas.drawPath(
        star,
        Paint()
          ..shader = ui.Gradient.linear(
            const Offset(0, 0),
            const Offset(40, 46),
            const <Color>[Color(0xfffff3b0), Color(0xffd99a31)],
          ));
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

class PrioritySupporterBadge extends StatefulWidget {
  const PrioritySupporterBadge({super.key, required this.active});

  static const Duration sweepDuration = Duration(milliseconds: 5600);

  final bool active;

  @override
  State<PrioritySupporterBadge> createState() => _PrioritySupporterBadgeState();
}

class _PrioritySupporterBadgeState extends State<PrioritySupporterBadge>
    with SingleTickerProviderStateMixin {
  static final Map<String, Future<ui.FragmentProgram>> _programLoads =
      <String, Future<ui.FragmentProgram>>{};

  late final AnimationController _controller;
  ui.FragmentProgram? _edgeStarProgram;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
        vsync: this, duration: PrioritySupporterBadge.sweepDuration);
    if (widget.active) {
      _controller.repeat();
    }
    _loadEdgeStarProgram();
  }

  Future<void> _loadEdgeStarProgram() async {
    final asset = _assetKey('shaders/diamond_edge_star.frag');
    final future = _programLoads[asset] ??= ui.FragmentProgram.fromAsset(asset);
    try {
      final program = await future;
      if (mounted) setState(() => _edgeStarProgram = program);
    } catch (_) {
      if (identical(_programLoads[asset], future)) {
        _programLoads.remove(asset);
      }
    }
  }

  @override
  void didUpdateWidget(covariant PrioritySupporterBadge oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.active == widget.active) return;
    if (widget.active) {
      _controller
        ..value = 0
        ..repeat();
    } else {
      _controller
        ..stop()
        ..value = 0;
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
        width: 60,
        height: 70,
        child: AnimatedBuilder(
            animation: _controller,
            builder: (context, _) => CustomPaint(
                painter: PrioritySupporterBadgePainter(
                    time: _controller.value,
                    active: widget.active,
                    edgeStarProgram: _edgeStarProgram))));
  }
}

class PrioritySupporterBadgePainter extends CustomPainter {
  PrioritySupporterBadgePainter(
      {required this.time, required this.active, this.edgeStarProgram});

  static const double upperDesignHeight = 62.0;
  static const double bottomStrokeMargin = 20.0;

  final double time;
  final bool active;
  final ui.FragmentProgram? edgeStarProgram;

  @visibleForTesting
  static List<Offset> diamondVerticesFor(Size size) => <Offset>[
        Offset(size.width * 0.29, upperDesignHeight * 0.12),
        Offset(size.width * 0.71, upperDesignHeight * 0.12),
        Offset(size.width * 0.90, upperDesignHeight * 0.35),
        Offset(size.width * 0.50, size.height - bottomStrokeMargin),
        Offset(size.width * 0.10, upperDesignHeight * 0.35),
      ];

  @visibleForTesting
  static List<Offset> diamondTableVerticesFor(Size size) => <Offset>[
        Offset(size.width * 0.40, upperDesignHeight * 0.20),
        Offset(size.width * 0.60, upperDesignHeight * 0.20),
        Offset(size.width * 0.67, upperDesignHeight * 0.35),
        Offset(size.width * 0.33, upperDesignHeight * 0.35),
      ];

  @visibleForTesting
  static Offset get causticDirection {
    const raw = Offset(0.18, -1.0);
    return raw / raw.distance;
  }

  @visibleForTesting
  static double causticBandHalfWidthFor(Size size) =>
      math.max(size.width, size.height) * 0.30;

  @visibleForTesting
  static Offset causticCenterFor(Size size, double progress) {
    final vertices = diamondVerticesFor(size);
    final direction = causticDirection;
    double projection(Offset point) =>
        point.dx * direction.dx + point.dy * direction.dy;
    final projections = vertices.map(projection);
    final minProjection = projections.reduce(math.min);
    final maxProjection = projections.reduce(math.max);
    final halfWidth = causticBandHalfWidthFor(size);
    final offscreenMargin = math.max(size.width, size.height) * 0.22;
    final startProjection = minProjection - halfWidth - offscreenMargin;
    final endProjection = maxProjection + halfWidth + offscreenMargin;
    final targetProjection = ui.lerpDouble(
        startProjection, endProjection, progress.clamp(0.0, 1.0))!;
    final anchor = Offset(size.width * 0.50, size.height * 0.53);
    final anchorProjection = projection(anchor);
    return anchor + direction * (targetProjection - anchorProjection);
  }

  @override
  void paint(Canvas canvas, Size size) {
    final w = size.width;
    final h = size.height;
    final pulse = active ? 0.5 + 0.5 * math.sin(time * math.pi * 2.0) : 0.35;

    final vertices = diamondVerticesFor(size);
    final topLeft = vertices[0];
    final topRight = vertices[1];
    final right = vertices[2];
    final bottom = vertices[3];
    final left = vertices[4];
    final tableVertices = diamondTableVerticesFor(size);
    final tableTopLeft = tableVertices[0];
    final tableTopRight = tableVertices[1];
    final tableBottomRight = tableVertices[2];
    final tableBottomLeft = tableVertices[3];

    Path facet(List<Offset> points) {
      final path = Path()..moveTo(points.first.dx, points.first.dy);
      for (final point in points.skip(1)) {
        path.lineTo(point.dx, point.dy);
      }
      return path..close();
    }

    final diamond = facet(<Offset>[
      topLeft,
      topRight,
      right,
      bottom,
      left,
    ]);

    // A single brilliant-cut silhouette. The soft glow reuses the same path,
    // so the badge remains one emblem rather than a stack of rank symbols.
    canvas.drawPath(
        diamond,
        Paint()
          ..color = const Color(0xffa96dff)
              .withValues(alpha: active ? 0.24 + pulse * 0.18 : 0.11)
          ..maskFilter = MaskFilter.blur(BlurStyle.normal, active ? 8 : 4));
    canvas.drawPath(
        diamond,
        Paint()
          ..shader = ui.Gradient.linear(
            Offset(w * 0.12, h * 0.10),
            Offset(w * 0.84, h * 0.90),
            const <Color>[
              Color(0xff22447c),
              Color(0xff4b1b78),
              Color(0xff170f32),
            ],
            const <double>[0.0, 0.48, 1.0],
          ));

    final facets = <(Path, Color)>[
      (
        facet(<Offset>[left, topLeft, tableTopLeft, tableBottomLeft]),
        const Color(0xff5ddcff),
      ),
      (
        facet(<Offset>[topLeft, topRight, tableTopRight, tableTopLeft]),
        const Color(0xffae8fff),
      ),
      (
        facet(<Offset>[topRight, right, tableBottomRight, tableTopRight]),
        const Color(0xffff7bd8),
      ),
      (
        facet(<Offset>[left, tableBottomLeft, bottom]),
        const Color(0xff244bb2),
      ),
      (
        facet(<Offset>[tableBottomLeft, tableBottomRight, bottom]),
        const Color(0xff7132a9),
      ),
      (
        facet(<Offset>[tableBottomRight, right, bottom]),
        const Color(0xffb12c91),
      ),
    ];
    for (final (path, color) in facets) {
      canvas.drawPath(
          path, Paint()..color = color.withValues(alpha: active ? 0.48 : 0.34));
    }

    final table = facet(<Offset>[
      tableTopLeft,
      tableTopRight,
      tableBottomRight,
      tableBottomLeft,
    ]);
    canvas.drawPath(
        table,
        Paint()
          ..shader = ui.Gradient.linear(
            tableTopLeft,
            tableBottomRight,
            <Color>[
              const Color(0xffe9fdff).withValues(alpha: active ? 0.88 : 0.72),
              const Color(0xffa779ff).withValues(alpha: active ? 0.72 : 0.58),
              const Color(0xff4a226f).withValues(alpha: active ? 0.78 : 0.64),
            ],
            const <double>[0.0, 0.46, 1.0],
          ));

    final seamPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.65
      ..strokeJoin = StrokeJoin.round
      ..color = const Color(0xffeefaff).withValues(alpha: active ? 0.34 : 0.22);
    for (final (path, _) in facets) {
      canvas.drawPath(path, seamPaint);
    }
    canvas.drawPath(table, seamPaint);

    if (active) {
      // This slow, upward-moving caustic has its own cycle and direction. Its
      // transparent band begins and ends beyond the diamond, so wrapping the
      // controller can never make the light visibly pop in or out.
      final direction = causticDirection;
      final halfWidth = causticBandHalfWidthFor(size);
      final center = causticCenterFor(size, time);
      canvas.save();
      canvas.clipPath(diamond);
      canvas.drawRect(
          Offset.zero & size,
          Paint()
            ..blendMode = BlendMode.screen
            ..shader = ui.Gradient.linear(
              center - direction * halfWidth,
              center + direction * halfWidth,
              const <Color>[
                Color(0x0000e5ff),
                Color(0x661fffff),
                Color(0xbbeffeff),
                Color(0x77ff49ce),
                Color(0x00ffd56b),
              ],
              const <double>[0.0, 0.38, 0.50, 0.62, 1.0],
            ));
      canvas.restore();
    }

    canvas.drawPath(
        diamond,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = active ? 1.8 : 1.35
          ..strokeJoin = StrokeJoin.round
          ..shader = ui.Gradient.linear(
            left,
            right,
            const <Color>[
              Color(0xff77efff),
              Color(0xffb98aff),
              Color(0xffff83d8),
              Color(0xffffd878),
            ],
            const <double>[0.0, 0.34, 0.68, 1.0],
          ));

    if (active) {
      _drawDiamondEdgeStar(canvas, Offset.lerp(topLeft, left, 0.00)!, time,
          0.10, const ui.Color.fromARGB(255, 183, 0, 255));
      _drawDiamondEdgeStar(canvas, Offset.lerp(topRight, right, 0.34)!, time,
          0.30, const Color(0xfff3fdff));
      _drawDiamondEdgeStar(canvas, Offset.lerp(left, bottom, 0.62)!, time, 0.72,
          const Color(0xffffdfff));
    }
  }

  @visibleForTesting
  static double sparkleEnvelopeFor(double time, double centerTime) {
    const halfWindow = 0.10;
    final distance = (time - centerTime).abs();
    if (distance >= halfWindow) return 0;
    final x = 1.0 - distance / halfWindow;
    final smooth = x * x * (3.0 - 2.0 * x);
    return smooth * smooth;
  }

  void _drawDiamondEdgeStar(Canvas canvas, Offset center, double time,
      double centerTime, Color color) {
    final envelope = sparkleEnvelopeFor(time, centerTime);
    if (envelope == 0) return;
    if (edgeStarProgram == null) {
      canvas.drawCircle(
          center,
          0.7 + envelope * 0.55,
          Paint()
            ..blendMode = BlendMode.plus
            ..color = color.withValues(alpha: envelope * 0.88));
      return;
    }

    const side = 84.0;
    final shader = edgeStarProgram!.fragmentShader()
      ..setFloat(0, side)
      ..setFloat(1, side)
      ..setFloat(2, 0.72 + envelope * 0.18)
      ..setFloat(3, envelope)
      ..setFloat(4, color.r)
      ..setFloat(5, color.g)
      ..setFloat(6, color.b);
    canvas.save();
    canvas.translate(center.dx - side * 0.5, center.dy - side * 0.5);
    canvas.drawRect(
        const Offset(0, 0) & const Size(side, side),
        Paint()
          ..shader = shader
          ..blendMode = BlendMode.plus);
    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant PrioritySupporterBadgePainter oldDelegate) {
    return oldDelegate.time != time ||
        oldDelegate.active != active ||
        oldDelegate.edgeStarProgram != edgeStarProgram;
  }
}

class ThanksCard extends StatelessWidget {
  const ThanksCard(
      {super.key,
      required this.avatarAsset,
      required this.title,
      required this.body,
      required this.accent});
  final String avatarAsset;
  final String title;
  final String body;
  final Color accent;

  @override
  Widget build(BuildContext context) {
    return Stack(
      fit: StackFit.expand,
      children: <Widget>[
        Positioned.fill(child: ArcaneCardSurface(accent: accent)),
        Positioned(
          left: 15,
          top: 12,
          width: 64,
          height: 64,
          child: DecoratedBox(
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              border: Border.all(color: const Color(0xffffd897), width: 1.2),
              boxShadow: <BoxShadow>[
                BoxShadow(color: accent.withValues(alpha: 0.45), blurRadius: 11)
              ],
            ),
            child: ClipOval(
              child: Image.asset(_assetKey(avatarAsset),
                  fit: BoxFit.cover, filterQuality: FilterQuality.high),
            ),
          ),
        ),
        Positioned(
            left: 92,
            top: 13,
            right: 12,
            height: 24,
            child: Text(title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                    color: Color(0xfff7d897),
                    fontFamily: 'Georgia',
                    fontSize: 16))),
        Positioned(
            left: 92,
            top: 42,
            right: 12,
            bottom: 10,
            child: Text(body,
                maxLines: 3,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                    color: Color(0xffeedfc4), fontSize: 13, height: 1.12))),
      ],
    );
  }
}

class SectionHeading extends StatelessWidget {
  const SectionHeading({super.key, required this.text});
  final String text;

  @override
  Widget build(BuildContext context) {
    return Row(children: <Widget>[
      const Expanded(child: Divider(color: Color(0x99845025))),
      Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: Text(text,
              style: const TextStyle(
                  color: Color(0xfff7d897),
                  fontFamily: 'Georgia',
                  fontWeight: FontWeight.bold,
                  fontSize: 16))),
      const Expanded(child: Divider(color: Color(0x99845025))),
    ]);
  }
}

class OrnateFrame extends StatelessWidget {
  const OrnateFrame({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
        margin: const EdgeInsets.all(7),
        decoration:
            BoxDecoration(border: Border.all(color: const Color(0xaa845025))));
  }
}
