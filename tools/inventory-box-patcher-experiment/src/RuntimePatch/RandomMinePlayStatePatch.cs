using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class RandomMinePlayStatePatch
    {
        private static FieldInfo playStateField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "RandomMine unused play-state release",
                "org.magickacommunitypatch.random-mine-play-state",
                FindTarget,
                typeof(RandomMinePlayStatePatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type randomMineType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.RandomMine",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = randomMineType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (playStateField == null || playStateField.FieldType != playStateType)
                throw new MissingFieldException(randomMineType.FullName, "mPlayState");

            MethodInfo target = null;
            MethodInfo[] methods = randomMineType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "Execute" &&
                    methods[index].ReturnType == typeof(bool) &&
                    parameters.Length == 2 &&
                    parameters[0].ParameterType.FullName ==
                        "Microsoft.Xna.Framework.Vector3" &&
                    parameters[1].ParameterType == playStateType)
                {
                    if (target != null)
                        throw new InvalidOperationException(
                            "Multiple RandomMine position Execute methods matched.");
                    target = methods[index];
                }
            }
            if (target == null)
                throw new MissingMethodException(randomMineType.FullName, "Execute");
            return target;
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
                        "Multiple RandomMine play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "RandomMine play-state assignment was not found.");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }
    }
}
