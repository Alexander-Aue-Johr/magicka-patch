using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class CharacterTemplateCacheScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        if (templateType.GetField(
            "sCachedAvatarTemplates",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null)
        {
            const string reason =
                "The paired avatar template cache is not present in this Magicka version";
            report.AddNotApplicable(
                "character_template_cache.shared_template",
                reason);
            report.AddNotApplicable(
                "character_template_cache.empty",
                reason);
            return;
        }
        CharacterTemplateCacheHarness harness =
            new CharacterTemplateCacheHarness(magicka);
        report.Add(
            "character_template_cache.shared_template",
            harness.SharedTemplate());
        report.Add(
            "character_template_cache.empty",
            harness.Empty());
    }
}

internal sealed class CharacterTemplateCacheHarness
{
    private readonly Type templateType;
    private readonly MethodInfo clearCache;
    private readonly FieldInfo templateCacheField;
    private readonly FieldInfo avatarCacheField;
    private readonly FieldInfo modelsField;
    private readonly FieldInfo gibsField;

    internal CharacterTemplateCacheHarness(Assembly magicka)
    {
        templateType = magicka.GetType(
            "Magicka.GameLogic.Entities.CharacterTemplate",
            true);
        clearCache = templateType.GetMethod(
            "ClearCache",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (clearCache == null || clearCache.ReturnType != typeof(void))
            throw new MissingMethodException(templateType.FullName, "ClearCache");
        templateCacheField = RequireDictionary(
            templateType,
            "mCachedTemplates");
        avatarCacheField = RequireDictionary(
            templateType,
            "sCachedAvatarTemplates");
        modelsField = RequireArrayField(templateType, "mModels");
        gibsField = RequireArrayField(templateType, "mGibs");
    }

    internal ScenarioResult SharedTemplate()
    {
        object template = NewUninitialized(templateType);
        Array models = Array.CreateInstance(
            modelsField.FieldType.GetElementType(),
            1);
        Array gibs = Array.CreateInstance(
            gibsField.FieldType.GetElementType(),
            0);
        modelsField.SetValue(template, models);
        gibsField.SetValue(template, gibs);

        IDictionary templates = NewDictionary(templateCacheField);
        templates.Add(123, template);
        templateCacheField.SetValue(null, templates);
        IDictionary avatars = NewDictionary(avatarCacheField);
        avatars.Add("wizard", template);
        avatarCacheField.SetValue(null, avatars);

        Invoke(clearCache);

        bool preserved = Object.ReferenceEquals(
            modelsField.GetValue(template),
            models);
        string actual = "templates:" + templates.Count +
            ",avatars:" + avatars.Count +
            ",models:" + (preserved ? "preserved" : "changed");
        const string expected =
            "templates:0,avatars:0,models:preserved";
        return new ScenarioResult(
            templates.Count == 0 && avatars.Count == 0 && preserved,
            actual,
            expected);
    }

    internal ScenarioResult Empty()
    {
        IDictionary templates = NewDictionary(templateCacheField);
        IDictionary avatars = NewDictionary(avatarCacheField);
        templateCacheField.SetValue(null, templates);
        avatarCacheField.SetValue(null, avatars);
        Invoke(clearCache);
        string actual = "templates:" + templates.Count +
            ",avatars:" + avatars.Count;
        const string expected = "templates:0,avatars:0";
        return new ScenarioResult(
            templates.Count == 0 && avatars.Count == 0,
            actual,
            expected);
    }

    private static IDictionary NewDictionary(FieldInfo field)
    {
        return (IDictionary)Activator.CreateInstance(field.FieldType);
    }

    private static FieldInfo RequireDictionary(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null ||
            !typeof(IDictionary).IsAssignableFrom(field.FieldType))
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static FieldInfo RequireArrayField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null || !field.FieldType.IsArray)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static void Invoke(MethodInfo method)
    {
        try
        {
            method.Invoke(null, new object[0]);
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
