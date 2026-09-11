using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class ControllerOptionsMenuPatch
    {
        private const BindingFlags Any = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;
        private static Type optionsType;
        private static FieldInfo itemsField;
        private static FieldInfo selectedField;
        private static MethodInfo addText;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorPostfix(
                "Controller mode options row",
                "org.magickacommunitypatch.controller-options-row",
                FindConstructor,
                typeof(ControllerOptionsMenuPatch).GetMethod("ConstructorPostfix"));
        internal static readonly RuntimePatchDefinition SelectDefinition =
            RuntimePatchDefinition.PrefixAndPostfix(
                "Controller mode options selection",
                "org.magickacommunitypatch.controller-options-select",
                assembly => FindMethod(assembly, "IControllerSelect", 1),
                CreateSelectPrefix,
                CreateStatePostfix);
        internal static readonly RuntimePatchDefinition HighlightDefinition =
            RuntimePatchDefinition.PrefixAndPostfix(
                "Controller mode options highlight",
                "org.magickacommunitypatch.controller-options-highlight",
                assembly => FindMethod(assembly, "IGetHighlightedButtonName", 0),
                CreateHighlightPrefix,
                CreateStatePostfix);

        private static ConstructorInfo FindConstructor(Assembly assembly)
        {
            Initialize(assembly);
            ConstructorInfo constructor = optionsType.GetConstructor(Any,
                null, Type.EmptyTypes, null);
            if (constructor == null) throw new MissingMethodException(
                optionsType.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindMethod(Assembly assembly, string name,
            int parameters)
        {
            Initialize(assembly);
            foreach (MethodInfo method in optionsType.GetMethods(Any |
                BindingFlags.DeclaredOnly))
                if (method.Name == name && method.GetParameters().Length == parameters)
                    return method;
            throw new MissingMethodException(optionsType.FullName, name);
        }

        private static void Initialize(Assembly assembly)
        {
            optionsType = assembly.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptions", true);
            Type menu = optionsType.BaseType;
            itemsField = FindField(menu, "mMenuItems");
            selectedField = FindField(menu, "mSelectedItem");
            foreach (MethodInfo method in menu.GetMethods(Any))
                if (method.Name == "AddMenuTextItem" &&
                    method.GetParameters().Length == 3 &&
                    method.GetParameters()[0].ParameterType == typeof(string))
                    addText = method;
            if (itemsField == null || selectedField == null || addText == null)
                throw new MissingMemberException(optionsType.FullName,
                    "controller option menu contract");
        }

        private static MethodInfo CreateSelectPrefix(MethodInfo target)
        {
            return typeof(ControllerOptionsSelectPatch<,>).MakeGenericType(
                target.DeclaringType, target.GetParameters()[0].ParameterType)
                .GetMethod("Prefix");
        }

        private static MethodInfo CreateHighlightPrefix(MethodInfo target)
        {
            return typeof(ControllerOptionsHighlightPatch<>).MakeGenericType(
                target.DeclaringType).GetMethod("Prefix");
        }

        private static MethodInfo CreateStatePostfix(MethodInfo target)
        {
            if (target.GetParameters().Length == 1)
                return typeof(ControllerOptionsSelectPatch<,>).MakeGenericType(
                    target.DeclaringType, target.GetParameters()[0].ParameterType)
                    .GetMethod("Postfix");
            return typeof(ControllerOptionsHighlightPatch<>).MakeGenericType(
                target.DeclaringType).GetMethod("Postfix");
        }

        public static void AddRow(object menu)
        {
            IList items = (IList)itemsField.GetValue(menu);
            if (items.Count != 4) return;
            object first = items[0];
            PropertyInfo fontProperty = FindProperty(first.GetType(), "Font");
            object font = fontProperty == null ? FindField(first.GetType(), "mFont")
                .GetValue(first) : fontProperty.GetValue(first, null);
            Type align = addText.GetParameters()[2].ParameterType;
            addText.Invoke(menu, new object[] { ModeText(), font,
                Enum.Parse(align, "Center") });
            object row = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            items.Insert(1, row);
        }

        public static void ConstructorPostfix(object __instance)
        { AddRow(__instance); }

        public static bool BeforeSelect(object menu, out int __state)
        {
            __state = (int)selectedField.GetValue(menu);
            if (__state == 1)
            {
                ModernControllerSchemePatch.SetEnabled(
                    !ModernControllerSchemePatch.IsEnabled());
                HybridInputPatch.InvalidateLabels();
                SetRowText(menu);
                return false;
            }
            if (__state > 1) selectedField.SetValue(menu, __state - 1);
            return true;
        }

        public static bool BeforeHighlight(object menu, ref string result,
            out int __state)
        {
            __state = (int)selectedField.GetValue(menu);
            if (__state == 1) { result = "controls"; return false; }
            if (__state > 1) selectedField.SetValue(menu, __state - 1);
            return true;
        }

        public static void Restore(object menu, int state)
        {
            if (state > 1) selectedField.SetValue(menu, state);
        }

        public static int TranslateIndex(int index)
        { return index > 1 ? index - 1 : index; }

        private static void SetRowText(object menu)
        {
            IList items = (IList)itemsField.GetValue(menu);
            if (items.Count < 2) return;
            MethodInfo setText = items[1].GetType().GetMethod("SetText", Any,
                null, new Type[] { typeof(string) }, null);
            if (setText != null) setText.Invoke(items[1],
                new object[] { ModeText() });
        }

        private static string ModeText()
        {
            return "Controller Mode: " + (ModernControllerSchemePatch.IsEnabled()
                ? "Magicka 2-style" : "Magicka 1 (original)");
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, Any |
                    BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, Any |
                    BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
    }

    public static class ControllerOptionsSelectPatch<T, TController>
    { public static bool Prefix(T __instance, TController iSender, out int __state)
      { return ControllerOptionsMenuPatch.BeforeSelect(__instance, out __state); }
      public static void Postfix(T __instance, int __state)
      { ControllerOptionsMenuPatch.Restore(__instance, __state); } }
    public static class ControllerOptionsHighlightPatch<T>
    { public static bool Prefix(T __instance, ref string __result, out int __state)
      { return ControllerOptionsMenuPatch.BeforeHighlight(
          __instance, ref __result, out __state); }
      public static void Postfix(T __instance, int __state)
      { ControllerOptionsMenuPatch.Restore(__instance, __state); } }
}
