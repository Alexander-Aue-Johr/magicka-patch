using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class TomeSupporterPatch
    {
        private static PropertyInfo currentMenuProperty;
        private static FieldInfo versionTextField;
        private static Type cutsceneMenuType;
        private static Type introMenuType;
        private static FieldInfo pointXField;
        private static FieldInfo pointYField;
        private static PropertyInfo mouseXProperty;
        private static PropertyInfo mouseYProperty;
        private static PropertyInfo mouseButtonProperty;
        private static object pressedButton;
        private static object releasedButton;
        private static PropertyInfo textCharactersProperty;
        private static PropertyInfo textEndIndexProperty;
        private static PropertyInfo textFontProperty;
        private static PropertyInfo fontLineHeightProperty;
        private static MethodInfo measureTextMethod;
        private static FieldInfo vectorXField;
        private static MethodInfo showPopupMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Tome supporter credits interaction",
                "org.magickacommunitypatch.tome-supporter-credits",
                FindControllerMouseAction,
                target => typeof(TomeSupporterPatch).GetMethod("Prefix"));

        private static MethodInfo FindControllerMouseAction(Assembly assembly)
        {
            Type tome = assembly.GetType("Magicka.GameLogic.UI.Tome", true);
            MethodInfo method = null;
            MethodInfo[] candidates = tome.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int index = 0; index < candidates.Length; index++)
                if (candidates[index].Name == "ControllerMouseAction" &&
                    candidates[index].GetParameters().Length == 4)
                    method = candidates[index];
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(tome.FullName,
                    "ControllerMouseAction");

            currentMenuProperty = tome.GetProperty("CurrentMenu",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            versionTextField = tome.GetField("sVersionText",
                BindingFlags.Static | BindingFlags.NonPublic);
            cutsceneMenuType = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCutscene", true);
            introMenuType = assembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuIntro", true);
            if (currentMenuProperty == null || versionTextField == null)
                throw new InvalidOperationException(
                    "Tome supporter field contract changed.");

            ParameterInfo[] parameters = method.GetParameters();
            Type point = parameters[1].ParameterType;
            Type mouse = parameters[2].ParameterType;
            pointXField = RequireField(point, "X");
            pointYField = RequireField(point, "Y");
            mouseXProperty = RequireProperty(mouse, "X");
            mouseYProperty = RequireProperty(mouse, "Y");
            mouseButtonProperty = RequireProperty(mouse, "LeftButton");
            Type button = mouseButtonProperty.PropertyType;
            pressedButton = Enum.Parse(button, "Pressed");
            releasedButton = Enum.Parse(button, "Released");

            Type text = versionTextField.FieldType;
            textCharactersProperty = RequireProperty(text, "Characters");
            textEndIndexProperty = RequireProperty(text, "EndIndex");
            textFontProperty = RequireProperty(text, "Font");
            Type font = textFontProperty.PropertyType;
            fontLineHeightProperty = RequireProperty(font, "LineHeight");
            measureTextMethod = font.GetMethod("MeasureText",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic, null,
                new Type[] { typeof(string), typeof(bool) }, null);
            if (measureTextMethod == null)
                throw new MissingMethodException(font.FullName, "MeasureText");
            vectorXField = RequireField(measureTextMethod.ReturnType, "X");

            Type popup = assembly.GetType(
                "Magicka.WebTools.Paradox.ParadoxPopupUtils", true);
            showPopupMethod = popup.GetMethod("ShowErrorPopup",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic, null,
                new Type[] { typeof(string), typeof(string) }, null);
            if (showPopupMethod == null)
                throw new MissingMethodException(popup.FullName,
                    "ShowErrorPopup(string,string)");
            return method;
        }

        public static bool Prefix(
            object __instance,
            object screenSize,
            object newMouseState,
            object mOldMouseState)
        {
            object newButton = mouseButtonProperty.GetValue(newMouseState, null);
            object oldButton = mouseButtonProperty.GetValue(mOldMouseState, null);
            if (!newButton.Equals(releasedButton) ||
                !oldButton.Equals(pressedButton))
                return true;
            object menu = currentMenuProperty.GetValue(__instance, null);
            if ((menu != null && cutsceneMenuType.IsInstanceOfType(menu)) ||
                (menu != null && introMenuType.IsInstanceOfType(menu)))
                return true;
            object text = versionTextField.GetValue(null);
            if (!IsVersionTextHit(screenSize, newMouseState, text))
                return true;
            ShowSupporters();
            return false;
        }

        public static bool IsVersionTextHit(
            object screenSize,
            object mouseState,
            object text)
        {
            if (text == null)
                return false;
            int width = (int)pointXField.GetValue(screenSize);
            int height = (int)pointYField.GetValue(screenSize);
            int x = (int)mouseXProperty.GetValue(mouseState, null);
            int y = (int)mouseYProperty.GetValue(mouseState, null);
            object font = textFontProperty.GetValue(text, null);
            int lineHeight = Convert.ToInt32(
                fontLineHeightProperty.GetValue(font, null));
            char[] characters = (char[])textCharactersProperty.GetValue(text, null);
            int length = Convert.ToInt32(textEndIndexProperty.GetValue(text, null));
            if (characters == null || length < 0 || length > characters.Length)
                return false;
            object size = measureTextMethod.Invoke(font, new object[]
            {
                new string(characters, 0, length), true
            });
            float textWidth = Convert.ToSingle(vectorXField.GetValue(size));
            return IsVersionTextHitValues(
                width, height, x, y, lineHeight, textWidth);
        }

        public static bool IsVersionTextHitValues(
            int screenWidth,
            int screenHeight,
            int mouseX,
            int mouseY,
            int lineHeight,
            float textWidth)
        {
            int top = screenHeight - 16 - lineHeight;
            return mouseX >= 16 && mouseY >= top &&
                mouseY <= screenHeight - 16 &&
                mouseX <= 16f + textWidth;
        }

        public static void ShowSupporters()
        {
            string names = string.Join(", ",
                RuntimePatchMetadata.PatreonSupporters);
            showPopupMethod.Invoke(null, new object[]
            {
                "Community Patch supporters",
                "Thank you for supporting the Community Patch and its " +
                    "continued development:\n\n" + names
            });
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }
}
