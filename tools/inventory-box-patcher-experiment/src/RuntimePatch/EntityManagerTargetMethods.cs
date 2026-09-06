using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EntityManagerTargetMethods
    {
        private const string ManagerTypeName = "Magicka.GameLogic.Entities.EntityManager";

        internal static MethodInfo FindGetClosestDamageableIn(Assembly targetAssembly)
        {
            Type managerType = targetAssembly.GetType(ManagerTypeName, true);
            MethodInfo[] methods = Array.FindAll(
                managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public),
                method => method.Name == "GetClosestIDamageable" &&
                    method.GetParameters().Length == 4);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one EntityManager.GetClosestIDamageable overload, found " +
                    methods.Length + ".");
            return methods[0];
        }
    }
}
