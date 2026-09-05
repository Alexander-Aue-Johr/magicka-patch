using System;
using System.Globalization;
using System.Reflection;

internal static class RuntimeReflection
{
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

    internal static FieldInfo RequireField(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field;
        }
        throw new MissingFieldException(type.FullName, name);
    }

    internal static object ReadField(object target, string name)
    {
        return RequireField(target.GetType(), name).GetValue(target);
    }

    internal static void WriteField(object target, string name, object value)
    {
        RequireField(target.GetType(), name).SetValue(target, value);
    }

    internal static object ReadProperty(object target, string name)
    {
        return target.GetType().GetProperty(name).GetValue(target, null);
    }

    internal static string Coordinates(object point)
    {
        float x = Convert.ToSingle(ReadField(point, "X"), CultureInfo.InvariantCulture);
        float y = Convert.ToSingle(ReadField(point, "Y"), CultureInfo.InvariantCulture);
        return x.ToString("R", CultureInfo.InvariantCulture) + "x" +
            y.ToString("R", CultureInfo.InvariantCulture);
    }
}
