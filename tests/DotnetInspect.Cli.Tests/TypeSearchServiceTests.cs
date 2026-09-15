using System.IO.Compression;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class TypeSearchServiceTests
{
    [Fact]
    public async Task FindWorkspacePlan_PreservesExplicitEcosystemOrder()
    {
        Assert.True(
            EcosystemPackId.TryCreate(
                "ecosystem.aspire",
                out EcosystemPackId? aspire));
        Assert.True(
            EcosystemPackId.TryCreate(
                "ecosystem.ai",
                out EcosystemPackId? ai));
        var options = new FindOptions
        {
            Ecosystems = [aspire!, ai!],
        };

        WorkspacePlan plan =
            FindSourceCollector.CreateWorkspacePlan(options);
        await using var workspace = new InspectionWorkspace(plan);

        WorkspaceRegistrationReadResult.Available snapshot =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot());
        Assert.Equal(
            ["ecosystem.aspire", "ecosystem.ai"],
            snapshot.Revision.Registrations
                .Cast<WorkspaceRegistration.Ecosystem>()
                .Select(
                    static registration =>
                        registration.Declaration.Id.Value)
                .ToArray());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_LocatorRetainsPackageAndPlatformChoices()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer",
            Packages = ["System.Text.Json@10.0.0"],
            PlatformAssemblies = ["System.Text.Json"],
            Tfm = "net10.0",
            IncludeAll = true,
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Equal(2, result.Rows.Count);
        Assert.All(
            result.Rows,
            static row => Assert.Equal("class", row.Kind));
        Assert.Contains(
            result.Rows,
            static row => row.Source == "System.Text.Json");
        Assert.Contains(
            result.Rows,
            static row => row.Source == "runtime");
        TypeDeclarationLocatorSectionResult.Evaluated section =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                Assert.Single(result.LocatorSections));
        TypeDeclarationLocatorSectionAnswer answer =
            Assert.Single(section.Answers);
        Assert.Equal(2, answer.AvailableCandidateCount);
        Assert.Equal(2, answer.Candidates.Length);
        Assert.All(result.Rows, row =>
        {
            Assert.NotNull(row.Location);
        });

        TypeFindResult package =
            Assert.Single(
                result.Rows,
                static row =>
                    row.Location?.Coordinate
                        is TypeDeclarationLocatorSectionCoordinate
                            .PackageCoordinate);
        TypeOptions packageType =
            TypeFindIfMissResult.Found(options.Pattern, package)
                .ApplyTo(new TypeOptions());
        Assert.Equal(
            "system.text.json@10.0.0",
            packageType.PackagePath);
        Assert.Equal(
            "lib/net10.0/System.Text.Json.dll",
            packageType.AssemblyPath);
        Assert.Equal("net10.0", packageType.Tfm);

        TypeFindResult platform =
            Assert.Single(
                result.Rows,
                static row =>
                    row.Location?.Coordinate
                        is TypeDeclarationLocatorSectionCoordinate
                            .PlatformCoordinate);
        TypeFindIfMissResult platformSelection =
            TypeFindIfMissResult.Found(
                options.Pattern,
                platform);
        Assert.Throws<InvalidOperationException>(
            () => platformSelection.ApplyTo(new TypeOptions()));
        Assert.Throws<InvalidOperationException>(
            () => platformSelection.ApplyTo(new MemberOptions()));

        var (typeExit, typeOutput, _) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(packageType));
        Assert.Equal(0, typeExit);
        Assert.Contains(
            "System.Text.Json.JsonSerializer",
            typeOutput,
            StringComparison.Ordinal);

        MemberOptions packageMember =
            TypeFindIfMissResult.Found(options.Pattern, package)
                .ApplyTo(
                    new MemberOptions
                    {
                        MemberFilter = ["Serialize"],
                        OverloadIndex = 1,
                        IncludeSections = ["Member Index"],
                    });
        var (memberExit, memberOutput, _) =
            await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(packageMember));
        Assert.Equal(0, memberExit);
        Assert.Contains(
            "System.Text.Json.JsonSerializer.Serialize",
            memberOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_PackageReopeningPreservesImplementationAsset()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "RuntimeBinder",
            Packages = ["Microsoft.CSharp@4.7.0"],
            Tfm = "netstandard2.0",
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        TypeFindResult row = Assert.Single(result.Rows);
        Assert.Equal(
            "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException",
            row.FullName);
        TypeOptions typeOptions =
            TypeFindIfMissResult.Found(options.Pattern, row)
                .ApplyTo(new TypeOptions());
        Assert.Equal(
            "lib/netstandard2.0/Microsoft.CSharp.dll",
            typeOptions.AssemblyPath);
        Assert.Equal("netstandard2.0", typeOptions.Tfm);
        Assert.Collection(
            result.LocatorSections,
            section => Assert.Equal(
                "RuntimeBinder",
                SinglePatternRequest(section)),
            section => Assert.Equal(
                "*",
                SinglePatternRequest(section)));

        var (exitCode, output, _) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(typeOptions));
        Assert.Equal(0, exitCode);
        Assert.Contains(row.FullName, output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_PackageReopeningUsesSelectedCompatibleTfm()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.JsonSerializer",
            Packages = ["System.Text.Json@10.0.0"],
            Tfm = "net11.0",
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        TypeFindResult row = Assert.Single(result.Rows);
        TypeDeclarationLocatorSelection.PackageSelection selection =
            Assert.IsType<
                TypeDeclarationLocatorSelection.PackageSelection>(
                    row.Location!.Observation.Selection);
        Assert.Equal("net10.0", selection.Tfm);
        Assert.Equal(
            "lib/net10.0/System.Text.Json.dll",
            selection.AssetPath);

        TypeOptions typeOptions =
            TypeFindIfMissResult.Found(options.Pattern, row)
                .ApplyTo(new TypeOptions());
        Assert.Equal("net10.0", typeOptions.Tfm);
        Assert.Equal(selection.AssetPath, typeOptions.AssemblyPath);

        var (exitCode, output, _) =
            await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(typeOptions));
        Assert.Equal(0, exitCode);
        Assert.Contains(row.FullName, output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_PrefixMatchDoesNotRetainWildcardCensus()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "System.Text",
            PlatformAssemblies = ["System.Private.CoreLib"],
            Tfm = "net10.0",
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.NotEmpty(result.Rows);
        Assert.All(
            result.Rows,
            static row =>
                Assert.StartsWith(
                    "System.Text",
                    row.FullName,
                    StringComparison.Ordinal));
        Assert.Collection(
            result.LocatorSections,
            section => Assert.Equal(
                "System.Text",
                SinglePatternRequest(section)),
            section => Assert.Equal(
                "System.Text*",
                SinglePatternRequest(section)));
        Assert.DoesNotContain(
            result.LocatorSections,
            static section =>
                SinglePatternRequest(section) == "*");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_PreservesDefinitionAndForwarderChoices()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "System.Object",
            PlatformAssemblies =
            [
                "System.Runtime",
                "System.Private.CoreLib",
            ],
            Tfm = "net10.0",
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.False(result.HasFailures);
        Assert.Equal(2, result.Rows.Count);
        Assert.All(
            result.Rows,
            static row => Assert.Equal("class", row.Kind));
        Assert.Contains(
            result.Rows,
            static row =>
                row.Location?.DeclarationKind
                    == AssemblyTypeDeclarationKind.Definition);
        Assert.Contains(
            result.Rows,
            static row =>
                row.Location?.DeclarationKind
                    == AssemblyTypeDeclarationKind.Forwarder);
        TypeDeclarationLocatorSectionResult.Evaluated section =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                Assert.Single(result.LocatorSections));
        Assert.All(
            section.Contexts,
            static context =>
                Assert.True(context.IsRealized));
        Assert.All(
            section.Answers,
            static answer =>
                Assert.True(answer.IsComplete));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FindTypesAsync_LimitDoesNotBoundLocatorInventory()
    {
        using var httpClient = new HttpClient();
        var options = new FindOptions
        {
            Pattern = "System.Text.Json.*",
            Packages = ["System.Text.Json@10.0.0"],
            Tfm = "net10.0",
            Limit = 1,
        };

        FindSearchResult<TypeFindResult> result =
            await TypeSearchService.FindTypesAsync(
                options,
                [options.Pattern],
                new VerboseLogger(enabled: false),
                httpClient,
                TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        TypeDeclarationLocatorSectionResult.Evaluated section =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                Assert.Single(result.LocatorSections));
        TypeDeclarationLocatorSectionAnswer answer =
            Assert.Single(section.Answers);
        Assert.True(answer.AvailableCandidateCount > 1);
        Assert.Null(section.MaxInventoryReads);
    }

    [Fact]
    public async Task CollectTypesAsync_BinPathUsesAssemblySetDirectorySource()
    {
        var directory = Directory.CreateTempSubdirectory("type-search-bin-test").FullName;
        var copiedAssembly = Path.Combine(directory, "CopiedSearchAssembly.dll");
        File.Copy(typeof(TypeSearchServiceTests).Assembly.Location, copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            var results = await TypeSearchService.CollectTypesAsync(
                new FindOptions
                {
                    Pattern = nameof(TypeSearchServiceTests),
                    BinPaths = [directory],
                    IncludeAll = true,
                },
                nameof(TypeSearchServiceTests),
                new VerboseLogger(enabled: false),
                httpClient);

            var result = Assert.Single(results, r => r.FullName == typeof(TypeSearchServiceTests).FullName);
            Assert.Equal("CopiedSearchAssembly", result.Assembly);
            Assert.Equal(Path.GetFileName(directory), result.Source);
            Assert.Null(result.SourceVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CollectTypesAsync_TrailingDirectorySeparatorDoesNotAbort()
    {
        string directory =
            Directory.CreateTempSubdirectory(
                "type-search-trailing-bin-test").FullName;
        string copiedAssembly =
            Path.Combine(directory, "CopiedSearchAssembly.dll");
        File.Copy(
            typeof(TypeSearchServiceTests).Assembly.Location,
            copiedAssembly);

        try
        {
            using var httpClient = new HttpClient();
            List<TypeSearchResult> results =
                await TypeSearchService.CollectTypesAsync(
                    new FindOptions
                    {
                        Pattern = nameof(TypeSearchServiceTests),
                        BinPaths =
                        [
                            directory + Path.DirectorySeparatorChar,
                        ],
                        IncludeAll = true,
                    },
                    nameof(TypeSearchServiceTests),
                    new VerboseLogger(enabled: false),
                    httpClient);

            TypeSearchResult result = Assert.Single(
                results,
                candidate =>
                    candidate.FullName
                    == typeof(TypeSearchServiceTests).FullName);
            Assert.Equal("", result.Source);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CollectTypesAsync_InvalidAssemblyWarnsWithoutVerbose()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllTextAsync(
            path,
            "not a managed assembly",
            TestContext.Current.CancellationToken);
        using var httpClient = new HttpClient();
        try
        {
            var capture = await ConsoleCapture.RunAsync(async () =>
            {
                List<TypeSearchResult> results =
                    await TypeSearchService.CollectTypesAsync(
                        new FindOptions
                        {
                            Pattern = "NoSuchType",
                            Assemblies = [path],
                        },
                        "NoSuchType",
                        new VerboseLogger(enabled: false),
                        httpClient);
                Assert.Empty(results);
                return 0;
            });

            Assert.Contains($"Could not read {path}", capture.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CollectTypesAsync_PackageFallbackKeepsRuntimeAssembliesForFind()
    {
        var packageDir = Directory.CreateTempSubdirectory("type-search-runtime-package-test").FullName;
        var packagePath = Path.Combine(packageDir, "RuntimeOnly.1.0.0.nupkg");
        var sourceAssembly = typeof(TypeSearchServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "runtimes/any/lib/net10.0/RuntimeOnly.dll");
            }

            using var httpClient = new HttpClient();
            var results = await TypeSearchService.CollectTypesAsync(
                new FindOptions
                {
                    Pattern = nameof(TypeSearchServiceTests),
                    Packages = [packagePath],
                    IncludeAll = true,
                },
                nameof(TypeSearchServiceTests),
                new VerboseLogger(enabled: false),
                httpClient);

            Assert.Contains(results, r => r.FullName == typeof(TypeSearchServiceTests).FullName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task CollectTypesAsync_WithLimitDoesNotResolveLaterSources()
    {
        using var httpClient = new HttpClient();
        List<TypeSearchResult>? results = null;
        var missingDirectory = Path.Combine(Path.GetTempPath(), $"missing-type-search-{Guid.NewGuid():N}");

        var capture = await ConsoleCapture.RunAsync(async () =>
        {
            results = await TypeSearchService.CollectTypesAsync(
                new FindOptions
                {
                    Pattern = nameof(TypeSearchServiceTests),
                    Assemblies = [typeof(TypeSearchServiceTests).Assembly.Location],
                    BinPaths = [missingDirectory],
                    IncludeAll = true,
                    Limit = 1,
                },
                nameof(TypeSearchServiceTests),
                new VerboseLogger(enabled: false),
                httpClient);
            return 0;
        });

        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.DoesNotContain("Directory not found", capture.Error);
    }

    static string SinglePatternRequest(
        TypeDeclarationLocatorSectionResult section)
    {
        TypeDeclarationLocatorSectionResult.Evaluated evaluated =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                section);
        TypeDeclarationLocatorSectionAnswer answer =
            Assert.Single(evaluated.Answers);
        return Assert.IsType<
                TypeDeclarationLocatorSectionRequest.PatternRequest>(
                    answer.Request)
            .Text;
    }
}
