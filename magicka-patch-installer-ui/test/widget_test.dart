// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'dart:convert';
import 'dart:io';

import 'package:crypto/crypto.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:magicka_community_patch_installer_ui/localization.dart';
import 'package:magicka_community_patch_installer_ui/main.dart';
import 'package:magicka_community_patch_installer_ui/original_game_files.dart';

Future<Directory> _createChinesePayload(Directory root) async {
  final payload = Directory(
      '${root.path}${Platform.pathSeparator}payload${Platform.pathSeparator}zho');
  final font = Directory('${payload.path}${Platform.pathSeparator}Font');
  await font.create(recursive: true);
  await File('${font.path}${Platform.pathSeparator}Maiandra14.xnb')
      .writeAsString('font-14');
  await File('${font.path}${Platform.pathSeparator}MenuTitle.xnb')
      .writeAsString('font-title');
  final names = <String>[
    'UI.loctable.xml',
    'Camp_Generic.loctable.xml',
    ...List<String>.generate(40, (index) => 'Test_$index.loctable.xml'),
  ];
  for (final name in names) {
    await File('${payload.path}${Platform.pathSeparator}$name')
        .writeAsString('<Workbook><Worksheet><Table /></Worksheet></Workbook>');
  }
  return payload;
}

Future<Directory> _createGcDiagnosticsPayload(Directory root) async {
  final payload = Directory(
      '${root.path}${Platform.pathSeparator}payload${Platform.pathSeparator}gc-diagnostics');
  await payload.create(recursive: true);
  for (final name in const <String>[
    'Magicka.GcAnalyzer.exe',
    'Magicka.GcAnalyzer.exe.config',
    'Microsoft.Diagnostics.Runtime.dll',
    'LICENSE-MIT.txt',
    'THIRD_PARTY_NOTICES.txt',
  ]) {
    await File('${payload.path}${Platform.pathSeparator}$name')
        .writeAsString(name);
  }
  return payload;
}

