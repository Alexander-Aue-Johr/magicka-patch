using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using Harmony;
using Harmony.ILCopying;

internal static class ProjectileSpellMissileLifecycleScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ProjectileSpellMissileLifecycleHarness harness =
            new ProjectileSpellMissileLifecycleHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "projectile_spell.null_missile_result",
            harness.NullMissileResult());
        report.Add(
            "projectile_spell.detached_missile_state",
            harness.DetachedMissileState());
        report.Add(
            "projectile_spell.usable_missile_state",
            harness.UsableMissileState());
    }
}

internal sealed class ProjectileSpellMissileLifecycleHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.ProjectileSpellMissileLifecyclePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly Type missileType;
    private readonly Type playStateType;
    private readonly Type entityManagerType;
    private readonly Type conditionCollectionType;
    private readonly Type queueType;
    private readonly FieldInfo missilePlayStateField;
    private readonly FieldInfo playStateEntityManagerField;
    private readonly MethodInfo enqueue;
    private readonly MethodInfo dequeue;
    private readonly MethodInfo runtimeUsable;
    private readonly MethodInfo runtimeReturn;
    private readonly bool manualLifecycleGuard;

    internal ProjectileSpellMissileLifecycleHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        missileType = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity",
            true);
        playStateType = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        entityManagerType = magicka.GetType(
            "Magicka.GameLogic.Entities.EntityManager",
            true);
        conditionCollectionType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.ConditionCollection",
            true);
        queueType = typeof(Queue<>).MakeGenericType(conditionCollectionType);
        missilePlayStateField = FindField(missileType, "mPlayState");
        playStateEntityManagerField = FindTypedField(
            playStateType,
            entityManagerType);
        enqueue = RequireQueueMethod(
            "Enqueue",
            new Type[] { conditionCollectionType });
        dequeue = RequireQueueMethod("Dequeue", Type.EmptyTypes);

        Type projectileSpell = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            true);
        MethodInfo spawnMissile = FindSpawnMissile(magicka, projectileSpell);
        manualLifecycleGuard = HasManualLifecycleGuard(spawnMissile);

        runtimeUsable = null;
        runtimeReturn = null;
        if (runtimePatchEnabled)
        {
            Type patch = Type.GetType(PatchTypeName, false);
            if (patch != null)
            {
                runtimeUsable = patch.GetMethod(
                    "HasUsableEntityManager",
                    BindingFlags.Static | BindingFlags.Public);
                runtimeReturn = patch.GetMethod(
                    "ReturnConditionCollection",
                    BindingFlags.Static | BindingFlags.Public);
            }
        }
    }

    internal ScenarioResult NullMissileResult()
    {
        object queue = NewQueueWithOneEntry();
        object borrowed = Invoke(dequeue, queue, new object[0]);
        Exception failure = null;
        try
        {
            bool usable;
            if (runtimeUsable != null)
                usable = (bool)Invoke(runtimeUsable, null, new object[] { null });
            else
                usable = !manualLifecycleGuard;

            if (usable)
                throw new NullReferenceException();
            ReturnBorrowed(queue, borrowed);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        string actual = "exception:" + ExceptionName(failure) +
            ",count:" + ((ICollection)queue).Count;
        const string expected = "exception:none,count:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    internal ScenarioResult DetachedMissileState()
    {
        object missile = NewUninitialized(missileType);
        return TestMissileState(missile, false);
    }

    internal ScenarioResult UsableMissileState()
    {
        object missile = NewUninitialized(missileType);
        object playState = NewUninitialized(playStateType);
        object entityManager = NewUninitialized(entityManagerType);
        missilePlayStateField.SetValue(missile, playState);
        playStateEntityManagerField.SetValue(playState, entityManager);
        return TestMissileState(missile, true);
    }

    private ScenarioResult TestMissileState(object missile, bool expectedUsable)
    {
        object queue = NewQueueWithOneEntry();
        object borrowed = Invoke(dequeue, queue, new object[0]);
        bool usable;
        if (runtimeUsable != null)
            usable = (bool)Invoke(
                runtimeUsable,
                null,
                new object[] { missile });
        else if (manualLifecycleGuard)
            usable = missilePlayStateField.GetValue(missile) != null &&
                playStateEntityManagerField.GetValue(
                    missilePlayStateField.GetValue(missile)) != null;
        else
            usable = true;

        ReturnBorrowed(queue, borrowed);
        string actual = "usable:" + usable +
            ",count:" + ((ICollection)queue).Count;
        string expected = "usable:" + expectedUsable + ",count:1";
        return new ScenarioResult(actual == expected, actual, expected);
    }

    private void ReturnBorrowed(object queue, object borrowed)
    {
        if (runtimeReturn != null)
            Invoke(runtimeReturn, null, new object[] { queue, borrowed });
        else
            Invoke(enqueue, queue, new object[] { borrowed });
    }

    private object NewQueueWithOneEntry()
    {
        object queue = Activator.CreateInstance(queueType);
        Invoke(
            enqueue,
            queue,
            new object[] { Activator.CreateInstance(conditionCollectionType) });
        return queue;
    }

    private bool HasManualLifecycleGuard(MethodInfo method)
    {
        Type entity = missileType.BaseType;
        PropertyInfo playState = entity.GetProperty(
            "PlayState",
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
        MethodInfo getter = playState == null ? null : playState.GetGetMethod(true);
        if (getter == null)
            return false;
        DynamicMethod target = new DynamicMethod(
            "ReadProjectileSpellMissileLifecycleBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(ProjectileSpellMissileLifecycleScenarios),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        int calls = 0;
        for (int index = 0; index < decoded.Count; index++)
        {
            CodeInstruction instruction = decoded[index].GetCodeInstruction();
            if ((instruction.opcode == OpCodes.Call ||
                instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, getter))
                calls++;
        }
        return calls >= 2;
    }

    private MethodInfo FindSpawnMissile(Assembly magicka, Type projectileSpell)
    {
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        Type model = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.Model");
        Type spell = magicka.GetType(
            "Magicka.GameLogic.Spells.Spell",
            true);
        MethodInfo method = projectileSpell.GetMethod(
            "SpawnMissile",
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            new Type[]
            {
                missileType.MakeByRefType(),
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

    private MethodInfo RequireQueueMethod(string name, Type[] parameters)
    {
        MethodInfo method = queueType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            null,
            parameters,
            null);
        if (method == null)
            throw new MissingMethodException(queueType.FullName, name);
        return method;
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
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
            throw new MissingFieldException(type.FullName, fieldType.FullName);
        return found;
    }

    private static object Invoke(
        MethodInfo method,
        object target,
        object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object NewUninitialized(Type type)
    {
        object value = FormatterServices.GetUninitializedObject(type);
        GC.SuppressFinalize(value);
        return value;
    }

    private static string ExceptionName(Exception failure)
    {
        return failure == null ? "none" : failure.GetType().Name;
    }
}
