using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class SummonDeathPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonDeath";

        private static FieldInfo outerPlayStateField;
        private static FieldInfo entityPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath owner play-state release",
                "org.magickacommunitypatch.summon-death-owner-state",
                FindOwnerExecute,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "OwnerExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath vector play-state release",
                "org.magickacommunitypatch.summon-death-vector-state",
                FindVectorExecute,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "VectorExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition SpawnDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath current play-state spawn",
                "org.magickacommunitypatch.summon-death-current-spawn",
                FindSpawn,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "SpawnTranspiler"));

        internal static readonly RuntimePatchDefinition DeathConstructorDefinition =
            RuntimePatchDefinition.ConstructorTranspile(
                "SummonDeath entity constructor play-state release",
                "org.magickacommunitypatch.summon-death-entity-constructor",
                FindDeathConstructor,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "DeathConstructorTranspiler"));

        internal static readonly RuntimePatchDefinition DeathInitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath entity current play-state initialization",
                "org.magickacommunitypatch.summon-death-entity-initialize",
                FindDeathInitialize,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "DeathInitializeTranspiler"));

        internal static readonly RuntimePatchDefinition DeathUpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath entity current play-state update",
                "org.magickacommunitypatch.summon-death-entity-update",
                FindDeathUpdate,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "DeathUpdateTranspiler"));

        internal static readonly RuntimePatchDefinition DeathDeinitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonDeath entity current play-state removal",
                "org.magickacommunitypatch.summon-death-entity-deinitialize",
                FindDeathDeinitialize,
                typeof(SummonDeathPlayStatePatch).GetMethod(
                    "DeathDeinitializeTranspiler"));

        private static MethodInfo FindOwnerExecute(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            Type owner = assembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            return RequireMethod(
                outer,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { owner, playState },
                typeof(bool));
        }

        private static MethodInfo FindVectorExecute(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                outer,
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector, playState },
                typeof(bool));
        }

        private static MethodInfo FindSpawn(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            return RequireMethod(
                outer,
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                new Type[] { vector },
                typeof(bool));
        }

        private static ConstructorInfo FindDeathConstructor(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            ConstructorInfo constructor = death.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { playState },
                null);
            if (constructor == null)
                throw new MissingMethodException(death.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindDeathInitialize(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            Type matrix = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Matrix").MakeByRefType();
            Type character = assembly.GetType(
                "Magicka.GameLogic.Entities.Character",
                true);
            return RequireMethod(
                death,
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { matrix, playState, character },
                typeof(void));
        }

        private static MethodInfo FindDeathUpdate(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                death,
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static MethodInfo FindDeathDeinitialize(Assembly assembly)
        {
            Type outer;
            Type death;
            Type playState;
            Configure(assembly, out outer, out death, out playState);
            return RequireMethod(
                death,
                "Deinitialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                typeof(void));
        }

        private static void Configure(
            Assembly assembly,
            out Type outer,
            out Type death,
            out Type playState)
        {
            outer = assembly.GetType(TypeName, true);
            death = outer.GetNestedType(
                "MagickDeath",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (death == null)
                throw new TypeLoadException(outer.FullName + "+MagickDeath");
            playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            outerPlayStateField = outer.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            entityPlayStateField = FindField(death, "mPlayState");
            if (outerPlayStateField == null ||
                outerPlayStateField.FieldType != playState ||
                entityPlayStateField.FieldType != playState)
                throw new MissingFieldException(TypeName, "mPlayState");

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

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null;
                current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
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

        public static IEnumerable<CodeInstruction> OwnerExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveStore(
                instructions,
                outerPlayStateField,
                OpCodes.Ldarg_2,
                "owner Execute");
        }

        public static IEnumerable<CodeInstruction> VectorExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveStore(
                instructions,
                outerPlayStateField,
                OpCodes.Ldarg_2,
                "vector Execute");
        }

        public static IEnumerable<CodeInstruction> SpawnTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(
                instructions,
                outerPlayStateField,
                6,
                "spawn");
        }

        public static IEnumerable<CodeInstruction> DeathConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return RemoveStore(
                instructions,
                entityPlayStateField,
                OpCodes.Ldarg_1,
                "entity constructor");
        }

        public static IEnumerable<CodeInstruction> DeathInitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(
                RemoveStore(
                    instructions,
                    entityPlayStateField,
                    OpCodes.Ldarg_2,
                    "entity Initialize"));
            return ReplaceReads(
                result,
                entityPlayStateField,
                1,
                "entity Initialize");
        }

        public static IEnumerable<CodeInstruction> DeathUpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(
                instructions,
                entityPlayStateField,
                12,
                "entity Update");
        }

        public static IEnumerable<CodeInstruction> DeathDeinitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(
                instructions,
                entityPlayStateField,
                1,
                "entity Deinitialize");
        }

        private static IEnumerable<CodeInstruction> RemoveStore(
            IEnumerable<CodeInstruction> instructions,
            FieldInfo field,
            OpCode valueLoad,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int stores = 0;
            for (int index = 2; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Stfld ||
                    !SameMember(result[index].operand, field))
                    continue;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != valueLoad)
                    throw new InvalidOperationException(
                        "SummonDeath " + methodName +
                        " play-state store source changed.");
                MakeNop(result[index - 2]);
                MakeNop(result[index - 1]);
                MakeNop(result[index]);
                stores++;
            }
            if (stores != 1)
                throw new InvalidOperationException(
                    "Expected one SummonDeath " + methodName +
                    " play-state store, found " + stores + ".");
            return result;
        }

        private static IEnumerable<CodeInstruction> ReplaceReads(
            IEnumerable<CodeInstruction> instructions,
            FieldInfo field,
            int expectedReads,
            string methodName)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int reads = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !SameMember(result[index].operand, field))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "SummonDeath " + methodName +
                        " play-state read source changed.");
                MakeNop(result[index - 1]);
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                reads++;
            }
            if (reads != expectedReads)
                throw new InvalidOperationException(
                    "Expected " + expectedReads + " SummonDeath " +
                    methodName + " play-state reads, found " + reads + ".");
            return result;
        }

        private static bool SameMember(object left, MemberInfo right)
        {
            MemberInfo leftMember = left as MemberInfo;
            return leftMember != null && right != null &&
                leftMember.Module == right.Module &&
                leftMember.MetadataToken == right.MetadataToken;
        }

        private static void MakeNop(CodeInstruction instruction)
        {
            instruction.opcode = OpCodes.Nop;
            instruction.operand = null;
        }
    }
}
