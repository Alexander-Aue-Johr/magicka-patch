Magicka Community Patch - manual installation
================================================

1. Close Magicka.
2. Open the Magicka installation directory (the folder containing Magicka.exe).
3. Back up the existing Magicka.exe and PolygonHead.dll.
4. Copy Magicka.exe and PolygonHead.dll from this ZIP into that directory and
   replace the existing files.

The patch-settings.ini file is optional. To use it, create a CommunityPatch
directory next to Magicka.exe and copy patch-settings.ini into that directory:

  <Magicka>\CommunityPatch\patch-settings.ini

Defaults when patch-settings.ini is missing
-------------------------------------------

- Telemetry is enabled.
- Checking GitHub for a newer files-only patch release is enabled.
- Automatically downloading and installing an update is disabled.
- The Magicka 2-style XInput controller scheme is enabled.

Controller scheme
-----------------

Default XInput gameplay controls:

  Left stick       Move
  Right stick      Aim / cast a queued spell forward
  LT / L2          Area cast
  RB / R1          Self cast / staff ability
  RT / R2          Weapon imbue / melee attack
  Hold LB / L1     Second element group
  R3               Clear queued elements (offline games only)
  Tap LB / L1      Interact / advance dialog / skip cutscene, if no element
                   or D-pad input was used

  A / Cross        Fire       LB / L1 + A / Cross        Cold
  B / Circle       Earth      LB / L1 + B / Circle       Shield
  X / Square       Lightning  LB / L1 + X / Square       Water
  Y / Triangle     Arcane     LB / L1 + Y / Triangle     Life

The existing element display at the bottom left changes to a wide four-pair
controller layout while XInput is active. X/Y/B/A are bright normally and the
LB variants are dimmed. A dim modifier element shows a compact, smaller "LB";
while LB is held, it shows only the additional X/Y/B/A button and the emphasis
is reversed. Mouse/keyboard mode keeps the original two-row layout and the
configured keyboard labels. No second element display is drawn above the
character.

When exactly one local player is present, fresh XInput activity takes control
from mouse and keyboard, and fresh mouse or keyboard activity takes it back.
Network players do not disable this handoff. Two or more local players keep the
input devices assigned in character selection so local co-op remains intact.

Xbox controllers use XInput directly. PlayStation controllers need Steam Input
or DS4Windows XInput mapping. Native DirectInput controllers keep Magicka's
original configurable controls.

R3 does nothing in multiplayer because Magicka 1 cannot safely remove an
already-replicated element queue from every peer.

To use Magicka's original controller scheme instead, set:

  use_magicka_1_controller_scheme=true

Restart Magicka after changing this value.

To disable telemetry and online update checks
---------------------------------------------

Copy patch-settings.ini to the CommunityPatch directory as described above and
set these values:

  usage_sharing=false
  crash_reports=false
  check_for_updates=false
  auto_update=false

usage_sharing=false disables patch telemetry. crash_reports=false also keeps
crash reports disabled if telemetry is enabled again later.

check_for_updates=false disables the GitHub release check. auto_update=false
keeps automatic downloading and installation disabled. The files-only package
does not include the Windows installer or auto-updater tools.
