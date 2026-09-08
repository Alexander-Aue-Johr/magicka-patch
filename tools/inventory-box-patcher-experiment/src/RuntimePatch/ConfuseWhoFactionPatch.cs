using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ConfuseWhoFactionPatch
    {
        private static MethodInfo templateGetter;
        private static MethodInfo templateFactionGetter;
        private static MethodInfo characterFactionGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "ConfuseWho detached victim faction cleanup",
                "org.magickacommunitypatch.confuse-who-faction",
                FindTarget,
                typeof(ConfuseWhoFactionPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type confuseWho = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.ConfuseWho",
                true);
            Type character = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type template = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            templateGetter = RequireGetter(character, "Template");
            templateFactionGetter = RequireGetter(template, "Faction");
            characterFactionGetter = RequireGetter(character, "Faction");

            MethodInfo result = null;
            MethodInfo[] methods = confuseWho.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "Update" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType.FullName !=
                        "PolygonHead.DataChannel" ||
                    parameters[1].ParameterType != typeof(float))
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        "Multiple ConfuseWho.Update methods matched.");
                result = methods[index];
            }
            if (result == null)
                throw new MissingMethodException(confuseWho.FullName, "Update");
            return result;
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
                        "Multiple ConfuseWho template-faction reads matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "ConfuseWho template-faction read was not found.");

            result[match].opcode = OpCodes.Callvirt;
            result[match].operand = characterFactionGetter;
            result[match + 1].opcode = OpCodes.Nop;
            result[match + 1].operand = null;
            return result;
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
