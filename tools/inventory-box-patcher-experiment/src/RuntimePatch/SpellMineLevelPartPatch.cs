using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SpellMineLevelPartPatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.SpellMine";

        private static FieldInfo animatedLevelPartField;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "SpellMine animated level-part release",
                "org.magickacommunitypatch.spell-mine-level-part",
                FindTarget,
                typeof(SpellMineLevelPartPatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            Type spellMine = targetAssembly.GetType(TypeName, false);
            return spellMine != null &&
                spellMine.GetField(
                    "mAnimatedLevelPart",
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly) != null;
        }

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type spellMine = Configure(targetAssembly);
            MethodInfo method = spellMine.GetMethod(
                "Deinitialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    spellMine.FullName,
                    "Deinitialize");
            return method;
        }

        private static Type Configure(Assembly targetAssembly)
        {
            Type spellMine = targetAssembly.GetType(TypeName, true);
            Type animatedLevelPart = targetAssembly.GetType(
                "Magicka.Levels.AnimatedLevelPart",
                true);
            animatedLevelPartField = spellMine.GetField(
                "mAnimatedLevelPart",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (animatedLevelPartField == null ||
                animatedLevelPartField.FieldType != animatedLevelPart)
                throw new MissingFieldException(
                    spellMine.FullName,
                    "mAnimatedLevelPart");
            return spellMine;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            Configure(
                RuntimeMember.FindLoadedType(TypeName).Assembly);
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int returns = 0;
            int writes = 0;
            int returnIndex = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Ret)
                {
                    returns++;
                    returnIndex = index;
                }
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, animatedLevelPartField))
                    writes++;
            }
            if (returns != 1 || writes != 0)
                throw new InvalidOperationException(
                    "Expected one SpellMine.Deinitialize return and no " +
                    "existing animated-level-part writes; found returns=" +
                    returns + ", writes=" + writes + ".");

            CodeInstruction load = new CodeInstruction(OpCodes.Ldarg_0);
            load.labels.AddRange(result[returnIndex].labels);
            load.blocks.AddRange(result[returnIndex].blocks);
            result[returnIndex].labels.Clear();
            result[returnIndex].blocks.Clear();
            result.InsertRange(
                returnIndex,
                new CodeInstruction[]
                {
                    load,
                    new CodeInstruction(OpCodes.Ldnull),
                    new CodeInstruction(
                        OpCodes.Stfld,
                        animatedLevelPartField)
                });
            return result;
        }
    }
}
