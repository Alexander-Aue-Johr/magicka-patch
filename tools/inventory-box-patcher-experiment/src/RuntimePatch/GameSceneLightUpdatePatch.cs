using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSceneLightUpdatePatch
    {
        private static FieldInfo capturedPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "GameScene current light-update play state",
                "org.magickacommunitypatch.game-scene-light-update",
                FindDestroy,
                typeof(GameSceneLightUpdatePatch).GetMethod("Transpiler"));

        private static MethodInfo FindDestroy(Assembly assembly)
        {
            Type gameScene = assembly.GetType("Magicka.Levels.GameScene", true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            capturedPlayStateField = gameScene.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (capturedPlayStateField == null ||
                capturedPlayStateField.FieldType != playState)
                throw new MissingFieldException(gameScene.FullName, "mPlayState");
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod(true);
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState ||
                recentPlayStateGetter.GetParameters().Length != 0)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");

            MethodInfo destroy = gameScene.GetMethod(
                "Destroy",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { typeof(bool) },
                null);
            if (destroy == null || destroy.ReturnType != typeof(void))
                throw new MissingMethodException(gameScene.FullName, "Destroy");
            return destroy;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int match = -1;
            int matches = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, capturedPlayStateField))
                    continue;
                match = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one GameScene.Destroy mPlayState load, found " +
                    matches + ".");

            result[match].opcode = OpCodes.Pop;
            result[match].operand = null;
            result.Insert(
                match + 1,
                new CodeInstruction(OpCodes.Call, recentPlayStateGetter));
            return result;
        }
    }
}
