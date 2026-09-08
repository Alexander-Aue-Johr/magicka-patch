using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class TutorialManagerPlayStatePatch
    {
        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition InitializeDefinition =
            RuntimePatchDefinition.Transpile(
                "TutorialManager play-state release",
                "org.magickacommunitypatch.tutorial-play-state-release",
                FindInitialize,
                typeof(TutorialManagerPlayStatePatch).GetMethod(
                    "InitializeTranspiler"));

        internal static readonly RuntimePatchDefinition ResolutionDefinition =
            RuntimePatchDefinition.Transpile(
                "TutorialManager current play-state resolution update",
                "org.magickacommunitypatch.tutorial-current-play-state-resolution",
                FindUpdateResolution,
                typeof(TutorialManagerPlayStatePatch).GetMethod(
                    "UpdateResolutionTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "TutorialManager current play-state update",
                "org.magickacommunitypatch.tutorial-current-play-state-update",
                FindUpdate,
                typeof(TutorialManagerPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindInitialize(Assembly targetAssembly)
        {
            Type tutorialType;
            Type playStateType;
            Configure(targetAssembly, out tutorialType, out playStateType);
            MethodInfo method = tutorialType.GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { playStateType },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    tutorialType.FullName,
                    "Initialize");
            return method;
        }

        private static MethodInfo FindUpdateResolution(Assembly targetAssembly)
        {
            Type tutorialType;
            Type playStateType;
            Configure(targetAssembly, out tutorialType, out playStateType);
            MethodInfo method = tutorialType.GetMethod(
                "UpdateResolution",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    tutorialType.FullName,
                    "UpdateResolution");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type tutorialType;
            Type playStateType;
            Configure(targetAssembly, out tutorialType, out playStateType);
            Type dataChannelType = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = tutorialType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannelType, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(tutorialType.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type tutorialType,
            out Type playStateType)
        {
            tutorialType = targetAssembly.GetType(
                "Magicka.Graphics.TutorialManager",
                true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = tutorialType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(
                    tutorialType.FullName,
                    "mPlayState");

            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playStateType)
                throw new MissingMethodException(
                    playStateType.FullName,
                    "get_RecentPlayState");
        }

        public static IEnumerable<CodeInstruction> InitializeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int assignment = -1;
            int matches = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_1 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, legacyPlayStateField))
                    continue;
                assignment = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one TutorialManager play-state assignment, found " +
                    matches + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateResolutionTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(
                instructions,
                2,
                "TutorialManager.UpdateResolution");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplacePlayStateReads(
                instructions,
                11,
                "TutorialManager.Update");
        }

        private static IEnumerable<CodeInstruction> ReplacePlayStateReads(
            IEnumerable<CodeInstruction> instructions,
            int expectedMatches,
            string context)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int matches = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, legacyPlayStateField))
                    continue;

                result[index - 1].opcode = OpCodes.Nop;
                result[index - 1].operand = null;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                matches++;
            }
            if (matches != expectedMatches)
                throw new InvalidOperationException(
                    context + " expected " + expectedMatches +
                    " play-state reads, found " + matches + ".");
            return result;
        }
    }
}
