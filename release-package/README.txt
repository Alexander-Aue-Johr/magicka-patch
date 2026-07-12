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
