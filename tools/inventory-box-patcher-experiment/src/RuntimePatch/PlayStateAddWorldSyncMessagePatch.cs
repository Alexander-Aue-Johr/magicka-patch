using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class PlayStateAddWorldSyncMessagePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "PlayState SpawnNPC WorldSync guard",
                "org.magickacommunitypatch.playstate-world-sync-guard-experiment",
                PlayStateTargetMethods.FindAddWorldSyncMessage,
                CreateHarmonyPrefix);

        public static bool Prefix(object __instance, object iMessage)
        {
            object messageType = RuntimeMember.ReadField(iMessage, "MessageType");
            object triggerMessage = RuntimeMember.ReadField(iMessage, "TriggerMessage");
            object actionType = RuntimeMember.ReadField(triggerMessage, "ActionType");

            if (messageType.ToString() != "Message" || actionType.ToString() != "SpawnNPC")
                return true;

            int handle = Convert.ToInt32(RuntimeMember.ReadField(triggerMessage, "Handle"));
            return Magicka.CommunityPatch.NetworkEntityHandleGuard.IsUsableWorldSyncSpawnNpc(
                handle,
                __instance);
        }

        private static MethodInfo CreateHarmonyPrefix(MethodInfo target)
        {
            return ExactPrefixAdapter.Create(target);
        }
    }

    internal static class PlayStateTargetMethods
    {
        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                false);
            return playState != null && FindCandidates(playState).Length == 1;
        }

        internal static MethodInfo FindAddWorldSyncMessage(Assembly targetAssembly)
        {
            Type playState = targetAssembly.GetType(
                "Magicka.GameLogic.GameStates.PlayState",
                true);
            MethodInfo[] matches = FindCandidates(playState);
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one PlayState.AddWorldSyncMessage method, found " + matches.Length + ".");
            return matches[0];
        }

        private static MethodInfo[] FindCandidates(Type playState)
        {
            return Array.FindAll(
                playState.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "AddWorldSyncMessage" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.FullName == "Magicka.Network.WorldSyncMessage");
        }
    }
}
