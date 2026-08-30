# Codex repository instructions

These instructions apply to the entire repository. When the user asks Codex to
create a release, perform the complete release workflow below unless the user
explicitly narrows the scope.

## Writing style

Use clear adult technical language in documentation, issues, release notes,
comments, and explanations.

- Write as a maintainer who knows the project. Do not sound like an investigator
  reporting on somebody else's work.
- State the result directly. Describe how it was discovered only when that
  information is needed to reproduce or verify the result.
- Prefer a release version over a commit hash. Mention a commit only when the
  commit itself is useful for reviewing, linking, or bisecting a change.
- Avoid forensic or evidentiary phrases such as "Git history places",
  "demonstrably", "the evidence confirms", "independently demonstrated", and
  "the investigation revealed".
- Do not repeat a distinction that is already clear. For example, "added in
  Community Patch 0.0.42" already says that the change is not part of the
  original game.
- Explain one important causal step per sentence. Use concrete subjects and
  verbs, especially when describing failures and fixes.
- Let technical accuracy create the professional tone. Do not make a sentence
  sound formal merely to signal expertise.
- Keep the reader at eye level. Avoid both unnecessary explanation of obvious
  conclusions and patronizingly simplified language.
- Prefer concise causal descriptions such as: "The guard prevents a crash, but
  also skips projectile cleanup."

## Issue-driven patch workflow

Use one GitHub issue for one independently reviewable problem. The issue must
contain everything needed to understand the problem; do not refer to a local
report, decompiler directory, CSV, or proposed diff.

1. Compare the current Community Patch with the original Magicka assembly.
   State whether the faulty behavior belongs to Magicka, to the patch, or to an
   interaction between both. Give the first affected patch version when it is
   useful to players or maintainers.
2. Describe the failing state and its consequence in direct language. Include
   exact telemetry reason codes and representative examples when available.
3. Propose the smallest fix that preserves packet compatibility and existing
   game behavior outside the failing path.
4. If the current code and telemetry are sufficient, implement the fix and add
   narrowly scoped validation telemetry when runtime confirmation is still
   useful. If the cause is uncertain, add diagnostic telemetry only.
5. Make one commit per issue and include the issue number in the subject. Do not
   combine unrelated fixes merely because they share a class or executable.
6. When new telemetry is supplied later, compare the old and new reason codes,
   recovered actions, remaining drops, affected versions, and time span. Either
   implement the fix or refine the telemetry. Repeat until the issue has a
   validated fix.
7. Update the issue with the implemented behavior, telemetry reason codes, and
   validation still required. Keep uncertain issues open until the runtime data
   supports closing them.

Create and update issues through an authenticated GitHub CLI or the GitHub REST
API. Read the final issue body or comment back from GitHub after each write. Do
not publish an issue that contains local paths, credentials, draft commentary,
or claims that are not supported by the code or supplied telemetry.

### Telemetry design

- Collect only fields that distinguish competing explanations or verify the
  fix. Prefer stable types, enum values, state flags, and bounded categories.
- Do not use raw handles, object IDs, timestamps, free text, player names, or
  Steam IDs as rate-limit keys. They create unbounded cardinality and may expose
  identifiers. They may appear in short diagnostic details only when necessary.
- Send the first event immediately. Rate-limit repeats per reason and bounded
  similarity key with exponential backoff. The next emitted event must include
  `skipped_count`, the number suppressed since the previous event in that
  category.
- A high-frequency path needs a per-session cap or timed backoff before it can
  ship. Telemetry must never block gameplay, throw into game code, or turn one
  remote packet into one network request indefinitely.
- Use a recovery event when the patch safely completes an action that used to
  be dropped. Use a drop event when the action is still rejected. Use a
  diagnostic event when behavior is unchanged and more context is needed.
- Keep event names and reason codes stable across releases so a later export can
  compare versions. Document new fields and reason codes in
  `docs/reverse-engineering-notes/network-guard-telemetry.md`.

### Managed assembly changes

- Before committing, compare the working-tree assembly with the previous
  committed version and check every changed method body for recompilation or
  decompiler noise. Remove incidental local-variable renames, control-flow
  reshaping, cast changes, and unrelated method rewrites so the final diff shows
  only the intended semantic changes.
- Back up the input executable before patching it. Work under the ignored
  `tmp/` directory and keep generated decompiler output, probes, replacement
  assemblies, and intermediate executables there.
