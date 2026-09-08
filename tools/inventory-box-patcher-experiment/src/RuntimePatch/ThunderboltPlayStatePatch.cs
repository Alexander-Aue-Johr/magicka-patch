using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ThunderboltPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderbolt";

        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderbolt vector play-state release",
                "org.magickacommunitypatch.thunderbolt-vector-play-state",
                FindVectorExecute,
                typeof(ThunderboltPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderbolt owner play-state release",
                "org.magickacommunitypatch.thunderbolt-owner-play-state",
                FindOwnerExecute,
                typeof(ThunderboltPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition CastDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderbolt current play-state cast",
                "org.magickacommunitypatch.thunderbolt-current-play-state",
                FindPrivateExecute,
                typeof(ThunderboltPlayStatePatch).GetMethod(
                    "CastTranspiler"));

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            Type thunderbolt;
            Type playState;
            Configure(targetAssembly, out thunderbolt, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                thunderbolt,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            Type thunderbolt;
            Type playState;
            Configure(targetAssembly, out thunderbolt, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                thunderbolt,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindPrivateExecute(Assembly targetAssembly)
        {
            Type thunderbolt;
            Type playState;
            Configure(targetAssembly, out thunderbolt, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                thunderbolt,
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector, vector, owner },
                typeof(bool));
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type thunderbolt,
            out Type playState)
        {
            thunderbolt = targetAssembly.GetType(TypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = thunderbolt.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playState)
                throw new MissingFieldException(
                    thunderbolt.FullName,
                    "mPlayState");

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
            BindingFlags flags,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                flags,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            for (int index = 2; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, legacyPlayStateField))
                    continue;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2)
                    throw new InvalidOperationException(
                        "Thunderbolt play-state store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            if (stores != 1)
                throw new InvalidOperationException(
                    "Expected one Thunderbolt play-state store, found " +
                    stores + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> CastTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int reads = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, legacyPlayStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "Thunderbolt play-state read source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                reads++;
            }
            if (reads != 11)
                throw new InvalidOperationException(
                    "Expected eleven Thunderbolt play-state reads, found " +
                    reads + ".");
            return result;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }
}
