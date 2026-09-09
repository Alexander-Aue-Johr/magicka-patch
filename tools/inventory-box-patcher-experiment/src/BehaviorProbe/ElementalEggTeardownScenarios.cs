using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ElementalEggTeardownScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ElementalEggTeardownHarness harness =
            new ElementalEggTeardownHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "elemental_egg_teardown.retained_graph",
            harness.RetainedGraph());
        report.Add(
            "elemental_egg_teardown.empty",
            harness.Empty());
    }
}

internal sealed class ElementalEggTeardownHarness
{
    private const string RuntimePatchTypeName =
        "Magicka.CommunityPatch.Runtime.ElementalEggTeardownPatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly Type eggType;
    private readonly MethodInfo manualDispose;
    private readonly FieldInfo controllerField;
    private readonly FieldInfo damageMemoryField;
    private readonly FieldInfo renderDataField;
    private readonly FieldInfo renderBonesField;
    private readonly FieldInfo renderVertexDeclarationField;
    private readonly FieldInfo summonerField;
    private readonly FieldInfo modelField;
    private readonly FieldInfo clipField;

    internal ElementalEggTeardownHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        eggType = magicka.GetType(
            "Magicka.GameLogic.Entities.ElementalEgg",
            true);
        Type renderData = eggType.GetNestedType(
            "RenderData",
            BindingFlags.Public | BindingFlags.NonPublic);
        if (renderData == null)
            throw new TypeLoadException(eggType.FullName + "+RenderData");

        manualDispose = eggType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        controllerField = RequireField(eggType, "mController");
        damageMemoryField = RequireField(eggType, "mDamageMemory");
        renderDataField = RequireField(eggType, "mRenderData");
        renderBonesField = RequireField(renderData, "mBones");
        renderVertexDeclarationField = RequireField(
            renderData,
            "mVertexDeclaration");
        summonerField = RequireField(eggType, "mSummoner");
        modelField = RequireField(eggType, "mModel");
        clipField = RequireField(eggType, "mClip");
    }

    internal ScenarioResult RetainedGraph()
    {
        object egg = NewUninitialized(eggType);
        object controller = Activator.CreateInstance(controllerField.FieldType);
        IDictionary damageMemory =
            (IDictionary)Activator.CreateInstance(damageMemoryField.FieldType);
        object data = NewUninitialized(
            renderDataField.FieldType.GetElementType());
        Array bones = Array.CreateInstance(
            renderBonesField.FieldType.GetElementType(),
            1);
        object vertexDeclaration = NewUninitialized(
            renderVertexDeclarationField.FieldType);
        Array renderData = Array.CreateInstance(data.GetType(), 1);
        object summoner = NewUninitialized(
            eggType.Assembly.GetType(
                "Magicka.GameLogic.Entities.Avatar",
                true));
        object model = NewUninitialized(modelField.FieldType);
        object clip = NewUninitialized(clipField.FieldType);

        controllerField.SetValue(egg, controller);
        damageMemoryField.SetValue(egg, damageMemory);
        renderBonesField.SetValue(data, bones);
        renderVertexDeclarationField.SetValue(data, vertexDeclaration);
        renderData.SetValue(data, 0);
        renderDataField.SetValue(egg, renderData);
        summonerField.SetValue(egg, summoner);
        modelField.SetValue(egg, model);
        clipField.SetValue(egg, clip);

        Cleanup(egg);

        bool released =
            controllerField.GetValue(egg) == null &&
            damageMemoryField.GetValue(egg) == null &&
            damageMemory.Count == 0 &&
            renderDataField.GetValue(egg) == null &&
            renderBonesField.GetValue(data) == null &&
            renderVertexDeclarationField.GetValue(data) == null &&
            summonerField.GetValue(egg) == null &&
            modelField.GetValue(egg) == null &&
            clipField.GetValue(egg) == null;
        string actual = released ? "released" : "retained";
        return new ScenarioResult(
            released,
            actual,
            "released");
    }

    internal ScenarioResult Empty()
    {
        object egg = NewUninitialized(eggType);
        Cleanup(egg);
        Cleanup(egg);
        bool empty = controllerField.GetValue(egg) == null &&
            damageMemoryField.GetValue(egg) == null &&
            renderDataField.GetValue(egg) == null &&
            summonerField.GetValue(egg) == null &&
            modelField.GetValue(egg) == null &&
            clipField.GetValue(egg) == null;
        string actual = empty ? "empty" : "retained";
        return new ScenarioResult(empty, actual, "empty");
    }

    private void Cleanup(object egg)
    {
        if (manualDispose != null)
        {
            Invoke(manualDispose, egg);
            return;
        }
        if (!runtimePatchEnabled)
            return;

        Type patch = Type.GetType(RuntimePatchTypeName, false);
        MethodInfo cleanup = patch == null
            ? null
            : patch.GetMethod(
                "CleanupFinal",
                BindingFlags.Static | BindingFlags.Public);
        if (cleanup == null)
            throw new MissingMethodException(
                RuntimePatchTypeName,
                "CleanupFinal");
        Invoke(cleanup, null, egg);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        for (Type current = type; current != null;
            current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static object Invoke(
        MethodInfo method,
        object target,
        params object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
