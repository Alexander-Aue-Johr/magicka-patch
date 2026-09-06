using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class JormungandrTargetMethods
    {
        private const string UndergroundStateTypeName =
            "Magicka.GameLogic.Entities.Bosses.Jormungandr+UndergroundState";

        internal static MethodInfo FindUndergroundUpdateIn(Assembly targetAssembly)
        {
            Type stateType = targetAssembly.GetType(UndergroundStateTypeName, true);
            MethodInfo[] methods = Array.FindAll(
                stateType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.Name == "OnUpdate" && method.GetParameters().Length == 2);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one Jormungandr UndergroundState.OnUpdate overload, found " +
                    methods.Length + ".");
            return methods[0];
        }
    }
}
