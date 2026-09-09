using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ConflagrationPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
            "Conflagration";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition VectorDefinition =
            RuntimePatchDefinition.Transpile(
                "Conflagration vector play-state release",
                "org.magickacommunitypatch.conflagration-vector-state",
                FindVectorExecute,
                typeof(ConflagrationPlayStatePatch).GetMethod(
                    "SecondArgumentTranspiler"));

        internal static readonly RuntimePatchDefinition DirectionDefinition =
            RuntimePatchDefinition.Transpile(
                "Conflagration direction play-state release",
                "org.magickacommunitypatch.conflagration-direction-state",
                FindDirectionExecute,
                typeof(ConflagrationPlayStatePatch).GetMethod(
                    "ThirdArgumentTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerDefinition =
            RuntimePatchDefinition.Transpile(
                "Conflagration owner play-state release",
                "org.magickacommunitypatch.conflagration-owner-state",
                FindOwnerExecute,
                typeof(ConflagrationPlayStatePatch).GetMethod(
                    "SecondArgumentTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Conflagration current play state",
                "org.magickacommunitypatch.conflagration-current-state",
                FindUpdate,
                typeof(ConflagrationPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            Type conflagration;
            Type playState;
            Configure(targetAssembly, out conflagration, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                conflagration,
                "Execute",
                new Type[] { vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindDirectionExecute(Assembly targetAssembly)
        {
            Type conflagration;
            Type playState;
            Configure(targetAssembly, out conflagration, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                conflagration,
                "Execute",
                new Type[] { vector, vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            Type conflagration;
            Type playState;
            Configure(targetAssembly, out conflagration, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                conflagration,
                "Execute",
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type conflagration;
            Type ignoredPlayState;
            Configure(
                targetAssembly,
                out conflagration,
                out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                conflagration,
                "Update",
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type conflagration,
            out Type playState)
        {
            conflagration = targetAssembly.GetType(TypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = conflagration.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playState)
                throw new MissingFieldException(
                    conflagration.FullName,
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

        public static IEnumerable<CodeInstruction> SecondArgumentTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveAssignment(instructions, OpCodes.Ldarg_2);
        }

        public static IEnumerable<CodeInstruction> ThirdArgumentTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveAssignment(instructions, OpCodes.Ldarg_3);
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
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
            if (replacements != 4)
                throw new InvalidOperationException(
                    "Expected four Conflagration Update play-state reads, " +
                    "found " + replacements + ".");
            return result;
        }

        private static IEnumerable<CodeInstruction> RemoveAssignment(
            IEnumerable<CodeInstruction> instructions,
            OpCode argumentLoad)
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
                    result[index - 1].opcode != argumentLoad ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one Conflagration play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        private static void ConfigureFromLoadedAssembly()
        {
            Type conflagration = RuntimeMember.FindLoadedType(TypeName);
            Type ignoredConflagration;
            Type ignoredPlayState;
            Configure(
                conflagration.Assembly,
                out ignoredConflagration,
                out ignoredPlayState);
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
    }
}
