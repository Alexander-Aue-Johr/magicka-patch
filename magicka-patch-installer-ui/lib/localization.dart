import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

class AppLocaleSelection {
  const AppLocaleSelection({
    required this.language,
    required this.systemLocale,
    required this.source,
    required this.requestedLocale,
  });

  final AppLanguage language;
  final Locale systemLocale;
  final String source;
  final String requestedLocale;

  String get resolvedLocaleTag => language.localeTag;
  String get systemLocaleTag => _localeTag(systemLocale);
}

enum AppLanguage {
  esAR('Argentina', 'es', 'AR', 'es-AR'),
  ruRU('Russia', 'ru', 'RU', 'ru-RU'),
  ukUA('Ukraine', 'uk', 'UA', 'uk-UA'),
  deDE('Germany', 'de', 'DE', 'de-DE'),
  jaJP('Japan', 'ja', 'JP', 'ja-JP'),
  enUS('United States', 'en', 'US', 'en-US'),
  frFR('France', 'fr', 'FR', 'fr-FR'),
  ptBR('Brazil', 'pt', 'BR', 'pt-BR'),
  koKR('South Korea', 'ko', 'KR', 'ko-KR'),
  zhCN('China', 'zh', 'CN', 'zh-CN'),
  csCZ('Czechia', 'cs', 'CZ', 'cs-CZ');

  const AppLanguage(
      this.countryName, this.languageCode, this.countryCode, this.localeTag);

  final String countryName;
  final String languageCode;
  final String countryCode;
  final String localeTag;

  Locale get locale => Locale(languageCode, countryCode);

  static const List<AppLanguage> valuesInMenuOrder = <AppLanguage>[
    esAR,
    ruRU,
    ukUA,
    deDE,
    jaJP,
    enUS,
    frFR,
    ptBR,
    koKR,
    zhCN,
    csCZ,
  ];

  static List<Locale> get supportedLocales =>
      valuesInMenuOrder.map((language) => language.locale).toList();
}

AppLocaleSelection resolveAppLocaleSelection(
    List<String> args, Locale systemLocale,
    [String buildDefaultLocale = '']) {
  final requested = _localeArg(args);
  final override = _languageFromToken(requested);
  if (override != null) {
    return AppLocaleSelection(
      language: override,
      systemLocale: systemLocale,
      source: 'command_line',
      requestedLocale: requested ?? '',
    );
  }

  final buildDefault = _languageFromToken(buildDefaultLocale);
  if (buildDefault != null) {
    return AppLocaleSelection(
      language: buildDefault,
      systemLocale: systemLocale,
      source: 'build_default',
      requestedLocale: buildDefaultLocale,
    );
  }

  final systemLanguage = _languageFromLocale(systemLocale) ?? AppLanguage.enUS;
  return AppLocaleSelection(
    language: systemLanguage,
    systemLocale: systemLocale,
    source: requested == null || requested.toLowerCase() == 'system'
        ? 'system'
        : 'system_fallback',
    requestedLocale: requested ?? '',
  );
}

String? _localeArg(List<String> args) {
  const keys = <String>['--locale', '--language', '--lang'];
  for (var i = 0; i < args.length; i++) {
    final arg = args[i].trim();
    final lower = arg.toLowerCase();
    for (final key in keys) {
      if (lower == key && i + 1 < args.length) {
        return args[i + 1].trim();
      }
      if (lower.startsWith('$key=')) {
        return arg.substring(key.length + 1).trim();
      }
    }
  }
  return null;
}

AppLanguage? _languageFromLocale(Locale locale) {
  final languageCode = locale.languageCode.toLowerCase();
  final countryCode = (locale.countryCode ?? '').toUpperCase();

  for (final language in AppLanguage.valuesInMenuOrder) {
    if (language.languageCode == languageCode &&
        language.countryCode == countryCode) {
      return language;
    }
  }

  for (final language in AppLanguage.valuesInMenuOrder) {
    if (language.languageCode == languageCode) return language;
  }
  return null;
}

AppLanguage? _languageFromToken(String? value) {
  final normalized = (value ?? '').trim().toLowerCase();
  if (normalized.isEmpty || normalized == 'system') return null;
  final token = normalized.replaceAll('_', '-').replaceAll(' ', '-');
  for (final language in AppLanguage.valuesInMenuOrder) {
    if (token == language.localeTag.toLowerCase() ||
        token == language.countryName.toLowerCase().replaceAll(' ', '-') ||
        token == language.languageCode ||
        token == language.countryCode.toLowerCase()) {
      return language;
    }
  }

  switch (token) {
    case 'argentina':
    case 'spanish':
    case 'espanol':
    case 'español':
      return AppLanguage.esAR;
    case 'russia':
    case 'russian':
      return AppLanguage.ruRU;
    case 'ukraine':
    case 'ukrainian':
      return AppLanguage.ukUA;
    case 'germany':
    case 'german':
    case 'deutsch':
      return AppLanguage.deDE;
    case 'japan':
    case 'japanese':
      return AppLanguage.jaJP;
    case 'usa':
    case 'us':
    case 'united-states':
    case 'english':
      return AppLanguage.enUS;
    case 'france':
    case 'french':
    case 'francais':
    case 'français':
      return AppLanguage.frFR;
    case 'brazil':
    case 'brasil':
    case 'portuguese':
    case 'portugues':
    case 'português':
      return AppLanguage.ptBR;
    case 'south-korea':
    case 'korea':
    case 'korean':
      return AppLanguage.koKR;
    case 'china':
    case 'chinese':
    case '中文':
    case '简体中文':
    case 'simplified-chinese':
      return AppLanguage.zhCN;
    case 'czechia':
    case 'czech':
      return AppLanguage.csCZ;
  }

  return null;
}

String _localeTag(Locale locale) {
  final country = locale.countryCode;
  return country == null || country.isEmpty
      ? locale.languageCode
      : '${locale.languageCode}-$country';
}

class AppStrings {
  AppStrings(this.language);

  final AppLanguage language;

  static const LocalizationsDelegate<AppStrings> delegate =
      _AppStringsDelegate();

  static AppStrings of(BuildContext context) =>
      Localizations.of<AppStrings>(context, AppStrings)!;

  String t(String key) =>
      (_strings[language]?[key] ?? _strings[AppLanguage.enUS]![key])!;

  String patchAlreadyInstalled(String version) =>
      t('patchAlreadyInstalled').replaceAll('{version}', version);
  String detectedFolder(String folder) =>
      t('detectedFolder').replaceAll('{folder}', folder);
  String installFailed(Object error) =>
      t('installFailed').replaceAll('{error}', '$error');
  String couldNotStartMagicka(Object error) =>
      t('couldNotStartMagicka').replaceAll('{error}', '$error');
  String patchReady(String version) =>
      t('patchReady').replaceAll('{version}', version);
  String patchInstalled(String version) =>
      t('patchInstalled').replaceAll('{version}', version);
  String updateFailed(Object error) =>
      t('updateFailed').replaceAll('{error}', '$error');
  String uninstallConfirmBody(String folder) =>
      t('uninstallConfirmBody').replaceAll('{folder}', folder);
  String uninstallFailed(Object error) =>
      t('uninstallFailed').replaceAll('{error}', '$error');
}

class _AppStringsDelegate extends LocalizationsDelegate<AppStrings> {
  const _AppStringsDelegate();

  @override
  bool isSupported(Locale locale) => _languageFromLocale(locale) != null;

  @override
  Future<AppStrings> load(Locale locale) => SynchronousFuture<AppStrings>(
      AppStrings(_languageFromLocale(locale) ?? AppLanguage.enUS));

  @override
  bool shouldReload(covariant LocalizationsDelegate<AppStrings> old) => false;
}

