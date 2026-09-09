using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate object ProjectileSpawnObjectGetter(object instance);

    public static class ProjectileSpawnOwnerGuardPatch
    {
        private const string ProjectileSpellTypeName =
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell";

        private static ProjectileSpawnObjectGetter getPlayState;
        private static ProjectileSpawnObjectGetter getEntityManager;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "ProjectileSpell detached owner guard",
                "org.magickacommunitypatch.projectile-spawn-owner-guard",
                FindSpawnMissile,
                target => typeof(ProjectileSpawnOwnerGuardPatch).GetMethod(
                    "Prefix"));

        private static MethodInfo FindSpawnMissile(Assembly assembly)
        {
            Type projectileSpell = assembly.GetType(
                ProjectileSpellTypeName,
                true);
            Type missile = assembly.GetType(
                "Magicka.GameLogic.Entities.MissileEntity",
                true);
            Type model = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Graphics.Model");
            Type owner = assembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector3");
            Type spell = assembly.GetType(
                "Magicka.GameLogic.Spells.Spell",
                true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type entityManager = assembly.GetType(
                "Magicka.GameLogic.Entities.EntityManager",
                true);

            MethodInfo playStateGetter = RequireGetter(
                owner,
                "PlayState",
                playState);
            MethodInfo entityManagerGetter = RequireGetter(
                playState,
                "EntityManager",
                entityManager);
            getPlayState = CreateGetter(owner, playStateGetter);
            getEntityManager = CreateGetter(playState, entityManagerGetter);

            MethodInfo method = projectileSpell.GetMethod(
                "SpawnMissile",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    missile.MakeByRefType(),
                    model,
                    owner,
                    typeof(float),
                    vector.MakeByRefType(),
                    vector.MakeByRefType(),
                    spell.MakeByRefType(),
                    typeof(float),
                    typeof(int)
                },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    projectileSpell.FullName,
                    "SpawnMissile");
            return method;
        }

        public static bool Prefix(object iOwner)
        {
            if (iOwner == null)
                return false;
            object playState = getPlayState(iOwner);
            return playState != null && getEntityManager(playState) != null;
        }

        private static ProjectileSpawnObjectGetter CreateGetter(
            Type instanceType,
            MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "Get" + getter.Name,
                typeof(object),
                new Type[] { typeof(object) },
                typeof(ProjectileSpawnOwnerGuardPatch),
                true);
            ILGenerator generator = method.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Castclass, instanceType);
            generator.EmitCall(OpCodes.Callvirt, getter, null);
            generator.Emit(OpCodes.Ret);
            return (ProjectileSpawnObjectGetter)method.CreateDelegate(
                typeof(ProjectileSpawnObjectGetter));
        }

        private static MethodInfo RequireGetter(
            Type type,
            string name,
            Type returnType)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            MethodInfo getter = property == null
                ? null
                : property.GetGetMethod(true);
            if (getter == null || getter.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, "get_" + name);
            return getter;
        }
    }
}
