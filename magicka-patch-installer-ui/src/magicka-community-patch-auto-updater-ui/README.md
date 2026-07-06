# Magicka Community Patch Auto Updater UI

Small Flutter Windows shell for the prepared-update UI.

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

To run or build the Windows updater:

```powershell
flutter run -d windows -- --offer-pending-update "G:\SteamLibrary\steamapps\common\Magicka" "0.0.13" "C:\path\to\update.zip"
flutter build windows --release
```
