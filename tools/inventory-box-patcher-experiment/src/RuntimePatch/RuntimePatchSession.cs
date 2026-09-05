using System;
using System.Linq;
using System.Reflection;
using Harmony;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class RuntimePatchSession
    {
        internal static void Apply(
            Assembly targetAssembly,
            RuntimePatchDefinition definition)
        {
            MethodInfo targetMethod = definition.FindTarget(targetAssembly);
            MethodInfo prefix = definition.CreatePrefix == null
                ? null
                : definition.CreatePrefix(targetMethod);
            MethodInfo postfix = definition.CreatePostfix == null
                ? null
                : definition.CreatePostfix(targetMethod);
            HarmonyInstance harmony = HarmonyInstance.Create(definition.HarmonyOwner);

            harmony.Patch(
                targetMethod,
                AsHarmonyMethod(prefix),
                AsHarmonyMethod(postfix),
                AsHarmonyMethod(definition.Transpiler));

            AssertPatchIsRegistered(harmony, targetMethod, definition, prefix, postfix);
            RuntimePatchAudit.WriteSuccess(targetAssembly, targetMethod, definition);
        }

        private static HarmonyMethod AsHarmonyMethod(MethodInfo method)
        {
            return method == null ? null : new HarmonyMethod(method);
        }

        private static void AssertPatchIsRegistered(
            HarmonyInstance harmony,
            MethodInfo targetMethod,
            RuntimePatchDefinition definition,
            MethodInfo prefix,
            MethodInfo postfix)
        {
            Patches patchInfo = harmony.GetPatchInfo(targetMethod);
            int registrations = CountRegistrations(patchInfo, definition, prefix, postfix);

            if (registrations != 1)
                throw new InvalidOperationException(
                    "Expected one registered patch, found " + registrations + ".");
        }

        private static int CountRegistrations(
            Patches patches,
            RuntimePatchDefinition definition,
            MethodInfo prefix,
            MethodInfo postfix)
        {
            if (patches == null)
                return 0;
            if (prefix != null)
                return patches.Prefixes.Count(patch =>
                    patch.owner == definition.HarmonyOwner && patch.patch == prefix);
            if (postfix != null)
                return patches.Postfixes.Count(patch =>
                    patch.owner == definition.HarmonyOwner && patch.patch == postfix);
            return patches.Transpilers.Count(patch =>
                patch.owner == definition.HarmonyOwner && patch.patch == definition.Transpiler);
        }
    }
}
