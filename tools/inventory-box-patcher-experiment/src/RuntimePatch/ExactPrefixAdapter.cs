using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class ExactPrefixAdapter
    {
        internal static MethodInfo Create(MethodInfo target)
        {
            Type adapter = typeof(TwoArgumentPrefix<,>).MakeGenericType(
                target.DeclaringType,
                target.GetParameters()[0].ParameterType);
            return adapter.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
        }

        internal static MethodInfo CreateNullResult(MethodInfo target)
        {
            Type adapter = typeof(NullResultPrefix<,>).MakeGenericType(
                target.DeclaringType,
                target.ReturnType);
            return adapter.GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
        }
    }

    public static class TwoArgumentPrefix<TInstance, TArgument>
    {
        public static bool Prefix(TInstance __instance, TArgument iMessage)
        {
            return PlayStateAddWorldSyncMessagePatch.Prefix(__instance, iMessage);
        }
    }

    public static class NullResultPrefix<TInstance, TResult>
    {
        public static bool Prefix(TInstance __instance, ref TResult __result)
        {
            bool runOriginal = AvatarFindInteractablePatch.Prefix(__instance);
            if (!runOriginal)
                __result = default(TResult);
            return runOriginal;
        }
    }
}
