# Magicka Community Patch – Stability, Memory & Compatibility Fixes

## About This Project

This repository provides a community patch for Magicka, focused on improving long-term stability, memory behaviour, and compatibility with modern systems while preserving the original gameplay experience, visuals, and chaotic co-op gameplay.

The patch is the result of extensive reverse engineering, memory analysis, WinDbg debugging, dnSpy inspection, profiling, and long continuous gameplay test runs across multiple campaigns and DLCs.

---

## Major Fixes & Improvements

### GameSparks Removal

One major issue was that Magicka still initialized the discontinued GameSparks online service years after its shutdown.

The original reconnect logic could repeatedly attempt to reconnect in the background, eventually causing severe stuttering, freezes, and instability during long play sessions. Instead of attempting to patch the reconnect behaviour itself, this patch completely removes GameSparks initialization and update calls so the issue can no longer occur.

---

### Finalizer Crash Bug

Another critical issue involved the `PlayState` finalizer.

Normally, `Dispose()` should only run once when leaving a level. However, the original game also contained a finalizer that could call `Dispose()` again later when the garbage collector decided to run.

If a new level had already been loaded at that moment, the delayed cleanup could unload assets that were still actively in use by the new scene, resulting in random crashes and corrupted state.

<img width="1536" height="1024" alt="image" src="https://github.com/user-attachments/assets/df96629c-c7dd-4409-895d-70945e867457" />

---

## Memory Stability Improvements

The repository also contains fixes for memory leaks, GPU resource accumulation, scene transition cleanup, and asset disposal problems.

The included **Memory Usage Comparison** graphic shows continuous stability and memory stress tests across multiple campaigns and scene transitions.

The original comparison was intentionally performed using **Magicka 1.5.1.0 in Full HD (1920x1080)** because this version was widely considered by the community to be one of the most stable releases before later scene loading and unloading behaviour changed in 1.10.4.2.

In contrast, newer versions became significantly more unstable due to changes in scene transition handling and asset cleanup behaviour. During testing, Magicka 1.5.1.0 already crashed after a full Dungeons & Gargoyles playthrough even in Full HD. At 4K resolution, the original game often crashed almost immediately or within the first two scenes.

The patched version was tested in **4K (3840x2160)** and remained stable throughout repeated Dungeons & Gargoyles runs while also successfully transitioning into:
* The Other Side of the Coin (OSOTC)
* Magicka Vietnam

These additional loads were included to test cross-campaign scene transitions and memory cleanup behaviour rather than full campaign playthroughs.

The comparison demonstrates:
* More stable memory usage
* Better cleanup between scenes
* Reduced long-session memory growth
* Improved stability even at 4K resolution

The patched version now surpasses even the previously regarded “stable” 1.5.1.0 release in long-session stability testing.

<img width="1671" height="941" alt="Magicka_Memory_Usage" src="https://github.com/user-attachments/assets/ce7ea3c3-e545-46a8-a1f6-4f8db0708cf0" />

---

## Community & Bug Reports

Players are very welcome to report crashes, bugs, regressions, or remaining issues through **GitHub Issues**.
Feedback, test results, and reproduction steps are extremely helpful for improving the patch further.

---

## Support The Project

This is a passion project dedicated to preserving and stabilizing one of the most chaotic and charming co-op games ever made.

If this patch helped you enjoy Magicka again and you would like to support further development, debugging, and future fixes, you can support the project on Patreon:

[Patreon Support Page](https://www.patreon.com/16047341/join)
