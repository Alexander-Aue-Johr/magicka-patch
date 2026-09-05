using System;
using System.Reflection;

namespace Magicka.InventoryBoxRuntimePatch
{
    internal sealed class RuntimePatchDefinition
    {
        internal string Name { get; private set; }
        internal string HarmonyOwner { get; private set; }
        internal string ExpectedCSharpDiff { get; private set; }
        internal Func<Assembly, MethodInfo> FindTarget { get; private set; }
        internal MethodInfo Transpiler { get; private set; }

        internal RuntimePatchDefinition(
            string name,
            string harmonyOwner,
            string expectedCSharpDiff,
            Func<Assembly, MethodInfo> findTarget,
            MethodInfo transpiler)
        {
            Name = name;
            HarmonyOwner = harmonyOwner;
            ExpectedCSharpDiff = expectedCSharpDiff;
            FindTarget = findTarget;
            Transpiler = transpiler;
        }
    }
}
