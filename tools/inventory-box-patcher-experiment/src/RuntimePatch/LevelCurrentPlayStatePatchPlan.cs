using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class LevelCurrentPlayStatePatchPlan
    {
        private static readonly RuntimePatchDefinition[] Definitions =
            new RuntimePatchDefinition[]
            {
                LevelCurrentPlayStatePatch.StateDefinition,
                LevelCurrentPlayStatePatch.GetterDefinition,
                LevelCurrentPlayStatePatch.UpdateDefinition,
                LevelCurrentPlayStatePatch.ChangeDefinition,
                LevelCurrentPlayStatePatch.ClearDefinition
            };

        internal static void ApplyTo(Assembly assembly)
        {
            for (int index = 0; index < Definitions.Length; index++)
                RuntimePatchSession.Apply(assembly, Definitions[index]);
        }
    }
}
