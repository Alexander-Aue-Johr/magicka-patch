using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class AIStateMoveTargetMethods
    {
        private const string StateTypeName = "Magicka.AI.AgentStates.AIStateMove";

        internal static MethodInfo FindOnEnterIn(Assembly targetAssembly)
        {
            return FindMethod(targetAssembly, "OnEnter", 1);
        }

        internal static MethodInfo FindOnExecuteIn(Assembly targetAssembly)
        {
            return FindMethod(targetAssembly, "OnExecute", 2);
        }

        private static MethodInfo FindMethod(
            Assembly targetAssembly,
            string name,
            int parameterCount)
        {
            Type stateType = targetAssembly.GetType(StateTypeName, true);
            MethodInfo[] methods = Array.FindAll(
                stateType.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.Name == name &&
                    method.GetParameters().Length == parameterCount);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one AIStateMove." + name + " overload, found " +
                    methods.Length + ".");
            return methods[0];
        }
    }
}
