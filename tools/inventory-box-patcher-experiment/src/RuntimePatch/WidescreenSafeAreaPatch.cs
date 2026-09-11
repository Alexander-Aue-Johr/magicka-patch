using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class WidescreenSafeAreaPatch
    {
        private static FieldInfo keyboardScreenSizeField;
        private static FieldInfo pointXField;
        private static FieldInfo pointYField;
        private static FieldInfo vectorXField;
        private static FieldInfo hintPositionField;
        private static FieldInfo hintBoxEffectField;

        internal static readonly RuntimePatchDefinition KeyboardDefinition =
            RuntimePatchDefinition.Prefix(
                "Keyboard HUD ultrawide safe area",
                "org.magickacommunitypatch.keyboard-hud-ultrawide-safe-area",
                FindKeyboardDrawIcon,
                CreateKeyboardPrefix);

        internal static readonly RuntimePatchDefinition TutorialDefinition =
            RuntimePatchDefinition.Transpile(
                "Tutorial prompt ultrawide safe area",
                "org.magickacommunitypatch.tutorial-ultrawide-safe-area",
                FindTutorialDraw,
                typeof(WidescreenSafeAreaPatch).GetMethod(
                    "TutorialTranspiler"));

        public static float GetHorizontalInset(int screenWidth, int screenHeight)
        {
            int safeWidth = screenHeight * 16 / 9;
            if (screenWidth > safeWidth)
                return (float)(screenWidth - safeWidth) * 0.5f;
            return 0f;
        }

        public static float GetRightAlignedCentre(
            int screenWidth,
            int screenHeight,
            float contentWidth)
        {
            int safeWidth = screenHeight * 16 / 9;
            if (safeWidth > screenWidth)
                safeWidth = screenWidth;
            return (float)(screenWidth - safeWidth) * 0.5f +
                (float)safeWidth * 0.95f - contentWidth * 0.5f;
        }

        private static MethodInfo FindKeyboardDrawIcon(Assembly targetAssembly)
        {
            Type keyboardHud = targetAssembly.GetType(
                "Magicka.GameLogic.UI.KeyboardHUD",
                true);
            Type renderData = keyboardHud.GetNestedType(
                "RenderData",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (renderData == null)
                throw new TypeLoadException(keyboardHud.FullName + "+RenderData");
            Type point = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Point");
            keyboardScreenSizeField = RequireField(
                renderData,
                "mScreenSize",
                point);
            pointXField = RequireField(point, "X", typeof(int));
            pointYField = RequireField(point, "Y", typeof(int));
            MethodInfo method = renderData.GetMethod(
                "DrawIcon",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[]
                {
                    typeof(int), typeof(int), typeof(float), typeof(float)
                },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(renderData.FullName, "DrawIcon");
            return method;
        }

        private static MethodInfo CreateKeyboardPrefix(MethodInfo target)
        {
            return typeof(WidescreenSafeAreaPatch).GetMethod("KeyboardPrefix");
        }

        public static void KeyboardPrefix(object __instance,
            ref int iPosition, int iElementIndex, ref float iXOffset,
            ref float iYOffset)
        {
            HybridInputPatch.AdjustIcon(iElementIndex, ref iPosition,
                ref iXOffset, ref iYOffset);
            object size = keyboardScreenSizeField.GetValue(__instance);
            int width = (int)pointXField.GetValue(size);
            int height = (int)pointYField.GetValue(size);
            iXOffset += GetHorizontalInset(width, height);
        }

        private static MethodInfo FindTutorialDraw(Assembly targetAssembly)
        {
            Type tutorial = targetAssembly.GetType(
                "Magicka.Graphics.TutorialManager",
                true);
            Type renderData = tutorial.GetNestedType(
                "HintRenderData",
                BindingFlags.Public | BindingFlags.NonPublic);
            if (renderData == null)
                throw new TypeLoadException(tutorial.FullName + "+HintRenderData");
            Type position = tutorial.GetNestedType(
                "Position",
                BindingFlags.Public | BindingFlags.NonPublic);
            Type point = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Point");
            Type vector = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Vector2");
            Type boxEffect = targetAssembly.GetType(
                "Magicka.Graphics.Effects.TextBoxEffect",
                true);
            hintPositionField = RequireField(
                renderData,
                "HintPosition",
                position);
            hintBoxEffectField = RequireField(
                renderData,
                "mBoxEffect",
                boxEffect);
            pointXField = RequireField(point, "X", typeof(int));
            pointYField = RequireField(point, "Y", typeof(int));
            vectorXField = RequireField(vector, "X", typeof(float));
            MethodInfo method = renderData.GetMethod(
                "Draw",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(renderData.FullName, "Draw");
            IList<LocalVariableInfo> locals = method.GetMethodBody().LocalVariables;
            if (locals.Count < 4 || locals[0].LocalType != vector ||
                locals[1].LocalType != point ||
                locals[2].LocalType != typeof(float) ||
                locals[3].LocalType != typeof(float))
                throw new InvalidOperationException(
                    "Tutorial HintRenderData.Draw local contract changed.");
            return method;
        }

        public static IEnumerable<CodeInstruction> TutorialTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            Type tutorial = RuntimeMember.FindLoadedType(
                "Magicka.Graphics.TutorialManager");
            FindTutorialDraw(tutorial.Assembly);
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int anchor = -1;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, hintBoxEffectField))
                    continue;
                anchor = index - 1;
                break;
            }
            if (anchor < 0)
                throw new InvalidOperationException(
                    "Tutorial prompt draw anchor was not found.");

            MethodInfo adjust = typeof(WidescreenSafeAreaPatch).GetMethod(
                "AdjustRightAlignedCentre");
            List<CodeInstruction> injected = new List<CodeInstruction>();
            injected.Add(new CodeInstruction(OpCodes.Ldarg_0));
            injected.Add(new CodeInstruction(OpCodes.Ldloc_2));
            injected.Add(new CodeInstruction(OpCodes.Ldloca_S, (byte)1));
            injected.Add(new CodeInstruction(OpCodes.Ldfld, pointXField));
            injected.Add(new CodeInstruction(OpCodes.Ldloca_S, (byte)1));
            injected.Add(new CodeInstruction(OpCodes.Ldfld, pointYField));
            injected.Add(new CodeInstruction(OpCodes.Ldloca_S, (byte)0));
            injected.Add(new CodeInstruction(OpCodes.Ldfld, vectorXField));
            injected.Add(new CodeInstruction(OpCodes.Call, adjust));
            injected.Add(new CodeInstruction(OpCodes.Stloc_2));
            injected[0].labels.AddRange(result[anchor].labels);
            injected[0].blocks.AddRange(result[anchor].blocks);
            result[anchor].labels.Clear();
            result[anchor].blocks.Clear();
            result.InsertRange(anchor, injected);
            return result;
        }

        public static float AdjustRightAlignedCentre(
            object renderData,
            float originalPosition,
            int screenWidth,
            int screenHeight,
            float contentWidth)
        {
            int position = Convert.ToInt32(
                hintPositionField.GetValue(renderData));
            if (position == 5 || position == 8)
                return GetRightAlignedCentre(
                    screenWidth,
                    screenHeight,
                    contentWidth);
            return originalPosition;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
