using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    public static class HelperArrayEqualsPatch
    {
        internal static readonly RuntimePatchDefinition Definition =
            RuntimePatchDefinition.Prefix(
                "Helper null-safe array equality",
                "org.magickacommunitypatch.helper-array-equals",
                FindTarget,
                CreatePrefix);

        public static bool Prefix(byte[] iA, byte[] iB, ref bool __result)
        {
            if (iA == null || iB == null || iA.Length != iB.Length)
            {
                __result = false;
                return false;
            }

            for (int index = 0; index < iA.Length; index++)
            {
                if (iA[index] != iB[index])
                {
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false;
        }

        private static MethodInfo FindTarget(Assembly targetAssembly)
        {
            Type type = targetAssembly.GetType("Magicka.Helper", true);
            MethodInfo method = type.GetMethod(
                "ArrayEquals",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new Type[] { typeof(byte[]), typeof(byte[]) },
                null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, "ArrayEquals");
            }
            return method;
        }

        private static MethodInfo CreatePrefix(MethodInfo target)
        {
            return typeof(HelperArrayEqualsPatch).GetMethod(
                "Prefix",
                BindingFlags.Static | BindingFlags.Public);
        }
    }
}
