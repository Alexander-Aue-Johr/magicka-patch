using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class LevelSceneTransitionPatchPlan
    {
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
