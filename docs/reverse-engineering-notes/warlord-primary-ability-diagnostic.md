# Warlord primary-ability diagnostic

Patch site:
`Magicka.GameLogic.Entities.Bosses.WarlordCharacter.ApplyTemplate`.

The original method assumes that element zero of the ability array supplied by
the Warlord character template is a `Melee`. The `as Melee` result is passed
directly to the `Bash` clone constructor. A null element or another ability
subtype therefore reaches `Ability(Ability)` as a null clone source and causes
a `NullReferenceException` while the Warlord is spawned.

Community Patch 0.0.43 and the current executable retain the original IL for
`Ability`, `Melee`, `WarlordCharacter`,
`NonPlayerCharacter.ApplyTemplate`, and the character-template ability loader.
The unchecked assumption is original Magicka behavior. Patch cache teardown
nulls the template's ability-array field but does not replace elements of an
array already assigned to an NPC. Cache-miss recovery reloads the asset name
recorded for the same template hash. Neither patch change produces the
observed non-null array with an invalid first element.

The patch now calls `WarlordAbilityDiagnostic.Inspect` after the base template
application and before the original cast. A valid `Melee` returns without
sending anything. An invalid value emits
`magicka_patch_warlord_ability_diagnostic` with reason
`warlord_primary_ability_not_melee`, then leaves the original method and its
failure behavior unchanged.

The event maps the failing runtime state as follows:

| Event field | Value |
| --- | --- |
| `guard` | `warlord_primary_ability_not_melee` |
| `collection` | `NonPlayerCharacter.Abilities` |
| `object_type` | Runtime type of element zero, or `null` |
| `asset_name` | `CharacterTemplate.Name`, when available |
| `details` | Template null/disposed state and ID; array null state and length; element null state; whether the NPC and template expose the same array |
| `skipped_count` | Matching occurrences suppressed by the existing exponential backoff |

`CharacterTemplate.CommunityPatchIsDisposed` is a new internal read-only helper
that exposes the existing private disposal flag to this diagnostic. It does not
change template lifetime.

The diagnostic catches all exceptions and cannot replace the original Warlord
failure with a telemetry failure. No recovery guard is included until runtime
data distinguishes invalid content, the wrong cached template, and later array
mutation.
