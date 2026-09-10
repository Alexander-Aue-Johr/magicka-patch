using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ItemWeaponCachePatch
    {
        private static Type itemType;
        private static FieldInfo cachedWeaponsField;
        private static FieldInfo itemTypeField;
        private static PropertyInfo recentPlayStateProperty;
        private static PropertyInfo contentProperty;
        private static MethodInfo contentLoadMethod;
        private static MethodInfo copyMethod;
        private static FieldInfo referencedAssetField;
        private static FieldInfo referencedAssetReferencesField;
        private static readonly Dictionary<int, string> AssetNames =
            new Dictionary<int, string>();

        internal static readonly RuntimePatchDefinition ReadDefinition =
            RuntimePatchDefinition.Postfix(
                "Item weapon asset-name registration",
                "org.magickacommunitypatch.item-weapon-name",
                FindRead,
                CreateReadPostfix);

        internal static readonly RuntimePatchDefinition CacheDefinition =
            RuntimePatchDefinition.Prefix(
                "Item weapon first-registration cache",
                "org.magickacommunitypatch.item-weapon-cache",
                FindCacheWeapon,
                CreateCachePrefix);

        internal static readonly RuntimePatchDefinition GetDefinition =
            RuntimePatchDefinition.Prefix(
                "Item weapon lazy cache restore",
                "org.magickacommunitypatch.item-weapon-restore",
                FindGetCachedWeapon,
                CreateGetPrefix);

        internal static readonly RuntimePatchDefinition HasDefinition =
            RuntimePatchDefinition.Prefix(
                "Item weapon lazy cache query",
                "org.magickacommunitypatch.item-weapon-has",
                FindHasCachedWeapon,
                target => typeof(ItemWeaponCachePatch).GetMethod(
                    "HasPrefix"));

        internal static readonly RuntimePatchDefinition CopyDefinition =
            RuntimePatchDefinition.Prefix(
                "Item weapon lazy cache copy",
                "org.magickacommunitypatch.item-weapon-copy",
                FindCopyCachedWeapon,
                CreateCopyPrefix);

        internal static readonly RuntimePatchDefinition ReleaseDefinition =
            RuntimePatchDefinition.Postfix(
                "Item weapon content release",
                "org.magickacommunitypatch.item-weapon-release",
                FindContentRelease,
                CreateReleasePostfix);

        private static void ResolveContracts(Assembly assembly)
        {
            if (itemType != null && itemType.Assembly == assembly)
                return;
            itemType = assembly.GetType(
                "Magicka.GameLogic.Entities.Items.Item", true);
            cachedWeaponsField = RequireField(
                itemType, "CachedWeapons", BindingFlags.Static);
            itemTypeField = RequireField(
                itemType, "mType", BindingFlags.Instance);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            recentPlayStateProperty = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            contentProperty = FindProperty(playState, "Content");
            if (recentPlayStateProperty == null || contentProperty == null)
                throw new MissingMemberException(playState.FullName,
                    "RecentPlayState/Content");
            MethodInfo[] methods = contentProperty.PropertyType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            contentLoadMethod = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "Load" && method.IsGenericMethodDefinition &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType == typeof(string))
                {
                    contentLoadMethod = method.MakeGenericMethod(itemType);
                    break;
                }
            }
            if (contentLoadMethod == null)
                throw new MissingMethodException(
                    contentProperty.PropertyType.FullName, "Load<T>");
            copyMethod = RequireMethod(itemType, "Copy",
                new Type[] { itemType });
        }

        private static MethodInfo FindRead(Assembly assembly)
        {
            ResolveContracts(assembly);
            MethodInfo[] methods = itemType.GetMethods(
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "Read" &&
                    methods[index].ReturnType == itemType &&
                    parameters.Length == 1)
                    return methods[index];
            }
            throw new MissingMethodException(itemType.FullName, "Read");
        }

        private static MethodInfo FindCacheWeapon(Assembly assembly)
        {
            ResolveContracts(assembly);
            return RequireMethod(itemType, "CacheWeapon",
                new Type[] { typeof(int), itemType });
        }

        private static MethodInfo FindGetCachedWeapon(Assembly assembly)
        {
            ResolveContracts(assembly);
            return RequireMethod(itemType, "GetCachedWeapon",
                new Type[] { typeof(int) });
        }

        private static MethodInfo FindHasCachedWeapon(Assembly assembly)
        {
            ResolveContracts(assembly);
            return RequireMethod(itemType, "HasCachedWeapon",
                new Type[] { typeof(int) });
        }

        private static MethodInfo FindCopyCachedWeapon(Assembly assembly)
        {
            ResolveContracts(assembly);
            return RequireMethod(itemType, "GetCachedWeapon",
                new Type[] { typeof(int), itemType });
        }

        private static MethodInfo FindContentRelease(Assembly assembly)
        {
            ResolveContracts(assembly);
            Type common = assembly.GetType(
                "Magicka.SharedContentManager+CommonContentManager", true);
            Type referenced = assembly.GetType(
                "Magicka.SharedContentManager+CommonContentManager+ReferencedAsset",
                true);
            referencedAssetField = RequireField(
                referenced, "Asset", BindingFlags.Instance);
            referencedAssetReferencesField = RequireField(
                referenced, "References", BindingFlags.Instance);
            return RequireMethod(common, "Dec",
                new Type[] { referenced, typeof(int) });
        }

        private static MethodInfo CreateReadPostfix(MethodInfo target)
        {
            return typeof(ItemWeaponCachePatch).GetMethod(
                "ReadPostfix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType,
                    target.ReturnType);
        }

        private static MethodInfo CreateCachePrefix(MethodInfo target)
        {
            return typeof(ItemWeaponCachePatch).GetMethod(
                "CachePrefix").MakeGenericMethod(
                    target.GetParameters()[1].ParameterType);
        }

        private static MethodInfo CreateGetPrefix(MethodInfo target)
        {
            return typeof(ItemWeaponCachePatch).GetMethod(
                "GetPrefix").MakeGenericMethod(target.ReturnType);
        }

        private static MethodInfo CreateCopyPrefix(MethodInfo target)
        {
            return typeof(ItemWeaponCachePatch).GetMethod(
                "CopyPrefix").MakeGenericMethod(
                    target.GetParameters()[1].ParameterType);
        }

        private static MethodInfo CreateReleasePostfix(MethodInfo target)
        {
            return typeof(ItemWeaponCachePatch).GetMethod(
                "ReleasePostfix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        public static void ReadPostfix<TReader, TItem>(
            TReader iInput, TItem __result)
        {
            if ((object)__result == null || (object)iInput == null)
                return;
            PropertyInfo assetName = iInput.GetType().GetProperty(
                "AssetName", BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            string name = assetName == null
                ? null : assetName.GetValue(iInput, null) as string;
            RecordAssetName(
                Convert.ToInt32(itemTypeField.GetValue(__result)), name);
        }

        public static bool CachePrefix<TItem>(int iType, TItem iItem)
        {
            IDictionary cache = cachedWeaponsField.GetValue(null) as IDictionary;
            if (cache != null && !cache.Contains(iType))
                cache[iType] = iItem;
            return false;
        }

        public static bool GetPrefix<TItem>(int iType, ref TItem __result)
        {
            object value = GetOrReload(iType);
            __result = value == null ? default(TItem) : (TItem)value;
            return false;
        }

        public static bool HasPrefix(int iType, ref bool __result)
        {
            __result = GetOrReload(iType) != null;
            return false;
        }

        public static bool CopyPrefix<TItem>(int iType, TItem iTarget)
        {
            object source = GetOrReload(iType);
            if (source == null)
                return true;
            copyMethod.Invoke(source, new object[] { iTarget });
            return false;
        }

        public static void ReleasePostfix<TReferencedAsset>(
            TReferencedAsset iAsset)
        {
            if ((object)iAsset == null ||
                Convert.ToInt32(referencedAssetReferencesField.GetValue(iAsset)) != 0)
                return;
            RemoveIfCurrent(referencedAssetField.GetValue(iAsset));
        }

        public static void RecordAssetName(int type, string assetName)
        {
            if (!String.IsNullOrEmpty(assetName) && !AssetNames.ContainsKey(type))
                AssetNames[type] = assetName;
        }

        public static object GetOrReload(int type)
        {
            IDictionary cache = cachedWeaponsField.GetValue(null) as IDictionary;
            if (cache != null && cache.Contains(type))
                return cache[type];
            string name;
            if (!AssetNames.TryGetValue(type, out name))
                return null;
            object playState = recentPlayStateProperty.GetValue(null, null);
            if (playState == null)
                return null;
            object content = contentProperty.GetValue(playState, null);
            if (content == null)
                return null;
            object item = contentLoadMethod.Invoke(content,
                new object[] { name });
            if (cache != null && !cache.Contains(type))
                cache[type] = item;
            return item;
        }

        public static void RemoveIfCurrent(object item)
        {
            if (item == null || itemType == null ||
                !itemType.IsInstanceOfType(item))
                return;
            int type = Convert.ToInt32(itemTypeField.GetValue(item));
            IDictionary cache = cachedWeaponsField.GetValue(null) as IDictionary;
            if (cache != null && cache.Contains(type) &&
                Object.ReferenceEquals(cache[type], item))
                cache.Remove(type);
        }

        private static FieldInfo RequireField(
            Type type, string name, BindingFlags kind)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
            {
                FieldInfo field = current.GetField(name,
                    kind | BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }
            return null;
        }

        private static MethodInfo RequireMethod(
            Type type, string name, Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name,
                BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
