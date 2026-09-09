using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class DerivedSpellEffectPlayStatePatch
    {
        private const string Namespace =
            "Magicka.GameLogic.Spells.SpellEffects.";

        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition PushGetDefinition =
            Define(
                "PushSpell current cache insertion play state",
                "push-spell-current-cache-insertion-state",
                FindPushGet,
                "PushSpellGetFromCacheTranspiler");

        internal static readonly RuntimePatchDefinition PushReturnDefinition =
            Define(
                "PushSpell current cache return play state",
                "push-spell-current-cache-return-state",
                FindPushReturn,
                "PushSpellReturnToCacheTranspiler");

        internal static readonly RuntimePatchDefinition SprayGetDefinition =
            Define(
                "SpraySpell current cache insertion play state",
                "spray-spell-current-cache-insertion-state",
                FindSprayGet,
                "SpraySpellGetFromCacheTranspiler");

        internal static readonly RuntimePatchDefinition SprayReturnDefinition =
            Define(
                "SpraySpell current cache return play state",
                "spray-spell-current-cache-return-state",
                FindSprayReturn,
                "SpraySpellReturnToCacheTranspiler");

        internal static readonly RuntimePatchDefinition SprayUpdateDefinition =
            Define(
                "SpraySpell current geometry play state",
                "spray-spell-current-geometry-state",
                FindSprayUpdate,
                "SpraySpellCastUpdateTranspiler");

        internal static readonly RuntimePatchDefinition ProjectileGetDefinition =
            Define(
                "ProjectileSpell current cache insertion play state",
                "projectile-spell-current-cache-insertion-state",
                FindProjectileGet,
                "ProjectileSpellGetFromCacheTranspiler");

        internal static readonly RuntimePatchDefinition ProjectileReturnDefinition =
            Define(
                "ProjectileSpell current cache return play state",
                "projectile-spell-current-cache-return-state",
                FindProjectileReturn,
                "ProjectileSpellReturnToCacheTranspiler");

        internal static readonly RuntimePatchDefinition RailGunGetDefinition =
            Define(
                "RailGunSpell current cache insertion play state",
                "railgun-spell-current-cache-insertion-state",
                FindRailGunGet,
                "RailGunSpellGetFromCacheTranspiler");

        internal static readonly RuntimePatchDefinition RailGunReturnDefinition =
            Define(
                "RailGunSpell current cache return play state",
                "railgun-spell-current-cache-return-state",
                FindRailGunReturn,
                "RailGunSpellReturnToCacheTranspiler");

        internal static readonly RuntimePatchDefinition RailGunSelfDefinition =
            Define(
                "RailGunSpell current self-cast play state",
                "railgun-spell-current-self-cast-state",
                FindRailGunSelf,
                "RailGunSpellCastSelfTranspiler");

        internal static readonly RuntimePatchDefinition RailGunWeaponDefinition =
            Define(
                "RailGunSpell current weapon-cast play state",
                "railgun-spell-current-weapon-cast-state",
                FindRailGunWeapon,
                "RailGunSpellCastWeaponTranspiler");

        internal static readonly RuntimePatchDefinition RailGunDeinitializeDefinition =
            Define(
                "RailGunSpell current removal play state",
                "railgun-spell-current-removal-state",
                FindRailGunDeInitialize,
                "RailGunSpellDeInitializeTranspiler");

        private static RuntimePatchDefinition Define(
            string name,
            string ownerSuffix,
            Func<Assembly, MethodInfo> findTarget,
            string transpilerName)
        {
            return RuntimePatchDefinition.Transpile(
                name,
                "org.magickacommunitypatch." + ownerSuffix,
                findTarget,
                typeof(DerivedSpellEffectPlayStatePatch).GetMethod(
                    transpilerName));
        }

        private static MethodInfo FindPushGet(Assembly assembly)
        {
            return FindGetFromCache(assembly, "PushSpell");
        }

        private static MethodInfo FindPushReturn(Assembly assembly)
        {
            return FindReturnToCache(assembly, "PushSpell");
        }

        private static MethodInfo FindSprayGet(Assembly assembly)
        {
            return FindGetFromCache(assembly, "SpraySpell");
        }

        private static MethodInfo FindSprayReturn(Assembly assembly)
        {
            return FindReturnToCache(assembly, "SpraySpell");
        }

        private static MethodInfo FindSprayUpdate(Assembly assembly)
        {
            return FindCastUpdate(assembly, "SpraySpell");
        }

        private static MethodInfo FindProjectileGet(Assembly assembly)
        {
            return FindGetFromCache(assembly, "ProjectileSpell");
        }

        private static MethodInfo FindProjectileReturn(Assembly assembly)
        {
            return FindReturnToCache(assembly, "ProjectileSpell");
        }

        private static MethodInfo FindRailGunGet(Assembly assembly)
        {
            return FindGetFromCache(assembly, "RailGunSpell");
        }

        private static MethodInfo FindRailGunReturn(Assembly assembly)
        {
            return FindReturnToCache(assembly, "RailGunSpell");
        }

        private static MethodInfo FindRailGunSelf(Assembly assembly)
        {
            return FindCast(assembly, "CastSelf");
        }

        private static MethodInfo FindRailGunWeapon(Assembly assembly)
        {
            return FindCast(assembly, "CastWeapon");
        }

        private static MethodInfo FindRailGunDeInitialize(Assembly assembly)
        {
            Type railGun;
            Type spellEffect;
            Type caster;
            Configure(assembly, "RailGunSpell", out railGun, out spellEffect,
                out caster);
            return RequireMethod(
                railGun,
                "DeInitialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { caster },
                typeof(void));
        }

        private static MethodInfo FindGetFromCache(
            Assembly assembly,
            string typeName)
        {
            Type type;
            Type spellEffect;
            Type caster;
            Configure(assembly, typeName, out type, out spellEffect, out caster);
            return RequireMethod(
                type,
                "GetFromCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                Type.EmptyTypes,
                spellEffect);
        }

        private static MethodInfo FindReturnToCache(
            Assembly assembly,
            string typeName)
        {
            Type type;
            Type spellEffect;
            Type caster;
            Configure(assembly, typeName, out type, out spellEffect, out caster);
            return RequireMethod(
                type,
                "ReturnToCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { type },
                typeof(void));
        }

        private static MethodInfo FindCastUpdate(
            Assembly assembly,
            string typeName)
        {
            Type type;
            Type spellEffect;
            Type caster;
            Configure(assembly, typeName, out type, out spellEffect, out caster);
            return RequireMethod(
                type,
                "CastUpdate",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[]
                {
                    typeof(float),
                    caster,
                    typeof(float).MakeByRefType()
                },
                typeof(bool));
        }

        private static MethodInfo FindCast(Assembly assembly, string name)
        {
            Type railGun;
            Type spellEffect;
            Type caster;
            Configure(assembly, "RailGunSpell", out railGun, out spellEffect,
                out caster);
            Type spell = assembly.GetType(
                "Magicka.GameLogic.Spells.Spell",
                true);
            return RequireMethod(
                railGun,
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                new Type[] { spell, caster, typeof(bool) },
                typeof(void));
        }

        private static void Configure(
            Assembly assembly,
            string derivedTypeName,
            out Type derivedType,
            out Type spellEffect,
            out Type caster)
        {
            derivedType = assembly.GetType(Namespace + derivedTypeName, true);
            spellEffect = assembly.GetType(Namespace + "SpellEffect", true);
            caster = assembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = spellEffect.GetField(
                "mPlayState",
                BindingFlags.Static | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playState)
                throw new MissingFieldException(
                    spellEffect.FullName,
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

        public static IEnumerable<CodeInstruction> PushSpellGetFromCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "PushSpell.GetFromCache");
        }

        public static IEnumerable<CodeInstruction> PushSpellReturnToCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "PushSpell.ReturnToCache");
        }

        public static IEnumerable<CodeInstruction> SpraySpellGetFromCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "SpraySpell.GetFromCache");
        }

        public static IEnumerable<CodeInstruction> SpraySpellReturnToCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "SpraySpell.ReturnToCache");
        }

        public static IEnumerable<CodeInstruction> SpraySpellCastUpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "SpraySpell.CastUpdate");
        }

        public static IEnumerable<CodeInstruction> ProjectileSpellGetFromCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "ProjectileSpell.GetFromCache");
        }

        public static IEnumerable<CodeInstruction> ProjectileSpellReturnToCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "ProjectileSpell.ReturnToCache");
        }

        public static IEnumerable<CodeInstruction> RailGunSpellGetFromCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "RailGunSpell.GetFromCache");
        }

        public static IEnumerable<CodeInstruction> RailGunSpellReturnToCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "RailGunSpell.ReturnToCache");
        }

        public static IEnumerable<CodeInstruction> RailGunSpellCastSelfTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 2, "RailGunSpell.CastSelf");
        }

        public static IEnumerable<CodeInstruction> RailGunSpellCastWeaponTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 2, "RailGunSpell.CastWeapon");
        }

        public static IEnumerable<CodeInstruction> RailGunSpellDeInitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "RailGunSpell.DeInitialize");
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
                    "Expected " + expected + " " + operation +
                    " play-state reads, found " + matches + ".");
            return result;
        }
    }
}
