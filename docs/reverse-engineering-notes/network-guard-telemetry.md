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
| `Magicka.Network.NetworkClient` | Route all 21 `Entity.GetFromHandle` call sites through a caller-local usability guard and reject cache-dependent CharacterAction/SpawnPlayer template application when its template is unavailable. |
| `Magicka.Network.NetworkServer` | Route all 17 `Entity.GetFromHandle` call sites through the same caller-local guard, validate host-side spawn requests, and catch last-resort `NullReferenceException` failures during message handling. |
| `Magicka.GameLogic.GameStates.PlayState.AddWorldSyncMessage` | Drop `SpawnNPC` WorldSync messages before queueing when their handle is not a usable NPC in the receiving play state. |
| `Magicka.Rendering.TypingText.Update` | Catch an out-of-range text reveal state, report diagnostic counters, and reveal the rest of the text instead of crashing. |

The goal is not to hide bugs. The goal is to keep multiplayer sessions alive
long enough to learn which object is actually missing, disposed, or detached
from its `PlayState`.

`Entity.GetFromHandle` itself deliberately remains unchanged. A resolved entity
is rejected only inside `NetworkClient` and `NetworkServer` when it is `null`,
disposed, or detached from its `PlayState`. This preserves valid handles used by
non-network code and by multiplayer transitions. Damage targets additionally
require a non-null physics `Body`, covering the disposal window in which
`mBody` has already been cleared but `IsDisposed` is not yet true.

## NetworkClient Authentication Compile Workaround

The `PacketType.Connect` branch must not inline the token-buffer pointer passed
to `SteamUser.InitiateGameConnection`. dnSpy does not reliably recompile the
decompiled inline pointer expression when any part of `NetworkClient.ReadMessage`
is edited.

Keep the connect branch call at this patch site:

```csharp
NetworkClient.InitiateAuthentication(ref authenticateRequestMessage,
	connectReplyMessage.ServerID, p2PSessionState,
	connectReplyMessage.VACSecure);
```

Keep this helper on `Magicka.Network.NetworkClient`:

```csharp
private unsafe static void InitiateAuthentication(
	ref AuthenticateRequestMessage message,
	SteamID serverID,
	P2PSessionState sessionState,
	bool vacSecure)
{
	fixed (byte* ptr = &message.Token.Data.FixedElementField)
	{
		byte* tokenBuffer = ptr;
		message.Token.Length = SteamUser.InitiateGameConnection(
			(void*)tokenBuffer, 1024, serverID,
			sessionState.RemoteIP, sessionState.RemotePort, vacSecure);
	}
}
```

This is a compile-stability workaround around the original authentication call,
not a behavior change. Future edits to `NetworkClient.ReadMessage` must preserve
the helper call.

## Telemetry Events

Network guards use two event names:

