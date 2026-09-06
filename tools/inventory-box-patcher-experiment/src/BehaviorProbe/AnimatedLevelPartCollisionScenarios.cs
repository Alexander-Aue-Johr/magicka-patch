using System;
using System.Reflection;
using System.Runtime.Serialization;
using Harmony;

internal static class AnimatedLevelPartCollisionScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        AnimatedLevelPartCollisionHarness harness =
            new AnimatedLevelPartCollisionHarness(magicka);
        try
        {
            report.Add(
                "animated_level_part.detached_entity",
                harness.DetachedEntity());
            report.Add(
                "animated_level_part.missing_entity",
                harness.MissingEntity());
            report.Add(
                "animated_level_part.expired_valid_entity",
                harness.ExpiredValidEntity());
        }
        finally
        {
            harness.Dispose();
        }
    }
}

internal sealed class AnimatedLevelPartCollisionHarness
{
    private readonly Type animatedLevelPartType;
    private readonly Type entityType;
    private readonly Type shieldType;
    private readonly Type bodyType;
    private readonly Type transformType;
    private readonly Type matrixType;
    private readonly Type dataChannelType;
    private readonly Type liquidType;
    private readonly FieldInfo collidingEntitiesField;
    private readonly FieldInfo bodyField;
    private readonly MethodInfo updateMethod;
    private readonly object identityMatrix;
    private readonly object movingMatrix;
    private readonly object identityTransform;
    private readonly PropertyInfo bodyTransformProperty;
    private readonly FieldInfo transformPositionField;
    private readonly FieldInfo vectorXField;
    private readonly object noDataChannel;
    private readonly HarmonyInstance harmony;

    internal AnimatedLevelPartCollisionHarness(Assembly magicka)
    {
        animatedLevelPartType = magicka.GetType(
            "Magicka.Levels.AnimatedLevelPart",
            true);
        entityType = magicka.GetType("Magicka.GameLogic.Entities.Entity", true);
        shieldType = magicka.GetType("Magicka.GameLogic.Entities.Shield", true);
        bodyType = RuntimeReflection.FindLoadedType("JigLibX.Physics.Body");
        transformType = RuntimeReflection.FindLoadedType("JigLibX.Math.Transform");
        matrixType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Matrix");
        dataChannelType = RuntimeReflection.FindLoadedType("PolygonHead.DataChannel");
        liquidType = magicka.GetType("Magicka.Levels.Liquid", true);
        Type gameSceneType = magicka.GetType("Magicka.Levels.GameScene", true);

        collidingEntitiesField = RequireField(
            animatedLevelPartType,
            "mCollidingEntities");
        bodyField = FindField(entityType, "mBody", bodyType);
        updateMethod = animatedLevelPartType.GetMethod(
            "Update",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                dataChannelType,
                typeof(float),
                matrixType.MakeByRefType(),
                gameSceneType
            },
            null);
        if (updateMethod == null || updateMethod.ReturnType != typeof(void))
            throw new MissingMethodException(animatedLevelPartType.FullName, "Update");

        MethodInfo getTransform = animatedLevelPartType.GetMethod(
            "GetTransform",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            new Type[] { matrixType.MakeByRefType() },
            null);
        if (getTransform == null || getTransform.ReturnType != typeof(void))
            throw new MissingMethodException(
                animatedLevelPartType.FullName,
                "GetTransform");

