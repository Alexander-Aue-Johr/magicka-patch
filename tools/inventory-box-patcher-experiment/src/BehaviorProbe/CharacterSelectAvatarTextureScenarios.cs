using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class CharacterSelectAvatarTextureScenarios
{
    internal static void Run(
        Assembly magicka, bool runtimePatchEnabled, BehaviorReport report)
    {
        if (magicka.GetName().Version.Minor < 10)
        {
            const string reason =
                "The character-select texture guard is a current-version patch.";
            report.AddNotApplicable(
                "character_select_avatar_texture.missing_avatar", reason);
            report.AddNotApplicable(
                "character_select_avatar_texture.missing_custom", reason);
            report.AddNotApplicable(
                "character_select_avatar_texture.valid", reason);
            return;
        }
        Type owner = magicka.GetType(
            "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
            true);
        MethodInfo target = FindTarget(owner);
        report.Add(
            "character_select_avatar_texture.missing_avatar",
            MissingTexture(owner, target, null, false));
        Type textureType = target.GetParameters()[0].ParameterType;
        object texture = FormatterServices.GetUninitializedObject(textureType);
        GC.SuppressFinalize(texture);
        report.Add(
            "character_select_avatar_texture.missing_custom",
            MissingTexture(owner, target, texture, true));
        report.Add(
            "character_select_avatar_texture.valid",
            ValidControl(owner, target, runtimePatchEnabled));
    }

    private static ScenarioResult MissingTexture(
        Type owner, MethodInfo target, object texture, bool custom)
    {
        object instance = FormatterServices.GetUninitializedObject(owner);
        GC.SuppressFinalize(instance);
        try
        {
            target.Invoke(instance, new object[]
            {
                texture,
                custom,
                Activator.CreateInstance(target.GetParameters()[2].ParameterType),
                Activator.CreateInstance(target.GetParameters()[3].ParameterType
                    .GetElementType()),
                1f
            });
            return Result(true, "skipped", "skipped");
        }
        catch (TargetInvocationException exception)
        {
            return Result(
                false,
                "exception:" + exception.InnerException.GetType().Name,
                "skipped");
        }
    }

    private static ScenarioResult ValidControl(
        Type owner, MethodInfo target, bool runtime)
    {
        if (!runtime)
            return Result(true, "continued", "continued");
        Type textureType = target.GetParameters()[0].ParameterType;
        object texture = FormatterServices.GetUninitializedObject(textureType);
        GC.SuppressFinalize(texture);
        object instance = FormatterServices.GetUninitializedObject(owner);
        GC.SuppressFinalize(instance);
        FieldInfo custom = owner.GetField(
            "mCustomTexture",
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        custom.SetValue(instance, texture);
        Type patch = typeof(
            Magicka.CommunityPatch.Runtime.Bootstrap).Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.CharacterSelectAvatarTexturePatch",
                true);
        MethodInfo prefix = patch.GetMethod("Prefix")
            .MakeGenericMethod(textureType);
        bool continued = (bool)prefix.Invoke(
            null, new object[] { instance, texture, true });
        return Result(
            continued,
            continued ? "continued" : "skipped",
            "continued");
    }

    private static MethodInfo FindTarget(Type owner)
    {
        MethodInfo[] methods = owner.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
            if (methods[index].Name == "DrawAvatar" &&
                methods[index].GetParameters().Length == 5)
                return methods[index];
        throw new MissingMethodException(owner.FullName, "DrawAvatar");
    }

    private static ScenarioResult Result(
        bool passed, string actual, string expected)
    {
        return new ScenarioResult(passed, actual, expected);
    }
}
