using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PolymorphPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Polymorph";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition OwnerDefinition =
            RuntimePatchDefinition.Transpile(
                "Polymorph owner play-state release",
                "org.magickacommunitypatch.polymorph-owner-state",
                FindOwnerExecute,
                typeof(PolymorphPlayStatePatch).GetMethod(
                    "SecondArgumentTranspiler"));

        internal static readonly RuntimePatchDefinition VectorDefinition =
            RuntimePatchDefinition.Transpile(
                "Polymorph vector play-state release",
                "org.magickacommunitypatch.polymorph-vector-state",
                FindVectorExecute,
                typeof(PolymorphPlayStatePatch).GetMethod(
                    "SecondArgumentTranspiler"));

        internal static readonly RuntimePatchDefinition TargetDefinition =
            RuntimePatchDefinition.Transpile(
                "Polymorph target play-state release",
                "org.magickacommunitypatch.polymorph-target-state",
                FindTargetExecute,
                typeof(PolymorphPlayStatePatch).GetMethod(
                    "ThirdArgumentTranspiler"));

        internal static readonly RuntimePatchDefinition RemoveDefinition =
            RuntimePatchDefinition.Transpile(
                "Polymorph current removal state",
                "org.magickacommunitypatch.polymorph-remove-state",
                FindOnRemove,
                typeof(PolymorphPlayStatePatch).GetMethod(
                    "RemoveTranspiler"));

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            Type polymorph;
            Type playState;
            Configure(targetAssembly, out polymorph, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                polymorph,
                "Execute",
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            Type polymorph;
            Type playState;
            Configure(targetAssembly, out polymorph, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                polymorph,
                "Execute",
                new Type[] { vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindTargetExecute(Assembly targetAssembly)
        {
            Type polymorph;
            Type playState;
            Configure(targetAssembly, out polymorph, out playState);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type entity = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Entity",
                true);
            return RequireMethod(
                polymorph,
                "Execute",
                new Type[] { owner, entity, playState },
                typeof(bool));
        }

        private static MethodInfo FindOnRemove(Assembly targetAssembly)
        {
            Type polymorph;
            Type ignoredPlayState;
            Configure(targetAssembly, out polymorph, out ignoredPlayState);
            return RequireMethod(
                polymorph,
                "OnRemove",
                Type.EmptyTypes,
                typeof(void));
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type polymorph,
            out Type playState)
        {
            polymorph = targetAssembly.GetType(TypeName, true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = polymorph.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playState)
                throw new MissingFieldException(
                    polymorph.FullName,
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

        public static IEnumerable<CodeInstruction> RemoveTranspiler(
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
            if (replacements != 2)
                throw new InvalidOperationException(
                    "Expected two Polymorph OnRemove play-state reads, found " +
                    replacements + ".");
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
                    "Expected one Polymorph play-state assignment, found " +
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
            Type polymorph = RuntimeMember.FindLoadedType(TypeName);
            Type ignoredPolymorph;
            Type ignoredPlayState;
            Configure(
                polymorph.Assembly,
                out ignoredPolymorph,
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
