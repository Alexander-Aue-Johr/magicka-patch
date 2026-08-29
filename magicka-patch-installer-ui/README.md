# Magicka Community Patch Installer / Updater

Version: **0.0.49**

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

For a release build, run the repository release script from the repo root. It copies the current patched payload files from the Steam Magicka folder into the repo before packaging:

```powershell
.\scripts\build-release.ps1
```

Pass `-MagickaDir "G:\SteamLibrary\steamapps\common\Magicka"` if Steam cannot be detected automatically.

The Flutter Windows build copies the repo-root payload files next to the generated installer EXE:

```text
build\windows\x64\runner\Release\Magicka.exe
build\windows\x64\runner\Release\PolygonHead.dll
build\windows\x64\runner\Release\Magicka.GcDiagnostics.dll
build\windows\x64\runner\Release\gc-diagnostics\
```

Release installer packages also contain `optional-languages\zho`. The
**Languages...** dialog currently offers Simplified Chinese and installs this
directory as `Content\Languages\zho`; it does not replace another game
language. The action is part of the installer options panel. When an unselected
optional language matches the system locale, installation asks once whether to
include it. Further language packages can be added to the same dialog.

The auto-updater continues to consume the files-only ZIP. It installs the GC
diagnostic runtime with the patched executable. It does not install optional
language packages or start another language download.

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

## Original-file recovery

Before replacing or restoring `Magicka.exe` and `PolygonHead.dll`, the installer
verifies candidate original files by size and SHA-256 against the supported
Steam build. It searches the install manifest, `CommunityPatch\backup`, plausible
manual backups next to the game executable, and finally the currently installed
game files. Verified originals found outside the backup directory are copied to
`CommunityPatch\backup` first. Files that merely have an original-looking name
but do not match the catalog are never used as restore backups.

If either original cannot be found, the installer explains that Steam validation
can also replace other modified game files and asks for confirmation before
opening:

```text
steam://validate/42910
```

After Steam finishes, the player selects **Check again**. Installation or
uninstallation continues only when both official files have been verified and
saved. Cancelling prevents the Community Patch from replacing files, although
Steam may already have restored official files. The Steam URI is a convenience
integration; players can still start file validation manually from Magicka's
Steam properties if protocol handling is unavailable.

## Auto-Update Release ZIP

The game-side updater checks:

```text
https://api.github.com/repos/Alexander-Aue-Johr/magicka-patch/releases/latest
```

The files-only release asset must contain a version newer than
`CommunityPatchInfo.Version`, for example:

```text
magicka-community-patch-0.0.49-files-only.zip
```

Files-only ZIP for manual installation and patch-only updates:

```text
magicka-community-patch-0.0.49-files-only.zip
- Magicka.exe
- PolygonHead.dll
- Magicka.GcDiagnostics.dll
- gc-diagnostics\
- patch-settings.ini
- README.txt
```

Full ZIP when the Flutter tool/runtime should update too:

```text
magicka-community-patch-0.0.49-installer.zip
- README.txt
- MagickaPatchInstaller.exe
- Magicka.exe
- PolygonHead.dll
- Magicka.GcDiagnostics.dll
- gc-diagnostics\
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

Players should start `MagickaPatchInstaller.exe` from the release ZIP root. The root `flutter_windows.dll` and `data\` folder are the runtime files it needs. The updater requires `Magicka.exe`, `PolygonHead.dll`, `Magicka.GcDiagnostics.dll`, and `gc-diagnostics\`; if `tools\installer\MagickaPatchTool.exe` is present, it silently replaces `MagickaPatchInstaller.exe`, `MagickaPatchTool.exe`, `MagickaPatchUninstaller.exe`, `flutter_windows.dll` and `data\` after the running updater exits.

The files-only ZIP is intended for manual Windows or Linux installation and is
also the asset used by the game-side release check and optional auto-updater.

## Game-Side Settings Defaults

Manual installs must copy `Magicka.exe`, `PolygonHead.dll`,
`Magicka.GcDiagnostics.dll`, and the diagnostic directory listed in the
files-only README. Without installer settings, telemetry and the online release
check default to on, while automatic update installation defaults to off:

```text
usage_sharing=true
crash_reports=true
check_for_updates=true
auto_update=false
use_magicka_1_controller_scheme=false
```

The installer writes `CommunityPatch\patch-settings.ini` based on the selected
checkboxes. A manual install can copy the template from the files-only ZIP to
that same path. Set `usage_sharing=false`, `crash_reports=false`, and
`check_for_updates=false` to disable telemetry and online release checks.
The Magicka 2-style XInput controller scheme is enabled by default. Set
`use_magicka_1_controller_scheme=true` to restore the original configurable
controller scheme; installer and updater runs preserve this choice.

## Helper Messages And Feedback

The player UI avoids the word "telemetry" and explains the feature as short anonymous helper messages.

Normal helper messages effectively send only the event name, for example:

```text
magicka_patch_installed
magicka_patch_auto_update
magicka_patch_start
magicka_patch_game_closed_normally
```

Crash helper messages additionally send a shortened, redacted error message and a short error hash.
The normal-close and crash events also include aggregate process-session counts
for keyboard/mouse and controller element selections and the controller share
from 0 to 1. They do not include which elements were selected, their order, or
player identities.

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
