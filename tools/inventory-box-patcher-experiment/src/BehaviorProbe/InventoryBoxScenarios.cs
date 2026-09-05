using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class InventoryBoxScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        InventoryBoxHarness harness = InventoryBoxHarness.Create(magicka);
        report.Add("inventory.initial_screen_size", harness.DrawAt(1920, 1080));
        report.Add(
            "inventory.changed_screen_size",
            harness.DrawAfterResize(1280, 720, 2560, 1440));
    }
}

internal sealed class InventoryBoxHarness
{
    private readonly Type renderDataType;
    private readonly Type textBoxEffectType;
    private readonly Type renderManagerType;

    private InventoryBoxHarness(Assembly magicka)
    {
        Type inventoryBox = magicka.GetType("Magicka.GameLogic.UI.InventoryBox", true);
        renderDataType = inventoryBox.GetNestedType(
            "RenderData",
            BindingFlags.Public | BindingFlags.NonPublic);
        textBoxEffectType = magicka.GetType("Magicka.Graphics.Effects.TextBoxEffect", true);
        renderManagerType = RuntimeReflection.FindLoadedType("PolygonHead.RenderManager");
    }

    internal static InventoryBoxHarness Create(Assembly magicka)
    {
        magicka.GetType("Magicka.GameLogic.UI.InventoryBox", true);
        Assembly.Load("PolygonHead");
        return new InventoryBoxHarness(magicka);
    }

    internal ScenarioResult DrawAt(int width, int height)
    {
        object textBoxEffect = FormatterServices.GetUninitializedObject(textBoxEffectType);
        object renderData = FormatterServices.GetUninitializedObject(renderDataType);
        RuntimeReflection.WriteField(renderData, "mTextBoxEffect", textBoxEffect);
        return DrawAt(renderData, textBoxEffect, width, height);
    }

    internal ScenarioResult DrawAfterResize(
        int firstWidth,
        int firstHeight,
        int secondWidth,
        int secondHeight)
    {
        object textBoxEffect = FormatterServices.GetUninitializedObject(textBoxEffectType);
        object renderData = FormatterServices.GetUninitializedObject(renderDataType);
        RuntimeReflection.WriteField(renderData, "mTextBoxEffect", textBoxEffect);
        DrawAt(renderData, textBoxEffect, firstWidth, firstHeight);
        return DrawAt(renderData, textBoxEffect, secondWidth, secondHeight);
    }

    private ScenarioResult DrawAt(
        object renderData,
        object textBoxEffect,
        int width,
        int height)
    {
        object renderManager = CreateRenderManager(width, height);
        object expectedScreenSize = RuntimeReflection.ReadProperty(renderManager, "ScreenSize");
        InvokeUntilFirstEffectWrite(renderData);
        object actualScreenSize = RuntimeReflection.ReadField(textBoxEffect, "mScreenSize");
        string expected = RuntimeReflection.Coordinates(expectedScreenSize);
        string actual = RuntimeReflection.Coordinates(actualScreenSize);
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private object CreateRenderManager(int width, int height)
    {
        object renderManager = FormatterServices.GetUninitializedObject(renderManagerType);
        FieldInfo sizeField = RuntimeReflection.RequireField(renderManagerType, "mSize");
        object size = Activator.CreateInstance(sizeField.FieldType);
        RuntimeReflection.WriteField(size, "X", width);
        RuntimeReflection.WriteField(size, "Y", height);
        sizeField.SetValue(renderManager, size);
        RuntimeReflection.RequireField(renderManagerType, "mSingelton").SetValue(null, renderManager);
        return renderManager;
    }

    private void InvokeUntilFirstEffectWrite(object renderData)
    {
        MethodInfo draw = renderDataType.GetMethod(
            "Draw",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        try
        {
            draw.Invoke(renderData, new object[] { 0.016f });
        }
        catch (TargetInvocationException)
        {
        }
    }
}
