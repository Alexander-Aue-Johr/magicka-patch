using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PlayStatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            if (PlayStateExitRenderingPatch.IsAvailableIn(targetAssembly))
                RuntimePatchSession.Apply(
                    targetAssembly,
                    PlayStateExitRenderingPatch.Definition);
            else
                RuntimePatchAudit.WriteNotApplicable(
                    PlayStateExitRenderingPatch.Definition,
                    "The PlayState.OnExit load task is not present in this Magicka version.");

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
