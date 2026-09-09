using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PoolExpansionPatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchSession.Apply(
                targetAssembly,
                PoolExpansionPatch.AvatarDefinition);
            ApplyOptional(
                targetAssembly,
                PoolExpansionPatch.GenericBossDefinition,
                PoolExpansionPatch.HasGenericBossPool(targetAssembly),
                "GenericBoss.GetFromCache is not present in this Magicka version.");
            ApplyOptional(
                targetAssembly,
                PoolExpansionPatch.DamageableDefinition,
                PoolExpansionPatch.HasDamageablePool(targetAssembly),
                "DamageablePhysicsEntity.GetFromCache is not present in this Magicka version.");
            RuntimePatchSession.Apply(
                targetAssembly,
                PoolExpansionPatch.GibDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                PoolExpansionPatch.ProjectileDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                PoolExpansionPatch.SprayDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                PoolExpansionPatch.RailGunDefinition);
            RuntimePatchSession.Apply(
                targetAssembly,
                PoolExpansionPatch.ShieldDefinition);
        }

        private static void ApplyOptional(
            Assembly targetAssembly,
            RuntimePatchDefinition definition,
            bool available,
            string reason)
        {
            if (!available)
            {
                RuntimePatchAudit.WriteNotApplicable(definition, reason);
                return;
            }
            RuntimePatchSession.Apply(targetAssembly, definition);
        }
    }

    public static class PoolExpansionPatch
    {
        private static PoolConfiguration avatar;
        private static PoolConfiguration genericBoss;
        private static PoolConfiguration damageable;
        private static PoolConfiguration gib;
        private static PoolConfiguration projectile;
        private static PoolConfiguration spray;
        private static PoolConfiguration railGun;
        private static PoolConfiguration shield;
        private static FieldInfo avatarPlayerField;
        private static MethodInfo avatarUniqueHandleGetter;

        internal static readonly RuntimePatchDefinition AvatarDefinition =
            RuntimePatchDefinition.Postfix(
                "Avatar exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.avatar",
                FindAvatar,
                CreateAvatarPostfix);

        internal static readonly RuntimePatchDefinition GenericBossDefinition =
            RuntimePatchDefinition.Prefix(
                "GenericBoss exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.generic-boss",
                FindGenericBoss,
                FixedPrefix("GenericBossPrefix"));

        internal static readonly RuntimePatchDefinition DamageableDefinition =
            RuntimePatchDefinition.Prefix(
                "DamageablePhysicsEntity exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.damageable-physics-entity",
                FindDamageable,
                FixedPrefix("DamageablePrefix"));

        internal static readonly RuntimePatchDefinition GibDefinition =
            RuntimePatchDefinition.Prefix(
                "Gib exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.gib",
                FindGib,
                FixedPrefix("GibPrefix"));

        internal static readonly RuntimePatchDefinition ProjectileDefinition =
            RuntimePatchDefinition.Prefix(
                "ProjectileSpell exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.projectile-spell",
                FindProjectile,
                FixedPrefix("ProjectilePrefix"));

        internal static readonly RuntimePatchDefinition SprayDefinition =
            RuntimePatchDefinition.Prefix(
                "SpraySpell exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.spray-spell",
                FindSpray,
                FixedPrefix("SprayPrefix"));

        internal static readonly RuntimePatchDefinition RailGunDefinition =
            RuntimePatchDefinition.Prefix(
                "RailGunSpell exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.railgun-spell",
                FindRailGun,
                FixedPrefix("RailGunPrefix"));

        internal static readonly RuntimePatchDefinition ShieldDefinition =
            RuntimePatchDefinition.Prefix(
                "ShieldSpell exhausted pool recovery",
                "org.magickacommunitypatch.pool-expansion.shield-spell",
                FindShield,
                FixedPrefix("ShieldPrefix"));

        private static Func<MethodInfo, MethodInfo> FixedPrefix(string name)
        {
            MethodInfo prefix = typeof(PoolExpansionPatch).GetMethod(name);
            return delegate { return prefix; };
        }

        internal static bool HasGenericBossPool(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.GenericBoss",
                true);
            return FindMethod(
                type,
                new Type[] { typeof(int), typeof(int), typeof(int) }) != null;
        }

        internal static bool HasDamageablePool(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
                true);
            return FindMethod(type, Type.EmptyTypes) != null;
        }

        private static MethodInfo FindAvatar(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities.Avatar",
                true);
            Type player = assembly.GetType("Magicka.GameLogic.Player", true);
            string lockName = type.GetField(
                "mCacheLock",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly) == null
                ? null
                : "mCacheLock";
            avatar = Configure(
                assembly,
                type,
                "mCache",
                lockName,
                new Type[] { PlayState(assembly) });
            avatarPlayerField = type.GetField(
                "mPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            PropertyInfo uniqueHandle = player.GetProperty(
                "UniqueAvatarHandle",
                BindingFlags.Instance | BindingFlags.Public);
            avatarUniqueHandleGetter = uniqueHandle == null
                ? null
                : uniqueHandle.GetGetMethod();
            if (avatarPlayerField == null ||
                avatarPlayerField.FieldType != player ||
                (avatarUniqueHandleGetter != null &&
                    avatarUniqueHandleGetter.ReturnType != typeof(ushort)))
                throw new MissingMemberException(
                    "Avatar player assignment contract is incomplete.");
            return RequireMethod(type, new Type[] { player });
        }

        private static MethodInfo FindGenericBoss(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities.Bosses.GenericBoss",
                true);
            genericBoss = Configure(
                assembly,
                type,
                "sCache",
                "sCacheLock",
                new Type[]
                {
                    PlayState(assembly),
                    typeof(int),
                    typeof(int),
                    typeof(int)
                });
            return RequireMethod(
                type,
                new Type[] { typeof(int), typeof(int), typeof(int) });
        }

        private static MethodInfo FindDamageable(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
                true);
            damageable = Configure(
                assembly,
                type,
                "sCache",
                "sCacheLock",
                new Type[] { PlayState(assembly) });
            return RequireMethod(type, Type.EmptyTypes);
        }

        private static MethodInfo FindGib(Assembly assembly)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Entities.Gib",
                true);
            gib = Configure(
                assembly,
                type,
                "GibCache",
                null,
                new Type[] { PlayState(assembly) });
            return RequireMethod(type, Type.EmptyTypes);
        }

        private static MethodInfo FindProjectile(Assembly assembly)
        {
            return FindSpellPool(
                assembly,
                "ProjectileSpell",
                "mCache",
                out projectile);
        }

        private static MethodInfo FindRailGun(Assembly assembly)
        {
            return FindSpellPool(
                assembly,
                "RailGunSpell",
                "mCache",
                out railGun);
        }

        private static MethodInfo FindSpray(Assembly assembly)
        {
            return FindSpellPool(
                assembly,
                "SpraySpell",
                "mCache",
                out spray);
        }

        private static MethodInfo FindShield(Assembly assembly)
        {
            return FindSpellPool(
                assembly,
                "ShieldSpell",
                "sCache",
                out shield);
        }

        private static MethodInfo FindSpellPool(
            Assembly assembly,
            string name,
            string cacheName,
            out PoolConfiguration configuration)
        {
            Type type = assembly.GetType(
                "Magicka.GameLogic.Spells.SpellEffects." + name,
                true);
            configuration = Configure(
                assembly,
                type,
                cacheName,
                null,
                Type.EmptyTypes);
            return RequireMethod(type, Type.EmptyTypes);
        }

        private static PoolConfiguration Configure(
            Assembly assembly,
            Type type,
            string cacheName,
            string lockName,
            Type[] constructorParameters)
        {
            FieldInfo cache = type.GetField(
                cacheName,
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (cache == null || !typeof(IList).IsAssignableFrom(cache.FieldType))
                throw new MissingFieldException(type.FullName, cacheName);
            FieldInfo sync = null;
            if (lockName != null)
            {
                sync = type.GetField(
                    lockName,
                    BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (sync == null)
                    throw new MissingFieldException(type.FullName, lockName);
            }
            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                constructorParameters,
                null);
            if (constructor == null)
                throw new MissingMethodException(type.FullName, ".ctor");
            MethodInfo recentPlayState = null;
            if (constructorParameters.Length > 0 &&
                constructorParameters[0] == PlayState(assembly))
            {
                PropertyInfo recent = constructorParameters[0].GetProperty(
                    "RecentPlayState",
                    BindingFlags.Static | BindingFlags.Public);
                recentPlayState = recent == null
                    ? null
                    : recent.GetGetMethod();
                if (recentPlayState == null ||
                    recentPlayState.ReturnType != constructorParameters[0])
                    throw new MissingMethodException(
                        constructorParameters[0].FullName,
                        "get_RecentPlayState");
            }
            return new PoolConfiguration(
                cache,
                sync,
                constructor,
                recentPlayState);
        }

        private static Type PlayState(Assembly assembly)
        {
            return assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
        }

        private static MethodInfo RequireMethod(Type type, Type[] parameters)
        {
            MethodInfo method = FindMethod(type, parameters);
            if (method == null)
                throw new MissingMethodException(type.FullName, "GetFromCache");
            return method;
        }

        private static MethodInfo FindMethod(Type type, Type[] parameters)
        {
            return type.GetMethod(
                "GetFromCache",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
        }

        private static MethodInfo CreateAvatarPostfix(MethodInfo target)
        {
            Type player = target.GetParameters()[0].ParameterType;
            Type avatarType = target.ReturnType;
            Type postfixType = typeof(AvatarPoolExpansionPostfix<,>)
                .MakeGenericType(player, avatarType);
            MethodInfo postfix = postfixType.GetMethod("Postfix");
            if (postfix == null)
                throw new MissingMethodException(
                    postfixType.FullName,
                    "Postfix");
            return postfix;
        }

        public static object CompleteAvatar(object player, object result)
        {
            if (result != null || player == null)
                return result;
            ushort handle = avatarUniqueHandleGetter == null
                ? (ushort)0
                : (ushort)avatarUniqueHandleGetter.Invoke(player, null);
            if (handle != 0)
                return null;
            object created = avatar.Constructor.Invoke(
                new object[] { CurrentPlayState(avatar) });
            avatarPlayerField.SetValue(created, player);
            return created;
        }

        public static void GenericBossPrefix(
            int iType,
            int iUniqueId,
            int iMeshIdx)
        {
            EnsureEntry(
                genericBoss,
                new object[]
                {
                    CurrentPlayState(genericBoss),
                    iType,
                    iUniqueId,
                    iMeshIdx
                });
        }

        public static void DamageablePrefix()
        {
            EnsureEntry(
                damageable,
                new object[] { CurrentPlayState(damageable) });
        }

        public static void GibPrefix()
        {
            EnsureEntry(gib, new object[] { CurrentPlayState(gib) });
        }

        public static void ProjectilePrefix()
        {
            EnsureEntry(projectile, new object[0]);
        }

        public static void SprayPrefix()
        {
            EnsureEntry(spray, new object[0]);
        }

        public static void RailGunPrefix()
        {
            EnsureEntry(railGun, new object[0]);
        }

        public static void ShieldPrefix()
        {
            EnsureEntry(shield, new object[0]);
        }

        private static object CurrentPlayState(PoolConfiguration configuration)
        {
            if (configuration.RecentPlayStateGetter == null)
                throw new MissingMethodException(
                    configuration.Constructor.DeclaringType.FullName,
                    "get_RecentPlayState");
            return configuration.RecentPlayStateGetter.Invoke(null, null);
        }

        private static void EnsureEntry(
            PoolConfiguration configuration,
            object[] constructorArguments)
        {
            object sync = configuration.LockField == null
                ? null
                : configuration.LockField.GetValue(null);
            if (sync == null)
            {
                EnsureEntryWithoutLock(configuration, constructorArguments);
                return;
            }
            lock (sync)
            {
                EnsureEntryWithoutLock(configuration, constructorArguments);
            }
        }

        private static void EnsureEntryWithoutLock(
            PoolConfiguration configuration,
            object[] constructorArguments)
        {
            IList cache = configuration.CacheField.GetValue(null) as IList;
            if (cache == null)
            {
                cache = (IList)Activator.CreateInstance(
                    configuration.CacheField.FieldType);
                configuration.CacheField.SetValue(null, cache);
            }
            if (cache.Count != 0)
                return;
            cache.Add(configuration.Constructor.Invoke(constructorArguments));
        }
    }

    internal sealed class PoolConfiguration
    {
        internal FieldInfo CacheField { get; private set; }
        internal FieldInfo LockField { get; private set; }
        internal ConstructorInfo Constructor { get; private set; }
        internal MethodInfo RecentPlayStateGetter { get; private set; }

        internal PoolConfiguration(
            FieldInfo cacheField,
            FieldInfo lockField,
            ConstructorInfo constructor,
            MethodInfo recentPlayStateGetter)
        {
            CacheField = cacheField;
            LockField = lockField;
            Constructor = constructor;
            RecentPlayStateGetter = recentPlayStateGetter;
        }
    }

    public static class AvatarPoolExpansionPostfix<TPlayer, TAvatar>
    {
        public static void Postfix(TPlayer iPlayer, ref TAvatar __result)
        {
            __result = (TAvatar)PoolExpansionPatch.CompleteAvatar(
                iPlayer,
                __result);
        }
    }
}
