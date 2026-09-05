using System.Globalization;
using InventoryBoxPatcherExperiment;
using Mono.Cecil;
using Mono.Cecil.Cil;

return VerificationWorkflow.Run(args);

internal static class VerificationWorkflow
{
    public static int Run(string[] arguments)
    {
        if (arguments.Length != 7)
        {
            Console.Error.WriteLine(
                "usage: AssemblyVerifier <original.exe> <current-patch.exe> <static.exe> " +
                "<runtime-host.exe> <runtime-patch.dll> <0Harmony.dll> <audit-directory>");
            return 2;
        }

        string auditDirectory = Path.GetFullPath(arguments[6]);
        Directory.CreateDirectory(auditDirectory);

        Verification verification = new();
        using ModuleDefinition original = AssemblyFile.Read(arguments[0]);
        using ModuleDefinition currentPatch = AssemblyFile.Read(arguments[1]);
        using ModuleDefinition staticPatch = AssemblyFile.Read(arguments[2]);
        using ModuleDefinition runtimeHost = AssemblyFile.Read(arguments[3]);
        using ModuleDefinition runtimePatch = AssemblyFile.Read(arguments[4]);
        using ModuleDefinition harmony = AssemblyFile.Read(arguments[5]);

        verification.VerifyRuntimeVersions(original, staticPatch, runtimeHost, runtimePatch, harmony);
        verification.VerifyStaticPatch(original, currentPatch, staticPatch);
        verification.VerifyRuntimeHost(original, runtimeHost);
        verification.VerifyRuntimeDependencies(runtimePatch, harmony);
        verification.WriteTargetSnapshots(auditDirectory, original, currentPatch, staticPatch);
        verification.WriteReport(Path.Combine(auditDirectory, "assembly-verification.txt"));

        Console.WriteLine("assembly_verification=" + (verification.Passed ? "PASS" : "FAIL"));
        Console.WriteLine("failures=" + verification.FailureCount);
        return verification.Passed ? 0 : 1;
    }
}

internal sealed class Verification
{
    private readonly List<string> report = new();
    private readonly List<string> failures = new();

    internal bool Passed => failures.Count == 0;
    internal int FailureCount => failures.Count;

    internal void VerifyRuntimeVersions(params ModuleDefinition[] modules)
    {
        foreach (ModuleDefinition module in modules)
        {
            report.Add($"runtime[{module.Name}]={module.RuntimeVersion}");
            Require(module.RuntimeVersion == "v2.0.50727", $"{module.Name} is not a CLR-2 assembly.");
        }
    }

    internal void VerifyStaticPatch(
        ModuleDefinition original,
        ModuleDefinition currentPatch,
        ModuleDefinition staticPatch)
    {
        RequireSameAssemblyShape(original, staticPatch, "static patch");
        RequireSameAssemblyReferences(original, staticPatch, "static patch");

        string[] changedMethods = FindChangedMethods(original, staticPatch).ToArray();
        report.Add("static_changed_methods=" + changedMethods.Length);
        foreach (string changedMethod in changedMethods)
            report.Add("static_changed_method=" + changedMethod);

        string expectedTarget = MethodKey(PatchTarget.FindDrawMethod(original));
        Require(
            changedMethods.SequenceEqual(new[] { expectedTarget }, StringComparer.Ordinal),
            "The static executable changes methods other than InventoryBox.RenderData.Draw.");

        MethodBodySnapshot expectedBody = MethodBodySnapshot.Create(PatchTarget.FindDrawMethod(currentPatch));
        MethodBodySnapshot actualBody = MethodBodySnapshot.Create(PatchTarget.FindDrawMethod(staticPatch));
        Require(
            actualBody.Equals(expectedBody),
            "The static Draw body does not exactly match the current patch Draw body.");
        report.Add("static_target_matches_current_patch=" + (actualBody.Equals(expectedBody) ? "yes" : "no"));
    }