- Keep readable C# equivalents of injected helpers under
  `docs/injected-source/`. When original Magicka methods change at IL level,
  record a focused C# diff for review when requested.
- Compare the finished assembly with the exact pre-change assembly. Match
  existing definitions by stable type, field, and method signature, then inspect
  their metadata and bodies. Added members can renumber later metadata tokens,
  so token-number changes alone are not behavioral changes. Confirm that only
  the intended existing methods changed and only the intended helper methods or
  fields were added. Reject accidental assembly references, CLR 4 references,
  decompiler noise, and unrelated metadata edits.
- Verify the PE/CLI metadata and inspect the final IL around every patched
  branch. A successful decompilation is useful, but it does not replace the
  token comparison and focused IL review.

## Mandatory user approval gate

- A request to create a release authorizes preparation only. It is not the final
  approval to publish.
- Complete the version updates, build, tests, ZIP inspection, hashes, and release
  diff first. Then show the user a concise summary of all changes and artifacts
  and explicitly ask for approval to proceed.
- Stop and wait for an explicit user OK given after that review. Before this OK,
  do not create the release commit, create or move a tag, push to `main`, create
  a GitHub release, or upload release assets. An ephemeral
  `actions/windows-release-*` branch may be pushed before approval only to build
  the requested Windows artifacts. The temporary build-snapshot commit must stay
  off `main` and is not the release commit. Delete that branch and its workflow
  artifact after downloading the ZIPs.
- If any release-related file or artifact changes after approval, show the
  updated result and obtain a fresh explicit OK before publishing.

## Release prerequisites

- Work from `main` and pull with rebase so no merge commit is created.
- Confirm the working tree and preserve user changes. Decide explicitly whether
  the repository or the detected Steam directory contains the authoritative
  patched `Magicka.exe`. Never let the build script replace a newly patched
  repository executable with an older game-directory copy. Use
  `-SkipSteamPayloadSync` when the repository copy is authoritative.
- Require an authenticated GitHub CLI or GitHub API client before publishing a
  GitHub release.
- Confirm that the requested version and tag do not already exist locally or on GitHub.
- Update the root `CHANGELOG.md` with the target version and its user-visible
  changes before presenting the release for approval. Include it in the same
  release commit.

## Version preparation

Use `scripts/build-release.ps1 -Version <version>` on Windows to update the
tracked version references. When that script cannot run on the current host,
apply the same replacements listed in `Set-ProjectVersion` and patch the one
UTF-16 version string in `Magicka.exe`. When the repository payload is
authoritative, use `-SkipSteamPayloadSync`. Do not use `-SkipExeVersionPatch`
for this preparation; the shipped executable and documented source must receive
the target version. Add the new section to `CHANGELOG.md` and inspect every
changed version reference before starting platform builds.

## Windows build and validation

1. Run the repository build script from the repository root:

   `powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Version <version> -SkipSteamPayloadSync`

   This form keeps the repository payload, updates the executable, documented
   patch source, and Flutter project versions, builds the Windows UI, and
   creates both `release\magicka-community-patch-<version>-installer.zip` and
   `release\magicka-community-patch-<version>-files-only.zip`. Omit
   `-SkipSteamPayloadSync` only when the detected Steam directory was explicitly
   chosen as the authoritative patched payload.

2. Verify every tracked product-version reference. At minimum check:

   - `Magicka.exe`
   - `CHANGELOG.md`
   - root `README.md` Windows installer, Linux installer when present, and
     files-only download links
   - `magicka-patch-installer-ui/pubspec.yaml`
   - `magicka-patch-installer-ui/lib/main.dart`
   - `magicka-patch-installer-ui/lib/localization.dart`
   - `magicka-patch-installer-ui/README.md`
   - `magicka-patch-installer-ui/test/widget_test.dart`
   - `magicka-patch-installer-ui/src/magicka-community-patch-auto-updater-ui/pubspec.yaml`
   - its `pubspec.lock` and README
   - `docs/injected-source/Magicka.CommunityPatch/CommunityPatchInfo.cs`

3. Run `flutter analyze --no-pub` and `flutter test --no-pub` in
   `magicka-patch-installer-ui` with the same Flutter installation used by the
   build script.
