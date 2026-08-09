# Borderless Fullscreen Loading Stability

This note documents the two patch sites used to prevent Direct3D 9 device-loss
crashes when Magicka loses focus during a loading screen. It intentionally does
not reproduce the full decompiled `Magicka.Game` class.

## Failure mode

Magicka's loading work runs on a loader thread and creates textures, effects,
shaders, and other graphics resources. Its loading screen also presents frames
directly. In exclusive fullscreen, switching to another application can put the
Direct3D 9 device into a lost state while that work is in progress. The result
is commonly a `DeviceLostException` from `LoadingScreen.Draw()` or a later
failure while constructing a graphics resource.

Simply catching the final exception is insufficient: the asset operation may
already depend on a usable graphics device. The patch therefore avoids the
ordinary exclusive Alt-Tab loss at the device-creation boundary.

## `Magicka.Game.mGraphics_PreparingDeviceSettings`

The original `BackBufferCount = 1` setting remains unchanged. When the
`GraphicsDeviceManager` is logically in fullscreen mode, the patch changes only
the device presentation parameters:

```text
keep BackBufferCount at 1
if logical fullscreen is enabled:
    create a windowed Direct3D presentation device
    set the fullscreen-only refresh-rate field to zero
```

XNA calls its window's fullscreen transition before raising
`PreparingDeviceSettings`. It therefore still creates the existing borderless,
monitor-sized window, but the resulting Direct3D device is non-exclusive. XNA's
logical `GraphicsDeviceManager.IsFullScreen` value and Magicka's saved
fullscreen setting remain enabled.

## `Magicka.Game.Update`

XNA marks its logical fullscreen window as topmost. That is appropriate for an
exclusive device, but with non-exclusive presentation it can obscure the
application selected through Alt-Tab. Directly after Magicka samples its window
focus, the patch applies this rule:

```text
if the saved setting is fullscreen and the actual device is non-exclusive:
    disable Form.TopMost
```

This keeps the borderless game window in the normal desktop Z-order and allows
the selected application to appear immediately.

## Result and scope

- Loading can continue creating graphics resources while Magicka is minimized
  or another application has focus.
- Alt-Tab no longer performs the ordinary exclusive Direct3D 9 device-loss
  transition.
- Windowed/fullscreen toggling, the fullscreen setting, selected render
  resolution, and XNA's monitor selection remain intact.
- Fullscreen presentation uses the desktop refresh mode rather than an
  exclusive refresh-rate switch.

This prevents the focus-related failure. It does not claim to eliminate device
loss caused by a graphics-driver reset, display reconfiguration, or hardware
failure.

## Mouse coordinates at non-native render resolutions

The borderless window remains monitor-sized even when Magicka renders to a
smaller selected backbuffer. `Mouse.GetState()` reports physical client
coordinates, whereas menu hit testing and gameplay picking expect coordinates
in the logical backbuffer. Both samples in `Magicka.Game.Draw` are therefore
routed through `MouseInputCompatibility.ScaleToLogicalResolution` before they
are stored in `Game.mMouseState`.

The transformation applies only while the saved fullscreen setting is enabled
and the actual presentation device is non-exclusive. It scales X and Y from
the WinForms client size to `PresentationParameters.BackBufferWidth` and
`BackBufferHeight`, preserving all mouse buttons and the scroll-wheel value.
Matching resolutions and windowed or exclusive presentation return the
original state unchanged.
