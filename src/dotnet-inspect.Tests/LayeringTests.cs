using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Text.Json;
using DotnetInspector.AssemblyOnlyHost.Fixture;
using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Queries;

namespace DotnetInspector.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void AssemblyOnlyHost_InspectsCallerSuppliedLocalAssembly()
    {
        string assemblyPath = typeof(AssemblyOnlyInspector).Assembly.Location;

        Assert.Equal(
            "DotnetInspector.AssemblyOnlyHost.Fixture",
            AssemblyOnlyInspector.ReadAssemblyName(assemblyPath));
    }

    [Fact]
    public void AssemblyOnlyHostClosure_ExcludesPackageAndNuGetImplementations()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string project = Path.Combine(
            root,
            "tests",
            "DotnetInspector.AssemblyOnlyHost.Fixture",
            "DotnetInspector.AssemblyOnlyHost.Fixture.csproj");
        Assert.True(File.Exists(project), $"Assembly-only host project not found: {project}");
        HashSet<string> closure = CommandErrorOwnershipTests.EvaluatedProjectClosure(project);

        Assert.Contains(Path.GetFullPath(project), closure);
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "ILInspector.Metadata");
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "CSharpText");
        Assert.Contains(
            "Microsoft.CodeAnalysis.BannedApiAnalyzers",
            CommandErrorOwnershipTests.ProjectPackageDependencies(project));
        AssertNoForbiddenImplementations(root, closure, PackageImplementationProjects);
    }

    [Fact]
    public void MetadataClosure_ExcludesPackageAndStorageImplementations()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string project = Path.Combine(
            root,
            "src",
            "ILInspector.Metadata",
            "ILInspector.Metadata.csproj");
        Assert.True(File.Exists(project), $"Metadata project not found: {project}");
        HashSet<string> closure = CommandErrorOwnershipTests.EvaluatedProjectClosure(project);

        Assert.Contains(Path.GetFullPath(project), closure);
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "ILInspector.MetadataPrimitives");
        AssertNoForbiddenImplementations(root, closure, PackageOrStorageImplementationProjects);
    }

    [Fact]
    public void PackageAssetsReader_IncludesResolvedTransitivePackages()
    {
        using JsonDocument assets = JsonDocument.Parse(
            """
            {
              "libraries": {
                "Local.Transitive.Wrapper/1.0.0": { "type": "package" },
                "NuGet.Protocol/7.3.0": { "type": "package" },
                "Local.Project/1.0.0": { "type": "project" }
              }
            }
            """);

        Assert.Equal(
            ["Local.Transitive.Wrapper", "NuGet.Protocol"],
            CommandErrorOwnershipTests.PackageDependenciesFromAssets(assets.RootElement)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MetadataAndInstructions_DoNotReferenceEachOther()
    {
        Assert.DoesNotContain(
            typeof(AssemblyInspectionSession).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "ILInspector.Instructions");
        Assert.DoesNotContain(
            typeof(InstructionProducer).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "ILInspector.Metadata");
    }

    [Fact]
    public void MetadataNameMatching_DoesNotDependOnFindingBackedText()
    {
        Assert.Equal(
            "ILInspector.MetadataPrimitives",
            typeof(StringDistance).Assembly.GetName().Name);

        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "ILInspector.Metadata",
            "ILInspector.Metadata.csproj");
        string[] closure = CommandErrorOwnershipTests.ProjectClosure(project)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.Contains("ILInspector.MetadataPrimitives", closure);
        Assert.DoesNotContain("ILInspector.Text", closure);
    }

    [Fact]
    public void InstructionDiff_DoesNotExpandInstructionSubstrate()
    {
        Assert.Equal(
            "ILInspector.ILDiff",
            typeof(IlBodyDiff).Assembly.GetName().Name);

        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "ILInspector.Instructions",
            "ILInspector.Instructions.csproj");
        string[] closure = CommandErrorOwnershipTests.ProjectClosure(project)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.DoesNotContain("ILInspector.ILDiff", closure);
        Assert.DoesNotContain("ILInspector.Findings", closure);
        Assert.DoesNotContain("ILInspector.Text", closure);
    }

    [Fact]
    public void CoreQueries_AcquireDecompilerButNotResearch()
    {
        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "DotnetInspector.Queries",
            "DotnetInspector.Queries.csproj");
        string[] closure = CommandErrorOwnershipTests.ProjectClosure(project)
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.DoesNotContain("ILInspector.Research", closure);
        Assert.Contains("ILInspector.Decompiler", closure);
        Assert.Equal(
            "DotnetInspector.Queries",
            typeof(ApiComparisonQuery).Assembly.GetName().Name);
    }

    [Fact]
    public void ImplementationQuery_ReturnsResearchOwnedPresentationNeutralResult()
    {
        Assert.Equal(
            "ILInspector.Research",
            typeof(ImplementationDiffResult).Assembly.GetName().Name);
        Assert.DoesNotContain(
            typeof(ImplementationDiffResult).Assembly
                .GetReferencedAssemblies(),
            reference => reference.Name == "Markout");
    }

    [Fact]
    public void Metadata_FriendsOnlyTestAssemblies()
    {
        string[] friends = typeof(AssemblyInspectionSession).Assembly
            .GetCustomAttributes(typeof(InternalsVisibleToAttribute), inherit: false)
            .Cast<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0])
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected =
        [
            "DotnetInspector.MetadataRendering.Tests",
            "ILInspector.Metadata.Tests",
            "dotnet-inspect.Tests",
        ];

        Assert.Equal(expected, friends);
    }

    [Fact]
    public void Cli_DoesNotReferenceRawMetadataReaders()
    {
        using var stream = File.OpenRead(typeof(LibraryInspection).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var referencedTypes = reader.TypeReferences
            .Select(handle => reader.GetTypeReference(handle))
            .Select(type => $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}")
            .ToList();

        Assert.DoesNotContain(
            "System.Reflection.PortableExecutable.PEReader",
            referencedTypes);
        Assert.DoesNotContain(
            "System.Reflection.Metadata.MetadataReader",
            referencedTypes);
    }

    [Fact]
    public void Cli_MetadataSourceFactories_RetainAcquisitionDescriptors()
    {
        string decompilerPath =
            typeof(ILInspector.Decompiler.MemberBodyProducer)
                .Assembly.Location;
        string[] productAssemblies = OwnedProductAssemblyPaths();
        CompiledCallGraph productCalls =
            ReadCallGraph(productAssemblies);
        HashSet<string> descriptorDesignatingMethods =
            FindTransitivelyTainted(
                productCalls.Calls,
                ReadDirectCallers(
                    [decompilerPath],
                    "ResolvedAssemblyReference::Create(",
                    "ResolvedAssemblyReference::CreateFromPath(",
                    "ResolvedAssemblyReference::TryCreateFromPath(",
                    "ResolvedAssemblyReference::CreateFromPathIfManaged(",
                    "ResolvedAssemblyReference::CreateFromModulePathIfManaged(",
                    "ResolvedAssemblyReference::CreateInspectionReferenceFromPathIfManaged(",
                    "ResolvedAssemblyReference::CreateFromStreamIfManaged("),
                productCalls.AcquisitionBearingBoundaries);
        List<string> cliCalls =
            ReadCalls(typeof(LibraryInspection).Assembly.Location);
        List<string> violations = cliCalls
            .Where(call => descriptorDesignatingMethods.Contains(CallKey(call)))
            .Where(call => !call.Contains(
                "ILInspector.Metadata.ResolvedAssemblyReference",
                StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(descriptorDesignatingMethods);
        Assert.Contains(
            cliCalls,
            call => call.Contains(
                "ILInspector.Metadata.ResolvedAssemblyReference",
                StringComparison.Ordinal));
        Assert.True(
            violations.Count == 0,
            "CLI calls can reach path designation without a retained "
            + "acquisition:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                violations.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void DescriptorFactoryReachability_TraversesWrapperIndirection()
    {
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Wrapper::Renamed"] = ["Core::Designates"],
            ["Core::Designates"] = [],
        };

        HashSet<string> tainted =
            FindTransitivelyTainted(calls, ["Core::Designates"]);

        Assert.Contains("Wrapper::Renamed", tainted);
    }

    [Fact]
    public void CompiledCallGraph_TraversesDelegateFactoryEdges()
    {
        CompiledCallGraph graph =
            ReadCallGraph(typeof(LayeringTests).Assembly.Location);

        Assert.Contains(
            graph.Calls[
                "DotnetInspector.Tests.LayeringTests"
                + "::DescriptorFactoryDelegateTarget("
                + "String, AssemblyResolutionProvenance)"],
            call => call.StartsWith(
                "ILInspector.Metadata.ResolvedAssemblyReference"
                    + "::CreateFromPathIfManaged(",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CompiledCallGraph_TraversesConstructorEdges()
    {
        string assemblyPath = typeof(LayeringTests).Assembly.Location;
        CompiledCallGraph graph = ReadCallGraph(assemblyPath);
        string caller = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "::ConstructDescriptorFixture(",
                StringComparison.Ordinal));
        string constructor = Assert.Single(
            graph.Calls[caller],
            key => key.Contains(
                "DescriptorConstructorFixture::.ctor(",
                StringComparison.Ordinal));

        Assert.Contains(
            ReadCalls(assemblyPath),
            call => CallKey(call) == constructor);

        HashSet<string> tainted = FindTransitivelyTainted(
            graph.Calls,
            ReadDirectCallers(
                [assemblyPath],
                "ResolvedAssemblyReference::CreateFromPathIfManaged("),
            graph.AcquisitionBearingBoundaries);
        Assert.Contains(caller, tainted);
    }

    [Fact]
    public void CompiledCallGraph_TraversesFieldBackedDelegateEdges()
    {
        CompiledCallGraph graph =
            ReadCallGraph(typeof(LayeringTests).Assembly.Location);
        string caller = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "DescriptorFactoryFieldFixture::Invoke(",
                StringComparison.Ordinal));

        Assert.Contains(
            graph.Calls[caller],
            call => call.StartsWith(
                "ILInspector.Metadata.ResolvedAssemblyReference"
                    + "::CreateFromPathIfManaged(",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AcquisitionBoundaryRecognition_UsesExactMetadataTypes()
    {
        string assemblyPath = typeof(LayeringTests).Assembly.Location;
        CompiledCallGraph graph = ReadCallGraph(assemblyPath);
        string exactBoundary = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "::ExactAcquisitionBoundary(",
                StringComparison.Ordinal));
        string lookalike = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "ResolvedAssemblyReferenceAdapter::Create(",
                StringComparison.Ordinal));
        string outProducer = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "::TryCreateOutAcquisition(",
                StringComparison.Ordinal));
        string refBoundary = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "::RefAcquisitionBoundary(",
                StringComparison.Ordinal));
        string inBoundary = Assert.Single(
            graph.Calls.Keys,
            key => key.Contains(
                "::InAcquisitionBoundary(",
                StringComparison.Ordinal));

        Assert.Contains(
            exactBoundary,
            graph.AcquisitionBearingBoundaries);
        Assert.DoesNotContain(
            lookalike,
            graph.AcquisitionBearingBoundaries);
        Assert.DoesNotContain(
            outProducer,
            graph.AcquisitionBearingBoundaries);
        Assert.Contains(
            refBoundary,
            graph.AcquisitionBearingBoundaries);
        Assert.Contains(
            inBoundary,
            graph.AcquisitionBearingBoundaries);

        HashSet<string> tainted = FindTransitivelyTainted(
            graph.Calls,
            ReadDirectCallers(
                [assemblyPath],
                "ResolvedAssemblyReference::CreateFromPathIfManaged("),
            graph.AcquisitionBearingBoundaries);
        Assert.Contains(lookalike, tainted);
    }

    static ResolvedAssemblyReference? DescriptorFactoryDelegateTarget(
        string path,
        AssemblyResolutionProvenance provenance)
    {
        Func<
            string,
            AssemblyResolutionProvenance,
            ResolvedAssemblyReference?> factory =
                ResolvedAssemblyReference.CreateFromPathIfManaged;
        return factory(path, provenance);
    }

    sealed class DescriptorConstructorFixture
    {
        internal DescriptorConstructorFixture(
            string path,
            AssemblyResolutionProvenance provenance)
        {
            _ = ResolvedAssemblyReference.CreateFromPathIfManaged(
                path,
                provenance);
        }
    }

    static void ConstructDescriptorFixture(
        string path,
        AssemblyResolutionProvenance provenance) =>
        _ = new DescriptorConstructorFixture(path, provenance);

    static class DescriptorFactoryFieldFixture
    {
        static readonly Func<
            string,
            AssemblyResolutionProvenance,
            ResolvedAssemblyReference?> Factory =
                ResolvedAssemblyReference.CreateFromPathIfManaged;

        internal static ResolvedAssemblyReference? Invoke(
            string path,
            AssemblyResolutionProvenance provenance) =>
            Factory(path, provenance);
    }

    static void ExactAcquisitionBoundary(
        ResolvedAssemblyReference assembly)
    {
        _ = assembly.Identity;
    }

    static bool TryCreateOutAcquisition(
        string path,
        AssemblyResolutionProvenance provenance,
        out ResolvedAssemblyReference? assembly)
    {
        assembly =
            ResolvedAssemblyReference.CreateFromPathIfManaged(
                path,
                provenance);
        return assembly is not null;
    }

    static void RefAcquisitionBoundary(
        ref ResolvedAssemblyReference assembly)
    {
        _ = assembly.Identity;
    }

    static void InAcquisitionBoundary(
        in ResolvedAssemblyReference assembly)
    {
        _ = assembly.Identity;
    }

    static class ResolvedAssemblyReferenceAdapter
    {
        internal static ResolvedAssemblyReference? Create(
            string path,
            AssemblyResolutionProvenance provenance) =>
            ResolvedAssemblyReference.CreateFromPathIfManaged(
                path,
                provenance);
    }

    static string[] OwnedProductAssemblyPaths()
    {
        string directory = Path.GetDirectoryName(
            typeof(LibraryInspection).Assembly.Location)!;
        return Directory.GetFiles(directory, "*.dll")
            .Where(path =>
            {
                string name = Path.GetFileNameWithoutExtension(path);
                return name is "dotnet-inspect"
                    or "CSharpText"
                    or "InertText"
                    || name.StartsWith("ILInspector.", StringComparison.Ordinal)
                    || name.StartsWith("DotnetInspector.", StringComparison.Ordinal);
            })
            .Where(path =>
                !Path.GetFileNameWithoutExtension(path)
                    .EndsWith(".Tests", StringComparison.Ordinal))
            .ToArray();
    }

    static List<string> ReadCalls(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var calls = new List<string>();
        foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
        {
            List<ILInstructionText>? instructions =
                MetadataInstructionProducer.DisassembleMethod(
                    peReader,
                    reader,
                    methodHandle);
            if (instructions is null)
                continue;

            calls.AddRange(
                instructions
                    .Where(static instruction =>
                        instruction.OpCodeName
                            is "call"
                            or "callvirt"
                            or "newobj"
                            or "ldftn"
                            or "ldvirtftn"
                        && instruction.Operand is not null)
                    .Select(static instruction => instruction.Operand!));
        }

        return calls;
    }

    sealed record CompiledCallGraph(
        Dictionary<string, HashSet<string>> Calls,
        HashSet<string> AcquisitionBearingBoundaries);

    static CompiledCallGraph ReadCallGraph(
        params string[] assemblyPaths)
    {
        var graph = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        var boundaries = new HashSet<string>(StringComparer.Ordinal);
        var delegateTargetsByField =
            new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);
        var delegateInvocations =
            new List<(
                string Caller,
                ImmutableArray<string> CandidateFields)>();
        foreach (string assemblyPath in assemblyPaths)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            string assemblyName = reader.IsAssembly
                ? reader.GetString(
                    reader.GetAssemblyDefinition().Name)
                : Path.GetFileNameWithoutExtension(assemblyPath);
            foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                string key = MethodKey(reader, method);
                graph.TryAdd(key, []);
                if (IsAcquisitionBearingBoundary(reader, method))
                    boundaries.Add(key);
                List<ILInstructionText>? instructions =
                    MetadataInstructionProducer.DisassembleMethod(
                        peReader,
                        reader,
                        methodHandle);
                if (instructions is null)
                    continue;

                foreach (string call in instructions
                    .Where(static instruction =>
                        instruction.OpCodeName
                            is "call"
                            or "callvirt"
                            or "newobj"
                            or "ldftn"
                            or "ldvirtftn"
                        && instruction.Operand is not null)
                    .Select(static instruction => instruction.Operand!))
                {
                    graph[key].Add(CallKey(call));
                }

                for (int index = 0; index < instructions.Count; index++)
                {
                    ILInstructionText instruction = instructions[index];
                    if (instruction.OpCodeName is "stsfld" or "stfld"
                        && instruction.Operand is { } storedField)
                    {
                        int start = Math.Max(0, index - 4);
                        ILInstructionText? target = instructions
                            .Skip(start)
                            .Take(index - start)
                            .LastOrDefault(candidate =>
                                candidate.OpCodeName
                                    is "ldftn"
                                    or "ldvirtftn"
                                && candidate.Operand is not null);
                        bool constructsDelegate = instructions
                            .Skip(start)
                            .Take(index - start)
                            .Any(candidate =>
                                candidate.OpCodeName == "newobj");
                        if (target?.Operand is { } targetOperand
                            && constructsDelegate)
                        {
                            string field =
                                FieldKey(assemblyName, storedField);
                            if (!delegateTargetsByField.TryGetValue(
                                    field,
                                    out HashSet<string>? targets))
                            {
                                targets = new HashSet<string>(
                                    StringComparer.Ordinal);
                                delegateTargetsByField[field] = targets;
                            }
                            targets.Add(CallKey(targetOperand));
                        }
                    }
                }

                for (int index = 0; index < instructions.Count; index++)
                {
                    ILInstructionText instruction = instructions[index];
                    if (instruction.OpCodeName
                            is not ("call" or "callvirt")
                        || instruction.Operand?.Contains(
                            "::Invoke(",
                            StringComparison.Ordinal) != true)
                    {
                        continue;
                    }

                    ImmutableArray<string> candidateFields =
                    [
                        .. instructions
                            .Take(index)
                            .Reverse()
                            .Where(candidate =>
                                candidate.OpCodeName
                                    is "ldsfld"
                                    or "ldfld"
                                && candidate.Operand is not null)
                            .Select(candidate =>
                                FieldKey(
                                    assemblyName,
                                    candidate.Operand!)),
                    ];
                    if (!candidateFields.IsDefaultOrEmpty)
                    {
                        delegateInvocations.Add(
                            (key, candidateFields));
                    }
                }
            }
        }

        foreach ((string caller, ImmutableArray<string> candidateFields)
            in delegateInvocations)
        {
            string? field = candidateFields.FirstOrDefault(
                delegateTargetsByField.ContainsKey);
            if (field is not null)
            {
                graph[caller].UnionWith(
                    delegateTargetsByField[field]);
            }
        }

        return new CompiledCallGraph(graph, boundaries);
    }

    static HashSet<string> ReadDirectCallers(
        IReadOnlyList<string> assemblyPaths,
        params string[] targets)
    {
        var callers = new HashSet<string>(StringComparer.Ordinal);
        foreach (string assemblyPath in assemblyPaths)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                List<ILInstructionText>? instructions =
                    MetadataInstructionProducer.DisassembleMethod(
                        peReader,
                        reader,
                        methodHandle);
                if (instructions?.Any(instruction =>
                        instruction.Operand is { } operand
                        && targets.Any(target =>
                            operand.Contains(
                                target,
                                StringComparison.Ordinal))) != true)
                {
                    continue;
                }

                callers.Add(
                    MethodKey(reader, method));
            }
        }

        return callers;
    }

    static HashSet<string> FindTransitivelyTainted(
        IReadOnlyDictionary<string, HashSet<string>> calls,
        IEnumerable<string> direct,
        IReadOnlySet<string>? acquisitionBearingBoundaries = null)
    {
        var tainted = new HashSet<string>(direct, StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach ((string caller, HashSet<string> callees) in calls)
            {
                if (acquisitionBearingBoundaries?.Contains(caller) == true)
                    continue;

                if (callees.Overlaps(tainted) && tainted.Add(caller))
                    changed = true;
            }
        }
        while (changed);

        return tainted;
    }

    static bool IsAcquisitionBearingBoundary(
        MetadataReader reader,
        MethodDefinition method)
    {
        string assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : "";
        TypeDefinition declaringType =
            reader.GetTypeDefinition(method.GetDeclaringType());
        if (AcquisitionSignatureTypes.IsAcquisitionType(
                assemblyName,
                reader.GetFullTypeName(declaringType)))
        {
            return true;
        }

        MethodSignature<bool> signature = method.DecodeSignature(
            new AcquisitionSignatureTypes(assemblyName),
            genericContext: null);
        HashSet<int> outSequences = method.GetParameters()
            .Select(reader.GetParameter)
            .Where(parameter =>
                parameter.SequenceNumber > 0
                && (parameter.Attributes
                    & System.Reflection.ParameterAttributes.Out) != 0)
            .Select(parameter => parameter.SequenceNumber)
            .ToHashSet();
        for (int index = 0;
            index < signature.ParameterTypes.Length;
            index++)
        {
            if (signature.ParameterTypes[index]
                && !outSequences.Contains(index + 1))
            {
                return true;
            }
        }
        return false;
    }

    static string FieldKey(
        string assemblyName,
        string operand)
    {
        int separator = operand.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
            return operand;
        int start = operand.LastIndexOf(' ', separator);
        return $"{assemblyName}:{operand[(start + 1)..]}";
    }

    static string CallKey(string operand)
    {
        int separator = operand.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
            return operand;
        int start = operand.LastIndexOf(' ', separator);
        string key = operand[(start + 1)..];
        int parameters = key.IndexOf('(', separator - start - 1);
        if (parameters < 0)
            return key;

        string signature = Regex.Replace(
            key[parameters..],
            @"(?:[A-Za-z_][A-Za-z0-9_]*\.)+"
                + @"([A-Za-z_][A-Za-z0-9_`]*)",
            "$1");
        signature = Regex.Replace(signature, @"`\d+", "");
        signature = Regex.Replace(
            signature,
            @"\b(string|bool|byte|sbyte|short|ushort|int|uint|long|ulong|"
                + @"float|double|char|object|nint|nuint)\b",
            static match => match.Value switch
            {
                "string" => "String",
                "bool" => "Boolean",
                "byte" => "Byte",
                "sbyte" => "SByte",
                "short" => "Int16",
                "ushort" => "UInt16",
                "int" => "Int32",
                "uint" => "UInt32",
                "long" => "Int64",
                "ulong" => "UInt64",
                "float" => "Single",
                "double" => "Double",
                "char" => "Char",
                "object" => "Object",
                "nint" => "IntPtr",
                "nuint" => "UIntPtr",
                _ => match.Value,
            });
        return key[..parameters] + signature;
    }

    static string MethodKey(
        MetadataReader reader,
        MethodDefinition method)
    {
        MethodSignature<string> signature = method.DecodeSignature(
            SimpleSignatureTypeNames.Instance,
            genericContext: null);
        return
            $"{reader.GetFullTypeName(reader.GetTypeDefinition(method.GetDeclaringType()))}"
            + $"::{reader.GetString(method.Name)}"
            + $"({string.Join(", ", signature.ParameterTypes)})";
    }

    sealed class SimpleSignatureTypeNames
        : ISignatureTypeProvider<string, object?>
    {
        internal static readonly SimpleSignatureTypeNames Instance = new();

        public string GetArrayType(string elementType, ArrayShape shape) =>
            $"{elementType}[]";

        public string GetByReferenceType(string elementType) =>
            $"{elementType}&";

        public string GetFunctionPointerType(
            MethodSignature<string> signature) =>
            "method";

        public string GetGenericInstantiation(
            string genericType,
            ImmutableArray<string> typeArguments) =>
            $"{TrimArity(genericType)}<{string.Join(", ", typeArguments)}>";

        public string GetGenericMethodParameter(
            object? genericContext,
            int index) =>
            $"!!{index}";

        public string GetGenericTypeParameter(
            object? genericContext,
            int index) =>
            $"!{index}";

        public string GetModifiedType(
            string modifier,
            string unmodifiedType,
            bool isRequired) =>
            unmodifiedType;

        public string GetPinnedType(string elementType) =>
            elementType;

        public string GetPointerType(string elementType) =>
            $"{elementType}*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode.ToString();

        public string GetSZArrayType(string elementType) =>
            $"{elementType}[]";

        public string GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle)
                .DecodeSignature(this, genericContext);

        static string TrimArity(string name)
        {
            int arity = name.IndexOf('`', StringComparison.Ordinal);
            return arity < 0 ? name : name[..arity];
        }
    }

    sealed class AcquisitionSignatureTypes
        : ISignatureTypeProvider<bool, object?>
    {
        readonly string _currentAssembly;

        internal AcquisitionSignatureTypes(string currentAssembly) =>
            _currentAssembly = currentAssembly;

        internal static bool IsAcquisitionType(
            string assemblyName,
            string fullName) =>
            (assemblyName == "ILInspector.Metadata"
                && fullName
                    == "ILInspector.Metadata.ResolvedAssemblyReference")
            || (assemblyName == "ILInspector.Research"
                && fullName
                    == "ILInspector.Research.ImplementationAssemblyInput");

        public bool GetArrayType(bool elementType, ArrayShape shape) =>
            elementType;

        public bool GetByReferenceType(bool elementType) =>
            elementType;

        public bool GetFunctionPointerType(
            MethodSignature<bool> signature) =>
            signature.ParameterTypes.Any(
                static type => type);

        public bool GetGenericInstantiation(
            bool genericType,
            ImmutableArray<bool> typeArguments) =>
            genericType
            || typeArguments.Any(static type => type);

        public bool GetGenericMethodParameter(
            object? genericContext,
            int index) =>
            false;

        public bool GetGenericTypeParameter(
            object? genericContext,
            int index) =>
            false;

        public bool GetModifiedType(
            bool modifier,
            bool unmodifiedType,
            bool isRequired) =>
            unmodifiedType;

        public bool GetPinnedType(bool elementType) =>
            elementType;

        public bool GetPointerType(bool elementType) =>
            elementType;

        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            false;

        public bool GetSZArrayType(bool elementType) =>
            elementType;

        public bool GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            IsAcquisitionType(
                _currentAssembly,
                reader.GetFullTypeName(
                    reader.GetTypeDefinition(handle)));

        public bool GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind) =>
            IsAcquisitionType(
                ReferenceAssemblyName(reader, handle),
                reader.GetFullTypeName(
                    reader.GetTypeReference(handle)));

        public bool GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) =>
            reader.GetTypeSpecification(handle)
                .DecodeSignature(this, genericContext);

        string ReferenceAssemblyName(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            EntityHandle scope =
                reader.GetTypeReference(handle).ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
            {
                scope = reader.GetTypeReference(
                    (TypeReferenceHandle)scope).ResolutionScope;
            }

            return scope.Kind == HandleKind.AssemblyReference
                ? reader.GetString(
                    reader.GetAssemblyReference(
                        (AssemblyReferenceHandle)scope).Name)
                : _currentAssembly;
        }
    }

    [Theory]
    [InlineData("DiffCommand.cs")]
    [InlineData("TimelineCommand.cs")]
    public void BodyComparisonCommands_UseMethodBodyInspectionSession(
        string commandFile)
    {
        string projectDirectory = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "dotnet-inspect");
        string path = Path.Combine(
            projectDirectory,
            "Commands",
            commandFile);
        string source = File.ReadAllText(path);
        string commandTypeName =
            Path.GetFileNameWithoutExtension(commandFile);
        var projectSources = Directory.EnumerateFiles(
                    projectDirectory,
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(sourcePath => (
                    Path: sourcePath,
                    Source: File.ReadAllText(sourcePath)))
                .ToArray();
        string projectSource = string.Join(
            Environment.NewLine,
            projectSources.Select(file => file.Source));
        string qualifiedIndexType =
            $@"(?:global\s*::\s*)?"
            + $@"(?:@?\w+\s*\.\s*)*@?{nameof(LibraryBodyIndex)}";
        string directIndexAccess =
            $@"\b{qualifiedIndexType}\s*\.\s*"
            + $@"@?{nameof(LibraryBodyIndex.Open)}\w*\b";
        string obscuredIndexImport =
            $@"\b(?:global\s+)?using\s+"
            + $@"(?:@?\w+\s*=\s*|static\s+)"
            + $@"{qualifiedIndexType}\s*;";
        string obscuredGlobalIndexImport =
            $@"\bglobal\s+using\s+"
            + $@"(?:@?\w+\s*=\s*|static\s+)"
            + $@"{qualifiedIndexType}\s*;";
        string sessionOpen =
            $@"\b(?:\w+\.)*{nameof(MethodBodyInspectionSession)}\s*\.\s*"
            + $@"{nameof(MethodBodyInspectionSession.Open)}\w*\b";
        string[] directIndexOwners = projectSources
            .Where(file =>
                System.Text.RegularExpressions.Regex.IsMatch(
                    file.Source,
                    directIndexAccess))
            .Select(file =>
                Path.GetRelativePath(projectDirectory, file.Path)
                    .Replace(Path.DirectorySeparatorChar, '/'))
            .Order()
            .ToArray();

        Assert.Matches(
            directIndexAccess,
            $"indexes.Select({nameof(LibraryBodyIndex)}."
                + $"{nameof(LibraryBodyIndex.Open)})");
        Assert.Matches(
            directIndexAccess,
            $"global :: ILInspector . Analysis . {nameof(LibraryBodyIndex)} . "
                + $"{nameof(LibraryBodyIndex.Open)}(path)");
        Assert.Matches(
            directIndexAccess,
            $"{nameof(LibraryBodyIndex)}.@{nameof(LibraryBodyIndex.Open)}(path)");
        Assert.Matches(
            obscuredIndexImport,
            $"using BodyIndex = ILInspector.Analysis."
                + $"{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredIndexImport,
            $"using @BodyIndex = ILInspector . Analysis . "
                + $"{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredIndexImport,
            $"using BodyIndex = ILInspector.Analysis."
                + $"@{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredIndexImport,
            $"global using static ILInspector.Analysis."
                + $"{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredGlobalIndexImport,
            $"global using BodyIndex = ILInspector.Analysis."
                + $"{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredGlobalIndexImport,
            $"global using BodyIndex = global :: ILInspector . Analysis . "
                + $"{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredGlobalIndexImport,
            $"global using static ILInspector . Analysis . "
                + $"{nameof(LibraryBodyIndex)};");
        Assert.Matches(
            obscuredGlobalIndexImport,
            $"global using static ILInspector.Analysis."
                + $"@{nameof(LibraryBodyIndex)};");
        Assert.Equal(
            ["Inspectors/MethodBodyInspectionSession.cs"],
            directIndexOwners);
        Assert.DoesNotMatch(directIndexAccess, source);
        Assert.DoesNotMatch(obscuredIndexImport, projectSource);
        Assert.DoesNotMatch(obscuredGlobalIndexImport, projectSource);
        Assert.Matches(sessionOpen, source);
    }

    private static void AssertNoForbiddenImplementations(
        string root,
        IEnumerable<string> closure,
        HashSet<string> forbiddenProjects)
    {
        string[] projects = closure
            .Where(path => forbiddenProjects.Contains(
                Path.GetFileNameWithoutExtension(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] packages = closure
            .SelectMany(CommandErrorOwnershipTests.ProjectPackageDependencies)
            .Where(IsNuGetImplementationPackage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            projects.Length == 0,
            $"Forbidden package or storage projects entered the closure: {string.Join(", ", projects)}");
        Assert.True(
            packages.Length == 0,
            $"Forbidden NuGet implementation packages entered the closure: {string.Join(", ", packages)}");
    }

    private static bool IsNuGetImplementationPackage(string package) =>
        package.Equals("NuGet", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("NuGet.", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> PackageImplementationProjects =
        new(StringComparer.Ordinal)
        {
            "DotnetInspector.Packages",
            "DotnetInspector.Services",
            "NuGetFetch",
        };

    private static readonly HashSet<string> PackageOrStorageImplementationProjects =
        new(PackageImplementationProjects, StringComparer.Ordinal)
        {
            "DotnetInspector.Core",
        };
}
