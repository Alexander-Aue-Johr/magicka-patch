using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class AudioManagerStopAllPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "AudioManager disposed cue guard",
                "org.magickacommunitypatch.audio-manager-stop-all",
                FindTarget,
                typeof(AudioManagerStopAllPatch).GetMethod("Transpiler"));

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType("Magicka.Audio.AudioManager", true);
            MethodInfo match = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (int index = 0; index < methods.Length; index++)
            {
                ParameterInfo[] parameters = methods[index].GetParameters();
                if (methods[index].Name == "StopAll" &&
                    methods[index].ReturnType == typeof(void) &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType.FullName ==
                        "Microsoft.Xna.Framework.Audio.AudioStopOptions")
                {
                    if (match != null)
                        throw new InvalidOperationException(
                            "Multiple AudioManager.StopAll targets matched.");
                    match = methods[index];
                }
            }
            if (match == null)
                throw new MissingMethodException(type.FullName, "StopAll");
            return match;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int stoppedCall = FindStoppedCall(result);
            if (stoppedCall == 0 || stoppedCall + 2 >= result.Count ||
                !LoadsLocal(result[stoppedCall - 1].opcode) ||
                !BranchesWhenTrue(result[stoppedCall + 1].opcode))
            {
                throw new InvalidOperationException(
                    "AudioManager.StopAll stopped-cue branch changed shape.");
            }

            MethodInfo stoppedGetter = (MethodInfo)result[stoppedCall].operand;
            PropertyInfo disposedProperty = stoppedGetter.DeclaringType.GetProperty(
                "IsDisposed");
            MethodInfo disposedGetter = disposedProperty == null
                ? null
                : disposedProperty.GetGetMethod();
            if (disposedGetter == null || disposedGetter.ReturnType != typeof(bool))
                throw new MissingMethodException(
                    stoppedGetter.DeclaringType.FullName,
                    "get_IsDisposed");
            object skipTarget = result[stoppedCall + 1].operand;
            int insertAt = stoppedCall + 2;
            CodeInstruction originalBody = result[insertAt];
            CodeInstruction cueLoad = new CodeInstruction(
                result[stoppedCall - 1].opcode,
                result[stoppedCall - 1].operand);
            cueLoad.labels.AddRange(originalBody.labels);
            cueLoad.blocks.AddRange(originalBody.blocks);
            originalBody.labels.Clear();
            originalBody.blocks.Clear();

            result.Insert(insertAt, cueLoad);
            result.Insert(
                insertAt + 1,
                new CodeInstruction(OpCodes.Callvirt, disposedGetter));
            result.Insert(
                insertAt + 2,
                new CodeInstruction(OpCodes.Brtrue, skipTarget));
            return result;
        }

        private static int FindStoppedCall(IList<CodeInstruction> instructions)
        {
            int match = -1;
            for (int index = 0; index < instructions.Count; index++)
            {
                MethodInfo method = instructions[index].operand as MethodInfo;
                if ((instructions[index].opcode == OpCodes.Call ||
                    instructions[index].opcode == OpCodes.Callvirt) &&
                    method != null && method.Name == "get_IsStopped" &&
                    method.DeclaringType.FullName ==
                        "Microsoft.Xna.Framework.Audio.Cue")
                {
                    if (match >= 0)
                        throw new InvalidOperationException(
                            "Multiple Cue.IsStopped calls matched in AudioManager.StopAll.");
                    match = index;
                }
            }
            if (match < 0)
                throw new InvalidOperationException(
                    "Cue.IsStopped was not found in AudioManager.StopAll.");
            return match;
        }

        private static bool LoadsLocal(OpCode opcode)
        {
            return opcode == OpCodes.Ldloc || opcode == OpCodes.Ldloc_S ||
                opcode == OpCodes.Ldloc_0 || opcode == OpCodes.Ldloc_1 ||
                opcode == OpCodes.Ldloc_2 || opcode == OpCodes.Ldloc_3;
        }

        private static bool BranchesWhenTrue(OpCode opcode)
        {
            return opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S;
        }
    }
}
