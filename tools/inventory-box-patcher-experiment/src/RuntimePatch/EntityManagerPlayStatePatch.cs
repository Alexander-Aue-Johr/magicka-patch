using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class EntityManagerPlayStatePatch
    {
        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.ConstructorTranspile(
                "EntityManager play-state release",
                "org.magickacommunitypatch.entity-manager-current-state",
                FindConstructor,
                typeof(EntityManagerPlayStatePatch).GetMethod("Transpiler"));

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Type manager = assembly.GetType(
                "Magicka.GameLogic.Entities.EntityManager",
                true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = manager.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null || playStateField.FieldType != playState)
                throw new MissingFieldException(manager.FullName, "mPlayState");
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
            ConstructorInfo constructor = manager.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(manager.FullName, ".ctor");
            return constructor;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            int reads = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(result[index].operand, playStateField))
                {
                    if (index < 2 ||
                        result[index - 2].opcode != OpCodes.Ldarg_0 ||
                        result[index - 1].opcode != OpCodes.Ldarg_1)
                        throw new InvalidOperationException(
                            "EntityManager play-state store source changed.");
                    MakeNop(result[index - 2]);
                    MakeNop(result[index - 1]);
                    MakeNop(result[index]);
                    stores++;
                }
                else if (result[index].opcode == OpCodes.Ldfld &&
                    Object.Equals(result[index].operand, playStateField))
                {
                    if (index == 0 ||
                        result[index - 1].opcode != OpCodes.Ldarg_0)
                        throw new InvalidOperationException(
                            "EntityManager play-state read source changed.");
                    MakeNop(result[index - 1]);
                    result[index].opcode = OpCodes.Call;
                    result[index].operand = recentPlayStateGetter;
                    reads++;
                }
            }
            if (stores != 1 || reads != 3)
                throw new InvalidOperationException(
                    "Expected one EntityManager play-state store and three " +
                    "reads, found " + stores + " and " + reads + ".");
            return result;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }
}
