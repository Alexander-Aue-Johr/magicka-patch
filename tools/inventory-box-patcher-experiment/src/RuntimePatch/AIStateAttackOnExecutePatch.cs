using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class AIStateAttackOnExecutePatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "AI attack detached target guard",
                "org.magickacommunitypatch.ai-attack-detached-target",
                AIStateAttackTargetMethods.FindOnExecuteIn,
                CreatePrefix);

        public static bool Prefix(object iOwner)
        {
            if (iOwner == null)
            {
                return true;
            }

            object target = RuntimeMember.ReadProperty(iOwner, "CurrentTarget");
            if (target == null || RuntimeMember.ReadProperty(target, "Body") != null)
            {
                return true;
            }

            Invoke(iOwner, "PopState");
            Invoke(iOwner, "ReleaseTarget");
            return false;
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(AIStateAttackOnExecutePatch).GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
        }

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Type.EmptyTypes,
                null);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }
            method.Invoke(target, null);
        }
    }
}
