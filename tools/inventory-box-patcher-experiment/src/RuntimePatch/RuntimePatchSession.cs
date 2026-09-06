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
            MethodBase targetMethod = definition.FindTarget(targetAssembly);
            MethodInfo methodTarget = targetMethod as MethodInfo;
            MethodInfo prefix = definition.FixedPrefix ??
                (definition.CreatePrefix == null
                    ? null
                    : definition.CreatePrefix(RequireMethodInfo(methodTarget, definition)));
            MethodInfo postfix = definition.FixedPostfix ??
                (definition.CreatePostfix == null
                    ? null
                    : definition.CreatePostfix(RequireMethodInfo(methodTarget, definition)));
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
            MethodBase targetMethod,
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

        private static MethodInfo RequireMethodInfo(
            MethodInfo targetMethod,
            RuntimePatchDefinition definition)
        {
            if (targetMethod == null)
                throw new InvalidOperationException(
                    definition.Name + " requires a method target.");
            return targetMethod;
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
