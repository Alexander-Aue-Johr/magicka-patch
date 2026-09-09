using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ArcaneBladePlayStatePatch
    {
        private const string BladeTypeName =
            "Magicka.GameLogic.Spells.ArcaneBlade";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "ArcaneBlade play-state release",
                "org.magickacommunitypatch.arcane-blade-state-release",
                FindInitialize,
                typeof(ArcaneBladePlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "ArcaneBlade current render scene",
                "org.magickacommunitypatch.arcane-blade-current-scene",
                FindUpdate,
                typeof(ArcaneBladePlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type blade;
            Type playState;
            Configure(targetAssembly, out blade, out playState);
            Type item = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Items.Item",
                true);
            Type elements = targetAssembly.GetType("Magicka.Elements", true);
            MethodInfo method = blade.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState, item, elements, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(blade.FullName, "Initialize");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type blade;
            Type ignoredPlayState;
            Configure(targetAssembly, out blade, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = blade.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(blade.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type blade,
            out Type playState)
        {
            blade = targetAssembly.GetType(BladeTypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = blade.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playState)
                throw new MissingFieldException(blade.FullName, "mPlayState");
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

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
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
                    result[index - 1].opcode != OpCodes.Ldarg_1 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one ArcaneBlade play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            ReplaceReads(result, 1, "Initialize");
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            ReplaceReads(result, 1, "Update");
            return result;
        }

        private static void ReplaceReads(
            List<CodeInstruction> instructions,
            int expected,
            string methodName)
        {
            int replacements = 0;
            for (int index = 1; index < instructions.Count; index++)
            {
                FieldInfo field = instructions[index].operand as FieldInfo;
                if (instructions[index - 1].opcode != OpCodes.Ldarg_0 ||
                    instructions[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                instructions[index - 1].opcode = OpCodes.Call;
                instructions[index - 1].operand = recentPlayStateGetter;
                instructions[index].opcode = OpCodes.Nop;
                instructions[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " ArcaneBlade " + methodName +
                    " play-state read, found " + replacements + ".");
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type blade = RuntimeMember.FindLoadedType(BladeTypeName);
            Type ignoredBlade;
            Type ignoredPlayState;
            Configure(
                blade.Assembly,
                out ignoredBlade,
                out ignoredPlayState);
        }
    }
}
