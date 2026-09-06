using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class StarGazeFactionPatch
    {
        private static MethodInfo templateGetter;
        private static MethodInfo templateFactionGetter;
        private static MethodInfo characterFactionGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "StarGaze detached victim faction cleanup",
                "org.magickacommunitypatch.star-gaze-faction",
                FindTarget,
                typeof(StarGazeFactionPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type starGazeType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.StarGaze",
                true);
            Type characterType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            templateGetter = RequireGetter(characterType, "Template");
            templateFactionGetter = RequireGetter(templateType, "Faction");
            characterFactionGetter = RequireGetter(characterType, "Faction");

            MethodInfo method = null;
            MethodInfo[] methods = starGazeType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "Update" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType.FullName != "PolygonHead.DataChannel" ||
                    parameters[1].ParameterType != typeof(float))
                    continue;
                if (method != null)
                    throw new InvalidOperationException(
                        "Multiple StarGaze.Update methods matched.");
                method = methods[index];
            }
            if (method == null)
                throw new MissingMethodException(starGazeType.FullName, "Update");
            return method;
        }

        private static MethodInfo RequireGetter(Type type, string propertyName)
        {
            PropertyInfo property = type.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo getter = property == null ? null : property.GetGetMethod(true);
            if (getter == null)
                throw new MissingMethodException(type.FullName, "get_" + propertyName);
            return getter;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int match = -1;
            for (int index = 0; index + 1 < result.Count; index++)
            {
                if (!Calls(result[index], templateGetter) ||
                    !Calls(result[index + 1], templateFactionGetter))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple StarGaze template-faction reads matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "StarGaze template-faction read was not found.");

            result[match].opcode = OpCodes.Callvirt;
            result[match].operand = characterFactionGetter;
            result[match + 1].opcode = OpCodes.Nop;
            result[match + 1].operand = null;
            return result;
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }
    }
}
