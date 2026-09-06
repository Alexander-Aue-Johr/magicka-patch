using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PortalTargetMethods
    {
        private const string PortalEntityTypeName =
            "Magicka.GameLogic.Entities.Abilities.SpecialAbilities.Portal+PortalEntity";

        internal static MethodInfo FindPortalEntityUpdateIn(Assembly targetAssembly)
        {
            Type portalEntityType = targetAssembly.GetType(PortalEntityTypeName, true);
            MethodInfo[] methods = Array.FindAll(
                portalEntityType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.Name == "Update" && method.GetParameters().Length == 2);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one PortalEntity.Update overload, found " +
                    methods.Length + ".");
            return methods[0];
        }
    }
}
