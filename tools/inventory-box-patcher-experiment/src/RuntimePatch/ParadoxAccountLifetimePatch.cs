using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ParadoxAccountLifetimePatch
    {
        private static MethodInfo accountDestroy;
        private static MethodInfo paradoxServicesInstanceGetter;
        private static MethodInfo paradoxServicesDispose;

        internal static readonly RuntimePatchDefinition MenuExitDefinition =
            RuntimePatchDefinition.Transpile(
                "Paradox account menu-exit lifetime",
                "org.magickacommunitypatch.paradox-account-menu-exit",
                FindMenuExit,
                typeof(ParadoxAccountLifetimePatch).GetMethod(
                    "MenuExitTranspiler"));

        internal static readonly RuntimePatchDefinition EndRunDefinition =
            RuntimePatchDefinition.Transpile(
                "Paradox account shutdown cleanup",
                "org.magickacommunitypatch.paradox-account-end-run",
                FindEndRun,
                typeof(ParadoxAccountLifetimePatch).GetMethod(
                    "EndRunTranspiler"));

        private static MethodInfo FindMenuExit(Assembly targetAssembly)
        {
            Configure(targetAssembly);
            Type menuState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.MenuState",
                true);
            MethodInfo method = menuState.GetMethod(
                "OnExit",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(menuState.FullName, "OnExit");
            return method;
        }

        private static MethodInfo FindEndRun(Assembly targetAssembly)
        {
            Configure(targetAssembly);
            Type game = targetAssembly.GetType("Magicka.Game", true);
            MethodInfo method = game.GetMethod(
                "EndRun",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(game.FullName, "EndRun");
            return method;
        }

        private static void Configure(Assembly targetAssembly)
        {
            Type accountSaveData = targetAssembly.GetType(
                "Magicka.Storage.ParadoxAccountSaveData",
                true);
            Type scopedSingleton = targetAssembly.GetType(
                "Magicka.Misc.ScopedSingleton`1",
                true);
            Type closedScopedSingleton = scopedSingleton.MakeGenericType(
                new Type[] { accountSaveData });
            accountDestroy = closedScopedSingleton.GetMethod(
                "Destroy",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            Type paradoxServices = targetAssembly.GetType(
                "Magicka.WebTools.Paradox.ParadoxServices",
                true);
            Type singleton = targetAssembly.GetType(
                "Magicka.Misc.Singleton`1",
                true);
            Type closedSingleton = singleton.MakeGenericType(
                new Type[] { paradoxServices });
            PropertyInfo instance = closedSingleton.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public);
            paradoxServicesInstanceGetter = instance == null
                ? null
                : instance.GetGetMethod();
            paradoxServicesDispose = paradoxServices.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);

            if (accountDestroy == null ||
                paradoxServicesInstanceGetter == null ||
                paradoxServicesDispose == null)
                throw new MissingMemberException(
                    "Paradox account lifetime contracts are incomplete.");
        }

        public static IEnumerable<CodeInstruction> MenuExitTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Call ||
                    !Object.Equals(result[index].operand, accountDestroy))
                    continue;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one Paradox account destroy call in MenuState.OnExit, found " +
                    matches + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> EndRunTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            int matches = 0;
            for (int index = 0; index < result.Count - 1; index++)
            {
                if (result[index].opcode != OpCodes.Call ||
                    !Object.Equals(
                        result[index].operand,
                        paradoxServicesInstanceGetter) ||
                    result[index + 1].opcode != OpCodes.Callvirt ||
                    !Object.Equals(
                        result[index + 1].operand,
                        paradoxServicesDispose))
                    continue;
                anchor = index + 1;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one ParadoxServices.Dispose call in Game.EndRun, found " +
                    matches + ".");
            result.Insert(
                anchor + 1,
                new CodeInstruction(OpCodes.Call, accountDestroy));
            return result;
        }
    }
}
