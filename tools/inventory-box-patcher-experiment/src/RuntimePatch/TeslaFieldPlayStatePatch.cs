using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class TeslaFieldPlayStatePatch
    {
        private static FieldInfo legacyPlayStateField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.ConstructorTranspile(
                "TeslaField play-state release",
                "org.magickacommunitypatch.tesla-field-play-state-release",
                FindConstructor,
                typeof(TeslaFieldPlayStatePatch).GetMethod("Transpiler"));

        private static ConstructorInfo FindConstructor(Assembly targetAssembly)
        {
            Type teslaFieldType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.TeslaField",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = teslaFieldType.GetField(
                "mPlaystate",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(
                    teslaFieldType.FullName,
                    "mPlaystate");

            ConstructorInfo constructor = teslaFieldType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playStateType },
                null);
            if (constructor == null)
                throw new MissingMethodException(
                    teslaFieldType.FullName,
                    ".ctor(PlayState)");
            return constructor;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int matches = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_1 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, legacyPlayStateField))
                    continue;
                assignment = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one TeslaField play-state assignment, found " +
                    matches + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }
    }
}
