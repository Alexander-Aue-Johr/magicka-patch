using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ProcessThreadAffinityPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Unavailable process thread guard",
                "org.magickacommunitypatch.process-thread-affinity",
                FindConstructor,
                typeof(ProcessThreadAffinityPatch).GetMethod("Transpiler"));

        private static ConstructorInfo FindConstructor(Assembly targetAssembly)
        {
            Type gameType = targetAssembly.GetType("Magicka.Game", true);
            ConstructorInfo constructor = gameType.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (constructor == null)
                throw new MissingMethodException(gameType.FullName, ".ctor");
            return constructor;
        }

        public static bool ShouldConfigureThread(
            bool available,
            int threadId,
            int currentThreadId)
        {
            return available && threadId == currentThreadId;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = 1; index + 2 < result.Count; index++)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (called == null || called.DeclaringType == null ||
                    called.DeclaringType.FullName !=
                        "System.Diagnostics.ProcessThread" ||
                    called.Name != "get_Id")
                    continue;
                if (!IsLocalLoad(result[index - 1].opcode) ||
                    !IsNotEqualBranch(result[index + 2].opcode) ||
                    !(result[index + 2].operand is Label))
                    throw new InvalidOperationException(
                        "ProcessThread affinity comparison shape changed.");

                result.Insert(
                    index,
                    new CodeInstruction(
                        OpCodes.Brfalse,
                        result[index + 2].operand));
                result.Insert(
                    index + 1,
                    CopyLocalLoad(result[index - 1]));
                index += 2;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one ProcessThread.Id affinity comparison, found " +
                    matches + ".");
            return result;
        }

        private static bool IsLocalLoad(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }

        private static bool IsNotEqualBranch(OpCode opcode)
        {
            return opcode == OpCodes.Bne_Un || opcode == OpCodes.Bne_Un_S;
        }

        private static CodeInstruction CopyLocalLoad(CodeInstruction source)
        {
            return new CodeInstruction(source.opcode, source.operand);
        }
    }
}
