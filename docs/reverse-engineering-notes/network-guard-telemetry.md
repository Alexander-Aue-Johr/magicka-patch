# Network Guard Telemetry

This note documents guard checks and telemetry added around multiplayer/network
paths that have historically produced difficult-to-diagnose
`NullReferenceException` crashes. It intentionally does not include the full
decompiled source of the modified original game classes.

## Scope

The guards currently cover these high-risk areas:

| Area | Patch intent |
| --- | --- |
| `Magicka.GameLogic.Entities.MissileEntity` | Drop invalid remote missile events instead of crashing when the missile, target, play state, hit lists, or condition collection are missing. |
| `Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell` | Guard missile creation when the owner, play state, condition cache, model, or cached missile instance is missing. |
| `Magicka.GameLogic.Entities.Character.NetworkAction` | Drop unsafe remote grip actions before they dereference missing actors, bodies, grippers, animation controllers, skeletons, or out-of-range grip joints. |
| `Magicka.Network.NetworkClient` | Validate incoming spawn/entity/missile packets before applying them to local game state. |
| `Magicka.Network.NetworkServer` | Validate host-side spawn requests and catch last-resort `NullReferenceException` failures during message handling. |
| `Magicka.Rendering.TypingText.Update` | Catch an out-of-range text reveal state, report diagnostic counters, and reveal the rest of the text instead of crashing. |

The goal is not to hide bugs. The goal is to keep multiplayer sessions alive
long enough to learn which object is actually missing, disposed, or detached
from its `PlayState`.

## Telemetry Events

Network guards use two event names:

| Event | Meaning |
| --- | --- |
| `magicka_patch_network_guard_drop` | A malformed or unsafe network action was ignored before it could crash the game. |
| `magicka_patch_network_guard_exception` | A guarded path still hit a `NullReferenceException`; the exception was caught and summarized. |
| `magicka_patch_typing_text_guard_exception` | The typing text guard caught an invalid reveal index and forced the text to a completed state. |

Crash-report telemetry may also emit `magicka_patch_crash_report_written` when a
crash report is generated.

## Common Fields

Network guard events include:

| Field | Meaning |
| --- | --- |
| `patch_name` | Community patch name. |
| `patch_version` | Value returned by `CommunityPatchInfo.Version`; should match the shipped `Magicka.exe`. |
| `game_version` | `Application.ProductVersion` reported by the game process. |
| `os` | Operating system string reported by .NET. |
| `side` | Guard origin, such as `client`, `server`, `missile_entity`, or `projectile_spell`. |
| `packet_type` | Network packet or subsystem being guarded. |
| `sender_steam_id` | Steam ID associated with the remote sender when available. |
| `sender_name` | Steam persona name associated with the remote sender when available. |
| `reason` | Stable reason code for grouping failures. |
| `details` | Short diagnostic context, truncated by the telemetry helper. |
| `details_hash` | Short hash of the diagnostic context for grouping similar failures. |
| `exception_type` | Exception type for guarded exception events. |
| `exception_message` | Exception message for guarded exception events. |
| `exception_hash` | Short hash of the full exception string. |

Privacy note: `sender_steam_id` and `sender_name` can identify another player.
For future releases, prefer hashing or removing these fields unless raw values
are truly needed to diagnose a live multiplayer failure.

## Representative Reason Codes

The following reason-code families are intentionally stable so reports can be
counted over time:

| Reason family | What it investigates |
| --- | --- |
| `spawn_missile_*` | Missing owner, missing item, missing target, empty condition cache, invalid model, missing cached missile instance, or a null reference during missile spawn handling. |
| `spawn_shield_*` | Spawned shield has no play state or lacks a usable owner/shield pair. |
| `spawn_barrier_*` | Barrier, owner, hit list, or play state is missing during network spawn handling. |
| `spawn_wave_*` | Wave entity, owner, hit list, or play state is missing during spawn handling. |
| `spawn_mine_*` | Mine entity or owner is missing, or the spawned mine has no play state. |
| `spawn_vortex_*` | Vortex instance, owner, or play state is missing. |
| `missile_event_*` | Remote missile event references a missing/unusable missile, target, collision target, condition collection, hit list, or damage target. |
| `network_server_readmessage_*` | Last-resort server-side guard caught an otherwise unhandled null reference while reading a network packet. |
| `CharacterActionMessage.Grip` reasons | Missing target character, missing body, missing gripper/gripped character, missing animation controller, missing skeleton, or invalid grip joint index. |
| `text_index_out_of_range` | The typing text reveal cursor moved outside the backing string; the patch reports counters and completes the reveal. |

When a reason code becomes frequent, the next step is to reproduce that specific
state and replace the guard with a narrower root-cause fix where possible.

## Player Controls

Telemetry is sent to PostHog at `https://eu.i.posthog.com/capture/` with a
short timeout so the game does not wait on the network for long.

A persistent telemetry identifier is stored at this path when `%APPDATA%` is writable:

```text
%APPDATA%\MagickaPatch\telemetry_id.txt
```

Players can disable telemetry by creating this file in the process working
directory. For a normal game launch, that is usually next to `Magicka.exe`:

```text
telemetry_disabled.txt
```

If the telemetry identifier cannot be stored, the patch uses an ephemeral ID for
that run. The patch should continue to run if telemetry sending fails.

## Documentation Rule

Do document:

- the modified class and method names,
- the guard condition in plain English,
- the telemetry event and reason code,
- the fields collected,
- whether the packet/action is ignored, retried, or converted into a safer state.

Do not document by pasting full decompiled methods or classes from the original
game.
