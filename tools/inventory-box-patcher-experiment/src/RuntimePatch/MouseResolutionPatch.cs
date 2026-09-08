using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MouseResolutionPatch
    {
        private static FieldInfo formField;
        private static MethodInfo graphicsDeviceGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Borderless mouse coordinate scaling",
                "org.magickacommunitypatch.mouse-resolution",
                FindGameDraw,
                typeof(MouseResolutionPatch).GetMethod("Transpiler"));

        private static MethodInfo FindGameDraw(Assembly targetAssembly)
        {
            Type gameType = targetAssembly.GetType("Magicka.Game", true);
            Type gameTimeType = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.GameTime");
            MethodInfo draw = gameType.GetMethod(
                "Draw",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { gameTimeType },
                null);
            if (draw == null || draw.ReturnType != typeof(void))
                throw new MissingMethodException(gameType.FullName, "Draw");
            formField = gameType.GetField(
                "mForm",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (formField == null || formField.FieldType.FullName !=
                "System.Windows.Forms.Form")
                throw new MissingFieldException(gameType.FullName, "mForm");
            PropertyInfo graphicsDevice = gameType.GetProperty(
                "GraphicsDevice",
                BindingFlags.Instance | BindingFlags.Public);
            graphicsDeviceGetter = graphicsDevice == null
                ? null
                : graphicsDevice.GetGetMethod();
            if (graphicsDeviceGetter == null ||
                graphicsDeviceGetter.ReturnType.FullName !=
                    "Microsoft.Xna.Framework.Graphics.GraphicsDevice")
                throw new MissingMethodException(gameType.FullName, "get_GraphicsDevice");
            return draw;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            DynamicMethod wrapper = null;
            MethodInfo mouseGetState = null;
            int replacements = 0;
            for (int index = result.Count - 1; index >= 0; index--)
            {
                MethodInfo called = result[index].operand as MethodInfo;
                if (result[index].opcode != OpCodes.Call || called == null ||
                    called.DeclaringType == null ||
                    called.DeclaringType.FullName !=
                        "Microsoft.Xna.Framework.Input.Mouse" ||
                    called.Name != "GetState" ||
                    called.GetParameters().Length != 0)
                    continue;
                if (wrapper == null)
                {
                    mouseGetState = called;
                    wrapper = BuildWrapper(called);
                }
                else if (!Object.Equals(called, mouseGetState))
                {
                    throw new InvalidOperationException(
                        "Multiple Mouse.GetState methods matched.");
                }
                CodeInstruction loadGame = new CodeInstruction(OpCodes.Ldarg_0);
                loadGame.labels.AddRange(result[index].labels);
                loadGame.blocks.AddRange(result[index].blocks);
                result[index].labels.Clear();
                result[index].blocks.Clear();
                result[index].operand = wrapper;
                result.Insert(index, loadGame);
                replacements++;
            }
            if (replacements != 2)
                throw new InvalidOperationException(
                    "Expected two Mouse.GetState calls in Game.Draw, found " +
                    replacements + ".");
            return result;
        }

        public static int ScaleCoordinate(
            int coordinate,
            int physicalSize,
            int logicalSize)
        {
            long scaled = (long)coordinate * (long)logicalSize / physicalSize;
            if (scaled < 0L)
                return 0;
            if (scaled >= logicalSize)
                return logicalSize - 1;
            return (int)scaled;
        }

        private static DynamicMethod BuildWrapper(MethodInfo mouseGetState)
        {
            Type gameType = formField.DeclaringType;
            Type mouseStateType = mouseGetState.ReturnType;
            Type formType = formField.FieldType;
            Type graphicsType = graphicsDeviceGetter.ReturnType;
            MethodInfo getPresentation = RequireGetter(
                graphicsType,
                "PresentationParameters");
            Type presentationType = getPresentation.ReturnType;
            MethodInfo getBorderStyle = RequireGetter(formType, "FormBorderStyle");
            MethodInfo getClientSize = RequireGetter(formType, "ClientSize");
            Type sizeType = getClientSize.ReturnType;
            MethodInfo getWidth = RequireGetter(sizeType, "Width");
            MethodInfo getHeight = RequireGetter(sizeType, "Height");
            MethodInfo getFullScreen = RequireGetter(presentationType, "IsFullScreen");
            MethodInfo getBackBufferWidth = RequireGetter(
                presentationType,
                "BackBufferWidth");
            MethodInfo getBackBufferHeight = RequireGetter(
                presentationType,
                "BackBufferHeight");
            MethodInfo getX = RequireGetter(mouseStateType, "X");
            MethodInfo getY = RequireGetter(mouseStateType, "Y");
            MethodInfo getWheel = RequireGetter(
                mouseStateType,
                "ScrollWheelValue");
            MethodInfo getLeft = RequireGetter(mouseStateType, "LeftButton");
            MethodInfo getMiddle = RequireGetter(mouseStateType, "MiddleButton");
            MethodInfo getRight = RequireGetter(mouseStateType, "RightButton");
            MethodInfo getX1 = RequireGetter(mouseStateType, "XButton1");
            MethodInfo getX2 = RequireGetter(mouseStateType, "XButton2");
            Type buttonStateType = getLeft.ReturnType;
            ConstructorInfo constructor = mouseStateType.GetConstructor(
                new Type[]
                {
                    typeof(int), typeof(int), typeof(int),
                    buttonStateType, buttonStateType, buttonStateType,
                    buttonStateType, buttonStateType
                });
            if (constructor == null)
                throw new MissingMethodException(mouseStateType.FullName, ".ctor");

            DynamicMethod scaleCore = new DynamicMethod(
                "Game_ScaleMouseState",
                mouseStateType,
                new Type[] { mouseStateType, gameType },
                typeof(MouseResolutionPatch).Module,
                true);
            ILGenerator il = scaleCore.GetILGenerator();
            LocalBuilder form = il.DeclareLocal(formType);
            LocalBuilder graphics = il.DeclareLocal(graphicsType);
            LocalBuilder presentation = il.DeclareLocal(presentationType);
            LocalBuilder size = il.DeclareLocal(sizeType);
            LocalBuilder physicalWidth = il.DeclareLocal(typeof(int));
            LocalBuilder physicalHeight = il.DeclareLocal(typeof(int));
            LocalBuilder logicalWidth = il.DeclareLocal(typeof(int));
            LocalBuilder logicalHeight = il.DeclareLocal(typeof(int));
            Label done = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldfld, formField);
            il.Emit(OpCodes.Stloc, form);
            il.Emit(OpCodes.Ldloc, form);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, graphicsDeviceGetter);
            il.Emit(OpCodes.Stloc, graphics);
            il.Emit(OpCodes.Ldloc, graphics);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, form);
            il.Emit(OpCodes.Callvirt, getBorderStyle);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldloc, graphics);
            il.Emit(OpCodes.Callvirt, getPresentation);
            il.Emit(OpCodes.Stloc, presentation);
            il.Emit(OpCodes.Ldloc, presentation);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, presentation);
            il.Emit(OpCodes.Callvirt, getFullScreen);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldloc, form);
            il.Emit(OpCodes.Callvirt, getClientSize);
            il.Emit(OpCodes.Stloc, size);
            il.Emit(OpCodes.Ldloca, size);
            il.Emit(OpCodes.Call, getWidth);
            il.Emit(OpCodes.Stloc, physicalWidth);
            il.Emit(OpCodes.Ldloca, size);
            il.Emit(OpCodes.Call, getHeight);
            il.Emit(OpCodes.Stloc, physicalHeight);
            il.Emit(OpCodes.Ldloc, presentation);
            il.Emit(OpCodes.Callvirt, getBackBufferWidth);
            il.Emit(OpCodes.Stloc, logicalWidth);
            il.Emit(OpCodes.Ldloc, presentation);
            il.Emit(OpCodes.Callvirt, getBackBufferHeight);
            il.Emit(OpCodes.Stloc, logicalHeight);
            EmitInvalidSizeGuard(il, physicalWidth, done);
            EmitInvalidSizeGuard(il, physicalHeight, done);
            EmitInvalidSizeGuard(il, logicalWidth, done);
            EmitInvalidSizeGuard(il, logicalHeight, done);
            Label scale = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, physicalWidth);
            il.Emit(OpCodes.Ldloc, logicalWidth);
            il.Emit(OpCodes.Bne_Un, scale);
            il.Emit(OpCodes.Ldloc, physicalHeight);
            il.Emit(OpCodes.Ldloc, logicalHeight);
            il.Emit(OpCodes.Beq, done);
            il.MarkLabel(scale);

            EmitMouseGetter(il, getX);
            il.Emit(OpCodes.Ldloc, physicalWidth);
            il.Emit(OpCodes.Ldloc, logicalWidth);
            il.Emit(OpCodes.Call, typeof(MouseResolutionPatch).GetMethod(
                "ScaleCoordinate"));
            EmitMouseGetter(il, getY);
            il.Emit(OpCodes.Ldloc, physicalHeight);
            il.Emit(OpCodes.Ldloc, logicalHeight);
            il.Emit(OpCodes.Call, typeof(MouseResolutionPatch).GetMethod(
                "ScaleCoordinate"));
            EmitMouseGetter(il, getWheel);
            EmitMouseGetter(il, getLeft);
            EmitMouseGetter(il, getMiddle);
            EmitMouseGetter(il, getRight);
            EmitMouseGetter(il, getX1);
            EmitMouseGetter(il, getX2);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ret);

            DynamicMethod wrapper = new DynamicMethod(
                "Game_GetScaledMouseState",
                mouseStateType,
                new Type[] { gameType },
                typeof(MouseResolutionPatch).Module,
                true);
            il = wrapper.GetILGenerator();
            LocalBuilder state = il.DeclareLocal(mouseStateType);
            Label wrapperDone = il.DefineLabel();
            il.Emit(OpCodes.Call, mouseGetState);
            il.Emit(OpCodes.Stloc, state);
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, scaleCore);
            il.Emit(OpCodes.Stloc, state);
            il.Emit(OpCodes.Leave, wrapperDone);
            il.BeginCatchBlock(typeof(Exception));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Leave, wrapperDone);
            il.EndExceptionBlock();
            il.MarkLabel(wrapperDone);
            il.Emit(OpCodes.Ldloc, state);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        private static void EmitInvalidSizeGuard(
            ILGenerator il,
            LocalBuilder value,
            Label done)
        {
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, done);
        }

        private static void EmitMouseGetter(
            ILGenerator il,
            MethodInfo getter)
        {
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, getter);
        }

        private static MethodInfo RequireGetter(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo getter = property == null ? null : property.GetGetMethod();
            if (getter == null)
                throw new MissingMethodException(type.FullName, "get_" + name);
            return getter;
        }
    }
}
