using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EarthQuakePlayStatePatch
    {
        private const string EarthQuakeTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.EarthQuake";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "EarthQuake play-state release",
                "org.magickacommunitypatch.earthquake-play-state-release",
                FindExecute,
                typeof(EarthQuakePlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition NewQuakeDefinition =
            RuntimePatchDefinition.Transpile(
                "EarthQuake current camera",
                "org.magickacommunitypatch.earthquake-current-camera",
                FindNewQuake,
                typeof(EarthQuakePlayStatePatch).GetMethod(
                    "NewQuakeTranspiler"));

        internal static readonly RuntimePatchDefinition QuakeDefinition =
            RuntimePatchDefinition.Transpile(
                "EarthQuake current entity manager",
                "org.magickacommunitypatch.earthquake-current-entity-manager",
                FindQuake,
                typeof(EarthQuakePlayStatePatch).GetMethod(
                    "QuakeTranspiler"));

        private static MethodInfo FindExecute(Assembly assembly)
        {
            Type earthQuake;
            Type playState;
            Configure(assembly, out earthQuake, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                earthQuake,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindNewQuake(Assembly assembly)
        {
            Type earthQuake;
            Type ignoredPlayState;
            Configure(assembly, out earthQuake, out ignoredPlayState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                earthQuake,
                "NewQuake",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector.MakeByRefType(), typeof(float) },
                typeof(void));
        }

        private static MethodInfo FindQuake(Assembly assembly)
        {
            Type earthQuake;
            Type ignoredPlayState;
            Configure(assembly, out earthQuake, out ignoredPlayState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                earthQuake,
                "Quake",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector.MakeByRefType(), typeof(float) },
                typeof(void));
        }

        private static void Configure(
            Assembly assembly,
            out Type earthQuake,
            out Type playState)
        {
            earthQuake = assembly.GetType(EarthQuakeTypeName, true);
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = earthQuake.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null || playStateField.FieldType != playState)
                throw new MissingFieldException(earthQuake.FullName, "mPlayState");
            PropertyInfo recentPlayState = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
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
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    writes++;
                if (result[index - 2].opcode == OpCodes.Ldarg_0 &&
                    result[index - 1].opcode == OpCodes.Ldarg_2 &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one EarthQuake play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            ReplaceReads(result, 1, "execute");
            return result;
        }

        public static IEnumerable<CodeInstruction> NewQuakeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            ReplaceReads(result, 1, "camera");
            return result;
        }

        public static IEnumerable<CodeInstruction> QuakeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            ReplaceReads(result, 2, "entity-manager");
            return result;
        }

        private static void ReplaceReads(
            IList<CodeInstruction> instructions,
            int expected,
            string operation)
        {
            int replacements = 0;
            for (int index = 1; index < instructions.Count; index++)
            {
                FieldInfo field = instructions[index].operand as FieldInfo;
                if (instructions[index - 1].opcode != OpCodes.Ldarg_0 ||
                    instructions[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                instructions[index - 1].opcode = OpCodes.Call;
                instructions[index - 1].operand = recentPlayStateGetter;
                instructions[index].opcode = OpCodes.Nop;
                instructions[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " EarthQuake " + operation +
                    " play-state reads, found " + replacements + ".");
        }
    }
}
