using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class AnimatedLevelPartDisposeScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        AnimatedLevelPartDisposeHarness harness =
            new AnimatedLevelPartDisposeHarness(magicka, runtimePatchEnabled);
        report.Add(
            "animated_level_part.dispose_idempotent",
            harness.DisposeIdempotent());
        report.Add(
            "animated_level_part.dispose_children",
            harness.DisposeChildren());
        report.Add(
            "animated_level_part.dispose_liquid",
            harness.DisposeLiquid());
    }
}

internal sealed class AnimatedLevelPartDisposeHarness
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly Type partType;
    private readonly Type liquidType;
    private readonly Type waterType;
    private readonly bool runtimePatchEnabled;
    private readonly MethodInfo disposeMethod;
    private readonly FieldInfo childrenField;
    private readonly FieldInfo collidingEntitiesField;
    private readonly FieldInfo levelField;

    internal AnimatedLevelPartDisposeHarness(
        Assembly magicka,
        bool applyRuntimePatch)
    {
        partType = magicka.GetType("Magicka.Levels.AnimatedLevelPart", true);
        liquidType = magicka.GetType("Magicka.Levels.Liquid", true);
        waterType = magicka.GetType("Magicka.Levels.Water", true);
        runtimePatchEnabled = applyRuntimePatch;
        disposeMethod = partType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (disposeMethod == null || disposeMethod.ReturnType != typeof(void))
            throw new MissingMethodException(partType.FullName, "Dispose");
        childrenField = RequireField("mChildren");
        collidingEntitiesField = RequireField("mCollidingEntities");
        levelField = RequireField("mLevel");
    }

    internal ScenarioResult DisposeIdempotent()
    {
        object part = CreatePart();
        string exceptionType = InvokeDispose(part, true);
        return Result(part, exceptionType, false, true);
    }

    internal ScenarioResult DisposeChildren()
    {
        object child = CreatePart();
        object parent = CreatePart();
        object children = childrenField.GetValue(parent);
        children.GetType().GetMethod("Add").Invoke(
            children,
            new object[] { 17, child });

        string exceptionType = InvokeDispose(parent, false);
        bool childReleased = childrenField.GetValue(child) == null &&
            levelField.GetValue(child) == null;
        return Result(parent, exceptionType, true, childReleased);
    }

    internal ScenarioResult DisposeLiquid()
    {
        object part = CreatePart();
        object liquid = FormatterServices.GetUninitializedObject(waterType);
        FieldInfo liquidRenderData = RequireField(waterType, "mRenderData");
        FieldInfo freezeVertices = RequireField(
            waterType,
            "mWaterFreezeVertices");
        liquidRenderData.SetValue(
            liquid,
            Array.CreateInstance(
                liquidRenderData.FieldType.GetElementType(),
                1));
        freezeVertices.SetValue(liquid, new float[] { 1f });
        SetIfPresent(liquid, "mCollisionSkin", null);
        SetIfPresent(liquid, "mVertexDeclaration", null);
        SetIfPresent(liquid, "mWaterFreezeVertexBuffer", null);
        SetIfPresent(liquid, "mWaterIndices", null);
        SetIfPresent(liquid, "mWaterVertices", null);
        SetIfPresent(liquid, "mIceCollisionMesh", null);
        SetIfPresent(liquid, "mWaterCollisionMesh", null);

        Type effectType = RuntimeReflection.FindLoadedType(
            "PolygonHead.Effects.RenderDeferredLiquidEffect");
        object effect = FormatterServices.GetUninitializedObject(effectType);
        GC.SuppressFinalize(effect);
        FieldInfo effectField = liquidType.GetField(
            "effect",
            InstanceFields);
        if (effectField != null)
            effectField.SetValue(liquid, effect);
        if (runtimePatchEnabled)
            Magicka.CommunityPatch.Runtime.AnimatedLevelPartDisposePatch
                .RecordLiquidEffectPostfix(liquid, effect);

        Array liquids = Array.CreateInstance(liquidType, 1);
        liquids.SetValue(liquid, 0);
        RequireField("mLiquids").SetValue(part, liquids);
        string exceptionType = InvokeDispose(part, false);
        bool liquidReleased =
            liquidRenderData.GetValue(liquid) == null &&
            freezeVertices.GetValue(liquid) == null;
        bool effectDisposed = ReadBooleanProperty(effect, "IsDisposed");
        bool passed = exceptionType == "none" &&
            RequireField("mLiquids").GetValue(part) == null &&
            liquidReleased && effectDisposed;
        string detail =
            "completed:" + (exceptionType == "none") +
            ",liquids:" +
            (RequireField("mLiquids").GetValue(part) == null
                ? "null"
                : "present") +
            ",liquid_resources:" +
            (liquidReleased ? "null" : "present") +
            ",effect_disposed:" + effectDisposed +
            ",exception:" + exceptionType;
        return new ScenarioResult(
            passed,
            detail,
            "completed:True,liquids:null,liquid_resources:null," +
                "effect_disposed:True,exception:none");
    }

    private object CreatePart()
    {
        object part = FormatterServices.GetUninitializedObject(partType);
        GC.SuppressFinalize(part);
        SetField(part, "mCollisionSkin", null);
        SetField(part, "mLiquids", Array.CreateInstance(liquidType, 0));
        SetField(part, "mNavMesh", null);
        SetField(part, "mModel", null);
        SetField(part, "mChildren", Activator.CreateInstance(childrenField.FieldType));
        object level = FormatterServices.GetUninitializedObject(
            levelField.FieldType);
        GC.SuppressFinalize(level);
        SetField(part, "mLevel", level);
        SetField(part, "mAdditiveRenderData", null);
        SetField(part, "mDefaultRenderData", null);
        SetField(part, "mHighlightRenderData", null);

        object collisions = Activator.CreateInstance(
            collidingEntitiesField.FieldType);
        collisions.GetType().GetMethod("Add").Invoke(
            collisions,
            new object[] { (ushort)7, 1f });
        SetField(part, "mCollidingEntities", collisions);
        FieldInfo decals = RequireField("mDecals");
        SetField(part, "mDecals", Activator.CreateInstance(decals.FieldType));
        return part;
    }

    private string InvokeDispose(object part, bool twice)
    {
        try
        {
            disposeMethod.Invoke(part, null);
            if (twice)
                disposeMethod.Invoke(part, null);
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
    }

    private ScenarioResult Result(
        object part,
        string exceptionType,
        bool includeChild,
        bool childReleased)
    {
        object collisions = collidingEntitiesField.GetValue(part);
        int collisionCount = collisions == null
            ? -1
            : Convert.ToInt32(collisions.GetType().GetProperty("Count")
                .GetValue(collisions, null));
        bool levelReleased = levelField.GetValue(part) == null;
        bool childrenReleased = childrenField.GetValue(part) == null;
        bool liquidsReleased = RequireField("mLiquids").GetValue(part) == null;
        bool renderDataReleased =
            RequireField("mAdditiveRenderData").GetValue(part) == null &&
            RequireField("mDefaultRenderData").GetValue(part) == null &&
            RequireField("mHighlightRenderData").GetValue(part) == null;
        bool completed = exceptionType == "none";
        bool passed = completed && levelReleased && childrenReleased &&
            liquidsReleased && collisionCount == 0 && renderDataReleased &&
            childReleased;
        string detail =
            "completed:" + completed +
            ",level:" + (levelReleased ? "null" : "present") +
            ",children:" + (childrenReleased ? "null" : "present") +
            ",liquids:" + (liquidsReleased ? "null" : "present") +
            ",collisions:" + collisionCount +
            ",render_data:" + (renderDataReleased ? "null" : "present");
        string expected =
            "completed:True,level:null,children:null,liquids:null," +
            "collisions:0,render_data:null";
        if (includeChild)
        {
            detail += ",child_released:" + childReleased;
            expected += ",child_released:True";
        }
        detail += ",exception:" + exceptionType;
        expected += ",exception:none";
        return new ScenarioResult(passed, detail, expected);
    }

    private void SetField(object target, string name, object value)
    {
        RequireField(name).SetValue(target, value);
    }

    private FieldInfo RequireField(string name)
    {
        FieldInfo field = partType.GetField(name, InstanceFields);
        if (field == null)
            throw new MissingFieldException(partType.FullName, name);
        return field;
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, InstanceFields);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static void SetIfPresent(object target, string name, object value)
    {
        RequireField(target.GetType(), name).SetValue(target, value);
    }

    private static bool ReadBooleanProperty(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public);
        if (property == null || property.PropertyType != typeof(bool))
            throw new MissingMemberException(target.GetType().FullName, name);
        return (bool)property.GetValue(target, null);
    }
}
