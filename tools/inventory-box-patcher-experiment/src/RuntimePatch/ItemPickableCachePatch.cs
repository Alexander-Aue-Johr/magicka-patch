using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ItemPickableCachePatch
    {
        private static FieldInfo cacheField;
        private static MethodInfo clearHandlesMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Pickable item cache release",
                "org.magickacommunitypatch.pickable-item-cache-release",
                FindPlayStateDispose,
                typeof(ItemPickableCachePatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type itemType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Items.Item",
                true);
            cacheField = itemType.GetField(
                "sPickableCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (cacheField == null ||
                !cacheField.FieldType.IsGenericType ||
                cacheField.FieldType.GetGenericTypeDefinition().FullName !=
                    "System.Collections.Generic.Queue`1" ||
                cacheField.FieldType.GetGenericArguments().Length != 1 ||
                cacheField.FieldType.GetGenericArguments()[0] != itemType)
                throw new MissingFieldException(itemType.FullName, "sPickableCache");

            Type entityType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            clearHandlesMethod = entityType.GetMethod(
                "ClearHandles",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (clearHandlesMethod == null ||
                clearHandlesMethod.ReturnType != typeof(void))
                throw new MissingMethodException(entityType.FullName, "ClearHandles");

            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo dispose = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return dispose;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(called, clearHandlesMethod))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Entity.ClearHandles call in PlayState.Dispose, found " +
                    matches + ".");

            result.Insert(
                anchor + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(ItemPickableCachePatch).GetMethod("Release")));
            return result;
        }

        public static void Release()
        {
            if (cacheField == null)
                throw new InvalidOperationException(
                    "Pickable item cache contract has not been initialized.");
            cacheField.SetValue(null, null);
        }
    }
}
