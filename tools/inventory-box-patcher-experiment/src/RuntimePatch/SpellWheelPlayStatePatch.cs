using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SpellWheelPlayStatePatch
    {
        private const string SpellWheelTypeName =
            "Magicka.GameLogic.UI.SpellWheel";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "SpellWheel play-state release",
                "org.magickacommunitypatch.spell-wheel-play-state-release",
                FindInitialize,
                typeof(SpellWheelPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "SpellWheel current render scene",
                "org.magickacommunitypatch.spell-wheel-current-render-scene",
                FindUpdate,
                typeof(SpellWheelPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindInitialize(Assembly assembly)
        {
            Type spellWheel;
            Type playState;
            Configure(assembly, out spellWheel, out playState);
            MethodInfo method = spellWheel.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    spellWheel.FullName,
                    "Initialize");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type spellWheel;
            Type ignoredPlayState;
            Configure(assembly, out spellWheel, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = spellWheel.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(spellWheel.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly assembly,
            out Type spellWheel,
            out Type playState)
        {
            spellWheel = assembly.GetType(SpellWheelTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = spellWheel.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null || playStateField.FieldType != playState)
                throw new MissingFieldException(spellWheel.FullName, "mPlayState");
            PropertyInfo recentPlayState = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
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
                    result[index - 1].opcode == OpCodes.Ldarg_1 &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one SpellWheel play-state assignment, found " +
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
            if (replacements != 1 && replacements != 3)
                throw new InvalidOperationException(
                    "Expected one or three SpellWheel play-state reads, found " +
                    replacements + ".");
            return result;
        }
    }
}
