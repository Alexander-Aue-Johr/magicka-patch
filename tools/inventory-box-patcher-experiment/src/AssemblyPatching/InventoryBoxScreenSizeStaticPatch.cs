using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InventoryBoxPatcherExperiment;

public static class InventoryBoxScreenSizeStaticPatch
{
    private const string PointType = "Microsoft.Xna.Framework.Point";
    private const string VectorType = "Microsoft.Xna.Framework.Vector2";
    private const string EffectType = "Magicka.Graphics.Effects.TextBoxEffect";
    private const string RenderManagerType = "PolygonHead.RenderManager";

    public static void Apply(ModuleDefinition module)
    {
        MethodDefinition drawMethod = PatchTarget.FindDrawMethod(module);
        PatchMembers members = PatchMembers.Find(drawMethod);
        Instruction screenSizeStored = FindScreenSizeStore(drawMethod, members.ScreenSizeLocal);

        AssertUnpatched(drawMethod, members.ScreenSizeSetter);
        InsertScreenSizeAssignment(drawMethod, screenSizeStored, members);
    }

    private static void InsertScreenSizeAssignment(
        MethodDefinition drawMethod,
        Instruction screenSizeStored,
        PatchMembers members)
    {
        Instruction[] assignment =
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, members.TextBoxEffectField),
            Instruction.Create(OpCodes.Ldloca, members.ScreenSizeLocal),
            Instruction.Create(OpCodes.Ldfld, members.PointXField),
            Instruction.Create(OpCodes.Conv_R4),
            Instruction.Create(OpCodes.Ldloca, members.ScreenSizeLocal),
            Instruction.Create(OpCodes.Ldfld, members.PointYField),
            Instruction.Create(OpCodes.Conv_R4),
            Instruction.Create(OpCodes.Newobj, members.VectorConstructor),
            Instruction.Create(OpCodes.Callvirt, members.ScreenSizeSetter)
        };

        ILProcessor processor = drawMethod.Body.GetILProcessor();
        Instruction insertionPoint = screenSizeStored;
        foreach (Instruction instruction in assignment)
        {
            processor.InsertAfter(insertionPoint, instruction);
            insertionPoint = instruction;
        }
    }

    private static Instruction FindScreenSizeStore(
        MethodDefinition drawMethod,
        VariableDefinition screenSizeLocal)
    {
        List<Instruction> instructions = drawMethod.Body.Instructions.ToList();
        Instruction[] matches = instructions
            .Where((instruction, index) =>
                IsScreenSizeGetter(instruction) &&
                index + 1 < instructions.Count &&
                StoresLocal(instructions[index + 1], screenSizeLocal))
            .Select(instruction => instructions[instructions.IndexOf(instruction) + 1])
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one RenderManager.ScreenSize store in {drawMethod.FullName}, found {matches.Length}.");
    }

    private static bool IsScreenSizeGetter(Instruction instruction)
    {
        return instruction.OpCode.Code is Code.Call or Code.Callvirt &&
            instruction.Operand is MethodReference method &&
            method.Name == "get_ScreenSize" &&
            method.DeclaringType.FullName == RenderManagerType;
    }

    private static bool StoresLocal(Instruction instruction, VariableDefinition variable)
    {
        return instruction.OpCode.Code switch
        {
            Code.Stloc_0 => variable.Index == 0,
            Code.Stloc_1 => variable.Index == 1,
            Code.Stloc_2 => variable.Index == 2,
            Code.Stloc_3 => variable.Index == 3,
            Code.Stloc or Code.Stloc_S => ReferenceEquals(instruction.Operand, variable),
            _ => false
        };
    }

    private static void AssertUnpatched(MethodDefinition drawMethod, MethodReference setter)
    {
        int setterCalls = CountCalls(drawMethod, setter);
        if (setterCalls != 0)
            throw new InvalidOperationException(
                $"Expected an unpatched Draw method, found {setterCalls} TextBoxEffect.ScreenSize calls.");
    }

    private static int CountCalls(MethodDefinition method, MethodReference expected)
    {
        return method.Body.Instructions.Count(instruction =>
            instruction.Operand is MethodReference called &&
            called.FullName == expected.FullName);
    }

    private sealed record PatchMembers(
        VariableDefinition ScreenSizeLocal,
        FieldReference TextBoxEffectField,
        FieldReference PointXField,
        FieldReference PointYField,
        MethodReference VectorConstructor,
        MethodReference ScreenSizeSetter)
    {
        public static PatchMembers Find(MethodDefinition drawMethod)
        {
            TypeDefinition renderData = drawMethod.DeclaringType;
            TypeDefinition inventoryBox = renderData.DeclaringType;

            return new PatchMembers(
                FindOne(drawMethod.Body.Variables, local => local.VariableType.FullName == PointType, "Point local"),
                FindOne(renderData.Fields, field =>
                    field.Name == "mTextBoxEffect" && field.FieldType.FullName == EffectType,
                    "mTextBoxEffect field"),
                FindFieldReference(drawMethod, PointType, "X"),
                FindFieldReference(drawMethod, PointType, "Y"),
                FindMethodReference(inventoryBox.Methods, VectorType, ".ctor", method =>
                    method.Parameters.Count == 2 &&
                    method.Parameters.All(parameter => parameter.ParameterType.FullName == "System.Single")),
                FindMethodReference(renderData.Methods, EffectType, "set_ScreenSize", method =>
                    method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == VectorType));
        }

        private static FieldReference FindFieldReference(
            MethodDefinition method,
            string declaringType,
            string name)
        {
            return FindOne(
                method.Body.Instructions
                    .Select(instruction => instruction.Operand)
                    .OfType<FieldReference>()
                    .GroupBy(field => field.FullName)
                    .Select(group => group.First()),
                field => field.DeclaringType.FullName == declaringType && field.Name == name,
                $"{declaringType}.{name} reference");
        }

        private static MethodReference FindMethodReference(
            IEnumerable<MethodDefinition> methods,
            string declaringType,
            string name,
            Func<MethodReference, bool> signatureMatches)
        {
            return FindOne(
                methods
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions)
                    .Select(instruction => instruction.Operand)
                    .OfType<MethodReference>()
                    .GroupBy(method => method.FullName)
                    .Select(group => group.First()),
                method =>
                    method.DeclaringType.FullName == declaringType &&
                    method.Name == name &&
                    signatureMatches(method),
                $"{declaringType}.{name} reference");
        }

        private static T FindOne<T>(IEnumerable<T> candidates, Func<T, bool> predicate, string label)
        {
            T[] matches = candidates.Where(predicate).ToArray();
            return matches.Length == 1
                ? matches[0]
                : throw new InvalidOperationException($"Expected one {label}, found {matches.Length}.");
        }
    }
}
