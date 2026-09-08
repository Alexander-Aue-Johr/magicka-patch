using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class NetworkClientRulesetPatch
    {
        private static Type rulesetMessageType;
        private static MethodInfo levelGetter;
        private static MethodInfo currentSceneGetter;
        private static MethodInfo rulesetGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "NetworkClient detached RulesetUpdate guard",
                "org.magickacommunitypatch.network-client-ruleset-update",
                FindReadMessage,
                typeof(NetworkClientRulesetPatch).GetMethod("Transpiler"));

        private static MethodInfo FindReadMessage(Assembly targetAssembly)
        {
            Type clientType = targetAssembly.GetType(
                "Magicka.Network.NetworkClient",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type levelType = targetAssembly.GetType(
                "Magicka.Levels.Level",
                true);
            Type sceneType = targetAssembly.GetType(
                "Magicka.Levels.GameScene",
                true);
            rulesetMessageType = targetAssembly.GetType(
                "Magicka.Network.RulesetMessage",
                true);
            levelGetter = RequireGetter(playStateType, "Level");
            currentSceneGetter = RequireGetter(levelType, "CurrentScene");
            rulesetGetter = RequireGetter(sceneType, "RuleSet");

            MethodInfo match = null;
            MethodInfo[] methods = clientType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "ReadMessage" ||
                    method.ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(System.IO.BinaryReader))
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple NetworkClient.ReadMessage methods matched.");
                match = method;
            }
            if (match == null)
                throw new MissingMethodException(clientType.FullName, "ReadMessage");
            return match;
        }

        private static MethodInfo RequireGetter(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            MethodInfo getter = property == null ? null : property.GetGetMethod(true);
            if (getter == null)
                throw new MissingMemberException(type.FullName, name);
            return getter;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int update = -1;
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if ((result[index].opcode != OpCodes.Call &&
                    result[index].opcode != OpCodes.Callvirt) ||
                    method == null || method.Name != "NetworkUpdate")
                    continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 ||
                    parameters[0].ParameterType !=
                        rulesetMessageType.MakeByRefType())
                    continue;
                if (update >= 0)
                    throw new InvalidOperationException(
                        "NetworkClient.ReadMessage contains multiple Ruleset updates.");
                update = index;
            }
            int callStart = update - 5;
            if (callStart < 0 || update + 1 >= result.Count ||
                !IsLocalLoad(result[callStart]) ||
                !IsCall(result[callStart + 1], levelGetter) ||
                !IsCall(result[callStart + 2], currentSceneGetter) ||
                !IsCall(result[callStart + 3], rulesetGetter) ||
                !IsLocalAddressLoad(result[callStart + 4]))
                throw new InvalidOperationException(
                    "NetworkClient RulesetUpdate call shape changed.");

            int start = callStart;
            int searchStart = Math.Max(1, update - 20);
            for (int index = update - 1; index >= searchStart; index--)
            {
                if (IsCall(result[index], levelGetter) &&
                    IsLocalLoad(result[index - 1]))
                    start = index - 1;
            }

            Label skip = generator.DefineLabel();
            result[update + 1].labels.Add(skip);
            CodeInstruction load = Clone(result[start]);
            CodeInstruction test = new CodeInstruction(
                OpCodes.Call,
                typeof(NetworkClientRulesetPatch).GetMethod("IsReady"));
            CodeInstruction branch = new CodeInstruction(OpCodes.Brfalse, skip);
            load.labels.AddRange(result[start].labels);
            load.blocks.AddRange(result[start].blocks);
            result[start].labels.Clear();
            result[start].blocks.Clear();
            result.InsertRange(
                start,
                new CodeInstruction[] { load, test, branch });
            return result;
        }

        private static bool IsCall(
            CodeInstruction instruction,
            MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloc ||
                instruction.opcode == OpCodes.Ldloc_S ||
                instruction.opcode == OpCodes.Ldloc_0 ||
                instruction.opcode == OpCodes.Ldloc_1 ||
                instruction.opcode == OpCodes.Ldloc_2 ||
                instruction.opcode == OpCodes.Ldloc_3;
        }

        private static bool IsLocalAddressLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloca ||
                instruction.opcode == OpCodes.Ldloca_S;
        }

        private static CodeInstruction Clone(CodeInstruction instruction)
        {
            return new CodeInstruction(instruction.opcode, instruction.operand);
        }

        public static bool IsReady(object playState)
        {
            if (playState == null)
                return false;
            object level = levelGetter.Invoke(playState, null);
            if (level == null)
                return false;
            object scene = currentSceneGetter.Invoke(level, null);
            return scene != null && rulesetGetter.Invoke(scene, null) != null;
        }
    }
}
