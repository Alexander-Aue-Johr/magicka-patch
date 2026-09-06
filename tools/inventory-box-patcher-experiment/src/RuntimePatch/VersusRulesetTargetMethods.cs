using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class VersusRulesetTargetMethods
    {
        internal static MethodInfo FindRevivePlayerIn(Assembly targetAssembly)
        {
            Type rulesetType = targetAssembly.GetType(
                "Magicka.Levels.Versus.VersusRuleset",
                true);
            MethodInfo[] methods = Array.FindAll(
                rulesetType.GetMethods(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
                method => method.Name == "RevivePlayer" &&
                    method.ReturnType == typeof(ushort) &&
                    method.GetParameters().Length == 4);
            if (methods.Length != 1)
                throw new InvalidOperationException(
                    "Expected one VersusRuleset.RevivePlayer overload, found " +
                    methods.Length + ".");
            return methods[0];
        }
    }
}
