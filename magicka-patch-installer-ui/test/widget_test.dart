// This is a basic Flutter widget test.
//
// To perform an interaction with a widget in your test, use the WidgetTester
// utility in the flutter_test package. For example, you can send tap and scroll
// gestures. You can also use WidgetTester to find child widgets in the widget
// tree, read text, and verify that the values of widget properties are correct.

import 'package:flutter/material.dart';
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

    expect(find.text('MAGICKA COMMUNITY PATCH 0.0.26'), findsOneWidget);
    expect(find.text('Patch-Update 0.0.26'), findsOneWidget);
  });
}
