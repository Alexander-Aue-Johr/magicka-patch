using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EntityUpdateMessageReadPatch
    {
        private static FieldInfo readerField;
        private static MethodInfo readMessageMethod;
        private static byte entityUpdatePacketType;

        internal static readonly RuntimePatchDefinition ServerDefinition =
            RuntimePatchDefinition.Transpile(
                "NetworkServer EntityUpdate Character marker decode",
                "org.magickacommunitypatch.server-entity-update-character-read",
                assembly => FindTarget(assembly, "NetworkServer"),
                typeof(EntityUpdateMessageReadPatch).GetMethod("Transpiler"));

        internal static readonly RuntimePatchDefinition ClientDefinition =
            RuntimePatchDefinition.Transpile(
                "NetworkClient EntityUpdate Character marker decode",
                "org.magickacommunitypatch.client-entity-update-character-read",
                assembly => FindTarget(assembly, "NetworkClient"),
                typeof(EntityUpdateMessageReadPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly, string typeName)
        {
            Type type = targetAssembly.GetType("Magicka.Network." + typeName, true);
            readerField = type.GetField(
                "mReader",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (readerField == null || readerField.FieldType != typeof(BinaryReader))
                throw new MissingFieldException(type.FullName, "mReader");

            readMessageMethod = FindReadMessage(type);
            Type packetType = targetAssembly.GetType("Magicka.Network.PacketType", true);
            entityUpdatePacketType = Convert.ToByte(Enum.Parse(packetType, "EntityUpdate"));

            MethodInfo update = type.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, "Update");
            return update;
        }

        private static MethodInfo FindReadMessage(Type type)
        {
            MethodInfo match = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "ReadMessage" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType != typeof(BinaryReader) ||
                    parameters[1].ParameterType.FullName != "SteamWrapper.SteamID")
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple " + type.Name + ".ReadMessage methods matched.");
                match = methods[index];
            }
            if (match == null)
                throw new MissingMethodException(type.FullName, "ReadMessage");
            return match;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int match = -1;
            for (int index = 4; index < result.Count; index++)
            {
                if (!Calls(result[index], readMessageMethod))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple network receive-loop calls matched.");
                match = index;
            }
            if (match < 0 ||
                result[match - 4].opcode != OpCodes.Ldarg_0 ||
                result[match - 3].opcode != OpCodes.Ldarg_0 ||
                result[match - 2].opcode != OpCodes.Ldfld ||
                !Object.Equals(result[match - 2].operand, readerField) ||
                !LoadsLocal(result[match - 1].opcode))
            {
                throw new InvalidOperationException(
                    "Network receive-loop call changed shape.");
            }

            int insertAt = match - 4;
            CodeInstruction loadOwner = new CodeInstruction(OpCodes.Ldarg_0);
            loadOwner.labels.AddRange(result[insertAt].labels);
            loadOwner.blocks.AddRange(result[insertAt].blocks);
            result[insertAt].labels.Clear();
            result[insertAt].blocks.Clear();
            result.Insert(insertAt, loadOwner);
            result.Insert(insertAt + 1, new CodeInstruction(OpCodes.Ldfld, readerField));
            result.Insert(
                insertAt + 2,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(EntityUpdateMessageReadPatch).GetMethod("PrepareReader")));
            return result;
        }

        public static void PrepareReader(BinaryReader reader)
        {
            if (reader == null)
                return;
            Stream stream = reader.BaseStream;
            if (stream == null || !stream.CanSeek || !stream.CanWrite)
                return;

            long start;
            try
            {
                start = stream.Position;
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                if (start < 0 || start + 7 > stream.Length)
                    return;
                stream.Position = start;
                if (stream.ReadByte() != entityUpdatePacketType)
                    return;
                stream.Position = start + 5;
                int low = stream.ReadByte();
                int high = stream.ReadByte();
                if (low < 0 || high < 0 || (low & 0x10) == 0)
                    return;
                stream.Position = start + 5;
                stream.WriteByte((byte)(low & 0xef));
            }
            catch (Exception)
            {
            }
            finally
            {
                try
                {
                    stream.Position = start;
                }
                catch (Exception)
                {
                }
            }
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }

        private static bool LoadsLocal(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }
    }
}
