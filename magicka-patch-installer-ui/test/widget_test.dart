// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:magicka_community_patch_installer_ui/localization.dart';
import 'package:magicka_community_patch_installer_ui/main.dart';

void main() {
  testWidgets('installer app smoke test', (WidgetTester tester) async {
    await tester.pumpWidget(MagickaPatchApp(
      forceUpdater: true,
      localeSelection: resolveAppLocaleSelection(
          const <String>['--locale', 'de-DE'], const Locale('en', 'US')),
    ));
    await tester.pump();

    expect(find.text('MAGICKA COMMUNITY PATCH 0.0.31'), findsOneWidget);
    expect(find.text('Patch-Update 0.0.31'), findsOneWidget);
    expect(find.text('SonofKalas'), findsWidgets);
    expect(find.byType(PrioritySupporterBadge), findsWidgets);
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
