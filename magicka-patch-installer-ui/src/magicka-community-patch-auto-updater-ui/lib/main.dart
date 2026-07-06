import 'package:magicka_community_patch_installer_ui/main.dart' as installer;

void main(List<String> args) {
  installer.runMagickaPatchApp(
    args,
    forceUpdater: true,
    assetPackage: 'magicka_community_patch_installer_ui',
  );
}
