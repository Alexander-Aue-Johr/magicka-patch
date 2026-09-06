# Telemetry and Privacy

The community patch uses lightweight telemetry to understand whether the patch is
working in real multiplayer sessions and to identify rare crashes that cannot be
reliably reproduced locally.

Telemetry is for maintenance of the patch. It is not used for gameplay scoring,
advertising, or player profiling.

## Events

| Event | When it is sent |
| --- | --- |
| `magicka_patch_installed` | The installer successfully installs the patch. |
| `magicka_patch_auto_update` | The auto-updater successfully applies a prepared update. |
| `magicka_patch_start` | The patched game starts. When telemetry is enabled, its background telemetry worker also audits locally available original-file backups. |
| `magicka_patch_game_closed_normally` | The patched game exits through the normal shutdown path and reports aggregate input-selection totals and cached runtime context for that process session. |
| `magicka_patch_crash_report_written` | The patch writes a crash report and reports aggregate input-selection totals and cached runtime context collected before the crash. |
| `magicka_patch_network_guard_drop` | A guard ignored an unsafe network action that could otherwise crash the game. |
| `magicka_patch_network_guard_exception` | A guarded network path caught a null-reference exception and reported a summary. |
| `magicka_patch_typing_text_guard_exception` | The typing text guard caught an out-of-range text reveal state and skipped to the end of the text. |
| `magicka_patch_khan_killplane_fallback` | Kahn fell through the battlefield kill plane and the patch started his existing defeat dialog trigger so the cutscene could continue. |
| `magicka_patch_content_unload_guard` | Content cleanup encountered an asset object that was already null and safely skipped the invalid dictionary removal. |
| `magicka_patch_interactable_guard` | Keyboard/mouse interaction scanning encountered missing play-state, level, scene, or trigger state during teardown. |
| `magicka_patch_grow_owner_guard` | An active Grow effect no longer had an owner and was safely expired. |
| `magicka_patch_spray_cache_guard` | A spray spell was requested while its object cache was null or empty, so a replacement was allocated. |
| `magicka_patch_animation_clip_missing` | A character or animated-physics asset referred to an animation clip key that its skinned model does not contain. The invalid slot was left empty so loading could continue. |

## Data Sent

Common fields include the patch version, game version, operating system, event
name, and a generated telemetry identifier.

Startup events include an original-backup audit for the Steam files targeted by
the patch. The audit checks the installer manifest, files below
`CommunityPatch\backup`, and plausible manually named backup files next to the
game executable. It compares file size and the known SHA-256 hash in the
background telemetry worker. The startup and render threads do not wait for the
audit. The event reports only the audit schema, Steam build catalog, the status
of each required file (`verified`, `unverified`, `missing`, or `audit_failed`),
and a combined status. Local paths, file names, file contents, and hashes are
not submitted. When telemetry is disabled, the audit is not run.

Network guard events may also include the guarded subsystem, packet type, reason
code, short diagnostic details, and hashes used to group similar failures.
Some multiplayer guard events can include the remote sender's Steam ID and Steam
persona name when the game exposes them to the patched code.
Central entity-lifecycle events do not receive sender identity. Their details
are limited to numeric entity handles and template hashes, play-state
initialization and WorldSync flags, entity type, and active/disposed state.
Network guard reasons use the existing per-reason exponential backoff.

Typing text guard events include text length, a text hash, reveal counters, and
exception metadata. They do not include the full text.

Runtime guard events include a guard reason, the affected collection, the
general object type, and a short technical description. The content-unload
event includes the internal game asset name only when that name is non-empty.
Repeated events of the same reason are rate-limited with exponential backoff.

Missing-animation events include the internal asset name, missing clip key,
animation enum name and numeric value, the number of clips available in the
model, the event's unique index for the current process session, and the
per-session event limit. At game startup the patch initializes a process-local,
thread-safe set of already reported `(asset name, clip key, animation name,
animation value)` combinations. An exact combination is reported at most once,
and no more than 16 unique missing-animation events are sent during one game
process. The set is held only in memory and is discarded when Magicka exits.
The next game start begins with an empty set.

Crash events may include exception type, exception hash, thread name, and the
crash report text written by the patch.

Normal-shutdown and crash events also include three aggregate process-session
fields:

| Field | Meaning |
| --- | --- |
| `keyboard_element_selection_count` | Number of element selections made through keyboard or mouse bindings. |
| `controller_element_selection_count` | Number of element selections made through the Magicka 2 XInput scheme or the legacy XInput/DirectInput scheme. |
| `controller_element_selection_ratio` | Controller selections divided by all keyboard/mouse and controller selections. `1` means all controller, `0` means all keyboard/mouse; it is `0` if no element was selected. |

These three values are sent as JSON numbers. They do not identify which
elements were selected, their order, an individual local player, or a remote
network player. They remain only in process memory when telemetry is disabled
and are discarded when the process exits.

Normal-shutdown and crash events also include cached context that helps
distinguish first-load failures from failures after several games, and vanilla
font memory pressure from modified language payloads:

| Field | Meaning |
| --- | --- |
| `navigation_history` | A bounded sequence of level-relative file names, level names, entered scene names, and returns to `Menu`. Installation-directory prefixes are removed. |
| `playstate_count` | Number of play states initialized during the process session. |
| `scene_transition_count` | Number of scenes published during the process session. |
| `navigation_history_truncated` | Whether the oldest text was discarded after the navigation value reached its 4096-character bound. |
| `language` | Selected game language. |
| `glyph_font_source` | Selected language folder, or `eng` when Magicka uses its English font fallback. |
| `glyph_file_count` | Number of `.xnb` glyph/font files in the effective language font folder. |
| `glyph_total_bytes` | Combined byte size of those files. |
| `glyph_sha256` | Deterministic SHA-256 of a manifest containing each sorted file name, size, and streamed file hash. |
| `glyph_fingerprint_status` | `ok`, `missing`, `error`, or `not_recorded`. |
| `resolution_width`, `resolution_height` | Most recently selected render resolution. |
| `ui_scale_percent` | Most recently selected Community Patch in-game UI scale. |

The language fingerprint is calculated and cached when the language changes.
Resolution and UI scale are cached by their setters, and navigation text is
updated at level and scene transitions. Crash reporting does not enumerate or
open files, calculate hashes, or inspect live game objects. It only copies the
cached strings into the report event. File contents, complete installation
paths, and language-file paths are not sent.

## Storage and Endpoint

Telemetry is submitted to PostHog:

```text
https://eu.i.posthog.com/capture/
```

The generated telemetry identifier is stored locally at this path when `%APPDATA%` is writable:

```text
%APPDATA%\MagickaPatch\telemetry_id.txt
```

If the identifier cannot be stored, the patch uses an ephemeral ID for that run.

## Opt Out

Telemetry is enabled when `patch-settings.ini` is missing. To opt out, create or
edit this file:

```text
<Magicka>\CommunityPatch\patch-settings.ini
```

Set these values:

```ini
usage_sharing=false
crash_reports=false
```

`usage_sharing=false` disables patch telemetry. `crash_reports=false` also keeps
crash reports disabled if usage sharing is enabled again later.

## Privacy Direction

For future releases, the safest default is data minimization: collect the reason
code and enough state to debug the crash, but avoid raw player identifiers when a
hash or omission would answer the same engineering question.
