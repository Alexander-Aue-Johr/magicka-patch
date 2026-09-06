using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class LoadingScreenEndDrawPatch
    {
        private static FieldInfo deviceField;
        private static FieldInfo depthBufferField;
        private static MethodInfo clearMethod;
        private static MethodInfo setDepthBufferMethod;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "LoadingScreen depth-buffer restore order",
                "org.magickacommunitypatch.loading-screen-depth-buffer",
                FindTarget,
                typeof(LoadingScreenEndDrawPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type loadingScreenType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.LoadingScreen",
                true);
            deviceField = loadingScreenType.GetField(
                "mDevice",
                BindingFlags.Instance | BindingFlags.NonPublic);
            depthBufferField = loadingScreenType.GetField(
                "depthStencilBuffer_Saved",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (deviceField == null)
                throw new MissingFieldException(loadingScreenType.FullName, "mDevice");
            if (depthBufferField == null)
                throw new MissingFieldException(
                    loadingScreenType.FullName,
                    "depthStencilBuffer_Saved");

            PropertyInfo depthBuffer = deviceField.FieldType.GetProperty(
                "DepthStencilBuffer",
                BindingFlags.Instance | BindingFlags.Public);
            setDepthBufferMethod = depthBuffer == null
                ? null
                : depthBuffer.GetSetMethod();
            if (setDepthBufferMethod == null ||
                setDepthBufferMethod.GetParameters().Length != 1 ||
                setDepthBufferMethod.GetParameters()[0].ParameterType !=
                    depthBufferField.FieldType)
                throw new MissingMethodException(
                    deviceField.FieldType.FullName,
                    "set_DepthStencilBuffer");
            clearMethod = FindClearMethod(deviceField.FieldType);

            MethodInfo method = loadingScreenType.GetMethod(
                "EndDraw",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(loadingScreenType.FullName, "EndDraw");
            return method;
        }

        private static MethodInfo FindClearMethod(Type graphicsDeviceType)
        {
            MethodInfo[] methods = graphicsDeviceType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            MethodInfo result = null;
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != "Clear" || parameters.Length != 4 ||
                    parameters[0].ParameterType.FullName !=
                        "Microsoft.Xna.Framework.Graphics.ClearOptions" ||
                    parameters[1].ParameterType.FullName !=
                        "Microsoft.Xna.Framework.Graphics.Color" ||
                    parameters[2].ParameterType != typeof(float) ||
                    parameters[3].ParameterType != typeof(int))
                    continue;
                if (result != null)
                    throw new InvalidOperationException(
                        "Multiple GraphicsDevice.Clear overloads matched.");
                result = method;
            }
            if (result == null)
                throw new MissingMethodException(graphicsDeviceType.FullName, "Clear");
            return result;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int clearCall = FindSingleCall(result, clearMethod, "Clear");
            int depthSetter = FindSingleCall(
                result,
                setDepthBufferMethod,
                "set_DepthStencilBuffer");
            if (clearCall < 6 || depthSetter < clearCall || depthSetter < 4 ||
                result[clearCall - 6].opcode != OpCodes.Ldarg_0 ||
                result[clearCall - 5].opcode != OpCodes.Ldfld ||
                result[clearCall - 5].operand as FieldInfo != deviceField ||
                result[depthSetter - 4].opcode != OpCodes.Ldarg_0 ||
                result[depthSetter - 3].opcode != OpCodes.Ldfld ||
                result[depthSetter - 3].operand as FieldInfo != deviceField ||
                result[depthSetter - 2].opcode != OpCodes.Ldarg_0 ||
                result[depthSetter - 1].opcode != OpCodes.Ldfld ||
                result[depthSetter - 1].operand as FieldInfo != depthBufferField)
                throw new InvalidOperationException(
                    "LoadingScreen.EndDraw restore sequence has an unexpected shape.");

            int depthStart = depthSetter - 4;
            int clearStart = clearCall - 6;
            List<CodeInstruction> restore = result.GetRange(depthStart, 5);
            result.RemoveRange(depthStart, 5);
            restore[0].labels.AddRange(result[clearStart].labels);
            restore[0].blocks.AddRange(result[clearStart].blocks);
            result[clearStart].labels.Clear();
            result[clearStart].blocks.Clear();
            result.InsertRange(clearStart, restore);
            return result;
        }

        private static int FindSingleCall(
            List<CodeInstruction> instructions,
            MethodInfo method,
            string label)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                if ((instructions[index].opcode != OpCodes.Call &&
                        instructions[index].opcode != OpCodes.Callvirt) ||
                    instructions[index].operand as MethodInfo != method)
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple LoadingScreen.EndDraw " + label + " calls matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "LoadingScreen.EndDraw " + label + " call was not found.");
            return match;
        }
    }
}
