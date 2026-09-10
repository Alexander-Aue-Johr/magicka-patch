using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PlayStateCheckpointSendPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        private static MethodInfo sendRaw;
        private static DynamicMethod safeSendRaw;
        private static FieldInfo checkpointStream;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "PlayState empty checkpoint payload",
                "org.magickacommunitypatch.play-state-empty-checkpoint",
                FindTarget,
                typeof(PlayStateCheckpointSendPatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", false);
            return playState != null && playState.GetMethod(
                "Initialize", Members, null, Type.EmptyTypes, null) != null;
        }

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            MethodInfo target = playState.GetMethod(
                "Initialize", Members, null, Type.EmptyTypes, null);
            if (target == null)
                throw new MissingMethodException(playState.FullName, "Initialize");
            checkpointStream = playState.GetField("mCheckpointStream", Members);
            if (checkpointStream == null)
                throw new MissingFieldException(playState.FullName, "mCheckpointStream");

            Type networkInterface = assembly.GetType(
                "Magicka.Network.NetworkInterface", true);
            MethodInfo[] methods = networkInterface.GetMethods(Members);
            sendRaw = null;
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "SendRaw" &&
                    parameters.Length == 3 &&
                    parameters[1].ParameterType.IsPointer &&
                    parameters[2].ParameterType == typeof(int))
                {
                    sendRaw = methods[index];
                    break;
                }
            }
            if (sendRaw == null)
                throw new MissingMethodException(networkInterface.FullName, "SendRaw/3");
            safeSendRaw = BuildForwarder(sendRaw);
            return target;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 0; index < result.Count; index++)
                if ((result[index].opcode == OpCodes.Call ||
                        result[index].opcode == OpCodes.Callvirt) &&
                    Object.Equals(result[index].operand, sendRaw) &&
                    ReferencesCheckpointStream(result, index))
                {
                    result[index].opcode = OpCodes.Call;
                    result[index].operand = safeSendRaw;
                    replacements++;
                }
            if (replacements != 1)
                throw new InvalidOperationException(
                    "Expected one checkpoint SendRaw call, found " + replacements + ".");
            return result;
        }

        private static bool ReferencesCheckpointStream(
            IList<CodeInstruction> body, int callIndex)
        {
            int start = Math.Max(0, callIndex - 12);
            for (int index = callIndex - 1; index >= start; index--)
                if (body[index].opcode == OpCodes.Ldfld &&
                    Object.Equals(body[index].operand, checkpointStream))
                    return true;
            return false;
        }

        private static DynamicMethod BuildForwarder(MethodInfo target)
        {
            ParameterInfo[] parameters = target.GetParameters();
            Type[] signature = new Type[]
            {
                target.DeclaringType,
                parameters[0].ParameterType,
                parameters[1].ParameterType,
                parameters[2].ParameterType
            };
            DynamicMethod method = new DynamicMethod(
                "SendCheckpointWithNullEmptyPointer",
                typeof(void), signature,
                typeof(PlayStateCheckpointSendPatch), true);
            ILGenerator il = method.GetILGenerator();
            Label nonEmpty = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Brtrue_S, nonEmpty);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, target);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(nonEmpty);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Callvirt, target);
            il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
