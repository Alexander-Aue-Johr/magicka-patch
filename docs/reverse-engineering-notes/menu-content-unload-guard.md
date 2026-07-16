# Menu Content Unload Guard

This note documents the menu-resource lifetime fix in
`Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect`. It does not
include decompiled game source.

## Failure

`SubMenuCharacterSelect.OnUnload` previously waited only while character
selection was `Tome.CurrentMenu`. Once a page transition made another menu
current, it unloaded `mTextureContent` and set its texture fields to `null`.

That condition was too narrow. During page transitions, `Tome` can still draw
the menu that has just stopped being current. The render thread also consumes
one of three cached `Tome.RenderData` channels, so a channel can still hold
`SubMenuCharacterSelect` briefly after the logic thread has changed menus.
`DrawAvatar` could consequently receive a texture that had just been cleared
and dereference its `Width` or `Height`.

## Stack behavior

The two relevant stacks have different lifetimes:

- `GameStateManager.PushState(PlayState)` leaves `MenuState` underneath the
  play state. Popping the play state re-enters that same menu state.
- `Tome.PushMenu` leaves the previous submenu in its array. Forward/base page
  drawing can use the lower adjacent entry; backward page drawing uses the
  upper adjacent entry.
- While `PlayState` is the active outer state, the underlying `MenuState` and
  its `Tome` are not rendered. `PlayState.OnExit` reloads the character-select
  textures while rendering is disabled before it enables menu rendering again.

The patch therefore does not require character selection to be removed from
the `Tome` array before freeing its textures. It requires the menu not to be
reachable by the active menu render pipeline.

## Patch sites

`Magicka.GameLogic.UI.Tome.IsMenuReferencedByRenderPipeline(SubMenu)` was added
as an internal helper. While `MenuState` is active it keeps a submenu alive if:

- it is referenced as `CurrentMenu` by any of the three cached render channels;
- it is the live current `Tome` menu; or
- a live or cached transition state can still call `DrawNewAndOld` and it is one
  of the two adjacent entries that forward or backward drawing can use.

When another outer game state such as `PlayState` is active, `Tome` is not part
of the rendered scene and the helper returns false.

The unload worker created by `SubMenuCharacterSelect.OnUnload` now waits on
that helper instead of checking only `Tome.CurrentMenu`. Once no live or cached
draw can reference the submenu, it unloads the content manager and clears the
texture fields as before. The unload-and-clear block holds the same
`GraphicsDevice` lock used by the render thread, so it cannot run in the middle
of an active draw call.

Magicka runs on CLR 2.0, where the later
`Monitor.Enter(object, ref bool)` overload is unavailable. The added lock is
therefore emitted with `Monitor.Enter(object)` and retains the generated
`try`/`finally` guard by setting its existing `lockTaken` local only after
`Monitor.Enter` returns successfully. This preserves the intended conditional
`Monitor.Exit` behavior without introducing a CLR 4 API dependency.

`SubMenuCharacterSelect.DrawAvatar` also returns immediately when its robe
texture is null, or when a custom avatar was requested but `mCustomTexture` is
null. This is a last-resort race guard; it is not the primary resource-lifetime
decision.

## ContentManager unload audit

All direct `ContentManager.Unload` sites in the decompiled executable were
reviewed. Character selection was the only site that coupled an unload to a
`CurrentMenu`/`CurrentState` wait. The other sites were not changed because
their lifetime rules differ:

| Site | Existing lifetime rule |
| --- | --- |
| `PlayState.Dispose` and `GameScene.UnloadWhenNotInUseAnymore` | Rendering is disabled/drained before scene resources are freed. |
| `SubMenuLeaderboards.LoadTexture`, `SubMenuCharacterSelect.LoadLevelPreviews`, `HUDStylesMenu.OnEnter`, and `FontManager.LoadFonts` | Replace content transactionally and load the new content immediately. |
| `SubMenuCutscene.Dispose` | Every cutscene draw entry checks `mDisposed` before using resources. |
| `BannerManager`, `Storefront`, `CircularTimer`, and legal-document popup content | Owned by the corresponding manager/widget visibility or disposal lifecycle. |
| `SpritesheetManager`/`Spritesheet` | Shared global cache lifetime, cleared on outer game-state transitions rather than a `Tome` submenu transition. |
| `SharedContentManager` | Generic reference-counted/shared content behavior. |

Applying the new `Tome` rule globally to those sites would retain unrelated
content or interfere with deliberate reload operations, so the patch remains
limited to the failing character-selection texture lifetime.
