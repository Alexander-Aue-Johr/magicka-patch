using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class ChangedMethodJitProbe
{
    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: ChangedMethodJitProbe <assembly> <manifest>");
            return 2;
        }

        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Module module = assembly.ManifestModule;
        int prepared = 0;
        int skipped = 0;
        foreach (string line in File.ReadAllLines(args[1]))
        {
            string[] fields = line.Split(new char[] { '\t' });
            if (fields[0] == "SKIP")
            {
                Console.WriteLine("SKIP " + fields[1] + ": " + fields[2]);
                skipped++;
                continue;
            }

            int methodToken = Int32.Parse(fields[1]);
            Type[] declaringArguments = ResolveTypes(module, fields[2]);
            Type[] methodArguments = ResolveTypes(module, fields[3]);
            MethodBase method = module.ResolveMethod(methodToken);
            if (method.DeclaringType.ContainsGenericParameters)
                method = CloseDeclaringType(method, declaringArguments);
            if (method.IsAbstract
                || (method.Attributes & MethodAttributes.PinvokeImpl) != 0
                || (method.GetMethodImplementationFlags()
                    & (MethodImplAttributes.Runtime | MethodImplAttributes.InternalCall)) != 0)
            {
                Console.WriteLine("SKIP " + fields[3] + ": no managed IL body");
                skipped++;
                continue;
            }

            MethodInfo generic = method as MethodInfo;
            if (generic != null && generic.IsGenericMethodDefinition)
            {
                method = generic.MakeGenericMethod(methodArguments);
            }
            if (method.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    "No concrete JIT instantiation for " + fields[4]);
            }

            Console.WriteLine("JIT " + fields[4]);
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            prepared++;
        }
        Console.WriteLine("Prepared " + prepared + "; skipped " + skipped + ".");
        return 0;
    }

    private static Type[] ResolveTypes(Module module, string tokenList)
    {
        if (tokenList.Length == 0)
            return new Type[0];

        string[] tokens = tokenList.Split(',');
        Type[] arguments = new Type[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
            arguments[index] = module.ResolveType(Int32.Parse(tokens[index]));
        return arguments;
    }

    private static MethodBase CloseDeclaringType(
        MethodBase method,
        Type[] declaringArguments)
    {
        Type closedType = method.DeclaringType.MakeGenericType(declaringArguments);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic;

        if (method is ConstructorInfo)
        {
            ConstructorInfo[] constructors = closedType.GetConstructors(flags);
            for (int index = 0; index < constructors.Length; index++)
            {
                if (constructors[index].MetadataToken == method.MetadataToken)
                    return constructors[index];
            }
        }
        else
        {
            MethodInfo[] methods = closedType.GetMethods(flags);
            for (int index = 0; index < methods.Length; index++)
            {
                if (methods[index].MetadataToken == method.MetadataToken)
                    return methods[index];
            }
        }

        throw new MissingMethodException(
            closedType.FullName,
            method.Name + " [token " + method.MetadataToken + "]");
    }
}
