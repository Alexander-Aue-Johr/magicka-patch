using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ChargeAbilityPlayStatePatch
    {
        private const string HomingTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.HomingCharge";
        private const string StopTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.StopCharge";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;
        private static FieldInfo initializedField;
        private static FieldInfo homingCacheField;
        private static FieldInfo stopCacheField;

        internal static readonly RuntimePatchDefinition HomingExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "HomingCharge play-state reference release",
                "org.magickacommunitypatch.homing-charge-play-state-release",
                FindHomingExecute,
                typeof(ChargeAbilityPlayStatePatch).GetMethod(
                    "HomingReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition HomingUpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "HomingCharge current entity query",
                "org.magickacommunitypatch.homing-charge-current-state",
                FindHomingUpdate,
                typeof(ChargeAbilityPlayStatePatch).GetMethod(
                    "HomingCurrentStateTranspiler"));

        internal static readonly RuntimePatchDefinition StopExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "StopCharge play-state reference release",
                "org.magickacommunitypatch.stop-charge-play-state-release",
                FindStopExecute,
                typeof(ChargeAbilityPlayStatePatch).GetMethod(
                    "StopReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition StopUpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "StopCharge current GreaseSplash state",
                "org.magickacommunitypatch.stop-charge-current-state",
                FindStopUpdate,
                typeof(ChargeAbilityPlayStatePatch).GetMethod(
                    "StopCurrentStateTranspiler"));

        internal static readonly RuntimePatchDefinition CacheCleanupDefinition =
            RuntimePatchDefinition.Transpile(
                "Charge ability level cache cleanup",
                "org.magickacommunitypatch.charge-ability-cache-cleanup",
                FindPlayStateDispose,
                typeof(ChargeAbilityPlayStatePatch).GetMethod("DisposeTranspiler"));

        private static MethodInfo FindHomingExecute(Assembly targetAssembly)
        {
            return FindExecute(targetAssembly, HomingTypeName);
        }

        private static MethodInfo FindHomingUpdate(Assembly targetAssembly)
        {
            return FindUpdate(targetAssembly, HomingTypeName);
        }

        private static MethodInfo FindStopExecute(Assembly targetAssembly)
        {
            return FindExecute(targetAssembly, StopTypeName);
        }

        private static MethodInfo FindStopUpdate(Assembly targetAssembly)
        {
            return FindUpdate(targetAssembly, StopTypeName);
        }

        private static MethodInfo FindExecute(Assembly targetAssembly, string typeName)
        {
            Type abilityType;
            Type playStateType;
            ConfigureAbility(targetAssembly, typeName, out abilityType, out playStateType);
            Type ownerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo method = abilityType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { ownerType, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(abilityType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly, string typeName)
        {
            Type abilityType;
            Type playStateType;
            ConfigureAbility(targetAssembly, typeName, out abilityType, out playStateType);
            MethodInfo match = null;
            MethodInfo[] methods = abilityType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name != "Update" ||
                    methods[index].ReturnType != typeof(void) ||
                    parameters.Length != 2 ||
                    parameters[0].ParameterType.FullName != "PolygonHead.DataChannel" ||
                    parameters[1].ParameterType != typeof(float))
                    continue;
                if (match != null)
                    throw new InvalidOperationException(
                        "Multiple " + abilityType.FullName + ".Update methods matched.");
                match = methods[index];
            }
            if (match == null)
                throw new MissingMethodException(abilityType.FullName, "Update");
            return match;
        }

        private static void ConfigureAbility(
            Assembly targetAssembly,
            string typeName,
            out Type abilityType,
            out Type playStateType)
        {
            abilityType = targetAssembly.GetType(typeName, true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = abilityType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null || playStateField.FieldType != playStateType)
                throw new MissingFieldException(abilityType.FullName, "mPlayState");
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

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            initializedField = playStateType.GetField(
                "mInitialized",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (initializedField == null || initializedField.FieldType != typeof(bool))
                throw new MissingFieldException(playStateType.FullName, "mInitialized");
            homingCacheField = RequireCacheField(targetAssembly, HomingTypeName);
            stopCacheField = RequireCacheField(targetAssembly, StopTypeName);

            MethodInfo method = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return method;
        }

        private static FieldInfo RequireCacheField(
            Assembly targetAssembly,
            string typeName)
        {
            Type type = targetAssembly.GetType(typeName, true);
            FieldInfo field = type.GetField(
                "sCache",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || !typeof(IList).IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, "sCache");
            return field;
        }

        public static IEnumerable<CodeInstruction> HomingReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler(HomingTypeName);
            return ReleaseTranspiler(instructions);
        }

        public static IEnumerable<CodeInstruction> StopReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler(StopTypeName);
            return ReleaseTranspiler(instructions);
        }

        private static IEnumerable<CodeInstruction> ReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    writes++;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple charge ability play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one charge ability play-state assignment, found " +
                    writes + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> HomingCurrentStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler(HomingTypeName);
            return CurrentStateTranspiler(instructions);
        }

        public static IEnumerable<CodeInstruction> StopCurrentStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler(StopTypeName);
            return CurrentStateTranspiler(instructions);
        }

        private static IEnumerable<CodeInstruction> CurrentStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
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
            if (replacements != 1)
                throw new InvalidOperationException(
                    "Expected one charge ability play-state read, found " +
                    replacements + ".");
            return result;
        }

        private static void ConfigureTranspiler(string typeName)
        {
            Type abilityType = FindLoadedType(typeName);
            Type ignoredAbility;
            Type ignoredPlayState;
            ConfigureAbility(
                abilityType.Assembly,
                typeName,
                out ignoredAbility,
                out ignoredPlayState);
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

        public static IEnumerable<CodeInstruction> DisposeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int branch = -1;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index - 1].operand as FieldInfo;
                bool conditionalBranch =
                    result[index].opcode == OpCodes.Brfalse ||
                    result[index].opcode == OpCodes.Brfalse_S ||
                    result[index].opcode == OpCodes.Brtrue ||
                    result[index].opcode == OpCodes.Brtrue_S;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, initializedField) || !conditionalBranch)
                    continue;
                if (branch >= 0)
                    throw new InvalidOperationException(
                        "Multiple PlayState initialization guards matched.");
                branch = index;
            }
            if (branch < 0)
                throw new InvalidOperationException(
                    "PlayState initialization guard was not found.");

            CodeInstruction cleanup = new CodeInstruction(
                OpCodes.Call,
                typeof(ChargeAbilityPlayStatePatch).GetMethod("ClearCaches"));
            if (result[branch].opcode == OpCodes.Brfalse ||
                result[branch].opcode == OpCodes.Brfalse_S)
            {
                result.Insert(branch + 1, cleanup);
            }
            else
            {
                int bodyStart = branch + 2;
                if (bodyStart >= result.Count || result[branch + 1].opcode != OpCodes.Ret)
                    throw new InvalidOperationException(
                        "PlayState initialization guard has an unexpected true branch.");
                Label target = (Label)result[branch].operand;
                if (!result[bodyStart].labels.Contains(target))
                    throw new InvalidOperationException(
                        "PlayState initialization guard target was not found.");
                cleanup.labels.AddRange(result[bodyStart].labels);
                result[bodyStart].labels.Clear();
                result.Insert(bodyStart, cleanup);
            }
            return result;
        }

        public static void ClearCaches()
        {
            ClearCache(homingCacheField);
            ClearCache(stopCacheField);
        }

        private static void ClearCache(FieldInfo field)
        {
            IList cache = (IList)field.GetValue(null);
            if (cache != null)
                cache.Clear();
        }
    }
}
