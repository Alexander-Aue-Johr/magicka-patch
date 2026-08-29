# Character-template static cache teardown

`CharacterTemplate.ClearCache` in the current game teardown disposes every
cached template. Several special abilities keep those disposed instances in
static fields until the next `PlayState` initializes their caches. CLR 2 exposes
these GC statics through pinned handle-table storage, which produces retention
paths beginning with `Pinning System.Object[]`.

The original Windows assembly contains the same static ability fields, but its
`CharacterTemplate.ClearCache` does not call `CharacterTemplate.Dispose` and it
has no `Magick.DisposeMagicks` teardown. The stale state is therefore caused by
combining the newer template disposal with the original static-cache lifetime.

The patch keeps cache initialization and ability behavior unchanged. It resets
the static references during `Magick.DisposeMagicks`, before
`CharacterTemplate.ClearCache` disposes the underlying templates.

Focused C# equivalents of the IL changes:

```diff
 static void SummonSpirit.DisposeCache()
 {
+    sTemplate = null;
 }

 static void SummonUndead.DisposeCache()
 {
+    sTemplates = null;
 }

 static void SummonCross.DisposeCache()
 {
     sCache.Clear();
+    sTemplate = null;
 }

+internal static void MutateBeastman.DisposeCache()
+{
+    sTemplate = null;
+}

+internal static void OtherworldlyDischarge.DisposeCache()
+{
+    sTemplate = null;
+}

+internal static void SummonElemental.DisposeCache()
+{
+    sTemplate = null;
+}

 static void Magick.DisposeMagicks()
 {
     // Existing cache teardown calls remain in their original order.
+    MutateBeastman.DisposeCache();
+    OtherworldlyDischarge.DisposeCache();
+    SummonElemental.DisposeCache();
 }

 static void CharacterTemplate.ClearCache()
 {
     CharacterTemplate[] templates =
         new List<CharacterTemplate>(mCachedTemplates.Values).ToArray();
     foreach (CharacterTemplate template in templates)
     {
         template.Dispose();
     }
     mCachedTemplates.Clear();
+    sCachedAvatarTemplates.Clear();
 }
```

The same single-field reset is added to `SummonFlamer`, `SummonZombie`, and
`SummonBug`. `SummonZombie` retains its existing object-pool clearing.
