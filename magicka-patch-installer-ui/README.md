# Magicka Community Patch Installer / Updater

Version: **0.0.17**

This directory contains the Flutter Windows UI for the Magicka Community Patch installer, updater and uninstaller surface.

## Projects

- `./`
  Main Flutter installer UI. It installs the patched game files, writes settings, installs the tool runtime and can also run as uninstaller.
- `src/magicka-community-patch-auto-updater-ui/`
  Small Flutter entry point for the automatic update flow. It reuses the installer UI package.

## Flutter Build

Build the installer:

```powershell
flutter clean
flutter pub get
flutter build windows --release
```

Build the auto-updater entry point:

```powershell
cd src\magicka-community-patch-auto-updater-ui
flutter clean
flutter pub get
flutter build windows --release
```

The Flutter Windows build output is under:

```text
build\windows\x64\runner\Release
```

For the installer release, copy the patched payload files into the Flutter asset payload folder before building:

```text
assets\payload\Magicka.exe
assets\payload\PolygonHead.dll
```

The installer also accepts these fallback locations while developing:

```text
Magicka.exe
PolygonHead.dll
Payload\Magicka.exe
Payload\PolygonHead.dll
assets\payload\Magicka.exe
assets\payload\PolygonHead.dll
```

## Installed Tool Files

The installer copies the Flutter runtime into the Magicka folder:

```text
MagickaPatchInstaller.exe
MagickaPatchTool.exe
MagickaPatchUninstaller.exe
flutter_windows.dll
data\
uninstall_magicka_patch.cmd
CommunityPatch\
```

`MagickaPatchInstaller.exe` is the player-facing installer. `MagickaPatchTool.exe` is the internal auto-update entry point used by the patched game, and `MagickaPatchUninstaller.exe` opens the uninstall UI.

## Auto-Update Release ZIP

The game-side updater checks:

```text
https://api.github.com/repos/Alexander-Aue-Johr/magicka-patch/releases/latest
```

The release tag must be newer than `CommunityPatchInfo.Version`, for example:

```text
v0.0.17
```

Minimal ZIP for patch-only updates:

```text
magicka-community-patch-0.0.17.zip
- Magicka.exe
- PolygonHead.dll
```

Full ZIP when the Flutter tool/runtime should update too:

```text
magicka-community-patch-0.0.17.zip
- MagickaPatchInstaller.exe
- Magicka.exe
- PolygonHead.dll
- flutter_windows.dll
- data\
  - flutter_assets\
- tools\
  - installer\
    - MagickaPatchInstaller.exe
    - MagickaPatchTool.exe
    - MagickaPatchUninstaller.exe
    - flutter_windows.dll
    - data\
      - flutter_assets\
  - auto-updater\
    - MagickaPatchAutoUpdater.exe
    - flutter_windows.dll
    - data\
      - flutter_assets\
```

Players should start `MagickaPatchInstaller.exe` from the release ZIP root. The root `flutter_windows.dll` and `data\` folder are the runtime files it needs. The updater requires `Magicka.exe` and `PolygonHead.dll`; if `tools\installer\MagickaPatchTool.exe` is present, it silently replaces `MagickaPatchInstaller.exe`, `MagickaPatchTool.exe`, `MagickaPatchUninstaller.exe`, `flutter_windows.dll` and `data\` after the running updater exits.

## Game-Side Settings Defaults

If players copy only `Magicka.exe` and `PolygonHead.dll` into the game folder, no installer settings exist. In that portable case everything defaults to off:

```text
usage_sharing=false
crash_reports=false
auto_update=false
```

The installer writes `CommunityPatch\patch-settings.ini` based on the selected checkboxes.

## Helper Messages And Feedback

The player UI avoids the word "telemetry" and explains the feature as short anonymous helper messages.

Normal helper messages effectively send only the event name, for example:

```text
magicka_patch_installed_0_0_12
magicka_patch_auto_updated_<version>
magicka_patch_game_started_0_0_12
magicka_patch_game_closed_0_0_12
```

Crash helper messages additionally send a shortened, redacted error message and a short error hash.

Feedback messages send only data the player explicitly enters:

- optional name
- optional subject
- message
- context: `install`, `auto_update`, `uninstall` or `manual`

Local helper messages are written here first:

```text
<Magicka>\CommunityPatch\event-log.jsonl
<Magicka>\CommunityPatch\event-log.sent
```

If there is no internet connection, events remain in the local log and are retried later.

## Third-Party Shader Notices

Some Flutter shader effects are adapted from public Shadertoy examples. See
`THIRD_PARTY_NOTICES.md` for source links and attribution details.

## Redistribution Warning

This project can technically ship patched `Magicka.exe` and `PolygonHead.dll`, but only distribute those files if you have the legal right to distribute them. If not, ship a patcher or binary delta and create the patched `Magicka.exe` locally from the player's own Steam installation.
