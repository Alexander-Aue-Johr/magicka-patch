# Codex release instructions

These instructions apply to the entire repository. When the user asks Codex to create a release, perform the complete workflow below unless the user explicitly narrows the scope.

## Mandatory user approval gate

- A request to create a release authorizes preparation only. It is not the final
  approval to publish.
- Complete the version updates, build, tests, ZIP inspection, hashes, and release
  diff first. Then show the user a concise summary of all changes and artifacts
  and explicitly ask for approval to proceed.
- Stop and wait for an explicit user OK given after that review. Before this OK,
  do not create the release commit, create or move a tag, push release changes,
  create a GitHub release, or upload release assets.
- If any release-related file or artifact changes after approval, show the
  updated result and obtain a fresh explicit OK before publishing.

## Release prerequisites

- Work from `main` and pull with rebase so no merge commit is created.
- Confirm the working tree and preserve user changes. The authoritative patched `Magicka.exe` is stored in the detected Steam Magicka directory, not necessarily in the repository before the build.
- Require GitHub CLI (`gh`) to be installed and authenticated before publishing a GitHub release.
- Confirm that the requested version and tag do not already exist locally or on GitHub.
- Update the root `CHANGELOG.md` with the target version and its user-visible
  changes before presenting the release for approval. Include it in the same
  release commit.

## Build and validation

1. Run the repository build script from the repository root:

   `powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Version <version>`

   The script detects the Steam Magicka directory, copies its modified `Magicka.exe` and `PolygonHead.dll`, updates the executable, documented patch source, and Flutter project versions, copies the versioned `Magicka.exe` back to the Steam directory, builds the Windows UI, and creates both `release\magicka-community-patch-<version>-installer.zip` and `release\magicka-community-patch-<version>-files-only.zip`. This round trip keeps the executable used for continued game-patch development on the same version as the repository and release.

2. Verify every tracked product-version reference. At minimum check:

   - `Magicka.exe`
   - `CHANGELOG.md`
   - root `README.md` installer and files-only download links
   - `magicka-patch-installer-ui/pubspec.yaml`
   - `magicka-patch-installer-ui/lib/main.dart`
   - `magicka-patch-installer-ui/lib/localization.dart`
   - `magicka-patch-installer-ui/README.md`
   - `magicka-patch-installer-ui/test/widget_test.dart`
   - `magicka-patch-installer-ui/src/magicka-community-patch-auto-updater-ui/pubspec.yaml`
   - its `pubspec.lock` and README
   - `docs/injected-source/Magicka.CommunityPatch/CommunityPatchInfo.cs`

3. Run `flutter test` in `magicka-patch-installer-ui` with the same Flutter installation used by the build script.
4. Inspect both ZIPs. Confirm the main ZIP contains the patched `Magicka.exe`, installer, updater, Flutter runtime/data, package README, and expected payload. Confirm the files-only ZIP contains exactly `Magicka.exe`, `PolygonHead.dll`, `patch-settings.ini`, and `README.txt`. Record both sizes and SHA-256 hashes.
5. Review `git diff` and stage only release-related files. Do not commit ignored build output or the ignored `release` directory.
6. Present the reviewed diff, test result, both ZIP names, sizes, and SHA-256
   hashes to the user and wait at the mandatory approval gate above.

## Publish the versioned release

1. Commit the release preparation with a message such as `Release <version>: <short description>`.
2. Create an annotated tag named exactly `<version>` with message `Release <version>`.
3. Push `main`, then push the tag. Do not force-push and do not create merge commits.
4. Create a non-draft, non-prerelease GitHub release from that tag. Use concise release notes describing the user-visible fix and installer/package changes.
5. Upload exactly `release\magicka-community-patch-<version>-installer.zip` and `release\magicka-community-patch-<version>-files-only.zip` as the release assets and verify both asset names and sizes on GitHub.

## Update the latest-download links

Before presenting the release diff for approval, update the root `README.md` to
show both latest-release links:

`https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/<version>/magicka-community-patch-<version>-installer.zip`

`https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/<version>/magicka-community-patch-<version>-files-only.zip`

Include these README changes in the same versioned release commit. Do not create
a separate post-release README commit. Once the approved release commit, tag,
and assets are pushed successfully, both links will resolve.

## Final checks and report

- Verify `main` matches `origin/main`, the tag resolves to the intended release commit, and the working tree is clean.
- Verify the GitHub release URL and that both ZIP assets are downloadable.
- Report the release commit, tag, release URL, both asset names, sizes and SHA-256 hashes, and test result.
