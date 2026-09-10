using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterTemplateLookupPatch
    {
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static FieldInfo cacheField;
        private static FieldInfo idField;
        private static PropertyInfo assetNameProperty;
        private static PropertyInfo recentPlayStateProperty;
        private static PropertyInfo contentProperty;
        private static MethodInfo contentLoadMethod;
        private static Type templateType;

        internal static readonly RuntimePatchDefinition ReadDefinition = RuntimePatchDefinition.Postfix(
            "Character template asset-name registration",
            "org.magickacommunitypatch.character-template-name",
            FindRead,
            CreateReadPostfix);

        internal static readonly RuntimePatchDefinition GetDefinition = RuntimePatchDefinition.Prefix(
            "Character template safe lazy lookup",
            "org.magickacommunitypatch.character-template-lookup",
            FindGet,
            CreateGetPrefix);

        private static MethodInfo FindRead(Assembly assembly)
        {
            Configure(assembly);
            MethodInfo[] methods = templateType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "Read" && methods[index].ReturnType == templateType && methods[index].GetParameters().Length == 1)
                {
                    if (result != null)
                        throw new AmbiguousMatchException("CharacterTemplate.Read");
                    result = methods[index];
                }
            if (result == null)
                throw new MissingMethodException(templateType.FullName, "Read");
            assetNameProperty = result.GetParameters()[0].ParameterType.GetProperty("AssetName", BindingFlags.Instance | BindingFlags.Public);
            if (assetNameProperty == null)
                throw new MissingMemberException("ContentReader.AssetName");
            return result;
        }

        private static MethodInfo FindGet(Assembly assembly)
        {
            Configure(assembly);
            MethodInfo method = templateType.GetMethod("GetCachedTemplate", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new Type[] { typeof(int) }, null);
            if (method == null || method.ReturnType != templateType)
                throw new MissingMethodException(templateType.FullName, "GetCachedTemplate");
            return method;
        }

        private static void Configure(Assembly assembly)
        {
            templateType = assembly.GetType("Magicka.GameLogic.Entities.CharacterTemplate", true);
            cacheField = templateType.GetField("mCachedTemplates", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            idField = templateType.GetField("mIDHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Type playState = assembly.GetType("Magicka.GameLogic.GameStates.PlayState", true);
            recentPlayStateProperty = playState.GetProperty("RecentPlayState", BindingFlags.Static | BindingFlags.Public);
            contentProperty = playState.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public);
            contentLoadMethod = FindLoad(contentProperty == null ? null : contentProperty.PropertyType);
            if (cacheField == null || idField == null || recentPlayStateProperty == null || contentProperty == null || contentLoadMethod == null)
                throw new MissingMemberException("Character template lazy-lookup contract is incomplete.");
        }

        private static MethodInfo FindLoad(Type contentType)
        {
            if (contentType == null)
                return null;
            MethodInfo[] methods = contentType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "Load" && methods[index].IsGenericMethodDefinition && methods[index].GetParameters().Length == 1 && methods[index].GetParameters()[0].ParameterType == typeof(string))
                    return methods[index];
            return null;
        }

        private static MethodInfo CreateReadPostfix(MethodInfo target)
        {
            return typeof(CharacterTemplateLookupPatch).GetMethod("ReadPostfix").MakeGenericMethod(target.GetParameters()[0].ParameterType, target.ReturnType);
        }

        private static MethodInfo CreateGetPrefix(MethodInfo target)
        {
            return typeof(CharacterTemplateLookupPatch).GetMethod("GetPrefix").MakeGenericMethod(target.ReturnType);
        }

        public static void ReadPostfix<TReader, TTemplate>(TReader iInput, TTemplate __result)
        {
            if ((object)__result == null || (object)iInput == null)
                return;
            string name = assetNameProperty.GetValue(iInput, null) as string;
            if (!String.IsNullOrEmpty(name))
                Names[(int)idField.GetValue(__result)] = name;
        }

        public static bool GetPrefix<TTemplate>(int iID, ref TTemplate __result)
        {
            IDictionary cache = cacheField.GetValue(null) as IDictionary;
            if (cache == null || cache.Contains(iID))
                return true;
            string name;
            if (!Names.TryGetValue(iID, out name))
                return true;
            object playState = recentPlayStateProperty.GetValue(null, null);
            object content = playState == null ? null : contentProperty.GetValue(playState, null);
            if (content == null)
            {
                __result = default(TTemplate);
                return false;
            }
            __result = (TTemplate)contentLoadMethod.MakeGenericMethod(templateType).Invoke(content, new object[] { name });
            return false;
        }
    }
}
