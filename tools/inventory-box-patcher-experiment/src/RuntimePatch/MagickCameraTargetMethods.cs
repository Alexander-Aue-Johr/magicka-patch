using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class MagickCameraTargetMethods
    {
        private const string CameraTypeName = "Magicka.Graphics.MagickCamera";

        internal static MethodInfo FindUpdateIn(Assembly targetAssembly)
        {
            Type cameraType = targetAssembly.GetType(CameraTypeName, true);
            MethodInfo[] methods = Array.FindAll(
                cameraType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.Name == "Update" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 2);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one MagickCamera.Update overload, found " + methods.Length + ".");
            return methods[0];
        }
    }
}
