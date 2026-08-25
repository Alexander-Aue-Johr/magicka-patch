# Paradox store price render-thread hang

This note documents a reproducible pause between the campaign intro and the
loading screen, the diagnostic path that identified it, and the compatibility
patch applied to `Magicka.exe`.

## Symptom and reproduction

The issue was reproduced on Bazzite with 32-bit Magicka running through Proton
and Wine Mono 11.2.0. The original game executable, without Community Patch
code, was used for the diagnosis.

1. Sign in far enough for `ParadoxAccount.IsLoggedFull` to become true.
2. Open the character-selection menu and select **Start**.
3. Let the campaign intro begin.
4. Click in the game window once to skip the intro.
5. Observe the fade from the Tome menu toward the loading screen.

The game could stop before the loading bar appeared and recover by itself after
approximately 100 seconds. The same symptom occurred with DXVK/Vulkan and with
WineD3D/OpenGL, on Proton Experimental and Proton 11.0-2, and with the game on a
RAM disk. Process and system memory remained available, and the longest
recorded Mono stop-the-world GC pause was only 8.57 ms.

## Diagnostic path

Temporary diagnostic builds placed duration checkpoints around existing call
sites. None of that instrumentation or its companion logging assembly is part
of the final patch.

The checkpoints narrowed the blocked render frame in four stages:

```text
Game.Draw
  -> PolygonHead.RenderManager.RenderScene
  -> RenderManager.OnTransitionEnd
  -> SubMenuIntro.OnTransitionEnd
  -> Tome.PopMenuInstant
  -> SubMenuCharacterSelect.OnEnter
  -> StoreItemDatabase.UpdateParadoxItems
```

The final capture measured
`SubMenuCharacterSelect.OnEnter -> StoreItemDatabase.UpdateParadoxItems` at
100191.5 ms. The next call in `OnEnter` completed normally. During the pause,
the render thread waited in Wine's `NtWaitForSingleObject`/`ntsync` path while
the logic thread slept; GPU work was idle. This excludes loading-screen drawing,
shader compilation, asset storage, and garbage collection as the primary cause.

`UpdateParadoxItems` performed two price requests synchronously on that render
thread before starting the game's already-asynchronous owned-inventory request:

```text
https://api.paradoxplaza.com/bertil/steamwallet/userinfo
http://services.paradoxplaza.com/adam/offers/red_wizard
```

The three-argument `WebRequestHelper.Get` uses
`HttpWebRequest.GetResponse()` and `StreamReader.ReadToEnd()` synchronously. It
does not configure a request timeout, read/write timeout, or connection policy.
The observed pause is consistent with the legacy request's 100-second timeout
path.

Host-side probes returned HTTP 200 quickly, showing that general connectivity
was available. A separate probe using the same Wine Mono runtime and request
properties produced these results:

| Request | Result |
| --- | --- |
| HTTPS `userinfo` | HTTP 200 in 651 ms |
| HTTP `offers/red_wizard` | `WebException` for status 308 in 37 ms |
| HTTPS `offers/red_wizard` | HTTP 200 in 564 ms |

The obsolete HTTP URL therefore also prevents reliable offer retrieval under
Wine Mono. The exact in-game 100-second wait can still depend on connection and
runtime state; asynchronous execution is required even after correcting the
URL.

The code was synchronous on Windows as well. There is no evidence that the
Windows version used an asynchronous implementation or a deliberately shorter
timeout. The likely difference is that Microsoft's .NET networking stack
completed or handled this endpoint path quickly enough that the synchronous
design was not visible to players.

## Patch behavior

`StoreItemDatabase.UpdateParadoxItems` now queues only the legacy price-refresh
portion through `RuntimeCompatibilityGuards.QueueParadoxStorePriceUpdate`.
The owned-inventory request remains on its original caller and retains its
existing asynchronous callback.

The price worker preserves the original parsing and `StoreInventoryItem`
updates. If both responses arrive later, their prices are still applied and are
available when the player subsequently opens the store. The offers endpoint is
changed from HTTP to HTTPS so Wine Mono receives the live JSON response without
the unsupported status-308 path.

An `Interlocked.CompareExchange` guard permits only one outstanding price
refresh. Re-entering menus while a request is pending does not create another
worker. The guard is cleared after success or failure, so a future menu entry
can retry. Worker exceptions are caught because an unhandled exception on a
ThreadPool callback must not terminate the game.

High-level patch-site pseudocode:

```csharp
public void UpdateParadoxItems()
{
    RuntimeCompatibilityGuards.QueueParadoxStorePriceUpdate(
        UpdateParadoxPricesSynchronously);

    // Original asynchronous owned-inventory request remains here.
}

private void UpdateParadoxPricesSynchronously()
{
    // Original request, JSON parsing, and UpdatePrice logic.
    // offers/red_wizard now uses HTTPS.
}
```

The authored queue helper is documented in
`docs/injected-source/Magicka.CommunityPatch/RuntimeCompatibilityGuards.cs`.
