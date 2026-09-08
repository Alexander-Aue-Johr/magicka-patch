using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class EffectManagerScenarios
{
    internal static void Prepare(Assembly magicka)
    {
        EffectManagerHarness.InstallProbeEarly(magicka);
    }

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        EffectManagerHarness harness = new EffectManagerHarness(magicka);
        try
        {
            report.Add(
                "effect_manager.duplicate_name",
                harness.DuplicateName());
            report.Add(
                "effect_manager.unique_names",
                harness.UniqueNames());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class EffectManagerHarness
{
    private const string HarmonyOwner =
        "org.magickacommunitypatch.behavior-probe-effect-manager";

    private readonly Type managerType;
    private readonly Type visualEffectType;
    private readonly MethodInfo readDirectory;
    private readonly FieldInfo sourceEffectsField;
    private readonly HarmonyInstance harmony;

    internal EffectManagerHarness(Assembly magicka)
    {
        managerType = magicka.GetType(
            "Magicka.Graphics.EffectManager",
            true);
        visualEffectType = RuntimeReflection.FindLoadedType(
            "PolygonHead.ParticleEffects.VisualEffect");
        readDirectory = managerType.GetMethod(
            "ReadDirectory",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(DirectoryInfo) },
            null);
        sourceEffectsField = RuntimeReflection.RequireField(
            managerType,
            "mSourceEffects");
        if (readDirectory == null ||
            !sourceEffectsField.FieldType.IsGenericType)
            throw new MissingMemberException(
                "EffectManager behavior contract is incomplete.");
        harmony = HarmonyInstance.Create(HarmonyOwner);
    }

    internal void Dispose()
    {
        EffectManagerProbe.Enabled = false;
        harmony.UnpatchAll(HarmonyOwner);
    }

    internal ScenarioResult DuplicateName()
    {
        return RunDirectoryCase("same", "same", 1, false);
    }

    internal ScenarioResult UniqueNames()
    {
        return RunDirectoryCase("first", "second", 2, false);
    }

    private ScenarioResult RunDirectoryCase(
        string rootName,
        string nestedName,
        int expectedCount,
        bool expectedFailure)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "magicka-effect-manager-" + Guid.NewGuid().ToString("N"));
        string nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(root, rootName + ".xml"), "probe");
        File.WriteAllText(Path.Combine(nested, nestedName + ".xml"), "probe");

        object manager = FormatterServices.GetUninitializedObject(managerType);
        GC.SuppressFinalize(manager);
        object dictionary = Activator.CreateInstance(sourceEffectsField.FieldType);
        sourceEffectsField.SetValue(manager, dictionary);
        EffectManagerProbe.Reset(visualEffectType);
        EffectManagerProbe.Enabled = true;
        string exception = "none";
        try
        {
            readDirectory.Invoke(
                manager,
                new object[] { new DirectoryInfo(root) });
        }
        catch (TargetInvocationException caught)
        {
            exception = (caught.InnerException ?? caught).GetType().Name;
        }
        finally
        {
            EffectManagerProbe.Enabled = false;
            Directory.Delete(root, true);
        }

        int count = ((IDictionary)dictionary).Count;
        bool failed = exception != "none";
        bool passed = EffectManagerProbe.FromFileCalls == expectedCount &&
            count == expectedCount && failed == expectedFailure;
        string actual = "loads:" + EffectManagerProbe.FromFileCalls +
            ",entries:" + count + ",exception:" + exception;
        string expected = "loads:" + expectedCount +
            ",entries:" + expectedCount + ",exception:" +
            (expectedFailure ? "ArgumentException" : "none");
        return new ScenarioResult(passed, actual, expected);
    }

    internal static void InstallProbeEarly(Assembly magicka)
    {
        Type visualEffect = RuntimeReflection.FindLoadedType(
            "PolygonHead.ParticleEffects.VisualEffect");
        MethodInfo fromFile = visualEffect.GetMethod(
            "FromFile",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(string) },
            null);
        if (fromFile == null || fromFile.ReturnType != visualEffect)
            throw new MissingMethodException(
                visualEffect.FullName,
                "FromFile");
        MethodInfo prefix = typeof(EffectManagerProbe).GetMethod(
            "FromFilePrefix").MakeGenericMethod(visualEffect);
        HarmonyInstance.Create(HarmonyOwner).Patch(
            fromFile,
            new HarmonyMethod(prefix),
            null,
            null);
    }
}

public static class EffectManagerProbe
{
    public static bool Enabled;
    public static int FromFileCalls;
    public static object Effect;

    public static void Reset(Type visualEffectType)
    {
        FromFileCalls = 0;
        Effect = Activator.CreateInstance(visualEffectType);
    }

    public static bool FromFilePrefix<TEffect>(ref TEffect __result)
    {
        if (!Enabled)
            return true;
        FromFileCalls++;
        __result = (TEffect)Effect;
        return false;
    }
}
