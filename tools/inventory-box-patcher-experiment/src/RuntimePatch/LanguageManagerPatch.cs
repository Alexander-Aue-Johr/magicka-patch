using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class LanguageManagerPatch
    {
        private const string TypeName = "Magicka.Localization.LanguageManager";

        internal static readonly RuntimePatchDefinition NativeNameDefinition =
            RuntimePatchDefinition.Prefix(
                "Simplified Chinese display name",
                "org.magickacommunitypatch.language-native-name",
                FindNativeName,
                CreateNativeNamePrefix);

        internal static readonly RuntimePatchDefinition LanguageLookupDefinition =
            RuntimePatchDefinition.Prefix(
                "Simplified Chinese language aliases",
                "org.magickacommunitypatch.language-aliases",
                FindLanguageLookup,
                CreateLanguageLookupPrefix);

        private static MethodInfo FindNativeName(Assembly targetAssembly)
        {
            Type managerType = targetAssembly.GetType(TypeName, true);
            Type languageType = targetAssembly.GetType("Magicka.Localization.Language", true);
            MethodInfo method = managerType.GetMethod(
                "GetNativeName",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { languageType },
                null);
            if (method == null || method.ReturnType != typeof(string))
                throw new MissingMethodException(managerType.FullName, "GetNativeName");
            RequireChineseLanguage(languageType);
            return method;
        }

        private static MethodInfo FindLanguageLookup(Assembly targetAssembly)
        {
            Type managerType = targetAssembly.GetType(TypeName, true);
            Type languageType = targetAssembly.GetType("Magicka.Localization.Language", true);
            MethodInfo method = managerType.GetMethod(
                "GetLanguage",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(string) },
                null);
            if (method == null || method.ReturnType != languageType)
                throw new MissingMethodException(managerType.FullName, "GetLanguage");
            RequireChineseLanguage(languageType);
            return method;
        }

        private static void RequireChineseLanguage(Type languageType)
        {
            if (!Enum.IsDefined(languageType, "zho"))
                throw new InvalidOperationException("Language.zho is not defined.");
        }

        private static MethodInfo CreateNativeNamePrefix(MethodInfo target)
        {
            Type adapter = typeof(LanguageNativeNamePrefix<>).MakeGenericType(
                target.GetParameters()[0].ParameterType);
            return adapter.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
        }

        private static MethodInfo CreateLanguageLookupPrefix(MethodInfo target)
        {
            Type adapter = typeof(LanguageLookupPrefix<>).MakeGenericType(target.ReturnType);
            return adapter.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
        }

        internal static bool IsSimplifiedChineseAlias(string value)
        {
            if (value == null)
                return false;
            return value.Equals("zho", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("schinese", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("chinese", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("simplified chinese", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("zh-cn", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("zh-hans", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("简体中文", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class LanguageNativeNamePrefix<TLanguage>
    {
        public static bool Prefix(TLanguage iLanguage, ref string __result)
        {
            if (!String.Equals(
                iLanguage.ToString(),
                "zho",
                StringComparison.Ordinal))
                return true;

            __result = "Simplified Chinese";
            return false;
        }
    }

    public static class LanguageLookupPrefix<TLanguage>
    {
        public static bool Prefix(string iLanguage, ref TLanguage __result)
        {
            if (!LanguageManagerPatch.IsSimplifiedChineseAlias(iLanguage))
                return true;

            __result = (TLanguage)Enum.Parse(typeof(TLanguage), "zho");
            return false;
        }
    }
}
