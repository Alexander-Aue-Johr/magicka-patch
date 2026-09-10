using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class MagickCameraLifetimePatchPlan
    {
        internal static void ApplyTo(Assembly targetAssembly)
        {
            RuntimePatchSession.Apply(targetAssembly,
                MagickCameraLifetimePatch.SetPlayStateDefinition);
            RuntimePatchSession.Apply(targetAssembly,
                MagickCameraLifetimePatch.InfluenceDefinition);
            System.Type camera = targetAssembly.GetType(
                "Magicka.Graphics.MagickCamera", true);
            if (camera.GetMethod("Dispose",
                BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic) == null)
            {
                RuntimePatchAudit.WriteNotApplicable(
                    MagickCameraLifetimePatch.DisposeDefinition,
                    "The camera hierarchy is not disposable in this Magicka version.");
                return;
            }
            RuntimePatchSession.Apply(targetAssembly,
                MagickCameraLifetimePatch.DisposeDefinition);
        }
    }
}
