using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.InventoryBoxRuntimePatch
{
    public static class InventoryBoxDrawTranspiler
    {
        internal static readonly string ExpectedCSharpDiff =
            CSharpPatchDiff.NormalizeTextBlock(
                @"
                 Point screenSize = RenderManager.Instance.ScreenSize;
                +mTextBoxEffect.ScreenSize = new Vector2(screenSize.X, screenSize.Y);
                 mPosition.X = (float)screenSize.X * 0.5f;
                 mPosition.Y = (float)screenSize.Y * 0.5f;");

        internal static readonly RuntimePatchDefinition Definition =
            new RuntimePatchDefinition(
                "InventoryBox screen size",
                "org.magickacommunitypatch.inventory-box-screen-size-experiment",
                ExpectedCSharpDiff,
                TargetMethod.FindIn,
                typeof(InventoryBoxDrawTranspiler).GetMethod(
                    "Apply",
                    BindingFlags.Static | BindingFlags.Public));

        public static IEnumerable<CodeInstruction> Apply(
            IEnumerable<CodeInstruction> source,
            MethodBase original)
        {
            List<CodeInstruction> instructions = new List<CodeInstruction>(source);
            List<CodeInstruction> originalInstructions = new List<CodeInstruction>(instructions);
            PatchAnchor anchor = PatchAnchor.Find(instructions);
            RuntimeMembers members = RuntimeMembers.Find(original, instructions, anchor);
            List<CodeInstruction> assignment = CreateScreenSizeAssignment(members);

            AssertUnpatched(instructions, members.ScreenSizeSetter);
            instructions.InsertRange(anchor.ScreenSizeStoreIndex + 1, assignment);
            string csharpDiff = CSharpPatchDiff.Create(
                originalInstructions,
                instructions,
                anchor,
                members);
            PatchObservation.Record(
                originalInstructions.Count,
                instructions.Count,
                assignment,
                csharpDiff);
            return instructions;
        }

        private static List<CodeInstruction> CreateScreenSizeAssignment(RuntimeMembers members)
        {
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, members.TextBoxEffectField),
                members.CreateScreenSizeLoad(),
                new CodeInstruction(OpCodes.Ldfld, members.PointXField),
                new CodeInstruction(OpCodes.Conv_R4),
                members.CreateScreenSizeLoad(),
                new CodeInstruction(OpCodes.Ldfld, members.PointYField),
                new CodeInstruction(OpCodes.Conv_R4),
                new CodeInstruction(OpCodes.Newobj, members.VectorConstructor),
                new CodeInstruction(OpCodes.Callvirt, members.ScreenSizeSetter)
            };
        }

        private static void AssertUnpatched(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo screenSizeSetter)
        {
            int setterCalls = CountCalls(instructions, screenSizeSetter);
            if (setterCalls != 0)
                throw new InvalidOperationException(
                    "Expected an unpatched Draw method, found " + setterCalls + " ScreenSize calls.");
        }

        private static int CountCalls(
            IEnumerable<CodeInstruction> instructions,
            MethodInfo expectedMethod)
        {
            return instructions.Count(instruction =>
                instruction.operand is MethodInfo &&
                (MethodInfo)instruction.operand == expectedMethod);
        }
    }

    internal sealed class PatchAnchor
    {
        internal readonly int ScreenSizeGetterIndex;
        internal readonly int ScreenSizeStoreIndex;
        internal readonly MethodInfo ScreenSizeGetter;

        private PatchAnchor(int getterIndex, MethodInfo getter)
        {
            ScreenSizeGetterIndex = getterIndex;
            ScreenSizeStoreIndex = getterIndex + 1;
            ScreenSizeGetter = getter;
        }

        internal static PatchAnchor Find(IList<CodeInstruction> instructions)
        {
            List<PatchAnchor> matches = new List<PatchAnchor>();
            for (int index = 0; index + 1 < instructions.Count; index++)
            {
                MethodInfo getter = instructions[index].operand as MethodInfo;
                if (getter == null ||
                    getter.Name != "get_ScreenSize" ||
                    getter.DeclaringType.FullName != "PolygonHead.RenderManager" ||
                    !StoresLocal(instructions[index + 1].opcode))
                    continue;

                matches.Add(new PatchAnchor(index, getter));
            }

            if (matches.Count != 1)
                throw new InvalidOperationException(
                    "Expected one RenderManager.ScreenSize local-store anchor, found " + matches.Count + ".");

            return matches[0];
        }

        private static bool StoresLocal(OpCode opcode)
        {
            return opcode == OpCodes.Stloc ||
                opcode == OpCodes.Stloc_S ||
                opcode == OpCodes.Stloc_0 ||
                opcode == OpCodes.Stloc_1 ||
                opcode == OpCodes.Stloc_2 ||
                opcode == OpCodes.Stloc_3;
        }
    }

    internal sealed class RuntimeMembers
    {
        private readonly int screenSizeLocalIndex;
        private readonly object screenSizeLocalOperand;
        internal readonly FieldInfo TextBoxEffectField;
        internal readonly FieldInfo PointXField;
        internal readonly FieldInfo PointYField;
        internal readonly ConstructorInfo VectorConstructor;
        internal readonly MethodInfo ScreenSizeSetter;

        private RuntimeMembers(
            CodeInstruction screenSizeLoad,
            FieldInfo textBoxEffectField,
            FieldInfo pointXField,
            FieldInfo pointYField,
            ConstructorInfo vectorConstructor,
            MethodInfo screenSizeSetter)
        {
            screenSizeLocalIndex = LocalInstruction.GetLoadedLocalIndex(screenSizeLoad);
            screenSizeLocalOperand = screenSizeLoad.operand ?? (object)(byte)screenSizeLocalIndex;
            TextBoxEffectField = textBoxEffectField;
            PointXField = pointXField;
            PointYField = pointYField;
            VectorConstructor = vectorConstructor;
            ScreenSizeSetter = screenSizeSetter;
        }

        internal static RuntimeMembers Find(
            MethodBase original,
            IList<CodeInstruction> instructions,
            PatchAnchor anchor)
        {
            Type pointType = anchor.ScreenSizeGetter.ReturnType;
            FieldInfo textBoxEffect = FindField(original.DeclaringType, "mTextBoxEffect");
            MethodInfo setter = FindScreenSizeSetter(textBoxEffect.FieldType);
            Type vectorType = setter.GetParameters()[0].ParameterType;

            return new RuntimeMembers(
                FindScreenSizeLoad(instructions, anchor.ScreenSizeStoreIndex),
                textBoxEffect,
                FindField(pointType, "X"),
                FindField(pointType, "Y"),
                FindVectorConstructor(vectorType),
                setter);
        }

        internal CodeInstruction CreateScreenSizeLoad()
        {
            return screenSizeLocalOperand is LocalBuilder
                ? new CodeInstruction(OpCodes.Ldloca, screenSizeLocalOperand)
                : new CodeInstruction(OpCodes.Ldloca_S, screenSizeLocalOperand);
        }

        internal int ScreenSizeLocalIndex
        {
            get { return screenSizeLocalIndex; }
        }

        private static CodeInstruction FindScreenSizeLoad(
            IList<CodeInstruction> instructions,
            int storeIndex)
        {
            int storedLocalIndex = LocalInstruction.GetStoredLocalIndex(instructions[storeIndex]);
            CodeInstruction[] matches = instructions
                .Skip(storeIndex + 1)
                .Take(24)
                .Where(instruction =>
                    LocalInstruction.GetLoadedLocalIndex(instruction) == storedLocalIndex)
                .GroupBy(instruction => LocalInstruction.GetLoadedLocalIndex(instruction))
                .Select(group => group.First())
                .ToArray();

            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one Point-local load near the ScreenSize store, found " + matches.Length + ".");

            return matches[0];
        }

        private static FieldInfo FindField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static MethodInfo FindScreenSizeSetter(Type effectType)
        {
            PropertyInfo property = effectType.GetProperty(
                "ScreenSize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property == null ? null : property.GetSetMethod(true);
            if (setter == null)
                throw new MissingMethodException(effectType.FullName, "set_ScreenSize");
            return setter;
        }

        private static ConstructorInfo FindVectorConstructor(Type vectorType)
        {
            ConstructorInfo constructor = vectorType.GetConstructor(new Type[] { typeof(float), typeof(float) });
            if (constructor == null)
                throw new MissingMethodException(vectorType.FullName, ".ctor(System.Single, System.Single)");
            return constructor;
        }
    }

    internal static class CSharpPatchDiff
    {
        internal static string NormalizeTextBlock(string value)
        {
            string[] lines = NormalizeLineEndings(value).Split('\n');
            int firstLine = lines.Length > 0 && lines[0].Length == 0 ? 1 : 0;
            int indentation = CommonIndentation(lines, firstLine);
            List<string> normalized = new List<string>();
            for (int index = firstLine; index < lines.Length; index++)
                normalized.Add(lines[index].Substring(Math.Min(indentation, lines[index].Length)));
            return String.Join("\n", normalized.ToArray());
        }

        internal static string Create(
            IList<CodeInstruction> originalInstructions,
            IList<CodeInstruction> patchedInstructions,
            PatchAnchor anchor,
            RuntimeMembers members)
        {
            string originalCSharp = InventoryBoxCSharpContextDecompiler.Decompile(
                originalInstructions,
                anchor,
                members);
            string patchedCSharp = InventoryBoxCSharpContextDecompiler.Decompile(
                patchedInstructions,
                anchor,
                members);
            return LineDiff.Create(originalCSharp, patchedCSharp);
        }

        internal static void AssertEqual(string expected, string actual, string failure)
        {
            expected = NormalizeLineEndings(expected);
            actual = NormalizeLineEndings(actual);
            if (!String.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(failure + "\n" + actual);
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static int CommonIndentation(string[] lines, int firstLine)
        {
            int indentation = Int32.MaxValue;
            for (int index = firstLine; index < lines.Length; index++)
            {
                if (lines[index].Length == 0)
                    continue;
                int current = 0;
                while (current < lines[index].Length && lines[index][current] == ' ')
                    current++;
                indentation = Math.Min(indentation, current);
            }
            return indentation == Int32.MaxValue ? 0 : indentation;
        }
    }

    internal static class InventoryBoxCSharpContextDecompiler
    {
        private const int ScreenSizeAssignmentLength = 10;
        private const int PositionAssignmentLength = 8;

        internal static string Decompile(
            IList<CodeInstruction> instructions,
            PatchAnchor anchor,
            RuntimeMembers members)
        {
            List<string> lines = new List<string>();
            lines.Add(DecompileScreenSizeDeclaration(instructions, anchor, members));

            int cursor = anchor.ScreenSizeStoreIndex + 1;
            if (IsScreenSizeAssignment(instructions, cursor, members))
            {
                lines.Add("mTextBoxEffect.ScreenSize = new Vector2(screenSize.X, screenSize.Y);");
                cursor += ScreenSizeAssignmentLength;
            }

            lines.Add(DecompilePositionAssignment(instructions, cursor, members, "X"));
            cursor += PositionAssignmentLength;
            lines.Add(DecompilePositionAssignment(instructions, cursor, members, "Y"));
            return String.Join("\n", lines.ToArray());
        }

        private static string DecompileScreenSizeDeclaration(
            IList<CodeInstruction> instructions,
            PatchAnchor anchor,
            RuntimeMembers members)
        {
            int getterIndex = anchor.ScreenSizeGetterIndex;
            bool matches = getterIndex > 0 &&
                IsMethod(instructions[getterIndex - 1], "PolygonHead.RenderManager", "get_Instance") &&
                Object.Equals(instructions[getterIndex].operand, anchor.ScreenSizeGetter) &&
                LocalInstruction.GetStoredLocalIndex(instructions[anchor.ScreenSizeStoreIndex]) ==
                    members.ScreenSizeLocalIndex;
            if (!matches)
                throw new InvalidOperationException("The ScreenSize declaration cannot be decompiled.");
            return "Point screenSize = RenderManager.Instance.ScreenSize;";
        }

        private static bool IsScreenSizeAssignment(
            IList<CodeInstruction> instructions,
            int start,
            RuntimeMembers members)
        {
            return HasRange(instructions, start, ScreenSizeAssignmentLength) &&
                instructions[start].opcode == OpCodes.Ldarg_0 &&
                IsField(instructions[start + 1], members.TextBoxEffectField) &&
                LocalInstruction.GetLoadedLocalIndex(instructions[start + 2]) == members.ScreenSizeLocalIndex &&
                IsField(instructions[start + 3], members.PointXField) &&
                instructions[start + 4].opcode == OpCodes.Conv_R4 &&
                LocalInstruction.GetLoadedLocalIndex(instructions[start + 5]) == members.ScreenSizeLocalIndex &&
                IsField(instructions[start + 6], members.PointYField) &&
                instructions[start + 7].opcode == OpCodes.Conv_R4 &&
                IsMember(instructions[start + 8], members.VectorConstructor) &&
                IsMember(instructions[start + 9], members.ScreenSizeSetter);
        }

        private static string DecompilePositionAssignment(
            IList<CodeInstruction> instructions,
            int start,
            RuntimeMembers members,
            string component)
        {
            bool matches = HasRange(instructions, start, PositionAssignmentLength) &&
                instructions[start].opcode == OpCodes.Ldarg_0 &&
                IsField(instructions[start + 1], "Magicka.GameLogic.UI.InventoryBox+RenderData", "mPosition") &&
                LocalInstruction.GetLoadedLocalIndex(instructions[start + 2]) == members.ScreenSizeLocalIndex &&
                IsField(instructions[start + 3], "Microsoft.Xna.Framework.Point", component) &&
                instructions[start + 4].opcode == OpCodes.Conv_R4 &&
                instructions[start + 5].opcode == OpCodes.Ldc_R4 &&
                instructions[start + 5].operand is Single &&
                (Single)instructions[start + 5].operand == 0.5f &&
                instructions[start + 6].opcode == OpCodes.Mul &&
                IsField(instructions[start + 7], "Microsoft.Xna.Framework.Vector2", component);
            if (!matches)
                throw new InvalidOperationException(
                    "The mPosition." + component + " assignment cannot be decompiled.");
            return "mPosition." + component + " = (float)screenSize." + component + " * 0.5f;";
        }

        private static bool HasRange(IList<CodeInstruction> instructions, int start, int length)
        {
            return start >= 0 && start + length <= instructions.Count;
        }

        private static bool IsMethod(CodeInstruction instruction, string typeName, string methodName)
        {
            MethodInfo method = instruction.operand as MethodInfo;
            return method != null &&
                method.DeclaringType.FullName == typeName &&
                method.Name == methodName;
        }

        private static bool IsField(CodeInstruction instruction, FieldInfo expected)
        {
            return IsMember(instruction, expected);
        }

        private static bool IsField(CodeInstruction instruction, string typeName, string fieldName)
        {
            FieldInfo field = instruction.operand as FieldInfo;
            return field != null &&
                field.DeclaringType.FullName == typeName &&
                field.Name == fieldName;
        }

        private static bool IsMember(CodeInstruction instruction, MemberInfo expected)
        {
            return Object.Equals(instruction.operand, expected);
        }
    }

    internal static class LineDiff
    {
        internal static string Create(string before, string after)
        {
            string[] beforeLines = before.Split('\n');
            string[] afterLines = after.Split('\n');
            int commonStart = CommonStart(beforeLines, afterLines);
            int commonEnd = CommonEnd(beforeLines, afterLines, commonStart);
            List<string> diff = new List<string>();

            AddLines(diff, beforeLines, 0, commonStart, " ");
            AddLines(diff, beforeLines, commonStart, beforeLines.Length - commonEnd, "-");
            AddLines(diff, afterLines, commonStart, afterLines.Length - commonEnd, "+");
            AddLines(diff, beforeLines, beforeLines.Length - commonEnd, beforeLines.Length, " ");
            return String.Join("\n", diff.ToArray());
        }

        private static int CommonStart(string[] before, string[] after)
        {
            int count = 0;
            while (count < before.Length &&
                count < after.Length &&
                String.Equals(before[count], after[count], StringComparison.Ordinal))
                count++;
            return count;
        }

        private static int CommonEnd(string[] before, string[] after, int commonStart)
        {
            int count = 0;
            while (count < before.Length - commonStart &&
                count < after.Length - commonStart &&
                String.Equals(
                    before[before.Length - count - 1],
                    after[after.Length - count - 1],
                    StringComparison.Ordinal))
                count++;
            return count;
        }

        private static void AddLines(
            ICollection<string> destination,
            string[] source,
            int start,
            int end,
            string prefix)
        {
            for (int index = start; index < end; index++)
                destination.Add(prefix + source[index]);
        }
    }

    internal static class LocalInstruction
    {
        internal static int GetStoredLocalIndex(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Stloc_0)
                return 0;
            if (instruction.opcode == OpCodes.Stloc_1)
                return 1;
            if (instruction.opcode == OpCodes.Stloc_2)
                return 2;
            if (instruction.opcode == OpCodes.Stloc_3)
                return 3;
            if (instruction.opcode == OpCodes.Stloc || instruction.opcode == OpCodes.Stloc_S)
                return OperandLocalIndex(instruction.operand);
            return -1;
        }

        internal static int GetLoadedLocalIndex(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0)
                return 0;
            if (instruction.opcode == OpCodes.Ldloc_1)
                return 1;
            if (instruction.opcode == OpCodes.Ldloc_2)
                return 2;
            if (instruction.opcode == OpCodes.Ldloc_3)
                return 3;
            if (instruction.opcode == OpCodes.Ldloc ||
                instruction.opcode == OpCodes.Ldloc_S ||
                instruction.opcode == OpCodes.Ldloca ||
                instruction.opcode == OpCodes.Ldloca_S)
                return OperandLocalIndex(instruction.operand);
            return -1;
        }

        private static int OperandLocalIndex(object operand)
        {
            LocalBuilder localBuilder = operand as LocalBuilder;
            if (localBuilder != null)
                return localBuilder.LocalIndex;

            LocalVariableInfo localInfo = operand as LocalVariableInfo;
            if (localInfo != null)
                return localInfo.LocalIndex;

            if (operand is byte)
                return (byte)operand;
            if (operand is short)
                return (short)operand;
            if (operand is int)
                return (int)operand;
            return -1;
        }
    }
}
