using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class GreaseLumpPlayStatePatch
    {
        private const string LumpTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
            "GreaseLump";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "GreaseLump play-state release",
                "org.magickacommunitypatch.grease-lump-state-release",
                FindExecute,
                typeof(GreaseLumpPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "GreaseLump current play state",
                "org.magickacommunitypatch.grease-lump-current-state",
                FindUpdate,
                typeof(GreaseLumpPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type lump;
            Type playState;
            Configure(targetAssembly, out lump, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo method = lump.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { owner, playState },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(lump.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type lump;
            Type ignoredPlayState;
            Configure(targetAssembly, out lump, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = lump.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(lump.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type lump,
            out Type playState)
        {
            lump = targetAssembly.GetType(LumpTypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = lump.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playState)
                throw new MissingFieldException(lump.FullName, "mPlayState");

            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    writes++;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one GreaseLump play-state assignment, found " +
                    writes + ".");
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
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != 2)
                throw new InvalidOperationException(
                    "Expected two GreaseLump Update play-state reads, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type lump = RuntimeMember.FindLoadedType(LumpTypeName);
            Type ignoredLump;
            Type ignoredPlayState;
            Configure(
                lump.Assembly,
                out ignoredLump,
                out ignoredPlayState);
        }
    }
}
