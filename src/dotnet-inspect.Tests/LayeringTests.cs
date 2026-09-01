using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.AssemblyOnlyHost.Fixture;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;
using ILInspector.TypeScriptGeneration;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Queries;

namespace DotnetInspector.Tests;

public sealed class LayeringTests
{
    [Fact]
    public async Task LocalOnlyHost_PreservesArtifactRegistrationThroughAssemblyInspection()
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
            AssemblyOnlyInspectionResult result =
                await AssemblyOnlyInspector
                    .InspectAfterDeletingSourceAsync(
                        temporaryAssembly,
                        TestContext.Current.CancellationToken);
            Assert.Equal(
                "DotnetInspector.AssemblyOnlyHost.Fixture",
                result.AssemblyName);
            Assert.Same(
                result.ArtifactRegistration,
                result.AssemblyRegistration.ArtifactRegistration);
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
        Assert.Contains(
            closure,
            path => Path.GetFileNameWithoutExtension(path) == "DotnetInspector.Artifacts");
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
        Assert.Contains(
            "<IsAotCompatible>true</IsAotCompatible>",
            File.ReadAllText(project));
    }

    [Fact]
    public void MetadataPrimitives_MetadataRootClassifierIsIsolated()
    {
        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "ILInspector.MetadataPrimitives",
            "ILInspector.MetadataPrimitives.csproj");
        var sources = EvaluatedSources(project);
        MetadataApiReference[] metadataBlockReferences = MetadataApiReferences()
            .Where(reference => reference.Api == "GetMetadata")
            .ToArray();
        string[] metadataBlockReaders = metadataBlockReferences
            .Select(reference => reference.CallerType)
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
            [
                "ILInspector.Metadata.MetadataImageFormatClassifier",
                "ILInspector.MetadataPrimitives.MethodSemanticsRowReader",
            ],
            metadataBlockReaders);
        Assert.All(
            metadataBlockReferences,
            reference => Assert.Contains(
                reference.OpCode,
                new[] { ILOpCode.Call, ILOpCode.Callvirt }));
        Assert.Equal(
            [$"{nameof(MetadataImageFormatClassifier)}.cs"],
            markerReaders);
        Assert.DoesNotContain("GetMetadataReader", classifier);
        Assert.DoesNotContain("TableIndex", classifier);
        Assert.DoesNotContain("MetadataTokens", classifier);
        Assert.DoesNotContain("ReadSerializedString", classifier);
        Assert.DoesNotContain("ReadUTF8", classifier);
    }

    [Fact]
    public void Metadata_MetadataReadersRequireFormatAdmission()
    {
        string[] sites = MetadataReaderConstructionSites(
                typeof(AssemblyInspectionSession).Assembly.Location)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "ILInspector.Metadata.MetadataFormatAdmission.GetMetadataReader/1",
                "ILInspector.Metadata.MetadataFormatAdmission.GetMetadataReader/2",
            ],
            sites);
    }

    [Fact]
    public void MetadataReaderConstructionGateRecognizesProviderFactories()
    {
        string[] sites = MetadataReaderConstructionSites(
                typeof(LayeringTests).Assembly.Location)
            .ToArray();

        Assert.Contains(
            "DotnetInspector.Tests.LayeringTests.MetadataProviderFromImageFixture/1",
            sites);
        Assert.Contains(
            "DotnetInspector.Tests.LayeringTests.MetadataProviderFromStreamFixture/1",
            sites);
        Assert.Contains(
            "DotnetInspector.Tests.LayeringTests.MetadataCallSiteNestedFixture.MetadataReaderFixture/1",
            sites);
        Assert.DoesNotContain(
            "DotnetInspector.Tests.LayeringTests.PortablePdbProviderFixture/1",
            sites);
    }

    [Fact]
    public void Metadata_MetadataPredicatesRequireFormatAdmission()
    {
        Assert.Empty(
            MetadataHasMetadataSites(
                typeof(AssemblyInspectionSession).Assembly.Location));
    }

    [Fact]
    public void MetadataPredicateGateRecognizesRawHasMetadata()
    {
        Assert.Contains(
            "DotnetInspector.Tests.LayeringTests.MetadataHasMetadataFixture/1",
            MetadataHasMetadataSites(
                typeof(LayeringTests).Assembly.Location));
        Assert.Contains(
            "DotnetInspector.Tests.LayeringTests.MetadataCallSiteNestedFixture.MetadataHasMetadataFixture/1",
            MetadataHasMetadataSites(
                typeof(LayeringTests).Assembly.Location));
    }

    [Fact]
    public void Decompiler_MetadataSourceRequiresFormatAdmission()
    {
        const string metadataSource =
            "ILInspector.Decompiler.Pipeline.MetadataSource.";
        string assembly =
            typeof(ILInspector.Decompiler.Pipeline.MetadataSource)
                .Assembly.Location;

        Assert.DoesNotContain(
            MetadataHasMetadataSites(assembly),
            site => site.StartsWith(
                metadataSource,
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            MetadataReaderConstructionSites(assembly),
            site => site.StartsWith(
                metadataSource,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Analysis_MetadataReadersRequireFormatAdmission()
    {
        Assert.Empty(
            MetadataReaderConstructionSites(
                typeof(LibraryBodyIndex).Assembly.Location));
    }

    [Fact]
    public void Analysis_MetadataPredicatesRequireFormatAdmission()
    {
        Assert.Empty(
            MetadataHasMetadataSites(
                typeof(LibraryBodyIndex).Assembly.Location));
    }

    [Fact]
    public void Product_AssemblyReadersDoNotPrefetchMetadataBeforeAdmission()
    {
        string sourceRoot = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src");
        string[] sites = Directory.EnumerateFiles(
                sourceRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj"
                    + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Where(path => !path.Contains(
                ".Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "PEStreamOptions.PrefetchMetadata",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            sites.Length == 0,
            "Assembly readers must remain lazy until format admission: "
                + string.Join(", ", sites));
    }

    [Fact]
    public void RemainingProduct_MetadataReadersRequireFormatAdmission()
    {
        AssertAdmissionClosed(
            MetadataReaderConstructionSites,
            typeof(ILInspector.Decompiler.Pipeline.MetadataSource),
            typeof(ResearchMatch),
            typeof(IlAssemblyDiff),
            typeof(AssemblyContextStructuralCloneRetrievalQuery),
            typeof(PlatformResolver),
            typeof(JsExportSurfaceLoader));
    }

    [Fact]
    public void RemainingProduct_MetadataPredicatesRequireFormatAdmission()
    {
        AssertAdmissionClosed(
            MetadataHasMetadataSites,
            typeof(ILInspector.Decompiler.Pipeline.MetadataSource),
            typeof(ResearchMatch),
            typeof(IlAssemblyDiff),
            typeof(AssemblyContextStructuralCloneRetrievalQuery),
            typeof(PlatformResolver),
            typeof(JsExportSurfaceLoader));
    }

    [Fact]
    public void Instructions_DoesNotExposeAssemblyImageEntryPoints()
    {
        string[] methods = typeof(MethodInstructions).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(method => method.GetParameters().Any(
                parameter => parameter.ParameterType == typeof(PEReader)))
            .Select(method =>
                $"{method.DeclaringType!.FullName}.{method.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            methods.Length == 0,
            "Instructions exposes raw assembly-image entry points: "
                + string.Join(", ", methods));
    }

    [Fact]
    public void MetadataPrimitives_MethodSemanticsReaderIsIsolated()
    {
        string project = Path.Combine(
            CommandErrorOwnershipTests.RepositoryRoot(),
            "src",
            "ILInspector.MetadataPrimitives",
            "ILInspector.MetadataPrimitives.csproj");
        var sources = EvaluatedSources(project);
        string reader = Assert.Single(
            sources,
            file => file.Name
                == $"{nameof(MethodSemanticsRowReader)}.cs").Source;
        System.Reflection.MethodInfo read = Assert.Single(
            typeof(MethodSemanticsRowReader).GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly));
        MetadataApiReference[] layoutCalls = MetadataApiReferences()
            .Where(reference =>
                reference.Api is "GetTableMetadataOffset"
                    or "GetTableRowSize")
            .ToArray();

        Assert.Equal(
            ["GetTableMetadataOffset", "GetTableRowSize"],
            layoutCalls
                .Select(call => call.Api)
                .Order(StringComparer.Ordinal));
        Assert.All(
            layoutCalls,
            call =>
            {
                Assert.Equal(
                    "ILInspector.MetadataPrimitives.MethodSemanticsRowReader",
                    call.CallerType);
                Assert.Equal(ILOpCode.Call, call.OpCode);
                Assert.Equal(TableIndex.MethodSemantics, call.Table);
            });
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
        Type[] apiTypes = typeof(MethodSemanticsRowReader).Assembly
            .GetTypes()
            .Where(IsExternallyVisible)
            .ToArray();
        Assert.DoesNotContain(
            apiTypes.SelectMany(ExternallyVisibleSignatureTypes),
            type => ContainsSignatureType(type, typeof(TableIndex)));
        Type[] forbiddenRetention =
        [
            typeof(PEReader),
            typeof(MetadataReader),
            typeof(PEMemoryBlock),
            typeof(BlobReader),
        ];
        Type[] methodSemanticsTypes = typeof(MethodSemanticsRowReader).Assembly
            .GetTypes()
            .Where(type => type.FullName?.StartsWith(
                "ILInspector.MetadataPrimitives.MethodSemantics",
                StringComparison.Ordinal) is true)
            .ToArray();
        Type[] resultVariants = typeof(MethodSemanticsReadResult).GetNestedTypes(
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.Equal(
            [
                nameof(MethodSemanticsReadResult.MalformedInput),
                nameof(MethodSemanticsReadResult.NoMetadata),
                nameof(MethodSemanticsReadResult.RetainedAssociationBudgetExceeded),
                nameof(MethodSemanticsReadResult.Success),
                nameof(MethodSemanticsReadResult.UnsupportedWindowsMetadata),
            ],
            resultVariants
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal));
        Assert.All(
            resultVariants,
            variant => Assert.Contains(variant, methodSemanticsTypes));
        Assert.DoesNotContain(
            methodSemanticsTypes
                .SelectMany(type => type.GetFields(
                    System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.DeclaredOnly)),
            field => ContainsPointerShaped(field.FieldType)
                || forbiddenRetention.Any(
                    forbidden => ContainsSignatureType(
                        field.FieldType,
                        forbidden)));
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
        Assert.Contains("node \"$main_js\"", runner);
        Assert.Contains(
            "tests/ILInspector.MetadataPrimitives.PlatformProbe/*) "
                + "CODE=true; WEB=true",
            changeDetection);
        Assert.Contains(
            "eng/run-method-semantics-platform-probe.sh) "
                + "CODE=true; WEB=true",
            changeDetection);
    }

    [Fact]
    public void LocalPathAdmission_PlatformClassifiersRemainPortable()
    {
        string root = CommandErrorOwnershipTests.RepositoryRoot();
        string probeDirectory = Path.Combine(
            root,
            "tests",
            "DotnetInspector.Artifacts.Local.PlatformProbe");
        string probe = File.ReadAllText(
            Path.Combine(probeDirectory, "Program.cs"));
        string nativeProject = File.ReadAllText(
            Path.Combine(
                probeDirectory,
                "LocalPathAdmissionPlatformProbe.csproj"));
        string browserProject = File.ReadAllText(
            Path.Combine(
                probeDirectory,
                "LocalPathAdmissionBrowserProbe.csproj"));
        string runner = File.ReadAllText(
            Path.Combine(
                root,
                "eng",
                "run-local-path-admission-platform-probe.sh"));
        string workflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "ci.yml"));
        string changeDetection = File.ReadAllText(
            Path.Combine(root, "eng", "ci-detect-changes.sh"));

        Assert.Contains(
            "LocalArtifactSource.AcquireFileAsync(",
            probe);
        Assert.Contains("\"local.file.missing\"", probe);
        Assert.Contains("\"local.file.unsupported-entry\"", probe);
        Assert.Contains(
            "Browser errno classification did not select only WASI values.",
            probe);
        Assert.Contains("LocalPathAdmission.IsUnixMissing(2)", probe);
        Assert.Contains(
            "LocalPathAdmission.IsUnixSymbolicLinkLoop(40)",
            probe);
        Assert.Contains(
            "<PublishAot>true</PublishAot>",
            nativeProject);
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
            "run-local-path-admission-platform-probe.sh nativeaot",
            workflow);
        Assert.Contains(
            "run-local-path-admission-platform-probe.sh browser",
            workflow);
        Assert.Contains("dotnet publish", runner);
        Assert.Contains("node \"$main_js\"", runner);
        Assert.Contains(
            "tests/DotnetInspector.Artifacts.Local.PlatformProbe/*) "
                + "CODE=true; WEB=true",
            changeDetection);
        Assert.Contains(
            "eng/run-local-path-admission-platform-probe.sh) "
                + "CODE=true; WEB=true",
            changeDetection);
    }

    private static (string Name, string Source)[] EvaluatedSources(
        string project)
    {
        string projectDirectory = Path.GetDirectoryName(project)!;
        return
        [
            .. CommandErrorOwnershipTests.EvaluatedCompileFiles(project)
                .Select(path => (
                    Name: Path.GetRelativePath(projectDirectory, path),
                    Source: File.ReadAllText(path))),
        ];
    }

    private sealed record MetadataApiReference(
        string CallerType,
        string Api,
        ILOpCode OpCode,
        TableIndex? Table);

    private static MetadataApiReference[] MetadataApiReferences()
    {
        using var stream = File.OpenRead(
            typeof(MethodSemanticsRowReader).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader metadata = peReader.GetMetadataReader();
        List<MetadataApiReference> references = [];

        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
            string typeNamespace = metadata.GetString(type.Namespace);
            string typeName = metadata.GetString(type.Name);
            string qualifiedType = string.IsNullOrEmpty(typeNamespace)
                ? typeName
                : $"{typeNamespace}.{typeName}";

            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method =
                    metadata.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0)
                    continue;

                MethodInstructions body = MethodInstructions.Decode(
                    peReader.GetMethodBody(method.RelativeVirtualAddress));
                Assert.True(
                    body.IsComplete,
                    $"Could not inspect {qualifiedType}."
                        + metadata.GetString(method.Name));
                for (int index = 0; index < body.Instructions.Length; index++)
                {
                    DecodedInstruction instruction = body.Instructions[index];
                    if (instruction.Operand != OperandKind.InlineMethod
                        || MetadataApi(
                            metadata,
                            checked((int)instruction.OperandValue))
                            is not { } api)
                    {
                        continue;
                    }

                    TableIndex? table = api != "GetMetadata"
                        && instruction.OpCode == ILOpCode.Call
                        && index > 0
                        && TryGetInt32Constant(
                            body.Instructions[index - 1],
                            out int value)
                            ? (TableIndex)value
                            : null;
                    references.Add(
                        new MetadataApiReference(
                            qualifiedType,
                            api,
                            instruction.OpCode,
                            table));
                }
            }
        }

        return [.. references];
    }

    private static string? MetadataApi(
        MetadataReader metadata,
        int token)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.MemberReference)
            return null;

        MemberReference member = metadata.GetMemberReference(
            (MemberReferenceHandle)handle);
        if (member.Parent.Kind != HandleKind.TypeReference)
            return null;

        TypeReference type = metadata.GetTypeReference(
            (TypeReferenceHandle)member.Parent);
        string typeNamespace = metadata.GetString(type.Namespace);
        string typeName = metadata.GetString(type.Name);
        string name = metadata.GetString(member.Name);
        bool isLayoutApi =
            typeNamespace == "System.Reflection.Metadata.Ecma335"
            && typeName == "MetadataReaderExtensions"
            && name is "GetTableMetadataOffset" or "GetTableRowSize";
        bool isMetadataAcquisition =
            typeNamespace == "System.Reflection.PortableExecutable"
            && typeName == nameof(PEReader)
            && name == "GetMetadata";
        if (!isLayoutApi && !isMetadataAcquisition)
        {
            return null;
        }

        Assert.True(
            type.ResolutionScope.Kind == HandleKind.AssemblyReference,
            $"{typeNamespace}.{typeName}.{name} is not assembly-scoped.");
        System.Reflection.Metadata.AssemblyReference reference =
            metadata.GetAssemblyReference(
                (AssemblyReferenceHandle)type.ResolutionScope);
        AssertSystemReflectionMetadataIdentity(metadata, reference);
        string expectedAssemblyName =
            typeof(MetadataReader).Assembly.GetName().Name!;
        Assert.All(
            metadata.AssemblyReferences
                .Select(metadata.GetAssemblyReference)
                .Where(candidate => metadata.StringComparer.Equals(
                    candidate.Name,
                    expectedAssemblyName)),
            candidate => AssertSystemReflectionMetadataIdentity(
                metadata,
                candidate));

        MethodSignature<string> signature = member.DecodeMethodSignature(
            ILSignatureTypeProvider.Instance,
            genericContext: null);
        Assert.Equal(
            SignatureCallingConvention.Default,
            signature.Header.CallingConvention);
        Assert.Equal(0, signature.GenericParameterCount);
        if (isLayoutApi)
        {
            Assert.False(signature.Header.IsInstance);
            Assert.Equal("int32", signature.ReturnType);
            Assert.Equal(
                [
                    "class [System.Reflection.Metadata]"
                        + "System.Reflection.Metadata.MetadataReader",
                    "valuetype [System.Reflection.Metadata]"
                        + "System.Reflection.Metadata.Ecma335.TableIndex",
                ],
                signature.ParameterTypes);
        }
        else
        {
            Assert.True(signature.Header.IsInstance);
            Assert.Equal(
                "valuetype [System.Reflection.Metadata]"
                    + "System.Reflection.PortableExecutable.PEMemoryBlock",
                signature.ReturnType);
            Assert.Empty(signature.ParameterTypes);
        }

        return name;
    }

    private static void AssertSystemReflectionMetadataIdentity(
        MetadataReader metadata,
        System.Reflection.Metadata.AssemblyReference reference)
    {
        System.Reflection.AssemblyName expected =
            typeof(MetadataReader).Assembly.GetName();
        Assert.Equal(expected.Name, metadata.GetString(reference.Name));
        Assert.Equal(expected.Version, reference.Version);
        Assert.Equal(
            expected.CultureName ?? string.Empty,
            metadata.GetString(reference.Culture));
        Assert.Equal(
            expected.GetPublicKeyToken() ?? [],
            metadata.GetBlobBytes(reference.PublicKeyOrToken));
    }

    private static bool TryGetInt32Constant(
        DecodedInstruction instruction,
        out int value)
    {
        if (instruction.OpCode is >= ILOpCode.Ldc_i4_0
            and <= ILOpCode.Ldc_i4_8)
        {
            value = (int)instruction.OpCode - (int)ILOpCode.Ldc_i4_0;
            return true;
        }

        switch (instruction.OpCode)
        {
            case ILOpCode.Ldc_i4_m1:
                value = -1;
                return true;
            case ILOpCode.Ldc_i4_s:
            case ILOpCode.Ldc_i4:
                value = checked((int)instruction.OperandValue);
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool IsExternallyVisible(Type type)
    {
        if (!type.IsNested)
            return type.IsPublic;

        return type.DeclaringType is not null
            && IsExternallyVisible(type.DeclaringType)
            && (type.IsNestedPublic
                || type.IsNestedFamily
                || type.IsNestedFamORAssem);
    }

    private static IEnumerable<Type> ExternallyVisibleSignatureTypes(Type type)
    {
        const System.Reflection.BindingFlags Declared =
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;

        if (type.BaseType is not null)
            yield return type.BaseType;

        foreach (Type interfaceType in type.GetInterfaces())
            yield return interfaceType;

        foreach (Type genericParameter in type.GetGenericArguments()
            .Where(argument => argument.IsGenericParameter))
        {
            foreach (Type constraint in genericParameter.GetGenericParameterConstraints())
                yield return constraint;
        }

        foreach (System.Reflection.MethodInfo method in type.GetMethods(Declared)
            .Where(IsExternallyVisible))
        {
            yield return method.ReturnType;
            foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                yield return parameter.ParameterType;
            foreach (Type genericParameter in method.GetGenericArguments()
                .Where(argument => argument.IsGenericParameter))
            {
                foreach (Type constraint in genericParameter.GetGenericParameterConstraints())
                    yield return constraint;
            }
        }

        foreach (System.Reflection.ConstructorInfo constructor
            in type.GetConstructors(Declared)
                .Where(IsExternallyVisible))
        {
            foreach (System.Reflection.ParameterInfo parameter
                in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (System.Reflection.PropertyInfo property
            in type.GetProperties(Declared)
                .Where(property => property.GetAccessors(nonPublic: true)
                    .Any(IsExternallyVisible)))
        {
            yield return property.PropertyType;
            foreach (System.Reflection.ParameterInfo parameter
                in property.GetIndexParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (System.Reflection.FieldInfo field in type.GetFields(Declared)
            .Where(IsExternallyVisible))
            yield return field.FieldType;

        foreach (System.Reflection.EventInfo eventInfo in type.GetEvents(Declared)
            .Where(eventInfo => new[]
                {
                    eventInfo.GetAddMethod(nonPublic: true),
                    eventInfo.GetRemoveMethod(nonPublic: true),
                    eventInfo.GetRaiseMethod(nonPublic: true),
                }
                .Any(method => method is not null
                    && IsExternallyVisible(method))))
        {
            if (eventInfo.EventHandlerType is not null)
                yield return eventInfo.EventHandlerType;
        }
    }

    private static bool IsExternallyVisible(
        System.Reflection.MethodBase method) =>
        method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool IsExternallyVisible(
        System.Reflection.FieldInfo field) =>
        field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;

    private static bool ContainsPointerShaped(Type type)
    {
        if (type == typeof(IntPtr)
            || type == typeof(UIntPtr)
            || type.IsByRef
            || type.IsPointer
            || type.IsFunctionPointer)
        {
            return true;
        }

        if (type.HasElementType
            && type.GetElementType() is { } elementType
            && ContainsPointerShaped(elementType))
        {
            return true;
        }

        return type.IsGenericType
            && type.GetGenericArguments().Any(ContainsPointerShaped);
    }

    private static bool ContainsSignatureType(Type type, Type expected)
    {
        if (type == expected)
            return true;

        if (type.HasElementType
            && type.GetElementType() is { } elementType
            && ContainsSignatureType(elementType, expected))
        {
            return true;
        }

        if (type.IsFunctionPointer)
        {
            return ContainsSignatureType(
                    type.GetFunctionPointerReturnType(),
                    expected)
                || type.GetFunctionPointerParameterTypes().Any(
                    parameter => ContainsSignatureType(
                        parameter,
                        expected));
        }

        return type.IsGenericType
            && type.GetGenericArguments().Any(
                argument => ContainsSignatureType(argument, expected));
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

    static IEnumerable<string> MetadataReaderConstructionSites(
        string assemblyPath) =>
        MetadataCallSites(
            assemblyPath,
            CallsAssemblyMetadataReaderConstruction);

    static IEnumerable<string> MetadataHasMetadataSites(
        string assemblyPath) =>
        MetadataCallSites(
            assemblyPath,
            CallsPeReaderHasMetadata);

    static void AssertAdmissionClosed(
        Func<string, IEnumerable<string>> callSites,
        params Type[] assemblyMarkers)
    {
        foreach (Type marker in assemblyMarkers)
        {
            string[] sites =
                callSites(marker.Assembly.Location)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            Assert.True(
                sites.Length == 0,
                $"Raw assembly metadata calls remain in "
                + $"{marker.Assembly.GetName().Name}: "
                + string.Join(", ", sites));
        }
    }

    static IEnumerable<string> MetadataCallSites(
        string assemblyPath,
        Func<MetadataReader, byte[], bool> matches)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        var sites = new HashSet<string>(StringComparer.Ordinal);

        foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            byte[] il =
                peReader.GetMethodBody(method.RelativeVirtualAddress)
                    .GetILBytes()
                ?? [];
            if (!matches(reader, il))
                continue;

            TypeDefinitionHandle declaringHandle =
                method.GetDeclaringType();
            int parameterCount = method.GetParameters().Count(
                parameter =>
                    reader.GetParameter(parameter).SequenceNumber != 0);
            sites.Add(
                $"{MetadataDeclaringTypeName(reader, declaringHandle)}."
                + $"{reader.GetString(method.Name)}/{parameterCount}");
        }

        return sites;
    }

    static string MetadataDeclaringTypeName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        var names = new Stack<string>();
        string typeNamespace;
        while (true)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            names.Push(reader.GetString(type.Name));
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            if (declaring.IsNil)
            {
                typeNamespace = reader.GetString(type.Namespace);
                break;
            }

            handle = declaring;
        }

        string typeName = string.Join(".", names);
        return string.IsNullOrEmpty(typeNamespace)
            ? typeName
            : $"{typeNamespace}.{typeName}";
    }

    static bool CallsPeReaderHasMetadata(
        MetadataReader reader,
        byte[] il)
    {
        foreach (DecodedInstruction instruction in InstructionDecoder.Decode(il))
        {
            if (instruction.Operand
                    is not (OperandKind.InlineMethod or OperandKind.InlineTok))
            {
                continue;
            }

            EntityHandle operand =
                MetadataTokens.EntityHandle((int)instruction.OperandValue);
            if (operand.Kind != HandleKind.MemberReference)
                continue;

            MemberReference member = reader.GetMemberReference(
                (MemberReferenceHandle)operand);
            if (member.Parent.Kind != HandleKind.TypeReference)
                continue;

            TypeReference type = reader.GetTypeReference(
                (TypeReferenceHandle)member.Parent);
            if (reader.GetString(type.Name) == nameof(PEReader)
                && reader.GetString(type.Namespace)
                    == typeof(PEReader).Namespace
                && reader.GetString(member.Name) == "get_HasMetadata")
            {
                return true;
            }
        }

        return false;
    }

    static bool CallsAssemblyMetadataReaderConstruction(
        MetadataReader reader,
        byte[] il)
    {
        foreach (DecodedInstruction instruction in InstructionDecoder.Decode(il))
        {
            if (instruction.Operand
                    is not (OperandKind.InlineMethod or OperandKind.InlineTok))
            {
                continue;
            }

            EntityHandle operand =
                MetadataTokens.EntityHandle((int)instruction.OperandValue);
            if (operand.Kind != HandleKind.MemberReference)
                continue;

            MemberReference member = reader.GetMemberReference(
                (MemberReferenceHandle)operand);
            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = reader.GetTypeReference(
                (TypeReferenceHandle)member.Parent);
            string methodName = reader.GetString(member.Name);
            string typeName = reader.GetString(type.Name);
            string typeNamespace = reader.GetString(type.Namespace);
            if ((typeName == nameof(PEReaderExtensions)
                    && typeNamespace == typeof(MetadataReader).Namespace
                    && methodName
                        == nameof(MetadataFormatAdmission.GetMetadataReader))
                || (typeName == nameof(MetadataReaderProvider)
                    && typeNamespace == typeof(MetadataReader).Namespace
                    && methodName
                        is nameof(MetadataReaderProvider.FromMetadataImage)
                            or nameof(MetadataReaderProvider.FromMetadataStream))
                || (typeName == nameof(MetadataReader)
                    && typeNamespace == typeof(MetadataReader).Namespace
                    && methodName == ".ctor"))
            {
                return true;
            }
        }

        return false;
    }

    static MetadataReaderProvider MetadataProviderFromImageFixture(
        ImmutableArray<byte> image)
        => MetadataReaderProvider.FromMetadataImage(image);

    static MetadataReaderProvider MetadataProviderFromStreamFixture(
        Stream stream)
        => MetadataReaderProvider.FromMetadataStream(stream);

    static MetadataReaderProvider PortablePdbProviderFixture(Stream stream)
        => MetadataReaderProvider.FromPortablePdbStream(stream);

    static bool MetadataHasMetadataFixture(PEReader reader)
        => reader.HasMetadata;

    static class MetadataCallSiteNestedFixture
    {
        internal static MetadataReader MetadataReaderFixture(
            PEReader reader) =>
            reader.GetMetadataReader();

        internal static bool MetadataHasMetadataFixture(
            PEReader reader) =>
            reader.HasMetadata;
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
