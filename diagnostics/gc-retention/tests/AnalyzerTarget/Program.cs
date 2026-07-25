using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: AnalyzerTarget <manifest.tsv>");
    return 2;
}

if (IntPtr.Size != 4
    || FormatHandleAddress(
        new IntPtr(unchecked((int)0xf1234567))) != "f1234567")
{
    throw new InvalidOperationException(
        "The analyzer target must normalize signed x86 pointer values"
        + " to zero-extended handle addresses.");
}

string manifestPath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(
    Path.GetDirectoryName(manifestPath) ?? Environment.CurrentDirectory);

StaleLevel staleLevel = new StaleLevel("integration-test-level");
PooledMissile pooledMissile = new PooledMissile
{
    Target = staleLevel,
};
PooledArray pooledArray = new PooledArray();
for (int index = 0; index < pooledArray.Slots.Length - 1; index++)
{
    pooledArray.Slots[index] = new object();
}

pooledArray.Slots[pooledArray.Slots.Length - 1] = staleLevel;
StaleLevel nestedLevel = new StaleLevel("nested-level");
RetiredScene retiredScene = new RetiredScene
{
    Level = nestedLevel,
};
TestRoots.PooledEntity = pooledMissile;
TestRoots.ArrayOwner = pooledArray;
TestRoots.NestedScene = retiredScene;

const long registryVersion = 73;
Magicka.GcDiagnostics.RetentionRegistry.RegistryVersion = registryVersion;

GCHandle levelHandle = GCHandle.Alloc(staleLevel, GCHandleType.Weak);
GCHandle missileHandle = GCHandle.Alloc(pooledMissile, GCHandleType.Weak);
GCHandle longWeakHandle = GCHandle.Alloc(
    staleLevel,
    GCHandleType.WeakTrackResurrection);
GCHandle arrayHandle = GCHandle.Alloc(pooledArray, GCHandleType.Weak);
GCHandle sceneHandle = GCHandle.Alloc(retiredScene, GCHandleType.Weak);
GCHandle nestedLevelHandle = GCHandle.Alloc(nestedLevel, GCHandleType.Weak);
long stateTicks = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(10)).Ticks;
int gen2AtState = GC.CollectionCount(GC.MaxGeneration);

staleLevel = null!;
pooledMissile = null!;
pooledArray = null!;
retiredScene = null!;
nestedLevel = null!;
GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
int currentGen2 = GC.CollectionCount(GC.MaxGeneration);

using Process currentProcess = Process.GetCurrentProcess();
long processStartUtcTicks = currentProcess.StartTime.ToUniversalTime().Ticks;
string processPath = Path.GetFullPath(
    currentProcess.MainModule?.FileName
    ?? throw new InvalidOperationException("Process path unavailable."));

