using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class PackLicenseTargetMethods
    {
        internal static MethodInfo FindSetterIn(
            Assembly targetAssembly,
            string typeName,
            string propertyName)
        {
            PackLicensePatch.Configure(targetAssembly);
            Type packType = targetAssembly.GetType(typeName, true);
            PropertyInfo property = packType.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);
            MethodInfo setter = property == null ? null : property.GetSetMethod();
            if (setter == null || setter.ReturnType != typeof(void) ||
                setter.GetParameters().Length != 1)
                throw new MissingMethodException(typeName, "set_" + propertyName);
            return setter;
        }
    }
}