    internal void VerifyRuntimeHost(ModuleDefinition original, ModuleDefinition runtimeHost)
    {
        RequireSameAssemblyShape(original, runtimeHost, "runtime host");

        HashSet<string> originalReferences = AssemblyReferences(original);
        HashSet<string> runtimeReferences = AssemblyReferences(runtimeHost);
        string[] addedReferences = runtimeReferences.Except(originalReferences).OrderBy(value => value).ToArray();
        string[] removedReferences = originalReferences.Except(runtimeReferences).OrderBy(value => value).ToArray();
        report.Add("runtime_host_added_references=" + addedReferences.Length);
        foreach (string reference in addedReferences)
            report.Add("runtime_host_added_reference=" + reference);
        report.Add("runtime_host_removed_references=" + removedReferences.Length);

        Require(
            addedReferences.Length == 1 &&
            addedReferences[0].StartsWith(
                PatchTarget.RuntimePatchAssemblyName + ", Version=1.0.0.0,",
                StringComparison.Ordinal),
            "The runtime host does not add exactly the expected runtime patch reference.");
        Require(removedReferences.Length == 0, "The runtime host removes an original assembly reference.");

        string[] changedMethods = FindChangedMethods(original, runtimeHost).ToArray();
        string mainKey = MethodKey(PatchTarget.FindMainMethod(original));
        report.Add("runtime_host_changed_methods=" + changedMethods.Length);
        foreach (string changedMethod in changedMethods)
            report.Add("runtime_host_changed_method=" + changedMethod);
        Require(
            changedMethods.SequenceEqual(new[] { mainKey }, StringComparer.Ordinal),
            "The runtime host changes methods other than Magicka.Program.Main.");

        MethodDefinition originalMain = PatchTarget.FindMainMethod(original);
        MethodDefinition runtimeMain = PatchTarget.FindMainMethod(runtimeHost);
        Require(
            runtimeMain.Body.Instructions.Count > 0 &&
            RuntimeLoaderInjection.IsBootstrapCall(runtimeMain.Body.Instructions[0]),
            "The runtime host does not start Main with the bootstrap call.");
        Require(
            MethodBodySnapshot.Create(originalMain).Equals(MethodBodySnapshot.Create(runtimeMain, 1)),
            "Program.Main differs beyond its leading bootstrap call.");
    }

    internal void VerifyRuntimeDependencies(ModuleDefinition runtimePatch, ModuleDefinition harmony)
    {
        Require(runtimePatch.Assembly.Name.Name == PatchTarget.RuntimePatchAssemblyName,
            "The runtime patch assembly name does not match the injected reference.");
        Require(runtimePatch.Assembly.Name.Version == PatchTarget.RuntimePatchAssemblyVersion,
            "The runtime patch assembly version does not match the injected reference.");
        RequireNoClr4FrameworkReferences(runtimePatch, "runtime patch");
        RequireNoClr4FrameworkReferences(harmony, "Harmony");
    }

    internal void WriteTargetSnapshots(
        string auditDirectory,
        ModuleDefinition original,
        ModuleDefinition currentPatch,
        ModuleDefinition staticPatch)
    {
        WriteSnapshot(auditDirectory, "original-target.il.txt", PatchTarget.FindDrawMethod(original));
        WriteSnapshot(auditDirectory, "current-patch-target.il.txt", PatchTarget.FindDrawMethod(currentPatch));
        WriteSnapshot(auditDirectory, "static-patch-target.il.txt", PatchTarget.FindDrawMethod(staticPatch));
    }

    internal void WriteReport(string path)
    {
        List<string> lines = new()
        {
            "result=" + (Passed ? "PASS" : "FAIL")
        };
        lines.AddRange(report);
        lines.Add("failures=" + failures.Count);
        lines.AddRange(failures.Select(failure => "failure=" + failure));
        File.WriteAllLines(path, lines);
    }

