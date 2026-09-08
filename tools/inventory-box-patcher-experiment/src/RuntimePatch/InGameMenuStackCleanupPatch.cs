using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class InGameMenuStackCleanupPatch
    {
        private static FieldInfo menuStackField;
        private static MethodInfo menuStackClear;
        private static MethodInfo bossFightClear;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "In-game menu stack cleanup",
                "org.magickacommunitypatch.in-game-menu-stack-cleanup",
                FindPlayStateDispose,
                typeof(InGameMenuStackCleanupPatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type menuType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenu",
                true);
            menuStackField = menuType.GetField(
                "sMenuStack",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (menuStackField == null ||
                !typeof(ICollection).IsAssignableFrom(menuStackField.FieldType))
                throw new MissingFieldException(menuType.FullName, "sMenuStack");
            menuStackClear = menuStackField.FieldType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (menuStackClear == null || menuStackClear.ReturnType != typeof(void))
                throw new MissingMethodException(
                    menuStackField.FieldType.FullName,
                    "Clear");

            Type bossFightType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.BossFight",
                true);
            bossFightClear = bossFightType.GetMethod(
                "Clear",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (bossFightClear == null || bossFightClear.ReturnType != typeof(void))
                throw new MissingMethodException(bossFightType.FullName, "Clear");

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
                    Object.Equals(called, bossFightClear))
                {
                    anchor = index;
                    matches++;
                }
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one BossFight.Clear call in PlayState.Dispose, found " +
                    matches + ".");

            result.Insert(
                anchor + 1,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(InGameMenuStackCleanupPatch).GetMethod(
                        "ClearMenuStack")));
            return result;
        }

        public static void ClearMenuStack()
        {
            if (menuStackField == null || menuStackClear == null)
                throw new InvalidOperationException(
                    "In-game menu stack contracts have not been initialized.");

            object stack = menuStackField.GetValue(null);
            if (stack != null)
                menuStackClear.Invoke(stack, new object[0]);
        }
    }
}
