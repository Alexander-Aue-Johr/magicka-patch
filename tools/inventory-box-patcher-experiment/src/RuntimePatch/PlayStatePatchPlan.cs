using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PlayStatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (!PlayStateTargetMethods.IsAvailableIn(targetAssembly))
            {
                RuntimePatchAudit.WriteNotApplicable(
                    PlayStateAddWorldSyncMessagePatch.Definition,
                    "PlayState.AddWorldSyncMessage is not present in this Magicka version.");
                return;
            }

            RuntimePatchSession.Apply(
                targetAssembly,
                PlayStateAddWorldSyncMessagePatch.Definition);
        }
    }
}
