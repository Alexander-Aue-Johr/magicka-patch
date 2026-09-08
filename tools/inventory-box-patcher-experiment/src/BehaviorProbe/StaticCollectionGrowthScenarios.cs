using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class StaticCollectionGrowthScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        StaticCollectionGrowthHarness harness =
            new StaticCollectionGrowthHarness(magicka, runtimePatchEnabled);
        report.Add("static_list.add_full", harness.StaticListAddFull());
        report.Add("static_list.insert_full", harness.StaticListInsertFull());
        report.Add("static_list.below_capacity", harness.StaticListBelowCapacity());
        report.Add(
            "static_list.entity_add_full",
            harness.StaticListEntityAddFull());
        report.Add(
            "static_list.spell_add_full",
            harness.StaticListSpellAddFull());
        report.Add("static_weak_list.add_full", harness.WeakListAddFull());
        report.Add("static_weak_list.insert_full", harness.WeakListInsertFull());
        report.Add("static_weak_list.below_capacity", harness.WeakListBelowCapacity());
        report.Add(
            "static_weak_list.character_add_full",
            harness.WeakCharacterListAddFull());
    }
}

internal sealed class StaticCollectionGrowthHarness
{
    private readonly Type staticIntListType;
    private readonly Type staticEntityListType;
    private readonly Type staticSpellListType;
    private readonly Type weakEntityListType;
    private readonly Type weakCharacterListType;
    private readonly Type avatarType;
    private readonly Type spellType;
    private readonly bool runtimePatchEnabled;

    internal StaticCollectionGrowthHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type staticValueList = magicka.GetType(
            "Magicka.StaticEquatableList`1",
            true);
        Type staticObjectList = magicka.GetType(
            "Magicka.StaticObjectList`1",
            true);
        Type weakList = magicka.GetType("Magicka.StaticWeakList`1", true);
        Type entityType = magicka.GetType(
            "Magicka.GameLogic.Entities.Entity",
            true);
        Type characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        spellType = magicka.GetType(
            "Magicka.GameLogic.Spells.Spell",
            true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        staticIntListType = staticValueList.MakeGenericType(typeof(int));
        staticEntityListType = staticObjectList.MakeGenericType(entityType);
        staticSpellListType = staticValueList.MakeGenericType(spellType);
        weakEntityListType = weakList.MakeGenericType(entityType);
        weakCharacterListType = weakList.MakeGenericType(characterType);
    }

    internal ScenarioResult StaticListAddFull()
    {
        object list = NewList(staticIntListType, 1);
        Invoke(list, "Add", 11);
        return Capture(
            () => Invoke(list, "Add", 22),
            () => DescribeIntList(list),
            "count:2,capacity:2,values:11|22");
    }

    internal ScenarioResult StaticListInsertFull()
    {
        object list = NewList(staticIntListType, 1);
        Invoke(list, "Add", 11);
        return Capture(
            () => Invoke(list, "Insert", 0, 22),
            () => DescribeIntList(list),
            "count:2,capacity:2,values:22|11");
    }

