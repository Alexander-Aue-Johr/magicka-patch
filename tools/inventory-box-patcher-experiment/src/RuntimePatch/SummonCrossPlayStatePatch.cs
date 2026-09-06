using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SummonCrossPlayStatePatch
    {
        private const string TypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonCross";

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonCross vector play-state release",
                "org.magickacommunitypatch.summon-cross-vector-play-state-release",
                FindVectorExecute,
                typeof(SummonCrossPlayStatePatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonCross owner play-state release",
                "org.magickacommunitypatch.summon-cross-owner-play-state-release",
                FindOwnerExecute,
                typeof(SummonCrossPlayStatePatch).GetMethod("ReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition SpawnDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonCross current play-state spawn",
                "org.magickacommunitypatch.summon-cross-current-play-state",
                FindPrivateExecute,
                typeof(SummonCrossPlayStatePatch).GetMethod("CurrentPlayStateTranspiler"));

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, false);
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, true);
        }

        private static MethodInfo FindPublicExecute(
            Assembly targetAssembly,
            bool ownerOverload)
        {
            Type crossType;
            Type playStateType;
            Configure(targetAssembly, out crossType, out playStateType);
            Type firstParameter = ownerOverload
                ? targetAssembly.GetType("Magicka.GameLogic.Entities.ISpellCaster", true)
                : FindLoadedType("Microsoft.Xna.Framework.Vector3");
            MethodInfo method = crossType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(crossType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindPrivateExecute(Assembly targetAssembly)
        {
            Type crossType;
            Type playStateType;
            Configure(targetAssembly, out crossType, out playStateType);
            MethodInfo method = crossType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(crossType.FullName, "Execute");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type crossType,
            out Type playStateType)
        {
            crossType = targetAssembly.GetType(TypeName, true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            FieldInfo legacyPlayState = crossType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (legacyPlayState == null || legacyPlayState.FieldType != playStateType)
                throw new MissingFieldException(crossType.FullName, "mPlayState");

            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> ReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo legacyPlayStateField;
            MethodInfo recentPlayStateGetter;
            ConfigureTranspiler(out legacyPlayStateField, out recentPlayStateGetter);
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            int fieldWrites = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    field != null && field == legacyPlayStateField)
                    fieldWrites++;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    field == null || field != legacyPlayStateField)
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple SummonCross play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0 && fieldWrites == 0)
                return result;
            if (assignment < 0 || fieldWrites != 1)
                throw new InvalidOperationException(
                    "Expected one SummonCross play-state assignment, found " +
                    fieldWrites + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> CurrentPlayStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo legacyPlayStateField;
            MethodInfo recentPlayStateGetter;
            ConfigureTranspiler(out legacyPlayStateField, out recentPlayStateGetter);
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int replacements = 0;
            int existingReplacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode == OpCodes.Call &&
                    Object.Equals(result[index].operand, recentPlayStateGetter))
                {
                    existingReplacements++;
                    continue;
                }

                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    field == null || field != legacyPlayStateField)
                    continue;

                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements == 0 && existingReplacements == 3)
                return result;
            if (replacements != 3 || existingReplacements != 0)
                throw new InvalidOperationException(
                    "Expected three SummonCross play-state reads, found " +
                    replacements + " new and " + existingReplacements +
                    " existing replacements.");
            return result;
        }

        private static void ConfigureTranspiler(
            out FieldInfo legacyPlayState,
            out MethodInfo recentPlayStateGetter)
        {
            Type crossType = FindLoadedType(TypeName);
            legacyPlayState = crossType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (legacyPlayState == null)
                throw new MissingFieldException(crossType.FullName, "mPlayState");

            Type playStateType = legacyPlayState.FieldType;
            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_RecentPlayState");
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            throw new TypeLoadException(fullName);
        }
    }
}
