using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ConfuseFactionPatch
    {
        private const string ConfuseTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Confuse";

        private static MethodInfo templateGetter;
        private static MethodInfo templateFactionGetter;
        private static MethodInfo characterFactionGetter;
        private static Type characterType;
        private static Type factionType;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Confuse detached target faction cleanup",
                "org.magickacommunitypatch.confuse-faction",
                FindOnRemove,
                typeof(ConfuseFactionPatch).GetMethod("Transpiler"));

        private static MethodInfo FindOnRemove(Assembly targetAssembly)
        {
            Type confuse = targetAssembly.GetType(ConfuseTypeName, true);
            characterType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type template = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            factionType = targetAssembly.GetType("Magicka.Factions", true);
            templateGetter = RequireGetter(characterType, "Template");
            templateFactionGetter = RequireGetter(template, "Faction");
            characterFactionGetter = RequireGetter(characterType, "Faction");

            MethodInfo onRemove = confuse.GetMethod(
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (onRemove == null || onRemove.ReturnType != typeof(void))
                throw new MissingMethodException(confuse.FullName, "OnRemove");
            return onRemove;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int match = -1;
            for (int index = 0; index + 1 < result.Count; index++)
            {
                if (!Calls(result[index], templateGetter) ||
                    !Calls(result[index + 1], templateFactionGetter))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple Confuse template-faction reads matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "Confuse template-faction read was not found.");

            MethodInfo resolver = typeof(ConfuseFactionPatch).GetMethod(
                "ResolveFaction").MakeGenericMethod(
                    new Type[] { characterType, factionType });
            result[match].opcode = OpCodes.Call;
            result[match].operand = resolver;
            result[match + 1].opcode = OpCodes.Nop;
            result[match + 1].operand = null;
            return result;
        }

        public static TFaction ResolveFaction<TCharacter, TFaction>(
            TCharacter character)
        {
            object target = character;
            object template = templateGetter.Invoke(target, null);
            object faction = template == null
                ? characterFactionGetter.Invoke(target, null)
                : templateFactionGetter.Invoke(template, null);
            return (TFaction)faction;
        }

        private static MethodInfo RequireGetter(
            Type type,
            string propertyName)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (getter == null)
                throw new MissingMethodException(
                    type.FullName,
                    "get_" + propertyName);
            return getter;
        }

        private static bool Calls(
            CodeInstruction instruction,
            MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }
    }
}
