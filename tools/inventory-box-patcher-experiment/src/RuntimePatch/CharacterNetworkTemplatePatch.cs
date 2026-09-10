using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterNetworkTemplatePatch
    {
        private const BindingFlags Members = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;
        private static FieldInfo templateField;
        private static MethodInfo networkAction;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "NetworkClient character template recovery",
                "org.magickacommunitypatch.network-client-character-template",
                FindReadMessage,
                typeof(CharacterNetworkTemplatePatch).GetMethod("Transpiler"));

        private static MethodInfo FindReadMessage(Assembly assembly)
        {
            Type client = assembly.GetType("Magicka.Network.NetworkClient", true);
            Type character = assembly.GetType("Magicka.GameLogic.Entities.Character", true);
            Type message = assembly.GetType("Magicka.Network.CharacterActionMessage", true);
            templateField = character.GetField("mTemplate", Members | BindingFlags.DeclaredOnly);
            networkAction = character.GetMethod("NetworkAction", Members | BindingFlags.DeclaredOnly,
                null, new Type[] { message.MakeByRefType() }, null);
            if (templateField == null || networkAction == null)
                throw new MissingMemberException("Character network-template contract is incomplete.");
            GameSceneSavedCharacterPatch.InitializeFor(assembly);
            MethodInfo[] methods = client.GetMethods(Members | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == "ReadMessage" && methods[index].GetParameters().Length == 2)
                    return methods[index];
            throw new MissingMethodException(client.FullName, "ReadMessage");
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int call = -1;
            for (int index = 0; index < result.Count; index++)
                if ((result[index].opcode == OpCodes.Call || result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(result[index].operand, networkAction))
                {
                    if (call >= 0)
                        throw new InvalidOperationException("Multiple client Character.NetworkAction calls found.");
                    call = index;
                }
            if (call < 2 || !IsLocalLoad(result[call - 2]) ||
                !IsLocalAddressLoad(result[call - 1]))
                throw new InvalidOperationException("Client Character.NetworkAction call shape changed.");
            Label skip = generator.DefineLabel();
            result[call + 1].labels.Add(skip);
            CodeInstruction load = Clone(result[call - 2]);
            load.labels.AddRange(result[call - 2].labels);
            load.blocks.AddRange(result[call - 2].blocks);
            result[call - 2].labels.Clear();
            result[call - 2].blocks.Clear();
            result.InsertRange(call - 2, new CodeInstruction[]
            {
                load,
                new CodeInstruction(OpCodes.Call,
                    typeof(CharacterNetworkTemplatePatch).GetMethod("EnsureTemplate")),
                new CodeInstruction(OpCodes.Brfalse, skip)
            });
            return result;
        }

        public static bool EnsureTemplate(object character)
        {
            if (character == null)
                return false;
            return templateField.GetValue(character) != null ||
                GameSceneSavedCharacterPatch.ReapplyNpcTemplate(character);
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S ||
                instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Ldloc_3;
        }

        private static bool IsLocalAddressLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S;
        }

        private static CodeInstruction Clone(CodeInstruction instruction)
        {
            return new CodeInstruction(instruction.opcode, instruction.operand);
        }
    }
}
