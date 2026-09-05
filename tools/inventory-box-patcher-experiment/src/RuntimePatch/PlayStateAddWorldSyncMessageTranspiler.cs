using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.InventoryBoxRuntimePatch
{
    public static class PlayStateAddWorldSyncMessageTranspiler
    {
        internal static readonly string ExpectedCSharpDiff =
            CSharpPatchDiff.NormalizeTextBlock(
                @"
                 public void AddWorldSyncMessage(WorldSyncMessage iMessage)
                 {
                -    mWorldSyncMessageQueue.Enqueue(iMessage);
                +    if (iMessage.MessageType != WorldSyncMessage.WorldSyncMessageType.Message || iMessage.TriggerMessage.ActionType != TriggerActionType.SpawnNPC || NetworkEntityHandleGuard.IsUsableWorldSyncSpawnNpc(iMessage.TriggerMessage.Handle, this))
                +    {
                +        mWorldSyncMessageQueue.Enqueue(iMessage);
                +    }
                 }");

        internal static readonly RuntimePatchDefinition Definition =
            new RuntimePatchDefinition(
                "PlayState SpawnNPC WorldSync guard",
                "org.magickacommunitypatch.playstate-world-sync-guard-experiment",
                ExpectedCSharpDiff,
                PlayStateTargetMethods.FindAddWorldSyncMessage,
                typeof(PlayStateAddWorldSyncMessageTranspiler).GetMethod(
                    "Apply",
                    BindingFlags.Static | BindingFlags.Public));

        public static IEnumerable<CodeInstruction> Apply(
            IEnumerable<CodeInstruction> source,
            MethodBase original,
            ILGenerator generator)
        {
            List<CodeInstruction> instructions = new List<CodeInstruction>(source);
            List<CodeInstruction> originalInstructions = new List<CodeInstruction>(instructions);
            WorldSyncMembers members = WorldSyncMembers.Find(original);
            Label enqueueMessage = generator.DefineLabel();

            AssertOriginalEnqueue(instructions, members);
            instructions[0].labels.Add(enqueueMessage);
            instructions.InsertRange(0, CreateGuard(members, enqueueMessage));

            string csharpDiff = PlayStateWorldSyncCSharpContext.CreateDiff(
                originalInstructions,
                instructions,
                members);
            PatchObservation.Record(
                originalInstructions.Count,
                instructions.Count,
                instructions.GetRange(0, instructions.Count - originalInstructions.Count),
                csharpDiff);
            return instructions;
        }

        private static List<CodeInstruction> CreateGuard(
            WorldSyncMembers members,
            Label enqueueMessage)
        {
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarga_S, (byte)1),
                new CodeInstruction(OpCodes.Ldfld, members.MessageType),
                new CodeInstruction(OpCodes.Ldc_I4, members.MessageValue),
                new CodeInstruction(OpCodes.Bne_Un, enqueueMessage),
                new CodeInstruction(OpCodes.Ldarga_S, (byte)1),
                new CodeInstruction(OpCodes.Ldflda, members.TriggerMessage),
                new CodeInstruction(OpCodes.Ldfld, members.ActionType),
                new CodeInstruction(OpCodes.Ldc_I4, members.SpawnNpcValue),
                new CodeInstruction(OpCodes.Bne_Un, enqueueMessage),
                new CodeInstruction(OpCodes.Ldarga_S, (byte)1),
                new CodeInstruction(OpCodes.Ldflda, members.TriggerMessage),
                new CodeInstruction(OpCodes.Ldfld, members.Handle),
                new CodeInstruction(OpCodes.Conv_I4),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, members.IsUsableSpawnNpc),
                new CodeInstruction(OpCodes.Brtrue, enqueueMessage),
                new CodeInstruction(OpCodes.Ret)
            };
        }

        private static void AssertOriginalEnqueue(
            IList<CodeInstruction> instructions,
            WorldSyncMembers members)
        {
            bool matches = instructions.Count == 5 &&
                instructions[0].opcode == OpCodes.Ldarg_0 &&
                Object.Equals(instructions[1].operand, members.Queue) &&
                instructions[2].opcode == OpCodes.Ldarg_1 &&
                Object.Equals(instructions[3].operand, members.Enqueue) &&
                instructions[4].opcode == OpCodes.Ret;
            if (!matches)
                throw new InvalidOperationException(
                    "PlayState.AddWorldSyncMessage does not match the expected original method.");
        }
    }

    internal sealed class WorldSyncMembers
    {
        internal FieldInfo Queue { get; private set; }
        internal MethodInfo Enqueue { get; private set; }
        internal FieldInfo MessageType { get; private set; }
        internal FieldInfo TriggerMessage { get; private set; }
        internal FieldInfo ActionType { get; private set; }
        internal FieldInfo Handle { get; private set; }
        internal int MessageValue { get; private set; }
        internal int SpawnNpcValue { get; private set; }
        internal MethodInfo IsUsableSpawnNpc { get; private set; }

        private WorldSyncMembers()
        {
        }

        internal static WorldSyncMembers Find(MethodBase original)
        {
            Type playState = original.DeclaringType;
            Type message = original.GetParameters()[0].ParameterType;
            FieldInfo triggerMessage = RequireField(message, "TriggerMessage");
            Type trigger = triggerMessage.FieldType;
            FieldInfo queue = RequireField(playState, "mWorldSyncMessageQueue");

            return new WorldSyncMembers
            {
                Queue = queue,
                Enqueue = queue.FieldType.GetMethod("Enqueue"),
                MessageType = RequireField(message, "MessageType"),
                TriggerMessage = triggerMessage,
                ActionType = RequireField(trigger, "ActionType"),
                Handle = RequireField(trigger, "Handle"),
                MessageValue = EnumValue(RequireField(message, "MessageType").FieldType, "Message"),
                SpawnNpcValue = EnumValue(RequireField(trigger, "ActionType").FieldType, "SpawnNPC"),
                IsUsableSpawnNpc = typeof(Magicka.CommunityPatch.NetworkEntityHandleGuard).GetMethod(
                    "IsUsableWorldSyncSpawnNpc",
                    BindingFlags.Static | BindingFlags.NonPublic)
            };
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static int EnumValue(Type enumType, string name)
        {
            return Convert.ToInt32(Enum.Parse(enumType, name));
        }
    }

    internal static class PlayStateTargetMethods
    {
        internal static MethodInfo FindAddWorldSyncMessage(Assembly targetAssembly)
        {
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo[] matches = Array.FindAll(
                playState.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "AddWorldSyncMessage" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.FullName == "Magicka.Network.WorldSyncMessage");
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one PlayState.AddWorldSyncMessage method, found " + matches.Length + ".");
            return matches[0];
        }
    }

    internal static class PlayStateWorldSyncCSharpContext
    {
        internal static string CreateDiff(
            IList<CodeInstruction> original,
            IList<CodeInstruction> patched,
            WorldSyncMembers members)
        {
            AssertEnqueueCall(original, 0, members);
            AssertEnqueueCall(patched, patched.Count - original.Count, members);
            string before =
                "public void AddWorldSyncMessage(WorldSyncMessage iMessage)\n" +
                "{\n" +
                "    mWorldSyncMessageQueue.Enqueue(iMessage);\n" +
                "}";
            string after =
                "public void AddWorldSyncMessage(WorldSyncMessage iMessage)\n" +
                "{\n" +
                "    if (iMessage.MessageType != WorldSyncMessage.WorldSyncMessageType.Message || iMessage.TriggerMessage.ActionType != TriggerActionType.SpawnNPC || NetworkEntityHandleGuard.IsUsableWorldSyncSpawnNpc(iMessage.TriggerMessage.Handle, this))\n" +
                "    {\n" +
                "        mWorldSyncMessageQueue.Enqueue(iMessage);\n" +
                "    }\n" +
                "}";
            return LineDiff.Create(before, after);
        }

        private static void AssertEnqueueCall(
            IList<CodeInstruction> instructions,
            int start,
            WorldSyncMembers members)
        {
            bool matches = start >= 0 &&
                start + 5 == instructions.Count &&
                instructions[start].opcode == OpCodes.Ldarg_0 &&
                Object.Equals(instructions[start + 1].operand, members.Queue) &&
                instructions[start + 2].opcode == OpCodes.Ldarg_1 &&
                Object.Equals(instructions[start + 3].operand, members.Enqueue) &&
                instructions[start + 4].opcode == OpCodes.Ret;
            if (!matches)
                throw new InvalidOperationException(
                    "The WorldSync enqueue context cannot be represented as expected C#.");
        }
    }
}
