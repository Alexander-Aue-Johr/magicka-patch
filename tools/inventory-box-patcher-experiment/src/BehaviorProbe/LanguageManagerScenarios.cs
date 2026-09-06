using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class LanguageManagerScenarios
{
    internal static void Run(Assembly magicka, BehaviorReport report)
    {
        Type managerType = magicka.GetType("Magicka.Localization.LanguageManager", true);
        Type languageType = magicka.GetType("Magicka.Localization.Language", true);
        MethodInfo getNativeName = managerType.GetMethod(
            "GetNativeName",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { languageType },
            null);
        MethodInfo getLanguage = managerType.GetMethod(
            "GetLanguage",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new Type[] { typeof(string) },
            null);
        if (getNativeName == null || getLanguage == null)
            throw new MissingMethodException(managerType.FullName, "language lookup methods");

        object manager = FormatterServices.GetUninitializedObject(managerType);
        object chinese = Enum.Parse(languageType, "zho");
        object english = Enum.Parse(languageType, "eng");

        report.Add(
            "language_manager.simplified_chinese_name",
            InvokeNativeName(getNativeName, manager, chinese));
        report.Add(
            "language_manager.simplified_chinese_aliases",
            InvokeChineseAliases(getLanguage, manager, chinese));
        report.Add(
            "language_manager.existing_lookup",
            InvokeExistingLookup(getLanguage, manager, english));
    }

    private static ScenarioResult InvokeNativeName(
        MethodInfo method,
        object manager,
        object language)
    {
        const string expected = "Simplified Chinese";
        try
        {
            string actual = (string)method.Invoke(manager, new object[] { language });
            return new ScenarioResult(
                String.Equals(actual, expected, StringComparison.Ordinal),
                actual,
                expected);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, expected);
        }
    }

    private static ScenarioResult InvokeChineseAliases(
        MethodInfo method,
        object manager,
        object expectedLanguage)
    {
        string[] aliases = new string[]
        {
            "zho",
            "schinese",
            "chinese",
            "simplified chinese",
            "zh-cn",
            "zh-hans",
            "简体中文"
        };
        for (int index = 0; index < aliases.Length; index++)
        {
            object actual = method.Invoke(manager, new object[] { aliases[index] });
            if (!Object.Equals(actual, expectedLanguage))
            {
                return new ScenarioResult(
                    false,
                    aliases[index] + ":" + actual,
                    "all aliases:zho");
            }
        }
        return new ScenarioResult(true, "all aliases:zho", "all aliases:zho");
    }

    private static ScenarioResult InvokeExistingLookup(
        MethodInfo method,
        object manager,
        object english)
    {
        object explicitEnglish = method.Invoke(manager, new object[] { "english" });
        object unknown = method.Invoke(manager, new object[] { "not-a-language" });
        bool passed = Object.Equals(explicitEnglish, english) && Object.Equals(unknown, english);
        string actual = "english:" + explicitEnglish + ",unknown:" + unknown;
        return new ScenarioResult(
            passed,
            actual,
            "english:eng,unknown:eng");
    }
}
