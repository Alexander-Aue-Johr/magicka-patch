using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ThunderstormPlayStatePatch
    {
        private const string ThunderstormTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Thunderstorm";

        private static FieldInfo playStateField;
        private static FieldInfo ownerField;
        private static MethodInfo recentPlayStateGetter;
        private static MethodInfo cueStopMethod;

        internal static readonly RuntimePatchDefinition VectorExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderstorm vector play-state release",
                "org.magickacommunitypatch.thunderstorm-vector-play-state-release",
                FindVectorExecute,
                typeof(ThunderstormPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition OwnerExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderstorm owner play-state release",
                "org.magickacommunitypatch.thunderstorm-owner-play-state-release",
                FindOwnerExecute,
                typeof(ThunderstormPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderstorm current update play state",
                "org.magickacommunitypatch.thunderstorm-current-update-state",
                FindUpdate,
                typeof(ThunderstormPlayStatePatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition RemoveDefinition =
            RuntimePatchDefinition.Transpile(
                "Thunderstorm remove reference cleanup",
                "org.magickacommunitypatch.thunderstorm-remove-reference-cleanup",
                FindOnRemove,
                typeof(ThunderstormPlayStatePatch).GetMethod("RemoveTranspiler"));

        private static MethodInfo FindVectorExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, false);
        }

        private static MethodInfo FindOwnerExecute(Assembly targetAssembly)
        {
            return FindPublicExecute(targetAssembly, true);
        }

        private static MethodInfo FindPublicExecute(
            Assembly targetAssembly,
            bool ownerOverload)
        {
            Type thunderstorm;
            Type playState;
            Configure(targetAssembly, out thunderstorm, out playState);
            Type firstParameter = ownerOverload
                ? targetAssembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            MethodInfo method = thunderstorm.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playState },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(
                    thunderstorm.FullName,
                    "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type thunderstorm;
            Type ignoredPlayState;
            Configure(
                targetAssembly,
                out thunderstorm,
                out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = thunderstorm.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    thunderstorm.FullName,
                    "Update");
            return method;
        }

        private static MethodInfo FindOnRemove(Assembly targetAssembly)
        {
            Type thunderstorm;
            Type ignoredPlayState;
            Configure(
                targetAssembly,
                out thunderstorm,
                out ignoredPlayState);
            Type cue = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.Cue");
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");
            cueStopMethod = cue.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { stopOptions },
                null);
            if (cueStopMethod == null ||
                cueStopMethod.ReturnType != typeof(void))
                throw new MissingMethodException(cue.FullName, "Stop");
            MethodInfo method = thunderstorm.GetMethod(
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    thunderstorm.FullName,
                    "OnRemove");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type thunderstorm,
            out Type playState)
        {
            thunderstorm = targetAssembly.GetType(
                ThunderstormTypeName,
                true);
            playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            Type owner = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            playStateField = RequireField(
                thunderstorm,
                "mPlayState",
                playState);
            ownerField = RequireField(thunderstorm, "mOwner", owner);
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
        }

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            RemoveAssignment(result);
            ReplaceReads(result, 1, "Execute");
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            ReplaceReads(result, 9, "Update");
            return result;
        }

        public static IEnumerable<CodeInstruction> RemoveTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConfigureTranspiler();
            List<CodeInstruction> source =
                new List<CodeInstruction>(instructions);
            ReplaceReads(source, 1, "OnRemove");
            List<CodeInstruction> result = new List<CodeInstruction>();
            int stopCalls = 0;
            for (int index = 0; index < source.Count; index++)
            {
                CodeInstruction instruction = source[index];
                result.Add(instruction);
                MethodInfo method = instruction.operand as MethodInfo;
                if (instruction.opcode != OpCodes.Callvirt ||
                    !Object.Equals(method, cueStopMethod))
                    continue;
                stopCalls++;
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                result.Add(new CodeInstruction(OpCodes.Ldnull));
                result.Add(new CodeInstruction(OpCodes.Stfld, ownerField));
            }
            if (stopCalls != 1)
                throw new InvalidOperationException(
                    "Expected one Thunderstorm Cue.Stop call, found " +
                    stopCalls + ".");
            return result;
        }

        private static void RemoveAssignment(List<CodeInstruction> result)
        {
            int assignment = -1;
            int writes = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    writes++;
                if (result[index - 2].opcode == OpCodes.Ldarg_0 &&
                    result[index - 1].opcode == OpCodes.Ldarg_2 &&
                    result[index].opcode == OpCodes.Stfld &&
                    Object.Equals(field, playStateField))
                    assignment = index;
            }
            if (assignment < 0 || writes != 1)
                throw new InvalidOperationException(
                    "Expected one Thunderstorm play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
        }

        private static void ReplaceReads(
            List<CodeInstruction> result,
            int expected,
            string methodName)
        {
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 1].opcode != OpCodes.Ldarg_0 ||
                    result[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                result[index - 1].opcode = OpCodes.Call;
                result[index - 1].operand = recentPlayStateGetter;
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " Thunderstorm " + methodName +
                    " play-state reads, found " + replacements + ".");
        }

        private static void ConfigureTranspiler()
        {
            Type thunderstorm = RuntimeMember.FindLoadedType(
                ThunderstormTypeName);
            Type ignoredThunderstorm;
            Type ignoredPlayState;
            Configure(
                thunderstorm.Assembly,
                out ignoredThunderstorm,
                out ignoredPlayState);
            Type cue = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.Cue");
            Type stopOptions = RuntimeMember.FindLoadedType(
                "Microsoft.Xna.Framework.Audio.AudioStopOptions");
            cueStopMethod = cue.GetMethod(
                "Stop",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { stopOptions },
                null);
            if (cueStopMethod == null)
                throw new MissingMethodException(cue.FullName, "Stop");
        }

        private static FieldInfo RequireField(
            Type type,
            string name,
            Type expectedType)
        {
            FieldInfo field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expectedType)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