    private void RequireSameAssemblyShape(
        ModuleDefinition expected,
        ModuleDefinition actual,
        string label)
    {
        Require(expected.Assembly.FullName == actual.Assembly.FullName,
            $"The {label} changes the assembly identity.");
        Require(expected.Resources.Select(resource => resource.Name).OrderBy(value => value).SequenceEqual(
                actual.Resources.Select(resource => resource.Name).OrderBy(value => value),
                StringComparer.Ordinal),
            $"The {label} changes the resource set.");

        Dictionary<string, TypeDefinition> expectedTypes = TypesByName(expected);
        Dictionary<string, TypeDefinition> actualTypes = TypesByName(actual);
        Require(expectedTypes.Keys.OrderBy(value => value).SequenceEqual(
                actualTypes.Keys.OrderBy(value => value),
                StringComparer.Ordinal),
            $"The {label} changes the type set.");

        foreach (KeyValuePair<string, TypeDefinition> pair in expectedTypes)
        {
            if (!actualTypes.TryGetValue(pair.Key, out TypeDefinition? actualType))
                continue;

            TypeDefinition expectedType = pair.Value;
            Require(MemberKeys(expectedType.Fields).SequenceEqual(MemberKeys(actualType.Fields), StringComparer.Ordinal),
                $"The {label} changes fields in {pair.Key}.");
            Require(MemberKeys(expectedType.Methods).SequenceEqual(MemberKeys(actualType.Methods), StringComparer.Ordinal),
                $"The {label} changes method definitions in {pair.Key}.");
            Require(MemberKeys(expectedType.Properties).SequenceEqual(MemberKeys(actualType.Properties), StringComparer.Ordinal),
                $"The {label} changes properties in {pair.Key}.");
            Require(MemberKeys(expectedType.Events).SequenceEqual(MemberKeys(actualType.Events), StringComparer.Ordinal),
                $"The {label} changes events in {pair.Key}.");
            Require(
                expectedType.Interfaces.Select(item => item.InterfaceType.FullName).OrderBy(value => value).SequenceEqual(
                    actualType.Interfaces.Select(item => item.InterfaceType.FullName).OrderBy(value => value),
                    StringComparer.Ordinal),
                $"The {label} changes interfaces in {pair.Key}.");
        }
    }

    private void RequireSameAssemblyReferences(
        ModuleDefinition expected,
        ModuleDefinition actual,
        string label)
    {
        Require(AssemblyReferences(expected).SetEquals(AssemblyReferences(actual)),
            $"The {label} changes the assembly-reference set.");
    }

    private IEnumerable<string> FindChangedMethods(ModuleDefinition expected, ModuleDefinition actual)
    {
        Dictionary<string, MethodDefinition> expectedMethods = MethodsByKey(expected);
        Dictionary<string, MethodDefinition> actualMethods = MethodsByKey(actual);
        foreach (string key in expectedMethods.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (!actualMethods.TryGetValue(key, out MethodDefinition? actualMethod) ||
                !MethodBodySnapshot.Create(expectedMethods[key]).Equals(MethodBodySnapshot.Create(actualMethod)))
                yield return key;
        }
    }

    private void RequireNoClr4FrameworkReferences(ModuleDefinition module, string label)
    {
        foreach (AssemblyNameReference reference in module.AssemblyReferences)
        {
            bool frameworkAssembly = reference.Name is "mscorlib" or "System" or "System.Core";
            if (frameworkAssembly && reference.Version.Major >= 4)
                failures.Add($"The {label} has a CLR-4 reference: {reference.FullName}");
        }
    }

    private void Require(bool condition, string failure)
    {
        if (!condition)
            failures.Add(failure);
    }

    private static void WriteSnapshot(string directory, string name, MethodDefinition method)
    {
        File.WriteAllLines(Path.Combine(directory, name), MethodBodySnapshot.Create(method).Lines);
    }

    private static Dictionary<string, TypeDefinition> TypesByName(ModuleDefinition module)
    {
        return PatchTarget.Flatten(module.Types).ToDictionary(type => type.FullName, StringComparer.Ordinal);
    }

    private static Dictionary<string, MethodDefinition> MethodsByKey(ModuleDefinition module)
    {
        return PatchTarget.Flatten(module.Types)
            .SelectMany(type => type.Methods)
            .ToDictionary(MethodKey, StringComparer.Ordinal);
    }

    private static HashSet<string> AssemblyReferences(ModuleDefinition module)
    {
        return new HashSet<string>(
            module.AssemblyReferences.Select(reference => reference.FullName),
            StringComparer.Ordinal);
    }