4. Run the pinned Mono startup compatibility gate. The release build script
   compiles a CLR-2 reflection probe and invokes
   `Magicka.CommunityPatch.PatchTelemetry.SendStartup` through Mono 6.12.0.206.
   It must stop when Mono cannot resolve or execute the method. Pass
   `-Mono <path-to-mono.exe>` when Mono is not on `PATH`.
5. Inspect both ZIPs. Confirm the main ZIP contains the patched `Magicka.exe`,
   `PolygonHead.dll`, `Magicka.GcDiagnostics.dll`, `gc-diagnostics/`, installer,
   updater, Flutter runtime/data, package README, and expected payload. Confirm
   the files-only ZIP contains those three managed payload files,
   `gc-diagnostics/`, `patch-settings.ini`, and `README.txt`. Record both sizes
   and SHA-256 hashes.
6. Review `git diff` and stage only release-related files. Do not commit ignored build output or the ignored `release` directory.
7. Present the reviewed diff, test result, both ZIP names, sizes, and SHA-256
   hashes to the user and wait at the mandatory approval gate above.

### Windows build through GitHub Actions

Use `.github/workflows/prepare-windows-release.yml` when Windows is not
available locally. The workflow installs the pinned Flutter SDK, builds both
ZIPs, runs analyze and tests, rejects generated source changes, and retains one
artifact for one day.

1. Finish and review the versioned source changes locally.
2. Push the current revision to a unique branch named
   `actions/windows-release-<version>-<suffix>`. Do not push it to `main`.
3. Wait for `Prepare Windows release artifacts` to succeed. Read the job logs
   if it fails; do not accept a merely completed workflow with a failed job.
4. Download and extract the artifact. Verify the two ZIP names, contents,
   sizes, and SHA-256 hashes locally.
5. Delete the workflow artifact through the GitHub API, then delete the remote
   ephemeral branch. Confirm both are gone. Do not delete the workflow run,
   because its logs are useful build evidence and do not contain release files.

Do not place a token in a command, file, URL, log, or issue. Use the configured
Git credential helper or an authenticated GitHub CLI/API client.

## Linux installer build

The PowerShell packaging script builds a Windows Flutter runner. Build the Linux
runner separately from `magicka-patch-installer-ui` with the same pinned Flutter
version used for release validation:

1. Run `flutter pub get`, `flutter analyze --no-pub`, `flutter test --no-pub`,
   and `flutter build linux --release`.
2. Package the complete `build/linux/x64/release/bundle` directory. Do not copy
   only the executable; the `lib` and `data` directories are required.
3. Add the repository `Magicka.exe`, `PolygonHead.dll`, and package README only
   if the Linux package is intended to install the patch directly. Keep the
   package layout consistent with the Windows installer where practical.
4. Store generated archives under ignored `release/`, record size and SHA-256,
   and inspect the archive before presenting it for approval.

## Publish the versioned release

1. Commit the release preparation with a message such as `Release <version>: <short description>`.
2. Create an annotated tag named exactly `<version>` with message `Release <version>`.
3. Push `main`, then push the tag. Do not force-push and do not create merge commits.
4. Create a non-draft, non-prerelease GitHub release from that tag. Use concise release notes describing the user-visible fix and installer/package changes.
5. Upload `release\magicka-community-patch-<version>-installer.zip` and
   `release\magicka-community-patch-<version>-files-only.zip`. When a Linux
   installer was prepared for the release, also upload
   `release/magicka-community-patch-<version>-linux-installer.zip`. Verify all
   uploaded asset names and sizes on GitHub.

## Update the latest-download links

Before presenting the release diff for approval, update the root `README.md` to
show the Windows installer and files-only links. Add the Linux installer link
when that package is part of the release:

`https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/<version>/magicka-community-patch-<version>-installer.zip`

`https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/<version>/magicka-community-patch-<version>-files-only.zip`

`https://github.com/Alexander-Aue-Johr/magicka-patch/releases/download/<version>/magicka-community-patch-<version>-linux-installer.zip`

Include these README changes in the same versioned release commit. Do not create
a separate post-release README commit. Once the approved release commit, tag,
and assets are pushed successfully, all listed links will resolve.

## Final checks and report

- Verify `main` matches `origin/main`, the tag resolves to the intended release commit, and the working tree is clean.
- Verify the GitHub release URL and that every prepared ZIP asset is downloadable.
- Report the release commit, tag, release URL, all asset names, sizes and SHA-256
  hashes, and test result.
