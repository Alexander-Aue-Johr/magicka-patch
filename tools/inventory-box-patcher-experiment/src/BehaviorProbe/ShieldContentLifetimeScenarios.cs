using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;

internal static class ShieldContentLifetimeScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        ShieldContentLifetimeHarness harness =
            new ShieldContentLifetimeHarness(
                magicka,
                runtimePatchEnabled);
        report.Add("shield.global_content_owner", harness.ContentOwner());
        report.Add("shield.graphics_load_shape", harness.LoadShape());
    }
}

internal sealed class ShieldContentLifetimeHarness
{
    private const string PatchTypeName =
        "Magicka.CommunityPatch.Runtime.ShieldContentLifetimePatch, " +
        "Magicka.CommunityPatch.Runtime";

    private readonly bool runtimePatchEnabled;
    private readonly ConstructorInfo constructor;
    private readonly MethodInfo playStateContent;
    private readonly MethodInfo gameInstance;
    private readonly MethodInfo gameContent;
    private readonly Type shieldEffect;
    private readonly Type texture2D;

    internal ShieldContentLifetimeHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        this.runtimePatchEnabled = runtimePatchEnabled;
        Type shield = magicka.GetType(
            "Magicka.GameLogic.Entities.Shield",
            true);
        Type playState = magicka.GetType(
            "Magicka.GameLogic.GameStates.PlayState",
            true);
        Type game = magicka.GetType("Magicka.Game", true);
        shieldEffect = magicka.GetType(
            "Magicka.Graphics.Effects.ShieldEffect",
            true);
        texture2D = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Graphics.Texture2D");

        constructor = shield.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
            null,
            new Type[] { playState },
            null);
        playStateContent = RequireGetter(playState, "Content", false);
        gameInstance = RequireGetter(game, "Instance", true);
        gameContent = RequireGetter(game, "Content", false);
        if (constructor == null)
            throw new MissingMethodException(shield.FullName, ".ctor");
        if (playStateContent.ReturnType != gameContent.ReturnType)
            throw new InvalidOperationException(
                "Shield content-manager property types do not match.");
    }

    internal ScenarioResult ContentOwner()
    {
        List<CodeInstruction> instructions = Body();
        int levelReads = CountPair(
            instructions,
            OpCodes.Ldarg_1,
            null,
            OpCodes.Callvirt,
            playStateContent);
        int globalReads = CountPair(
            instructions,
            OpCodes.Call,
            gameInstance,
            OpCodes.Callvirt,
            gameContent);
        string actual = "level_reads:" + levelReads +
            ",global_reads:" + globalReads;
        const string expected = "level_reads:0,global_reads:2";
        return new ScenarioResult(
            actual == expected,
            actual,
            expected);
    }

    internal ScenarioResult LoadShape()
    {
        List<CodeInstruction> instructions = Body();
        int effects = 0;
        int textures = 0;
        for (int index = 0; index < instructions.Count; index++)
        {
            ConstructorInfo created =
                instructions[index].operand as ConstructorInfo;
            if (instructions[index].opcode == OpCodes.Newobj &&
                created != null &&
                created.DeclaringType == shieldEffect)
                effects++;

            MethodInfo method = instructions[index].operand as MethodInfo;
            if ((instructions[index].opcode == OpCodes.Call ||
                instructions[index].opcode == OpCodes.Callvirt) &&
                method != null && method.Name == "Load" &&
                method.IsGenericMethod &&
                method.GetGenericArguments().Length == 1 &&
                method.GetGenericArguments()[0] == texture2D)
                textures++;
        }
        string actual = "effects:" + effects + ",textures:" + textures;
        const string expected = "effects:1,textures:1";
        return new ScenarioResult(
            actual == expected,
            actual,
            expected);
    }

    private List<CodeInstruction> Body()
    {
        List<CodeInstruction> instructions = Decode(constructor);
        if (!runtimePatchEnabled)
            return instructions;
        Type patch = Type.GetType(PatchTypeName, false);
        if (patch == null)
            return instructions;
        MethodInfo transpiler = patch.GetMethod(
            "Transpiler",
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patch.FullName, "Transpiler");
        object transformed = transpiler.Invoke(
            null,
            new object[] { instructions });
        return new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
    }

    private static int CountPair(
        List<CodeInstruction> instructions,
        OpCode firstOpcode,
        MemberInfo firstMember,
        OpCode secondOpcode,
        MemberInfo secondMember)
    {
        int count = 0;
        for (int index = 1; index < instructions.Count; index++)
        {
            if (instructions[index - 1].opcode == firstOpcode &&
                SameMember(instructions[index - 1].operand, firstMember) &&
                instructions[index].opcode == secondOpcode &&
                SameMember(instructions[index].operand, secondMember))
                count++;
        }
        return count;
    }

    private static bool SameMember(object operand, MemberInfo expected)
    {
        if (expected == null)
            return operand == null;
        MemberInfo actual = operand as MemberInfo;
        return actual != null && actual.Module == expected.Module &&
            actual.MetadataToken == expected.MetadataToken;
    }

    private static MethodInfo RequireGetter(
        Type type,
        string name,
        bool isStatic)
    {
        PropertyInfo property = type.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.NonPublic |
                (isStatic ? BindingFlags.Static : BindingFlags.Instance));
        MethodInfo getter = property == null
            ? null
            : property.GetGetMethod(true);
        if (getter == null || getter.IsStatic != isStatic)
            throw new MissingMethodException(type.FullName, "get_" + name);
        return getter;
    }

    private static List<CodeInstruction> Decode(MethodBase method)
    {
        DynamicMethod target = new DynamicMethod(
            "ReadShieldConstructor",
            typeof(void),
            Type.EmptyTypes,
            typeof(ShieldContentLifetimeHarness),
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
}
