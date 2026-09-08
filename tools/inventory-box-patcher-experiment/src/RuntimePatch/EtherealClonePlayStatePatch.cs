using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EtherealClonePlayStatePatch
    {
        private static FieldInfo legacyPlayStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ExecuteDefinition =
            RuntimePatchDefinition.Transpile(
                "EtherealClone play-state release",
                "org.magickacommunitypatch.ethereal-clone-play-state-release",
                FindExecute,
                typeof(EtherealClonePlayStatePatch).GetMethod(
                    "ExecuteTranspiler"));

        internal static readonly RuntimePatchDefinition SpawnDefinition =
            RuntimePatchDefinition.Transpile(
                "EtherealClone current NavMesh",
                "org.magickacommunitypatch.ethereal-clone-current-nav-mesh",
                FindSpawnClone,
                typeof(EtherealClonePlayStatePatch).GetMethod(
                    "SpawnTranspiler"));

        private static MethodInfo FindExecute(Assembly targetAssembly)
        {
            Type cloneType;
            Type playStateType;
            Configure(targetAssembly, out cloneType, out playStateType);
            Type ownerType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.ISpellCaster",
                true);
            MethodInfo method = cloneType.GetMethod(
                "Execute",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { ownerType, playStateType },
                null);
            if (method == null || method.ReturnType != typeof(bool))
                throw new MissingMethodException(cloneType.FullName, "Execute");
            return method;
        }

        private static MethodInfo FindSpawnClone(Assembly targetAssembly)
        {
            Type cloneType;
            Type playStateType;
            Configure(targetAssembly, out cloneType, out playStateType);
            Type templateType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.CharacterTemplate",
                true);
            MethodInfo method = cloneType.GetMethod(
                "SpawnClone",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly,
                null,
                new Type[] { templateType, typeof(int), typeof(uint) },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(
                    cloneType.FullName,
                    "SpawnClone");
            return method;
        }

        private static void Configure(
            Assembly targetAssembly,
            out Type cloneType,
            out Type playStateType)
        {
            cloneType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.Abilities.SpecialAbilities." +
                    "EtherealClone",
                true);
            playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            legacyPlayStateField = cloneType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            if (legacyPlayStateField == null ||
                legacyPlayStateField.FieldType != playStateType)
                throw new MissingFieldException(cloneType.FullName, "mPlayState");

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
                    "Expected one EtherealClone play-state assignment, found " +
                    matches + ".");

            for (int index = assignment - 2; index <= assignment; index++)
            {
                result[index].opcode = OpCodes.Nop;
                result[index].operand = null;
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> SpawnTranspiler(
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
            if (matches != 1)
                throw new InvalidOperationException(
                    "Expected one EtherealClone play-state read, found " +
                    matches + ".");
            return result;
        }
    }
}
