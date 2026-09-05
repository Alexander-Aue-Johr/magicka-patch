using System.Reflection;

namespace Magicka.InventoryBoxRuntimePatch
{
    internal static class PlayStatePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchSession.Apply(
                targetAssembly,
                PlayStateAddWorldSyncMessageTranspiler.Definition);
        }
    }
}
