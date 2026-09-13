using System.Reflection;
using System.Threading;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class LevelSceneTransitionPatchPlan
    {
        private static int applied;

        internal static void ApplyDeferred()
        {
            if (Interlocked.CompareExchange(ref applied, 1, 0) != 0)
                return;
            ApplyTo(Assembly.GetEntryAssembly());
        }

        internal static void ApplyTo(Assembly assembly)
        {
            if (assembly.GetName().Version.Minor < 10)
                return;
            RuntimePatchSession.Apply(
                assembly, LevelSceneTransitionPatch.GoToDefinition);
            RuntimePatchSession.Apply(
                assembly, LevelSceneTransitionPatch.FinishDefinition);
            RuntimePatchSession.Apply(
                assembly, LevelSceneTransitionPatch.ChangeDefinition);
        }
    }
}
