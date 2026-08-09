# In-game UI render scaling

This note documents the patch sites that render gameplay UI with a virtual
layout size into a native-resolution transparent target and composite it over
the native-resolution 3D scene. It does not reproduce the modified original
classes.

## Render-target split

`PolygonHead.RenderManager.RenderScene` keeps the completed scene at native
resolution and redirects the gameplay GUI pass to a native-resolution render
target while exposing a smaller virtual coordinate system. For example, 200%
UI scale on a 3840x2160 backbuffer lays out the GUI in 1920x1080 coordinates
but rasterizes it directly into a 3840x2160 target. The resulting native-size
texture is composited back over the scene without first enlarging a low-
resolution UI image. This path is used only for active `PlayState` rendering
at supported backbuffer sizes.

Before `Scene.DrawGui`, the completed opaque scene is copied into that UI
target. Magicka then blends menus, dialog backgrounds, shadows, and other
partially transparent GUI elements directly over the scene, matching the
unscaled render path. The completed opaque image is copied back with blending
disabled. Keeping the scene and GUI in one target avoids relying on the alpha
channel left behind by effects that were originally designed to blend directly
over an opaque scene; compositing a separate transparent GUI texture made such
black overlays appear too pale.

The changed `RenderManager.ScreenSize` and `GUIScale` accessors return virtual
values only on the active render thread. Logic and input code continue to see
the native backbuffer size.

## Pre-rendered screen positions

Several render-data objects project a world position before the virtual GUI
pass begins. Their cached positions are therefore native pixels even though
their effects later receive the virtual screen size. The following draw methods
convert those cached projections with
`InGameUiRenderScale.AdjustProjectedPosition`:

- `TextBox.RenderData.Draw`
- `CutsceneText.CutSceneRenderData.Draw`
- `IconRenderer.RenderData.Draw`
- `SpellWheel.RenderData.Draw`
- `NotifierButton.RenderData.Draw`

`NotifierButton` supplies contextual prompts such as `Pick Up`. Its
`PreRenderUpdate` adds a layout offset after projecting the owner or copying a
screen anchor. The notifier-specific overload preserves that layout offset:

```text
virtual position = (cached position - layout offset) / scale + layout offset
```

This converts the native anchor while retaining the prompt's virtual-pixel
spacing, so its background, key icon, text, and placement scale together.
Because the same GUI render data can be drawn more than once in a render pass,
`NotifierButton.RenderData.Draw` saves the cached native position before the
conversion and restores it before returning. Each draw therefore starts from
the same native anchor instead of cumulatively scaling the previous draw's
temporary position and leaving duplicate prompts toward the upper-left corner.

## Menus and mouse input

The in-game menu uses the virtual size for layout and divides physical mouse
positions by the selected UI scale for hit testing. Gameplay aiming and picking
remain in native coordinates. Main-menu rendering is outside the scaled
gameplay pass and is unchanged.
