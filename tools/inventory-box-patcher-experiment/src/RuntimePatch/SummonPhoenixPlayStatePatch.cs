using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class SummonPhoenixPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonPhoenix";

        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonPhoenix vector play-state lifetime",
                "org.magickacommunitypatch.summon-phoenix-vector-play-state",
                FindVectorExecute,
                typeof(SummonPhoenixPlayStatePatch).GetMethod(
                    "VectorExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonPhoenix owner play-state lifetime",
                "org.magickacommunitypatch.summon-phoenix-owner-play-state",
                FindOwnerExecute,
                typeof(SummonPhoenixPlayStatePatch).GetMethod(
                    "OwnerExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonPhoenix current play-state update",
                "org.magickacommunitypatch.summon-phoenix-current-play-state",
                FindUpdate,
                typeof(SummonPhoenixPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            Type ability;
            Type playState;
            Configure(targetAssembly, out ability, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                ability,
                "Execute",
                new Type[] { vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            Type ability;
            Type playState;
            Configure(targetAssembly, out ability, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                ability,
                "Execute",
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type ability;
            Type playState;
            Configure(targetAssembly, out ability, out playState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                ability,
                "Update",
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type ability,
            out Type playState)
        {
            ability = targetAssembly.GetType(TypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = ability.GetField(
                "sPlayState",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playState)
                throw new MissingFieldException(ability.FullName, "sPlayState");

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
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static IEnumerable<CodeInstruction> VectorExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return TranspileExecute(instructions, "vector");
        }

        public static IEnumerable<CodeInstruction> OwnerExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return TranspileExecute(instructions, "owner");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int reads = ReplaceReads(result);
            if (reads != 7)
                throw new InvalidOperationException(
                    "Expected seven SummonPhoenix update play-state reads, " +
                    "found " + reads + ".");
            return result;
        }

        private static IEnumerable<CodeInstruction> TranspileExecute(
            IEnumerable<CodeInstruction> instructions,
            string overload)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stsfld ||
                    !Object.Equals(result[index].operand, legacyPlayStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_2)
                    throw new InvalidOperationException(
                        "SummonPhoenix " + overload +
                        " play-state store source changed.");
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            int reads = ReplaceReads(result);
            if (stores != 1 || reads != 3)
                throw new InvalidOperationException(
                    "Expected one store and three reads in SummonPhoenix " +
                    overload + " Execute, found " + stores + " and " +
                    reads + ".");
            return result;
        }

        private static int ReplaceReads(List<CodeInstruction> instructions)
        {
            int reads = 0;
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].opcode != OpCodes.Ldsfld ||
                    !Object.Equals(
                        instructions[index].operand,
                        legacyPlayStateField))
                    continue;
                instructions[index].opcode = OpCodes.Call;
                instructions[index].operand = recentPlayStateGetter;
                reads++;
            }
            return reads;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }
}
