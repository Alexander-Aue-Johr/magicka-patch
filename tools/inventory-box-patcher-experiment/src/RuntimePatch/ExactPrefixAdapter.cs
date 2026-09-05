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
    }

    public static class TwoArgumentPrefix<TInstance, TArgument>
    {
        public static bool Prefix(TInstance __instance, TArgument iMessage)
        {
            return PlayStateAddWorldSyncMessagePatch.Prefix(__instance, iMessage);
        }
    }
}
