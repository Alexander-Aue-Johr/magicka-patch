using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AmbientAudioLocatorPatch
    {
        private static MethodInfo locatorUpdate;
        private static Type locatorType;
        private static Action<object, object> updateLocator;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "Ambient audio invalid locator recovery",
                "org.magickacommunitypatch.ambient-audio-locator",
                FindGameSceneUpdate,
                typeof(AmbientAudioLocatorPatch).GetMethod("Transpiler"));

        private static MethodInfo FindGameSceneUpdate(Assembly targetAssembly)
        {
            Type sceneType = targetAssembly.GetType(
                "Magicka.Levels.GameScene",
                true);
            locatorType = targetAssembly.GetType(
                "Magicka.Levels.AudioLocator",
                true);
            if (!locatorType.IsValueType)
                throw new InvalidOperationException(
                    "AudioLocator must remain a value type.");
            locatorUpdate = locatorType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                new Type[] { sceneType },
                null);
            if (locatorUpdate == null || locatorUpdate.ReturnType != typeof(void))
                throw new MissingMethodException(locatorType.FullName, "Update");
            updateLocator = BuildUpdateAdapter(
                locatorType,
                sceneType,
                locatorUpdate);

            MethodInfo[] matches = Array.FindAll(
                sceneType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.DeclaredOnly),
                method => IsGameSceneUpdate(method));
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one GameScene.Update method, found " +
                    matches.Length + ".");
            return matches[0];
        }

        private static bool IsGameSceneUpdate(MethodInfo method)
        {
            if (method.Name != "Update" || method.ReturnType != typeof(void))
                return false;
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 2 &&
                parameters[0].ParameterType.FullName ==
                    "PolygonHead.DataChannel" &&
                parameters[1].ParameterType == typeof(float);
        }

        private static Action<object, object> BuildUpdateAdapter(
            Type audioLocator,
            Type gameScene,
            MethodInfo update)
        {
            DynamicMethod adapter = new DynamicMethod(
                "AmbientAudioLocatorUpdateAdapter",
                typeof(void),
                new Type[] { typeof(object), typeof(object) },
                typeof(AmbientAudioLocatorPatch),
                true);
            ILGenerator il = adapter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox, audioLocator);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, gameScene);
            il.Emit(OpCodes.Call, update);
            il.Emit(OpCodes.Ret);
            return (Action<object, object>)adapter.CreateDelegate(
                typeof(Action<object, object>));
        }

        public static bool TryUpdate(object locator, object scene)
        {
            try
            {
                updateLocator(locator, scene);
                return true;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int updateCall = FindSingleCall(result, locatorUpdate);
            if (updateCall < 2 || result[updateCall - 1].opcode != OpCodes.Ldarg_0 ||
                !IsLocalAddressLoad(result[updateCall - 2]))
                throw new InvalidOperationException(
                    "AudioLocator.Update arguments have an unexpected shape.");

            object cleanupLabel = FindCleanupLabel(result, updateCall);
            if (cleanupLabel == null)
                throw new InvalidOperationException(
                    "Ambient audio cleanup branch was not found.");

            CodeInstruction locatorLoad = result[updateCall - 2];
            locatorLoad.opcode = locatorLoad.opcode == OpCodes.Ldloca
                ? OpCodes.Ldloc
                : OpCodes.Ldloc_S;
            result.Insert(
                updateCall - 1,
                new CodeInstruction(OpCodes.Box, locatorType));
            updateCall++;
            result[updateCall].opcode = OpCodes.Call;
            result[updateCall].operand =
                typeof(AmbientAudioLocatorPatch).GetMethod("TryUpdate");
            result.Insert(
                updateCall + 1,
                new CodeInstruction(OpCodes.Brfalse, cleanupLabel));
            return result;
        }

        private static int FindSingleCall(
            IList<CodeInstruction> instructions,
            MethodInfo method)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                if ((instructions[index].opcode != OpCodes.Call &&
                        instructions[index].opcode != OpCodes.Callvirt) ||
                    !Object.Equals(instructions[index].operand, method))
                    continue;
                if (match >= 0)
                    throw new InvalidOperationException(
                        "Multiple AudioLocator.Update calls matched.");
                match = index;
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "AudioLocator.Update call was not found.");
            return match;
        }

        private static object FindCleanupLabel(
            IList<CodeInstruction> instructions,
            int updateCall)
        {
            for (int index = updateCall + 1;
                index + 1 < instructions.Count && index < updateCall + 24;
                index++)
            {
                MethodInfo called = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                        instructions[index].opcode == OpCodes.Callvirt) &&
                    called != null &&
                    called.Name == "get_IsStopped" &&
                    (instructions[index + 1].opcode == OpCodes.Brtrue ||
                        instructions[index + 1].opcode == OpCodes.Brtrue_S))
                    return instructions[index + 1].operand;
            }
            return null;
        }

        private static bool IsLocalAddressLoad(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldloca ||
                instruction.opcode == OpCodes.Ldloca_S;
        }
    }
}
