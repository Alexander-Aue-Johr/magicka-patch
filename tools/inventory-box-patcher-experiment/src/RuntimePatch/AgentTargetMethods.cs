using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class AgentTargetMethods
    {
        private const string AgentTypeName = "Magicka.AI.Agent";

        internal static MethodInfo FindChooseTargetIn(Assembly targetAssembly)
        {
            Type agentType = targetAssembly.GetType(AgentTypeName, true);
            MethodInfo[] methods = Array.FindAll(
                agentType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.Name == "ChooseTarget" &&
                    method.GetParameters().Length == 2);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one Agent.ChooseTarget overload, found " +
                    methods.Length + ".");
            return methods[0];
        }
    }
}
