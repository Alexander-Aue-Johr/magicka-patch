using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ProjectileSpellConditionCacheScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ProjectileSpellConditionCacheHarness harness =
            new ProjectileSpellConditionCacheHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "projectile_spell.empty_condition_cache",
            harness.EmptyConditionCache());
        report.Add(
            "projectile_spell.cached_condition_identity",
            harness.CachedConditionIdentity());
    }
}

internal sealed class ProjectileSpellConditionCacheHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.ProjectileSpellConditionCachePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly Type conditionCollectionType;
    private readonly Type queueType;
    private readonly MethodInfo enqueue;
    private readonly MethodInfo dequeue;
    private readonly MethodInfo runtimeEnsure;
    private readonly bool manualRecovery;

    internal ProjectileSpellConditionCacheHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        conditionCollectionType = magicka.GetType(
            "Magicka.GameLogic.Entities.Items.ConditionCollection",
            true);
        queueType = typeof(System.Collections.Generic.Queue<>).MakeGenericType(
            conditionCollectionType);
        enqueue = RequireQueueMethod(
            "Enqueue",
            new Type[] { conditionCollectionType });
        dequeue = RequireQueueMethod("Dequeue", Type.EmptyTypes);

        Type projectileSpell = magicka.GetType(
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            true);
        MethodInfo spawnMissile = FindSpawnMissile(magicka, projectileSpell);
        manualRecovery = HasDirectRecovery(spawnMissile);

        runtimeEnsure = null;
        if (runtimePatchEnabled)
        {
            Type patch = Type.GetType(PatchTypeName, false);
            runtimeEnsure = patch == null
                ? null
                : patch.GetMethod(
                    "EnsureConditionCollection",
                    BindingFlags.Static | BindingFlags.Public);
        }
    }

    internal ScenarioResult EmptyConditionCache()
    {
        object queue = Activator.CreateInstance(queueType);
        try
        {
            object result = Take(queue);
            bool returned = result != null &&
                conditionCollectionType.IsInstanceOfType(result);
            return new ScenarioResult(
                returned,
                "returned:" + returned + ",count:" + Count(queue),
                "returned:True,count:0");
        }
        catch (InvalidOperationException)
        {
            return new ScenarioResult(
                false,
                "exception:InvalidOperationException",
                "returned:True,count:0");
        }
    }

    internal ScenarioResult CachedConditionIdentity()
    {
        object queue = Activator.CreateInstance(queueType);
        object cached = Activator.CreateInstance(conditionCollectionType);
        enqueue.Invoke(queue, new object[] { cached });
        object result = Take(queue);
        bool same = Object.ReferenceEquals(cached, result);
        return new ScenarioResult(
            same && Count(queue) == 0,
            "same:" + same + ",count:" + Count(queue),
            "same:True,count:0");
    }

    private object Take(object queue)
    {
        if (runtimeEnsure != null)
            InvokeStatic(runtimeEnsure, queue);
        else if (manualRecovery && Count(queue) == 0)
            enqueue.Invoke(
                queue,
                new object[] { Activator.CreateInstance(conditionCollectionType) });
        return InvokeInstance(dequeue, queue);
    }

    private int Count(object queue)
    {
        return ((ICollection)queue).Count;
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

    private MethodInfo FindSpawnMissile(Assembly magicka, Type projectileSpell)
    {
        Type missile = magicka.GetType(
            "Magicka.GameLogic.Entities.MissileEntity",
            true);
        Type model = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.Model");
        Type owner = magicka.GetType(
            "Magicka.GameLogic.Entities.ISpellCaster",
            true);
        Type vector = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
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

    private bool HasDirectRecovery(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadProjectileSpellConditionCacheBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(ProjectileSpellConditionCacheScenarios),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        for (int index = 0; index < decoded.Count; index++)
        {
            CodeInstruction instruction = decoded[index].GetCodeInstruction();
            ConstructorInfo constructor = instruction.operand as ConstructorInfo;
            if (instruction.opcode == OpCodes.Newobj &&
                constructor != null &&
                constructor.DeclaringType == conditionCollectionType)
                return true;
        }
        return false;
    }

    private static object InvokeStatic(MethodInfo method, object argument)
    {
        try
        {
            return method.Invoke(null, new object[] { argument });
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static object InvokeInstance(MethodInfo method, object target)
    {
        try
        {
            return method.Invoke(target, new object[0]);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }
}
