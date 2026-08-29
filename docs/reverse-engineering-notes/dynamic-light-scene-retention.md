# Dynamic-light scene detachment

The original `PolygonHead.Lights.Light` removal lifecycle leaves its private
`mScene` field assigned. `Magicka.Graphics.Lights.DynamicLight.OnRemove()`
places the light in the static `sLightCache`, so a cached light can retain the
scene from its previous use.

The optional GC diagnostics reported the stable finding
`MustDetach:Magicka.Graphics.Lights.DynamicLight` at lifecycle
`Magicka.Graphics.Lights.DynamicLight.OnRemove.CacheInsert`. The shortest
observed outbound path was:

```text
DynamicLight --.mScene--> PolygonHead.Scene
```

Longer paths continued through the scene's active-light or light-removal
lists. Those list entries can exist temporarily until `Scene.UpdateLights()`
drains the queues. The direct `mScene` edge persists because the original
removal code never clears it.

Community Patch 0.0.49 moves scene detachment into the existing virtual
`Light.OnRemove()` lifecycle. The method preserves the old scene in a local,
clears `mScene`, and calls `RemoveLight(this)` on the preserved scene. The
duplicate scene-removal instructions after `OnRemove()` are removed from both
the immediate-disable branch and the completed fade-out branch.

`DynamicLight.OnRemove()` remains unchanged. It already calls
`base.OnRemove()` before inserting the instance into `sLightCache`, so a light
now loses its outgoing scene reference before becoming available for reuse.
Scene list mutation still uses the original synchronized `Scene.RemoveLight()`
queue. Network behavior and light initialization are unchanged.

The release validator checks the following assembly invariants:

- `Light.OnRemove()` loads the old scene, clears `mScene`, and then calls
  `Scene.RemoveLight()`;
- `Light.Disable(...)` and `Light.Update(...)` call `OnRemove()` once and no
  longer call `Scene.RemoveLight()` a second time;
- `DynamicLight.OnRemove()` calls the base cleanup before the cache insertion;
- all payload assemblies remain CLR-2-compatible.

Runtime validation remains useful after release. A successful result is the
absence of later `MustDetach` findings at the same cache-insertion lifecycle.
