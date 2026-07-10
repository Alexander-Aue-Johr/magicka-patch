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
| `magicka_patch_start` | The patched game starts. |
| `magicka_patch_game_closed_normally` | The patched game exits through the normal shutdown path. |
| `magicka_patch_crash_report_written` | The patch writes a crash report. |
| `magicka_patch_network_guard_drop` | A guard ignored an unsafe network action that could otherwise crash the game. |
| `magicka_patch_network_guard_exception` | A guarded network path caught a null-reference exception and reported a summary. |
| `magicka_patch_typing_text_guard_exception` | The typing text guard caught an out-of-range text reveal state and skipped to the end of the text. |

## Data Sent

Common fields include the patch version, game version, operating system, event
name, and a generated telemetry identifier.

Network guard events may also include the guarded subsystem, packet type, reason
code, short diagnostic details, and hashes used to group similar failures.
Some multiplayer guard events can include the remote sender's Steam ID and Steam
persona name when the game exposes them to the patched code.

Typing text guard events include text length, a text hash, reveal counters, and
exception metadata. They do not include the full text.

Crash events may include exception type, exception hash, thread name, and the
crash report text written by the patch.

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

Create this file in the process working directory. For a normal game launch, that
is usually next to `Magicka.exe`:

```text
telemetry_disabled.txt
```

When that file exists, telemetry sending returns immediately.

## Privacy Direction

For future releases, the safest default is data minimization: collect the reason
code and enough state to debug the crash, but avoid raw player identifiers when a
hash or omission would answer the same engineering question.
