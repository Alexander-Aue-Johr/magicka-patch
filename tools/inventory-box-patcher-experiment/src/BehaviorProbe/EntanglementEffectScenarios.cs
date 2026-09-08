using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class EntanglementEffectScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        EntanglementEffectHarness harness =
            new EntanglementEffectHarness(
                magicka,
                runtimePatchEnabled);
        report.Add(
            "entanglement.shared_effect",
            harness.SharedEffect());
        report.Add(
            "entanglement.initialize_without_effect_update",
            harness.InitializeWithoutEffectUpdate());
    }
}

internal sealed class EntanglementEffectHarness
{
    private readonly bool runtimePatchEnabled;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo initialize;
    private readonly MethodInfo renderManagerGetEffect;
    private readonly MethodInfo renderManagerRegisterEffect;
    private readonly MethodInfo diffuseMapSetter;

    internal EntanglementEffectHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type entanglement = magicka.GetType(
            "Magicka.GameLogic.Entities.Entanglement",
            true);
        Type effect = magicka.GetType(
            "Magicka.Graphics.Effects.EntangleEffect",
            true);
        constructor = RequireConstructor(entanglement);
        initialize = RequireMethod(entanglement, "Initialize");
        PropertyInfo effectDiffuseMap = effect.GetProperty(
            "DiffuseMap",
            BindingFlags.Instance | BindingFlags.Public);
        diffuseMapSetter = effectDiffuseMap == null
            ? null
            : effectDiffuseMap.GetSetMethod();

        Type renderManager = RuntimeReflection.FindLoadedType(
            "PolygonHead.RenderManager");
        renderManagerGetEffect = renderManager.GetMethod(
            "GetEffect",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new Type[] { typeof(int) },
            null);
        renderManagerRegisterEffect = FindRegisterEffect(
            renderManager,
            effect);
        if (diffuseMapSetter == null || renderManagerGetEffect == null ||
            renderManagerRegisterEffect == null)
            throw new MissingMemberException(
                "Entanglement effect behavior contract is incomplete.");
    }

    internal ScenarioResult SharedEffect()
    {
        List<CodeInstruction> instructions = Decode(constructor);
        if (runtimePatchEnabled)
            ApplyTranspiler("ConstructorTranspiler", instructions);
        bool directRegistry = ContainsCall(
            instructions,
            renderManagerGetEffect) && ContainsCall(
                instructions,
                renderManagerRegisterEffect);
        bool runtimeRegistry = ContainsNamedCall(
            instructions,
            typeof(Magicka.CommunityPatch.Runtime
                .EntanglementEffectPatch),
            "GetOrCreateSharedEffect");
        bool shared = directRegistry || runtimeRegistry;
        return new ScenarioResult(
            shared,
            "shared_registry:" + shared,
            "shared_registry:True");
    }

    internal ScenarioResult InitializeWithoutEffectUpdate()
    {
        List<CodeInstruction> instructions = Decode(initialize);
        if (runtimePatchEnabled)
            ApplyTranspiler("InitializeTranspiler", instructions);
        int setters = CountCalls(instructions, diffuseMapSetter);
        return new ScenarioResult(
            setters == 0,
            "diffuse_updates:" + setters,
            "diffuse_updates:0");
    }

    private static ConstructorInfo RequireConstructor(Type type)
    {
        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (constructors.Length != 1 ||
            constructors[0].GetParameters().Length != 1)
            throw new InvalidOperationException(
                "Expected one Entanglement constructor.");
        return constructors[0];
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        MethodInfo method = type.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        if (method == null || method.ReturnType != typeof(void))
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    private static MethodInfo FindRegisterEffect(
        Type renderManager,
        Type effect)
    {
        MethodInfo[] methods = renderManager.GetMethods(
            BindingFlags.Instance | BindingFlags.Public);
        for (int index = 0; index < methods.Length; index++)
        {
            ParameterInfo[] parameters = methods[index].GetParameters();
            if (methods[index].Name == "RegisterEffect" &&
                parameters.Length == 1 &&
                parameters[0].ParameterType.IsAssignableFrom(effect))
                return methods[index];
        }
        return null;
    }

    private static bool ContainsCall(
        IList<CodeInstruction> instructions,
        MethodInfo method)
    {
        return CountCalls(instructions, method) > 0;
    }

    private static bool ContainsNamedCall(
        IList<CodeInstruction> instructions,
        Type declaringType,
        string name)
    {
        for (int index = 0; index < instructions.Count; index++)
        {
            MethodInfo method = instructions[index].operand as MethodInfo;
            if (method != null &&
                method.DeclaringType == declaringType &&
                method.Name == name)
                return true;
        }
        return false;
    }

    private static int CountCalls(
        IList<CodeInstruction> instructions,
        MethodInfo method)
    {
        int count = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                Object.Equals(instructions[index].operand, method))
                count++;
        }
        return count;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadEntanglementBody",
            typeof(void),
            Type.EmptyTypes,
            typeof(EntanglementEffectHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result =
            new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static void ApplyTranspiler(
        string name,
        List<CodeInstruction> instructions)
    {
        MethodInfo method = typeof(Magicka.CommunityPatch.Runtime
            .EntanglementEffectPatch).GetMethod(name);
        if (method == null)
            throw new MissingMethodException(
                typeof(Magicka.CommunityPatch.Runtime
                    .EntanglementEffectPatch).FullName,
                name);
        object transformed = method.Invoke(
            null,
            new object[] { instructions });
        instructions.Clear();
        instructions.AddRange(
            (IEnumerable<CodeInstruction>)transformed);
    }
}
