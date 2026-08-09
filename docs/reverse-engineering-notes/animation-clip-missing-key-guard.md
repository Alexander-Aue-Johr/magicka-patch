# Missing animation clip compatibility guard

This note documents the compatibility behavior used when a character or
animated-physics template names an animation clip that is absent from its
associated skinned model. It describes the patch sites without reproducing the
original game classes.

## Failure mode

Patch sites:

- `Magicka.GameLogic.Entities.AnimationClipAction..ctor`
- `Magicka.GameLogic.Entities.CharacterTemplate.Read`
- `Magicka.GameLogic.Entities.PhysicsEntityTemplate.Read`

The template's binary content contains an animation enum and a clip-name key.
The original constructor indexed the model's animation dictionary directly. A
mod or mismatched asset can name a key that is not present, causing a
`KeyNotFoundException` on the loader thread and aborting the entire content-load
operation.

The constructor now uses the dictionary's existing `TryGetValue` path. A miss
sets the action's clip to `null`, reports the diagnostic event described below,
and still reads the action count and every remaining action from the binary
stream. Reading the complete record is essential: returning early would leave
the `ContentReader` at the wrong position and corrupt every following field.

Both template readers construct the complete action but assign it into their
animation array only when its `Clip` is non-null. A missing clip therefore has
the same representation as an animation that the template never declared: an
empty array slot.

## Runtime fallback

Additional patch sites:

- `Magicka.GameLogic.Entities.Character.Initialize`
- `Magicka.GameLogic.Entities.Character.GoToAnimation`
- `Magicka.GameLogic.Entities.Character.ForceAnimation`
- `Magicka.GameLogic.Entities.AnimatedPhysicsEntity.GoToAnimation`
- `Magicka.GameLogic.Entities.AnimatedPhysicsEntity.ForceAnimation`

These consumers already fall back to the default `idle` animation when a
requested slot is empty. Their original fallback directly dereferenced slot
`[Default][idle]`, however, so a mismatched asset that also lacks `idle` could
turn the recovered loader error into a later `NullReferenceException`.

The fallback lookup now validates the outer array, inner array, and both
indices. If `idle` exists, existing playback behavior continues. If it does
not, the request returns without changing the animation controller. This keeps
the entity usable without inventing an animation or modifying the source XNB
asset. During `Character.Initialize`, where returning would leave a pooled
entity only partly reset, a missing `idle` instead installs an empty animation
action array and continues the remaining initialization.

## Diagnostic telemetry and bounds

Newly authored helper source is available at
`docs/injected-source/Magicka.CommunityPatch/AnimationClipCompatibility.cs`.
`PatchTelemetry.SendStartup` initializes its process-session collection when
Magicka starts.

On a dictionary miss, the patch emits
`magicka_patch_animation_clip_missing` with:

- internal asset name;
- missing clip key;
- animation enum name and numeric value;
- number of clips present in the model;
- the unique event index and configured event limit for that process.

A locked `HashSet<string>` records the exact `(asset name, clip key, animation
name, animation value)` combinations already observed. Repeated combinations
are suppressed, even if several templates or animation requests encounter the
same bad data. A hard limit of 16 unique events per Magicka process prevents a
large broken content pack from producing unbounded telemetry. The set is never
persisted and is recreated on the next launch. `usage_sharing=false` continues
to disable the actual send operation.

## Why this fixes the crash

The only throwing dictionary access is replaced by a non-throwing lookup, the
content stream remains aligned, and invalid actions are not exposed as usable
array entries. The runtime checks cover the remaining assumption that an
`idle` fallback must always exist. Valid assets follow the same successful
lookup and playback paths as before.
