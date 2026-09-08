using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class TimeWarpPlayStatePatch
    {
        private const string TimeWarpTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TimeWarp";
        private const string TimeWarpStaffTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.TimeWarpStaff";

        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition TimeWarpVectorDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarp vector play-state release",
                "org.magickacommunitypatch.time-warp-vector-play-state-release",
                FindTimeWarpVectorExecute,
                typeof(TimeWarpPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition TimeWarpOwnerDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarp owner play-state release",
                "org.magickacommunitypatch.time-warp-owner-play-state-release",
                FindTimeWarpOwnerExecute,
                typeof(TimeWarpPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition TimeWarpUpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarp current update play state",
                "org.magickacommunitypatch.time-warp-current-update-state",
                FindTimeWarpUpdate,
                typeof(TimeWarpPlayStatePatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition TimeWarpRemoveDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarp current removal play state",
                "org.magickacommunitypatch.time-warp-current-remove-state",
                FindTimeWarpOnRemove,
                typeof(TimeWarpPlayStatePatch).GetMethod("RemoveTranspiler"));

        internal static readonly RuntimePatchDefinition StaffOwnerDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarpStaff play-state release",
                "org.magickacommunitypatch.time-warp-staff-play-state-release",
                FindStaffOwnerExecute,
                typeof(TimeWarpPlayStatePatch).GetMethod("ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition StaffUpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarpStaff current update play state",
                "org.magickacommunitypatch.time-warp-staff-current-update-state",
                FindStaffUpdate,
                typeof(TimeWarpPlayStatePatch).GetMethod("UpdateTranspiler"));

        internal static readonly RuntimePatchDefinition StaffRemoveDefinition =
            RuntimePatchDefinition.Transpile(
                "TimeWarpStaff current removal play state",
                "org.magickacommunitypatch.time-warp-staff-current-remove-state",
                FindStaffOnRemove,
                typeof(TimeWarpPlayStatePatch).GetMethod("RemoveTranspiler"));

        private static MethodInfo FindTimeWarpVectorExecute(Assembly assembly)
        {
            return FindExecute(assembly, TimeWarpTypeName, false);
        }

        private static MethodInfo FindTimeWarpOwnerExecute(Assembly assembly)
        {
            return FindExecute(assembly, TimeWarpTypeName, true);
        }

        private static MethodInfo FindStaffOwnerExecute(Assembly assembly)
        {
            return FindExecute(assembly, TimeWarpStaffTypeName, true);
        }

        private static MethodInfo FindExecute(
            Assembly assembly,
            string typeName,
            bool owner)
        {
            Type effectType;
            Type playStateType;
            Configure(assembly, typeName, out effectType, out playStateType);
            Type firstParameter = owner
                ? assembly.GetType(
                    "Magicka.GameLogic.Entities.ISpellCaster",
                    true)
                : RuntimeMember.FindLoadedType(
                    "Microsoft.Xna.Framework.Vector3");
            MethodInfo execute = effectType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { firstParameter, playStateType },
                null);
            if (execute == null || execute.ReturnType != typeof(bool))
                throw new MissingMethodException(effectType.FullName, "Execute");
            return execute;
        }

        private static MethodInfo FindTimeWarpUpdate(Assembly assembly)
        {
            return FindUpdate(assembly, TimeWarpTypeName);
        }

        private static MethodInfo FindStaffUpdate(Assembly assembly)
        {
            return FindUpdate(assembly, TimeWarpStaffTypeName);
        }

        private static MethodInfo FindUpdate(Assembly assembly, string typeName)
        {
            Type effectType;
            Type ignoredPlayState;
            Configure(assembly, typeName, out effectType, out ignoredPlayState);
            Type dataChannel = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo update = effectType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannel, typeof(float) },
                null);
            if (update == null || update.ReturnType != typeof(void))
                throw new MissingMethodException(effectType.FullName, "Update");
            return update;
        }

        private static MethodInfo FindTimeWarpOnRemove(Assembly assembly)
        {
            return FindOnRemove(assembly, TimeWarpTypeName);
        }

        private static MethodInfo FindStaffOnRemove(Assembly assembly)
        {
            return FindOnRemove(assembly, TimeWarpStaffTypeName);
        }

        private static MethodInfo FindOnRemove(Assembly assembly, string typeName)
        {
            Type effectType;
            Type ignoredPlayState;
            Configure(assembly, typeName, out effectType, out ignoredPlayState);
            MethodInfo onRemove = effectType.GetMethod(
                "OnRemove",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (onRemove == null || onRemove.ReturnType != typeof(void))
                throw new MissingMethodException(effectType.FullName, "OnRemove");
            return onRemove;
        }

        private static void Configure(
            Assembly assembly,
            string typeName,
            out Type effectType,
            out Type playStateType)
        {
            effectType = assembly.GetType(typeName, true);
            playStateType = assembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = effectType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (playStateField == null ||
                playStateField.FieldType != playStateType)
                throw new MissingFieldException(effectType.FullName, "mPlayState");

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

        public static IEnumerable<CodeInstruction> ExecuteTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
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
                    "Expected one TimeWarp play-state assignment, found " +
                    writes + ".");
            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            ReplaceReads(result, 1, "execute");
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            ReplaceReads(result, 4, "update");
            return result;
        }

        public static IEnumerable<CodeInstruction> RemoveTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result =
                new List<CodeInstruction>(instructions);
            ReplaceReads(result, 3, "removal");
            return result;
        }

        private static void ReplaceReads(
            IList<CodeInstruction> instructions,
            int expected,
            string operation)
        {
            int replacements = 0;
            for (int index = 1; index < instructions.Count; index++)
            {
                FieldInfo field = instructions[index].operand as FieldInfo;
                if (instructions[index - 1].opcode != OpCodes.Ldarg_0 ||
                    instructions[index].opcode != OpCodes.Ldfld ||
                    !Object.Equals(field, playStateField))
                    continue;
                instructions[index - 1].opcode = OpCodes.Call;
                instructions[index - 1].operand = recentPlayStateGetter;
                instructions[index].opcode = OpCodes.Nop;
                instructions[index].operand = null;
                replacements++;
            }
            if (replacements != expected)
                throw new InvalidOperationException(
                    "Expected " + expected + " TimeWarp " + operation +
                    " play-state reads, found " + replacements + ".");
        }
    }
}
