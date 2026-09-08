using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class GreaseTrailPlayStatePatch
    {
        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "GreaseTrail play-state release",
                "org.magickacommunitypatch.grease-trail-play-state-release",
                FindExecute,
                typeof(GreaseTrailPlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition UpdateDefinition =
            RuntimePatchDefinition.Transpile(
                "GreaseTrail current play state",
                "org.magickacommunitypatch.grease-trail-current-play-state",
                FindUpdate,
                typeof(GreaseTrailPlayStatePatch).GetMethod(
                    "UpdateTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type trailType;
            Type playStateType;
            Configure(targetAssembly, out trailType, out playStateType);
            Type ownerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo method = trailType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { ownerType, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(trailType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindUpdate(Assembly targetAssembly)
        {
            Type trailType;
            Type playStateType;
            Configure(targetAssembly, out trailType, out playStateType);
            Type dataChannelType = RuntimeMember.FindLoadedType(
                "PolygonHead.DataChannel");
            MethodInfo method = trailType.GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { dataChannelType, typeof(float) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(trailType.FullName, "Update");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type trailType,
            out Type playStateType)
        {
            trailType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                    "GreaseTrail",
                true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = trailType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(trailType.FullName, "mPlayState");

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
            int matches = 0;
            for (int index = 2; index < result.Count; index++)
            {
                FieldInfo field = result[index].operand as FieldInfo;
                if (result[index - 2].opcode != OpCodes.Ldarg_0 ||
                    result[index - 1].opcode != OpCodes.Ldarg_2 ||
                    result[index].opcode != OpCodes.Stfld ||
                    !Object.Equals(field, legacyPlayStateField))
                    continue;
                assignment = index;
                matches++;
            }
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one GreaseTrail play-state assignment, found " +
                    matches + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> UpdateTranspiler(
            IEnumerable<CodeInstruction> instructions)
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
            if (matches != 2)
                throw new InvalidOperationException(
                    "Expected two GreaseTrail play-state reads, found " +
                    matches + ".");
            return result;
        }
    }
}
