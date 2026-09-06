using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using Harmony.ILCopying;
using Magicka.CommunityPatch.Runtime;

internal static class UndeadSummonNetworkScenarios
{
    internal static void Run(
        Assembly magicka,
        bool runtimePatchEnabled,
        BehaviorReport report)
    {
        UndeadSummonNetworkHarness harness = new UndeadSummonNetworkHarness(
            magicka,
            runtimePatchEnabled);
        report.Add("undead_network.host_marker", harness.HostMarker());
        report.Add("undead_network.client_marked", harness.ClientMarked());
        report.Add("undead_network.client_normal", harness.ClientNormal());
        report.Add(
            "undead_network.wire_marker_roundtrip",
            harness.WireMarkerRoundTrip());
    }
}

internal sealed class UndeadSummonNetworkHarness
{
    private enum ClientMarkerMode
    {
        Invalid,
        LegacyFalse,
        Bool2,
        CombinedMarker
    }

    private readonly Type messageType;
    private readonly Type vectorType;
    private readonly FieldInfo actionTypeField;
    private readonly FieldInfo bool2Field;
    private readonly FieldInfo colorField;
    private readonly FieldInfo vectorXField;
    private readonly MethodInfo writeMethod;
    private readonly MethodInfo readMethod;
    private readonly List<CodeInstruction> hostInstructions;
    private readonly List<CodeInstruction> clientInstructions;
    private readonly MethodInfo summonedSingle;
    private readonly MethodInfo summonedWithFlag;
    private readonly MethodInfo markerMethod;
    private readonly float negativeZero;

    internal UndeadSummonNetworkHarness(
        Assembly magicka,
        bool runtimePatchEnabled)
    {
        messageType = magicka.GetType("Magicka.Network.TriggerActionMessage", true);
        vectorType = RuntimeReflection.FindLoadedType(
            "Microsoft.Xna.Framework.Vector3");
        actionTypeField = RequireField(messageType, "ActionType");
        bool2Field = RequireField(messageType, "Bool2");
        colorField = RequireField(messageType, "Color");
        vectorXField = RequireField(vectorType, "X");
        writeMethod = RequireMethod(
            messageType,
            "Write",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { typeof(BinaryWriter) });
        readMethod = RequireMethod(
            messageType,
            "Read",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { typeof(BinaryReader) });

        Type undeadType = magicka.GetType(
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.SummonUndead",
            true);
        MethodInfo host = RequireMethod(
            undeadType,
            "Execute",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            new Type[] { vectorType, vectorType });

        Type triggerType = magicka.GetType("Magicka.Levels.Triggers.Trigger", true);
        Type messageByReference = messageType.MakeByRefType();
        MethodInfo client = RequireMethod(
            triggerType,
            "SpawnNPC",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            new Type[] { messageByReference });

        Type characterType = magicka.GetType(
            "Magicka.GameLogic.Entities.Character",
            true);
        Type npcType = magicka.GetType(
            "Magicka.GameLogic.Entities.NonPlayerCharacter",
            true);
        summonedSingle = RequireMethod(
            npcType,
            "Summoned",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { characterType });
        summonedWithFlag = RequireMethod(
            npcType,
            "Summoned",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            new Type[] { characterType, typeof(bool) });

        negativeZero = BitConverter.ToSingle(new byte[] { 0, 0, 0, 128 }, 0);
        hostInstructions = Decode(host);
        clientInstructions = Decode(client);

        Type patchType = typeof(Bootstrap).Assembly.GetType(
            "Magicka.CommunityPatch.Runtime.UndeadSummonNetworkPatch",
            false);
        markerMethod = patchType == null
            ? null
            : patchType.GetMethod(
                "IsUndeadMarker",
                BindingFlags.Static | BindingFlags.Public);
        if (runtimePatchEnabled && patchType != null)
        {
            ApplyTranspiler(patchType, "HostTranspiler", hostInstructions);
            ApplyTranspiler(patchType, "ClientTranspiler", clientInstructions);
        }
    }

    internal ScenarioResult HostMarker()
    {
        bool inMemory = HasBool2TrueStore(hostInstructions);
        bool wire = HasNegativeZeroColorStore(hostInstructions);
        bool passed = inMemory && wire;
        return new ScenarioResult(
            passed,
            "marked:" + passed +
                ",in_memory:" + inMemory +
                ",wire:" + wire,
            "marked:True");
    }

    internal ScenarioResult ClientMarked()
    {
        ClientMarkerMode mode = FindClientMarkerMode();
        bool result = EvaluateClient(mode, false, negativeZero);
        return new ScenarioResult(
            result,
            "mode:" + mode + ",undead:" + result,
            "undead:True");
    }

    internal ScenarioResult ClientNormal()
    {
        ClientMarkerMode mode = FindClientMarkerMode();
        bool result = EvaluateClient(mode, false, 0f);
        return new ScenarioResult(
            !result,
            "mode:" + mode + ",undead:" + result,
            "undead:False");
    }

    internal ScenarioResult WireMarkerRoundTrip()
    {
        byte[] ordinary = WriteMessage(0f);
        byte[] marked = WriteMessage(negativeZero);
        object roundTrip = Activator.CreateInstance(messageType);
        MemoryStream stream = new MemoryStream(marked);
        BinaryReader reader = new BinaryReader(stream);
        readMethod.Invoke(roundTrip, new object[] { reader });
        object color = colorField.GetValue(roundTrip);
        float value = Convert.ToSingle(vectorXField.GetValue(color));
        bool sameSize = ordinary.Length == marked.Length;
        bool survived = IsNegativeZero(value);
        return new ScenarioResult(
            sameSize && survived,
            "same_size:" + sameSize + ",negative_zero:" + survived,
            "same_size:True,negative_zero:True");
    }

