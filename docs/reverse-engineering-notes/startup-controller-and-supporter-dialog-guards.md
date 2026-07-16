# Startup, controller, version-text, and supporter dialog guards

This patch addresses three startup/menu crash reports and adds an interaction
to the Community Patch credit line in the main menu.

## `Tome` version-text overflow

`Tome..ctor()` originally created its footer `PolygonHead.Text` with a capacity
of 32 characters and then assigned the longer game/patch version string.
`Text.SetText()` grows its buffers to exactly the assigned string length when
the original capacity is too small. If the core-file integrity check later
reported `HackHelper.Status.Hacked`, `Tome` appended `" (Modified)"` without
growing the buffer. `Text.Append()` has no capacity check and also writes a
terminating null character, so this branch wrote beyond the array and raised
`IndexOutOfRangeException`.

The exception was rare because the append only runs when the integrity check
classifies the installation as modified. Likely triggers include a modded,
renamed, missing, unreadable, or corrupt core XNB file, or files from an
incompatible game/patch state. It is not caused by an unusual display name or
normal menu input.

The patched constructor reserves 512 characters and replaces the unsafe append
with a helper that reconstructs the current visible string, adds the suffix,
and calls `SetText()`.

## Startup path and launch-option guards

`Program.Main()` opened `steam_api.dll` by a relative filename. The patched
path is derived from `Application.ExecutablePath`, so launchers and unusual
working directories cannot redirect the check to an invalid relative path. If
the file still cannot be opened, startup ends cleanly after the existing-style
Windows error dialog recommends starting through Steam and verifying the game
files.

The same method had three off-by-one argument checks: a trailing
`+connect_lobby`, `+connect`, or `+password` could pass an `args.Length > i`
test and then read `args[i + 1]`. Each check now requires the value element to
exist.

## Missing Managed DirectInput

The reported assembly is the legacy Managed DirectX 1.1 component
`Microsoft.DirectX.DirectInput, Version=1.0.2902.0`. Magicka ships the correct
Microsoft installer at:

```text
<Magicka>\Dependencies\directx_feb2010\DXSETUP.exe
```

The assembly itself is carried by `Apr2006_MDX1_x86.cab` in that directory.
Users should run `DXSETUP.exe` as administrator rather than manually extracting
or copying the DLL. The Community Patch installer already runs this
redistributable when its **Start Game** action detects that Managed DirectInput
is missing.

The CLR can fail while preparing `ControlManager.FindNewGamePads()` before its
first IL instruction; the `0xffffffff` stack offset in the report is consistent
with that boundary. The catch therefore lives in a DirectInput-free wrapper,
not inside the affected routine. A second wrapper protects all three calls to
`SubMenuOptionsControls.UpdateControllers()`, whose body also references
Managed DirectInput types.

On `FileNotFoundException` or `FileLoadException`, controller discovery is
disabled for the rest of the process. Exactly one thread atomically schedules
the warning. `MenuState.Update()` displays it on the logic thread only after
pending authentication errors and active widget popups are clear. The dialog
states that controllers cannot be used until Managed DirectX is installed,
points to both the patch installer and the exact relative `DXSETUP.exe` path,
and asks the user to restart Magicka. Keyboard and mouse navigation remain
available.

## Shared dialog and supporter list

The custom messages reuse the game's authentication-error presentation:
`ParadoxPopupUtils.ShowErrorPopup(string, string)` and its
`SingleButtonMessagePopup`. The overload is made assembly-visible and now
clears `ExtraMessage` before every custom message so an authentication error
code cannot leak into a later controller or supporter dialog.

`Tome.ControllerMouseAction()` now tests the exact screen rectangle used to
draw the bottom-left version/credit text. A completed left-button click opens a
dialog containing `CommunityPatchInfo.PatreonSupporters`; clicks during the
intro/cutscene, outside the rendered text, or button presses without a release
do nothing. Future supporter names belong in that property (and should remain
mirrored in `magicka-patch-installer-ui/supporters.json`).

The added helper implementation is documented in
`docs/injected-source/Magicka.CommunityPatch/RuntimeCompatibilityGuards.cs`.
