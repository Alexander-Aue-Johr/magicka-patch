# Magicka Community Patch: A Forensic Engine Investigation

> [!TIP]
>
> ### ❤️ Enjoying the Patch?
>
> If the patch brought Magicka back to life for you and you'd like to support further reverse-engineering work and long-term maintenance, consider supporting the project on Patreon:
>
> **☕ Support on Patreon:** [https://www.patreon.com/16047341/join](https://www.patreon.com/16047341/join)
> 
> 🚀 **Latest release:** https://github.com/Alexander-Aue-Johr/magicka-patch/releases/latest
>
> 🐛 **Report issues:** https://github.com/Alexander-Aue-Johr/magicka-patch/issues
>
> 💬 **Prefer not to use GitHub?** You can also add me on Steam and send me what you observed: **27126879**
> 
> Every bit of support helps keep these investigations going.


## About This Project

This repository contains a massive collection of root-cause stability, memory-management, and compatibility fixes for Magicka and its DLCs. 

This project is the culmination of weeks of grueling reverse engineering, dnSpy inspection, WinDbg/SOS heap analysis, memory profiling, and endless continuous stress testing. It was built to answer a single question: *Why does this engine slowly collapse, and how do we actually fix it?*

The goal is to preserve the original gameplay experience while completely overhauling the resource lifetime management to ensure rock-solid stability during long play sessions, even at 4K resolution. This README is written specifically for other developers, engineers, and perhaps even the original creators, to document exactly what went wrong under the hood for more than a decade, and how it was finally resolved.

---

## The Mystery: A 32-bit Engine on the Brink

For weeks, I kept chasing crashes that seemed completely random. The game showed an impossible array of symptoms: crashes during scene transitions, crashes after cutscenes, random out-of-memory (OOM) exceptions, severe stuttering after long sessions, and instability that became catastrophic at 4K.

Some crashes happened immediately. Others appeared 30 minutes later. Sometimes the game would survive several scene transitions before suddenly collapsing in a completely unrelated location. 

The deeper I investigated the heap state and object lifetimes, the more obvious it became that the visible crashes were often not the source of the problem. Time and time again, the real mistake had happened much earlier. The real question slowly changed from *"Why is the game crashing?"* to *"Why are old game sessions still alive at all?"*

---

## 1. The GameSparks Arithmetic Overflow

One of the most bizarre bugs caused the game to suddenly become incredibly stuttery and unplayable after long sessions. 

Magicka still initialized the old GameSparks online service, even though the GameSparks backend was discontinued years ago. The SDK uses an exponential reconnect backoff timer that runs roughly every 16ms:

```csharp
this.RetryBase * (int)Math.Pow(2.0, (double)attempt)
```

Because the backend is dead, reconnect attempts continue indefinitely. After 21 failed attempts, `Math.Pow(2.0, 21)` equals `2097152`. Multiplied by a `RetryBase` of `2000`, the result is mathematically `4194304000`. 

However, this exceeds the maximum signed 32-bit integer value and overflows into `-100663296`. The SDK then passes this negative value into a random number generator:

```csharp
Random.Next(0, negativeValue)
```

This throws a `System.ArgumentOutOfRangeException`, which is silently swallowed inside the 16ms timer loop. The timer immediately retries on the next tick, throwing thousands of exceptions per second, repeatedly creating useless WebSockets, and bringing the game to its knees.

**The Fix:** The obsolete GameSparks initialization and update calls were completely stripped out of `Game.cs`.

```diff
namespace Magicka
 {
 	public sealed class Game : Game
 	{
    // ...
 		protected override void Initialize()
 		{
			// ...
 			Singleton<ParadoxServices>.Instance.Initialize();
-			Singleton<GameSparksServices>.Instance.Initialize<GSWindowsPlatform>();
 			base.Initialize();
 		}
    // ...
 		private void Update(float iDeltaTime)
 		{
 			SteamAPI.RunCallbacks();
			// ...
 			Singleton<ParadoxServices>.Instance.Update();
-			Singleton<GameSparksServices>.Instance.Update();
 			Singleton<ParadoxAccount>.Instance.Update();
 		}
    // ...
 		protected override void EndRun()
 		{
			// ...
 			NetworkManager.Instance.Dispose();
 			Singleton<ParadoxServices>.Instance.Dispose();
-			Singleton<GameSparksServices>.Instance.Dispose();
 			base.EndRun();
 		}
    // ...
 	}
 }
```

---

## 2. The Pharaoh in the Memory

While inspecting memory usage and logging every asset load to track down Out-Of-Memory exceptions, I noticed massive assets being loaded that clearly had nothing to do with the current level. One filename stood out: **Pharaoh**. 

The Pharaoh is a purchasable cosmetic character skin. Magicka was preloading essentially *all* playable character skins in the database at the start of a session, regardless of whether any player was actually using them.

```diff
  public static void InitialisePlayerAvatarCache(PlayState iPlayState)
  {
      CharacterTemplate.sCachedAvatarTemplates.Clear();
-     foreach (Profile.PlayableAvatar playableAvatar in Profile.Instance.Avatars.Values)
+     foreach (Player player in Game.Instance.Players)
      {
+         if (player != null && player.Gamer != null)
+         {
+             string typeName = player.Gamer.Avatar.TypeName;
              // ... dynamic loading logic ...
```

By changing this to only load skins actively needed by connected players (and dynamically loading late-joiners), over **100 MB of memory** was immediately freed, drastically reducing OOM crashes.

---

## 3. Missing IDisposable Implementations

While fixing the character avatars, another major issue involved assets loaded through the XNA content pipeline. 

Classes such as `CharacterTemplate` and many other custom asset types were loaded by the content manager but *never implemented* `IDisposable`. Because of this, unloading the content manager only removed the top-level asset references internally without actually calling cleanup logic for those objects.

At first glance this looked harmless. However, many of these objects were heavily referenced throughout gameplay systems, caches, entities, and scene logic. As long as those internal references existed, the garbage collector could not fully reclaim them. 

**The Fix:** Proper `IDisposable` behaviour was retrofitted to many asset classes (like `CharacterTemplate : IDisposable`) so they can correctly release internal resources, cached references, and associated data during unloading.



---

## 4. The Finalizer Trap: A Cure Worse Than the Disease

The developers seemingly noticed that old game objects were not always being unloaded properly. To address this, they introduced a C# finalizer (`~PlayState()`) to automatically dispose of old game states. 

While the idea sounded reasonable, the implementation introduced a fatal race condition. `Dispose()` was already being called correctly when leaving a level. But because finalizers are executed asynchronously by the C# garbage collector at unpredictable times, `Dispose()` would suddenly run a *second* time much later.

```diff
 namespace Magicka.GameLogic.GameStates
 {
 	public class PlayState : GameState, IDisposable
 	{
 		public void Dispose()
 		{
 			if (this.mContent != null)
 			{
 				this.mContent.Unload();
 				this.mContent.Dispose();
 				this.mContent = null;
 			}
 		}

-		~PlayState()
-		{
-			this.Dispose();
-		}
 	}
 }
```

If a new level had already loaded by the time the GC triggered the finalizer, this delayed cleanup would accidentally unload assets that were *actively in use* by the new scene. This was responsible for a huge percentage of random scene-transition and cutscene crashes.

<img width="1536" height="1024" alt="The Finalizer Problem in Magicka" src="https://github.com/user-attachments/assets/df96629c-c7dd-4409-895d-70945e867457" />

---

## 5. Hunting Ghosts on the Heap

Fixing the finalizer stopped the random unloads, but why was the GC waiting so long to trigger in the first place? Using WinDbg and SOS, I started directly analyzing the managed heap.

```text
!dumpheap -type PlayState
!gcroot <object-address>
!dumparray <array-address>
```

WinDbg revealed that old `PlayState` instances frequently remained alive far longer than intended because unrelated static systems silently stored references to them. For example, dumping pinned object arrays revealed hidden trigger and action data:

```text
[733] 037f16e4   Magicka.GameLogic.GameStates.PlayState
[734] ...        List<Magicka.Levels.Triggers.Actions.GiveOrder>
```

### The Flash Effect: A Single Frame That Kept Entire Levels Alive

While investigating the finalizer crashes, I discovered that the delayed double-disposal was only half the battle. A much larger issue was that old sessions frequently remained alive far longer than intended because completely unrelated systems silently stored references to the state, the levels, or the rendering scenes. 

These references were often hidden deep inside static gameplay caches, helper systems, or temporary visual logic. One of the most striking examples of this "butterfly effect" on the managed heap was the cinematic **Flash Effect** (`Magicka.Graphics.Flash`). 

This visual effect is used sparingly during specific narrative moments—such as the boss fight with Vlad in his castle, or in the *Dungeons & Gargoyles* DLC to trigger the flashback revealing how the cult turned the villagers into slimes. 

When executed, the screen flashes white for a fraction of a second. The visual is gone almost instantly. But beneath the surface, the effect class did something catastrophic: it stored a reference to the active `Scene` internally and *never released it*.

```csharp
// The fatal flaw: A long-lived visual effect caching the active Scene
public class Flash : IAbilityEffect, IRenderableAdditiveObject, IPreRenderRenderer
{
    private static Flash sSingelton; // Static root!
    
    // This single reference prevents the entire render pipeline from being garbage collected.
    private Scene mScene; 

    public void Execute(Scene iScene, float iTime)
    {
        this.mTTL = (this.mIntensity = Math.Max(iTime, this.mIntensity));
        
        // THE BUG: Storing a hard reference to the current scene inside a Singleton
        this.mScene = iScene; 
        
        SpellManager.Instance.AddSpellEffect(this);
    }
}
```

Because `Flash` is a static Singleton (`Flash.Instance`), the object itself outlives the current level. The `mScene` reference was only overwritten if the flash effect happened to be executed *again* much later in the game. 

Why is holding the `Scene` so dangerous? Looking at the engine's `PolygonHead.Scene` class, it acts as the master container for the entire rendering pipeline. It holds massive collections:
* `List<IRenderableObject>[] mRenderableObjects`
* `List<IRenderableAdditiveObject>[] mRenderableAdditiveObjects`
* `List<Light> mLights`
* `List<IProjectionObject>[] mProjectionObjects`

By statically anchoring the `Scene`, the Singleton prevented the Garbage Collector from clearing those render lists. This meant essentially every enemy, spell, mesh, light, and UI element that implemented those rendering interfaces remained permanently pinned in memory, along with all their associated GPU vertex buffers and effect hashes. 

Patterns exactly like this existed throughout large parts of the codebase. As a result:
* Old game scenes remained artificially alive.
* Massive object graphs and render queues remained referenced in the background.
* GPU resources quietly accumulated over time, inevitably leading to Out-Of-Memory exceptions.

**The Fix:**

To address this, the patch implements a massive cleanup of static roots. Dozens of classes were rewritten to ensure temporary state is actually discarded, or better yet, *never stored in the first place*.

For the `Flash` effect, the fix completely removes the caching behavior. Instead of saving the `Scene` inside the Singleton, the `Update` method now resolves the currently active scene dynamically on the fly:

```diff
  public void Execute(Scene iScene, float iTime)
  {
      this.mTTL = (this.mIntensity = Math.Max(iTime, this.mIntensity));
-     this.mScene = iScene;
      SpellManager.Instance.AddSpellEffect(this);
  }

  public void Update(DataChannel iDataChannel, float iDeltaTime)
  {
      this.mIntensity -= iDeltaTime;
      this.mIntensities[(int)iDataChannel] = this.mIntensity / this.mTTL * 0.75f;
      
-     this.mScene.AddRenderableAdditiveObject(iDataChannel, this);
+     PlayState.RecentPlayState.Scene.AddRenderableAdditiveObject(iDataChannel, this);
  }
```

This seemingly small architectural shift changed everything. By preventing long-lived systems from persistently anchoring themselves to specific game scenes, the garbage collector can finally reclaim discarded render queues natively without interference, drastically improving memory stability during continuous play sessions.

---

## 6. XNA’s Secret Arsenal: The Missing Link

Even after fixing the `PlayState` leaks, memory usage barely decreased between scenes. Magicka’s custom `SharedContentManager` looked well-designed—it reference-counted assets and properly called `.Dispose()` on them. So why were RAM allocations permanently accumulating?

Digging into XNA’s decompiled framework internals revealed a global `List<IDisposable> disposableAssets`. When XNA loads a top-level asset, it often secretly spins up child GPU resources (vertex buffers, render targets) and tracks them in its own internal list.

I discovered how Magicka called the asset reader:

```csharp
base.ReadAsset<T>(assetName, null);
```

Because Magicka passed `null` for the `recordDisposableObject` action, XNA silently hoarded all child GPU resources globally. During scene transitions, Magicka correctly disposed the parent asset, but XNA kept the heavy GPU resources alive until the *entire* `ContentManager` was destroyed.

**The Fix:** Intercept XNA's disposable resources during load and tie them directly to Magicka's reference-counting system:

```diff
- referencedAsset.Asset =
-     base.ReadAsset<T>(assetName, null);

+ referencedAsset.Asset =
+     base.ReadAsset<T>(
+         assetName,
+         delegate(IDisposable disposableObject)
+         {
+             referencedAsset.DisposableObjects.Add(disposableObject);
+         });
```

Now, when a scene unloads, all underlying XNA RAM resources are instantly nuked. This changed everything.

---

## 7. Shared Shader Effects


While investigating memory usage, one category of allocations stood out immediately: shader effects. During level loading, many of these effects were recreated repeatedly even though they all used the exact same compiled shader bytecode.

Originally, every effect class directly inherited from `Effect` and constructed a completely new effect instance:

```csharp
public class RenderDeferredEffect : Effect
```

This meant that every instance recreated its own internal graphics resources from the compiled shader bytecode, accumulating duplicate internal VRAM resources.

**The Fix:**
A new base class called `SharedEffect` was introduced. Instead of creating a completely new `Effect` every time, the class keeps one shared base effect per shader bytecode and clones from it:

```csharp
public class SharedEffect : Effect
{
    private static readonly Dictionary<byte[], Effect> sSharedEffects = new Dictionary<byte[], Effect>();

    protected SharedEffect(GraphicsDevice graphicsDevice, byte[] effectCode, CompilerOptions options, EffectPool effectPool)
        : base(graphicsDevice, GetOrCreateSharedEffect(graphicsDevice, effectCode, options, effectPool))
    {
    }
}
```

Several major effect classes were converted to use this, including `AdditiveEffect`, `GUIBasicEffect`, `RenderDeferredEffect`, `SkinnedModelBasicEffect`, and `SpotLightEffect`. The effect system now behaves much more like a shared resource cache, reducing GPU memory pressure during scene loading.

---

## 8. The 4K Catalyst

At lower resolutions, the fragmented 32-bit address space could mostly survive the engine's memory leaks. At 4K, the allocations became so massive the game would instantly crash.

### The Permanent Screenshot Buffer
When leaving a level, Magicka briefly captures the final game screen and displays it as a photo inside the menu book. However, the screenshot render target permanently existed throughout gameplay:

```csharp
this.mScreenShot = new ResolveTexture2D(this.mDevice, screenSize.X, screenSize.Y, 1, SurfaceFormat.Color);
```

At 4K, this meant an enormous, completely unused 4K render target was hogging VRAM for 99% of the session. The patch refactors `RenderManager.cs` to allocate this buffer lazily *only* at the exact moment the screenshot is requested, and immediately dispose of it afterward.

### Resolution-Aware Multisampling
Magicka forcefully applied 4x multisampling to GUI render targets. At 4K, this caused massive bandwidth and memory spikes. The patch scales GUI multisampling based on resolution (4x for 1080p, 2x for 1440p, and disabled entirely for 4K).

---

## 9. Restoring Scene Transition Logic

Older versions of Magicka loaded the next scene *first*, then unloaded the previous scene. A later patch accidentally reversed this:

```csharp
// The Broken Order
gameScene.UnloadContent();
this.mNextScene.LoadLevel();
```

By unloading first, shared assets (textures, models) temporarily hit a reference count of zero, were destroyed from RAM, and were then immediately re-allocated milliseconds later by the new scene. This caused horrific RAM fragmentation. 

```diff
+ this.mNextScene.LoadLevel();
  this.mCurrentScene = this.mNextScene;
  if (this.mCurrentScene != gameScene && gameScene != null)
  {
      gameScene.UnloadContent();
  }
- this.mNextScene.LoadLevel();
```
The patch restores overlapping loads, allowing the shared asset manager to naturally recycle shared textures without thrashing the GPU.

---

## The Results

The memory comparison below shows continuous stability and memory stress tests across multiple campaigns and scene transitions.

The original game (1.5.1.0) was tested at **1080p** and still crashed due to RAM exhaustion when playing *Dungeons & Gargoyles* for the second time. The patched version was tested at **4K** and remained completely stable through repeated *Dungeons & Gargoyles* runs, transitioning flawlessly into *The Other Side of the Coin* and *Magicka Vietnam*.

<img width="1671" height="941" alt="Magicka_Memory_Usage" src="https://github.com/user-attachments/assets/ce7ea3c3-e545-46a8-a1f6-4f8db0708cf0" />

The patched version now achieves greater long-session stability at 4K than the unpatched game previously managed at 1080p.

---

## Credits & Acknowledgements

Special thanks to **pj1234678** for previously maintaining the MagickaFix repository. Before that repository went offline, it was my first major point of reference and the reason I realized patching this XNA-based game was even possible. It motivated me to start experimenting with dnSpy and to learn how to investigate and patch the game assembly directly, leading to this root-cause investigation.

## Community & Support

* **Bug Reports:** Players are highly encouraged to report crashes, bugs, regressions, or remaining issues through GitHub Issues. Logs and reproduction steps are incredibly valuable.
* **Support Development:** This is an independent passion project dedicated to preserving Magicka. The weeks of debugging, reverse engineering, and testing required a tremendous amount of time and effort. If this patch helped you enjoy the game again and you would like to support the continued preservation of this engine, consider supporting the project on Patreon: **[Join the Patreon](https://www.patreon.com/16047341/join?utm_source=chatgpt.com)**
