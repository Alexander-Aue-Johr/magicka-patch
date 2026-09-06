using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class UndeadSummonNetworkPatch
    {
        private const string UndeadTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonUndead";

        private static FieldInfo bool2Field;
        private static FieldInfo colorField;
        private static FieldInfo point2Field;
        private static FieldInfo vectorXField;
        private static MethodInfo summonedSingle;
        private static MethodInfo summonedWithFlag;
        private static readonly float NegativeZero = BitConverter.ToSingle(
            new byte[] { 0, 0, 0, 128 },
            0);

        internal static readonly RuntimePatchDefinition HostDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonUndead network state marker",
                "org.magickacommunitypatch.summon-undead-network-marker",
                FindHostTarget,
                typeof(UndeadSummonNetworkPatch).GetMethod("HostTranspiler"));

        internal static readonly RuntimePatchDefinition ClientDefinition =
            RuntimePatchDefinition.Transpile(
                "SpawnNPC undead state application",
                "org.magickacommunitypatch.spawn-npc-undead-state",
                FindClientTarget,
                typeof(UndeadSummonNetworkPatch).GetMethod("ClientTranspiler"));

        private static MethodInfo FindHostTarget(Assembly targetAssembly)
        {
            ConfigureMessageFields(targetAssembly);
            Type vectorType = FindLoadedType("Microsoft.Xna.Framework.Vector3");
            Type undeadType = targetAssembly.GetType(UndeadTypeName, true);
            MethodInfo method = undeadType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { vectorType, vectorType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(undeadType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindClientTarget(Assembly targetAssembly)
        {
            Type messageType = ConfigureMessageFields(targetAssembly);
            Type npcType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.NonPlayerCharacter",
                true);
            Type characterType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            summonedSingle = RequireMethod(
                npcType,
                "Summoned",
                new Type[] { characterType });
            summonedWithFlag = RequireMethod(
                npcType,
                "Summoned",
                new Type[] { characterType, typeof(bool) });

            Type triggerType = targetAssembly.GetType(
                "Magicka.Levels.Triggers.Trigger",
                true);
            MethodInfo method = triggerType.GetMethod(
                "SpawnNPC",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { messageType.MakeByRefType() },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(triggerType.FullName, "SpawnNPC");
            return method;
        }

        private static Type ConfigureMessageFields(Assembly targetAssembly)
        {
            Type messageType = targetAssembly.GetType(
                "Magicka.Network.TriggerActionMessage",
                true);
            bool2Field = RequireField(messageType, "Bool2", typeof(bool));
            point2Field = RequireField(messageType, "Point2", typeof(int));
            Type vectorType = FindLoadedType("Microsoft.Xna.Framework.Vector3");
            colorField = RequireField(messageType, "Color", vectorType);
            vectorXField = RequireField(vectorType, "X", typeof(float));
            return messageType;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                parameterTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            throw new TypeLoadException(fullName);
        }

        public static IEnumerable<CodeInstruction> HostTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int point2Write = FindPoint2Write(result);
            if (HasExactHostMarker(result, point2Write))
                return result;

            int bool2Writes = CountFieldWrites(result, bool2Field);
            int colorMarkers = CountColorMarkers(result);
            if (bool2Writes != 0 || colorMarkers != 0)
            {
                throw new InvalidOperationException(
                    "SummonUndead marker fields changed shape.");
            }

            CodeInstruction messageAddress = result[point2Write - 2];
            int insertAt = point2Write + 1;
            result.Insert(
                insertAt++,
                new CodeInstruction(messageAddress.opcode, messageAddress.operand));
            result.Insert(insertAt++, new CodeInstruction(OpCodes.Ldc_I4_1));
            result.Insert(insertAt++, new CodeInstruction(OpCodes.Stfld, bool2Field));
            result.Insert(
                insertAt++,
                new CodeInstruction(messageAddress.opcode, messageAddress.operand));
            result.Insert(insertAt++, new CodeInstruction(OpCodes.Ldflda, colorField));
            result.Insert(insertAt++, new CodeInstruction(OpCodes.Ldc_R4, NegativeZero));
            result.Insert(insertAt, new CodeInstruction(OpCodes.Stfld, vectorXField));
            return result;
        }

        public static IEnumerable<CodeInstruction> ClientTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            MethodInfo marker = typeof(UndeadSummonNetworkPatch).GetMethod(
                "IsUndeadMarker");
            int legacyCall = -1;
            int flaggedCalls = 0;
            int markerCalls = 0;
            int flaggedCall = -1;
            int markerCall = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (Calls(result[index], summonedSingle))
                {
                    if (legacyCall >= 0)
                        throw new InvalidOperationException(
                            "Multiple SpawnNPC summon calls matched.");
                    legacyCall = index;
                }
                if (Calls(result[index], summonedWithFlag))
                {
                    flaggedCalls++;
                    flaggedCall = index;
                }
                if (Calls(result[index], marker))
                {
                    markerCalls++;
                    markerCall = index;
                }
            }
            if (legacyCall < 0 && flaggedCalls == 1 && markerCalls == 1 &&
                HasExactClientMarker(result, flaggedCall, markerCall))
                return result;
            if (legacyCall < 0 || flaggedCalls != 0 || markerCalls != 0)
            {
                throw new InvalidOperationException(
                    "Expected one legacy SpawnNPC summon call.");
            }

            result.Insert(legacyCall++, new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(legacyCall++, new CodeInstruction(OpCodes.Ldfld, bool2Field));
            result.Insert(legacyCall++, new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(legacyCall++, new CodeInstruction(OpCodes.Ldflda, colorField));
            result.Insert(legacyCall++, new CodeInstruction(OpCodes.Ldfld, vectorXField));
            result.Insert(legacyCall++, new CodeInstruction(OpCodes.Call, marker));
            result[legacyCall].operand = summonedWithFlag;
            return result;
        }

        private static bool HasExactClientMarker(
            List<CodeInstruction> instructions,
            int flaggedCall,
            int markerCall)
        {
            return markerCall >= 5 && flaggedCall == markerCall + 1 &&
                instructions[markerCall - 5].opcode == OpCodes.Ldarg_0 &&
                instructions[markerCall - 4].opcode == OpCodes.Ldfld &&
                Object.Equals(instructions[markerCall - 4].operand, bool2Field) &&
                instructions[markerCall - 3].opcode == OpCodes.Ldarg_0 &&
                instructions[markerCall - 2].opcode == OpCodes.Ldflda &&
                Object.Equals(instructions[markerCall - 2].operand, colorField) &&
                instructions[markerCall - 1].opcode == OpCodes.Ldfld &&
                Object.Equals(instructions[markerCall - 1].operand, vectorXField);
        }

        public static bool IsUndeadMarker(bool inMemoryFlag, float wireMarker)
        {
            return inMemoryFlag ||
                (wireMarker == 0f && Single.IsNegativeInfinity(1f / wireMarker));
        }

        private static int FindPoint2Write(List<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 2; index < instructions.Count; index++)
            {
                if (instructions[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(instructions[index].operand, point2Field) ||
                    !LoadsInteger(instructions[index - 1], 170) ||
                    !LoadsLocalAddress(instructions[index - 2].opcode))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple SummonUndead Point2 writes matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "SummonUndead Point2 packet anchor was not found.");
            return match;
        }

        private static bool HasExactHostMarker(
            List<CodeInstruction> instructions,
            int point2Write)
        {
            if (point2Write + 7 >= instructions.Count)
                return false;
            CodeInstruction address = instructions[point2Write - 2];
            return SameLocalAddress(address, instructions[point2Write + 1]) &&
                LoadsInteger(instructions[point2Write + 2], 1) &&
                instructions[point2Write + 3].opcode == OpCodes.Stfld &&
                Object.Equals(instructions[point2Write + 3].operand, bool2Field) &&
                SameLocalAddress(address, instructions[point2Write + 4]) &&
                instructions[point2Write + 5].opcode == OpCodes.Ldflda &&
                Object.Equals(instructions[point2Write + 5].operand, colorField) &&
                instructions[point2Write + 6].opcode == OpCodes.Ldc_R4 &&
                IsNegativeZero(Convert.ToSingle(instructions[point2Write + 6].operand)) &&
                instructions[point2Write + 7].opcode == OpCodes.Stfld &&
                Object.Equals(instructions[point2Write + 7].operand, vectorXField);
        }

        private static int CountFieldWrites(
            List<CodeInstruction> instructions,
            FieldInfo field)
        {
            int count = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].opcode == OpCodes.Stfld &&
                    Object.Equals(instructions[index].operand, field))
                    count++;
            }
            return count;
        }

        private static int CountColorMarkers(List<CodeInstruction> instructions)
        {
            int count = 0;
            for (int index = 3; index < instructions.Count; index++)
            {
                if (instructions[index].opcode == OpCodes.Stfld &&
                    Object.Equals(instructions[index].operand, vectorXField) &&
                    instructions[index - 1].opcode == OpCodes.Ldc_R4 &&
                    IsNegativeZero(Convert.ToSingle(instructions[index - 1].operand)) &&
                    instructions[index - 2].opcode == OpCodes.Ldflda &&
                    Object.Equals(instructions[index - 2].operand, colorField))
                    count++;
            }
            return count;
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            return method != null &&
                (instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }

        private static bool LoadsInteger(CodeInstruction instruction, int value)
        {
            if (value == 1 && instruction.opcode == OpCodes.Ldc_I4_1)
                return true;
            if (instruction.opcode == OpCodes.Ldc_I4)
                return Convert.ToInt32(instruction.operand) == value;
            if (instruction.opcode == OpCodes.Ldc_I4_S)
                return Convert.ToInt32(instruction.operand) == value;
            return false;
        }

        private static bool LoadsLocalAddress(OpCode opcode)
        {
            return opcode == OpCodes.Ldloca || opcode == OpCodes.Ldloca_S;
        }

        private static bool SameLocalAddress(
            CodeInstruction left,
            CodeInstruction right)
        {
            return left.opcode == right.opcode &&
                Object.Equals(left.operand, right.operand);
        }

        private static bool IsNegativeZero(float value)
        {
            return value == 0f && Single.IsNegativeInfinity(1f / value);
        }
    }
}
