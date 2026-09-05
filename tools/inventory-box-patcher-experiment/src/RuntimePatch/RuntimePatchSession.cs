using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;

namespace Magicka.InventoryBoxRuntimePatch
{
    internal static class RuntimePatchSession
    {
        internal static void Apply(
            Assembly targetAssembly,
            RuntimePatchDefinition definition)
        {
            MethodInfo targetMethod = definition.FindTarget(targetAssembly);
            MethodInfo transpiler = definition.Transpiler;
            HarmonyInstance harmony = HarmonyInstance.Create(definition.HarmonyOwner);

            PatchObservation.Reset();
            DynamicMethod replacement = harmony.Patch(
                targetMethod,
                null,
                null,
                new HarmonyMethod(transpiler));

            AssertReplacementWasCreated(replacement);
            AssertTranspilerRanExactlyOnce();
            AssertCSharpDiffMatchesDefinition(definition);
            AssertTranspilerIsRegistered(harmony, targetMethod, definition);
            RuntimePatchAudit.WriteSuccess(targetAssembly, targetMethod, definition);
        }

        private static void AssertReplacementWasCreated(DynamicMethod replacement)
        {
            if (replacement == null)
                throw new InvalidOperationException("Harmony did not create a replacement method.");
        }

        private static void AssertTranspilerRanExactlyOnce()
        {
            if (PatchObservation.TranspilerCalls != 1)
                throw new InvalidOperationException(
                    "Expected one transpiler execution, observed " + PatchObservation.TranspilerCalls + ".");
        }

        private static void AssertCSharpDiffMatchesDefinition(RuntimePatchDefinition definition)
        {
            CSharpPatchDiff.AssertEqual(
                definition.ExpectedCSharpDiff,
                PatchObservation.CSharpDiff,
                "The " + definition.Name + " patch produced a different C# diff.");
        }

        private static void AssertTranspilerIsRegistered(
            HarmonyInstance harmony,
            MethodInfo targetMethod,
            RuntimePatchDefinition definition)
        {
            Patches patchInfo = harmony.GetPatchInfo(targetMethod);
            int registrations = patchInfo == null
                ? 0
                : patchInfo.Transpilers.Count(patch =>
                    patch.owner == definition.HarmonyOwner &&
                    patch.patch == definition.Transpiler);

            if (registrations != 1)
                throw new InvalidOperationException(
                    "Expected one registered transpiler, found " + registrations + ".");
        }
    }
}
