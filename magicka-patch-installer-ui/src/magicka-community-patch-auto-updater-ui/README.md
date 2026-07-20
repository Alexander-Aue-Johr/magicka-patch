# Magicka Community Patch Auto Updater UI

Small Flutter entry point for the prepared-update UI.

The UI widgets, shaders, assets, Patreon effects, feedback dialog, and special
thanks banner are reused from the parent installer package.

Expected updater arguments:

```text
--offer-pending-update <gameDir> <version> <source> --wait-pid <pid>
--apply-update <source> <gameDir> <version> --wait-pid <pid>
```

The screen waits for the optional process id, extracts a prepared zip package
or uses a prepared directory, copies `Magicka.exe` and `PolygonHead.dll` into
the Magicka folder, keeps the current settings, and removes
`CommunityPatch/pending-update.ini`.

For release packages, `scripts/build-release.ps1` builds the parent installer
Windows app and copies that runner as `MagickaPatchAutoUpdater.exe`. The updater
mode is selected by the command-line arguments, so this package does not track a
separate Windows runner project.

To run the updater UI from the parent installer project:

```powershell
cd ..\..
flutter run -d windows -- --offer-pending-update "G:\SteamLibrary\steamapps\common\Magicka" "0.0.34" "C:\path\to\update.zip"
```
