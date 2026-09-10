using System;
using System.Collections;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class InGameUiScaleSelectionPatch
    {
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic;
        private static Type graphicsType;
        private static Type resolutionType;
        private static Type menuType;
        private static Type menuTextItemType;
        private static Type vectorType;
        private static Type textAlignType;
        private static FieldInfo menuItemsField;
        private static FieldInfo selectedItemField;
        private static FieldInfo backgroundSizeField;
        private static FieldInfo graphicsOptionsField;
        private static FieldInfo resolutionItemsField;
        private static FieldInfo resolutionFontField;
        private static FieldInfo scrollBarField;
        private static ConstructorInfo menuTextConstructor;
        private static ConstructorInfo vectorConstructor;
        private static MethodInfo menuTextSetText;
        private static MethodInfo pushMenu;
        private static MethodInfo popMenu;
        private static PropertyInfo resolutionInstance;

        internal static readonly RuntimePatchDefinition GraphicsConstructor =
            RuntimePatchDefinition.ConstructorPostfix(
                "Graphics menu UI-scale row",
                "org.magickacommunitypatch.ui-scale-graphics-row",
                FindGraphicsConstructor,
                typeof(InGameUiScaleSelectionPatch).GetMethod(
                    "GraphicsConstructorPostfix"));

        internal static readonly RuntimePatchDefinition GraphicsSelect =
            RuntimePatchDefinition.Prefix(
                "Graphics menu UI-scale selection",
                "org.magickacommunitypatch.ui-scale-graphics-select",
                assembly => FindGraphicsMethod(assembly, "IControllerSelect"),
                CreateGraphicsSelectPrefix);

        internal static readonly RuntimePatchDefinition GraphicsHighlight =
            RuntimePatchDefinition.Prefix(
                "Graphics menu UI-scale label",
                "org.magickacommunitypatch.ui-scale-graphics-label",
                assembly => FindGraphicsMethod(
                    assembly, "IGetHighlightedButtonName"),
                target => typeof(InGameUiScaleSelectionPatch).GetMethod(
                    "GraphicsHighlightPrefix"));

        internal static readonly RuntimePatchDefinition GraphicsEnter =
            RuntimePatchDefinition.Postfix(
                "Graphics menu UI-scale refresh",
                "org.magickacommunitypatch.ui-scale-graphics-enter",
                assembly => FindGraphicsMethod(assembly, "OnEnter"),
                target => typeof(InGameUiScaleSelectionPatch).GetMethod(
                    "GraphicsEnterPostfix"));

        internal static readonly RuntimePatchDefinition GraphicsPositions =
            RuntimePatchDefinition.Postfix(
                "Graphics menu UI-scale layout",
                "org.magickacommunitypatch.ui-scale-graphics-layout",
                assembly => FindGraphicsMethod(assembly, "UpdatePositions"),
                target => typeof(InGameUiScaleSelectionPatch).GetMethod(
                    "GraphicsPositionsPostfix"));

        internal static readonly RuntimePatchDefinition ResolutionEnter =
            RuntimePatchDefinition.Prefix(
                "UI-scale choice population",
                "org.magickacommunitypatch.ui-scale-resolution-enter",
                assembly => FindResolutionMethod(assembly, "OnEnter"),
                target => typeof(InGameUiScaleSelectionPatch).GetMethod(
                    "ResolutionEnterPrefix"));

        internal static readonly RuntimePatchDefinition ResolutionSelect =
            RuntimePatchDefinition.Prefix(
                "UI-scale choice application",
                "org.magickacommunitypatch.ui-scale-resolution-select",
                assembly => FindResolutionMethod(
                    assembly, "IControllerSelect"),
                CreateResolutionSelectPrefix);

        internal static readonly RuntimePatchDefinition ResolutionBack =
            RuntimePatchDefinition.Prefix(
                "UI-scale choice cancellation",
                "org.magickacommunitypatch.ui-scale-resolution-back",
                assembly => FindResolutionMethod(assembly, "IControllerBack"),
                CreateResolutionBackPrefix);

        internal static readonly RuntimePatchDefinition ResolutionExit =
            RuntimePatchDefinition.Prefix(
                "UI-scale choice exit reset",
                "org.magickacommunitypatch.ui-scale-resolution-exit",
                assembly => FindResolutionMethod(assembly, "OnExit"),
                target => typeof(InGameUiScaleSelectionPatch).GetMethod(
                    "ResolutionExitPrefix"));

        internal static RuntimePatchDefinition[] Definitions
        {
            get
            {
                return new RuntimePatchDefinition[]
                {
                    GraphicsConstructor,
                    GraphicsSelect,
                    GraphicsHighlight,
                    GraphicsEnter,
                    GraphicsPositions,
                    ResolutionEnter,
                    ResolutionSelect,
                    ResolutionBack,
                    ResolutionExit
                };
            }
        }

        private static ConstructorInfo FindGraphicsConstructor(
            Assembly assembly)
        {
            Configure(assembly);
            ConstructorInfo constructor = graphicsType.GetConstructor(
                Instance, null, Type.EmptyTypes, null);
            if (constructor == null)
                throw new MissingMethodException(graphicsType.FullName, ".ctor");
            return constructor;
        }

        private static MethodInfo FindGraphicsMethod(
            Assembly assembly, string name)
        {
            Configure(assembly);
            return RequireDeclaredMethod(graphicsType, name);
        }

        private static MethodInfo FindResolutionMethod(
            Assembly assembly, string name)
        {
            Configure(assembly);
            return RequireDeclaredMethod(resolutionType, name);
        }

        private static MethodInfo RequireDeclaredMethod(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(
                Instance | BindingFlags.DeclaredOnly);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == name)
                {
                    if (result != null)
                        throw new AmbiguousMatchException(type.FullName + "." + name);
                    result = methods[index];
                }
            if (result == null)
                throw new MissingMethodException(type.FullName, name);
            return result;
        }

        private static void Configure(Assembly assembly)
        {
            if (graphicsType != null && graphicsType.Assembly == assembly)
                return;
            graphicsType = assembly.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptionsGraphics",
                true);
            resolutionType = assembly.GetType(
                "Magicka.GameLogic.GameStates.InGameMenus.InGameMenuOptionsResolution",
                true);
            menuType = graphicsType.BaseType;
            menuTextItemType = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.MenuTextItem", true);
            vectorType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector2");
            textAlignType = RuntimeMember.FindLoadedType("PolygonHead.TextAlign");
            menuItemsField = RequireField(menuType, "mMenuItems");
            selectedItemField = RequireField(menuType, "mSelectedItem");
            backgroundSizeField = RequireField(menuType, "mBackgroundSize");
            graphicsOptionsField = RequireField(graphicsType, "mOptions");
            resolutionItemsField = RequireField(
                resolutionType, "mResolutions");
            resolutionFontField = RequireField(resolutionType, "mFont");
            scrollBarField = RequireField(resolutionType, "mScrollBar");
            Type bitmapFont = RuntimeMember.FindLoadedType("PolygonHead.BitmapFont");
            menuTextConstructor = menuTextItemType.GetConstructor(
                new Type[]
                {
                    typeof(string), vectorType, bitmapFont, textAlignType
                });
            vectorConstructor = vectorType.GetConstructor(
                new Type[] { typeof(float), typeof(float) });
            menuTextSetText = menuTextItemType.GetMethod(
                "SetText", Instance, null,
                new Type[] { typeof(string) }, null);
            pushMenu = menuType.GetMethod(
                "PushMenu", BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null, new Type[] { menuType }, null);
            popMenu = menuType.GetMethod(
                "PopMenu", BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            resolutionInstance = resolutionType.GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (menuTextConstructor == null || vectorConstructor == null ||
                menuTextSetText == null || pushMenu == null || popMenu == null ||
                resolutionInstance == null)
                throw new MissingMemberException("UI-scale menu contract");
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name, Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private static MethodInfo CreateGraphicsSelectPrefix(MethodInfo target)
        {
            return typeof(InGameUiScaleSelectionPatch).GetMethod(
                "GraphicsSelectPrefix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        private static MethodInfo CreateResolutionSelectPrefix(MethodInfo target)
        {
            return typeof(InGameUiScaleSelectionPatch).GetMethod(
                "ResolutionSelectPrefix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        private static MethodInfo CreateResolutionBackPrefix(MethodInfo target)
        {
            return typeof(InGameUiScaleSelectionPatch).GetMethod(
                "ResolutionBackPrefix").MakeGenericMethod(
                    target.GetParameters()[0].ParameterType);
        }

        public static void GraphicsConstructorPostfix(object __instance)
        {
            IList items = (IList)menuItemsField.GetValue(__instance);
            IList options = (IList)graphicsOptionsField.GetValue(__instance);
            object sample = options[0];
            FieldInfo fontField = RequireField(menuTextItemType, "mFont");
            object font = fontField.GetValue(sample);
            object zero = Activator.CreateInstance(vectorType);
            object right = Enum.Parse(textAlignType, "Right");
            object left = Enum.Parse(textAlignType, "Left");
            items.Insert(2, menuTextConstructor.Invoke(
                new object[] { "UI Scale", zero, font, right }));
            options.Add(menuTextConstructor.Invoke(
                new object[]
                {
                    HighResolutionUiRenderPatch.GetScaleText(),
                    zero, font, left
                }));
            SetBackground(__instance, 500f, 200f);
        }

        public static bool GraphicsSelectPrefix<TController>(
            object __instance, TController iSender)
        {
            int selected = (int)selectedItemField.GetValue(__instance);
            if (selected == 2)
            {
                HighResolutionUiRenderPatch.BeginScaleSelection();
                pushMenu.Invoke(null, new object[]
                {
                    resolutionInstance.GetValue(null, null)
                });
                return false;
            }
            if (selected == 3)
            {
                popMenu.Invoke(null, null);
                return false;
            }
            return true;
        }

        public static bool GraphicsHighlightPrefix(
            object __instance, ref string __result)
        {
            int selected = (int)selectedItemField.GetValue(__instance);
            if (selected < 2)
                return true;
            __result = selected == 2 ? "ui_scale" : "back";
            return false;
        }

        public static void GraphicsEnterPostfix(object __instance)
        {
            IList options = (IList)graphicsOptionsField.GetValue(__instance);
            if (options.Count > 2)
                menuTextSetText.Invoke(
                    options[2],
                    new object[] { HighResolutionUiRenderPatch.GetScaleText() });
        }

        public static void GraphicsPositionsPostfix(object __instance)
        {
            SetBackground(__instance, 500f, 200f);
        }

        public static bool ResolutionEnterPrefix(object __instance)
        {
            if (!HighResolutionUiRenderPatch.IsScaleSelection())
                return true;
            selectedItemField.SetValue(__instance, 0);
            object scrollBar = scrollBarField.GetValue(__instance);
            PropertyInfo value = scrollBar.GetType().GetProperty(
                "Value", Instance);
            value.SetValue(scrollBar, 0, null);
            object resolutions = resolutionItemsField.GetValue(__instance);
            MethodInfo clear = resolutions.GetType().GetMethod("Clear");
            MethodInfo add = resolutions.GetType().GetMethod("Add");
            clear.Invoke(resolutions, null);
            Type pairType = resolutionItemsField.FieldType
                .GetGenericArguments()[1];
            Type intListType = pairType.GetGenericArguments()[1];
            object font = resolutionFontField.GetValue(__instance);
            object center = Enum.Parse(textAlignType, "Center");
            object zero = Activator.CreateInstance(vectorType);
            for (int index = 0; index < 13; index++)
            {
                int percent = 100 + index * 25;
                string label = index == 0 ? "Off" : percent + "%";
                object item = menuTextConstructor.Invoke(
                    new object[] { label, zero, font, center });
                object pair = Activator.CreateInstance(
                    pairType,
                    new object[]
                    {
                        item, Activator.CreateInstance(intListType)
                    });
                add.Invoke(resolutions, new object[] { (uint)percent, pair });
            }
            MethodInfo setMax = scrollBar.GetType().GetMethod(
                "SetMaxValue", Instance, null,
                new Type[] { typeof(int) }, null);
            setMax.Invoke(scrollBar, new object[] { 1 });
            MethodInfo update = resolutionType.GetMethod(
                "UpdatePositions", Instance);
            update.Invoke(__instance, null);
            return false;
        }

        public static bool ResolutionSelectPrefix<TController>(
            object __instance, TController iSender)
        {
            if (!HighResolutionUiRenderPatch.IsScaleSelection())
                return true;
            int selected = (int)selectedItemField.GetValue(__instance);
            if (selected >= 0 && selected < 13)
                HighResolutionUiRenderPatch.ApplyScalePercent(
                    100 + selected * 25);
            HighResolutionUiRenderPatch.EndScaleSelection();
            popMenu.Invoke(null, null);
            return false;
        }

        public static void ResolutionBackPrefix<TController>(
            TController iSender)
        {
            HighResolutionUiRenderPatch.EndScaleSelection();
        }

        public static void ResolutionExitPrefix()
        {
            HighResolutionUiRenderPatch.EndScaleSelection();
        }

        private static void SetBackground(
            object instance, float width, float height)
        {
            backgroundSizeField.SetValue(
                instance,
                vectorConstructor.Invoke(new object[] { width, height }));
        }
    }
}
