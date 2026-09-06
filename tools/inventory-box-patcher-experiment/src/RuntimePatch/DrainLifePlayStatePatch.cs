using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class DrainLifePlayStatePatch
    {
        private static FieldInfo playStateField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "DrainLife unused play-state release",
                "org.magickacommunitypatch.drain-life-play-state",
                FindTarget,
                typeof(DrainLifePlayStatePatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type drainLifeType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.DrainLife",
                true);
            Type ownerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = drainLifeType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo method = drainLifeType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { ownerType, playStateType },
                null);
            if (playStateField == null || playStateField.FieldType != playStateType)
                throw new MissingFieldException(drainLifeType.FullName, "mPlayState");
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(drainLifeType.FullName, "Execute");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode != OpCodes.Stfld ||
                    field == null || field != playStateField ||
                    result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2)
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple DrainLife play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "DrainLife play-state assignment was not found.");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }
    }
}
