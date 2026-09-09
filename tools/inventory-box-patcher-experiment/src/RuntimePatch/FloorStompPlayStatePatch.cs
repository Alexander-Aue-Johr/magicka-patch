using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class FloorStompPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.FloorStomp";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "FloorStomp play-state release",
                "org.magickacommunitypatch.floor-stomp-state",
                FindExecute,
                typeof(FloorStompPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "FloorStomp current play state",
                "org.magickacommunitypatch.floor-stomp-current-state",
                FindUpdate,
                typeof(FloorStompPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type floorStomp;
            Type playState;
            Configure(targetAssembly, out floorStomp, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                floorStomp,
                "Execute",
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type floorStomp;
            Type ignoredPlayState;
            Configure(targetAssembly, out floorStomp, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                floorStomp,
                "Update",
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type floorStomp,
            out Type playState)
        {
            floorStomp = targetAssembly.GetType(TypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = floorStomp.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playState)
                throw new MissingFieldException(
                    floorStomp.FullName,
                    "mPlayState");
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
                if (result[index - 2].opcode == OpCodes.Ldarg_0 &&
                    result[index - 1].opcode == OpCodes.Ldarg_2 &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one FloorStomp play-state assignment, found " +
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
            if (replacements != 6)
                throw new InvalidOperationException(
                    "Expected six FloorStomp Update play-state reads, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type floorStomp = RuntimeMember.FindLoadedType(TypeName);
            Type ignoredFloorStomp;
            Type ignoredPlayState;
            Configure(
                floorStomp.Assembly,
                out ignoredFloorStomp,
                out ignoredPlayState);
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
