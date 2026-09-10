using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class LevelCurrentPlayStatePatch
    {
        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition StateDefinition =
            RuntimePatchDefinition.Transpile(
                "Level state restore current play state",
                "org.magickacommunitypatch.level-state-current-play-state",
                FindStateApply,
                typeof(LevelCurrentPlayStatePatch).GetMethod(
                    "StateTranspiler"));

        internal static readonly RuntimePatchDefinition GetterDefinition =
            RuntimePatchDefinition.Transpile(
                "Level current play-state getter",
                "org.magickacommunitypatch.level-current-play-state-getter",
                FindPlayStateGetter,
                typeof(LevelCurrentPlayStatePatch).GetMethod(
                    "GetterTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Level update current play state",
                "org.magickacommunitypatch.level-update-current-play-state",
                FindUpdate,
                typeof(LevelCurrentPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition ChangeDefinition =
            RuntimePatchDefinition.Transpile(
                "Level scene change current play state",
                "org.magickacommunitypatch.level-change-current-play-state",
                FindChangeScene,
                typeof(LevelCurrentPlayStatePatch).GetMethod(
                    "ChangeTranspiler"));

        internal static readonly RuntimePatchDefinition ClearDefinition =
            RuntimePatchDefinition.Transpile(
                "Level transition clear current play state",
                "org.magickacommunitypatch.level-clear-current-play-state",
                FindClearTransition,
                typeof(LevelCurrentPlayStatePatch).GetMethod(
                    "ClearTranspiler"));

        private static MethodInfo FindStateApply(Assembly assembly)
        {
            Type level = Configure(assembly);
            Type state = level.GetNestedType("State", InstanceMembers);
            if (state == null)
                throw new TypeLoadException(level.FullName + "+State");
            Type list = typeof(List<>).MakeGenericType(typeof(int));
            Type callback = typeof(Action<>).MakeGenericType(typeof(float));
            return RequireMethod(
                state,
                "ApplyState",
                new Type[] { list, callback },
                typeof(void));
        }

        private static MethodInfo FindPlayStateGetter(Assembly assembly)
        {
            Type level = Configure(assembly);
            MethodInfo getter = level.GetMethod(
                "get_PlayState",
                InstanceMembers,
                null,
                Type.EmptyTypes,
                null);
            if (getter == null || getter.ReturnType != playStateField.FieldType)
                throw new MissingMethodException(level.FullName, "get_PlayState");
            return getter;
        }

        private static MethodInfo FindUpdate(Assembly assembly)
        {
            Type level = Configure(assembly);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            return RequireMethod(
                level,
                "Update",
                new Type[] { dataChannel, typeof(float) },
                typeof(void));
        }

        private static MethodInfo FindChangeScene(Assembly assembly)
        {
            Type level = Configure(assembly);
            return RequireMethod(
                level,
                "ChangeScene",
                Type.EmptyTypes,
                typeof(void));
        }

        private static MethodInfo FindClearTransition(Assembly assembly)
        {
            Type level = Configure(assembly);
            return RequireMethod(
                level,
                "ClearTransition",
                Type.EmptyTypes,
                typeof(void));
        }

        private static Type Configure(Assembly assembly)
        {
            Type level = assembly.GetType("Magicka.Levels.Level", true);
            Type playState = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = level.GetField("mPlayState", InstanceMembers);
            if (playStateField == null || playStateField.FieldType != playState)
                throw new MissingFieldException(level.FullName, "mPlayState");
            PropertyInfo recent = playState.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recent == null
                ? null
                : recent.GetGetMethod();
            if (recentPlayStateGetter == null ||
                recentPlayStateGetter.ReturnType != playState)
                throw new MissingMethodException(
                    playState.FullName,
                    "get_RecentPlayState");
            return level;
        }

        public static IEnumerable<CodeInstruction> StateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "Level.State.ApplyState");
        }

        public static IEnumerable<CodeInstruction> GetterTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "Level.get_PlayState");
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 2, "Level.Update");
        }

        public static IEnumerable<CodeInstruction> ChangeTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 5, "Level.ChangeScene");
        }

        public static IEnumerable<CodeInstruction> ClearTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceReads(instructions, 1, "Level.ClearTransition");
        }

        private static IEnumerable<CodeInstruction> ReplaceReads(
            IEnumerable<CodeInstruction> instructions,
            int expected,
            string target)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(result[index].operand, playStateField))
                    continue;
                result[index].opcode = OpCodes.Pop;
                result[index].operand = null;
                result.Insert(
                    ++index,
                    new CodeInstruction(OpCodes.Call, recentPlayStateGetter));
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    target + " expected " + expected +
                    " stored play-state reads, found " + replacements + ".");
            return result;
        }

        private static MethodInfo RequireMethod(
            Type type,
            string name,
            Type[] parameters,
            Type returnType)
        {
            MethodInfo method = type.GetMethod(
                name,
                InstanceMembers,
                null,
                parameters,
                null);
            if (method == null || method.ReturnType != returnType)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