        MethodInfo getFromHandle = entityType.GetMethod(
            "GetFromHandle",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(int) },
            null);
        if (getFromHandle == null || getFromHandle.ReturnType != entityType)
            throw new MissingMethodException(entityType.FullName, "GetFromHandle");

        bodyTransformProperty = bodyType.GetProperty(
            "Transform",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo setTransform = bodyTransformProperty == null
            ? null
            : bodyTransformProperty.GetSetMethod();
        if (setTransform == null || setTransform.ReturnType != typeof(void) ||
            bodyTransformProperty.PropertyType != transformType)
            throw new MissingMethodException(bodyType.FullName, "set_Transform");
        transformPositionField = transformType.GetField(
            "Position",
            BindingFlags.Instance | BindingFlags.Public);
        if (transformPositionField == null)
            throw new MissingFieldException(transformType.FullName, "Position");
        vectorXField = transformPositionField.FieldType.GetField(
            "X",
            BindingFlags.Instance | BindingFlags.Public);
        if (vectorXField == null || vectorXField.FieldType != typeof(float))
            throw new MissingFieldException(
                transformPositionField.FieldType.FullName,
                "X");

        PropertyInfo identity = matrixType.GetProperty(
            "Identity",
            BindingFlags.Static | BindingFlags.Public);
        if (identity == null || identity.PropertyType != matrixType)
            throw new MissingMemberException(matrixType.FullName, "Identity");
        identityMatrix = identity.GetValue(null, null);
        MethodInfo createTranslation = matrixType.GetMethod(
            "CreateTranslation",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new Type[] { typeof(float), typeof(float), typeof(float) },
            null);
        if (createTranslation == null || createTranslation.ReturnType != matrixType)
            throw new MissingMethodException(matrixType.FullName, "CreateTranslation");
        movingMatrix = createTranslation.Invoke(
            null,
            new object[] { 5f, 0f, 0f });
        FieldInfo identityTransformField = transformType.GetField(
            "Identity",
            BindingFlags.Static | BindingFlags.Public);
        if (identityTransformField == null ||
            identityTransformField.FieldType != transformType)
            throw new MissingFieldException(transformType.FullName, "Identity");
        identityTransform = identityTransformField.GetValue(null);
        noDataChannel = Enum.Parse(dataChannelType, "None");
        AnimatedLevelPartCollisionProbe.PartTransformMatrix = movingMatrix;

        harmony = HarmonyInstance.Create(
            "org.magickacommunitypatch.behavior-probe-animated-level-part");
        harmony.Patch(
            getTransform,
            new HarmonyMethod(
                typeof(AnimatedLevelPartCollisionProbe)
                    .GetMethod("GetTransformPrefix")
                    .MakeGenericMethod(new Type[] { matrixType })),
            null,
            null);
        harmony.Patch(
            getFromHandle,
            new HarmonyMethod(
                typeof(AnimatedLevelPartCollisionProbe)
                    .GetMethod("GetFromHandlePrefix")
                    .MakeGenericMethod(new Type[] { entityType })),
            null,
            null);
        harmony.Patch(
            bodyField.DeclaringType.GetProperty(
                "Body",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly).GetGetMethod(),
            null,
            new HarmonyMethod(
                typeof(AnimatedLevelPartCollisionProbe)
                    .GetMethod("BodyGetterPostfix")
                    .MakeGenericMethod(new Type[] { bodyType })),
            null);
    }

    internal void Dispose()
    {
        harmony.UnpatchAll(
            "org.magickacommunitypatch.behavior-probe-animated-level-part");
    }

    internal ScenarioResult DetachedEntity()
    {
        object entity = NewUninitialized(shieldType);
        return RunScenario(entity, float.MaxValue, null, 1);
    }

    internal ScenarioResult MissingEntity()
    {
        return RunScenario(null, float.MaxValue, null, 0);
    }

    internal ScenarioResult ExpiredValidEntity()
    {
        object entity = NewUninitialized(shieldType);
        object body = NewUninitialized(bodyType);
        bodyTransformProperty.SetValue(body, identityTransform, null);
        bodyField.SetValue(entity, body);
        return RunScenario(entity, 0.05f, 5f, 1);
    }

    private ScenarioResult RunScenario(
        object entity,
        float ttl,
        float? expectedPositionX,
        int expectedBodyReads)
    {
        object part = CreatePart(ttl);
        AnimatedLevelPartCollisionProbe.Reset(entity);
        bool completed = false;
        string exceptionType = "none";
        try
        {
            object[] arguments = new object[]
            {
                noDataChannel,
                0.1f,
                identityMatrix,
                null
            };
            updateMethod.Invoke(part, arguments);
            completed = true;
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            exceptionType = inner.GetType().FullName;
        }

        object registrations = collidingEntitiesField.GetValue(part);
        int count = Convert.ToInt32(
            registrations.GetType().GetProperty("Count").GetValue(
                registrations,
                null));
        float? positionX = ReadBodyPositionX(entity);
        bool positionMatches = !expectedPositionX.HasValue ||
            (positionX.HasValue &&
                Math.Abs(positionX.Value - expectedPositionX.Value) < 0.001f);
        bool passed = completed && count == 0 && positionMatches &&
            AnimatedLevelPartCollisionProbe.BodyReads == expectedBodyReads;
        return new ScenarioResult(
            passed,
            "completed:" + completed + ",registrations:" + count +
                ",position_x:" +
                (positionX.HasValue ? positionX.Value.ToString() : "none") +
                ",body_reads:" + AnimatedLevelPartCollisionProbe.BodyReads +
                ",last_body_present:" +
                AnimatedLevelPartCollisionProbe.LastBodyPresent +
                ",exception:" + exceptionType,
            "completed:True,registrations:0" +
                ",position_x:" +
                (expectedPositionX.HasValue
                    ? expectedPositionX.Value.ToString()
                    : "none") +
                ",body_reads:" + expectedBodyReads +
                ",last_body_present:diagnostic" +
                ",exception:none");
    }

    private float? ReadBodyPositionX(object entity)
    {
        if (entity == null)
            return null;
        object body = bodyField.GetValue(entity);
        if (body == null)
            return null;
        object transform = bodyTransformProperty.GetValue(body, null);
        object position = transformPositionField.GetValue(transform);
        return Convert.ToSingle(vectorXField.GetValue(position));
    }

    private object CreatePart(float ttl)
    {
        object part = NewUninitialized(animatedLevelPartType);
        object registrations = Activator.CreateInstance(
            collidingEntitiesField.FieldType);
        registrations.GetType().GetMethod("Add").Invoke(
            registrations,
            new object[] { (ushort)7, ttl });
        collidingEntitiesField.SetValue(part, registrations);

        SetField(part, "mLights", new int[0]);
        SetField(part, "mOldTransform", identityMatrix);
        SetField(part, "mCollisionSkin", null);
        SetField(
            part,
            "mDecals",
            Activator.CreateInstance(RequireField(
                animatedLevelPartType,
                "mDecals").FieldType));
        SetField(part, "mNavMesh", null);
        SetField(part, "mLiquids", Array.CreateInstance(liquidType, 0));
        SetField(
            part,
            "mChildren",
            Activator.CreateInstance(RequireField(
                animatedLevelPartType,
                "mChildren").FieldType));
        SetField(part, "mAffectShields", true);
        SetField(part, "mHighlighted", -1f);
        return part;
    }

    private void SetField(object target, string name, object value)
    {
        RequireField(animatedLevelPartType, name).SetValue(target, value);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static FieldInfo FindField(
        Type type,
        string name,
        Type expectedType)
    {
        Type current = type;
        while (current != null)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                if (field.FieldType != expectedType)
                    throw new MissingFieldException(current.FullName, name);
                return field;
            }
            current = current.BaseType;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}

public static class AnimatedLevelPartCollisionProbe
{
    public static object CurrentEntity;
    public static object PartTransformMatrix;
    public static int BodyReads;
    public static bool LastBodyPresent;

    public static void Reset(object entity)
    {
        CurrentEntity = entity;
        BodyReads = 0;
        LastBodyPresent = false;
    }

    public static bool GetTransformPrefix<TMatrix>(ref TMatrix oTransform)
    {
        oTransform = (TMatrix)PartTransformMatrix;
        return false;
    }

    public static bool GetFromHandlePrefix<TEntity>(ref TEntity __result)
    {
        __result = (TEntity)CurrentEntity;
        return false;
    }

    public static void BodyGetterPostfix<TBody>(TBody __result)
    {
        BodyReads++;
        LastBodyPresent = (object)__result != null;
    }
}