    internal ScenarioResult StaticListBelowCapacity()
    {
        object list = NewList(staticIntListType, 3);
        Invoke(list, "Add", 11);
        Invoke(list, "Insert", 0, 22);
        string actual = DescribeIntList(list);
        const string expected = "count:2,capacity:3,values:22|11";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult StaticListEntityAddFull()
    {
        object first = NewAvatar();
        object second = NewAvatar();
        object list = NewList(staticEntityListType, 1);
        AddStaticListReference(list, first);
        ScenarioResult result = Capture(
            () => AddStaticListReference(list, second),
            () => DescribeReferenceList(list, first, second),
            "count:2,capacity:2,values:first|second");
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        return result;
    }

    internal ScenarioResult StaticListSpellAddFull()
    {
        object list = NewList(staticSpellListType, 1);
        object spell = Activator.CreateInstance(spellType);
        Invoke(list, "Add", spell);
        return Capture(
            () => Invoke(list, "Add", spell),
            () => DescribeCount(list),
            "count:2,capacity:2");
    }

    internal ScenarioResult WeakListAddFull()
    {
        object first = NewAvatar();
        object second = NewAvatar();
        object list = NewList(weakEntityListType, 1);
        AddStaticWeakListReference(list, first);
        ScenarioResult result = Capture(
            () => AddStaticWeakListReference(list, second),
            () => DescribeWeakList(list, first, second),
            "count:2,capacity:2,values:first|second");
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        return result;
    }

    internal ScenarioResult WeakListInsertFull()
    {
        object first = NewAvatar();
        object second = NewAvatar();
        object list = NewList(weakEntityListType, 1);
        AddStaticWeakListReference(list, first);
        ScenarioResult result = Capture(
            () => InsertStaticWeakListReference(list, 0, second),
            () => DescribeWeakList(list, first, second),
            "count:2,capacity:2,values:second|first");
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        return result;
    }

    internal ScenarioResult WeakListBelowCapacity()
    {
        object first = NewAvatar();
        object second = NewAvatar();
        object list = NewList(weakEntityListType, 3);
        Invoke(list, "Add", first);
        Invoke(list, "Insert", 0, second);
        string actual = DescribeWeakList(list, first, second);
        const string expected = "count:2,capacity:3,values:second|first";
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult WeakCharacterListAddFull()
    {
        object first = NewAvatar();
        object second = NewAvatar();
        object list = NewList(weakCharacterListType, 1);
        AddStaticWeakListReference(list, first);
        ScenarioResult result = Capture(
            () => AddStaticWeakListReference(list, second),
            () => DescribeWeakList(list, first, second),
            "count:2,capacity:2,values:first|second");
        GC.KeepAlive(first);
        GC.KeepAlive(second);
        return result;
    }

    private void AddStaticListReference(object list, object item)
    {
        if (runtimePatchEnabled)
        {
            Magicka.CommunityPatch.Runtime.StaticCollectionReferenceCallPatch
                .AddStaticListReference(list, item);
            return;
        }
        Invoke(list, "Add", item);
    }

    private void AddStaticWeakListReference(object list, object item)
    {
        if (runtimePatchEnabled)
        {
            Magicka.CommunityPatch.Runtime.StaticCollectionReferenceCallPatch
                .AddStaticWeakListReference(list, item);
            return;
        }
        Invoke(list, "Add", item);
    }

    private void InsertStaticWeakListReference(
        object list,
        int index,
        object item)
    {
        if (runtimePatchEnabled)
        {
            Magicka.CommunityPatch.Runtime.StaticCollectionReferenceCallPatch
                .InsertStaticWeakListReference(list, index, item);
            return;
        }
        Invoke(list, "Insert", index, item);
    }

    private object NewAvatar()
    {
        object avatar = FormatterServices.GetUninitializedObject(avatarType);
        GC.SuppressFinalize(avatar);
        return avatar;
    }

    private static object NewList(Type type, int capacity)
    {
        return Activator.CreateInstance(type, new object[] { capacity });
    }

    private static ScenarioResult Capture(
        Action action,
        Func<string> describe,
        string expected)
    {
        try
        {
            action();
            string actual = describe();
            return new ScenarioResult(actual == expected, actual, expected);
        }
        catch (TargetInvocationException exception)
        {
            Exception inner = exception.InnerException ?? exception;
            return new ScenarioResult(false, inner.GetType().FullName, expected);
        }
    }

    private static string DescribeIntList(object list)
    {
        int count = (int)ReadProperty(list, "Count");
        int capacity = (int)ReadProperty(list, "Capacity");
        return "count:" + count + ",capacity:" + capacity + ",values:" +
            ReadIndex(list, 0) + "|" + ReadIndex(list, 1);
    }

    private static string DescribeReferenceList(
        object list,
        object first,
        object second)
    {
        int count = (int)ReadProperty(list, "Count");
        int capacity = (int)ReadProperty(list, "Capacity");
        return "count:" + count + ",capacity:" + capacity + ",values:" +
            Name(ReadIndex(list, 0), first, second) + "|" +
            Name(ReadIndex(list, 1), first, second);
    }

    private static string DescribeCount(object list)
    {
        return "count:" + ReadProperty(list, "Count") + ",capacity:" +
            ReadProperty(list, "Capacity");
    }

    private static string DescribeWeakList(
        object list,
        object first,
        object second)
    {
        int count = (int)ReadProperty(list, "Count");
        int capacity = (int)ReadProperty(list, "Capacity");
        return "count:" + count + ",capacity:" + capacity + ",values:" +
            Name(ReadIndex(list, 0), first, second) + "|" +
            Name(ReadIndex(list, 1), first, second);
    }

    private static string Name(object value, object first, object second)
    {
        if (Object.ReferenceEquals(value, first))
            return "first";
        if (Object.ReferenceEquals(value, second))
            return "second";
        return value == null ? "null" : "other";
    }

    private static object ReadProperty(object target, string name)
    {
        return target.GetType().GetProperty(name).GetValue(target, null);
    }

    private static object ReadIndex(object target, int index)
    {
        return target.GetType().GetProperty("Item").GetValue(
            target,
            new object[] { index });
    }

    private static object Invoke(object target, string name, params object[] arguments)
    {
        Type[] parameterTypes = Array.ConvertAll(
            arguments,
            argument => argument.GetType());
        MethodInfo method = target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            parameterTypes,
            null);
        if (method == null)
            throw new MissingMethodException(target.GetType().FullName, name);
        return method.Invoke(target, arguments);
    }
}
