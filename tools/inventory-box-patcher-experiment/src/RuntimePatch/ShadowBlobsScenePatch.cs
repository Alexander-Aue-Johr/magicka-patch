using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ShadowBlobsScenePatch
    {
        private static FieldInfo playStateSceneField;
        private static FieldInfo shadowSceneField;
        private static MethodInfo shadowBlobsInstanceGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "ShadowBlobs scene release",
                "org.magickacommunitypatch.shadow-blobs-scene-release",
                FindPlayStateDispose,
                typeof(ShadowBlobsScenePatch).GetMethod("Transpiler"));

        private static MethodInfo FindPlayStateDispose(Assembly targetAssembly)
        {
            Type sceneType = FindLoadedType("PolygonHead.Scene");
            Type shadowBlobsType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.ShadowBlobs",
                true);
            shadowSceneField = RequireField(
                shadowBlobsType,
                "mScene",
                sceneType,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            PropertyInfo instanceProperty = shadowBlobsType.GetProperty(
                "Instance",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            shadowBlobsInstanceGetter = instanceProperty == null
                ? null
                : instanceProperty.GetGetMethod();
            if (shadowBlobsInstanceGetter == null ||
                shadowBlobsInstanceGetter.ReturnType != shadowBlobsType ||
                shadowBlobsInstanceGetter.GetParameters().Length != 0)
                throw new MissingMethodException(
                    shadowBlobsType.FullName,
                    "get_Instance");

            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type gameStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.GameState",
                true);
            playStateSceneField = RequireField(
                gameStateType,
                "mScene",
                sceneType,
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            MethodInfo dispose = playStateType.GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (dispose == null || dispose.ReturnType != typeof(void))
                throw new MissingMethodException(playStateType.FullName, "Dispose");
            return dispose;
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type fieldType,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != fieldType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            throw new TypeLoadException(fullName);
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            MethodInfo helper = typeof(ShadowBlobsScenePatch).GetMethod(
                "DetachScene");
            int sceneNullStore = -1;
            int helperCall = -1;
            for (int index = 0; index < result.Count; index++)
            {
                if (Calls(result[index], helper))
                    helperCall = SingleMatch(helperCall, index, "scene-release helper");
                if (index < 2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(result[index].operand, playStateSceneField) ||
                    result[index - 1].opcode != OpCodes.Ldnull ||
                    result[index - 2].opcode != OpCodes.Ldarg_0)
                    continue;
                sceneNullStore = SingleMatch(
                    sceneNullStore,
                    index,
                    "PlayState scene null store");
            }
            if (sceneNullStore < 0)
                throw new InvalidOperationException(
                    "PlayState scene null store was not found.");
            if (helperCall >= 0)
            {
                if (helperCall != sceneNullStore - 3)
                    throw new InvalidOperationException(
                        "ShadowBlobs scene-release helper has an unexpected position.");
                return result;
            }

            int insertAt = sceneNullStore - 2;
            CodeInstruction first = new CodeInstruction(
                OpCodes.Call,
                shadowBlobsInstanceGetter);
            first.labels.AddRange(result[insertAt].labels);
            first.blocks.AddRange(result[insertAt].blocks);
            result[insertAt].labels.Clear();
            result[insertAt].blocks.Clear();
            result.InsertRange(
                insertAt,
                new CodeInstruction[]
                {
                    first,
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld, playStateSceneField),
                    new CodeInstruction(OpCodes.Call, helper)
                });
            return result;
        }

        private static int SingleMatch(int previous, int current, string description)
        {
            if (previous >= 0)
                throw new InvalidOperationException(
                    "Multiple " + description + " instructions matched.");
            return current;
        }

        private static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            return method != null &&
                (instruction.opcode == OpCodes.Call ||
                    instruction.opcode == OpCodes.Callvirt) &&
                Object.Equals(instruction.operand, method);
        }

        public static void DetachScene(object shadowBlobs, object expected)
        {
            if (shadowBlobs == null)
                throw new ArgumentNullException("shadowBlobs");
            if (shadowSceneField == null)
                throw new InvalidOperationException(
                    "ShadowBlobs scene contract has not been initialized.");
            if (Object.ReferenceEquals(shadowSceneField.GetValue(shadowBlobs), expected))
                shadowSceneField.SetValue(shadowBlobs, null);
        }
    }
}
