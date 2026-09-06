using System;
using System.Reflection;

namespace Magicka.CommunityPatch.Runtime
{
    internal static class RuntimeMember
    {
        internal static object ReadField(object target, string name)
        {
            Type targetType = target.GetType();
            for (Type current = targetType; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field.GetValue(target);
            }
            throw new MissingFieldException(targetType.FullName, name);
        }

        internal static void WriteField(object target, string name, object value)
        {
            Type targetType = target.GetType();
            for (Type current = targetType; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }
            throw new MissingFieldException(targetType.FullName, name);
        }

        internal static object ReadProperty(object target, string name)
        {
            PropertyInfo property = RequireProperty(target, name);
            return property.GetValue(target, null);
        }

        internal static void WriteProperty(object target, string name, object value)
        {
            PropertyInfo property = RequireProperty(target, name);
            property.SetValue(target, value, null);
        }

        internal static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, false);
                if (type != null)
                    return type;
            }
            throw new TypeLoadException(fullName);
        }

        private static PropertyInfo RequireProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
                throw new MissingMemberException(target.GetType().FullName, name);
            return property;
        }
    }
}
