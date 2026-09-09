using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Magicka.CommunityPatch.Runtime
{
    internal delegate object CurrentGameScenePlayStateGetter();

    public static class GameScenePlayStatePatch
    {
        private static CurrentGameScenePlayStateGetter getCurrentPlayState;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "GameScene current play-state getter",
                "org.magickacommunitypatch.game-scene-play-state",
                FindGetter,
                CreatePrefix);

        private static MethodInfo FindGetter(Assembly assembly)
        {
            Type gameScene = assembly.GetType(
                "Magicka.Levels.GameScene",
                true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            PropertyInfo scenePlayState = gameScene.GetProperty(
                "PlayState",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            MethodInfo target = scenePlayState == null
                ? null
                : scenePlayState.GetGetMethod(true);
            if (target == null || target.ReturnType != playState ||
                target.GetParameters().Length != 0)
                throw new MissingMethodException(
                    gameScene.FullName,
                    "get_PlayState");

            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            MethodInfo recentGetter = recent == null
                ? null
                : recent.GetGetMethod(true);
            if (recentGetter == null || recentGetter.ReturnType != playState ||
                recentGetter.GetParameters().Length != 0)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
            getCurrentPlayState = CreateCurrentGetter(recentGetter);
            return target;
        }

        public static object CurrentPlayState()
        {
            return getCurrentPlayState();
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            Type adapter = typeof(GameScenePlayStatePrefix<>).MakeGenericType(
                target.ReturnType);
            return adapter.GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
        }

        private static CurrentGameScenePlayStateGetter CreateCurrentGetter(
            MethodInfo getter)
        {
            DynamicMethod method = new DynamicMethod(
                "GetCurrentGameScenePlayState",
                typeof(object),
                Type.EmptyTypes,
                typeof(GameScenePlayStatePatch),
                true);
            ILGenerator generator = method.GetILGenerator();
            generator.EmitCall(OpCodes.Call, getter, null);
            generator.Emit(OpCodes.Ret);
            return (CurrentGameScenePlayStateGetter)method.CreateDelegate(
                typeof(CurrentGameScenePlayStateGetter));
        }
    }

    public static class GameScenePlayStatePrefix<TPlayState>
    {
        public static bool Prefix(ref TPlayState __result)
        {
            __result = (TPlayState)GameScenePlayStatePatch.CurrentPlayState();
            return false;
        }
    }
}