    private static string[] MemberKeys<T>(IEnumerable<T> members) where T : IMemberDefinition
    {
        return members
            .Select(member => MemberKey(member))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string MemberKey(IMemberDefinition member)
    {
        int attributes = member switch
        {
            FieldDefinition field => (int)field.Attributes,
            MethodDefinition method => (int)method.Attributes,
            PropertyDefinition property => (int)property.Attributes,
            EventDefinition @event => (int)@event.Attributes,
            _ => throw new InvalidOperationException("Unsupported member definition: " + member.GetType().FullName)
        };
        return attributes.ToString(CultureInfo.InvariantCulture) + "|" + member.FullName;
    }

    private static string MethodKey(MethodDefinition method)
    {
        return ((int)method.Attributes).ToString(CultureInfo.InvariantCulture) + "|" + method.FullName;
    }
}

internal sealed class MethodBodySnapshot : IEquatable<MethodBodySnapshot>
{
    internal string[] Lines { get; }

    private MethodBodySnapshot(IEnumerable<string> lines)
    {
        Lines = lines.ToArray();
    }

    internal static MethodBodySnapshot Create(MethodDefinition method, int leadingInstructionsToIgnore = 0)
    {
        if (!method.HasBody)
            return new MethodBodySnapshot(new[] { "body=<none>" });

        List<Instruction> instructions = method.Body.Instructions.Skip(leadingInstructionsToIgnore).ToList();
        Dictionary<Instruction, int> indexes = instructions
            .Select((instruction, index) => new { instruction, index })
            .ToDictionary(item => item.instruction, item => item.index);

        List<string> lines = new()
        {
            "method=" + method.FullName,
            "init_locals=" + method.Body.InitLocals,
            "max_stack=" + method.Body.MaxStackSize,
            "locals=" + method.Body.Variables.Count
        };
        lines.AddRange(method.Body.Variables.Select(variable =>
            $"local[{variable.Index}]={variable.VariableType.FullName}|pinned={variable.IsPinned}"));

        for (int index = 0; index < instructions.Count; index++)
        {
            Instruction instruction = instructions[index];
            lines.Add(
                $"IL_{index:D4}={instruction.OpCode.Code}|{NormalizeOperand(instruction.Operand, indexes)}");
        }

        lines.Add("handlers=" + method.Body.ExceptionHandlers.Count);
        lines.AddRange(method.Body.ExceptionHandlers.Select(handler => NormalizeHandler(handler, indexes)));
        return new MethodBodySnapshot(lines);
    }

    public bool Equals(MethodBodySnapshot? other)
    {
        return other is not null && Lines.SequenceEqual(other.Lines, StringComparer.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MethodBodySnapshot);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (string line in Lines)
            hash.Add(line, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static string NormalizeOperand(object? operand, Dictionary<Instruction, int> indexes)
    {
        return operand switch
        {
            null => "<none>",
            Instruction target => "instruction:" + InstructionIndex(target, indexes),
            Instruction[] targets => "instructions:" + string.Join(",", targets.Select(target => InstructionIndex(target, indexes))),
            MethodReference method => "method:" + method.FullName,
            FieldReference field => "field:" + field.FullName,
            TypeReference type => "type:" + type.FullName,
            ParameterDefinition parameter => "parameter:" + parameter.Index,
            VariableDefinition variable => "variable:" + variable.Index,
            string text => "string:" + text,
            IFormattable formattable => operand.GetType().FullName + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => operand.GetType().FullName + ":" + operand
        };
    }

    private static string NormalizeHandler(
        ExceptionHandler handler,
        Dictionary<Instruction, int> indexes)
    {
        return string.Join("|", new[]
        {
            handler.HandlerType.ToString(),
            handler.CatchType?.FullName ?? "<none>",
            InstructionIndex(handler.TryStart, indexes),
            InstructionIndex(handler.TryEnd, indexes),
            InstructionIndex(handler.HandlerStart, indexes),
            InstructionIndex(handler.HandlerEnd, indexes),
            InstructionIndex(handler.FilterStart, indexes)
        });
    }

    private static string InstructionIndex(
        Instruction? instruction,
        Dictionary<Instruction, int> indexes)
    {
        if (instruction is null)
            return "<end>";
        return indexes.TryGetValue(instruction, out int index)
            ? index.ToString(CultureInfo.InvariantCulture)
            : "<outside>";
    }
}
