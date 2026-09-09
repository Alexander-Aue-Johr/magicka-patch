using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class UnderGroundAttackPlayStatePatch
    {
        private const string AttackTypeName =
            "Magicka.GameLogic.Spells.UnderGroundAttack";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "UnderGroundAttack play-state release",
                "org.magickacommunitypatch.underground-attack-state-release",
                FindConstructor,
                typeof(UnderGroundAttackPlayStatePatch).GetMethod(
                    "ConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "UnderGroundAttack current initialize play state",
                "org.magickacommunitypatch.underground-attack-initialize-state",
                FindInitialize,
                typeof(UnderGroundAttackPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "UnderGroundAttack current update play state",
                "org.magickacommunitypatch.underground-attack-update-state",
                FindUpdate,
                typeof(UnderGroundAttackPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static ConstructorInfo FindConstructor(Assembly targetAssembly)
        {
            Type attack;
            Type playState;
            Configure(targetAssembly, out attack, out playState);
            ConstructorInfo constructor = attack.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(attack.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type attack;
            Type ignoredPlayState;
            Configure(targetAssembly, out attack, out ignoredPlayState);
            Type vector3 = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            Type vector2 = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector2");
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo found = null;
            MethodInfo[] methods = attack.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo candidate = methods[index];
                ParameterInfo[] parameters = candidate.GetParameters();
                if (candidate.Name != "Initialize" ||
                    candidate.ReturnType != typeof(void) ||
                    parameters.Length != 7 ||
                    parameters[0].ParameterType != vector3.MakeByRefType() ||
                    parameters[1].ParameterType != vector2.MakeByRefType() ||
                    parameters[2].ParameterType != owner ||
                    parameters[3].ParameterType != typeof(double) ||
                    parameters[4].ParameterType != typeof(float) ||
                    parameters[6].ParameterType != typeof(bool))
                    continue;
                if (found != null)
                    throw new AmbiguousMatchException(
                        attack.FullName + ".Initialize");
                found = candidate;
            }
            if (found == null)
                throw new MissingMethodException(attack.FullName, "Initialize");
            return found;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type attack;
            Type ignoredPlayState;
            Configure(targetAssembly, out attack, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = attack.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(attack.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type attack,
            out Type playState)
        {
            attack = targetAssembly.GetType(AttackTypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = attack.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playState)
                throw new MissingFieldException(attack.FullName, "mPlayState");

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
            ConfigureFromLoadedAssembly();
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
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_1 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one UnderGroundAttack play-state assignment, " +
                    "found " + writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 2, "Initialize");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 5, "Update");
        }

        private static IEnumerable<CodeInstruction> ReplaceReads(
            IEnumerable<CodeInstruction> instructions,
            int expected,
            string methodName)
        {
            ConfigureFromLoadedAssembly();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " UnderGroundAttack " +
                    methodName + " play-state reads, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type attack = RuntimeMember.FindLoadedType(AttackTypeName);
            Type ignoredAttack;
            Type ignoredPlayState;
            Configure(
                attack.Assembly,
                out ignoredAttack,
                out ignoredPlayState);
        }
    }
}
