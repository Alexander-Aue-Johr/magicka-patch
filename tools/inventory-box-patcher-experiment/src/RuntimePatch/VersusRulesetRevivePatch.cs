using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class VersusRulesetRevivePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "VersusRuleset missing revive avatar guard",
                "org.magickacommunitypatch.versus-ruleset-revive",
                VersusRulesetTargetMethods.FindRevivePlayerIn,
                typeof(VersusRulesetRevivePatch).GetMethod("Transpiler"));

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int initializeCall = FindAvatarInitializeCall(result);
            int cacheCall = FindLastAvatarCacheCall(result, initializeCall);
            int avatarStore = FindNextLocalStore(result, cacheCall, initializeCall);
            CodeInstruction avatarLoad = LoadForStore(result[avatarStore]);
            Label continueLabel = generator.DefineLabel();
            CodeInstruction continuation = result[avatarStore + 1];

            MoveEntryMetadata(continuation, avatarLoad);
            continuation.labels.Add(continueLabel);
            result.Insert(avatarStore + 1, avatarLoad);
            result.Insert(avatarStore + 2, new CodeInstruction(OpCodes.Brtrue, continueLabel));
            result.Insert(avatarStore + 3, new CodeInstruction(OpCodes.Ldc_I4_0));
            result.Insert(avatarStore + 4, new CodeInstruction(OpCodes.Ret));
            return result;
        }

        private static int FindAvatarInitializeCall(IList<CodeInstruction> instructions)
        {
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if (method != null &&
                    method.Name == "Initialize" &&
                    method.GetParameters().Length == 3 &&
                    method.GetParameters()[0].ParameterType.FullName ==
                        "Magicka.GameLogic.Entities.CharacterTemplate")
                    return index;
            }
            throw new InvalidOperationException(
                "VersusRuleset.RevivePlayer Avatar.Initialize call was not found.");
        }

        private static int FindLastAvatarCacheCall(
            IList<CodeInstruction> instructions,
            int beforeIndex)
        {
            int found = -1;
            for (int index = 0; index < beforeIndex; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if (method != null &&
                    method.Name == "GetFromCache" &&
                    method.DeclaringType.FullName == "Magicka.GameLogic.Entities.Avatar")
                    found = index;
            }
            if (found < 0)
                throw new InvalidOperationException(
                    "VersusRuleset.RevivePlayer Avatar.GetFromCache call was not found.");
            return found;
        }

        private static int FindNextLocalStore(
            IList<CodeInstruction> instructions,
            int afterIndex,
            int beforeIndex)
        {
            for (int index = afterIndex + 1; index < beforeIndex; index++)
            {
                OpCode opcode = instructions[index].opcode;
                if (opcode == OpCodes.Stloc || opcode == OpCodes.Stloc_S ||
                    opcode == OpCodes.Stloc_0 || opcode == OpCodes.Stloc_1 ||
                    opcode == OpCodes.Stloc_2 || opcode == OpCodes.Stloc_3)
                    return index;
            }
            throw new InvalidOperationException(
                "VersusRuleset.RevivePlayer avatar local store was not found.");
        }

        private static CodeInstruction LoadForStore(CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc)
                return new CodeInstruction(OpCodes.Ldloc, store.operand);
            if (store.opcode == OpCodes.Stloc_S)
                return new CodeInstruction(OpCodes.Ldloc_S, store.operand);
            if (store.opcode == OpCodes.Stloc_0)
                return new CodeInstruction(OpCodes.Ldloc_0);
            if (store.opcode == OpCodes.Stloc_1)
                return new CodeInstruction(OpCodes.Ldloc_1);
            if (store.opcode == OpCodes.Stloc_2)
                return new CodeInstruction(OpCodes.Ldloc_2);
            if (store.opcode == OpCodes.Stloc_3)
                return new CodeInstruction(OpCodes.Ldloc_3);
            throw new InvalidOperationException(
                "VersusRuleset.RevivePlayer avatar is no longer stored in a local.");
        }

        private static void MoveEntryMetadata(
            CodeInstruction source,
            CodeInstruction destination)
        {
            destination.labels.AddRange(source.labels);
            destination.blocks.AddRange(source.blocks);
            source.labels.Clear();
            source.blocks.Clear();
        }
    }
}
