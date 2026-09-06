using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class MachineTargetMethods
    {
        private const string MachineTypeName =
            "Magicka.GameLogic.Entities.Bosses.Machine";
        private const string MessageTypeName =
            "Magicka.GameLogic.Entities.Bosses.BossInitializeMessage";

        internal static MethodInfo FindNetworkInitializeIn(Assembly targetAssembly)
        {
            Type machineType = targetAssembly.GetType(MachineTypeName, true);
            Type messageType = targetAssembly.GetType(MessageTypeName, true);
            MethodInfo[] methods = machineType.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "NetworkInitialize" &&
                    method.ReturnType == typeof(void) &&
                    parameters.Length == 1 &&
                    parameters[0].ParameterType.IsByRef &&
                    parameters[0].ParameterType.GetElementType() == messageType)
                {
                    return method;
                }
            }

            throw new MissingMethodException(machineType.FullName, "NetworkInitialize");
        }
    }
}
