# Rain scene retention

The original `Rain.OnRemove()` stops its ambience and visual effect and restores
`mScene.LightTargetIntensity`, but leaves the per-cast `mScene` and `mCaster`
fields assigned. `Rain` is a process-lifetime singleton reached through the
process-lifetime `Thunderstorm` singleton.

GC retention telemetry reported the resulting path:

```text
Thunderstorm --.mRain--> Rain --.mScene--> GameScene --.mPlayState--> PlayState
```

The same edge also retains the disposed scene's `Level`. The original assembly
and Community Patch 0.0.51 have the same `Rain.OnRemove` behavior, so this is an
original lifecycle omission rather than a regression in the patch.

Community Patch 0.0.52 preserves the old scene in a local, clears `mScene` and
`mCaster`, and then restores the preserved scene's light intensity when it is
non-null. Clearing before the setter ensures that a failed light update cannot
leave the level graph attached.

`Thunderstorm.OnRemove()` also clears its per-cast `mOwner`. It deliberately
keeps `mRain`: the constructor assigns that permanent singleton dependency only
once, so clearing it would break later Thunderstorm casts.

The payload validator checks the cleanup order, the null-scene guard, the owner
reset, and preservation of `Thunderstorm.mRain`. Runtime validation should
confirm that later retention events no longer contain the path above.
