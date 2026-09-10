using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;
using Harmony.ILCopying;

internal static class HighResolutionUiRenderScenarios
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic;

    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The legacy executable predates high-resolution UI scaling.";
            report.AddNotApplicable("ui_render.activation", reason);
            report.AddNotApplicable("ui_render.projected_positions", reason);
            report.AddNotApplicable("ui_render.notifier_restoration", reason);
            report.AddNotApplicable("ui_render.screen_size", reason);
            return;
        }
        HighResolutionUiRenderHarness harness =
            new HighResolutionUiRenderHarness(magicka, runtimePatchEnabled);
        report.Add("ui_render.activation", harness.Activation());
        report.Add(
            "ui_render.projected_positions",
            harness.ProjectedPositions());
        report.Add(
            "ui_render.notifier_restoration",
            harness.NotifierRestoration());
        report.Add("ui_render.screen_size", harness.ScreenSize());
    }
}

internal sealed class HighResolutionUiRenderHarness
{
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
        BindingFlags.NonPublic;

    private readonly Assembly magicka;
    private readonly bool runtimePatchEnabled;
    private readonly Type scaleType;
    private readonly Type vectorType;
    private readonly MethodInfo adjust;
    private readonly MethodInfo adjustOffset;
    private readonly Type runtimePatchType;

