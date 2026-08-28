using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
    public async Task LocalOnlyHost_InspectsCallerSuppliedLocalAssembly()
    {
        string assemblyPath = typeof(AssemblyOnlyInspector).Assembly.Location;
        string temporaryDirectory =
            Directory.CreateTempSubdirectory(
                "dotnet-inspect-artifact-").FullName;
        string temporaryAssembly = Path.Combine(
            temporaryDirectory,
            Path.GetFileName(assemblyPath));
        File.Copy(assemblyPath, temporaryAssembly);
        try
        {
            Assert.Equal(
                "DotnetInspector.AssemblyOnlyHost.Fixture",
                await AssemblyOnlyInspector
                    .ReadAssemblyNameAfterDeletingSourceAsync(
                        temporaryAssembly,
                        TestContext.Current.CancellationToken));
            Assert.False(File.Exists(temporaryAssembly));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void LocalOnlyHostClosure_ExcludesPackageFeedCacheAndArchiveImplementations()
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
            path => Path.GetFileNameWithoutExtension(path) == "DotnetInspector.Artifacts");
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "DotnetInspector.Artifacts.Local");
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "DotnetInspector.Artifacts.Workspaces");
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "CSharpText");
        Assert.Contains(
            "Microsoft.CodeAnalysis.BannedApiAnalyzers",
            CommandErrorOwnershipTests.ProjectPackageDependencies(project));
        AssertNoForbiddenImplementations(
            root,
            closure,
            PackageOrStorageImplementationProjects);
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
    public void ArtifactContractsClosure_ExcludesMetadataPackagesAndStorageImplementations()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string project = Path.Combine(
            root,
            "src",
            "DotnetInspector.Artifacts",
            "DotnetInspector.Artifacts.csproj");
        Assert.True(
            File.Exists(project),
            $"Artifact contracts project not found: {project}");

        HashSet<string> closure =
            CommandErrorOwnershipTests.EvaluatedProjectClosure(project);
        Assert.Equal(
            [Path.GetFullPath(project)],
            closure.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "Microsoft.CodeAnalysis.BannedApiAnalyzers",
                "Microsoft.NET.ILLink.Tasks",
            ],
            CommandErrorOwnershipTests.ProjectPackageDependencies(project)
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Contains(
            "<IsAotCompatible>true</IsAotCompatible>",
            File.ReadAllText(project));
    }

    [Fact]
    public void ArtifactWorkspaceClosure_ExcludesMetadataPackagesAndStorageImplementations()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string project = Path.Combine(
            root,
            "src",
            "DotnetInspector.Artifacts.Workspaces",
            "DotnetInspector.Artifacts.Workspaces.csproj");
        HashSet<string> closure =
            CommandErrorOwnershipTests.EvaluatedProjectClosure(project);

        Assert.Equal(
            [
                Path.GetFullPath(project),
                Path.Combine(
                    root,
                    "src",
                    "DotnetInspector.Artifacts",
                    "DotnetInspector.Artifacts.csproj"),
            ],
            closure.Order(StringComparer.Ordinal));
        AssertNoForbiddenImplementations(
            root,
            closure,
            PackageOrStorageImplementationProjects);
        Assert.Equal(
            [
                "Microsoft.CodeAnalysis.BannedApiAnalyzers",
                "Microsoft.NET.ILLink.Tasks",
            ],
            CommandErrorOwnershipTests.ProjectPackageDependencies(project)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalArtifactAdapterClosure_ExcludesMetadataPackagesAndStorageImplementations()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string project = Path.Combine(
            root,
            "src",
            "DotnetInspector.Artifacts.Local",
            "DotnetInspector.Artifacts.Local.csproj");
        HashSet<string> closure =
            CommandErrorOwnershipTests.EvaluatedProjectClosure(project);

        Assert.Equal(
            [
                Path.GetFullPath(project),
                Path.Combine(
                    root,
                    "src",
                    "DotnetInspector.Artifacts",
                    "DotnetInspector.Artifacts.csproj"),
            ],
            closure.Order(StringComparer.Ordinal));
        AssertNoForbiddenImplementations(
            root,
            closure,
            PackageOrStorageImplementationProjects);
        Assert.Equal(
            [
                "Microsoft.CodeAnalysis.BannedApiAnalyzers",
                "Microsoft.NET.ILLink.Tasks",
            ],
            CommandErrorOwnershipTests.ProjectPackageDependencies(project)
                .Order(StringComparer.OrdinalIgnoreCase));
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
    public void MetadataPrimitives_RemainsLeaf()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string project = Path.Combine(
            root,
            "src",
            "ILInspector.MetadataPrimitives",
            "ILInspector.MetadataPrimitives.csproj");
        HashSet<string> closure =
            CommandErrorOwnershipTests.EvaluatedProjectClosure(project);

        Assert.Equal(
            [Path.GetFullPath(project)],
            closure.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "Microsoft.CodeAnalysis.BannedApiAnalyzers",
                "Microsoft.NET.ILLink.Tasks",
            ],
            CommandErrorOwnershipTests.ProjectPackageDependencies(project)
                .Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void MetadataPrimitives_MetadataRootClassifierIsIsolated()
    {
        string projectDirectory = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "ILInspector.MetadataPrimitives");
        var sources = Directory.EnumerateFiles(
                projectDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(path => (
                Name: Path.GetFileName(path),
                Source: File.ReadAllText(path)))
            .ToArray();
        string[] metadataBlockReaders = sources
            .Where(file => file.Source.Contains(
                ".GetMetadata()",
                StringComparison.Ordinal))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] markerReaders = sources
            .Where(file => file.Source.Contains(
                "WindowsRuntime",
                StringComparison.Ordinal))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string classifier = Assert.Single(
            sources,
            file => file.Name
                == $"{nameof(MetadataImageFormatClassifier)}.cs").Source;

        Assert.Equal(
            [$"{nameof(MetadataImageFormatClassifier)}.cs"],
            markerReaders);
        Assert.All(
            metadataBlockReaders,
            name => Assert.Contains(
                name,
                new[]
                {
                    $"{nameof(MetadataImageFormatClassifier)}.cs",
                    "MethodSemanticsRowReader.cs",
                }));
        Assert.DoesNotContain("GetMetadataReader", classifier);
        Assert.DoesNotContain("TableIndex", classifier);
        Assert.DoesNotContain("MetadataTokens", classifier);
        Assert.DoesNotContain("ReadSerializedString", classifier);
        Assert.DoesNotContain("ReadUTF8", classifier);
    }

    [Fact]
    public void MetadataPrimitives_MethodSemanticsReaderIsIsolated()
    {
        string projectDirectory = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "ILInspector.MetadataPrimitives");
        var sources = Directory.EnumerateFiles(
                projectDirectory,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(path => (
                Name: Path.GetFileName(path),
                Source: File.ReadAllText(path)))
            .ToArray();
        string[] tableLayoutReaders = sources
            .Where(file =>
                file.Source.Contains(
                    ".GetTableMetadataOffset(",
                    StringComparison.Ordinal)
                || file.Source.Contains(
                    ".GetTableRowSize(",
                    StringComparison.Ordinal))
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string reader = Assert.Single(
            sources,
            file => file.Name
                == $"{nameof(MethodSemanticsRowReader)}.cs").Source;
        System.Reflection.MethodInfo read = Assert.Single(
            typeof(MethodSemanticsRowReader).GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly));

        Assert.Equal(
            [$"{nameof(MethodSemanticsRowReader)}.cs"],
            tableLayoutReaders);
        Assert.Contains("TableIndex.MethodSemantics", reader);
        Assert.DoesNotContain("TableIndex table", reader);
        Assert.DoesNotContain("TableIndex tableIndex", reader);
        Assert.Equal(
            [
                typeof(PEReader),
                typeof(MethodSemanticsReadBudget),
            ],
            read.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(MethodSemanticsReadResult), read.ReturnType);
        Assert.All(
            new[]
            {
                typeof(MethodSemanticsAssociationKind),
                typeof(MethodSemanticsRow),
                typeof(MethodSemanticsMalformedReason),
                typeof(MethodSemanticsReadResult),
                typeof(MethodSemanticsReadBudget),
                typeof(MethodSemanticsRowReader),
            },
            type => Assert.Equal(
                "ILInspector.MetadataPrimitives",
                type.Namespace));
        Assert.DoesNotContain(
            typeof(MethodSemanticsRowReader).Assembly.ExportedTypes
                .SelectMany(type => type.GetMethods())
                .SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(TableIndex));
        Type[] forbiddenRetention =
        [
            typeof(PEReader),
            typeof(MetadataReader),
            typeof(PEMemoryBlock),
            typeof(BlobReader),
        ];
        Assert.DoesNotContain(
            typeof(MethodSemanticsRowReader).Assembly.ExportedTypes
                .Where(type =>
                    type.Namespace == "ILInspector.MetadataPrimitives"
                    && type.Name.StartsWith(
                        "MethodSemantics",
                        StringComparison.Ordinal))
                .SelectMany(type => type.GetFields(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)),
            field => forbiddenRetention.Contains(field.FieldType));
    }

    [Fact]
    public void MetadataPrimitives_MethodSemanticsPlatformProbesAreWired()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string probeDirectory = Path.Combine(
            root,
            "tests",
            "ILInspector.MetadataPrimitives.PlatformProbe");
        string probe = File.ReadAllText(
            Path.Combine(probeDirectory, "Program.cs"));
        string nativeProject = File.ReadAllText(
            Path.Combine(
                probeDirectory,
                "MethodSemanticsPlatformProbe.csproj"));
        string browserProject = File.ReadAllText(
            Path.Combine(
                probeDirectory,
                "MethodSemanticsBrowserProbe.csproj"));
        string runner = File.ReadAllText(
            Path.Combine(
                root,
                "eng",
                "run-method-semantics-platform-probe.sh"));
        string workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "ci.yml"));
        string changeDetection = File.ReadAllText(
            Path.Combine(root, "eng", "ci-detect-changes.sh"));

        Assert.Contains("MethodSemanticsRowReader.Read(", probe);
        Assert.Contains("row.RawSemantics", probe);
        Assert.Contains("row.AssociationKind", probe);
        Assert.Contains("row.AssociationRowNumber", probe);
        Assert.Contains("MetadataTokens.GetRowNumber(row.Method)", probe);
        Assert.Contains("<PublishAot>true</PublishAot>", nativeProject);
        Assert.Contains(
            "<IsPublishable>true</IsPublishable>",
            nativeProject);
        Assert.Contains(
            "Microsoft.NET.Sdk.WebAssembly",
            browserProject);
        Assert.Contains(
            "<IsPublishable>true</IsPublishable>",
            browserProject);
        Assert.Contains(
            "run-method-semantics-platform-probe.sh nativeaot",
            workflow);
        Assert.Contains(
            "run-method-semantics-platform-probe.sh browser",
            workflow);
        Assert.Contains("dotnet publish", runner);
        Assert.Contains("node --experimental-default-type=module", runner);
        Assert.Contains(
            "tests/ILInspector.MetadataPrimitives.PlatformProbe/*) "
                + "CODE=true; WEB=true",
            changeDetection);
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
