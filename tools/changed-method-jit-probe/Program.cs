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
            MethodBase method = module.ResolveMethod(methodToken);
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
                string[] tokens = fields[2].Length == 0
                    ? new string[0]
                    : fields[2].Split(',');
                Type[] arguments = new Type[tokens.Length];
                for (int index = 0; index < tokens.Length; index++)
                {
                    arguments[index] = module.ResolveType(Int32.Parse(tokens[index]));
                }
                method = generic.MakeGenericMethod(arguments);
            }
            if (method.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    "No concrete JIT instantiation for " + fields[3]);
            }

            Console.WriteLine("JIT " + fields[3]);
            RuntimeHelpers.PrepareMethod(method.MethodHandle);
            prepared++;
        }
        Console.WriteLine("Prepared " + prepared + "; skipped " + skipped + ".");
        return 0;
    }
}
