# PlayState and Avatar Cleanup Guards

This note documents two cleanup fixes made inside existing Magicka classes. It
describes the patch sites and behavior without reproducing the complete
decompiled methods.

## `PlayState.OnExit`

The delayed exit task previously disposed avatars belonging to connected
network players before disposing `PlayState.sRecentPlayState`.

That explicit network-avatar loop was removed. `PlayState.Dispose()` already
iterates over every game player and disposes each non-null avatar. Disposing the
same network avatar first in `OnExit` and then again from `PlayState.Dispose()`
could dereference state that had already been cleared and throw a
`NullReferenceException`.

The delayed task now:

1. waits until `GameStateManager.CurrentState` is no longer a `PlayState`;
2. disposes `PlayState.sRecentPlayState` once and clears the static reference;
3. restores the appropriate local-player lobby state;
4. preloads character-selection textures and re-enables rendering.

The existing network-player state wait and its telemetry remain in place.

## `Avatar.Deinitialize`

Closing an avatar's inventory previously assumed that `mPlayState` was still
available during teardown. Cleanup ordering can clear the play-state reference
before avatar deinitialization runs.

The inventory close call is now guarded by both conditions:

```text
mPlayState != null && mPlayState.Inventory != null
```

This prevents a teardown-time `NullReferenceException` while retaining the
normal inventory cleanup whenever the owning play state still exists.
