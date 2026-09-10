using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PlayStateExitRenderingPatch
    {
        private const BindingFlags Members =
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "PlayState render-safe exit task",
                "org.magickacommunitypatch.play-state-render-safe-exit",
                FindTarget,
                typeof(PlayStateExitRenderingPatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", false);
            return playState != null && FindTargetOrNull(playState) != null;
        }

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            MethodInfo target = FindTargetOrNull(playState);
            if (target == null)
                throw new MissingMethodException(playState.FullName, "<OnExit>b__*");
            return target;
        }

        private static MethodInfo FindTargetOrNull(Type playState)
        {
            MethodInfo[] methods = playState.GetMethods(Members);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].IsStatic &&
                    methods[index].Name.StartsWith("<OnExit>b__") &&
                    methods[index].GetParameters().Length == 0 &&
                    methods[index].ReturnType == typeof(void))
                    return methods[index];
            return null;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int returnIndex = -1;
            int returns = 0;
            for (int index = 0; index < result.Count; index++)
                if (result[index].opcode == OpCodes.Ret)
                {
                    returnIndex = index;
                    returns++;
                }
            if (returns != 1)
                throw new InvalidOperationException(
                    "PlayState exit task must have one return.");

            result.Insert(0, new CodeInstruction(
                OpCodes.Call,
                typeof(LevelSceneTransitionPatch).GetMethod(
                    "AwaitDisabledRendering")));
            result.Insert(returnIndex + 1, new CodeInstruction(
                OpCodes.Call,
                typeof(LevelSceneTransitionPatch).GetMethod(
                    "EnableRendering")));
            return result;
        }
    }
}
