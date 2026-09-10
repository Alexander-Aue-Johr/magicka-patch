using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class MagickCameraLifetimePatch
    {
        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition SetPlayStateDefinition =
            RuntimePatchDefinition.Prefix(
                "MagickCamera stored PlayState removal",
                "org.magickacommunitypatch.magick-camera-set-playstate",
                FindSetPlayState,
                target => typeof(MagickCameraLifetimePatch).GetMethod("SkipSetPlayState"));

        internal static readonly RuntimePatchDefinition InfluenceDefinition =
            RuntimePatchDefinition.Transpile(
                "MagickCamera current PlayState influence lookup",
                "org.magickacommunitypatch.magick-camera-current-playstate",
                FindInfluence,
                typeof(MagickCameraLifetimePatch).GetMethod("InfluenceTranspiler"));

        internal static readonly RuntimePatchDefinition DisposeDefinition =
            RuntimePatchDefinition.Prefix(
                "MagickCamera collection teardown",
                "org.magickacommunitypatch.magick-camera-dispose",
                FindDispose,
                target => typeof(MagickCameraLifetimePatch).GetMethod("DisposePrefix"));

        private static MethodInfo FindSetPlayState(Assembly assembly)
        {
            Type camera = assembly.GetType("Magicka.Graphics.MagickCamera", true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            MethodInfo method = camera.GetMethod("SetPlayState",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, new Type[] { playState }, null);
            if (method == null)
                throw new MissingMethodException(camera.FullName, "SetPlayState");
            return method;
        }

        private static MethodInfo FindInfluence(Assembly assembly)
        {
            Type camera = assembly.GetType("Magicka.Graphics.MagickCamera", true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState", true);
            playStateField = camera.GetField("mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo recent = playState.GetProperty("RecentPlayState",
                BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic);
            recentPlayStateGetter = recent == null ? null : recent.GetGetMethod(true);
            MethodInfo method = camera.GetMethod("GetInfluenceVector",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (method == null || playStateField == null ||
                recentPlayStateGetter == null)
                throw new MissingMemberException(camera.FullName,
                    "GetInfluenceVector current PlayState dependencies");
            return method;
        }

        private static MethodInfo FindDispose(Assembly assembly)
        {
            Type camera = assembly.GetType("Magicka.Graphics.MagickCamera", true);
            MethodInfo method = camera.GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            if (method == null)
                throw new MissingMethodException(camera.FullName, "Dispose");
            return method;
        }

        public static bool SkipSetPlayState()
        {
            return false;
        }

        public static IEnumerable<CodeInstruction> InfluenceTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, playStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "MagickCamera PlayState load changed shape.");
                result[index - 1].opcode = OpCodes.Nop;
                result[index - 1].operand = null;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one MagickCamera PlayState read, found " + matches + ".");
            return result;
        }

        public static void DisposePrefix(object __instance)
        {
            if (__instance == null || __instance.GetType().FullName !=
                "Magicka.Graphics.MagickCamera")
                return;
            Clear(__instance, "mNetworkPlayers");
            Clear(__instance, "mPlayers");
            Clear(__instance, "mVisualEffects");
        }

        private static void Clear(object owner, string fieldName)
        {
            object value = RuntimeMember.ReadField(owner, fieldName);
            IDictionary dictionary = value as IDictionary;
            if (dictionary != null)
            {
                dictionary.Clear();
                return;
            }
            IList list = value as IList;
            if (list != null)
                list.Clear();
        }
    }
}
