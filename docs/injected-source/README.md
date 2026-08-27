# Injected Managed Source Snapshots

This directory documents C# classes added to the patched managed assemblies,
including the small GC diagnostics runtime loaded by `Magicka.exe` and
`PolygonHead.dll`.

These files are provided for transparency and review. They are not a complete
buildable source tree for Magicka, and this repository does not contain the
original game source code.

Version-specific values such as `CommunityPatchInfo.Version` are expected to
change between releases and should match the version embedded in the shipped
patched executable for that release.

Do not place full decompiled original game classes in this directory. Changes to
existing Magicka classes are documented as patch-site notes under
`docs/reverse-engineering-notes/` instead.
