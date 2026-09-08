using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Magicka.CommunityPatch.Runtime
{
    public static class CharacterSelectWidgetTexturePatch
    {
        private static Func<object, bool> shouldDraw;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Character-select disposed widget texture guard",
                "org.magickacommunitypatch.character-select-widget-texture",
                FindDrawWidget,
                target => typeof(CharacterSelectWidgetTexturePatch).GetMethod(
                    "Prefix"));

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            return targetAssembly.GetType(
                    "Magicka.GameLogic.UI.UISystem.Widget",
                    false) != null &&
                targetAssembly.GetType(
                    "Magicka.GameLogic.UI.UISystem.Image",
                    false) != null;
        }

        private static MethodInfo FindDrawWidget(Assembly targetAssembly)
        {
            Type widgetType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.UISystem.Widget",
                true);
            Type imageType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.UISystem.Image",
                true);
            PropertyInfo textureProperty = imageType.GetProperty(
                "Texture",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo textureGetter = textureProperty == null
                ? null
                : textureProperty.GetGetMethod();
            if (textureGetter == null)
                throw new MissingMemberException(imageType.FullName, "Texture");
            Type textureType = textureGetter.ReturnType;
            PropertyInfo disposedProperty = textureType.GetProperty(
                "IsDisposed",
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo disposedGetter = disposedProperty == null
                ? null
                : disposedProperty.GetGetMethod();
            if (disposedGetter == null || disposedGetter.ReturnType != typeof(bool))
                throw new MissingMemberException(textureType.FullName, "IsDisposed");

            shouldDraw = BuildPredicate(
                imageType,
                textureType,
                textureGetter,
                disposedGetter);

            Type menuType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.Menu.Main.SubMenuCharacterSelect",
                true);
            MethodInfo drawWidget = menuType.GetMethod(
                "DrawWidget",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { widgetType },
                null);
            if (drawWidget == null || drawWidget.ReturnType != typeof(void))
                throw new MissingMethodException(menuType.FullName, "DrawWidget");
            return drawWidget;
        }

        private static Func<object, bool> BuildPredicate(
            Type imageType,
            Type textureType,
            MethodInfo textureGetter,
            MethodInfo disposedGetter)
        {
            DynamicMethod predicate = new DynamicMethod(
                "CharacterSelectWidgetShouldDraw",
                typeof(bool),
                new Type[] { typeof(object) },
                typeof(CharacterSelectWidgetTexturePatch),
                true);
            ILGenerator il = predicate.GetILGenerator();
            LocalBuilder image = il.DeclareLocal(imageType);
            LocalBuilder texture = il.DeclareLocal(textureType);
            Label allow = il.DefineLabel();
            Label reject = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, imageType);
            il.Emit(OpCodes.Stloc, image);
            il.Emit(OpCodes.Ldloc, image);
            il.Emit(OpCodes.Brfalse, allow);
            il.Emit(OpCodes.Ldloc, image);
            il.Emit(OpCodes.Callvirt, textureGetter);
            il.Emit(OpCodes.Stloc, texture);
            il.Emit(OpCodes.Ldloc, texture);
            il.Emit(OpCodes.Brfalse, reject);
            il.Emit(OpCodes.Ldloc, texture);
            il.Emit(OpCodes.Callvirt, disposedGetter);
            il.Emit(OpCodes.Brtrue, reject);

            il.MarkLabel(allow);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(reject);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            return (Func<object, bool>)predicate.CreateDelegate(
                typeof(Func<object, bool>));
        }

        public static bool Prefix(object iWidget)
        {
            return shouldDraw(iWidget);
        }
    }
}
