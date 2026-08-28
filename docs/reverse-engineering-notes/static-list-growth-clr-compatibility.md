# Collection growth CLR compatibility

Community Patch 0.0.45 added `EnsureCapacityForCommunityPatch` to
`StaticList<T>` and `StaticWeakList<T>`. Their mutation methods call it while
holding the collection's existing lock, preserving the original entries and
their order when the backing array grows.

## Windows failure

Recompiling the affected collection methods changed lock acquisition from
`Monitor.Enter(object)` to `Monitor.Enter(object, ref bool)`. The latter
overload is a .NET 4 API and does not exist on the .NET 3.5/CLR-2 framework used
by Magicka. Wine Mono supplies the overload, while Windows throws
`MissingMethodException` when `StaticList<T>.Add` is first called during scene
initialization. The same incompatible call appears in `StaticList<T>.Insert`
and in `StaticWeakList<T>.Add`, `Insert`, and `Expand`.

## Patch behavior

All five methods now call `Monitor.Enter(object)`. Their existing lock-taken local
is set to true immediately after the call returns. The generated conditional
`finally` block therefore calls `Monitor.Exit` only after successful lock
acquisition, matching the safety property of the newer overload without
requiring its API.

Dynamic capacity growth, item ordering, and bounded `static_list_expanded`
telemetry remain unchanged. The payload validator rejects the two-argument
overload anywhere in the managed payload and checks the complete lock shape in
all five affected collection methods.
