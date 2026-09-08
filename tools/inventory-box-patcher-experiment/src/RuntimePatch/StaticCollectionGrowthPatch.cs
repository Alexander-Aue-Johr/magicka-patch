using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class StaticCollectionGrowthPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            Type spell = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.Spell",
                true);

            ApplyStaticList(targetAssembly, typeof(int), "Int32");
            ApplyStaticList(targetAssembly, spell, "Spell");
            RuntimePatchSession.Apply(
                targetAssembly,
                StaticCollectionReferenceCallPatch.EntityManagerDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                StaticCollectionReferenceCallPatch.TriggerAreaDefinition);
        }

        private static void ApplyStaticList(
            Assembly targetAssembly,
            Type itemType,
            string itemName)
        {
            Apply(
                targetAssembly,
                "Magicka.StaticList`1",
                itemType,
                itemName,
                "Add",
                new Type[] { itemType },
                "AddPrefix",
                typeof(StaticListMutationPrefix<,>));
            Apply(
                targetAssembly,
                "Magicka.StaticList`1",
                itemType,
                itemName,
                "Insert",
                new Type[] { typeof(int), itemType },
                "InsertPrefix",
                typeof(StaticListMutationPrefix<,>));
        }

        private static void Apply(
            Assembly targetAssembly,
            string genericTypeName,
            Type itemType,
            string itemName,
            string methodName,
            Type[] parameterTypes,
            string prefixName,
            Type openPrefixType)
        {
            string listName = genericTypeName.IndexOf("Weak") >= 0
                ? "StaticWeakList"
                : "StaticList";
            RuntimePatchDefinition definition = RuntimePatchDefinition.Prefix(
                listName + " " + itemName + " stable " + methodName,
                "org.magickacommunitypatch." + listName.ToLowerInvariant() +
                    "-" + itemName.ToLowerInvariant() + "-" +
                    methodName.ToLowerInvariant(),
                assembly => FindTarget(
                    assembly,
                    genericTypeName,
                    itemType,
                    methodName,
                    parameterTypes),
                target => CreatePrefix(
                    target,
                    itemType,
                    prefixName,
                    openPrefixType));
            RuntimePatchSession.Apply(targetAssembly, definition);
        }

        private static MethodInfo FindTarget(
            Assembly targetAssembly,
            string genericTypeName,
            Type itemType,
            string methodName,
            Type[] parameterTypes)
        {
            Type openType = targetAssembly.GetType(genericTypeName, true);
            Type closedType = openType.MakeGenericType(itemType);
            MethodInfo method = closedType.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                parameterTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(closedType.FullName, methodName);
            return method;
        }

        private static MethodInfo CreatePrefix(
            MethodInfo target,
            Type itemType,
            string prefixName,
            Type openPrefixType)
        {
            Type prefixType = openPrefixType.IsGenericTypeDefinition
                ? openPrefixType.MakeGenericType(target.DeclaringType, itemType)
                : openPrefixType;
            MethodInfo prefix = prefixType.GetMethod(
                prefixName,
                BindingFlags.Static | BindingFlags.Public);
            if (prefix == null)
                throw new MissingMethodException(prefixType.FullName, prefixName);
            return prefix;
        }
    }

    public static class StaticCollectionReferenceCallPatch
    {
        internal static readonly RuntimePatchDefinition EntityManagerDefinition =
            RuntimePatchDefinition.Transpile(
                "EntityManager StaticList growth",
                "org.magickacommunitypatch.staticlist-entity-manager-add",
                FindEntityManagerAdd,
                typeof(StaticCollectionReferenceCallPatch).GetMethod(
                    "EntityManagerTranspiler"));

        internal static readonly RuntimePatchDefinition TriggerAreaDefinition =
            RuntimePatchDefinition.Transpile(
                "TriggerArea StaticWeakList growth",
                "org.magickacommunitypatch.staticweaklist-trigger-area-add",
                FindTriggerAreaAdd,
                typeof(StaticCollectionReferenceCallPatch).GetMethod(
                    "TriggerAreaTranspiler"));

        private static MethodInfo FindEntityManagerAdd(Assembly assembly)
        {
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type manager = assembly.GetType(
                "Magicka.GameLogic.Entities.EntityManager",
                true);
            MethodInfo method = manager.GetMethod(
                "AddEntity",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { entity },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(manager.FullName, "AddEntity");
            return method;
        }

        private static MethodInfo FindTriggerAreaAdd(Assembly assembly)
        {
            Type entity = assembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            Type triggerArea = assembly.GetType(
                "Magicka.Levels.Triggers.TriggerArea",
                true);
            MethodInfo method = triggerArea.GetMethod(
                "AddEntity",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { entity },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(triggerArea.FullName, "AddEntity");
            return method;
        }

        public static IEnumerable<CodeInstruction> EntityManagerTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceAdds(
                instructions,
                "Magicka.StaticList`1",
                1,
                typeof(StaticCollectionReferenceCallPatch).GetMethod(
                    "AddStaticListReference"));
        }

        public static IEnumerable<CodeInstruction> TriggerAreaTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceAdds(
                instructions,
                "Magicka.StaticWeakList`1",
                2,
                typeof(StaticCollectionReferenceCallPatch).GetMethod(
                    "AddStaticWeakListReference"));
        }

        public static void AddStaticListReference(object list, object item)
        {
            StaticListReferenceMutationPrefix.AddPrefix(list, item);
        }

        public static void AddStaticWeakListReference(object list, object item)
        {
            StaticWeakListReferenceMutationPrefix.AddPrefix(list, item);
        }

        public static void InsertStaticWeakListReference(
            object list,
            int index,
            object item)
        {
            StaticWeakListReferenceMutationPrefix.InsertPrefix(
                list,
                index,
                item);
        }

        private static IEnumerable<CodeInstruction> ReplaceAdds(
            IEnumerable<CodeInstruction> instructions,
            string genericTypeName,
            int expectedCount,
            MethodInfo replacement)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode != OpCodes.Call &&
                        result[index].opcode != OpCodes.Callvirt) ||
                    called == null || called.Name != "Add" ||
                    called.DeclaringType == null ||
                    !called.DeclaringType.IsGenericType ||
                    called.DeclaringType.GetGenericTypeDefinition().FullName !=
                        genericTypeName)
                    continue;
                result[index].opcode = OpCodes.Call;
                result[index].operand = replacement;
                replaced++;
            }
            if (replaced != expectedCount)
                throw new InvalidOperationException(
                    "Expected " + expectedCount + " " + genericTypeName +
                    ".Add calls, found " + replaced + ".");
            return result;
        }
    }

    public static class StaticListReferenceMutationPrefix
    {
        public static bool AddPrefix(object __instance, object iItem)
        {
            bool lockTaken = false;
            try
            {
                Monitor.Enter(__instance);
                lockTaken = true;
                StaticCollectionState state = StaticCollectionState.For(
                    __instance,
                    "Magicka.StaticList`1");
                Array objects = state.GetObjects(__instance);
                int count = state.GetCount(__instance);
                objects = EnsureCapacity(state, __instance, objects, count);
                objects.SetValue(iItem, count);
                state.SetCount(__instance, count + 1);
                return false;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(__instance);
            }
        }

        public static bool InsertPrefix(
            object __instance,
            int iIndex,
            object iItem)
        {
            bool lockTaken = false;
            try
            {
                Monitor.Enter(__instance);
                lockTaken = true;
                StaticCollectionState state = StaticCollectionState.For(
                    __instance,
                    "Magicka.StaticList`1");
                int count = state.GetCount(__instance);
                if (iIndex < 0 || iIndex > count)
                    throw new IndexOutOfRangeException();
                Array objects = state.GetObjects(__instance);
                objects = EnsureCapacity(state, __instance, objects, count);
                for (int index = count; index > iIndex; index--)
                    objects.SetValue(objects.GetValue(index - 1), index);
                objects.SetValue(iItem, iIndex);
                state.SetCount(__instance, count + 1);
                return false;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(__instance);
            }
        }

        private static Array EnsureCapacity(
            StaticCollectionState state,
            object instance,
            Array objects,
            int count)
        {
            if (count < objects.Length)
                return objects;
            int capacity = objects.Length == 0
                ? 4
                : checked(objects.Length * 2);
            Array expanded = Array.CreateInstance(
                objects.GetType().GetElementType(),
                capacity);
            Array.Copy(objects, expanded, count);
            state.SetObjects(instance, expanded);
            return expanded;
        }
    }

    public static class StaticWeakListReferenceMutationPrefix
    {
        public static bool AddPrefix(object __instance, object iItem)
        {
            bool lockTaken = false;
            try
            {
                Monitor.Enter(__instance);
                lockTaken = true;
                StaticCollectionState state = StaticCollectionState.For(
                    __instance,
                    "Magicka.StaticWeakList`1");
                WeakReference[] objects =
                    (WeakReference[])state.GetObjects(__instance);
                int count = state.GetCount(__instance);
                objects = EnsureCapacity(state, __instance, objects, count);
                objects[count].Target = iItem;
                state.SetCount(__instance, count + 1);
                return false;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(__instance);
            }
        }

        public static bool InsertPrefix(
            object __instance,
            int iIndex,
            object iItem)
        {
            bool lockTaken = false;
            try
            {
                Monitor.Enter(__instance);
                lockTaken = true;
                StaticCollectionState state = StaticCollectionState.For(
                    __instance,
                    "Magicka.StaticWeakList`1");
                int count = state.GetCount(__instance);
                if (iIndex < 0 || iIndex > count)
                    throw new IndexOutOfRangeException();
                WeakReference[] objects =
                    (WeakReference[])state.GetObjects(__instance);
                objects = EnsureCapacity(state, __instance, objects, count);
                for (int index = count; index > iIndex; index--)
                    objects[index].Target = objects[index - 1].Target;
                objects[iIndex].Target = iItem;
                state.SetCount(__instance, count + 1);
                return false;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(__instance);
            }
        }

        public static bool ExpandPrefix(object __instance, int ammount)
        {
            bool lockTaken = false;
            try
            {
                Monitor.Enter(__instance);
                lockTaken = true;
                StaticCollectionState state = StaticCollectionState.For(
                    __instance,
                    "Magicka.StaticWeakList`1");
                WeakReference[] objects =
                    (WeakReference[])state.GetObjects(__instance);
                int growth = ammount > 0 ? ammount : 0;
                WeakReference[] expanded = new WeakReference[
                    checked(objects.Length + growth)];
                Array.Copy(objects, expanded, objects.Length);
                Initialize(expanded, objects.Length);
                state.SetObjects(__instance, expanded);
                return false;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(__instance);
            }
        }

        private static WeakReference[] EnsureCapacity(
            StaticCollectionState state,
            object instance,
            WeakReference[] objects,
            int count)
        {
            if (count < objects.Length)
                return objects;
            int growth = objects.Length == 0 ? 4 : objects.Length;
            WeakReference[] expanded = new WeakReference[
                checked(objects.Length + growth)];
            Array.Copy(objects, expanded, objects.Length);
            Initialize(expanded, objects.Length);
            state.SetObjects(instance, expanded);
            return expanded;
        }

        private static void Initialize(WeakReference[] objects, int start)
        {
            for (int index = start; index < objects.Length; index++)
                objects[index] = new WeakReference(null);
        }
    }

    public static class StaticListMutationPrefix<TInstance, TItem>
    {
        public static bool AddPrefix(TInstance __instance, TItem iItem)
        {
            return StaticListReferenceMutationPrefix.AddPrefix(
                __instance,
                iItem);
        }

        public static bool InsertPrefix(
            TInstance __instance,
            int iIndex,
            TItem iItem)
        {
            return StaticListReferenceMutationPrefix.InsertPrefix(
                __instance,
                iIndex,
                iItem);
        }
    }
}
