using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class BossHealthBarScenePatch
    {
        private static FieldInfo sceneField;
        private static PropertyInfo recentPlayStateProperty;
        private static PropertyInfo playStateSceneProperty;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorPostfix(
                "BossHealthBar constructor scene release",
                "org.magickacommunitypatch.boss-health-bar-constructor",
                BossHealthBarTargetMethods.FindConstructorIn,
                typeof(BossHealthBarScenePatch).GetMethod("ConstructorPostfix"));

        internal static readonly RuntimePatchDefinition GetterDefinition =
            RuntimePatchDefinition.Prefix(
                "BossHealthBar current scene getter",
                "org.magickacommunitypatch.boss-health-bar-getter",
                BossHealthBarTargetMethods.FindSceneGetterIn,
                CreateGetterPrefix);

        internal static readonly RuntimePatchDefinition SetterDefinition =
            RuntimePatchDefinition.Prefix(
                "BossHealthBar legacy scene setter release",
                "org.magickacommunitypatch.boss-health-bar-setter",
                BossHealthBarTargetMethods.FindSceneSetterIn,
                CreateSetterPrefix);

        internal static void Configure(Assembly targetAssembly)
        {
            Type healthBarType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.BossHealthBar",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            sceneField = healthBarType.GetField(
                "mScene",
                BindingFlags.Instance | BindingFlags.NonPublic);
            recentPlayStateProperty = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            playStateSceneProperty = playStateType.GetProperty(
                "Scene",
                BindingFlags.Instance | BindingFlags.Public);
            if (sceneField == null ||
                recentPlayStateProperty == null ||
                playStateSceneProperty == null)
                throw new MissingMemberException(
                    "BossHealthBar scene lifetime members are incomplete.");
        }

        public static void ConstructorPostfix(object __instance)
        {
            ClearLegacyScene(__instance);
        }

        public static bool SetterPrefix(object __instance)
        {
            ClearLegacyScene(__instance);
            return false;
        }

        public static object CurrentScene()
        {
            object playState = recentPlayStateProperty.GetValue(null, null);
            return playStateSceneProperty.GetValue(playState, null);
        }

        private static void ClearLegacyScene(object instance)
        {
            sceneField.SetValue(instance, null);
        }

        private static MethodInfo CreateSetterPrefix(MethodInfo target)
        {
            return typeof(BossHealthBarScenePatch).GetMethod("SetterPrefix");
        }

        private static MethodInfo CreateGetterPrefix(MethodInfo target)
        {
            Type adapterType = typeof(BossHealthBarSceneGetterPrefix<>).MakeGenericType(
                target.ReturnType);
            return adapterType.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
        }
    }

    public static class BossHealthBarSceneGetterPrefix<TScene>
    {
        public static bool Prefix(ref TScene __result)
        {
            __result = (TScene)BossHealthBarScenePatch.CurrentScene();
            return false;
        }
    }
}
