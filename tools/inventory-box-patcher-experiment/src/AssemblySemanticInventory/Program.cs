using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("usage: AssemblySemanticInventory <original> <patched> <output.csv> [methods.tsv]");
    return 2;
}

using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[0]));
using AssemblyDefinition patched = AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[1]));
Dictionary<string, TypeDefinition> oldTypes = Types(original.MainModule)
    .ToDictionary(TypeKey, StringComparer.Ordinal);
List<Row> rows = new();
List<MethodRow> methodRows = new();
string[] oldAttributes = original.CustomAttributes.Select(AttributeKey)
    .OrderBy(value => value, StringComparer.Ordinal).ToArray();
string[] newAttributes = patched.CustomAttributes.Select(AttributeKey)
    .OrderBy(value => value, StringComparer.Ordinal).ToArray();
int metadataChanges = oldAttributes.Except(newAttributes, StringComparer.Ordinal).Count() +
    newAttributes.Except(oldAttributes, StringComparer.Ordinal).Count();
if (metadataChanges != 0)
    rows.Add(new Row(Path.Combine("Properties", "AssemblyInfo.cs"), "<assembly>",
        0, 0, 0, 0, 0, metadataChanges));
foreach (TypeDefinition type in Types(patched.MainModule).Where(t => t.Name != "<Module>"))
{
    int changed = 0, layoutOnly = 0, unchanged = 0, addedMethods = 0, addedFields = 0;
    if (!oldTypes.TryGetValue(TypeKey(type), out TypeDefinition? oldType))
        continue;
    Dictionary<string, MethodDefinition> oldMethods = oldType.Methods
        .ToDictionary(MethodKey, StringComparer.Ordinal);
    foreach (MethodDefinition method in type.Methods)
    {
        if (!oldMethods.TryGetValue(MethodKey(method), out MethodDefinition? oldMethod))
        {
            addedMethods++;
            continue;
        }
        string status;
        if (ExactBodyKey(oldMethod) == ExactBodyKey(method))
        {
            unchanged++;
            status = "exact";
        }
        else if (CanonicalBodyKey(oldMethod) == CanonicalBodyKey(method))
        {
            layoutOnly++;
            status = "layout-only";
        }
        else
        {
            changed++;
            status = "changed";
        }
        methodRows.Add(new MethodRow(SourcePath(type), TypeKey(type),
            SourceMethodName(method), method.Parameters.Count,
            method.GenericParameters.Count, SourceMethodOrdinal(type, method), status));
    }
    HashSet<string> oldFields = oldType.Fields.Select(FieldKey).ToHashSet(StringComparer.Ordinal);
    addedFields = type.Fields.Count(field => !oldFields.Contains(FieldKey(field)));
    if (changed + layoutOnly + addedMethods + addedFields > 0)
        rows.Add(new Row(SourcePath(type), TypeKey(type), changed, layoutOnly,
            unchanged, addedMethods, addedFields, 0));
}
if (args.Length == 4)
{
    string methodsPath = Path.GetFullPath(args[3]);
    Directory.CreateDirectory(Path.GetDirectoryName(methodsPath)!);
    using StreamWriter methods = new(methodsPath, false, new UTF8Encoding(false));
    methods.WriteLine("File\tType\tName\tParameterCount\tGenericCount\tOrdinal\tStatus");
    foreach (MethodRow row in methodRows.OrderBy(row => row.File)
        .ThenBy(row => row.Type).ThenBy(row => row.Name)
        .ThenBy(row => row.ParameterCount).ThenBy(row => row.Ordinal))
        methods.WriteLine(string.Join('\t', row.File, row.Type, row.Name,
            row.ParameterCount, row.GenericCount, row.Ordinal, row.Status));
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
using (StreamWriter writer = new(Path.GetFullPath(args[2]), false, new UTF8Encoding(false)))
{
    writer.WriteLine("File,Type,ChangedExistingMethods,LayoutOnlyMethods,UnchangedExistingMethods,AddedMethods,AddedFields,MetadataChanges");
    foreach (Row row in rows.OrderBy(r => r.File).ThenBy(r => r.Type))
        writer.WriteLine(string.Join(',', Csv(row.File), Csv(row.Type), row.Changed,
            row.LayoutOnly, row.Unchanged, row.AddedMethods, row.AddedFields,
            row.MetadataChanges));
}
Console.WriteLine($"types={rows.Count}; changed_existing_methods={rows.Sum(r => r.Changed)}; layout_only_methods={rows.Sum(r => r.LayoutOnly)}; added_methods={rows.Sum(r => r.AddedMethods)}; added_fields={rows.Sum(r => r.AddedFields)}");
return 0;

static IEnumerable<TypeDefinition> Types(ModuleDefinition module) =>
    module.Types.SelectMany(Traverse);
static IEnumerable<TypeDefinition> Traverse(TypeDefinition type) =>
    new[] { type }.Concat(type.NestedTypes.SelectMany(Traverse));
static string TypeKey(TypeReference type) => type.FullName;
static string MethodKey(MethodReference method) => method.Name + "`" + method.GenericParameters.Count +
    "(" + string.Join(",", method.Parameters.Select(p => p.ParameterType.FullName)) + "):" + method.ReturnType.FullName;
static string FieldKey(FieldReference field) => field.Name + ":" + field.FieldType.FullName;
static string SourceMethodName(MethodDefinition method) => method.Name;
static int SourceMethodOrdinal(TypeDefinition type, MethodDefinition method) =>
    type.Methods.Where(candidate => SourceMethodName(candidate) == SourceMethodName(method) &&
        candidate.Parameters.Count == method.Parameters.Count &&
        candidate.GenericParameters.Count == method.GenericParameters.Count)
    .TakeWhile(candidate => candidate != method).Count();
static string AttributeKey(CustomAttribute attribute) => attribute.AttributeType.FullName +
    "(" + string.Join(",", attribute.ConstructorArguments.Select(argument =>
        argument.Type.FullName + "=" + argument.Value)) + ")" +
    string.Join(",", attribute.Properties.Select(property =>
        property.Name + "=" + property.Argument.Value));
static string SourcePath(TypeDefinition type)
{
    TypeDefinition outer = type;
    while (outer.DeclaringType != null) outer = outer.DeclaringType;
    string prefix = string.IsNullOrEmpty(outer.Namespace) ? "" : outer.Namespace.Replace('.', Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return prefix + outer.Name.Split('`')[0] + ".cs";
}
static string ExactBodyKey(MethodDefinition method) => BodyKey(method, false);
static string CanonicalBodyKey(MethodDefinition method) => BodyKey(method, true);
static string BodyKey(MethodDefinition method, bool canonical)
{
    if (!method.HasBody) return "no-body:" + method.Attributes + ":" + method.ImplAttributes;
    MethodBody body = method.Body;
    Dictionary<Instruction, int> instructionIndex = body.Instructions
        .Select((instruction, index) => (instruction, index)).ToDictionary(x => x.instruction, x => x.index);
    Dictionary<VariableDefinition, int> variableMap = new();
    int nextVariable = 0;
    string Operand(object? operand) => operand switch
    {
        null => "",
        Instruction instruction => "I" + instructionIndex[instruction],
        Instruction[] instructions => string.Join("/", instructions.Select(i => "I" + instructionIndex[i])),
        MethodReference methodReference => methodReference.DeclaringType.FullName + "::" + MethodKey(methodReference),
        FieldReference fieldReference => fieldReference.FullName,
        TypeReference typeReference => typeReference.FullName,
        ParameterDefinition parameter => "A" + parameter.Index + ":" + parameter.ParameterType.FullName,
        VariableDefinition variable when canonical => "V" + CanonicalVariable(variable),
        VariableDefinition variable => "V" + variable.Index + ":" + variable.VariableType.FullName,
        string value => "S:" + value,
        _ => operand.ToString() ?? ""
    };
    string CanonicalVariable(VariableDefinition variable)
    {
        if (!variableMap.TryGetValue(variable, out int index)) variableMap[variable] = index = nextVariable++;
        return index + ":" + variable.VariableType.FullName;
    }
    string instructions = string.Join(";", body.Instructions.Select(i => i.OpCode.Code + ":" + Operand(i.Operand)));
    string handlers = string.Join(";", body.ExceptionHandlers.Select(h => h.HandlerType + ":" + h.CatchType?.FullName +
        ":" + Index(h.TryStart) + ":" + Index(h.TryEnd) + ":" + Index(h.HandlerStart) + ":" + Index(h.HandlerEnd) + ":" + Index(h.FilterStart)));
    string locals = canonical ? string.Join(",", variableMap.OrderBy(x => x.Value).Select(x => x.Key.VariableType.FullName))
        : string.Join(",", body.Variables.Select(v => v.VariableType.FullName));
    return (canonical ? "" : "max:" + body.MaxStackSize + ":") + "init:" + body.InitLocals + ":locals:" + locals + ":il:" + instructions + ":eh:" + handlers;
    string Index(Instruction? instruction) => instruction == null ? "-" : instructionIndex[instruction].ToString();
}
static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
internal readonly record struct Row(string File, string Type, int Changed, int LayoutOnly,
    int Unchanged, int AddedMethods, int AddedFields, int MetadataChanges);
internal readonly record struct MethodRow(string File, string Type, string Name,
    int ParameterCount, int GenericCount, int Ordinal, string Status);
