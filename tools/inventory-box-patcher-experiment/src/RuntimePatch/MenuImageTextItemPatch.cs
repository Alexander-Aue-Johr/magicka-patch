using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class MenuImageTextItemPatch
    {
        private static FieldInfo textIdField;
        private static FieldInfo titleField;
        private static FieldInfo fontField;
        private static FieldInfo lineHeightField;
        private static MethodInfo lineHeightGetter;
        private static MethodInfo markAsDirty;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "MenuImageTextItem language font refresh",
                "org.magickacommunitypatch.menu-image-text-language",
                FindTarget,
                typeof(MenuImageTextItemPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type itemType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.MenuImageTextItem",
                true);
            textIdField = RequireField(itemType, "mText");
            titleField = RequireField(itemType, "mTitle");
            fontField = RequireField(itemType, "mFont");
            lineHeightField = RequireField(itemType, "mLineHeight");

            PropertyInfo lineHeight = fontField.FieldType.GetProperty(
                "LineHeight",
                BindingFlags.Instance | BindingFlags.Public);
            lineHeightGetter = lineHeight == null ? null : lineHeight.GetGetMethod();
            markAsDirty = titleField.FieldType.GetMethod(
                "MarkAsDirty",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (textIdField.FieldType != typeof(int) ||
                lineHeightField.FieldType != typeof(float) ||
                lineHeightGetter == null ||
                lineHeightGetter.ReturnType != typeof(int) ||
                markAsDirty == null ||
                markAsDirty.ReturnType != typeof(void))
                throw new MissingMemberException(
                    "MenuImageTextItem language members have an unexpected shape.");

            MethodInfo method = itemType.GetMethod(
                "LanguageChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(itemType.FullName, "LanguageChanged");
            return method;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int condition = FindLiteralReturn(result);
            if (ContainsFieldStore(result, lineHeightField))
                throw new InvalidOperationException(
                    "MenuImageTextItem.LanguageChanged already refreshes mLineHeight.");

            CodeInstruction originalFirst = result[0];
            CodeInstruction prefixStart = new CodeInstruction(OpCodes.Ldarg_0);
            prefixStart.labels.AddRange(originalFirst.labels);
            prefixStart.blocks.AddRange(originalFirst.blocks);
            originalFirst.labels.Clear();
            originalFirst.blocks.Clear();
            result.InsertRange(
                0,
                new CodeInstruction[]
                {
                    prefixStart,
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, fontField),
                    new CodeInstruction(OpCodes.Callvirt, lineHeightGetter),
                    new CodeInstruction(OpCodes.Conv_R4),
                    new CodeInstruction(OpCodes.Stfld, lineHeightField)
                });

            int literalReturn = condition + 6 + 3;
            CodeInstruction originalReturn = result[literalReturn];
            CodeInstruction dirtyStart = new CodeInstruction(OpCodes.Ldarg_0);
            dirtyStart.labels.AddRange(originalReturn.labels);
            dirtyStart.blocks.AddRange(originalReturn.blocks);
            result.RemoveAt(literalReturn);
            result.InsertRange(
                literalReturn,
                new CodeInstruction[]
                {
                    dirtyStart,
                    new CodeInstruction(OpCodes.Ldfld, titleField),
                    new CodeInstruction(OpCodes.Callvirt, markAsDirty),
                    new CodeInstruction(OpCodes.Ret)
                });
            return result;
        }

        private static int FindLiteralReturn(List<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 0; index + 3 < instructions.Count; index++)
            {
                if (instructions[index].opcode != OpCodes.Ldarg_0 ||
                    instructions[index + 1].opcode != OpCodes.Ldfld ||
                    !Object.Equals(instructions[index + 1].operand, textIdField) ||
                    !IsBranchWhenTrue(instructions[index + 2].opcode) ||
                    instructions[index + 3].opcode != OpCodes.Ret)
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple MenuImageTextItem literal returns matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "MenuImageTextItem literal return was not found.");
            return match;
        }

        private static bool ContainsFieldStore(
            List<CodeInstruction> instructions,
            FieldInfo field)
        {
            for (int index = 0; index < instructions.Count; index++)
            {
                if (instructions[index].opcode == OpCodes.Stfld &&
                    Object.Equals(instructions[index].operand, field))
                    return true;
            }
            return false;
        }

        private static bool IsBranchWhenTrue(OpCode opcode)
        {
            return opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S;
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
