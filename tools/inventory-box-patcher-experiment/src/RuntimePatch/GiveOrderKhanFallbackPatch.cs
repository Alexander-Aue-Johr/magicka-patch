using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GiveOrderKhanFallbackPatch
    {
        private static Type warlordType;
        private static MethodInfo executeTrigger;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Kahn kill-plane defeat fallback",
                "org.magickacommunitypatch.khan-killplane-fallback",
                FindExec,
                typeof(GiveOrderKhanFallbackPatch).GetMethod("Transpiler"));

        private static MethodInfo FindExec(Assembly targetAssembly)
        {
            warlordType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.WarlordCharacter",
                true);
            Type characterType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            Type gameSceneType = targetAssembly.GetType(
                "Magicka.Levels.GameScene",
                true);
            executeTrigger = gameSceneType.GetMethod(
                "ExecuteTrigger",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(int), characterType, typeof(bool) },
                null);
            if (executeTrigger == null || executeTrigger.ReturnType != typeof(void))
                throw new MissingMethodException(gameSceneType.FullName, "ExecuteTrigger");

            Type giveOrderType = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Actions.GiveOrder",
                true);
            MethodInfo exec = giveOrderType.GetMethod(
                "Exec",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (exec == null || exec.ReturnType != typeof(void))
                throw new MissingMethodException(giveOrderType.FullName, "Exec");
            return exec;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            List<int> setOrderCalls = new List<int>();
            for (int index = 0; index < result.Count; index++)
            {
                MethodInfo method = result[index].operand as MethodInfo;
                if ((result[index].opcode == OpCodes.Call ||
                    result[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == "SetOrder" &&
                    method.DeclaringType.FullName == "Magicka.AI.Agent")
                    setOrderCalls.Add(index);
            }
            if (setOrderCalls.Count != 2)
                throw new InvalidOperationException(
                    "Expected two Agent.SetOrder calls in GiveOrder.Exec, found " +
                    setOrderCalls.Count + ".");

            int firstCall = setOrderCalls[0];
            CodeInstruction characterLoad = FindCharacterLoad(result, firstCall);
            result.Insert(firstCall + 1, new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(firstCall + 2, Clone(characterLoad));
            result.Insert(
                firstCall + 3,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(GiveOrderKhanFallbackPatch).GetMethod("Handle")));
            return result;
        }

        public static void Handle(object order, object khan)
        {
            if (order == null || khan == null)
                return;
            string id = RuntimeMember.ReadField(order, "mID") as string;
            if (!String.Equals(id, "#boss_n06", StringComparison.OrdinalIgnoreCase) ||
                !warlordType.IsInstanceOfType(khan) ||
                !(bool)RuntimeMember.ReadField(khan, "mDead"))
                return;

            Array events = RuntimeMember.ReadField(order, "mEvents") as Array;
            if (events == null || events.Length == 0)
                return;
            object firstEvent = events.GetValue(0);
            object eventType = RuntimeMember.ReadField(firstEvent, "EventType");
            if (!String.Equals(
                Convert.ToString(eventType),
                "Animation",
                StringComparison.Ordinal))
                return;
            object animationEvent = RuntimeMember.ReadField(
                firstEvent,
                "AnimationEvent");
            int trigger = Convert.ToInt32(
                RuntimeMember.ReadField(animationEvent, "Trigger"));
            if (trigger == 0)
                return;

            object scene = RuntimeMember.ReadField(order, "mScene");
            executeTrigger.Invoke(scene, new object[] { trigger, khan, false });
        }

        private static CodeInstruction FindCharacterLoad(
            IList<CodeInstruction> instructions,
            int setOrderCall)
        {
            int first = Math.Max(0, setOrderCall - 20);
            for (int index = setOrderCall - 1; index > first; index--)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null &&
                    method.Name == "get_AI" &&
                    method.DeclaringType.FullName ==
                        "Magicka.GameLogic.Entities.NonPlayerCharacter" &&
                    IsLocalLoad(instructions[index - 1]))
                    return instructions[index - 1];
            }
            throw new InvalidOperationException(
                "The specific GiveOrder character local was not found.");
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

        private static CodeInstruction Clone(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }
    }
}
