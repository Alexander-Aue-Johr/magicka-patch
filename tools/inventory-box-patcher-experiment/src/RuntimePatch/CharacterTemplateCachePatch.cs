using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterTemplateCachePatch
    {
        private static FieldInfo templateCacheField;
        private static FieldInfo avatarCacheField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character template shared-asset cache cleanup",
                "org.magickacommunitypatch.character-template-cache-cleanup",
                FindClearCache,
                target => typeof(CharacterTemplateCachePatch).GetMethod("Prefix"));

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                false);
            return templateType != null &&
                templateType.GetField(
                    "mCachedTemplates",
                    BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null &&
                templateType.GetField(
                    "sCachedAvatarTemplates",
                    BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
        }

        private static MethodInfo FindClearCache(Assembly targetAssembly)
        {
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            templateCacheField = RequireDictionary(
                templateType,
                "mCachedTemplates",
                typeof(int),
                templateType);
            avatarCacheField = RequireDictionary(
                templateType,
                "sCachedAvatarTemplates",
                typeof(string),
                templateType);
            MethodInfo method = templateType.GetMethod(
                "ClearCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(templateType.FullName, "ClearCache");
            return method;
        }

        private static FieldInfo RequireDictionary(
            Type type,
            string name,
            Type keyType,
            Type valueType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null ||
                !typeof(IDictionary).IsAssignableFrom(field.FieldType) ||
                !field.FieldType.IsGenericType)
                throw new MissingFieldException(type.FullName, name);
            Type[] arguments = field.FieldType.GetGenericArguments();
            if (arguments.Length != 2 ||
                arguments[0] != keyType ||
                arguments[1] != valueType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static bool Prefix()
        {
            if (templateCacheField == null || avatarCacheField == null)
                throw new InvalidOperationException(
                    "Character template cache contract has not been initialized.");
            ((IDictionary)templateCacheField.GetValue(null)).Clear();
            ((IDictionary)avatarCacheField.GetValue(null)).Clear();
            return false;
        }
    }
}
