using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
        Dictionary<string, HashSet<string>> decompilerCalls =
            ReadCallGraph(typeof(ILInspector.Decompiler.MemberBodyProducer).Assembly.Location);
        HashSet<string> descriptorDesignatingMethods =
            FindTransitivelyTainted(
                decompilerCalls,
                ReadDirectCallers(
                    typeof(ILInspector.Decompiler.MemberBodyProducer).Assembly.Location,
                    "ResolvedAssemblyReference::Create(",
                    "ResolvedAssemblyReference::CreateFromPath(",
                    "ResolvedAssemblyReference::TryCreateFromPath("));
        List<string> calls = ReadCalls(
                typeof(LibraryInspection).Assembly.Location)
            .Where(call => descriptorDesignatingMethods.Contains(CallKey(call)))
            .ToList();

        Assert.NotEmpty(calls);
        Assert.All(
            calls,
            call => Assert.Contains(
                "ILInspector.Metadata.ResolvedAssemblyReference",
                call));
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
                        instruction.OpCodeName is "call" or "callvirt"
                        && instruction.Operand is not null)
                    .Select(static instruction => instruction.Operand!));
        }

        return calls;
    }

    static Dictionary<string, HashSet<string>> ReadCallGraph(
        string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var graph = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            string key =
                $"{reader.GetFullTypeName(reader.GetTypeDefinition(method.GetDeclaringType()))}"
                + $"::{reader.GetString(method.Name)}";
            graph.TryAdd(key, []);
            List<ILInstructionText>? instructions =
                MetadataInstructionProducer.DisassembleMethod(
                    peReader,
                    reader,
                    methodHandle);
            if (instructions is null)
                continue;

            foreach (string call in instructions
                .Where(static instruction =>
                    instruction.OpCodeName is "call" or "callvirt"
                    && instruction.Operand is not null)
                .Select(static instruction => instruction.Operand!))
            {
                graph[key].Add(CallKey(call));
            }
        }

        return graph;
    }

    static HashSet<string> ReadDirectCallers(
        string assemblyPath,
        params string[] targets)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var callers = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodDefinitionHandle methodHandle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
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
                $"{reader.GetFullTypeName(reader.GetTypeDefinition(method.GetDeclaringType()))}"
                + $"::{reader.GetString(method.Name)}");
        }

        return callers;
    }

    static HashSet<string> FindTransitivelyTainted(
        IReadOnlyDictionary<string, HashSet<string>> calls,
        IEnumerable<string> direct)
    {
        var tainted = new HashSet<string>(direct, StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach ((string caller, HashSet<string> callees) in calls)
            {
                if (callees.Overlaps(tainted) && tainted.Add(caller))
                    changed = true;
            }
        }
        while (changed);

        return tainted;
    }

    static string CallKey(string operand)
    {
        int separator = operand.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
            return operand;
        int start = operand.LastIndexOf(' ', separator);
        int end = operand.IndexOf('(', separator);
        if (end < 0)
            end = operand.Length;
        return operand[(start + 1)..end];
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
}
