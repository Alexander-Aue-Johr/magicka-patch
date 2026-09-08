using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class InGameMenuMagicksLanguagePatch
    {
        private static FieldInfo descriptionsField;
        private static FieldInfo markedItemField;
        private static FieldInfo descriptionField;
        private static MethodInfo setTextMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Magicks menu language selection guard",
                "org.magickacommunitypatch.magicks-language-selection-guard",
                FindLanguageChanged,
                typeof(InGameMenuMagicksLanguagePatch).GetMethod("Transpiler"));

        private static MethodInfo FindLanguageChanged(Assembly targetAssembly)
        {
            Type menuType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuMagicks",
                true);
            Type textType = RuntimeMember.FindLoadedType("PolygonHead.Text");
            descriptionsField = RequireField(
                menuType,
                "mDescriptions",
                typeof(string[]));
            markedItemField = RequireField(
                menuType,
                "mMarkedItem",
                typeof(int));
            descriptionField = RequireField(
                menuType,
                "mDescription",
                textType);
            setTextMethod = textType.GetMethod(
                "SetText",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { typeof(string) },
                null);
            if (setTextMethod == null || setTextMethod.ReturnType != typeof(void))
                throw new MissingMethodException(textType.FullName, "SetText");

            MethodInfo method = menuType.GetMethod(
                "LanguageChanged",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(menuType.FullName, "LanguageChanged");
            return method;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int start = -1;
            int matches = 0;
            for (int index = 0; index + 7 < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldarg_0 ||
                    result[index + 1].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index + 1].operand, descriptionField) ||
                    result[index + 2].opcode != OpCodes.Ldarg_0 ||
                    result[index + 3].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index + 3].operand, descriptionsField) ||
                    result[index + 4].opcode != OpCodes.Ldarg_0 ||
                    result[index + 5].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index + 5].operand, markedItemField) ||
                    result[index + 6].opcode != OpCodes.Ldelem_Ref ||
                    result[index + 7].opcode != OpCodes.Callvirt ||
                    !Object.Equals(result[index + 7].operand, setTextMethod))
                    continue;

                start = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one unguarded Magicks description selection, found " +
                    matches + ".");
            for (int index = start + 1; index < start + 8; index++)
            {
                if (result[index].labels.Count != 0 ||
                    result[index].blocks.Count != 0)
                    throw new InvalidOperationException(
                        "Magicks description selection contains an internal branch " +
                        "or exception boundary.");
            }

            CodeInstruction loadMenu = new CodeInstruction(OpCodes.Ldarg_0);
            loadMenu.labels.AddRange(result[start].labels);
            loadMenu.blocks.AddRange(result[start].blocks);
            result.RemoveRange(start, 8);
            result.Insert(
                start,
                new CodeInstruction(
                    OpCodes.Call,
                    typeof(InGameMenuMagicksLanguagePatch).GetMethod(
                        "UpdateDescription")));
            result.Insert(start, loadMenu);
            return result;
        }

        public static void UpdateDescription(object menu)
        {
            string[] descriptions = (string[])descriptionsField.GetValue(menu);
            int markedItem = (int)markedItemField.GetValue(menu);
            string text = markedItem >= 0 && markedItem < descriptions.Length
                ? descriptions[markedItem]
                : "";
            object description = descriptionField.GetValue(menu);
            setTextMethod.Invoke(description, new object[] { text });
        }
    }
}