const Map<String, String> _en = <String, String>{
  'appTitle': 'Magicka Community Patch',
  'appHeader': 'MAGICKA COMMUNITY PATCH 0.0.40',
  'appSubtitle': 'Community Installer & Updater',
  'ready': 'Ready.',
  'ok': 'OK',
  'close': 'Close',
  'cancel': 'Cancel',
  'browse': 'Browse...',
  'findAutomatically': 'Find automatically',
  'startGame': 'Start game',
  'installPatch': 'Install patch',
  'sendFeedback': 'Send feedback',
  'supportOnPatreon': 'Support on Patreon',
  'gameFolder': 'Game folder',
  'patchAlreadyInstalled': 'Patch {version} is already installed.',
  'detectedFolder': 'Detected folder: {folder}',
  'invalidMagickaFolder': 'This does not look like the Magicka Steam folder.',
  'searchingSteamLibraries': 'Searching Steam libraries...',
  'thePatchWasInstalled': 'The patch was installed.',
  'installFailed': 'Install failed: {error}',
  'magickaWasNotStarted': 'Magicka was not started.',
  'magickaWasStarted': 'Magicka was started.',
  'couldNotStartMagicka': 'Could not start Magicka: {error}',
  'feedbackTitle': 'Send feedback',
  'feedbackName': 'Name (optional)',
  'feedbackSubject': 'Subject (optional)',
  'feedbackMessage': 'Message',
  'feedbackSend': 'Send',
  'feedbackSent': 'Feedback sent.',
  'feedbackNotSent': 'Feedback could not be sent.',
  'feedbackThankYou': 'Thanks. Your feedback was sent.',
  'feedbackFailed': 'Feedback could not be sent right now.',
  'telemetryIntroTitle':
      'Send anonymous crash and usage data to help improve the patch',
  'telemetryIntroBody':
      'No personal data is sent. When enabled, only these events are shared:',
  'eventGameStarted': 'Game started',
  'eventGameStartedBody':
      'Event name + patch version. Measures active sessions per version.',
  'eventGameClosed': 'Game closed normally',
  'eventGameClosedBody':
      'Keyboard/controller element counts and controller share.',
  'eventPatchInstalled': 'Patch installed',
  'eventPatchInstalledBody':
      'Event name + patch version. Estimates installs and ongoing use.',
  'eventAutoUpdate': 'Auto update',
  'eventAutoUpdateBody':
      'Event name + patch version. Confirms auto-update adoption.',
  'eventCrashReport': 'Crash / error report',
  'eventCrashReportBody':
      'Short error details, element counts and controller share.',
  'checkForUpdates': 'Check for updates when the game starts',
  'saveErrorNotes':
      'Save error notes too: <Magicka>\\CommunityPatch\\event-log.jsonl',
  'noPendingUpdate': 'No pending update was supplied.',
  'patchReady': 'Patch {version} is ready.',
  'preparingUpdate': 'Preparing update...',
  'patchInstalled': 'Patch {version} installed.',
  'updateFailed': 'Update failed: {error}',
  'updateTitle': 'Patch update {version}',
  'updateBody':
      'A prepared Magicka Community Patch update is ready. It will replace Magicka.exe and PolygonHead.dll, keep the current settings, and store the previous patch files as backup.',
  'updating': 'Updating...',
  'updatePatch': 'Update patch',
  'uninstallInitialStatus': 'Ready to remove the patch.',
  'uninstallPatch': 'Uninstall patch',
  'uninstallTitle': 'Uninstall patch',
  'uninstallBody':
      'Restore the original Magicka files from backup and remove the Community Patch tool files.',
  'uninstallConfirmTitle': 'Uninstall patch?',
  'uninstallConfirmBody':
      'This will restore the original Magicka.exe and PolygonHead.dll from backup.\n\nFolder:\n{folder}',
  'uninstallConfirmButton': 'Uninstall',
  'restoringOriginalFiles': 'Restoring original files...',
  'thePatchWasRemoved': 'The patch was removed.',
  'uninstallFailed': 'Uninstall failed: {error}',
  'removing': 'Removing...',
  'readyToPlay': 'READY TO PLAY',
  'startMagickaNow': 'Start Magicka now?',
  'startMagickaBody':
      'You can launch the patched game immediately or close this window and start it from Steam later.',
  'directXMissingTitle': 'Required DirectX component missing',
  'directXUnavailableHeading': 'Managed DirectX 1.1 is unavailable',
  'directXUnavailableBody':
      'Magicka needs an older Microsoft DirectX component before it can start correctly.',
  'directXInstallerNotFound': 'Installer not found',
  'directXInstallerNotFoundBody':
      'The component is not installed, and the DirectX installer could not be found in the Magicka folder. Please verify the Magicka game files in Steam and then start the game again.',
  'directXInstallHeading': 'Install the bundled DirectX redistributable',
  'directXInstallBody':
      'Magicka needs Managed DirectX 1.1. It is not installed on this Windows setup yet.',
  'directXSetupFound': 'Steam redist package found',
  'directXSetupFoundBody':
      'The official DirectX installer included with your Steam copy of Magicka can install the missing component. Windows may ask for administrator permission.',
  'installDirectX': 'Install DirectX',
  'notNow': 'Not now',
  'directXIncompleteTitle': 'DirectX installation incomplete',
  'directXIncompleteHeading': 'Managed DirectX is still unavailable',
  'directXIncompleteBody':
      'The required component could not be detected after the installer finished.',
  'directXInstallDidNotComplete': 'Installation did not complete',
  'directXInstallDidNotCompleteBody':
      'The installation may have been cancelled or may have failed. Please start Magicka once from Steam or verify the game files in Steam before trying again.',
  'directXSection': 'DIRECTX REDISTRIBUTABLE',
  'specialThanks': 'Special Thanks',
  'specialThanksCaps': 'SPECIAL THANKS',
  'supporter': 'SUPPORTER',
  'prioritySupporter': 'PRIORITY',
};

