using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SummonPlayStatePatch
    {
        private const string FlamerTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonFlamer";
        private const string SpiritTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonSpirit";
        private const string BugTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonBug";
        private const string ElementalTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonElemental";
        private const string BeastmanTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.MutateBeastman";
        private const string DischargeTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.OtherworldlyDischarge";

        private static FieldInfo flamerTemplateField;
        private static FieldInfo spiritTemplateField;
        private static FieldInfo bugTemplateField;
        private static FieldInfo elementalTemplateField;
        private static FieldInfo beastmanTemplateField;
        private static FieldInfo dischargeTemplateField;
        private static FieldInfo crossCacheField;
        private static FieldInfo crossTemplateField;
        private static FieldInfo initializedField;

        internal static readonly RuntimePatchDefinition FlamerVectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonFlamer vector play-state release",
                "org.magickacommunitypatch.summon-flamer-vector-play-state-release",
                FindFlamerVectorExecute,
                typeof(SummonPlayStatePatch).GetMethod("FlamerReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition FlamerOwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonFlamer owner play-state release",
                "org.magickacommunitypatch.summon-flamer-owner-play-state-release",
                FindFlamerOwnerExecute,
                typeof(SummonPlayStatePatch).GetMethod("FlamerReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition SpiritVectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonSpirit vector play-state release",
                "org.magickacommunitypatch.summon-spirit-vector-play-state-release",
                FindSpiritVectorExecute,
                typeof(SummonPlayStatePatch).GetMethod("SpiritReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition SpiritOwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonSpirit owner play-state release",
                "org.magickacommunitypatch.summon-spirit-owner-play-state-release",
                FindSpiritOwnerExecute,
                typeof(SummonPlayStatePatch).GetMethod("SpiritReleaseTranspiler"));

        internal static readonly RuntimePatchDefinition FlamerSpawnDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonFlamer current play-state spawn",
                "org.magickacommunitypatch.summon-flamer-current-play-state",
                FindFlamerPrivateExecute,
                typeof(SummonPlayStatePatch).GetMethod("FlamerCurrentPlayStateTranspiler"));

        internal static readonly RuntimePatchDefinition SpiritSpawnDefinition =
            RuntimePatchDefinition.Transpile(
                "SummonSpirit current play-state spawn",
                "org.magickacommunitypatch.summon-spirit-current-play-state",
                FindSpiritPrivateExecute,
                typeof(SummonPlayStatePatch).GetMethod("SpiritCurrentPlayStateTranspiler"));

        internal static readonly RuntimePatchDefinition TemplateCleanupDefinition =
            RuntimePatchDefinition.Transpile(
                "Summon ability template cleanup",
                "org.magickacommunitypatch.summon-template-cleanup",
                FindPlayStateDispose,
                typeof(SummonPlayStatePatch).GetMethod("DisposeTranspiler"));

        private static MethodInfo FindFlamerVectorExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, FlamerTypeName, false);
        }

        private static MethodInfo FindFlamerOwnerExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, FlamerTypeName, true);
        }

        private static MethodInfo FindSpiritVectorExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, SpiritTypeName, false);
        }

        private static MethodInfo FindSpiritOwnerExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, SpiritTypeName, true);
        }

        private static MethodInfo FindFlamerPrivateExecute(Assembly targetAssembly)
        {
            return FindPrivateExecute(targetAssembly, FlamerTypeName);
        }

        private static MethodInfo FindSpiritPrivateExecute(Assembly targetAssembly)
        {
            return FindPrivateExecute(targetAssembly, SpiritTypeName);
        }

        private static MethodInfo FindPublicExecute(
            Assembly targetAssembly,
            string typeName,
            bool ownerOverload)
        {
            Type abilityType;
            Type playStateType;
            ConfigureAbility(targetAssembly, typeName, out abilityType, out playStateType);
            Type firstParameter = ownerOverload
                ? targetAssembly.GetType("Magicka.GameLogic.Entities.ISpellCaster", true)
                : FindLoadedType("Microsoft.Xna.Framework.Vector3");
            MethodInfo method = abilityType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(abilityType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindPrivateExecute(
            Assembly targetAssembly,
            string typeName)
        {
            Type abilityType;
            Type playStateType;
            ConfigureAbility(targetAssembly, typeName, out abilityType, out playStateType);
            Type vectorType = FindLoadedType("Microsoft.Xna.Framework.Vector3");
            MethodInfo method = abilityType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { vectorType, vectorType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(abilityType.FullName, "Execute");
            return method;
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
            FieldInfo legacyPlayState = abilityType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (legacyPlayState == null || legacyPlayState.FieldType != playStateType)
                throw new MissingFieldException(abilityType.FullName, "mPlayState");

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

            flamerTemplateField = RequireTemplateField(targetAssembly, FlamerTypeName);
            spiritTemplateField = RequireTemplateField(targetAssembly, SpiritTypeName);
            bugTemplateField = RequireTemplateField(targetAssembly, BugTypeName);
            elementalTemplateField = RequireTemplateField(
                targetAssembly,
                ElementalTypeName);
            beastmanTemplateField = RequireTemplateField(targetAssembly, BeastmanTypeName);
            dischargeTemplateField = RequireTemplateField(targetAssembly, DischargeTypeName);
            Type crossType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonCross",
                true);
            crossCacheField = crossType.GetField(
                "sCache",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (crossCacheField == null || !typeof(IList).IsAssignableFrom(
                crossCacheField.FieldType))
                throw new MissingFieldException(crossType.FullName, "sCache");
            crossTemplateField = RequireTemplateField(
                targetAssembly,
                crossType.FullName);
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

        private static FieldInfo RequireTemplateField(
            Assembly targetAssembly,
            string typeName)
        {
            Type type = targetAssembly.GetType(typeName, true);
            FieldInfo field = type.GetField(
                "sTemplate",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            if (field == null || field.FieldType != templateType)
                throw new MissingFieldException(type.FullName, "sTemplate");
            return field;
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

        public static IEnumerable<CodeInstruction> FlamerReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReleaseTranspiler(instructions, FlamerTypeName);
        }

        public static IEnumerable<CodeInstruction> SpiritReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReleaseTranspiler(instructions, SpiritTypeName);
        }

        private static IEnumerable<CodeInstruction> ReleaseTranspiler(
            IEnumerable<CodeInstruction> instructions,
            string typeName)
        {
            FieldInfo legacyPlayStateField;
            MethodInfo recentPlayStateGetter;
            ConfigureTranspiler(
                typeName,
                out legacyPlayStateField,
                out recentPlayStateGetter);
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
                        "Multiple summon play-state assignments matched.");
                assignment = index;
            }
            if (assignment < 0 && fieldWrites == 0)
                return result;
            if (assignment < 0 || fieldWrites != 1)
                throw new InvalidOperationException(
                    "Expected one summon play-state assignment, found " +
                    fieldWrites + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> FlamerCurrentPlayStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return CurrentPlayStateTranspiler(instructions, FlamerTypeName);
        }

        public static IEnumerable<CodeInstruction> SpiritCurrentPlayStateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return CurrentPlayStateTranspiler(instructions, SpiritTypeName);
        }

        private static IEnumerable<CodeInstruction> CurrentPlayStateTranspiler(
            IEnumerable<CodeInstruction> instructions,
            string typeName)
        {
            FieldInfo legacyPlayStateField;
            MethodInfo recentPlayStateGetter;
            ConfigureTranspiler(
                typeName,
                out legacyPlayStateField,
                out recentPlayStateGetter);
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
            if (replacements == 0 && existingReplacements == 4)
                return result;
            if (replacements != 4 || existingReplacements != 0)
                throw new InvalidOperationException(
                    "Expected four summon play-state reads, found " + replacements +
                    " new and " + existingReplacements + " existing replacements.");
            return result;
        }

        private static void ConfigureTranspiler(
            string typeName,
            out FieldInfo legacyPlayState,
            out MethodInfo recentPlayStateGetter)
        {
            Type abilityType = FindLoadedType(typeName);
            legacyPlayState = abilityType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (legacyPlayState == null)
                throw new MissingFieldException(abilityType.FullName, "mPlayState");

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
                    field == null || field != initializedField || !conditionalBranch)
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
                typeof(SummonPlayStatePatch).GetMethod("ClearTemplates"));
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

        public static void ClearTemplates()
        {
            flamerTemplateField.SetValue(null, null);
            spiritTemplateField.SetValue(null, null);
            bugTemplateField.SetValue(null, null);
            elementalTemplateField.SetValue(null, null);
            beastmanTemplateField.SetValue(null, null);
            dischargeTemplateField.SetValue(null, null);
            IList crossCache = (IList)crossCacheField.GetValue(null);
            if (crossCache != null)
                crossCache.Clear();
            crossTemplateField.SetValue(null, null);
        }
    }
}
