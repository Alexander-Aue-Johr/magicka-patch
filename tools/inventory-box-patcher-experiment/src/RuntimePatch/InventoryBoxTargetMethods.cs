using System;
using System.Linq;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class InventoryBoxTargetMethods
    {
        internal const string RenderDataTypeName = "Magicka.GameLogic.UI.InventoryBox+RenderData";

        internal static MethodInfo FindIn(Assembly targetAssembly)
        {
            if (targetAssembly == null)
                throw new ArgumentNullException("targetAssembly");

            Type renderData = targetAssembly.GetType(RenderDataTypeName, false);
            if (renderData == null)
                throw new InvalidOperationException("Type " + RenderDataTypeName + " was not found.");

            MethodInfo[] matches = renderData
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method =>
                    method.Name == "Draw" &&
                    method.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(float))
                .ToArray();

            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "Expected one InventoryBox.RenderData.Draw(float), found " + matches.Length + ".");

            return matches[0];
        }
    }
}
