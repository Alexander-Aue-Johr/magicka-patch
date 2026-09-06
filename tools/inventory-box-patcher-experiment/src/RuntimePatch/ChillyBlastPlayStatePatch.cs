using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ChillyBlastPlayStatePatch
    {
        private const string ChillyBlastTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ChillyBlast";

        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "ChillyBlast play-state reference release",
                "org.magickacommunitypatch.chilly-blast-play-state-release",
                FindExecute,
                typeof(ChillyBlastPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "ChillyBlast current entity query",
                "org.magickacommunitypatch.chilly-blast-current-entity-query",
                FindUpdate,
                typeof(ChillyBlastPlayStatePatch).GetMethod("UpdateTranspiler"));

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            return targetAssembly.GetType(ChillyBlastTypeName, false) != null;
        }

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type chillyBlastType;
            Type playStateType;
            Configure(targetAssembly, out chillyBlastType, out playStateType);
            Type ownerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo method = chillyBlastType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { ownerType, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(chillyBlastType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type chillyBlastType;
            Type playStateType;
            Configure(targetAssembly, out chillyBlastType, out playStateType);
            MethodInfo result = null;
            MethodInfo[] methods = chillyBlastType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name != "Update" ||
                    methods[index].GetParameters().Length != 2 ||
                    methods[index].ReturnType != typeof(void))
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        "Multiple ChillyBlast.Update methods matched.");
                result = methods[index];
            }
            if (result == null)
                throw new MissingMethodException(chillyBlastType.FullName, "Update");
            return result;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type chillyBlastType,
            out Type playStateType)
        {
            chillyBlastType = targetAssembly.GetType(ChillyBlastTypeName, true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = chillyBlastType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(chillyBlastType.FullName, "mPlayState");

            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    field == null || field != legacyPlayStateField)
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple ChillyBlast play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "ChillyBlast play-state assignment was not found.");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    field == null || field != legacyPlayStateField)
                    continue;

                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != 2)
                throw new InvalidOperationException(
                    "Expected two ChillyBlast play-state reads, found " +
                    replacements + ".");
            return result;
        }
    }
}