final Map<AppLanguage, Map<String, String>> _strings =
    <AppLanguage, Map<String, String>>{
  AppLanguage.enUS: _en,
  AppLanguage.zhCN: <String, String>{
    ..._en,
    'appTitle': 'Magicka 社区补丁',
    'appHeader': 'MAGICKA 社区补丁 0.0.37',
    'appSubtitle': '社区安装器与更新器',
    'ready': '已就绪。',
    'ok': '确定',
    'close': '关闭',
    'cancel': '取消',
    'browse': '浏览...',
    'findAutomatically': '自动查找',
    'startGame': '启动游戏',
    'installPatch': '安装补丁',
    'sendFeedback': '发送反馈',
    'supportOnPatreon': '在 Patreon 上支持',
    'gameFolder': '游戏文件夹',
    'patchAlreadyInstalled': '补丁 {version} 已安装。',
    'detectedFolder': '已检测到文件夹：{folder}',
    'invalidMagickaFolder': '该文件夹似乎不是 Magicka 的 Steam 文件夹。',
    'searchingSteamLibraries': '正在搜索 Steam 库...',
    'thePatchWasInstalled': '补丁已安装。',
    'installFailed': '安装失败：{error}',
    'magickaWasNotStarted': 'Magicka 未启动。',
    'magickaWasStarted': 'Magicka 已启动。',
    'couldNotStartMagicka': '无法启动 Magicka：{error}',
    'feedbackTitle': '发送反馈',
    'feedbackName': '名称（可选）',
    'feedbackSubject': '主题（可选）',
    'feedbackMessage': '消息',
    'feedbackSend': '发送',
    'feedbackSent': '反馈已发送。',
    'feedbackNotSent': '无法发送反馈。',
    'feedbackThankYou': '谢谢，您的反馈已发送。',
    'feedbackFailed': '目前无法发送反馈。',
    'telemetryIntroTitle': '发送匿名崩溃和使用数据以帮助改进补丁',
    'telemetryIntroBody': '不会发送个人数据。启用后，仅会共享以下事件：',
    'eventGameStarted': '游戏已启动',
    'eventGameStartedBody': '事件名称 + 补丁版本，用于统计各版本的活跃会话。',
    'eventGameClosed': '游戏正常关闭',
    'eventGameClosedBody': '键盘/手柄元素选择次数及手柄占比。',
    'eventPatchInstalled': '补丁已安装',
    'eventPatchInstalledBody': '事件名称 + 补丁版本，用于估算安装量和持续使用情况。',
    'eventAutoUpdate': '自动更新',
    'eventAutoUpdateBody': '事件名称 + 补丁版本，用于确认自动更新的使用情况。',
    'eventCrashReport': '崩溃/错误报告',
    'eventCrashReportBody': '简短错误信息、元素选择次数及手柄占比。',
    'checkForUpdates': '游戏启动时检查更新',
    'saveErrorNotes': '同时保存错误记录：<Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': '未提供待处理的更新。',
    'patchReady': '补丁 {version} 已准备就绪。',
    'preparingUpdate': '正在准备更新...',
    'patchInstalled': '补丁 {version} 已安装。',
    'updateFailed': '更新失败：{error}',
    'updateTitle': '补丁更新 {version}',
    'updateBody':
        '已准备好 Magicka 社区补丁更新。它将替换 Magicka.exe 和 PolygonHead.dll，保留当前设置，并将之前的补丁文件保存为备份。',
    'updating': '正在更新...',
    'updatePatch': '更新补丁',
    'uninstallInitialStatus': '已准备好卸载补丁。',
    'uninstallPatch': '卸载补丁',
    'uninstallTitle': '卸载补丁',
    'uninstallBody': '从备份中恢复原始 Magicka 文件，并删除社区补丁工具文件。',
    'uninstallConfirmTitle': '卸载补丁？',
    'uninstallConfirmBody':
        '这将从备份中恢复原始 Magicka.exe 和 PolygonHead.dll。\n\n文件夹：\n{folder}',
    'uninstallConfirmButton': '卸载',
    'restoringOriginalFiles': '正在恢复原始文件...',
    'thePatchWasRemoved': '补丁已卸载。',
    'uninstallFailed': '卸载失败：{error}',
    'removing': '正在删除...',
    'readyToPlay': '可以开始游戏',
    'startMagickaNow': '现在启动 Magicka？',
    'startMagickaBody': '您可以立即启动已打补丁的游戏，也可以关闭此窗口，稍后从 Steam 启动。',
    'directXMissingTitle': '缺少必需的 DirectX 组件',
    'directXUnavailableHeading': 'Managed DirectX 1.1 不可用',
    'directXUnavailableBody': 'Magicka 需要较旧的 Microsoft DirectX 组件才能正常启动。',
    'directXInstallerNotFound': '未找到安装程序',
    'directXInstallerNotFoundBody':
        '该组件尚未安装，且在 Magicka 文件夹中未找到 DirectX 安装程序。请在 Steam 中验证 Magicka 游戏文件，然后再次启动游戏。',
    'directXInstallHeading': '安装随附的 DirectX 可再分发组件',
    'directXInstallBody': 'Magicka 需要 Managed DirectX 1.1，当前 Windows 系统尚未安装。',
    'directXSetupFound': '已找到 Steam 可再分发软件包',
    'directXSetupFoundBody':
        '您的 Steam 版 Magicka 附带的官方 DirectX 安装程序可以安装缺失的组件。Windows 可能会请求管理员权限。',
    'installDirectX': '安装 DirectX',
    'notNow': '暂不',
    'directXIncompleteTitle': 'DirectX 安装未完成',
    'directXIncompleteHeading': 'Managed DirectX 仍不可用',
    'directXIncompleteBody': '安装程序完成后仍未检测到所需组件。',
    'directXInstallDidNotComplete': '安装未完成',
    'directXInstallDidNotCompleteBody':
        '安装可能已取消或失败。请先从 Steam 启动一次 Magicka，或在 Steam 中验证游戏文件，然后重试。',
    'directXSection': 'DIRECTX 可再分发组件',
    'specialThanks': '特别感谢',
    'specialThanksCaps': '特别感谢',
    'supporter': '支持者',
    'prioritySupporter': '优先支持',
  },
  AppLanguage.deDE: <String, String>{
    ..._en,
    'ready': 'Bereit.',
    'ok': 'OK',
    'close': 'Schließen',
    'cancel': 'Abbrechen',
    'browse': 'Durchsuchen...',
    'findAutomatically': 'Automatisch finden',
    'startGame': 'Spiel starten',
    'installPatch': 'Patch installieren',
    'sendFeedback': 'Feedback senden',
    'supportOnPatreon': 'Auf Patreon unterstützen',
    'gameFolder': 'Spielordner',
    'patchAlreadyInstalled': 'Patch {version} ist bereits installiert.',
    'detectedFolder': 'Gefundener Ordner: {folder}',
    'invalidMagickaFolder': 'Das sieht nicht wie der Magicka-Steam-Ordner aus.',
    'searchingSteamLibraries': 'Steam-Bibliotheken werden durchsucht...',
    'thePatchWasInstalled': 'Der Patch wurde installiert.',
    'installFailed': 'Installation fehlgeschlagen: {error}',
    'magickaWasNotStarted': 'Magicka wurde nicht gestartet.',
    'magickaWasStarted': 'Magicka wurde gestartet.',
    'couldNotStartMagicka': 'Magicka konnte nicht gestartet werden: {error}',
    'feedbackTitle': 'Feedback senden',
    'feedbackName': 'Name (optional)',
    'feedbackSubject': 'Betreff (optional)',
    'feedbackMessage': 'Nachricht',
    'feedbackSend': 'Senden',
    'feedbackSent': 'Feedback gesendet.',
    'feedbackNotSent': 'Feedback konnte nicht gesendet werden.',
    'feedbackThankYou': 'Danke. Dein Feedback wurde gesendet.',
    'feedbackFailed': 'Feedback kann gerade nicht gesendet werden.',
    'telemetryIntroTitle':
        'Anonyme Absturz- und Nutzungsdaten senden, um den Patch zu verbessern',
    'telemetryIntroBody':
        'Es werden keine persönlichen Daten gesendet. Wenn aktiviert, werden nur diese Events geteilt:',
    'eventGameStarted': 'Spiel gestartet',
    'eventGameStartedBody':
        'Eventname + Patch-Version. Misst aktive Sitzungen je Version.',
    'eventGameClosed': 'Spiel normal beendet',
    'eventGameClosedBody':
        'Tastatur-/Controller-Elementzahlen und Controller-Anteil.',
    'eventPatchInstalled': 'Patch installiert',
    'eventPatchInstalledBody':
        'Eventname + Patch-Version. Schätzt Installationen und Nutzung.',
    'eventAutoUpdate': 'Auto-Update',
    'eventAutoUpdateBody':
        'Eventname + Patch-Version. Bestätigt Auto-Update-Nutzung.',
    'eventCrashReport': 'Absturz-/Fehlerbericht',
    'eventCrashReportBody':
        'Kurze Fehlerdetails, Elementzahlen und Controller-Anteil.',
    'checkForUpdates': 'Beim Spielstart nach Updates suchen',
    'saveErrorNotes':
        'Fehlernotizen speichern: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'Es wurde kein ausstehendes Update übergeben.',
    'patchReady': 'Patch {version} ist bereit.',
    'preparingUpdate': 'Update wird vorbereitet...',
    'patchInstalled': 'Patch {version} installiert.',
    'updateFailed': 'Update fehlgeschlagen: {error}',
    'updateTitle': 'Patch-Update {version}',
    'updateBody':
        'Ein vorbereitetes Magicka-Community-Patch-Update ist bereit. Es ersetzt Magicka.exe und PolygonHead.dll, behält die aktuellen Einstellungen und legt die bisherigen Patch-Dateien als Backup ab.',
    'updating': 'Aktualisiere...',
    'updatePatch': 'Patch aktualisieren',
    'uninstallInitialStatus': 'Bereit, den Patch zu entfernen.',
    'uninstallPatch': 'Patch deinstallieren',
    'uninstallTitle': 'Patch deinstallieren',
    'uninstallBody':
        'Stellt die originalen Magicka-Dateien aus dem Backup wieder her und entfernt die Community-Patch-Tools.',
    'uninstallConfirmTitle': 'Patch deinstallieren?',
    'uninstallConfirmBody':
        'Dadurch werden Magicka.exe und PolygonHead.dll aus dem Backup wiederhergestellt.\n\nOrdner:\n{folder}',
    'uninstallConfirmButton': 'Deinstallieren',
    'restoringOriginalFiles': 'Originaldateien werden wiederhergestellt...',
    'thePatchWasRemoved': 'Der Patch wurde entfernt.',
    'uninstallFailed': 'Deinstallation fehlgeschlagen: {error}',
    'removing': 'Entferne...',
    'readyToPlay': 'BEREIT ZUM SPIELEN',
    'startMagickaNow': 'Magicka jetzt starten?',
    'startMagickaBody':
        'Du kannst das gepatchte Spiel sofort starten oder dieses Fenster schließen und es später über Steam starten.',
    'directXMissingTitle': 'Erforderliche DirectX-Komponente fehlt',
    'directXUnavailableHeading': 'Managed DirectX 1.1 ist nicht verfügbar',
    'directXUnavailableBody':
        'Magicka benötigt eine ältere Microsoft-DirectX-Komponente, bevor es korrekt starten kann.',
    'directXInstallerNotFound': 'Installer nicht gefunden',
    'directXInstallerNotFoundBody':
        'Die Komponente ist nicht installiert, und der DirectX-Installer wurde im Magicka-Ordner nicht gefunden. Bitte überprüfe die Magicka-Spieldateien in Steam und starte das Spiel erneut.',
    'directXInstallHeading':
        'Mitgeliefertes DirectX-Redistributable installieren',
    'directXInstallBody':
        'Magicka benötigt Managed DirectX 1.1. Auf diesem Windows-System ist es noch nicht installiert.',
    'directXSetupFound': 'Steam-Redist-Paket gefunden',
    'directXSetupFoundBody':
        'Der offizielle DirectX-Installer deiner Steam-Kopie von Magicka kann die fehlende Komponente installieren. Windows fragt möglicherweise nach Administratorrechten.',
    'installDirectX': 'DirectX installieren',
    'notNow': 'Nicht jetzt',
    'directXIncompleteTitle': 'DirectX-Installation unvollständig',
    'directXIncompleteHeading': 'Managed DirectX ist weiterhin nicht verfügbar',
    'directXIncompleteBody':
        'Die erforderliche Komponente wurde nach Abschluss des Installers nicht erkannt.',
    'directXInstallDidNotComplete': 'Installation nicht abgeschlossen',
    'directXInstallDidNotCompleteBody':
        'Die Installation wurde möglicherweise abgebrochen oder ist fehlgeschlagen. Starte Magicka einmal über Steam oder überprüfe die Spieldateien in Steam und versuche es erneut.',
    'directXSection': 'DIRECTX-REDISTRIBUTABLE',
    'specialThanks': 'Besonderer Dank',
    'specialThanksCaps': 'BESONDERER DANK',
    'supporter': 'UNTERSTÜTZER',
    'prioritySupporter': 'PRIORITÄT',
  },
  AppLanguage.esAR: <String, String>{
    ..._en,
    'ready': 'Listo.',
    'ok': 'OK',
    'close': 'Cerrar',
    'cancel': 'Cancelar',
    'browse': 'Buscar...',
    'findAutomatically': 'Buscar automáticamente',
    'startGame': 'Iniciar juego',
    'installPatch': 'Instalar patch',
    'sendFeedback': 'Enviar feedback',
    'supportOnPatreon': 'Apoyar en Patreon',
    'gameFolder': 'Carpeta del juego',
    'patchAlreadyInstalled': 'El patch {version} ya está instalado.',
    'detectedFolder': 'Carpeta detectada: {folder}',
    'invalidMagickaFolder':
        'Esto no parece ser la carpeta de Magicka en Steam.',
    'searchingSteamLibraries': 'Buscando bibliotecas de Steam...',
    'thePatchWasInstalled': 'El patch fue instalado.',
    'installFailed': 'Falló la instalación: {error}',
    'magickaWasNotStarted': 'Magicka no se inició.',
    'magickaWasStarted': 'Magicka se inició.',
    'couldNotStartMagicka': 'No se pudo iniciar Magicka: {error}',
    'feedbackTitle': 'Enviar feedback',
    'feedbackName': 'Nombre (opcional)',
    'feedbackSubject': 'Asunto (opcional)',
    'feedbackMessage': 'Mensaje',
    'feedbackSend': 'Enviar',
    'feedbackSent': 'Feedback enviado.',
    'feedbackNotSent': 'No se pudo enviar el feedback.',
    'feedbackThankYou': 'Gracias. Tu feedback fue enviado.',
    'feedbackFailed': 'No se puede enviar feedback ahora.',
    'telemetryIntroTitle':
        'Enviar datos anónimos de fallos y uso para mejorar el patch',
    'telemetryIntroBody':
        'No se envían datos personales. Si está activado, solo se comparten estos eventos:',
    'eventGameStarted': 'Juego iniciado',
    'eventGameStartedBody':
        'Nombre del evento + versión. Mide sesiones activas por versión.',
    'eventGameClosed': 'Juego cerrado normalmente',
    'eventGameClosedBody':
        'Conteos de elementos por teclado/mando y proporción del mando.',
    'eventPatchInstalled': 'Patch instalado',
    'eventPatchInstalledBody':
        'Nombre del evento + versión. Estima instalaciones y uso.',
    'eventAutoUpdate': 'Actualización auto',
    'eventAutoUpdateBody':
        'Nombre del evento + versión. Confirma adopción de auto-update.',
    'eventCrashReport': 'Reporte de fallo/error',
    'eventCrashReportBody':
        'Error breve, conteos de elementos y proporción del mando.',
    'checkForUpdates': 'Buscar updates al iniciar el juego',
    'saveErrorNotes':
        'Guardar notas de error: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'No se recibió ningún update pendiente.',
    'patchReady': 'El patch {version} está listo.',
    'preparingUpdate': 'Preparando update...',
    'patchInstalled': 'Patch {version} instalado.',
    'updateFailed': 'Falló el update: {error}',
    'updateTitle': 'Update del patch {version}',
    'updateBody':
        'Hay un update preparado para Magicka Community Patch. Reemplazará Magicka.exe y PolygonHead.dll, conservará los ajustes actuales y guardará los archivos anteriores como backup.',
    'updating': 'Actualizando...',
    'updatePatch': 'Actualizar patch',
    'uninstallInitialStatus': 'Listo para quitar el patch.',
    'uninstallPatch': 'Desinstalar patch',
    'uninstallTitle': 'Desinstalar patch',
    'uninstallBody':
        'Restaura los archivos originales de Magicka desde el backup y quita las herramientas del Community Patch.',
    'uninstallConfirmTitle': '¿Desinstalar patch?',
    'uninstallConfirmBody':
        'Esto restaurará Magicka.exe y PolygonHead.dll originales desde el backup.\n\nCarpeta:\n{folder}',
    'uninstallConfirmButton': 'Desinstalar',
    'restoringOriginalFiles': 'Restaurando archivos originales...',
    'thePatchWasRemoved': 'El patch fue quitado.',
    'uninstallFailed': 'Falló la desinstalación: {error}',
    'removing': 'Quitando...',
    'readyToPlay': 'LISTO PARA JUGAR',
    'startMagickaNow': '¿Iniciar Magicka ahora?',
    'startMagickaBody':
        'Podés iniciar el juego parcheado ahora o cerrar esta ventana e iniciarlo desde Steam más tarde.',
    'directXMissingTitle': 'Falta un componente requerido de DirectX',
    'directXUnavailableHeading': 'Managed DirectX 1.1 no está disponible',
    'directXUnavailableBody':
        'Magicka necesita un componente antiguo de Microsoft DirectX para iniciar correctamente.',
    'directXInstallerNotFound': 'Instalador no encontrado',
    'directXInstallerNotFoundBody':
        'El componente no está instalado y no se encontró el instalador de DirectX en la carpeta de Magicka. Verificá los archivos del juego en Steam e intentá de nuevo.',
    'directXInstallHeading': 'Instalar el redistributable de DirectX incluido',
    'directXInstallBody':
        'Magicka necesita Managed DirectX 1.1. Todavía no está instalado en este Windows.',
    'directXSetupFound': 'Paquete redist de Steam encontrado',
    'directXSetupFoundBody':
        'El instalador oficial de DirectX incluido con tu copia de Steam puede instalar el componente faltante. Windows puede pedir permisos de administrador.',
    'installDirectX': 'Instalar DirectX',
    'notNow': 'Ahora no',
    'directXIncompleteTitle': 'Instalación de DirectX incompleta',
    'directXIncompleteHeading': 'Managed DirectX sigue sin estar disponible',
    'directXIncompleteBody':
        'No se pudo detectar el componente requerido después de terminar el instalador.',
    'directXInstallDidNotComplete': 'La instalación no terminó',
    'directXInstallDidNotCompleteBody':
        'La instalación pudo haberse cancelado o fallado. Iniciá Magicka una vez desde Steam o verificá los archivos del juego antes de intentar de nuevo.',
    'directXSection': 'REDISTRIBUTABLE DE DIRECTX',
    'specialThanks': 'Agradecimientos',
    'specialThanksCaps': 'AGRADECIMIENTOS',
    'supporter': 'APOYA',
    'prioritySupporter': 'PRIORIDAD',
  },
  AppLanguage.frFR: <String, String>{
    ..._en,
    'ready': 'Prêt.',
    'close': 'Fermer',
    'cancel': 'Annuler',
    'browse': 'Parcourir...',
    'findAutomatically': 'Détecter automatiquement',
    'startGame': 'Lancer le jeu',
    'installPatch': 'Installer le patch',
    'sendFeedback': 'Envoyer un retour',
    'supportOnPatreon': 'Soutenir sur Patreon',
    'gameFolder': 'Dossier du jeu',
    'patchAlreadyInstalled': 'Le patch {version} est déjà installé.',
    'detectedFolder': 'Dossier détecté : {folder}',
    'invalidMagickaFolder':
        'Ce dossier ne semble pas être celui de Magicka sur Steam.',
    'searchingSteamLibraries': 'Recherche dans les bibliothèques Steam...',
    'thePatchWasInstalled': 'Le patch a été installé.',
    'installFailed': 'Installation échouée : {error}',
    'magickaWasNotStarted': 'Magicka n’a pas été lancé.',
    'magickaWasStarted': 'Magicka a été lancé.',
    'couldNotStartMagicka': 'Impossible de lancer Magicka : {error}',
    'feedbackTitle': 'Envoyer un retour',
    'feedbackName': 'Nom (facultatif)',
    'feedbackSubject': 'Sujet (facultatif)',
    'feedbackMessage': 'Message',
    'feedbackSend': 'Envoyer',
    'feedbackSent': 'Retour envoyé.',
    'feedbackNotSent': 'Le retour n’a pas pu être envoyé.',
    'feedbackThankYou': 'Merci. Votre retour a été envoyé.',
    'feedbackFailed': 'Impossible d’envoyer le retour maintenant.',
    'telemetryIntroTitle':
        'Envoyer des données anonymes de crash et d’utilisation pour améliorer le patch',
    'telemetryIntroBody':
        'Aucune donnée personnelle n’est envoyée. Si activé, seuls ces événements sont partagés :',
    'eventGameStarted': 'Jeu lancé',
    'eventGameStartedBody':
        'Nom d’événement + version du patch. Mesure les sessions actives.',
    'eventGameClosed': 'Jeu fermé normalement',
    'eventGameClosedBody':
        'Nombres d’éléments clavier/manette et part de la manette.',
    'eventPatchInstalled': 'Patch installé',
    'eventPatchInstalledBody':
        'Nom d’événement + version. Estime les installations et l’usage.',
    'eventAutoUpdate': 'Mise à jour auto',
    'eventAutoUpdateBody':
        'Nom d’événement + version. Confirme l’adoption des mises à jour.',
    'eventCrashReport': 'Rapport crash/erreur',
    'eventCrashReportBody':
        'Bref détail, nombres d’éléments et part de la manette.',
    'checkForUpdates': 'Chercher les mises à jour au lancement du jeu',
    'saveErrorNotes':
        'Enregistrer les notes d’erreur : <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'Aucune mise à jour en attente n’a été fournie.',
    'patchReady': 'Le patch {version} est prêt.',
    'preparingUpdate': 'Préparation de la mise à jour...',
    'patchInstalled': 'Patch {version} installé.',
    'updateFailed': 'Mise à jour échouée : {error}',
    'updateTitle': 'Mise à jour du patch {version}',
    'updateBody':
        'Une mise à jour préparée du Magicka Community Patch est prête. Elle remplacera Magicka.exe et PolygonHead.dll, conservera les paramètres actuels et sauvegardera les anciens fichiers.',
    'updating': 'Mise à jour...',
    'updatePatch': 'Mettre à jour',
    'uninstallInitialStatus': 'Prêt à supprimer le patch.',
    'uninstallPatch': 'Désinstaller',
    'uninstallTitle': 'Désinstaller le patch',
    'uninstallBody':
        'Restaure les fichiers Magicka d’origine depuis la sauvegarde et supprime les outils du Community Patch.',
    'uninstallConfirmTitle': 'Désinstaller le patch ?',
    'uninstallConfirmBody':
        'Cela restaurera Magicka.exe et PolygonHead.dll depuis la sauvegarde.\n\nDossier :\n{folder}',
    'uninstallConfirmButton': 'Désinstaller',
    'restoringOriginalFiles': 'Restauration des fichiers d’origine...',
    'thePatchWasRemoved': 'Le patch a été supprimé.',
    'uninstallFailed': 'Désinstallation échouée : {error}',
    'removing': 'Suppression...',
    'readyToPlay': 'PRÊT À JOUER',
    'startMagickaNow': 'Lancer Magicka maintenant ?',
    'startMagickaBody':
        'Vous pouvez lancer le jeu patché maintenant ou fermer cette fenêtre et le lancer depuis Steam plus tard.',
    'directXMissingTitle': 'Composant DirectX requis manquant',
    'directXUnavailableHeading': 'Managed DirectX 1.1 est indisponible',
    'directXUnavailableBody':
        'Magicka a besoin d’un ancien composant Microsoft DirectX pour démarrer correctement.',
    'directXInstallerNotFound': 'Installateur introuvable',
    'directXInstallerNotFoundBody':
        'Le composant n’est pas installé et l’installateur DirectX est introuvable dans le dossier Magicka. Vérifiez les fichiers du jeu dans Steam puis réessayez.',
    'directXInstallHeading': 'Installer le redistribuable DirectX inclus',
    'directXInstallBody':
        'Magicka a besoin de Managed DirectX 1.1. Il n’est pas encore installé sur ce Windows.',
    'directXSetupFound': 'Paquet Steam redist trouvé',
    'directXSetupFoundBody':
        'L’installateur DirectX officiel inclus avec votre copie Steam peut installer le composant manquant. Windows peut demander les droits administrateur.',
    'installDirectX': 'Installer DirectX',
    'notNow': 'Pas maintenant',
    'directXIncompleteTitle': 'Installation DirectX incomplète',
    'directXIncompleteHeading': 'Managed DirectX est toujours indisponible',
    'directXIncompleteBody':
        'Le composant requis n’a pas été détecté après la fin de l’installateur.',
    'directXInstallDidNotComplete': 'Installation non terminée',
    'directXInstallDidNotCompleteBody':
        'L’installation a peut-être été annulée ou a échoué. Lancez Magicka une fois depuis Steam ou vérifiez les fichiers du jeu avant de réessayer.',
    'directXSection': 'REDISTRIBUABLE DIRECTX',
    'specialThanks': 'Remerciements',
    'specialThanksCaps': 'REMERCIEMENTS',
    'supporter': 'SOUTIEN',
    'prioritySupporter': 'PRIORITÉ',
  },
  AppLanguage.ptBR: <String, String>{
    ..._en,
    'ready': 'Pronto.',
    'close': 'Fechar',
    'cancel': 'Cancelar',
    'browse': 'Procurar...',
    'findAutomatically': 'Encontrar automaticamente',
    'startGame': 'Iniciar jogo',
    'installPatch': 'Instalar patch',
    'sendFeedback': 'Enviar feedback',
    'supportOnPatreon': 'Apoiar no Patreon',
    'gameFolder': 'Pasta do jogo',
    'patchAlreadyInstalled': 'O patch {version} já está instalado.',
    'detectedFolder': 'Pasta detectada: {folder}',
    'invalidMagickaFolder': 'Esta não parece ser a pasta do Magicka no Steam.',
    'searchingSteamLibraries': 'Procurando bibliotecas do Steam...',
    'thePatchWasInstalled': 'O patch foi instalado.',
    'installFailed': 'Falha na instalação: {error}',
    'magickaWasNotStarted': 'Magicka não foi iniciado.',
    'magickaWasStarted': 'Magicka foi iniciado.',
    'couldNotStartMagicka': 'Não foi possível iniciar Magicka: {error}',
    'feedbackTitle': 'Enviar feedback',
    'feedbackName': 'Nome (opcional)',
    'feedbackSubject': 'Assunto (opcional)',
    'feedbackMessage': 'Mensagem',
    'feedbackSend': 'Enviar',
    'feedbackSent': 'Feedback enviado.',
    'feedbackNotSent': 'Não foi possível enviar o feedback.',
    'feedbackThankYou': 'Obrigado. Seu feedback foi enviado.',
    'feedbackFailed': 'Não foi possível enviar feedback agora.',
    'telemetryIntroTitle':
        'Enviar dados anônimos de travamento e uso para melhorar o patch',
    'telemetryIntroBody':
        'Nenhum dado pessoal é enviado. Quando ativado, apenas estes eventos são compartilhados:',
    'eventGameStarted': 'Jogo iniciado',
    'eventGameStartedBody':
        'Nome do evento + versão. Mede sessões ativas por versão.',
    'eventGameClosed': 'Jogo fechado normalmente',
    'eventGameClosedBody':
        'Contagens por teclado/controle e proporção do controle.',
    'eventPatchInstalled': 'Patch instalado',
    'eventPatchInstalledBody':
        'Nome do evento + versão. Estima instalações e uso.',
    'eventAutoUpdate': 'Atualização auto',
    'eventAutoUpdateBody':
        'Nome do evento + versão. Confirma adoção do auto-update.',
    'eventCrashReport': 'Relatório de erro',
    'eventCrashReportBody':
        'Erro breve, contagens de elementos e proporção do controle.',
    'checkForUpdates': 'Verificar updates ao iniciar o jogo',
    'saveErrorNotes':
        'Salvar notas de erro: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'Nenhuma atualização pendente foi fornecida.',
    'patchReady': 'O patch {version} está pronto.',
    'preparingUpdate': 'Preparando atualização...',
    'patchInstalled': 'Patch {version} instalado.',
    'updateFailed': 'Falha na atualização: {error}',
    'updateTitle': 'Atualização do patch {version}',
    'updateBody':
        'Uma atualização preparada do Magicka Community Patch está pronta. Ela substituirá Magicka.exe e PolygonHead.dll, manterá as configurações atuais e salvará os arquivos anteriores como backup.',
    'updating': 'Atualizando...',
    'updatePatch': 'Atualizar patch',
    'uninstallInitialStatus': 'Pronto para remover o patch.',
    'uninstallPatch': 'Desinstalar patch',
    'uninstallTitle': 'Desinstalar patch',
    'uninstallBody':
        'Restaura os arquivos originais do Magicka a partir do backup e remove as ferramentas do Community Patch.',
    'uninstallConfirmTitle': 'Desinstalar patch?',
    'uninstallConfirmBody':
        'Isto restaurará Magicka.exe e PolygonHead.dll originais a partir do backup.\n\nPasta:\n{folder}',
    'uninstallConfirmButton': 'Desinstalar',
    'restoringOriginalFiles': 'Restaurando arquivos originais...',
    'thePatchWasRemoved': 'O patch foi removido.',
    'uninstallFailed': 'Falha na desinstalação: {error}',
    'removing': 'Removendo...',
    'readyToPlay': 'PRONTO PARA JOGAR',
    'startMagickaNow': 'Iniciar Magicka agora?',
    'startMagickaBody':
        'Você pode iniciar o jogo com patch agora ou fechar esta janela e iniciar pelo Steam depois.',
    'directXMissingTitle': 'Componente DirectX necessário ausente',
    'directXUnavailableHeading': 'Managed DirectX 1.1 indisponível',
    'directXUnavailableBody':
        'Magicka precisa de um componente antigo do Microsoft DirectX para iniciar corretamente.',
    'directXInstallerNotFound': 'Instalador não encontrado',
    'directXInstallerNotFoundBody':
        'O componente não está instalado e o instalador DirectX não foi encontrado na pasta do Magicka. Verifique os arquivos do jogo no Steam e tente novamente.',
    'directXInstallHeading': 'Instalar o redistribuível DirectX incluído',
    'directXInstallBody':
        'Magicka precisa do Managed DirectX 1.1. Ele ainda não está instalado neste Windows.',
    'directXSetupFound': 'Pacote redist do Steam encontrado',
    'directXSetupFoundBody':
        'O instalador oficial do DirectX incluído com sua cópia Steam pode instalar o componente ausente. O Windows pode pedir permissão de administrador.',
    'installDirectX': 'Instalar DirectX',
    'notNow': 'Agora não',
    'directXIncompleteTitle': 'Instalação DirectX incompleta',
    'directXIncompleteHeading': 'Managed DirectX ainda indisponível',
    'directXIncompleteBody':
        'O componente necessário não foi detectado após o instalador terminar.',
    'directXInstallDidNotComplete': 'Instalação não concluída',
    'directXInstallDidNotCompleteBody':
        'A instalação pode ter sido cancelada ou falhado. Inicie Magicka uma vez pelo Steam ou verifique os arquivos do jogo antes de tentar novamente.',
    'directXSection': 'REDISTRIBUÍVEL DIRECTX',
    'specialThanks': 'Agradecimentos',
    'specialThanksCaps': 'AGRADECIMENTOS',
    'supporter': 'APOIADOR',
    'prioritySupporter': 'PRIORIDADE',
  },
  AppLanguage.csCZ: <String, String>{
    ..._en,
    'ready': 'Připraveno.',
    'close': 'Zavřít',
    'cancel': 'Zrušit',
    'browse': 'Procházet...',
    'findAutomatically': 'Najít automaticky',
    'startGame': 'Spustit hru',
    'installPatch': 'Instalovat patch',
    'sendFeedback': 'Odeslat zpětnou vazbu',
    'supportOnPatreon': 'Podpořit na Patreonu',
    'gameFolder': 'Složka hry',
    'patchAlreadyInstalled': 'Patch {version} je již nainstalován.',
    'detectedFolder': 'Nalezená složka: {folder}',
    'invalidMagickaFolder': 'Toto nevypadá jako složka Magicka ze Steamu.',
    'searchingSteamLibraries': 'Hledám knihovny Steamu...',
    'thePatchWasInstalled': 'Patch byl nainstalován.',
    'installFailed': 'Instalace selhala: {error}',
    'magickaWasNotStarted': 'Magicka nebyla spuštěna.',
    'magickaWasStarted': 'Magicka byla spuštěna.',
    'couldNotStartMagicka': 'Magicku se nepodařilo spustit: {error}',
    'feedbackTitle': 'Odeslat zpětnou vazbu',
    'feedbackName': 'Jméno (volitelné)',
    'feedbackSubject': 'Předmět (volitelné)',
    'feedbackMessage': 'Zpráva',
    'feedbackSend': 'Odeslat',
    'feedbackSent': 'Zpětná vazba odeslána.',
    'feedbackNotSent': 'Zpětnou vazbu se nepodařilo odeslat.',
    'feedbackThankYou': 'Díky. Zpětná vazba byla odeslána.',
    'feedbackFailed': 'Zpětnou vazbu teď nelze odeslat.',
    'telemetryIntroTitle':
        'Odesílat anonymní data o pádech a použití pro zlepšení patche',
    'telemetryIntroBody':
        'Neodesílají se žádná osobní data. Při zapnutí se sdílí jen tyto události:',
    'eventGameStarted': 'Hra spuštěna',
    'eventGameStartedBody':
        'Název události + verze. Měří aktivní relace podle verzí.',
    'eventGameClosed': 'Hra ukončena normálně',
    'eventGameClosedBody':
        'Počty prvků z klávesnice/ovladače a podíl ovladače.',
    'eventPatchInstalled': 'Patch nainstalován',
    'eventPatchInstalledBody':
        'Název události + verze. Odhaduje instalace a používání.',
    'eventAutoUpdate': 'Auto update',
    'eventAutoUpdateBody':
        'Název události + verze. Potvrzuje používání auto-update.',
    'eventCrashReport': 'Pád / chyba',
    'eventCrashReportBody': 'Krátká chyba, počty prvků a podíl ovladače.',
    'checkForUpdates': 'Při spuštění hry kontrolovat aktualizace',
    'saveErrorNotes':
        'Ukládat poznámky k chybám: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'Nebyla předána žádná čekající aktualizace.',
    'patchReady': 'Patch {version} je připraven.',
    'preparingUpdate': 'Připravuji aktualizaci...',
    'patchInstalled': 'Patch {version} nainstalován.',
    'updateFailed': 'Aktualizace selhala: {error}',
    'updateTitle': 'Aktualizace patche {version}',
    'updateBody':
        'Je připravena aktualizace Magicka Community Patch. Nahradí Magicka.exe a PolygonHead.dll, ponechá aktuální nastavení a uloží předchozí soubory jako zálohu.',
    'updating': 'Aktualizuji...',
    'updatePatch': 'Aktualizovat patch',
    'uninstallInitialStatus': 'Připraveno k odstranění patche.',
    'uninstallPatch': 'Odinstalovat patch',
    'uninstallTitle': 'Odinstalovat patch',
    'uninstallBody':
        'Obnoví původní soubory Magicka ze zálohy a odstraní nástroje Community Patch.',
    'uninstallConfirmTitle': 'Odinstalovat patch?',
    'uninstallConfirmBody':
        'Obnoví se původní Magicka.exe a PolygonHead.dll ze zálohy.\n\nSložka:\n{folder}',
    'uninstallConfirmButton': 'Odinstalovat',
    'restoringOriginalFiles': 'Obnovuji původní soubory...',
    'thePatchWasRemoved': 'Patch byl odstraněn.',
    'uninstallFailed': 'Odinstalace selhala: {error}',
    'removing': 'Odebírám...',
    'readyToPlay': 'PŘIPRAVENO HRÁT',
    'startMagickaNow': 'Spustit Magicku teď?',
    'startMagickaBody':
        'Patchnutou hru můžete spustit hned, nebo okno zavřít a spustit ji později ze Steamu.',
    'directXMissingTitle': 'Chybí požadovaná komponenta DirectX',
    'directXUnavailableHeading': 'Managed DirectX 1.1 není k dispozici',
    'directXUnavailableBody':
        'Magicka pro správné spuštění potřebuje starší komponentu Microsoft DirectX.',
    'directXInstallerNotFound': 'Instalátor nenalezen',
    'directXInstallerNotFoundBody':
        'Komponenta není nainstalována a instalátor DirectX nebyl ve složce Magicka nalezen. Ověřte soubory hry ve Steamu a zkuste to znovu.',
    'directXInstallHeading': 'Instalovat přiložený redistributable DirectX',
    'directXInstallBody':
        'Magicka potřebuje Managed DirectX 1.1. Na tomto Windows zatím není nainstalován.',
    'directXSetupFound': 'Nalezen Steam redist balíček',
    'directXSetupFoundBody':
        'Oficiální instalátor DirectX přiložený ke kopii Magicka na Steamu může chybějící komponentu nainstalovat. Windows může požádat o práva správce.',
    'installDirectX': 'Instalovat DirectX',
    'notNow': 'Teď ne',
    'directXIncompleteTitle': 'Instalace DirectX není úplná',
    'directXIncompleteHeading': 'Managed DirectX stále není dostupný',
    'directXIncompleteBody':
        'Po dokončení instalátoru nebyla požadovaná komponenta zjištěna.',
    'directXInstallDidNotComplete': 'Instalace nebyla dokončena',
    'directXInstallDidNotCompleteBody':
        'Instalace mohla být zrušena nebo selhala. Spusťte Magicku jednou ze Steamu nebo ověřte soubory hry a zkuste to znovu.',
    'directXSection': 'DIRECTX REDISTRIBUTABLE',
    'specialThanks': 'Poděkování',
    'specialThanksCaps': 'PODĚKOVÁNÍ',
    'supporter': 'PODPOROVATEL',
    'prioritySupporter': 'PRIORITA',
  },
  AppLanguage.ruRU: <String, String>{
    ..._en,
    'ready': 'Готово.',
    'close': 'Закрыть',
    'cancel': 'Отмена',
    'browse': 'Обзор...',
    'findAutomatically': 'Найти автоматически',
    'startGame': 'Запустить игру',
    'installPatch': 'Установить патч',
    'sendFeedback': 'Отправить отзыв',
    'supportOnPatreon': 'Поддержать на Patreon',
    'gameFolder': 'Папка игры',
    'patchAlreadyInstalled': 'Патч {version} уже установлен.',
    'detectedFolder': 'Найдена папка: {folder}',
    'invalidMagickaFolder': 'Это не похоже на папку Magicka в Steam.',
    'searchingSteamLibraries': 'Поиск библиотек Steam...',
    'thePatchWasInstalled': 'Патч установлен.',
    'installFailed': 'Ошибка установки: {error}',
    'magickaWasNotStarted': 'Magicka не была запущена.',
    'magickaWasStarted': 'Magicka запущена.',
    'couldNotStartMagicka': 'Не удалось запустить Magicka: {error}',
    'feedbackTitle': 'Отправить отзыв',
    'feedbackName': 'Имя (необязательно)',
    'feedbackSubject': 'Тема (необязательно)',
    'feedbackMessage': 'Сообщение',
    'feedbackSend': 'Отправить',
    'feedbackSent': 'Отзыв отправлен.',
    'feedbackNotSent': 'Не удалось отправить отзыв.',
    'feedbackThankYou': 'Спасибо. Ваш отзыв отправлен.',
    'feedbackFailed': 'Сейчас не удалось отправить отзыв.',
    'telemetryIntroTitle':
        'Отправлять анонимные данные о сбоях и использовании для улучшения патча',
    'telemetryIntroBody':
        'Личные данные не отправляются. При включении передаются только эти события:',
    'eventGameStarted': 'Игра запущена',
    'eventGameStartedBody':
        'Имя события + версия патча. Измеряет активные сессии.',
    'eventGameClosed': 'Игра закрыта нормально',
    'eventGameClosedBody':
        'Выборы элементов с клавиатуры/геймпада и доля геймпада.',
    'eventPatchInstalled': 'Патч установлен',
    'eventPatchInstalledBody':
        'Имя события + версия. Оценивает установки и использование.',
    'eventAutoUpdate': 'Автообновление',
    'eventAutoUpdateBody':
        'Имя события + версия. Подтверждает использование автообновления.',
    'eventCrashReport': 'Отчет о сбое',
    'eventCrashReportBody': 'Краткая ошибка, выборы элементов и доля геймпада.',
    'checkForUpdates': 'Проверять обновления при запуске игры',
    'saveErrorNotes':
        'Сохранять заметки об ошибках: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'Ожидающее обновление не передано.',
    'patchReady': 'Патч {version} готов.',
    'preparingUpdate': 'Подготовка обновления...',
    'patchInstalled': 'Патч {version} установлен.',
    'updateFailed': 'Ошибка обновления: {error}',
    'updateTitle': 'Обновление патча {version}',
    'updateBody':
        'Подготовлено обновление Magicka Community Patch. Оно заменит Magicka.exe и PolygonHead.dll, сохранит текущие настройки и сделает резервную копию прежних файлов.',
    'updating': 'Обновление...',
    'updatePatch': 'Обновить патч',
    'uninstallInitialStatus': 'Готово к удалению патча.',
    'uninstallPatch': 'Удалить патч',
    'uninstallTitle': 'Удалить патч',
    'uninstallBody':
        'Восстанавливает исходные файлы Magicka из резервной копии и удаляет инструменты Community Patch.',
    'uninstallConfirmTitle': 'Удалить патч?',
    'uninstallConfirmBody':
        'Будут восстановлены исходные Magicka.exe и PolygonHead.dll из резервной копии.\n\nПапка:\n{folder}',
    'uninstallConfirmButton': 'Удалить',
    'restoringOriginalFiles': 'Восстановление исходных файлов...',
    'thePatchWasRemoved': 'Патч удален.',
    'uninstallFailed': 'Ошибка удаления: {error}',
    'removing': 'Удаление...',
    'readyToPlay': 'ГОТОВО К ИГРЕ',
    'startMagickaNow': 'Запустить Magicka сейчас?',
    'startMagickaBody':
        'Можно запустить игру с патчем сейчас или закрыть окно и запустить ее позже из Steam.',
    'directXMissingTitle': 'Отсутствует нужный компонент DirectX',
    'directXUnavailableHeading': 'Managed DirectX 1.1 недоступен',
    'directXUnavailableBody':
        'Magicka нужен старый компонент Microsoft DirectX для корректного запуска.',
    'directXInstallerNotFound': 'Установщик не найден',
    'directXInstallerNotFoundBody':
        'Компонент не установлен, а установщик DirectX не найден в папке Magicka. Проверьте файлы игры в Steam и попробуйте снова.',
    'directXInstallHeading': 'Установить включенный DirectX redistributable',
    'directXInstallBody':
        'Magicka нужен Managed DirectX 1.1. Он еще не установлен в этой Windows.',
    'directXSetupFound': 'Найден Steam redist-пакет',
    'directXSetupFoundBody':
        'Официальный установщик DirectX из вашей Steam-копии Magicka может установить отсутствующий компонент. Windows может запросить права администратора.',
    'installDirectX': 'Установить DirectX',
    'notNow': 'Не сейчас',
    'directXIncompleteTitle': 'Установка DirectX не завершена',
    'directXIncompleteHeading': 'Managed DirectX все еще недоступен',
    'directXIncompleteBody':
        'После завершения установщика нужный компонент не обнаружен.',
    'directXInstallDidNotComplete': 'Установка не завершилась',
    'directXInstallDidNotCompleteBody':
        'Установка могла быть отменена или завершиться с ошибкой. Запустите Magicka один раз из Steam или проверьте файлы игры и попробуйте снова.',
    'directXSection': 'DIRECTX REDISTRIBUTABLE',
    'specialThanks': 'Благодарности',
    'specialThanksCaps': 'БЛАГОДАРНОСТИ',
    'supporter': 'ПОДДЕРЖКА',
    'prioritySupporter': 'ПРИОРИТЕТ',
  },
  AppLanguage.ukUA: <String, String>{
    ..._en,
    'ready': 'Готово.',
    'close': 'Закрити',
    'cancel': 'Скасувати',
    'browse': 'Огляд...',
    'findAutomatically': 'Знайти автоматично',
    'startGame': 'Запустити гру',
    'installPatch': 'Встановити патч',
    'sendFeedback': 'Надіслати відгук',
    'supportOnPatreon': 'Підтримати на Patreon',
    'gameFolder': 'Папка гри',
    'patchAlreadyInstalled': 'Патч {version} уже встановлено.',
    'detectedFolder': 'Знайдена папка: {folder}',
    'invalidMagickaFolder': 'Це не схоже на папку Magicka у Steam.',
    'searchingSteamLibraries': 'Пошук бібліотек Steam...',
    'thePatchWasInstalled': 'Патч встановлено.',
    'installFailed': 'Помилка встановлення: {error}',
    'magickaWasNotStarted': 'Magicka не запущено.',
    'magickaWasStarted': 'Magicka запущено.',
    'couldNotStartMagicka': 'Не вдалося запустити Magicka: {error}',
    'feedbackTitle': 'Надіслати відгук',
    'feedbackName': "Ім'я (необов'язково)",
    'feedbackSubject': "Тема (необов'язково)",
    'feedbackMessage': 'Повідомлення',
    'feedbackSend': 'Надіслати',
    'feedbackSent': 'Відгук надіслано.',
    'feedbackNotSent': 'Не вдалося надіслати відгук.',
    'feedbackThankYou': 'Дякуємо. Ваш відгук надіслано.',
    'feedbackFailed': 'Зараз не вдалося надіслати відгук.',
    'telemetryIntroTitle':
        'Надсилати анонімні дані про збої та використання для покращення патча',
    'telemetryIntroBody':
        'Особисті дані не надсилаються. Якщо увімкнено, передаються лише ці події:',
    'eventGameStarted': 'Гру запущено',
    'eventGameStartedBody':
        'Назва події + версія. Вимірює активні сесії за версіями.',
    'eventGameClosed': 'Гру закрито нормально',
    'eventGameClosedBody':
        'Вибори елементів з клавіатури/геймпада та частка геймпада.',
    'eventPatchInstalled': 'Патч встановлено',
    'eventPatchInstalledBody':
        'Назва події + версія. Оцінює встановлення та використання.',
    'eventAutoUpdate': 'Автооновлення',
    'eventAutoUpdateBody':
        'Назва події + версія. Підтверджує використання автооновлення.',
    'eventCrashReport': 'Звіт про збій',
    'eventCrashReportBody':
        'Коротка помилка, вибори елементів і частка геймпада.',
    'checkForUpdates': 'Перевіряти оновлення під час запуску гри',
    'saveErrorNotes':
        'Зберігати нотатки про помилки: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': 'Очікуване оновлення не передано.',
    'patchReady': 'Патч {version} готовий.',
    'preparingUpdate': 'Підготовка оновлення...',
    'patchInstalled': 'Патч {version} встановлено.',
    'updateFailed': 'Помилка оновлення: {error}',
    'updateTitle': 'Оновлення патча {version}',
    'updateBody':
        'Підготовлено оновлення Magicka Community Patch. Воно замінить Magicka.exe і PolygonHead.dll, збереже поточні налаштування та створить резервну копію попередніх файлів.',
    'updating': 'Оновлення...',
    'updatePatch': 'Оновити патч',
    'uninstallInitialStatus': 'Готово до видалення патча.',
    'uninstallPatch': 'Видалити патч',
    'uninstallTitle': 'Видалити патч',
    'uninstallBody':
        'Відновлює оригінальні файли Magicka з резервної копії та видаляє інструменти Community Patch.',
    'uninstallConfirmTitle': 'Видалити патч?',
    'uninstallConfirmBody':
        'Буде відновлено оригінальні Magicka.exe і PolygonHead.dll з резервної копії.\n\nПапка:\n{folder}',
    'uninstallConfirmButton': 'Видалити',
    'restoringOriginalFiles': 'Відновлення оригінальних файлів...',
    'thePatchWasRemoved': 'Патч видалено.',
    'uninstallFailed': 'Помилка видалення: {error}',
    'removing': 'Видалення...',
    'readyToPlay': 'ГОТОВО ДО ГРИ',
    'startMagickaNow': 'Запустити Magicka зараз?',
    'startMagickaBody':
        'Можна запустити гру з патчем зараз або закрити це вікно й запустити її зі Steam пізніше.',
    'directXMissingTitle': 'Відсутній потрібний компонент DirectX',
    'directXUnavailableHeading': 'Managed DirectX 1.1 недоступний',
    'directXUnavailableBody':
        'Magicka потребує старішого компонента Microsoft DirectX для правильного запуску.',
    'directXInstallerNotFound': 'Інсталятор не знайдено',
    'directXInstallerNotFoundBody':
        'Компонент не встановлено, а інсталятор DirectX не знайдено в папці Magicka. Перевірте файли гри у Steam і спробуйте знову.',
    'directXInstallHeading': 'Встановити вбудований DirectX redistributable',
    'directXInstallBody':
        'Magicka потребує Managed DirectX 1.1. Його ще не встановлено у цій Windows.',
    'directXSetupFound': 'Знайдено Steam redist-пакет',
    'directXSetupFoundBody':
        'Офіційний інсталятор DirectX з вашої Steam-копії Magicka може встановити відсутній компонент. Windows може запросити права адміністратора.',
    'installDirectX': 'Встановити DirectX',
    'notNow': 'Не зараз',
    'directXIncompleteTitle': 'Встановлення DirectX неповне',
    'directXIncompleteHeading': 'Managed DirectX усе ще недоступний',
    'directXIncompleteBody':
        'Після завершення інсталятора потрібний компонент не виявлено.',
    'directXInstallDidNotComplete': 'Встановлення не завершено',
    'directXInstallDidNotCompleteBody':
        'Встановлення могло бути скасовано або завершитися помилкою. Запустіть Magicka один раз зі Steam або перевірте файли гри й спробуйте знову.',
    'directXSection': 'DIRECTX REDISTRIBUTABLE',
    'specialThanks': 'Подяки',
    'specialThanksCaps': 'ПОДЯКИ',
    'supporter': 'ПІДТРИМКА',
    'prioritySupporter': 'ПРІОРИТЕТ',
  },
  AppLanguage.jaJP: <String, String>{
    ..._en,
    'ready': '準備完了。',
    'ok': 'OK',
    'close': '閉じる',
    'cancel': 'キャンセル',
    'browse': '参照...',
    'findAutomatically': '自動検索',
    'startGame': 'ゲーム開始',
    'installPatch': 'パッチをインストール',
    'sendFeedback': 'フィードバック送信',
    'supportOnPatreon': 'Patreonで支援',
    'gameFolder': 'ゲームフォルダー',
    'patchAlreadyInstalled': 'パッチ {version} はすでにインストール済みです。',
    'detectedFolder': '検出フォルダー: {folder}',
    'invalidMagickaFolder': 'Magicka の Steam フォルダーではないようです。',
    'searchingSteamLibraries': 'Steam ライブラリを検索中...',
    'thePatchWasInstalled': 'パッチをインストールしました。',
    'installFailed': 'インストール失敗: {error}',
    'magickaWasNotStarted': 'Magicka は起動しませんでした。',
    'magickaWasStarted': 'Magicka を起動しました。',
    'couldNotStartMagicka': 'Magicka を起動できませんでした: {error}',
    'feedbackTitle': 'フィードバック送信',
    'feedbackName': '名前（任意）',
    'feedbackSubject': '件名（任意）',
    'feedbackMessage': 'メッセージ',
    'feedbackSend': '送信',
    'feedbackSent': 'フィードバックを送信しました。',
    'feedbackNotSent': 'フィードバックを送信できませんでした。',
    'feedbackThankYou': 'ありがとうございます。フィードバックを送信しました。',
    'feedbackFailed': '現在フィードバックを送信できません。',
    'telemetryIntroTitle': 'パッチ改善のため匿名のクラッシュ/使用データを送信',
    'telemetryIntroBody': '個人データは送信されません。有効時は次のイベントのみ共有されます:',
    'eventGameStarted': 'ゲーム開始',
    'eventGameStartedBody': 'イベント名 + パッチ版。版ごとのセッションを測定します。',
    'eventGameClosed': '正常終了',
    'eventGameClosedBody': 'キーボード/コントローラーの元素選択数とコントローラー比率。',
    'eventPatchInstalled': 'パッチ導入',
    'eventPatchInstalledBody': 'イベント名 + 版。インストールと利用を推定します。',
    'eventAutoUpdate': '自動更新',
    'eventAutoUpdateBody': 'イベント名 + 版。自動更新の利用を確認します。',
    'eventCrashReport': 'クラッシュ/エラー',
    'eventCrashReportBody': '短いエラー詳細、元素選択数とコントローラー比率。',
    'checkForUpdates': 'ゲーム起動時に更新を確認',
    'saveErrorNotes': 'エラーノートも保存: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': '保留中の更新が指定されていません。',
    'patchReady': 'パッチ {version} の準備ができました。',
    'preparingUpdate': '更新を準備中...',
    'patchInstalled': 'パッチ {version} をインストールしました。',
    'updateFailed': '更新失敗: {error}',
    'updateTitle': 'パッチ更新 {version}',
    'updateBody':
        'Magicka Community Patch の更新準備ができました。Magicka.exe と PolygonHead.dll を置き換え、現在の設定を保持し、以前のファイルをバックアップします。',
    'updating': '更新中...',
    'updatePatch': 'パッチ更新',
    'uninstallInitialStatus': 'パッチ削除の準備完了。',
    'uninstallPatch': 'パッチ削除',
    'uninstallTitle': 'パッチ削除',
    'uninstallBody': 'バックアップから元の Magicka ファイルを復元し、Community Patch ツールを削除します。',
    'uninstallConfirmTitle': 'パッチを削除しますか？',
    'uninstallConfirmBody':
        'バックアップから元の Magicka.exe と PolygonHead.dll を復元します。\n\nフォルダー:\n{folder}',
    'uninstallConfirmButton': '削除',
    'restoringOriginalFiles': '元のファイルを復元中...',
    'thePatchWasRemoved': 'パッチを削除しました。',
    'uninstallFailed': '削除失敗: {error}',
    'removing': '削除中...',
    'readyToPlay': 'プレイ準備完了',
    'startMagickaNow': '今すぐ Magicka を起動しますか？',
    'startMagickaBody': 'パッチ済みのゲームを今すぐ起動するか、このウィンドウを閉じて後で Steam から起動できます。',
    'directXMissingTitle': '必要な DirectX コンポーネントがありません',
    'directXUnavailableHeading': 'Managed DirectX 1.1 が利用できません',
    'directXUnavailableBody':
        'Magicka を正しく起動するには古い Microsoft DirectX コンポーネントが必要です。',
    'directXInstallerNotFound': 'インストーラーが見つかりません',
    'directXInstallerNotFoundBody':
        'コンポーネントが未インストールで、Magicka フォルダーに DirectX インストーラーが見つかりません。Steam でゲームファイルを確認してから再試行してください。',
    'directXInstallHeading': '同梱の DirectX redistributable をインストール',
    'directXInstallBody':
        'Magicka には Managed DirectX 1.1 が必要です。この Windows にはまだインストールされていません。',
    'directXSetupFound': 'Steam redist パッケージを検出',
    'directXSetupFoundBody':
        'Steam 版 Magicka に含まれる公式 DirectX インストーラーで不足コンポーネントをインストールできます。Windows が管理者権限を求める場合があります。',
    'installDirectX': 'DirectX をインストール',
    'notNow': '今はしない',
    'directXIncompleteTitle': 'DirectX のインストールが未完了',
    'directXIncompleteHeading': 'Managed DirectX がまだ利用できません',
    'directXIncompleteBody': 'インストーラー完了後も必要なコンポーネントを検出できませんでした。',
    'directXInstallDidNotComplete': 'インストールが完了しませんでした',
    'directXInstallDidNotCompleteBody':
        'インストールがキャンセルまたは失敗した可能性があります。Steam から一度 Magicka を起動するか、ゲームファイルを確認して再試行してください。',
    'directXSection': 'DIRECTX REDISTRIBUTABLE',
    'specialThanks': 'Special Thanks',
    'specialThanksCaps': 'SPECIAL THANKS',
    'supporter': 'SUPPORTER',
    'prioritySupporter': '優先支援',
  },
  AppLanguage.koKR: <String, String>{
    ..._en,
    'ready': '준비됨.',
    'ok': 'OK',
    'close': '닫기',
    'cancel': '취소',
    'browse': '찾아보기...',
    'findAutomatically': '자동으로 찾기',
    'startGame': '게임 시작',
    'installPatch': '패치 설치',
    'sendFeedback': '피드백 보내기',
    'supportOnPatreon': 'Patreon 후원',
    'gameFolder': '게임 폴더',
    'patchAlreadyInstalled': '패치 {version}이 이미 설치되어 있습니다.',
    'detectedFolder': '감지된 폴더: {folder}',
    'invalidMagickaFolder': 'Magicka Steam 폴더가 아닌 것 같습니다.',
    'searchingSteamLibraries': 'Steam 라이브러리 검색 중...',
    'thePatchWasInstalled': '패치가 설치되었습니다.',
    'installFailed': '설치 실패: {error}',
    'magickaWasNotStarted': 'Magicka가 시작되지 않았습니다.',
    'magickaWasStarted': 'Magicka가 시작되었습니다.',
    'couldNotStartMagicka': 'Magicka를 시작할 수 없습니다: {error}',
    'feedbackTitle': '피드백 보내기',
    'feedbackName': '이름(선택)',
    'feedbackSubject': '제목(선택)',
    'feedbackMessage': '메시지',
    'feedbackSend': '보내기',
    'feedbackSent': '피드백을 보냈습니다.',
    'feedbackNotSent': '피드백을 보낼 수 없습니다.',
    'feedbackThankYou': '감사합니다. 피드백을 보냈습니다.',
    'feedbackFailed': '지금은 피드백을 보낼 수 없습니다.',
    'telemetryIntroTitle': '패치 개선을 위해 익명 충돌 및 사용 데이터 보내기',
    'telemetryIntroBody': '개인 데이터는 전송되지 않습니다. 활성화 시 다음 이벤트만 공유됩니다:',
    'eventGameStarted': '게임 시작',
    'eventGameStartedBody': '이벤트 이름 + 패치 버전. 버전별 세션을 측정합니다.',
    'eventGameClosed': '정상 종료',
    'eventGameClosedBody': '키보드/컨트롤러 원소 선택 수와 컨트롤러 비율.',
    'eventPatchInstalled': '패치 설치됨',
    'eventPatchInstalledBody': '이벤트 이름 + 버전. 설치와 사용량을 추정합니다.',
    'eventAutoUpdate': '자동 업데이트',
    'eventAutoUpdateBody': '이벤트 이름 + 버전. 자동 업데이트 사용을 확인합니다.',
    'eventCrashReport': '충돌/오류 보고',
    'eventCrashReportBody': '짧은 오류 정보, 원소 선택 수와 컨트롤러 비율.',
    'checkForUpdates': '게임 시작 시 업데이트 확인',
    'saveErrorNotes': '오류 메모 저장: <Magicka>\\CommunityPatch\\event-log.jsonl',
    'noPendingUpdate': '대기 중인 업데이트가 제공되지 않았습니다.',
    'patchReady': '패치 {version} 준비 완료.',
    'preparingUpdate': '업데이트 준비 중...',
    'patchInstalled': '패치 {version} 설치됨.',
    'updateFailed': '업데이트 실패: {error}',
    'updateTitle': '패치 업데이트 {version}',
    'updateBody':
        'Magicka Community Patch 업데이트가 준비되었습니다. Magicka.exe와 PolygonHead.dll을 교체하고 현재 설정을 유지하며 이전 파일을 백업합니다.',
    'updating': '업데이트 중...',
    'updatePatch': '패치 업데이트',
    'uninstallInitialStatus': '패치 제거 준비 완료.',
    'uninstallPatch': '패치 제거',
    'uninstallTitle': '패치 제거',
    'uninstallBody': '백업에서 원본 Magicka 파일을 복원하고 Community Patch 도구 파일을 제거합니다.',
    'uninstallConfirmTitle': '패치를 제거할까요?',
    'uninstallConfirmBody':
        '백업에서 원본 Magicka.exe와 PolygonHead.dll을 복원합니다.\n\n폴더:\n{folder}',
    'uninstallConfirmButton': '제거',
    'restoringOriginalFiles': '원본 파일 복원 중...',
    'thePatchWasRemoved': '패치가 제거되었습니다.',
    'uninstallFailed': '제거 실패: {error}',
    'removing': '제거 중...',
    'readyToPlay': '플레이 준비 완료',
    'startMagickaNow': '지금 Magicka를 시작할까요?',
    'startMagickaBody': '패치된 게임을 바로 시작하거나 이 창을 닫고 나중에 Steam에서 시작할 수 있습니다.',
    'directXMissingTitle': '필수 DirectX 구성 요소 누락',
    'directXUnavailableHeading': 'Managed DirectX 1.1을 사용할 수 없음',
    'directXUnavailableBody':
        'Magicka를 올바르게 시작하려면 오래된 Microsoft DirectX 구성 요소가 필요합니다.',
    'directXInstallerNotFound': '설치 프로그램을 찾을 수 없음',
    'directXInstallerNotFoundBody':
        '구성 요소가 설치되어 있지 않고 Magicka 폴더에서 DirectX 설치 프로그램을 찾을 수 없습니다. Steam에서 게임 파일을 확인한 뒤 다시 시도하세요.',
    'directXInstallHeading': '포함된 DirectX redistributable 설치',
    'directXInstallBody':
        'Magicka에는 Managed DirectX 1.1이 필요합니다. 이 Windows에는 아직 설치되어 있지 않습니다.',
    'directXSetupFound': 'Steam redist 패키지 발견',
    'directXSetupFoundBody':
        'Steam Magicka에 포함된 공식 DirectX 설치 프로그램이 누락된 구성 요소를 설치할 수 있습니다. Windows가 관리자 권한을 요청할 수 있습니다.',
    'installDirectX': 'DirectX 설치',
    'notNow': '나중에',
    'directXIncompleteTitle': 'DirectX 설치 미완료',
    'directXIncompleteHeading': 'Managed DirectX를 아직 사용할 수 없음',
    'directXIncompleteBody': '설치 프로그램이 끝난 뒤에도 필요한 구성 요소를 감지하지 못했습니다.',
    'directXInstallDidNotComplete': '설치가 완료되지 않음',
    'directXInstallDidNotCompleteBody':
        '설치가 취소되었거나 실패했을 수 있습니다. Steam에서 Magicka를 한 번 실행하거나 게임 파일을 확인한 뒤 다시 시도하세요.',
    'directXSection': 'DIRECTX REDISTRIBUTABLE',
    'specialThanks': '감사의 말',
    'specialThanksCaps': '감사의 말',
    'supporter': '후원자',
    'prioritySupporter': '우선 후원',
  },
};
