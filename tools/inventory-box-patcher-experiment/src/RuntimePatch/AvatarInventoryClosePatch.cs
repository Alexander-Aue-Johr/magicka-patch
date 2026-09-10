using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AvatarInventoryClosePatch
    {
        private static MethodInfo closeMethod;
        private static FieldInfo playStateField;
        private static PropertyInfo inventoryProperty;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Avatar released inventory close",
                "org.magickacommunitypatch.avatar-inventory-close",
                FindDeinitialize,
                typeof(AvatarInventoryClosePatch).GetMethod("Transpiler"));

        private static MethodInfo FindDeinitialize(Assembly assembly)
        {
            Type avatar = assembly.GetType(
                "Magicka.GameLogic.Entities.Avatar", true);
            MethodInfo method = avatar.GetMethod(
                "Deinitialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null)
                throw new MissingMethodException(avatar.FullName, "Deinitialize");
            Type entity = avatar;
            while (entity != null && entity.FullName !=
                "Magicka.GameLogic.Entities.Entity")
                entity = entity.BaseType;
            if (entity == null)
                throw new TypeLoadException(
                    "Magicka.GameLogic.Entities.Entity");
            playStateField = entity.GetField(
                "mPlayState", BindingFlags.Instance | BindingFlags.NonPublic);
            if (playStateField == null)
                throw new MissingFieldException(entity.FullName, "mPlayState");
            inventoryProperty = playStateField.FieldType.GetProperty(
                "Inventory", BindingFlags.Instance | BindingFlags.Public);
            if (inventoryProperty == null)
                throw new MissingMemberException(
                    "Avatar inventory ownership contract is incomplete.");
            closeMethod = null;
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            MethodInfo helper = typeof(AvatarInventoryClosePatch).GetMethod(
                "CloseIfAvailable");
            int replaced = 0;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (called == null || called.Name != "Close" ||
                    called.DeclaringType == null ||
                    called.DeclaringType.FullName !=
                        "Magicka.GameLogic.UI.InventoryBox")
                    continue;
                ParameterInfo[] parameters = called.GetParameters();
                if (parameters.Length != 1 ||
                    parameters[0].ParameterType.FullName !=
                        "Magicka.GameLogic.Entities.Character")
                    throw new InvalidOperationException(
                        "Avatar inventory close signature changed.");
                if (index < 4 || result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "Avatar inventory close IL shape changed.");
                FieldInfo loadedPlayState = result[index - 3].operand as FieldInfo;
                MethodInfo getInventory = result[index - 2].operand as MethodInfo;
                if (loadedPlayState != playStateField || getInventory == null ||
                    getInventory != inventoryProperty.GetGetMethod())
                    throw new InvalidOperationException(
                        "Avatar inventory lookup IL shape changed.");
                for (int clear = index - 4; clear <= index - 2; clear++)
                {
                    result[clear].opcode = OpCodes.Nop;
                    result[clear].operand = null;
                }
                closeMethod = called;
                result[index].opcode = OpCodes.Call;
                result[index].operand = helper;
                replaced++;
            }
            if (replaced != 1)
                throw new InvalidOperationException(
                    "Expected one Avatar inventory close, found " +
                    replaced + ".");
            return result;
        }

        public static void CloseIfAvailable(object avatar)
        {
            object playState = playStateField.GetValue(avatar);
            object inventory = playState == null
                ? null
                : inventoryProperty.GetValue(playState, null);
            if (inventory != null)
                closeMethod.Invoke(inventory, new object[] { avatar });
        }
    }
}
