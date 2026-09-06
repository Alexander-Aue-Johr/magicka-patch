using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class EntityStateStorageTargetMethods
    {
        private const string StorageTypeName =
            "Magicka.GameLogic.Entities.EntityStateStorage";

        internal static ConstructorInfo FindConstructorIn(Assembly targetAssembly)
        {
            EntityStateStoragePatch.Configure(targetAssembly);
            Type storageType = targetAssembly.GetType(StorageTypeName, true);
            ConstructorInfo[] constructors = storageType.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            ConstructorInfo found = null;
            for (int index = 0; index < constructors.Length; index++)
            {
                ParameterInfo[] parameters = constructors[index].GetParameters();
                if (parameters.Length == 1 &&
                    parameters[0].ParameterType.FullName ==
                        "Magicka.GameLogic.GameStates.PlayState")
                {
                    if (found != null)
                        throw new InvalidOperationException(
                            "Multiple EntityStateStorage constructors matched.");
                    found = constructors[index];
                }
            }
            if (found == null)
                throw new MissingMethodException(StorageTypeName, ".ctor(PlayState)");
            return found;
        }

        internal static MethodInfo FindRestoreIn(Assembly targetAssembly)
        {
            EntityStateStoragePatch.Configure(targetAssembly);
            Type storageType = targetAssembly.GetType(StorageTypeName, true);
            MethodInfo method = storageType.GetMethod(
                "Restore",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new Type[]
                {
                    typeof(System.Collections.Generic.List<>).MakeGenericType(
                        targetAssembly.GetType("Magicka.GameLogic.Entities.Entity", true))
                },
                null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(StorageTypeName, "Restore");
            return method;
        }
    }
}
