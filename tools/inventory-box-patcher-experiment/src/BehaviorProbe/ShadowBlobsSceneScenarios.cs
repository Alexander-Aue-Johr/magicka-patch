using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;
using Magicka.CommunityPatch.Runtime;

internal static class ShadowBlobsSceneScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ShadowBlobsSceneHarness harness = new ShadowBlobsSceneHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("shadow_blobs.matching_scene", harness.MatchingScene());
        report.Add("shadow_blobs.replacement_scene", harness.ReplacementScene());
    }
}

internal sealed class ShadowBlobsSceneHarness
{
    private readonly Type shadowBlobsType;
    private readonly Type sceneType;
    private readonly FieldInfo sceneField;
    private readonly MethodInfo detachMethod;

    internal ShadowBlobsSceneHarness(Assembly magicka, bool runtimePatchEnabled)
    {
        shadowBlobsType = magicka.GetType(
            "Magicka.GameLogic.UI.ShadowBlobs",
            true);
        sceneType = RuntimeReflection.FindLoadedType("PolygonHead.Scene");
        sceneField = shadowBlobsType.GetField(
            "mScene",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (sceneField == null || sceneField.FieldType != sceneType)
            throw new MissingFieldException(shadowBlobsType.FullName, "mScene");

        Type playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        MethodInfo dispose = playStateType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (dispose == null || dispose.ReturnType != typeof(void))
            throw new MissingMethodException(playStateType.FullName, "Dispose");
        List<CodeInstruction> instructions = Decode(dispose);

        if (runtimePatchEnabled)
        {
            Type patchType = typeof(Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.ShadowBlobsScenePatch",
                true);
            ApplyTranspiler(patchType, instructions);
        }
        detachMethod = FindDetachMethod(instructions);
    }

    internal ScenarioResult MatchingScene()
    {
        object shadowBlobs = NewUninitialized(shadowBlobsType);
        object scene = NewUninitialized(sceneType);
        sceneField.SetValue(shadowBlobs, scene);
        InvokeDetach(shadowBlobs, scene);
        object actual = sceneField.GetValue(shadowBlobs);
        return new ScenarioResult(
            actual == null,
            "scene_null:" + (actual == null),
            "scene_null:True");
    }

    internal ScenarioResult ReplacementScene()
    {
        object shadowBlobs = NewUninitialized(shadowBlobsType);
        object oldScene = NewUninitialized(sceneType);
        object replacement = NewUninitialized(sceneType);
        sceneField.SetValue(shadowBlobs, replacement);
        InvokeDetach(shadowBlobs, oldScene);
        bool retained = Object.ReferenceEquals(
            sceneField.GetValue(shadowBlobs),
            replacement);
        return new ScenarioResult(
            retained,
            "replacement_retained:" + retained,
            "replacement_retained:True");
    }

    private void InvokeDetach(object shadowBlobs, object expected)
    {
        if (detachMethod == null)
            return;
        object target = detachMethod.IsStatic ? null : shadowBlobs;
        object[] arguments = detachMethod.IsStatic
            ? new object[] { shadowBlobs, expected }
            : new object[] { expected };
        try
        {
            detachMethod.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "ShadowBlobsSceneDecode",
            typeof(void),
            Type.EmptyTypes,
            typeof(ShadowBlobsSceneHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static void ApplyTranspiler(
        Type patchType,
        List<CodeInstruction> instructions)
    {
        MethodInfo transpiler = patchType.GetMethod(
            "Transpiler",
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patchType.FullName, "Transpiler");
        object transformed = transpiler.Invoke(null, new object[] { instructions });
        instructions.Clear();
        instructions.AddRange((IEnumerable<CodeInstruction>)transformed);
    }

    private static MethodInfo FindDetachMethod(
        List<CodeInstruction> instructions)
    {
        MethodInfo match = null;
        for (int index = 0; index < instructions.Count; index++)
        {
            if (instructions[index].opcode != OpCodes.Call &&
                instructions[index].opcode != OpCodes.Callvirt)
                continue;
            MethodInfo method = instructions[index].operand as MethodInfo;
            if (!IsDetachMethod(method))
                continue;
            if (match != null)
                throw new InvalidOperationException(
                    "Multiple ShadowBlobs detach calls were found.");
            match = method;
        }
        return match;
    }

    private static bool IsDetachMethod(MethodInfo method)
    {
        if (method == null || method.ReturnType != typeof(void))
            return false;
        ParameterInfo[] parameters = method.GetParameters();
        if (method.Name == "CommunityPatchDetachScene" && !method.IsStatic)
            return parameters.Length == 1;
        return method.Name == "DetachScene" && method.IsStatic &&
            parameters.Length == 2;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
