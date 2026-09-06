using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class AIStateAttackTargetMethods
    {
        private const string StateTypeName = "Magicka.AI.AgentStates.AIStateAttack";
        private const string OwnerTypeName = "Magicka.AI.IAI";

        internal static MethodInfo FindOnExecuteIn(Assembly targetAssembly)
        {
            Type stateType = targetAssembly.GetType(StateTypeName, true);
            Type ownerType = targetAssembly.GetType(OwnerTypeName, true);
            MethodInfo method = stateType.GetMethod(
                "OnExecute",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[] { ownerType, typeof(float) },
                null);
            if (method == null)
            {
                throw new MissingMethodException(stateType.FullName, "OnExecute");
            }
            return method;
        }
    }
}
