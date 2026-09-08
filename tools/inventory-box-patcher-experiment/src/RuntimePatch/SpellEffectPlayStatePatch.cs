using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class SpellEffectPlayStatePatch
    {
        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "SpellEffect play-state release",
                "org.magickacommunitypatch.spell-effect-play-state-release",
                FindInitializeCaches,
                typeof(SpellEffectPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition CacheDefinition =
            RuntimePatchDefinition.Transpile(
                "LightningSpell current cache play state",
                "org.magickacommunitypatch.lightning-spell-current-cache-state",
                FindGetFromCache,
                typeof(SpellEffectPlayStatePatch).GetMethod(
                    "CacheTranspiler"));

        internal static readonly RuntimePatchDefinition CastDefinition =
            RuntimePatchDefinition.Transpile(
                "LightningSpell current cast play state",
                "org.magickacommunitypatch.lightning-spell-current-cast-state",
                FindCastUpdate,
                typeof(SpellEffectPlayStatePatch).GetMethod(
                    "CastTranspiler"));

        private static MethodInfo FindInitializeCaches(Assembly targetAssembly)
        {
            Type spellEffectType;
            Type lightningSpellType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spellEffectType,
                out lightningSpellType,
                out playStateType);
            Type contentManagerType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Content.ContentManager");
            MethodInfo method = spellEffectType.GetMethod(
                "IntializeCaches",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playStateType, contentManagerType },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    spellEffectType.FullName,
                    "IntializeCaches");
            return method;
        }

        private static MethodInfo FindGetFromCache(Assembly targetAssembly)
        {
            Type spellEffectType;
            Type lightningSpellType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spellEffectType,
                out lightningSpellType,
                out playStateType);
            MethodInfo method = lightningSpellType.GetMethod(
                "GetFromCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != spellEffectType)
                throw new MissingMethodException(
                    lightningSpellType.FullName,
                    "GetFromCache");
            return method;
        }

        private static MethodInfo FindCastUpdate(Assembly targetAssembly)
        {
            Type spellEffectType;
            Type lightningSpellType;
            Type playStateType;
            Configure(
                targetAssembly,
                out spellEffectType,
                out lightningSpellType,
                out playStateType);
            Type casterType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo method = lightningSpellType.GetMethod(
                "CastUpdate",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    typeof(float),
                    casterType,
                    typeof(float).MakeByRefType()
                },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(
                    lightningSpellType.FullName,
                    "CastUpdate");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type spellEffectType,
            out Type lightningSpellType,
            out Type playStateType)
        {
            spellEffectType = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.SpellEffects.SpellEffect",
                true);
            lightningSpellType = targetAssembly.GetType(
                "Magicka.GameLogic.Spells.SpellEffects.LightningSpell",
                true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = spellEffectType.GetField(
                "mPlayState",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(
                    spellEffectType.FullName,
                    "mPlayState");

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

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int matches = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Stsfld ||
                    !Object.Equals(field, legacyPlayStateField))
                    continue;
                assignment = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one SpellEffect play-state assignment, found " +
                    matches + ".");
            for (int index = assignment - 1; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> CacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 2, "cache");
        }

        public static IEnumerable<CodeInstruction> CastTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "cast");
        }

        private static IEnumerable<CodeInstruction> ReplaceReads(
            IEnumerable<CodeInstruction> instructions,
            int expected,
            string operation)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode != OpCodes.Ldsfld ||
                    !Object.Equals(field, legacyPlayStateField))
                    continue;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                matches++;
            }
            if (matches != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " LightningSpell " + operation +
                    " play-state reads, found " + matches + ".");
            return result;
        }
    }
}
