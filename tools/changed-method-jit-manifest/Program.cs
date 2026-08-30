using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 3)
{
    Console.Error.WriteLine(
        "Usage: ChangedMethodJitManifest <previous> <current> <manifest>");
    return 2;
}

using AssemblyDefinition previous = AssemblyDefinition.ReadAssembly(args[0]);
using AssemblyDefinition current = AssemblyDefinition.ReadAssembly(args[1]);
Dictionary<string, MethodDefinition> previousMethods = Methods(previous)
    .ToDictionary(MethodKey, StringComparer.Ordinal);
MethodDefinition[] currentMethods = Methods(current).ToArray();
HashSet<string> changedKeys = currentMethods
    .Where(method => !previousMethods.TryGetValue(MethodKey(method), out var old)
        || MethodBodyKey(method) != MethodBodyKey(old))
    .Select(MethodKey)
    .ToHashSet(StringComparer.Ordinal);

List<string> entries = [];
foreach (MethodDefinition method in currentMethods
             .Where(method => changedKeys.Contains(MethodKey(method)))
             .OrderBy(MethodKey, StringComparer.Ordinal))
{
    if (!method.HasGenericParameters && !method.DeclaringType.HasGenericParameters)
    {
        entries.Add(Entry(method, []));
        continue;
    }

    GenericInstanceMethod[] instances = currentMethods
        .Where(candidate => candidate.HasBody)
        .SelectMany(candidate => candidate.Body.Instructions)
        .Select(instruction => instruction.Operand)
        .OfType<GenericInstanceMethod>()
        .Where(instance => instance.ElementMethod.Name == method.Name
            && instance.ElementMethod.DeclaringType.FullName
                == method.DeclaringType.FullName
            && instance.ElementMethod.Parameters.Count == method.Parameters.Count
            && instance.ElementMethod.GenericParameters.Count
                == method.GenericParameters.Count)
        .GroupBy(instance => string.Join(",", instance.GenericArguments
            .Select(argument => argument.MetadataToken.ToInt32())))
        .Select(group => group.First())
        .ToArray();
    if (instances.Length == 0)
    {
        TypeReference[] representativeArguments = method.GenericParameters
            .Select(parameter => RepresentativeType(parameter, current))
            .ToArray();
        if (representativeArguments.All(argument => argument is not null))
        {
            entries.Add(Entry(method, representativeArguments!));
            continue;
        }
        entries.Add("SKIP\t" + MethodKey(method)
            + "\topen generic method has no resolvable concrete instantiation");
        continue;
    }
    entries.AddRange(instances.Select(instance => Entry(
        method,
        instance.GenericArguments)));
}

File.WriteAllLines(Path.GetFullPath(args[2]), entries);
Console.WriteLine(
    "Changed methods: " + changedKeys.Count
    + "; JIT entries: " + entries.Count(line => line.StartsWith("JIT\t"))
    + "; explicit skips: " + entries.Count(line => line.StartsWith("SKIP\t")));
return 0;

static string Entry(
    MethodDefinition method,
    IEnumerable<TypeReference> genericArguments) =>
    "JIT\t" + method.MetadataToken.ToInt32() + "\t"
    + string.Join(",", genericArguments.Select(argument =>
        argument.MetadataToken.ToInt32())) + "\t" + MethodKey(method);

static IEnumerable<MethodDefinition> Methods(AssemblyDefinition assembly) =>
    AllTypes(assembly.MainModule).SelectMany(type => type.Methods);

static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) =>
    module.Types.SelectMany(TraverseType);

static IEnumerable<TypeDefinition> TraverseType(TypeDefinition type) =>
    new[] { type }.Concat(type.NestedTypes.SelectMany(TraverseType));

static TypeDefinition RepresentativeType(
    GenericParameter parameter,
    AssemblyDefinition assembly)
{
    string[] constraints = parameter.Constraints
        .Select(constraint => constraint.ConstraintType.FullName)
        .ToArray();
    return AllTypes(assembly.MainModule).First(type =>
        (!parameter.HasNotNullableValueTypeConstraint || type.IsValueType)
        && (!parameter.HasDefaultConstructorConstraint
            || type.IsValueType
            || type.Methods.Any(method => method.IsConstructor
                && !method.IsStatic && method.Parameters.Count == 0))
        && constraints.All(constraint => type.FullName == constraint
            || type.Interfaces.Any(implementation =>
                implementation.InterfaceType.FullName == constraint)));
}

static string MethodKey(MethodReference method) =>
    method.DeclaringType.FullName + "::" + method.Name + "("
    + string.Join(",", method.Parameters.Select(parameter =>
        parameter.ParameterType.FullName)) + "):" + method.ReturnType.FullName;

static string MethodBodyKey(MethodDefinition method)
{
    if (!method.HasBody)
    {
        return "attributes:" + method.Attributes + ":impl:" + method.ImplAttributes;
    }
    MethodBody body = method.Body;
    return "max:" + body.MaxStackSize
        + ":init:" + body.InitLocals
        + ":locals:" + string.Join(",", body.Variables.Select(variable =>
            variable.VariableType.FullName))
        + ":il:" + string.Join(";", body.Instructions.Select(instruction =>
            instruction.OpCode.Code + ":" + OperandKey(instruction.Operand)))
        + ":eh:" + string.Join(";", body.ExceptionHandlers.Select(handler =>
            handler.HandlerType + ":" + handler.CatchType?.FullName
            + ":" + handler.TryStart.Offset + ":" + handler.TryEnd.Offset
            + ":" + handler.HandlerStart.Offset + ":" + handler.HandlerEnd.Offset));
}

static string OperandKey(object? operand) => operand switch
{
    null => "",
    Instruction instruction => "IL_" + instruction.Offset,
    Instruction[] instructions => string.Join(",", instructions.Select(
        instruction => "IL_" + instruction.Offset)),
    MethodReference method => MethodKey(method),
    FieldReference field => field.FullName,
    TypeReference type => type.FullName,
    VariableDefinition variable => "V" + variable.Index + ":"
        + variable.VariableType.FullName,
    ParameterDefinition parameter => "A" + parameter.Index + ":"
        + parameter.ParameterType.FullName,
    _ => operand.ToString() ?? "",
};
