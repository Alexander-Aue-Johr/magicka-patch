using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class FlashScenePatch
    {
        private static FieldInfo legacySceneField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo sceneGetter;
        private static MethodInfo addRenderableMethod;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Flash scene reference release",
                "org.magickacommunitypatch.flash-scene-release",
                FindExecute,
                typeof(FlashScenePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Flash current scene update",
                "org.magickacommunitypatch.flash-current-scene",
                FindUpdate,
                typeof(FlashScenePatch).GetMethod("UpdateTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type flashType;
            Type sceneType;
            Configure(targetAssembly, out flashType, out sceneType);
            MethodInfo method = flashType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { sceneType, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(flashType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type flashType;
            Type sceneType;
            Configure(targetAssembly, out flashType, out sceneType);
            Type dataChannelType = addRenderableMethod.GetParameters()[0].ParameterType;
            MethodInfo method = flashType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { dataChannelType, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(flashType.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type flashType,
            out Type sceneType)
        {
            flashType = targetAssembly.GetType("Magicka.Graphics.Flash", true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacySceneField = flashType.GetField(
                "mScene",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (legacySceneField == null)
                throw new MissingFieldException(flashType.FullName, "mScene");
            sceneType = legacySceneField.FieldType;

            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            PropertyInfo scene = playStateType.GetProperty(
                "Scene",
                BindingFlags.Instance | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            sceneGetter = scene == null ? null : scene.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_RecentPlayState");
            if (sceneGetter == null || sceneGetter.ReturnType != sceneType)
                throw new MissingMethodException(playStateType.FullName, "get_Scene");

            addRenderableMethod = null;
            MethodInfo[] methods = sceneType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "AddRenderableAdditiveObject" &&
                    methods[index].ReturnType == typeof(void) &&
                    parameters.Length == 2)
                {
                    if (addRenderableMethod != null)
                        throw new InvalidOperationException(
                            "Multiple additive render methods matched Flash.Update.");
                    addRenderableMethod = methods[index];
                }
            }
            if (addRenderableMethod == null)
                throw new MissingMethodException(
                    sceneType.FullName,
                    "AddRenderableAdditiveObject");
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int assignment = -1;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode != OpCodes.Stfld ||
                    field == null || field != legacySceneField ||
                    result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_1)
                    continue;
                if (assignment >= 0)
                    throw new InvalidOperationException(
                        "Multiple Flash scene assignments matched.");
                assignment = index;
            }
            if (assignment < 0)
                throw new InvalidOperationException(
                    "Flash scene assignment was not found.");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int receiver = -1;
            for (int index = 1; index + 3 < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                MethodInfo called = result[index + 3].operand as MethodInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    field == null || field != legacySceneField ||
                    result[index + 1].opcode != OpCodes.Ldarg_1 ||
                    result[index + 2].opcode != OpCodes.Ldarg_0 ||
                    !IsCall(result[index + 3].opcode) ||
                    called == null || called != addRenderableMethod)
                    continue;
                if (receiver >= 0)
                    throw new InvalidOperationException(
                        "Multiple Flash render receivers matched.");
                receiver = index;
            }
            if (receiver < 0)
                throw new InvalidOperationException(
                    "Flash render receiver was not found.");

            result[receiver - 1].opcode = OpCodes.Call;
            result[receiver - 1].operand = recentPlayStateGetter;
            result[receiver].opcode = OpCodes.Callvirt;
            result[receiver].operand = sceneGetter;
            return result;
        }

        private static bool IsCall(OpCode opcode)
        {
            return opcode == OpCodes.Call || opcode == OpCodes.Callvirt;
        }
    }
}
