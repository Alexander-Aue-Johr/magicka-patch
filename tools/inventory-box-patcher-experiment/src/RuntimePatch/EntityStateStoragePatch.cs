using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    public static class EntityStateStoragePatch
    {
        private static FieldInfo playStateField;
        private static MethodInfo recentPlayStateGetter;

        internal static readonly RuntimePatchDefinition ConstructorDefinition =
            RuntimePatchDefinition.ConstructorPostfix(
                "EntityStateStorage constructor play-state release",
                "org.magickacommunitypatch.entity-state-storage-constructor",
                EntityStateStorageTargetMethods.FindConstructorIn,
                typeof(EntityStateStoragePatch).GetMethod("ConstructorPostfix"));

        internal static readonly RuntimePatchDefinition RestoreDefinition =
            RuntimePatchDefinition.Transpile(
                "EntityStateStorage current play-state restore",
                "org.magickacommunitypatch.entity-state-storage-restore",
                EntityStateStorageTargetMethods.FindRestoreIn,
                typeof(EntityStateStoragePatch).GetMethod("Transpiler"));

        internal static void Configure(Assembly targetAssembly)
        {
            Type storageType = targetAssembly.GetType(
                "Magicka.GameLogic.Entities.EntityStateStorage",
                true);
            Type playStateType = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            playStateField = storageType.GetField(
                "mPlayState",
                BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo recentPlayState = playStateType.GetProperty(
                "RecentPlayState",
                BindingFlags.Static | BindingFlags.Public);
            recentPlayStateGetter = recentPlayState == null
                ? null
                : recentPlayState.GetGetMethod();
            if (playStateField == null || recentPlayStateGetter == null)
                throw new MissingMemberException(
                    "EntityStateStorage play-state members are incomplete.");
        }

        public static void ConstructorPostfix(object __instance)
        {
            playStateField.SetValue(__instance, null);
        }

        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = new List<CodeInstruction>(instructions);
            int replacements = 0;
            for (int index = 1; index < result.Count; index++)
            {
                if (result[index].opcode != OpCodes.Ldfld ||
                    !Equals(result[index].operand, playStateField))
                    continue;
                if (result[index - 1].opcode != OpCodes.Ldarg_0)
                    throw new InvalidOperationException(
                        "EntityStateStorage.mPlayState is no longer loaded from this.");

                result[index - 1].opcode = OpCodes.Nop;
                result[index - 1].operand = null;
                result[index].opcode = OpCodes.Call;
                result[index].operand = recentPlayStateGetter;
                replacements++;
            }
            if (replacements != 2)
                throw new InvalidOperationException(
                    "Expected two EntityStateStorage.mPlayState reads, found " +
                    replacements + ".");
            return result;
        }
    }
}
