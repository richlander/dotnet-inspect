using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;
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
    public void CoreQueries_DoNotAcquireResearchOrDecompilerProjects()
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
        Assert.DoesNotContain("ILInspector.Decompiler", closure);
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
