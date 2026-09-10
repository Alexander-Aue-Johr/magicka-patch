using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class GameSceneContentUnloadPatch
    {
        private const BindingFlags Members = BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo modelField;
        private static MethodInfo gameInstanceGetter;
        private static MethodInfo disableRendering;
        private static MethodInfo renderingEnabledGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "GameScene render-safe content unload",
                "org.magickacommunitypatch.game-scene-content-unload",
                FindTarget,
                typeof(GameSceneContentUnloadPatch).GetMethod("Transpiler"));

        internal static bool IsAvailableIn(Assembly assembly)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", false);
            Type game = assembly.GetType("Magicka.Game", false);
            return scene != null && game != null &&
                scene.GetMethod("UnloadContent", Members, null,
                    Type.EmptyTypes, null) != null &&
                scene.GetField("mModel", Members) != null &&
                game.GetMethod("DisableRendering", Members, null,
                    Type.EmptyTypes, null) != null &&
                game.GetProperty("RenderingEnabled", Members) != null;
        }

        private static MethodInfo FindTarget(Assembly assembly)
        {
            Type scene = assembly.GetType("Magicka.Levels.GameScene", true);
            Type game = assembly.GetType("Magicka.Game", true);
            modelField = scene.GetField("mModel", Members);
            if (modelField == null)
                throw new MissingFieldException(scene.FullName, "mModel");
            PropertyInfo instance = game.GetProperty("Instance", Members);
            gameInstanceGetter = instance == null ? null : instance.GetGetMethod(true);
            disableRendering = game.GetMethod(
                "DisableRendering", Members, null, Type.EmptyTypes, null);
            PropertyInfo rendering = game.GetProperty("RenderingEnabled", Members);
            renderingEnabledGetter = rendering == null
                ? null : rendering.GetGetMethod(true);
            if (gameInstanceGetter == null || disableRendering == null ||
                renderingEnabledGetter == null)
                throw new MissingMethodException(game.FullName,
                    "Instance/DisableRendering/RenderingEnabled");
            MethodInfo target = scene.GetMethod(
                "UnloadContent", Members, null, Type.EmptyTypes, null);
            if (target == null)
                throw new MissingMethodException(scene.FullName, "UnloadContent");
            return target;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            result.Insert(0, new CodeInstruction(OpCodes.Ldarg_0));
            result.Insert(1, new CodeInstruction(OpCodes.Call,
                typeof(GameSceneContentUnloadPatch).GetMethod("PrepareUnload")));
            return result;
        }

        public static void PrepareUnload(object scene)
        {
            object game = gameInstanceGetter.Invoke(null, null);
            disableRendering.Invoke(game, null);
            while ((bool)renderingEnabledGetter.Invoke(game, null))
                Thread.Sleep(16);
            object model = modelField.GetValue(scene);
            IDisposable disposable = model as IDisposable;
            if (disposable != null)
                disposable.Dispose();
            modelField.SetValue(scene, null);
        }
    }
}