void main() {
  test('Steam validation URI targets the Magicka app', () {
    expect(AppConstants.magickaSteamValidationUrl, 'steam://validate/42910');
  });

  test('Linux Steam candidates cover native, Flatpak and compatibility paths',
      () {
    final candidates = linuxSteamDirectoryCandidates(const <String, String>{
      'HOME': '/home/player',
      'XDG_DATA_HOME': '/home/player/.data',
      'STEAM_COMPAT_CLIENT_INSTALL_PATH': '/opt/custom-steam',
    });

    expect(candidates, contains('/opt/custom-steam'));
    expect(candidates, contains('/home/player/.data/Steam'));
    expect(candidates, contains('/home/player/.local/share/Steam'));
    expect(candidates, contains('/home/player/.steam/steam'));
    expect(
      candidates,
      contains(
        '/home/player/.var/app/com.valvesoftware.Steam/.local/share/Steam',
      ),
    );
  });

  test('platform path joining normalizes embedded separators', () {
    expect(
      joinPathForPlatform(
        '/home/player/',
        r'.local\share\Steam',
        c: r'steamapps\common\Magicka',
        windowsPaths: false,
      ),
      '/home/player/.local/share/Steam/steamapps/common/Magicka',
    );
    expect(
      joinPathForPlatform(
        r'C:\Steam\',
        'steamapps/common',
        c: 'Magicka',
        windowsPaths: true,
      ),
      r'C:\Steam\steamapps\common\Magicka',
    );
  });

  test('Steam library VDF resolves Magicka from an additional library',
      () async {
    final root = await Directory.systemTemp.createTemp('magicka_steam_');
    addTearDown(() async {
      if (await root.exists()) await root.delete(recursive: true);
    });
    String linuxPath(String path) => path.replaceAll(r'\', '/');

    final steam = Directory('${root.path}${Platform.pathSeparator}Steam');
    final library =
        Directory('${root.path}${Platform.pathSeparator}SteamLibrary');
    final game = Directory(
      '${library.path}${Platform.pathSeparator}steamapps'
      '${Platform.pathSeparator}common${Platform.pathSeparator}Magicka',
    );
    await Directory(
      '${steam.path}${Platform.pathSeparator}steamapps',
    ).create(recursive: true);
    await game.create(recursive: true);
    await File(
      '${steam.path}${Platform.pathSeparator}steamapps'
      '${Platform.pathSeparator}libraryfolders.vdf',
    ).writeAsString('''
"libraryfolders"
{
  "1"
  {
    "path" "${linuxPath(library.path)}"
  }
}
''');
    await File(
      '${library.path}${Platform.pathSeparator}steamapps'
      '${Platform.pathSeparator}appmanifest_42910.acf',
    ).writeAsString('''
"AppState"
{
  "appid" "42910"
  "installdir" "Magicka"
}
''');
    await File('${game.path}${Platform.pathSeparator}Magicka.exe')
        .writeAsBytes(const <int>[1]);
    await File('${game.path}${Platform.pathSeparator}steam_api.dll')
        .writeAsBytes(const <int>[1]);

    final result = await findSteamAppDirectory(
      steamDirectories: <String>[linuxPath(steam.path)],
      appId: '42910',
      fallbackInstallDirectory: 'Magicka',
      windowsPaths: false,
      isValidDirectory: (path) =>
          File('$path/Magicka.exe').existsSync() &&
          File('$path/steam_api.dll').existsSync(),
    );

    expect(result, linuxPath(game.path));
  });

  test('verified live originals are preserved in the canonical backup folder',
      () async {
    final root = await Directory.systemTemp.createTemp('magicka_originals_');
    addTearDown(() async {
      if (await root.exists()) await root.delete(recursive: true);
    });
    final game = Directory('${root.path}${Platform.pathSeparator}Magicka');
    final backup = Directory(
        '${game.path}${Platform.pathSeparator}CommunityPatch${Platform.pathSeparator}backup');
    await game.create(recursive: true);

    final magickaBytes = utf8.encode('official Magicka executable');
    final polygonBytes = utf8.encode('official PolygonHead library');
    final magickaSpec = OriginalGameFileSpec(
      fileName: 'Magicka.exe',
      size: magickaBytes.length,
      sha256: sha256.convert(magickaBytes).toString(),
    );
    final polygonSpec = OriginalGameFileSpec(
      fileName: 'PolygonHead.dll',
      size: polygonBytes.length,
      sha256: sha256.convert(polygonBytes).toString(),
    );
    await File('${game.path}${Platform.pathSeparator}Magicka.exe')
        .writeAsBytes(magickaBytes);
    await File('${game.path}${Platform.pathSeparator}PolygonHead.dll')
        .writeAsBytes(polygonBytes);

    final store = OriginalGameFileStore(
      magickaSpec: magickaSpec,
      polygonHeadSpec: polygonSpec,
    );
    final resolved = await store.resolve(
      gameDirectory: game.path,
      backupDirectory: backup.path,
    );

    expect(resolved.complete, isTrue);
    expect(resolved.magicka,
        '${backup.path}${Platform.pathSeparator}Magicka.exe.original');
    expect(resolved.polygonHead,
        '${backup.path}${Platform.pathSeparator}PolygonHead.dll.original');
    expect(await store.matches(resolved.magicka!, magickaSpec), isTrue);
    expect(await store.matches(resolved.polygonHead!, polygonSpec), isTrue);
  });

  test('invalid original backup is preserved and never accepted', () async {
    final root = await Directory.systemTemp.createTemp('magicka_backups_');
    addTearDown(() async {
      if (await root.exists()) await root.delete(recursive: true);
    });
    final game = Directory('${root.path}${Platform.pathSeparator}Magicka');
    final backup = Directory(
        '${game.path}${Platform.pathSeparator}CommunityPatch${Platform.pathSeparator}backup');
    await backup.create(recursive: true);

    final magickaBytes = utf8.encode('official executable');
    final polygonBytes = utf8.encode('official library');
    final magickaSpec = OriginalGameFileSpec(
      fileName: 'Magicka.exe',
      size: magickaBytes.length,
      sha256: sha256.convert(magickaBytes).toString(),
    );
    final polygonSpec = OriginalGameFileSpec(
      fileName: 'PolygonHead.dll',
      size: polygonBytes.length,
      sha256: sha256.convert(polygonBytes).toString(),
    );
    final invalid =
        File('${backup.path}${Platform.pathSeparator}Magicka.exe.original');
    await invalid.writeAsString('previous patched executable');
    await File('${game.path}${Platform.pathSeparator}Magicka.exe.backup')
        .writeAsBytes(magickaBytes);
    await File('${game.path}${Platform.pathSeparator}PolygonHead.dll')
        .writeAsString('patched library');

    final store = OriginalGameFileStore(
      magickaSpec: magickaSpec,
      polygonHeadSpec: polygonSpec,
    );
    final resolved = await store.resolve(
      gameDirectory: game.path,
      backupDirectory: backup.path,
      manifestMagickaPath: invalid.path,
    );

    expect(resolved.magicka,
        '${backup.path}${Platform.pathSeparator}Magicka.exe.original.1');
    expect(resolved.polygonHead, isNull);
    expect(await invalid.readAsString(), 'previous patched executable');
    expect(await store.matches(resolved.magicka!, magickaSpec), isTrue);
  });

  test('simplified Chinese locale resolves from system and command line', () {
    final systemSelection =
        resolveAppLocaleSelection(const <String>[], const Locale('zh', 'CN'));
    expect(systemSelection.language, AppLanguage.zhCN);
    expect(systemSelection.source, 'system');

    final commandLineSelection = resolveAppLocaleSelection(
        const <String>['--locale', '简体中文'], const Locale('en', 'US'));
    expect(commandLineSelection.language, AppLanguage.zhCN);
    expect(commandLineSelection.source, 'command_line');
    expect(AppStrings(AppLanguage.zhCN).t('installPatch'), '安装补丁');
  });

  test('system language suggestions distinguish simplified Chinese', () {
    expect(
        suggestedOptionalLanguageCodesForSystemLocale(const Locale('zh', 'CN')),
        <String>{'zho'});
    expect(
        suggestedOptionalLanguageCodesForSystemLocale(const Locale('zh', 'SG')),
        <String>{'zho'});
    expect(
        suggestedOptionalLanguageCodesForSystemLocale(
            const Locale.fromSubtags(languageCode: 'zh', scriptCode: 'Hans')),
        <String>{'zho'});
    expect(
        suggestedOptionalLanguageCodesForSystemLocale(const Locale('zh', 'TW')),
        isEmpty);
    expect(
        suggestedOptionalLanguageCodesForSystemLocale(
            const Locale.fromSubtags(languageCode: 'zh', scriptCode: 'Hant')),
        isEmpty);
    expect(
        suggestedOptionalLanguageCodesForSystemLocale(const Locale('de', 'DE')),
        isEmpty);
  });

  test('GC diagnostics payload installs under CommunityPatch', () async {
    final root = await Directory.systemTemp.createTemp('magicka_gc_payload_');
    addTearDown(() async {
      if (await root.exists()) await root.delete(recursive: true);
    });
    final payload = await _createGcDiagnosticsPayload(root);
    final game = Directory('${root.path}${Platform.pathSeparator}game');
    await game.create(recursive: true);

    await installGcDiagnostics(
      gameDirectory: game.path,
      payloadDirectory: payload,
    );

    final installed = Directory(
        '${game.path}${Platform.pathSeparator}CommunityPatch${Platform.pathSeparator}GcDiagnostics');
    expect(
        await File(
                '${installed.path}${Platform.pathSeparator}Magicka.GcAnalyzer.exe')
            .exists(),
        isTrue);
    expect(
        await File(
                '${installed.path}${Platform.pathSeparator}Microsoft.Diagnostics.Runtime.dll')
            .exists(),
        isTrue);
  });

  test('optional Chinese install backs up and restores an existing zho folder',
      () async {
    final root = await Directory.systemTemp.createTemp('magicka_zho_existing_');
    addTearDown(() async {
      if (await root.exists()) await root.delete(recursive: true);
    });
    final payload = await _createChinesePayload(root);
    final game = Directory('${root.path}${Platform.pathSeparator}game');
    final english = Directory(
        '${game.path}${Platform.pathSeparator}Content${Platform.pathSeparator}Languages${Platform.pathSeparator}eng');
    final chinese = Directory(
        '${game.path}${Platform.pathSeparator}Content${Platform.pathSeparator}Languages${Platform.pathSeparator}zho');
    final backup = Directory(
        '${game.path}${Platform.pathSeparator}CommunityPatch${Platform.pathSeparator}backup');
    await english.create(recursive: true);
    await chinese.create(recursive: true);
    await File('${english.path}${Platform.pathSeparator}keep.txt')
        .writeAsString('english');
    await File('${chinese.path}${Platform.pathSeparator}user.txt')
        .writeAsString('original Chinese files');

    final result = await installOptionalSimplifiedChinese(
      gameDirectory: game.path,
      backupDirectory: backup.path,
      existingManifest: const <String, String>{},
      payloadDirectory: payload,
      now: DateTime.utc(2026, 8, 27),
    );

    expect(result.installed, isTrue);
    expect(result.hadExistingDirectory, isTrue);
    expect(
        await File('${chinese.path}${Platform.pathSeparator}UI.loctable.xml')
            .exists(),
        isTrue);
    expect(
        await File('${chinese.path}${Platform.pathSeparator}user.txt').exists(),
        isFalse);
    expect(
        await File(
                '${result.originalBackupDirectory}${Platform.pathSeparator}user.txt')
            .readAsString(),
        'original Chinese files');
    expect(
        await File('${english.path}${Platform.pathSeparator}keep.txt')
            .readAsString(),
        'english');

    await restoreOptionalSimplifiedChinese(
      gameDirectory: game.path,
      manifest: <String, String>{
        'simplified_chinese_installed': 'true',
        'simplified_chinese_had_existing': 'true',
        'original_simplified_chinese_backup': result.originalBackupDirectory,
      },
    );
    expect(
        await File('${chinese.path}${Platform.pathSeparator}user.txt')
            .readAsString(),
        'original Chinese files');
    expect(
        await File('${chinese.path}${Platform.pathSeparator}UI.loctable.xml')
            .exists(),
        isFalse);
    expect(
        await File('${english.path}${Platform.pathSeparator}keep.txt')
            .readAsString(),
        'english');
  });

  test('optional Chinese uninstall removes a newly created zho folder',
      () async {
    final root = await Directory.systemTemp.createTemp('magicka_zho_clean_');
    addTearDown(() async {
      if (await root.exists()) await root.delete(recursive: true);
    });
    final payload = await _createChinesePayload(root);
    final game = Directory('${root.path}${Platform.pathSeparator}game');
    final backup = Directory(
        '${game.path}${Platform.pathSeparator}CommunityPatch${Platform.pathSeparator}backup');
    final result = await installOptionalSimplifiedChinese(
      gameDirectory: game.path,
      backupDirectory: backup.path,
      existingManifest: const <String, String>{},
      payloadDirectory: payload,
    );
    expect(result.hadExistingDirectory, isFalse);

    await restoreOptionalSimplifiedChinese(
      gameDirectory: game.path,
      manifest: const <String, String>{
        'simplified_chinese_installed': 'true',
        'simplified_chinese_had_existing': 'false',
        'original_simplified_chinese_backup': '',
      },
    );
    expect(
        await Directory(
                '${game.path}${Platform.pathSeparator}Content${Platform.pathSeparator}Languages${Platform.pathSeparator}zho')
            .exists(),
        isFalse);
  });

  testWidgets('installer app smoke test', (WidgetTester tester) async {
    await tester.pumpWidget(MagickaPatchApp(
      forceUpdater: true,
      localeSelection: resolveAppLocaleSelection(
          const <String>['--locale', 'de-DE'], const Locale('en', 'US')),
    ));
    await tester.pump();

    expect(find.text('MAGICKA COMMUNITY PATCH 0.0.50'), findsOneWidget);
    expect(find.text('Patch-Update 0.0.50'), findsOneWidget);
    expect(find.text('SonofKalas'), findsWidgets);
    expect(find.text('莎德娜丝（Sadness）'), findsWidgets);
    expect(find.text('Extensive bug reports, playtesting & screen sharing'),
        findsWidgets);
    expect(find.byType(PrioritySupporterBadge), findsWidgets);
  });

  testWidgets('installer subtitle stays above its status line',
      (WidgetTester tester) async {
    await tester.binding.setSurfaceSize(const Size(1605, 949));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    await tester.pumpWidget(MaterialApp(
      locale: const Locale('de', 'DE'),
      supportedLocales: AppLanguage.supportedLocales,
      localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
        AppStrings.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: const InstallerScreen(detectGameOnStart: false),
    ));
    await tester.pump();

    final subtitle =
        tester.getRect(find.byKey(const ValueKey('installer-header-subtitle')));
    final status =
        tester.getRect(find.byKey(const ValueKey('installer-status-text')));
    expect(subtitle.bottom, lessThanOrEqualTo(status.top));
    expect(find.byKey(const ValueKey('optional-languages-button')),
        findsOneWidget);
    final optionsPanel = find.byKey(const ValueKey('installer-options-panel'));
    final languageButton =
        find.byKey(const ValueKey('optional-languages-button'));
    expect(find.descendant(of: optionsPanel, matching: languageButton),
        findsOneWidget);
    final panelRect = tester.getRect(optionsPanel);
    final buttonRect = tester.getRect(languageButton);
    final panelScale = panelRect.width / 946;
    expect(buttonRect.left, closeTo(panelRect.left + 16 * panelScale, 0.5));
    expect(buttonRect.top, greaterThan(panelRect.center.dy));
    expect(buttonRect.bottom, closeTo(panelRect.bottom - 21 * panelScale, 0.5));
    final styledButton = tester.widget<FlameButton>(languageButton);
    expect(styledButton.overlayIcon, isTrue);
    expect(styledButton.effects, isFalse);
    expect(tester.takeException(), isNull);
  });

  testWidgets('optional language button opens a reusable language list',
      (WidgetTester tester) async {
    await tester.binding.setSurfaceSize(const Size(1605, 949));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    await tester.pumpWidget(MaterialApp(
      locale: const Locale('en', 'US'),
      supportedLocales: AppLanguage.supportedLocales,
      localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
        AppStrings.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: const InstallerScreen(detectGameOnStart: false),
    ));
    await tester.pump();

    expect(find.text('No additional language selected'), findsOneWidget);
    await tester.tap(find.byKey(const ValueKey('optional-languages-button')));
    await tester.pump(const Duration(milliseconds: 300));

    expect(find.text('Install language files'), findsOneWidget);
    expect(find.byKey(const ValueKey('optional-language-zho')), findsOneWidget);
    expect(find.text('Simplified Chinese'), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('optional-language-zho')));
    await tester.pump();
    expect(
        tester
            .widget<CheckboxListTile>(
                find.byKey(const ValueKey('optional-language-zho')))
            .value,
        isTrue);

    await tester.tap(find.byKey(const ValueKey('apply-optional-languages')));
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.text('Selected: Simplified Chinese'), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('optional-languages-button')));
    await tester.pump(const Duration(milliseconds: 300));
    expect(
        tester
            .widget<CheckboxListTile>(
                find.byKey(const ValueKey('optional-language-zho')))
            .value,
        isTrue);
    await tester.tap(find.byKey(const ValueKey('cancel-optional-languages')));
    await tester.pump(const Duration(milliseconds: 300));
    expect(tester.takeException(), isNull);
  });

  testWidgets('install offers a matching unselected system language',
      (WidgetTester tester) async {
    tester.binding.platformDispatcher.localeTestValue =
        const Locale('zh', 'CN');
    addTearDown(() => tester.binding.platformDispatcher.clearLocaleTestValue());
    late BuildContext promptContext;

    await tester.pumpWidget(MaterialApp(
      locale: const Locale('en', 'US'),
      supportedLocales: AppLanguage.supportedLocales,
      localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
        AppStrings.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: Builder(builder: (context) {
        promptContext = context;
        return const SizedBox();
      }),
    ));
    await tester.pump();
    final result = offerSystemLanguageIfAvailable(
      promptContext,
      const <String>{},
      systemLocale: const Locale('zh', 'CN'),
    );
    await tester.pump(const Duration(milliseconds: 300));

    expect(find.byKey(const ValueKey('system-language-suggestion')),
        findsOneWidget);
    expect(find.text('Install your system language?'), findsOneWidget);
    expect(
        find.byKey(const ValueKey('accept-system-language')), findsOneWidget);
    expect(
        find.byKey(const ValueKey('decline-system-language')), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.byKey(const ValueKey('decline-system-language')));
    await tester.pumpAndSettle();
    expect(await result, isEmpty);
  });

  testWidgets('install does not re-offer an already selected system language',
      (WidgetTester tester) async {
    tester.binding.platformDispatcher.localeTestValue =
        const Locale('zh', 'CN');
    addTearDown(() => tester.binding.platformDispatcher.clearLocaleTestValue());
    late BuildContext promptContext;

    await tester.pumpWidget(MaterialApp(
      locale: const Locale('en', 'US'),
      supportedLocales: AppLanguage.supportedLocales,
      localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
        AppStrings.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: Builder(builder: (context) {
        promptContext = context;
        return const SizedBox();
      }),
    ));
    await tester.pump();
    final result = await offerSystemLanguageIfAvailable(
      promptContext,
      const <String>{'zho'},
      systemLocale: const Locale('zh', 'CN'),
    );

    expect(
        find.byKey(const ValueKey('system-language-suggestion')), findsNothing);
    expect(result, isEmpty);
    expect(tester.takeException(), isNull);
  });

  testWidgets('priority supporter description stays above its badge',
      (WidgetTester tester) async {
    const person = SpecialThanksPerson(
      name: 'SonofKalas',
      description: 'Fix Requests & Priorities Patreon supporter',
      accent: Color(0xffd99cff),
      supporter: true,
      prioritySupporter: true,
    );

    Future<void> pumpCard(Size size) async {
      await tester.pumpWidget(MaterialApp(
        locale: const Locale('de', 'DE'),
        supportedLocales: AppLanguage.supportedLocales,
        localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
          AppStrings.delegate,
          GlobalMaterialLocalizations.delegate,
          GlobalWidgetsLocalizations.delegate,
          GlobalCupertinoLocalizations.delegate,
        ],
        home: Center(
          child: SizedBox(
            width: size.width,
            height: size.height,
            child: const SpecialThanksCard(person: person, starProgram: null),
          ),
        ),
      ));
      await tester.pump();
    }

    for (final size in const <Size>[Size(312, 90), Size(360, 116)]) {
      await pumpCard(size);
      final description = tester.getRect(
          find.byKey(const ValueKey('special-thanks-description-SonofKalas')));
      final badge = tester.getRect(
          find.byKey(const ValueKey('special-thanks-badge-SonofKalas')));
      expect(description.bottom, lessThanOrEqualTo(badge.top));
      expect(
          tester
              .renderObject<RenderParagraph>(find.descendant(
                of: find.byKey(
                    const ValueKey('special-thanks-description-SonofKalas')),
                matching: find.text(person.description),
              ))
              .didExceedMaxLines,
          isFalse);
      expect(tester.takeException(), isNull);
    }
  });

  testWidgets('button and localized event-card copy fits without ellipses',
      (WidgetTester tester) async {
    await tester.pumpWidget(MaterialApp(
      home: Center(
        child: SizedBox(
          width: 195,
          height: 34,
          child: FlameButton(
            program: null,
            label: 'Automatisch finden',
            icon: Icons.search,
            accent: const Color(0xff3f9fff),
            overlayIcon: true,
            effects: false,
            onTap: () {},
          ),
        ),
      ),
    ));
    await tester.pump();
    expect(
        tester
            .renderObject<RenderParagraph>(find.text('Automatisch finden'))
            .didExceedMaxLines,
        isFalse);

    for (final language in AppLanguage.valuesInMenuOrder) {
      final strings = AppStrings(language);
      final samples = <(String, String)>[
        (
          strings.t('eventGameClosed'),
          strings.t('eventGameClosedBody'),
        ),
        (
          strings.t('eventCrashReport'),
          strings.t('eventCrashReportBody'),
        ),
      ];
      for (final sample in samples) {
        await tester.pumpWidget(MaterialApp(
          locale: language.locale,
          supportedLocales: AppLanguage.supportedLocales,
          localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
            AppStrings.delegate,
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          home: Center(
            child: SizedBox(
              width: 174,
              height: 112,
              child: Row(children: <Widget>[
                EventCard(
                    icon: Icons.warning_amber_rounded,
                    title: sample.$1,
                    body: sample.$2,
                    accent: const Color(0xffd63a20)),
              ]),
            ),
          ),
        ));
        await tester.pump();
        expect(
            tester
                .renderObject<RenderParagraph>(find.text(sample.$1))
                .didExceedMaxLines,
            isFalse,
            reason: '${language.localeTag} event title: ${sample.$1}');
        expect(
            tester
                .renderObject<RenderParagraph>(find.text(sample.$2))
                .didExceedMaxLines,
            isFalse,
            reason: '${language.localeTag} event body: ${sample.$2}');
        expect(tester.takeException(), isNull);
      }
    }
  });

  testWidgets('Skappnil detail links the Bitesquid Mod Loader',
      (WidgetTester tester) async {
    await tester.binding.setSurfaceSize(const Size(1000, 700));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    String? openedUrl;
    const person = SpecialThanksPerson(
      name: 'Skappnil',
      description: 'Bitesquid Mod Loader developer',
      accent: Color(0xffab4fff),
      avatarAsset: 'assets/Skappnil.png',
      featureAsset: 'assets/bitesquid-mod-loader.jpg',
      featureUrl: AppConstants.bitesquidModLoaderUrl,
    );

    await tester.pumpWidget(MaterialApp(
      supportedLocales: AppLanguage.supportedLocales,
      localizationsDelegates: const <LocalizationsDelegate<dynamic>>[
        AppStrings.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      home: SpecialThanksDetailDialog(
        person: person,
        starProgram: null,
        openUrl: (url) async => openedUrl = url,
      ),
    ));
    await tester.pumpAndSettle();

    expect(
        find.byKey(const ValueKey('skappnil-mod-loader-logo')), findsOneWidget);
    expect(
        find.byKey(const ValueKey('skappnil-mod-loader-link')), findsOneWidget);
    expect(find.text('Steam Workshop'), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('skappnil-mod-loader-link')));
    await tester.pump();

    expect(openedUrl, AppConstants.bitesquidModLoaderUrl);
  });

  testWidgets('priority diamond owns a slow hover cycle and resets on exit',
      (WidgetTester tester) async {
    PrioritySupporterBadgePainter painter() {
      final paint = tester.widget<CustomPaint>(find.descendant(
        of: find.byType(PrioritySupporterBadge),
        matching: find.byType(CustomPaint),
      ));
      return paint.painter! as PrioritySupporterBadgePainter;
    }

    await tester.pumpWidget(const Directionality(
      textDirection: TextDirection.ltr,
      child: Center(child: PrioritySupporterBadge(active: false)),
    ));
    expect(tester.getSize(find.byType(PrioritySupporterBadge)),
        const Size(60, 70));
    expect(painter().time, 0);

    await tester.pumpWidget(const Directionality(
      textDirection: TextDirection.ltr,
      child: Center(child: PrioritySupporterBadge(active: true)),
    ));
    await tester.pump(const Duration(milliseconds: 1400));

    expect(PrioritySupporterBadge.sweepDuration,
        const Duration(milliseconds: 5600));
    expect(
        PrioritySupporterBadge.sweepDuration.inMilliseconds, greaterThan(1500));
    expect(painter().time, closeTo(0.25, 0.01));

    await tester.pumpWidget(const Directionality(
      textDirection: TextDirection.ltr,
      child: Center(child: PrioritySupporterBadge(active: false)),
    ));
    expect(painter().active, isFalse);
    expect(painter().time, 0);
  });

  test('priority diamond caustic wraps while fully outside the diamond', () {
    const size = Size(60, 70);
    final direction = PrioritySupporterBadgePainter.causticDirection;
    final halfWidth =
        PrioritySupporterBadgePainter.causticBandHalfWidthFor(size);
    final vertices = PrioritySupporterBadgePainter.diamondVerticesFor(size);
    double projection(Offset point) =>
        point.dx * direction.dx + point.dy * direction.dy;
    final minProjection =
        vertices.map(projection).reduce((a, b) => a < b ? a : b);
    final maxProjection =
        vertices.map(projection).reduce((a, b) => a > b ? a : b);
    final startProjection =
        projection(PrioritySupporterBadgePainter.causticCenterFor(size, 0));
    final endProjection =
        projection(PrioritySupporterBadgePainter.causticCenterFor(size, 1));

    expect(startProjection + halfWidth, lessThan(minProjection));
    expect(endProjection - halfWidth, greaterThan(maxProjection));
  });

  test('priority diamond extends only its lower geometry', () {
    const oldSize = Size(60, 62);
    const extendedSize = Size(60, 70);
    final oldVertices =
        PrioritySupporterBadgePainter.diamondVerticesFor(oldSize);
    final extendedVertices =
        PrioritySupporterBadgePainter.diamondVerticesFor(extendedSize);

    for (final index in const <int>[0, 1, 2, 4]) {
      expect(extendedVertices[index], oldVertices[index]);
    }
    expect(extendedVertices[0].dx, closeTo(17.4, 0.001));
    expect(extendedVertices[0].dy, closeTo(7.44, 0.001));
    expect(extendedVertices[2].dx, closeTo(54, 0.001));
    expect(extendedVertices[2].dy, closeTo(21.7, 0.001));
    expect(extendedVertices[3].dy, closeTo(50.0, 0.001));
    expect(extendedVertices[3].dy, greaterThan(oldVertices[3].dy));

    expect(PrioritySupporterBadgePainter.diamondTableVerticesFor(extendedSize),
        PrioritySupporterBadgePainter.diamondTableVerticesFor(oldSize));
    final table =
        PrioritySupporterBadgePainter.diamondTableVerticesFor(extendedSize);
    const expectedTable = <Offset>[
      Offset(24, 12.4),
      Offset(36, 12.4),
      Offset(40.2, 21.7),
      Offset(19.8, 21.7),
    ];
    for (var index = 0; index < table.length; index++) {
      expect(table[index].dx, closeTo(expectedTable[index].dx, 0.001));
      expect(table[index].dy, closeTo(expectedTable[index].dy, 0.001));
    }
  });

  test('priority diamond sparkles fade in separate windows without popping',
      () {
    const centers = <double>[0.10, 0.30, 0.72];
    for (final center in centers) {
      expect(PrioritySupporterBadgePainter.sparkleEnvelopeFor(0, center), 0);
      expect(PrioritySupporterBadgePainter.sparkleEnvelopeFor(1, center), 0);
      expect(
          PrioritySupporterBadgePainter.sparkleEnvelopeFor(center, center), 1);
    }
    for (var i = 0; i < centers.length; i++) {
      for (var j = 0; j < centers.length; j++) {
        if (i == j) continue;
        expect(
            PrioritySupporterBadgePainter.sparkleEnvelopeFor(
                centers[i], centers[j]),
            0);
      }
    }
  });

  test('priority diamond registers and isolates its supplied star shader', () {
    final pubspec = File('pubspec.yaml').readAsStringSync();
    final shader = File('shaders/diamond_edge_star.frag').readAsStringSync();
    final mainSource = File('lib/main.dart').readAsStringSync();

    expect(pubspec, contains('- shaders/diamond_edge_star.frag'));
    expect(shader, contains('float Star(vec2 uv, float flare)'));
    expect(shader, contains('0.02 / max(d, 0.001)'));
    expect(shader, contains('uv.x * uv.y * 1000.0'));
    expect(shader, contains('uv *= Rot(3.1415 / 4.0)'));
    expect(shader, contains('m += rays * 0.3 * flare'));
    expect(shader, contains('1.0 - smoothstep(0.2, 1.0, d)'));
    expect(mainSource, isNot(contains('_drawDiamondEdgeSparkle')));
  });
}
