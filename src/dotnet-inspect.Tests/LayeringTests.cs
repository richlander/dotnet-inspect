using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
            "ILInspector.Decompiler.Tests",
            "ILInspector.Metadata.Tests",
            "decompiler-harness",
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