    internal HighResolutionUiRenderHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.magicka = magicka;
        this.runtimePatchEnabled = runtimePatchEnabled;
        scaleType = Type.GetType(
            "PolygonHead.CommunityPatch.InGameUiRenderScale, PolygonHead",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector2");
        adjust = scaleType.GetMethod(
            "AdjustProjectedPosition",
            Members,
            null,
            new Type[] { vectorType.MakeByRefType() },
            null);
        adjustOffset = scaleType.GetMethod(
            "AdjustProjectedPosition",
            Members,
            null,
            new Type[] { vectorType.MakeByRefType(), vectorType },
            null);
        runtimePatchType = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.HighResolutionUiRenderPatch",
                true);
    }

    internal ScenarioResult Activation()
    {
        bool passed;
        if (runtimePatchEnabled)
        {
            MethodInfo set = runtimePatchType.GetMethod(
                "SetRenderState",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo enabled = scaleType.GetMethod(
                "get_Enabled",
                BindingFlags.Static | BindingFlags.Public);
            set.Invoke(null, new object[] { true });
            bool active = (bool)enabled.Invoke(null, null);
            set.Invoke(null, new object[] { false });
            passed = active && !(bool)enabled.Invoke(null, null);
        }
        else
        {
            MethodInfo draw = FindGameDraw();
            passed = CountNamedCalls(draw, "ApplyScaleSetting") == 1 &&
                CountCalls(draw, "SetEnabled") == 1;
        }
        return BoolResult(passed);
    }

    internal ScenarioResult ProjectedPositions()
    {
        bool passed;
        if (runtimePatchEnabled)
        {
            ActivateScale();
            bool scaled = TestProjection(false) && TestProjection(true) &&
                PrefixCallsAdjust("TextBoxPrefix") &&
                PrefixCallsAdjust("CutscenePrefix") &&
                PrefixCallsAdjust("ProjectedPositionPrefix") &&
                PrefixCallsAdjust("NotifierPrefix");
            DeactivateScale();
            passed = scaled && TestUnscaledProjection();
        }
        else
        {
            passed = CountProjectionCalls() == 5;
        }
        return BoolResult(passed);
    }

    internal ScenarioResult NotifierRestoration()
    {
        bool passed;
        if (runtimePatchEnabled)
        {
            ActivateScale();
            object instance = NewProjectionCarrier(true);
            FieldInfo position = RequireField(instance.GetType(), "mPosition");
            object before = position.GetValue(instance);
            InvokeAdjust(instance, RequireField(
                instance.GetType(), "mOffset").GetValue(instance));
            runtimePatchType.GetMethod(
                "NotifierPostfix",
                BindingFlags.Static | BindingFlags.Public).Invoke(
                    null,
                    new object[] { instance, before });
            passed = VectorEquals(before, position.GetValue(instance)) &&
                PrefixCallsAdjust("NotifierPrefix") &&
                CountNamedCalls(
                    runtimePatchType.GetMethod(
                        "NotifierPostfix",
                        BindingFlags.Static | BindingFlags.Public),
                    "SetValue") == 1;
            DeactivateScale();
        }
        else
        {
            Type renderData = Nested(
                "Magicka.Graphics.NotifierButton", "RenderData");
            passed = Count(
                Decode(Draw(renderData)),
                OpCodes.Stfld,
                RequireField(renderData, "mPosition")) == 1;
        }
        return BoolResult(passed);
    }

    internal ScenarioResult ScreenSize()
    {
        bool passed;
        if (runtimePatchEnabled)
        {
            MethodInfo helper = runtimePatchType.GetMethod(
                "SetEffectScreenSize",
                BindingFlags.Static | BindingFlags.NonPublic);
            passed = CountCalls(
                    runtimePatchType.GetMethod(
                        "TextBoxPrefix",
                        BindingFlags.Static | BindingFlags.Public),
                    helper) == 1 &&
                CountCalls(
                    runtimePatchType.GetMethod(
                        "CutscenePrefix",
                        BindingFlags.Static | BindingFlags.Public),
                    helper) == 1;
        }
        else
        {
            passed = CountPropertySetters(
                    Draw(Nested("Magicka.Graphics.TextBox", "RenderData")),
                    "ScreenSize") == 1 &&
                CountPropertySetters(
                    Draw(Nested(
                        "Magicka.Graphics.CutsceneText",
                        "CutSceneRenderData")),
                    "ScreenSize") == 1;
        }
        return BoolResult(passed);
    }

    private bool TestProjection(bool notifier)
    {
        object instance = NewProjectionCarrier(notifier);
        FieldInfo position = RequireField(instance.GetType(), "mPosition");
        object offset = notifier
            ? RequireField(instance.GetType(), "mOffset").GetValue(instance)
            : null;
        InvokeAdjust(instance, offset);
        object value = position.GetValue(instance);
        float x = (float)RequireField(vectorType, "X").GetValue(value);
        float y = (float)RequireField(vectorType, "Y").GetValue(value);
        return notifier
            ? Math.Abs(x - 55f) < 0.001f && Math.Abs(y - 30f) < 0.001f
            : Math.Abs(x - 50f) < 0.001f && Math.Abs(y - 25f) < 0.001f;
    }

    private bool TestUnscaledProjection()
    {
        object instance = NewProjectionCarrier(false);
        FieldInfo position = RequireField(instance.GetType(), "mPosition");
        InvokeAdjust(instance, null);
        object value = position.GetValue(instance);
        float x = (float)RequireField(vectorType, "X").GetValue(value);
        float y = (float)RequireField(vectorType, "Y").GetValue(value);
        return Math.Abs(x - 100f) < 0.001f &&
            Math.Abs(y - 50f) < 0.001f;
    }

    private object NewProjectionCarrier(bool notifier)
    {
        object instance = new ProjectionCarrier();
        RequireField(instance.GetType(), "mPosition").SetValue(
            instance,
            Activator.CreateInstance(vectorType, new object[] { 100f, 50f }));
        if (notifier)
            RequireField(instance.GetType(), "mOffset").SetValue(
                instance,
                Activator.CreateInstance(vectorType, new object[] { 10f, 10f }));
        return instance;
    }

    private void InvokeAdjust(object instance, object offset)
    {
        runtimePatchType.GetMethod(
            "Adjust",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(
                null,
                new object[] { instance, offset });
    }

    private bool PrefixCallsAdjust(string prefixName)
    {
        MethodInfo prefix = runtimePatchType.GetMethod(
            prefixName,
            BindingFlags.Static | BindingFlags.Public);
        return CountNamedCalls(prefix, "Adjust") == 1;
    }

    private int CountProjectionCalls()
    {
        int count = 0;
        count += Count(Decode(Draw(Nested("Magicka.Graphics.TextBox", "RenderData"))), OpCodes.Call, adjust);
        count += Count(Decode(Draw(Nested("Magicka.Graphics.CutsceneText", "CutSceneRenderData"))), OpCodes.Call, adjust);
        count += Count(Decode(Draw(Nested("Magicka.GameLogic.UI.IconRenderer", "RenderData"))), OpCodes.Call, adjust);
        count += Count(Decode(Draw(Nested("Magicka.GameLogic.UI.SpellWheel", "RenderData"))), OpCodes.Call, adjust);
        count += Count(Decode(Draw(Nested("Magicka.Graphics.NotifierButton", "RenderData"))), OpCodes.Call, adjustOffset);
        return count;
    }

    private void ActivateScale()
    {
        scaleType.GetField("sActive", Members).SetValue(null, true);
        scaleType.GetField("sRenderThreadId", Members).SetValue(
            null,
            Thread.CurrentThread.ManagedThreadId);
        scaleType.GetField("sScaleFactor", Members).SetValue(null, 2f);
    }

    private void DeactivateScale()
    {
        scaleType.GetField("sActive", Members).SetValue(null, false);
        scaleType.GetField("sRenderThreadId", Members).SetValue(null, -1);
    }

    private MethodInfo FindGameDraw()
    {
        Type gameTime = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.GameTime");
        return magicka.GetType("Magicka.Game", true).GetMethod(
            "Draw",
            Members,
            null,
            new Type[] { gameTime },
            null);
    }

    private Type Nested(string owner, string name)
    {
        return magicka.GetType(owner, true).GetNestedType(
            name,
            BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static MethodInfo Draw(Type type)
    {
        return type.GetMethod(
            "Draw",
            Members,
            null,
            new Type[] { typeof(float) },
            null);
    }

    private int CountCalls(MethodBase method, string name)
    {
        List<CodeInstruction> body = Decode(method);
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodInfo called = body[index].operand as MethodInfo;
            if (called != null && called.DeclaringType == scaleType &&
                called.Name == name)
                count++;
        }
        return count;
    }

    private static int CountNamedCalls(MethodBase method, string name)
    {
        List<CodeInstruction> body = Decode(method);
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodInfo called = body[index].operand as MethodInfo;
            if (called != null && called.Name == name)
                count++;
        }
        return count;
    }

    private static int CountCalls(MethodBase method, MethodInfo called)
    {
        return Count(Decode(method), OpCodes.Call, called);
    }

    private static int CountPropertySetters(
        MethodBase method,
        string propertyName)
    {
        List<CodeInstruction> body = Decode(method);
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            MethodInfo called = body[index].operand as MethodInfo;
            if (called != null && called.Name == "set_" + propertyName)
                count++;
        }
        return count;
    }

    private static int Count(
        List<CodeInstruction> body,
        OpCode opcode,
        object operand)
    {
        int count = 0;
        for (int index = 0; index < body.Count; index++)
        {
            if (body[index].opcode == opcode &&
                Object.Equals(body[index].operand, operand))
                count++;
        }
        return count;
    }

    private static bool VectorEquals(object left, object right)
    {
        FieldInfo x = RequireField(left.GetType(), "X");
        FieldInfo y = RequireField(left.GetType(), "Y");
        return Object.Equals(x.GetValue(left), x.GetValue(right)) &&
            Object.Equals(y.GetValue(left), y.GetValue(right));
    }

    private static ScenarioResult BoolResult(bool value)
    {
        return new ScenarioResult(
            value,
            "implemented:" + value.ToString().ToLowerInvariant(),
            "implemented:true");
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, Members);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadHighResolutionUiBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(HighResolutionUiRenderHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private sealed class ProjectionCarrier
    {
#pragma warning disable 169, 649
        private object mPosition;
        private object mOffset;
#pragma warning restore 169, 649
    }
}