using (StreamWriter writer = new StreamWriter(
           manifestPath,
           false,
           new UTF8Encoding(false)))
{
    writer.WriteLine("# magicka-gc-retention-v2");
    writer.WriteLine(
        "# pid\t"
        + currentProcess.Id.ToString(CultureInfo.InvariantCulture));
    writer.WriteLine(
        "# process-start-utc-ticks\t"
        + processStartUtcTicks.ToString(CultureInfo.InvariantCulture));
    writer.WriteLine("# process-path\t" + processPath);
    writer.WriteLine("# epoch\t1");
    writer.WriteLine("# checkpoint\t1");
    writer.WriteLine(
        "# registry-version\t"
        + registryVersion.ToString(CultureInfo.InvariantCulture));
    writer.WriteLine(
        "# current-gen2\t"
        + currentGen2.ToString(CultureInfo.InvariantCulture));
    writer.WriteLine(
        "id\thandle\texpectation\tepoch\ttype\tlifecycle"
        + "\tcreated_utc_ticks\tstate_utc_ticks\tgen2_at_state"
        + "\tcurrent_gen2");
    WriteWatch(
        writer,
        1,
        levelHandle,
        "MustCollect",
        typeof(StaleLevel).FullName!,
        "integration.Dispose",
        stateTicks,
        currentGen2,
        currentGen2);
    WriteWatch(
        writer,
        2,
        missileHandle,
        "MustDetach",
        typeof(PooledMissile).FullName!,
        "integration.Deinitialize",
        stateTicks,
        gen2AtState,
        currentGen2);
    WriteWatch(
        writer,
        3,
        longWeakHandle,
        "MustCollect",
        typeof(StaleLevel).FullName!,
        "integration.WrongHandleKind",
        stateTicks,
        currentGen2,
        currentGen2);
    WriteWatch(
        writer,
        4,
        missileHandle,
        "MustCollect",
        "Wrong.Type",
        "integration.WrongType",
        stateTicks,
        currentGen2,
        currentGen2);
    WriteWatch(
        writer,
        5,
        arrayHandle,
        "MustDetach",
        typeof(PooledArray).FullName!,
        "integration.LateArrayReference",
        stateTicks,
        gen2AtState,
        currentGen2);
    WriteWatch(
        writer,
        6,
        sceneHandle,
        "MustCollect",
        typeof(RetiredScene).FullName!,
        "integration.SceneDispose",
        stateTicks,
        currentGen2,
        currentGen2);
    WriteWatch(
        writer,
        7,
        nestedLevelHandle,
        "MustCollect",
        typeof(StaleLevel).FullName!,
        "integration.NestedLevelDispose",
        stateTicks,
        currentGen2,
        currentGen2);
}

Console.WriteLine(currentProcess.Id);
Console.Out.Flush();
Thread.Sleep(TimeSpan.FromMinutes(2));

TestRoots.PooledEntity = null;
TestRoots.ArrayOwner = null;
TestRoots.NestedScene = null;
levelHandle.Free();
missileHandle.Free();
longWeakHandle.Free();
arrayHandle.Free();
sceneHandle.Free();
nestedLevelHandle.Free();
return 0;

static void WriteWatch(
    StreamWriter writer,
    long id,
    GCHandle handle,
    string expectation,
    string typeName,
    string lifecycle,
    long stateTicks,
    int gen2AtState,
    int currentGen2)
{
    writer.Write(id.ToString(CultureInfo.InvariantCulture));
    writer.Write('\t');
    writer.Write(FormatHandleAddress(GCHandle.ToIntPtr(handle)));
    writer.Write('\t');
    writer.Write(expectation);
    writer.Write("\t1\t");
    writer.Write(typeName);
    writer.Write('\t');
    writer.Write(lifecycle);
    writer.Write('\t');
    writer.Write(stateTicks.ToString(CultureInfo.InvariantCulture));
    writer.Write('\t');
    writer.Write(stateTicks.ToString(CultureInfo.InvariantCulture));
    writer.Write('\t');
    writer.Write(gen2AtState.ToString(CultureInfo.InvariantCulture));
    writer.Write('\t');
    writer.WriteLine(currentGen2.ToString(CultureInfo.InvariantCulture));
}

static string FormatHandleAddress(IntPtr address)
{
    if (IntPtr.Size == 4)
    {
        return unchecked((uint)address.ToInt32()).ToString(
            "x",
            CultureInfo.InvariantCulture);
    }

    return unchecked((ulong)address.ToInt64()).ToString(
        "x",
        CultureInfo.InvariantCulture);
}

static class TestRoots
{
    public static PooledMissile? PooledEntity;
    public static PooledArray? ArrayOwner;
    public static RetiredScene? NestedScene;
}

sealed class PooledMissile
{
    public StaleLevel? Target;
}

sealed class RetiredScene
{
    public StaleLevel? Level;
}

sealed class PooledArray
{
    public object?[] Slots { get; } = new object?[700];
}

sealed class StaleLevel
{
    public StaleLevel(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
