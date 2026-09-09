using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class LevelModelTeardownScenarios
{
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public |
        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type levelModelType = magicka.GetType(
            "Magicka.Levels.LevelModel",
            true);
        MethodInfo dispose = levelModelType.GetMethod(
            "Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (dispose == null || dispose.ReturnType != typeof(void))
            throw new MissingMethodException(levelModelType.FullName, "Dispose");

        object levelModel = FormatterServices.GetUninitializedObject(
            levelModelType);
        GC.SuppressFinalize(levelModel);
        object collisionSkin = Populate(levelModelType, levelModel);

        string exception = InvokeTwice(dispose, levelModel);
        string[] fields = new string[]
        {
            "mCollisionSkin",
            "mLights",
            "mAnimatedLevelParts",
            "mWaters",
            "mForceFields",
            "mModel",
            "mCameraMesh",
            "mNavMesh",
            "mTriggerAreas",
            "mLocators",
            "mEffects",
            "mPhysEntities"
        };
        int retained = 0;
        for (int index = 0; index < fields.Length; index++)
        {
            if (RequireField(levelModelType, fields[index])
                .GetValue(levelModel) != null)
                retained++;
        }

        PropertyInfo tag = collisionSkin.GetType().GetProperty(
            "Tag",
            BindingFlags.Instance | BindingFlags.Public);
        bool tagReleased = tag != null &&
            tag.GetValue(collisionSkin, null) == null;
        bool passed = exception == "none" && retained == 0 && tagReleased;
        report.Add(
            "level_model.dispose_complete",
            new ScenarioResult(
                passed,
                "completed:" + (exception == "none") +
                    ",retained_fields:" + retained +
                    ",skin_tag:" + (tagReleased ? "null" : "present") +
                    ",exception:" + exception,
                "completed:True,retained_fields:0,skin_tag:null," +
                    "exception:none"));
    }

    private static object Populate(Type type, object levelModel)
    {
        SetCollection(type, levelModel, "mLights");
        SetCollection(type, levelModel, "mAnimatedLevelParts");
        SetArray(type, levelModel, "mWaters");
        SetArray(type, levelModel, "mForceFields");
        SetArray(type, levelModel, "mEffects");
        SetArray(type, levelModel, "mPhysEntities");
        SetCollection(type, levelModel, "mTriggerAreas");
        SetCollection(type, levelModel, "mLocators");
        SetSentinel(type, levelModel, "mNavMesh");
        SetSentinel(type, levelModel, "mCameraMesh");

        FieldInfo skinField = RequireField(type, "mCollisionSkin");
        object skin = FormatterServices.GetUninitializedObject(
            skinField.FieldType);
        PropertyInfo tag = skinField.FieldType.GetProperty(
            "Tag",
            BindingFlags.Instance | BindingFlags.Public);
        if (tag == null || !tag.CanWrite)
            throw new MissingMemberException(skinField.FieldType.FullName, "Tag");
        tag.SetValue(skin, levelModel, null);
        skinField.SetValue(levelModel, skin);

        // A partially initialized content object can reach the finalizer before
        // its XNA model has been assigned.
        RequireField(type, "mModel").SetValue(levelModel, null);
        return skin;
    }

    private static void SetCollection(Type type, object target, string name)
    {
        FieldInfo field = RequireField(type, name);
        object collection = Activator.CreateInstance(field.FieldType);
        if (!(collection is IEnumerable))
            throw new InvalidOperationException(name + " is not a collection.");
        field.SetValue(target, collection);
    }

    private static void SetArray(Type type, object target, string name)
    {
        FieldInfo field = RequireField(type, name);
        Type elementType = field.FieldType.GetElementType();
        if (elementType == null)
            throw new InvalidOperationException(name + " is not an array.");
        field.SetValue(target, Array.CreateInstance(elementType, 1));
    }

    private static void SetSentinel(Type type, object target, string name)
    {
        FieldInfo field = RequireField(type, name);
        object value = FormatterServices.GetUninitializedObject(field.FieldType);
        GC.SuppressFinalize(value);
        field.SetValue(target, value);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(name, InstanceFields);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static string InvokeTwice(MethodInfo method, object target)
    {
        try
        {
            method.Invoke(target, null);
            method.Invoke(target, null);
            return "none";
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return inner.GetType().FullName;
        }
        catch (Exception exception)
        {
            return exception.GetType().FullName;
        }
    }
}
