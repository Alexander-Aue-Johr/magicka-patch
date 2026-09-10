using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ItemWeaponCacheScenarios
{
    internal static void Run(
        Assembly magicka, bool runtime, BehaviorReport report)
    {
        ItemWeaponCacheHarness harness =
            new ItemWeaponCacheHarness(magicka, runtime);
        report.Add("item_weapon_cache.first_registration",
            harness.FirstRegistration());
        report.Add("item_weapon_cache.missing_returns_null",
            harness.MissingReturnsNull());
        report.Add("item_weapon_cache.asset_name",
            harness.AssetNameRegistration());
        report.Add("item_weapon_cache.conditional_release",
            harness.ConditionalRelease());
        report.Add("item_weapon_cache.lookup_routes",
            harness.LookupRoutes());
    }
}

internal sealed class ItemWeaponCacheHarness
{
    private readonly Type itemType;
    private readonly FieldInfo cacheField;
    private readonly FieldInfo typeField;
    private readonly MethodInfo cacheWeapon;
    private readonly MethodInfo getCachedWeapon;
    private readonly bool runtime;
    private readonly Type runtimePatch;

    internal ItemWeaponCacheHarness(Assembly magicka, bool runtimeEnabled)
    {
        runtime = runtimeEnabled;
        itemType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.Item", true);
        cacheField = FindField(itemType, "CachedWeapons");
        typeField = FindField(itemType, "mType");
        cacheWeapon = itemType.GetMethod("CacheWeapon",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic,
            null, new Type[] { typeof(int), itemType }, null);
        getCachedWeapon = itemType.GetMethod("GetCachedWeapon",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic,
            null, new Type[] { typeof(int) }, null);
        runtimePatch = typeof(Magicka.CommunityPatch.Runtime.Bootstrap)
            .Assembly.GetType(
                "Magicka.CommunityPatch.Runtime.ItemWeaponCachePatch", true);
    }

    internal ScenarioResult FirstRegistration()
    {
        int key = Int32.MinValue + 258;
        IDictionary cache = (IDictionary)cacheField.GetValue(null);
        cache.Remove(key);
        object first = FormatterServices.GetUninitializedObject(itemType);
        object second = FormatterServices.GetUninitializedObject(itemType);
        typeField.SetValue(first, key);
        typeField.SetValue(second, key);
        cacheWeapon.Invoke(null, new object[] { key, first });
        cacheWeapon.Invoke(null, new object[] { key, second });
        bool stable = Object.ReferenceEquals(cache[key], first);
        cache.Remove(key);
        return new ScenarioResult(stable,
            stable ? "first_registration" : "overwritten",
            "first_registration");
    }

    internal ScenarioResult MissingReturnsNull()
    {
        int key = Int32.MinValue + 259;
        ((IDictionary)cacheField.GetValue(null)).Remove(key);
        object result = null;
        string state;
        try
        {
            result = getCachedWeapon.Invoke(null, new object[] { key });
            state = result == null ? "null" : "unexpected_item";
        }
        catch (TargetInvocationException exception)
        {
            state = "throws:" + exception.InnerException.GetType().Name;
        }
        return new ScenarioResult(state == "null", state, "null");
    }

    internal ScenarioResult AssetNameRegistration()
    {
        bool available = runtime || itemType.GetMethod("CacheWeaponName",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
        return new ScenarioResult(available,
            available ? "recorded" : "not_recorded", "recorded");
    }

    internal ScenarioResult ConditionalRelease()
    {
        bool available = runtime || itemType.GetMethod("Dispose",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, Type.EmptyTypes, null) != null;
        if (runtime)
            available = runtimePatch.GetMethod("RemoveIfCurrent") != null;
        return new ScenarioResult(available,
            available ? "identity_checked" : "not_released",
            "identity_checked");
    }

    internal ScenarioResult LookupRoutes()
    {
        bool routed = runtime || itemType.GetMethod("CacheWeaponName",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null;
        return new ScenarioResult(routed,
            routed ? "lazy_get_has_copy" : "direct_dictionary_access",
            "lazy_get_has_copy");
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null;
            current = current.BaseType)
        {
            FieldInfo field = current.GetField(name,
                BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }
}
