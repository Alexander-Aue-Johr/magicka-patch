using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class GenericHealthBarScenePatch
    {
        private static FieldInfo legacySceneField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo sceneGetter;

        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Transpile(
                "GenericHealthBar current scene",
                "org.magickacommunitypatch.generic-health-bar-current-scene",
                FindUpdate,
                typeof(GenericHealthBarScenePatch).GetMethod("Transpiler"));

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type healthBarType = targetAssembly.GetType(
                "Magicka.GameLogic.UI.GenericHealthBar",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacySceneField = healthBarType.GetField(
                "mScene",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacySceneField == null)
                throw new MissingFieldException(healthBarType.FullName, "mScene");

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
            if (sceneGetter == null ||
                sceneGetter.ReturnType != legacySceneField.FieldType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_Scene");

            Type dataChannelType = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo update = healthBarType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannelType, typeof(float) },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(healthBarType.FullName, "Update");
            return update;
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int receiver = -1;
            int matches = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, legacySceneField))
                    continue;
                receiver = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one GenericHealthBar scene read, found " +
                    matches + ".");

            result[receiver - 1].opcode = OpCodes.Call;
            result[receiver - 1].operand = recentPlayStateGetter;
            result[receiver].opcode = OpCodes.Callvirt;
            result[receiver].operand = sceneGetter;
            return result;
        }
    }
}
