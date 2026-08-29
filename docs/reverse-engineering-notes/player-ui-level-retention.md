# Player UI level retention

`Player.InitializeGame(PlayState)` creates or reinitializes the player's
spell wheel, icon renderer, notifier button, and obtained-item text box.
`Player.DeinitializeGame()` is empty in both the original game and Community
Patch 0.0.51.

Players survive level transitions. `TextBox` stores strong `mScene` and
`mOwner` fields, so an obtained-item message can otherwise keep an entity and
the unloaded scene graph reachable after `PlayState.OnExit` calls the empty
player teardown.

Community Patch 0.0.52 makes `Player.DeinitializeGame()` call the existing
`TextBox.ReleaseLevelReferences()` helper when `mObtainedTextBox` exists. That
helper clears `mOwner` and `mScene` and resets transient display state.

The text-box object and the other player UI objects remain allocated. Their
existing `InitializeGame` reuse avoids unnecessary graphics-resource churn, and
the spell-wheel and icon-renderer state has no direct `PlayState`, `GameScene`,
or entity field.

The payload validator checks the null guard, the single cleanup call, the
helper's owner and scene resets, and the existing call from `PlayState.OnExit`
to `Player.DeinitializeGame`. Runtime validation should confirm that an old
scene is no longer rooted through `Player.mObtainedTextBox`.
