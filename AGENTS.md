# Codex release instructions

These instructions apply to the entire repository. When the user asks Codex to create a release, perform the complete workflow below unless the user explicitly narrows the scope.

## Release prerequisites

- Work from `main` and pull with rebase so no merge commit is created.
- Confirm the working tree and preserve user changes. The authoritative patched `Magicka.exe` is stored in the detected Steam Magicka directory, not necessarily in the repository before the build.
- Require GitHub CLI (`gh`) to be installed and authenticated before publishing a GitHub release.
- Confirm that the requested version and tag do not already exist locally or on GitHub.

## Build and validation

1. Run the repository build script from the repository root:

   `powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Version <version>`

   The script detects the Steam Magicka directory, copies its modified `Magicka.exe` and `PolygonHead.dll`, updates the executable and Flutter project versions, copies the versioned `Magicka.exe` back to the Steam directory, builds the Windows UI, and creates `release\magicka-community-patch-<version>.zip`. This round trip keeps the executable used for continued game-patch development on the same version as the repository and release.

2. Verify every tracked product-version reference. At minimum check:

   - `Magicka.exe`
   - `magicka-patch-installer-ui/pubspec.yaml`
   - `magicka-patch-installer-ui/lib/main.dart`
   - `magicka-patch-installer-ui/lib/localization.dart`
   - `magicka-patch-installer-ui/README.md`
   - `magicka-patch-installer-ui/test/widget_test.dart`
   - `magicka-patch-installer-ui/src/magicka-community-patch-auto-updater-ui/pubspec.yaml`
   - its `pubspec.lock` and README
   - `docs/injected-source/Magicka.CommunityPatch/CommunityPatchInfo.cs`

3. Run `flutter test` in `magicka-patch-installer-ui` with the same Flutter installation used by the build script.
4. Inspect the ZIP contents. Confirm it contains the patched `Magicka.exe`, installer, updater, Flutter runtime/data, and expected payload. Record its size and SHA-256.
5. Review `git diff` and stage only release-related files. Do not commit ignored build output or the ignored `release` directory.

## Publish the versioned release

1. Commit the release preparation with a message such as `Release <version>: <short description>`.
2. Create an annotated tag named exactly `<version>` with message `Release <version>`.
3. Push `main`, then push the tag. Do not force-push and do not create merge commits.
4. Create a non-draft, non-prerelease GitHub release from that tag. Use concise release notes describing the user-visible fix and installer/package changes.
5. Upload exactly `release\magicka-community-patch-<version>.zip` as the release asset and verify the asset name and size on GitHub.

## Update the latest-download link

After the GitHub release and asset exist, update the root `README.md` latest-release link to:

`https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/<version>/magicka-community-patch-<version>.zip`

Commit this README change separately with message `Update latest release link in README.md` and push `main`. The release tag must remain on the preceding release commit, matching the established repository history.

## Final checks and report

- Verify `main` matches `origin/main`, the tag resolves to the intended release commit, and the working tree is clean.
- Verify the GitHub release URL and that its ZIP asset is downloadable.
- Report the release commit, tag, release URL, asset name, size, SHA-256, test result, and the separate README commit.
