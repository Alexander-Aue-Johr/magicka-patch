using System;
using System.Reflection;
using System.Runtime.Serialization;

internal static class ProjectileSpawnOwnerGuardScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ProjectileSpawnOwnerGuardHarness harness =
            new ProjectileSpawnOwnerGuardHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("projectile_spawn.null_owner", harness.NullOwner());
        report.Add(
            "projectile_spawn.complete_owner",
            harness.CompleteOwner());
    }
}

internal sealed class ProjectileSpawnOwnerGuardHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.ProjectileSpawnOwnerGuardPatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly Assembly magicka;
    private readonly MethodInfo spawnMissile;
    private readonly FieldInfo conditionCacheField;
    private readonly Type conditionCollectionType;
    private readonly Type missileType;
    private readonly Type vectorType;
    private readonly Type spellType;
    private readonly Type avatarType;
    private readonly Type playStateType;
    private readonly FieldInfo entityPlayStateField;
    private readonly FieldInfo playStateEntityManagerField;

    internal ProjectileSpawnOwnerGuardHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.magicka = magicka;
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type projectileSpell = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            true);
        conditionCollectionType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.ConditionCollection",
            true);
        missileType = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity",
            true);
        Type ownerType = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type modelType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.Model");
        spellType = magicka.GetType("Magicka.GameLogic.Spells.Spell", true);
        avatarType = magicka.GetType(
            "Magicka.GameLogic.Entities.Avatar",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);

        spawnMissile = projectileSpell.GetMethod(
            "SpawnMissile",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                missileType.MakeByRefType(),
                modelType,
                ownerType,
                typeof(float),
                vectorType.MakeByRefType(),
                vectorType.MakeByRefType(),
                spellType.MakeByRefType(),
                typeof(float),
                typeof(int)
            },
            null);
        conditionCacheField = projectileSpell.GetField(
            "sCachedConditions",
            BindingFlags.Static | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        entityPlayStateField = FindField(avatarType, "mPlayState");
        playStateEntityManagerField = FindTypedField(
            playStateType,
            entityManagerType);
        if (spawnMissile == null || spawnMissile.ReturnType != typeof(void))
            throw new MissingMethodException(
                projectileSpell.FullName,
                "SpawnMissile");
        if (conditionCacheField == null)
            throw new MissingFieldException(
                projectileSpell.FullName,
                "sCachedConditions");
    }

    internal ScenarioResult NullOwner()
    {
        object originalCache = conditionCacheField.GetValue(null);
        object testCache = Activator.CreateInstance(
            conditionCacheField.FieldType);
        MethodInfo enqueue = conditionCacheField.FieldType.GetMethod(
            "Enqueue",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { conditionCollectionType },
            null);
        if (enqueue == null)
            throw new MissingMethodException(
                conditionCacheField.FieldType.FullName,
                "Enqueue");
        enqueue.Invoke(
            testCache,
            new object[] { Activator.CreateInstance(conditionCollectionType) });
        conditionCacheField.SetValue(null, testCache);
        object[] arguments = new object[]
        {
            null,
            null,
            null,
            0f,
            Activator.CreateInstance(vectorType),
            Activator.CreateInstance(vectorType),
            Activator.CreateInstance(spellType),
            0f,
            1
        };
        Exception failure = null;
        try
        {
            spawnMissile.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception)
        {
            failure = exception.InnerException ?? exception;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            conditionCacheField.SetValue(null, originalCache);
        }

        string actual = "exception:" +
            (failure == null ? "none" : failure.GetType().FullName) +
            ",missile:" + (arguments[0] == null ? "unchanged" : "changed");
        const string expected = "exception:none,missile:unchanged";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult CompleteOwner()
    {
        object owner = NewUninitialized(avatarType);
        object playState = NewUninitialized(playStateType);
        object entityManager = NewUninitialized(
            playStateEntityManagerField.FieldType);
        entityPlayStateField.SetValue(owner, playState);
        playStateEntityManagerField.SetValue(playState, entityManager);

        bool runOriginal = true;
        if (runtimePatchEnabled)
        {
            Type patch = Type.GetType(PatchTypeName, false);
            MethodInfo prefix = patch == null
                ? null
                : patch.GetMethod(
                    "Prefix",
                    BindingFlags.Static | BindingFlags.Public);
            if (prefix == null)
                runOriginal = false;
            else
                runOriginal = (bool)prefix.Invoke(
                    null,
                    new object[] { owner });
        }

        string actual = "run_original:" + runOriginal;
        const string expected = "run_original:True";
        return new ScenarioResult(
            actual == expected,
            actual,
            expected);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null;
            current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    private static FieldInfo FindTypedField(Type type, Type fieldType)
    {
        FieldInfo found = null;
        FieldInfo[] fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        for (int index = 0; index < fields.Length; index++)
        {
            if (fields[index].FieldType != fieldType)
                continue;
            if (found != null)
                throw new AmbiguousMatchException(
                    type.FullName + " field of type " + fieldType.FullName);
            found = fields[index];
        }
        if (found == null)
            throw new MissingFieldException(
                type.FullName,
                fieldType.FullName);
        return found;
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }
}
