using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class HUDManagerTargetMethods
    {
        private const string TypeName =
            "Magicka.CoreFramework.GameSystem.HUDCustomisation.HUDManager";

        internal static bool IsAvailableIn(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType(TypeName, false);
            return type != null && FindInitialise(type) != null;
        }

        internal static MethodInfo FindInitialiseIn(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType(TypeName, true);
            MethodInfo method = FindInitialise(type);
            if (method == null)
                throw new MissingMethodException(type.FullName, "Initialise");
            return method;
        }

        private static MethodInfo FindInitialise(Type type)
        {
            return type.GetMethod(
                "Initialise",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
        }
    }
}