| Event | Meaning |
| --- | --- |
| `magicka_patch_network_guard_drop` | A malformed or unsafe network action was ignored before it could crash the game. |
| `magicka_patch_network_guard_exception` | A guarded path still hit a `NullReferenceException`; the exception was caught and summarized. |
| `magicka_patch_network_diagnostic` | A valid but unusual network path was processed and recorded for later validation. |
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
| `similarity_key` | Bounded category used to rate-limit similar diagnostic events together. |
| `skipped_count` | Number of matching events suppressed since the previous event in the same category. |
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
| `missile_event_collision_target_unusable_missile_killed` | The collision target was unavailable, so target-dependent effects were skipped and the consumed missile was removed locally. |
| `character_action_missing_or_unusable_character` | A character action references a missing, disposed, or detached character. |
| `character_action_template_not_cached` | A character action needs to restore its character template, but the stored template hash is not cached or the reapply did not produce a template. |
| `spawn_player_missing_gamer_for_template` / `spawn_player_template_not_cached` | A SpawnPlayer packet cannot safely obtain its player's cached avatar template before initialization. |
| `entity_remove_missing_or_unusable_entity` / `character_die_missing_or_unusable_character` | A remove/death packet references an entity that can no longer be used safely. |
| `damage_missing_or_unusable_*` | A damage packet references an unusable attacker or a target with no usable entity/body. |
| `forced_player_status_sync_invalid_player_id_or_sender` | A forced status-sync request has no matching player/avatar or came from another player's client. |
| `forced_player_status_sync_player_resolved` / `forced_player_status_sync_response_built` | A valid forced-sync request resolved its player and produced a populated response. |
| `hotjoin_broadcast_continued` | A cacheable packet was queued for a syncing player and still sent to later live clients. |
| `summon_undead_state_sent` / `summon_undead_state_applied` | A necromancer-staff SpawnNPC packet carried the undead flag from the host and the client applied it. The template ID is the bounded similarity key. |
| `entity_handle_active_spawn_observed` | A spawn packet addressed an already active handle. The event compares the current and incoming type, template, and unique ID before the spawn guard decides whether reuse is valid. |
| `damage_missing_attacker_context` | A valid damage target was not updated because its attacker handle was stale. The diagnostic includes target type/state, elements, magnitude, damage features, and message age. |
| `trigger_action_sender_is_not_server` | A non-server peer sent one of the eight known host-authoritative spawn actions, so the client rejected it. |
| `trigger_action_non_server_sender_observed` | A non-server peer sent another TriggerAction type. The action still runs while telemetry builds the remaining authority inventory. |
| `world_sync_spawn_npc_missing_or_unusable_entity` | A SpawnNPC WorldSync message references no usable NPC in the receiving play state and is dropped before entering the delayed action queue. |
| `network_server_readmessage_*` | Last-resort server-side guard caught an otherwise unhandled null reference while reading a network packet. |
| `CharacterActionMessage.Grip` reasons | Missing target character, missing body, missing gripper/gripped character, missing animation controller, missing skeleton, or invalid grip joint index. |
| `text_index_out_of_range` | The typing text reveal cursor moved outside the backing string; the patch reports counters and completes the reveal. |

When a reason code becomes frequent, the next step is to reproduce that specific
state and replace the guard with a narrower root-cause fix where possible.

## GC retention telemetry

The `magicka_patch_gc_retention` event uses `analysis_mode=clrmd_root_paths`
when the external CLR 2 analyzer produced the result. Its bounded finding list
contains expectation, managed type, lifecycle, root category, and field-labelled
path. An occurrence suffix represents identical findings without repeating the
complete path.

The event includes these serialization counters:

- `finding_count`: raw findings before deduplication and selection;
- `finding_group_count`: distinct expectation/type/lifecycle groups;
- `serialized_finding_count`: complete finding rows included in `findings`;
- `omitted_finding_count`: raw findings represented only by rows that did not
  fit the row or text limit;
- `telemetry_truncated`: `true` only when finding rows were omitted for
  telemetry serialization.

`truncated` has a separate meaning: it indicates that heap traversal or root
analysis itself reached a bound. The in-game sender never cuts a finding in the
middle. If its final 3,500-character event-property limit is reached, it drops
the complete row and updates the serialization counters instead.

## Drop-event resend backoff

`magicka_patch_network_guard_drop` is rate-limited independently for every
stable reason code. The first event is sent immediately. Repeated events for
the same reason are then allowed after 1, 2, 4, 8, 16, 32, 64, 128, 256, and
at most 300 seconds. A quiet interval of at least 120 seconds resets that reason
to the initial one-second delay. A different reason has its own timer and is
therefore not suppressed by an unrelated noisy packet path.

Diagnostic events use the same backoff with a bounded similarity key. The next
event includes `skipped_count`, so the server receives the suppressed volume
without receiving every repeated event.

## Player Controls

Telemetry is sent to PostHog at `https://eu.i.posthog.com/capture/` with a
short timeout so the game does not wait on the network for long.

A persistent telemetry identifier is stored at this path when `%APPDATA%` is writable:

```text
%APPDATA%\MagickaPatch\telemetry_id.txt
```

Telemetry is enabled when the settings file is missing. Players can disable it
in:

```text
<Magicka>\CommunityPatch\patch-settings.ini
```

Set `usage_sharing=false` and `crash_reports=false`.

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
