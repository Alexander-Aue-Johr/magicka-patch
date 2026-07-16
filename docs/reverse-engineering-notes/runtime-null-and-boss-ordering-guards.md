# Runtime null and boss ordering guards

This note documents narrowly scoped fixes for several asynchronous lifetime and
network-ordering failures. It describes patch sites and behavior without
reproducing the original game methods.

## Avatar cache lookup

Patch sites:

- `Magicka.GameLogic.Entities.Avatar.GetFromCache(Player, ushort)`
- `Magicka.Levels.Versus.VersusRuleset.RevivePlayer`

The handle overload can resolve no entity, or an entity that is not an Avatar.
It now returns `null` before removing or assigning the cached Avatar. The versus
revive boundary treats that failed lookup as a failed revive and returns handle
zero instead of dereferencing it. `Entity.GetFromHandle` itself remains
unchanged.

## Player input locks

Patch site: the `Controller` overloads of
`Magicka.GameLogic.Controls.ControlManager.LockPlayerInput`,
`IsPlayerInputLocked`, and `UnlockPlayerInput`.

A controller can outlive its detached `Player` during dialogue and disconnect
teardown. The shared ControlManager boundary now ignores lock/unlock requests
when either object is missing; the query returns `false`. This central check
covers callers such as `Interact` without duplicating guards throughout the
dialogue call hierarchy.

## Gameplay-start telemetry

Patch site:
`Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect.Start`.

Level loading and the transition to gameplay have already been requested before
the trailing telemetry block runs. If the selected `LevelNode` or its name is
missing, only that telemetry block is skipped. Missing analytics data therefore
cannot prevent the game from starting.

## SpawnNPC WorldSync messages

Patch sites:

- `Magicka.GameLogic.GameStates.PlayState.AddWorldSyncMessage`
- `Magicka.CommunityPatch.NetworkEntityHandleGuard.IsUsableWorldSyncSpawnNpc`

Before a WorldSync message with `TriggerActionType.SpawnNPC` enters the play
state queue, its handle must resolve to a non-disposed
`NonPlayerCharacter` attached to that same `PlayState`. An invalid message is
dropped and reported with reason
`world_sync_spawn_npc_missing_or_unusable_entity`.

The check is intentionally limited to `SpawnNPC`. `TriggerActionMessage.Handle`
has different meanings for other action types, including a triggering entity,
a cached spawn object, or no entity at all, so a universal handle rule would be
incorrect.

## Network template-cache validation

Patch sites:

- `Magicka.Network.NetworkClient.ReadMessage`
- `Magicka.GameLogic.Entities.Character.ReApplyTemplate`
- `Magicka.GameLogic.Entities.Character.TryReApplyCachedTemplate`

Two incoming packet paths can certainly apply a `CharacterTemplate` obtained
from the static template cache. They now validate that dependency before they
change character or player state.

For `CharacterAction`, an already assigned template is left alone. If the
character has no template, `TryReApplyCachedTemplate` first checks the
character's stored template hash, performs the reapply only when that hash is
cached, and verifies that the character has a template afterwards. A cache miss
or failed reapply drops the packet before `Character.NetworkAction`. This also
prevents a network teleport action from reaching `TriggerArea.AddEntity` with a
template-less character. The public `ReApplyTemplate` method has an additional
last-line-of-defense null check and never passes a null template to
`ApplyTemplate`.

For `SpawnPlayer`, the receiving player's `Gamer` and avatar template are
resolved before entering the player lock. The exact non-null cached template is
kept in a local and later passed to `Avatar.Initialize`; it is not looked up a
second time. A missing gamer or cache entry therefore drops the packet before
the code clears the player's weapon and staff or mutates the cached Avatar.

The explicit `Content.Load<CharacterTemplate>` path used by `AvatarCheat` is
unchanged because it already supplies the loaded template object directly and
does not depend on `GetCachedTemplate` returning an entry.

Drops use the normal per-reason exponential telemetry backoff and the reason
codes `character_action_template_not_cached`,
`spawn_player_missing_gamer_for_template`, and
`spawn_player_template_not_cached`.

## Deferred client boss initialization

Patch sites:

- `Magicka.GameLogic.Entities.Bosses.BossFight`
- `Magicka.GameLogic.Entities.Bosses.Machine.NetworkInitialize`

On a network client, `BossFight.Initialize` now waits while
`IBoss.NetworkInitialized` is false. It stores the boss reference, area hash,
and unique ID, and it records an early `Start` request instead of exposing a
half-initialized boss to the normal update path.

Boss initialize packets that arrive too early remain queued. One queued packet
is retried on each BossFight update. Once the matching boss reports
`NetworkInitialized == true`, the stored parameters are passed to the original
initialization path and a pending `Start` is replayed. `Clear` and `Reset`
discard all pending state.

`Machine.NetworkInitialize` now reports readiness only when its network-provided
warlock handle actually resolved to a `NonPlayerCharacter`. If the entity is not
available yet, the packet and boss setup remain retryable instead of allowing
`Machine.Initialize` to dereference a null warlock.