    private byte[] WriteMessage(float marker)
    {
        object message = Activator.CreateInstance(messageType);
        object action = Enum.Parse(actionTypeField.FieldType, "SpawnNPC");
        actionTypeField.SetValue(message, action);
        object color = Activator.CreateInstance(vectorType);
        vectorXField.SetValue(color, marker);
        colorField.SetValue(message, color);

        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);
        writeMethod.Invoke(message, new object[] { writer });
        writer.Flush();
        return stream.ToArray();
    }

    private ClientMarkerMode FindClientMarkerMode()
    {
        int singleCalls = 0;
        int flaggedCalls = 0;
        ClientMarkerMode mode = ClientMarkerMode.Invalid;
        for (int index = 0; index < clientInstructions.Count; index++)
        {
            MethodInfo method = clientInstructions[index].operand as MethodInfo;
            if (!IsCall(clientInstructions[index]) || method == null)
                continue;
            if (method == summonedSingle)
            {
                singleCalls++;
                mode = ClientMarkerMode.LegacyFalse;
                continue;
            }
            if (method != summonedWithFlag)
                continue;
            flaggedCalls++;
            if (index > 0 && IsCall(clientInstructions[index - 1]) &&
                clientInstructions[index - 1].operand as MethodInfo == markerMethod)
            {
                mode = ClientMarkerMode.CombinedMarker;
            }
            else if (index > 0 && clientInstructions[index - 1].opcode == OpCodes.Ldfld &&
                Object.Equals(clientInstructions[index - 1].operand, bool2Field))
            {
                mode = ClientMarkerMode.Bool2;
            }
        }
        return singleCalls + flaggedCalls == 1 ? mode : ClientMarkerMode.Invalid;
    }

    private bool EvaluateClient(
        ClientMarkerMode mode,
        bool inMemoryFlag,
        float wireMarker)
    {
        if (mode == ClientMarkerMode.LegacyFalse)
            return false;
        if (mode == ClientMarkerMode.Bool2)
            return inMemoryFlag;
        if (mode == ClientMarkerMode.CombinedMarker && markerMethod != null)
        {
            return (bool)markerMethod.Invoke(
                null,
                new object[] { inMemoryFlag, wireMarker });
        }
        return false;
    }

    private bool HasBool2TrueStore(List<CodeInstruction> instructions)
    {
        int matches = 0;
        for (int index = 1; index < instructions.Count; index++)
        {
            if (instructions[index].opcode == OpCodes.Stfld &&
                Object.Equals(instructions[index].operand, bool2Field) &&
                LoadsOne(instructions[index - 1].opcode))
                matches++;
        }
        return matches == 1;
    }

    private bool HasNegativeZeroColorStore(List<CodeInstruction> instructions)
    {
        int matches = 0;
        for (int index = 3; index < instructions.Count; index++)
        {
            if (instructions[index].opcode != OpCodes.Stfld ||
                !Object.Equals(instructions[index].operand, vectorXField) ||
                instructions[index - 1].opcode != OpCodes.Ldc_R4 ||
                !IsNegativeZero(Convert.ToSingle(instructions[index - 1].operand)) ||
                instructions[index - 2].opcode != OpCodes.Ldflda ||
                !Object.Equals(instructions[index - 2].operand, colorField))
                continue;
            matches++;
        }
        return matches == 1;
    }

    private static bool IsNegativeZero(float value)
    {
        return value == 0f && Single.IsNegativeInfinity(1f / value);
    }

    private static bool IsCall(CodeInstruction instruction)
    {
        return instruction.opcode == OpCodes.Call ||
            instruction.opcode == OpCodes.Callvirt;
    }

    private static bool LoadsOne(OpCode opcode)
    {
        return opcode == OpCodes.Ldc_I4_1;
    }

    private static List<CodeInstruction> Decode(MethodInfo method)
    {
        DynamicMethod target = new DynamicMethod(
            "UndeadSummonNetworkDecode",
            typeof(void),
            Type.EmptyTypes,
            typeof(UndeadSummonNetworkHarness),
            true);
        List<ILInstruction> decoded = MethodBodyReader.GetInstructions(
            target.GetILGenerator(),
            method);
        List<CodeInstruction> result = new List<CodeInstruction>(decoded.Count);
        for (int index = 0; index < decoded.Count; index++)
            result.Add(decoded[index].GetCodeInstruction());
        return result;
    }

    private static void ApplyTranspiler(
        Type patchType,
        string name,
        List<CodeInstruction> instructions)
    {
        MethodInfo transpiler = patchType.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public);
        if (transpiler == null)
            throw new MissingMethodException(patchType.FullName, name);
        object transformed = transpiler.Invoke(null, new object[] { instructions });
        List<CodeInstruction> result = new List<CodeInstruction>(
            (IEnumerable<CodeInstruction>)transformed);
        instructions.Clear();
        instructions.AddRange(result);
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        FieldInfo field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static MethodInfo RequireMethod(
        Type type,
        string name,
        BindingFlags flags,
        Type[] parameterTypes)
    {
        MethodInfo method = type.GetMethod(name, flags, null, parameterTypes, null);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
}
