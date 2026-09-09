using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class PoolExpansionScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "avatar",
            "Magicka.GameLogic.Entities.Avatar",
            "org.magickacommunitypatch.pool-expansion.avatar",
            new string[] { "Magicka.GameLogic.Player" },
            true);
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "generic_boss",
            "Magicka.GameLogic.Entities.Bosses.GenericBoss",
            "org.magickacommunitypatch.pool-expansion.generic-boss",
            new string[]
            {
                typeof(int).FullName,
                typeof(int).FullName,
                typeof(int).FullName
            },
            false);
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "damageable_physics_entity",
            "Magicka.GameLogic.Entities.DamageablePhysicsEntity",
            "org.magickacommunitypatch.pool-expansion.damageable-physics-entity",
            new string[0],
            false);
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "gib",
            "Magicka.GameLogic.Entities.Gib",
            "org.magickacommunitypatch.pool-expansion.gib",
            new string[0],
            false);
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "projectile_spell",
            "Magicka.GameLogic.Spells.SpellEffects.ProjectileSpell",
            "org.magickacommunitypatch.pool-expansion.projectile-spell",
            new string[0],
            false);
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "railgun_spell",
            "Magicka.GameLogic.Spells.SpellEffects.RailGunSpell",
            "org.magickacommunitypatch.pool-expansion.railgun-spell",
            new string[0],
            false);
        Add(
            magicka,
            runtimePatchEnabled,
            report,
            "shield_spell",
            "Magicka.GameLogic.Spells.SpellEffects.ShieldSpell",
            "org.magickacommunitypatch.pool-expansion.shield-spell",
            new string[0],
            false);
    }

    private static void Add(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report,
        string key,
        string typeName,
        string owner,
        string[] parameterTypeNames,
        bool postfix)
    {
        Type type = magicka.GetType(typeName, true);
        MethodInfo method = FindGetFromCache(type, parameterTypeNames);
        if (method == null)
        {
            report.AddNotApplicable(
                "pool_expansion." + key,
                typeName + ".GetFromCache is not present in this version");
            return;
        }

        if (runtimePatchEnabled)
        {
            bool registered = HasRuntimePatch(method, owner, postfix);
            report.Add(
                "pool_expansion." + key,
                new ScenarioResult(
                    registered,
                    "registered:" + registered,
                    "registered:True"));
            return;
        }

        int constructors = CountConstructors(method, type);
        bool recovery = constructors > 0;
        report.Add(
            "pool_expansion." + key,
            new ScenarioResult(
                recovery,
                "recovery:" + recovery +
                    ",constructors:" + constructors,
                "recovery:True"));
    }

    private static MethodInfo FindGetFromCache(
        Type type,
        string[] parameterTypeNames)
    {
        MethodInfo match = null;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int index = 0; index < methods.Length; index++)
        {
            MethodInfo method = methods[index];
            if (method.Name != "GetFromCache")
                continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterTypeNames.Length)
                continue;
            bool matched = true;
            for (int parameter = 0; parameter < parameters.Length; parameter++)
            {
                if (parameters[parameter].ParameterType.FullName !=
                    parameterTypeNames[parameter])
                {
                    matched = false;
                    break;
                }
            }
            if (!matched)
                continue;
            if (match != null)
                throw new InvalidOperationException(
                    "Multiple GetFromCache methods matched " +
                    type.FullName + ".");
            match = method;
        }
        return match;
    }

    private static bool HasRuntimePatch(
        MethodInfo method,
        string owner,
        bool postfix)
    {
        Patches patches = HarmonyInstance.Create(
                "org.magickacommunitypatch.behavior-probe-pool-expansion")
            .GetPatchInfo(method);
        if (patches == null)
            return false;
        IEnumerable<Patch> candidates = postfix
            ? patches.Postfixes
            : patches.Prefixes;
        foreach (Patch patch in candidates)
        {
            if (patch.owner == owner)
                return true;
        }
        return false;
    }

    private static int CountConstructors(MethodInfo method, Type type)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadPoolExpansionBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(PoolExpansionScenarios),
            true);
        List<ILInstruction> instructions = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            CodeInstruction instruction =
                instructions[index].GetCodeInstruction();
            ConstructorInfo constructor = instruction.operand as ConstructorInfo;
            if (instruction.opcode == OpCodes.Newobj &&
                constructor != null && constructor.DeclaringType == type)
                count++;
        }
        return count;
    }
}
