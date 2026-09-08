using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class VladPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Bosses.Vlad";

        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "Vlad play-state reference release",
                "org.magickacommunitypatch.vlad-play-state-release",
                FindConstructor,
                typeof(VladPlayStatePatch).GetMethod(
                    "ConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "Vlad current play-state initialization",
                "org.magickacommunitypatch.vlad-current-play-state",
                FindInitialize,
                typeof(VladPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        private static ConstructorInfo FindConstructor(Assembly targetAssembly)
        {
            Type vlad;
            Type playState;
            Configure(targetAssembly, out vlad, out playState);
            ConstructorInfo constructor = vlad.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                new Type[] { playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(vlad.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type vlad;
            Type playState;
            Configure(targetAssembly, out vlad, out playState);
            Type matrix = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Matrix").MakeByRefType();
            MethodInfo method = vlad.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { matrix },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(vlad.FullName, "Initialize");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type vlad,
            out Type playState)
        {
            vlad = targetAssembly.GetType(TypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = vlad.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playState)
                throw new MissingFieldException(vlad.FullName, "mPlayState");

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

        public static IEnumerable<CodeInstruction> ConstructorTranspiler(
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
                    result[index - 1].opcode != OpCodes.Ldarg_1)
                    throw new InvalidOperationException(
                        "Vlad constructor play-state store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            if (stores != 1)
                throw new InvalidOperationException(
                    "Expected one Vlad constructor play-state store, found " +
                    stores + ".");
            return result;
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
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
                        "Vlad initialize play-state read source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                reads++;
            }
            if (reads != 2)
                throw new InvalidOperationException(
                    "Expected two Vlad initialize play-state reads, found " +
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
