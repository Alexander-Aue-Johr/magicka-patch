using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterSpellUsagePatch
    {
        private static Type networkGamerType;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Character detached-Gamer spell statistics guard",
                "org.magickacommunitypatch.character-spell-usage",
                FindCastSpell,
                typeof(CharacterSpellUsagePatch).GetMethod("Transpiler"));

        private static MethodInfo FindCastSpell(Assembly targetAssembly)
        {
            Type characterType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            networkGamerType = targetAssembly.GetType(
                "Magicka.Gamers.NetworkGamer",
                true);
            MethodInfo method = characterType.GetMethod(
                "CastSpell",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(bool), typeof(string) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(characterType.FullName, "CastSpell");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            List<int> gamerChecks = new List<int>();
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Isinst &&
                    Object.Equals(result[index].operand, networkGamerType))
                    gamerChecks.Add(index);
            }
            if (gamerChecks.Count != 2)
                throw new InvalidOperationException(
                    "Character.CastSpell NetworkGamer checks changed: " +
                    gamerChecks.Count + ".");

            int statisticsCheck = gamerChecks[1];
            int branch = NextMeaningfulInstruction(result, statisticsCheck + 1);
            OpCode branchOpcode = result[branch].opcode;
            if (branchOpcode != OpCodes.Brtrue &&
                branchOpcode != OpCodes.Brtrue_S)
                throw new InvalidOperationException(
                    "Character.CastSpell statistics branch changed.");

            result[statisticsCheck].opcode = OpCodes.Call;
            result[statisticsCheck].operand = typeof(CharacterSpellUsagePatch)
                .GetMethod("CanRecordLocalSpellUsage");
            result[branch].opcode = branchOpcode == OpCodes.Brtrue
                ? OpCodes.Brfalse
                : OpCodes.Brfalse_S;
            return result;
        }

        public static bool CanRecordLocalSpellUsage(object gamer)
        {
            return gamer != null &&
                CanRecordLocalSpellUsageType(gamer.GetType());
        }

        public static bool CanRecordLocalSpellUsageType(Type gamerType)
        {
            return gamerType != null &&
                !networkGamerType.IsAssignableFrom(gamerType);
        }

        private static int NextMeaningfulInstruction(
            IList<CodeInstruction> instructions,
            int start)
        {
            for (int index = start; index < instructions.Count; index++)
            {
                if (instructions[index].opcode != OpCodes.Nop)
                    return index;
            }
            throw new InvalidOperationException(
                "Character.CastSpell statistics branch is missing.");
        }
    }
}
