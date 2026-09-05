using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;

namespace Magicka.InventoryBoxRuntimePatch
{
    internal static class PatchObservation
    {
        internal static int TranspilerCalls { get; private set; }
        internal static int InstructionCountBefore { get; private set; }
        internal static int InstructionCountAfter { get; private set; }
        internal static string InsertedOpcodes { get; private set; }
        internal static string CSharpDiff { get; private set; }

        internal static void Reset()
        {
            TranspilerCalls = 0;
            InstructionCountBefore = 0;
            InstructionCountAfter = 0;
            InsertedOpcodes = String.Empty;
            CSharpDiff = String.Empty;
        }

        internal static void Record(
            int instructionCountBefore,
            int instructionCountAfter,
            IEnumerable<CodeInstruction> inserted,
            string csharpDiff)
        {
            TranspilerCalls++;
            InstructionCountBefore = instructionCountBefore;
            InstructionCountAfter = instructionCountAfter;
            InsertedOpcodes = String.Join(",", inserted.Select(instruction => instruction.opcode.Name).ToArray());
            CSharpDiff = csharpDiff;
        }
    }
}
