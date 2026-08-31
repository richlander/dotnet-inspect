using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Fixtures;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class MemberInspectionRouteCharacterizationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"dotnet-inspect-route-characterization-{Guid.NewGuid():N}");
    private readonly string _packagePath;

    public MemberInspectionRouteCharacterizationTests()
    {
        string content = Path.Combine(_temporaryDirectory, "content");
        string library = Path.Combine(content, "lib", "net11.0");
        Directory.CreateDirectory(library);
        File.WriteAllText(
            Path.Combine(content, "Route.Characterization.nuspec"),
            """
            <?xml version="1.0"?>
            <package>
              <metadata>
                <id>Route.Characterization</id>
                <version>1.0.0</version>
                <authors>dotnet-inspect</authors>
                <description>Member route characterization fixture.</description>
              </metadata>
            </package>
            """);
        File.Copy(
            typeof(BodyShapeFixture).Assembly.Location,
            Path.Combine(library, "Route.Characterization.dll"));
        _packagePath = Path.Combine(
            _temporaryDirectory,
            "Route.Characterization.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, _packagePath);
    }

    public void Dispose()
        => Directory.Delete(_temporaryDirectory, recursive: true);

    [Fact]
    public async Task CurrentRoutes_HaveOneCompletePlanningMatrix()
    {
        // Slice 1 deliberately precedes the product route registry in slice 2. The accepted
        // design's ten route identities are therefore the closure set here; every row below
        // independently exercises its current product branch.
        var packagePipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;
        HashSet<string> packageSections =
        [
            PackageSections.SourceLinkAvailability,
            PackageSections.Vulnerabilities,
        ];
        var packageOptions = new InspectionOptions
        {
            IncludeSections = packageSections,
            Verbosity = Verbosity.Minimal,
        };

        var libraryPipeline = LibrarySections.CreateCatalog().Pipeline;
        HashSet<string> librarySections =
        [
            SectionNames.LibraryInfo,
            SectionNames.SourceLinkFiles,
        ];
        var libraryAuthorization =
            LibrarySourcePlans.For(Verbosity.Minimal, librarySections);
        HostQueryDemand[] libraryDiscoveryDemand =
        [
            .. LibraryCommand.DiscoveryQueries,
            .. LibraryCommand.BareDiscoveryQueries,
        ];
        string libraryCapabilities =
            $"pdb={libraryAuthorization.AllowPdbDownload};"
            + $"source={libraryAuthorization.CollectSourceFiles};"
            + $"cached={libraryAuthorization.ReadCachedPdb}";

        var assemblyTypeOptions = new TypeOptions();
        var typeMemberOptions = new TypeOptions
        {
            TypeName = nameof(BodyShapeFixture),
            IncludeSections = [SectionNames.DecompiledSource],
        };
        var memberTypeOptions = new MemberOptions
        {
            TypeName = nameof(BodyShapeFixture),
            IncludeSections = [SectionNames.PdbSource],
        };
        var overloadOptions = memberTypeOptions with
        {
            MemberFilter = [nameof(BodyShapeFixture.Classify)],
        };
        var detailOptions = overloadOptions with { OverloadIndex = 1 };
        ApiType apiType = LoadFixtureApiType();

        RouteObservation[] actual =
        [
            Observe(
                "package",
                await ObservePackageDiscoveryAsync(),
                packagePipeline,
                packageSections,
                $"vulnerability-traffic="
                    + PackageCommand.AllowsVulnerabilityTraffic(packageOptions)),
            Observe(
                "package-single-library",
                await ObservePackageLibraryDiscoveryAsync(),
                libraryPipeline,
                librarySections,
                libraryCapabilities,
                libraryDiscoveryDemand),
            Observe(
                "package-all-libraries",
                await ObservePackageAllLibrariesDiscoveryAsync(),
                libraryPipeline,
                librarySections,
                libraryCapabilities,
                discoveryRejected: true),
            Observe(
                "direct-library",
                await ObserveLibraryDiscoveryAsync(),
                libraryPipeline,
                librarySections,
                libraryCapabilities,
                libraryDiscoveryDemand),
            Observe(
                "assembly-type-list",
                await ObserveApiDiscoveryAsync(assemblyTypeOptions),
                TypePipeline(assemblyTypeOptions),
                [SectionNames.ApiInfo],
                "none"),
            Observe(
                "type-member-list",
                await ObserveApiDiscoveryAsync(typeMemberOptions),
                MemberPipeline(typeMemberOptions),
                typeMemberOptions.IncludeSections!,
                $"pdb={TypeCommand.AuthorizesPdbAcquisition(apiType, typeMemberOptions)};"
                    + $"source={TypeCommand.AuthorizesSourceInfoAcquisition(
                        apiType,
                        typeMemberOptions)}"),
            Observe(
                "member-type-view",
                await ObserveApiDiscoveryAsync(memberTypeOptions),
                MemberPipeline(memberTypeOptions),
                memberTypeOptions.IncludeSections!,
                MemberCapabilities(apiType, memberTypeOptions)),
            Observe(
                "overload-inventory",
                await ObserveApiDiscoveryAsync(overloadOptions),
                MemberPipeline(overloadOptions),
                overloadOptions.IncludeSections!,
                MemberCapabilities(apiType, overloadOptions)),
            Observe(
                "exact-member-detail",
                await ObserveApiDiscoveryAsync(detailOptions),
                MemberPipeline(detailOptions),
                detailOptions.IncludeSections!,
                MemberCapabilities(apiType, detailOptions)),
            await ObserveHiddenRouterAsync(),
        ];

        RouteObservation[] expected =
        [
            new(
                "package",
                "schema-static-without-target/effective-with-target",
                "Package",
                "focus=SourceLink: Availability->SourceLink availability;"
                    + "discovery=none",
                "vulnerability-traffic=True"),
            new(
                "package-single-library",
                "schema/discovery-after-package-acquisition",
                "Library",
                "focus=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders;"
                    + "discovery=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders,"
                    + "References applicability->Assembly references,"
                    + "Unsafe Members applicability->Unsafe evidence presence,"
                    + "discovery catalog->Metadata image",
                "pdb=True;source=True;cached=False"),
            new(
                "package-all-libraries",
                "discovery-rejected",
                "Library",
                "focus=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders;discovery=rejected",
                "pdb=True;source=True;cached=False"),
            new(
                "direct-library",
                "schema-static-without-target/effective-with-target",
                "Library",
                "focus=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders;"
                    + "discovery=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders,"
                    + "References applicability->Assembly references,"
                    + "Unsafe Members applicability->Unsafe evidence presence,"
                    + "discovery catalog->Metadata image",
                "pdb=True;source=True;cached=False"),
            new(
                "assembly-type-list",
                "schema-static/effective-deferred",
                "ApiType",
                "focus=none;discovery=none",
                "none"),
            new(
                "type-member-list",
                "schema-static/effective-deferred",
                "ApiMember",
                "focus=none;discovery=none",
                "pdb=True;source=False"),
            new(
                "member-type-view",
                "schema-static/effective-deferred",
                "ApiMember",
                "focus=none;discovery=none",
                "pdb=False;source=False"),
            new(
                "overload-inventory",
                "schema-static/effective-deferred",
                "ApiMemberOverload",
                "focus=none;discovery=none",
                "pdb=False;source=False"),
            new(
                "exact-member-detail",
                "schema-static/effective-deferred",
                "ApiMemberDetail",
                "focus=none;discovery=none",
                "pdb=True;source=True"),
            new(
                "hidden-router",
                "router-to-member/schema-static",
                "ApiMember",
                "focus=none;discovery=none",
                "none"),
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CurrentDeclarations_RecordFullSummaryAndFocusedProjections()
    {
        // GetTypeSurface is the current focused declaration seam being replaced in slice 6.
        // Freeze its differences now so that cutover can change all three projections atomically.
        string assemblyPath = typeof(BodyShapeFixture).Assembly.Location;

        ApiSurface full;
        using (var stream = File.OpenRead(assemblyPath))
        using (var pe = new PEReader(stream))
            full = ApiSurfaceExtractor.Extract(pe);

        ApiSurface summary;
        using (var stream = File.OpenRead(assemblyPath))
        using (var pe = new PEReader(stream))
            summary = ApiSurfaceExtractor.ExtractSummary(pe);

        using var focusedStream = File.OpenRead(assemblyPath);
        using var focusedPe = new PEReader(focusedStream);
        var reader = focusedPe.GetMetadataReader();

        Type[] fixtureTypes =
        [
            typeof(IBodyShapeValue),
            typeof(BodyShapeFixture),
            typeof(BodyShapeFixtureExtensions),
            typeof(GenericBodyShapeFixture<>),
            typeof(OverloadedIndexerBodyShapeFixture),
        ];

        DeclarationObservation[] actual = fixtureTypes
            .Select(type =>
            {
                int token = type.MetadataToken;
                var handle = MetadataTokens.TypeDefinitionHandle(token);
                ApiType focused =
                    MetadataDeclarationQuery.GetTypeSurface(reader, handle);
                ApiType fullType = Find(full, token);
                ApiType summaryType = Find(summary, type);
                return new DeclarationObservation(
                    type.Name,
                    Members(fullType),
                    Members(summaryType),
                    Members(focused));
            })
            .ToArray();

        DeclarationObservation[] expected =
        [
            new(
                nameof(IBodyShapeValue),
                "event:Changed,property:Value",
                "event:Changed,property:Value",
                "method:add_Changed,method:remove_Changed,property:Value"),
            new(
                nameof(BodyShapeFixture),
                "constructor:.ctor,"
                    + "explicit-interface-implementation:"
                    + "DotnetInspector.Fixtures.IBodyShapeValue.add_Changed,"
                    + "explicit-interface-implementation:"
                    + "DotnetInspector.Fixtures.IBodyShapeValue.get_Value,"
                    + "explicit-interface-implementation:"
                    + "DotnetInspector.Fixtures.IBodyShapeValue.remove_Changed,"
                    + "extension-method:ProjectedCreation,"
                    + "method:Branch,method:Classify,method:PublicCreation,"
                    + "method:PublicLocalFunctionBox,method:PublicSmallArray,"
                    + "method:ReadableLocal",
                "extension-method:ProjectedCreation,method:.ctor,method:Branch,"
                    + "method:Classify,"
                    + "method:DotnetInspector.Fixtures.IBodyShapeValue.add_Changed,"
                    + "method:DotnetInspector.Fixtures.IBodyShapeValue.get_Value,"
                    + "method:DotnetInspector.Fixtures.IBodyShapeValue.remove_Changed,"
                    + "method:PublicCreation,method:PublicLocalFunctionBox,"
                    + "method:PublicSmallArray,method:ReadableLocal",
                "constructor:.ctor,method:Branch,method:Classify,"
                    + "method:PublicCreation,method:PublicLocalFunctionBox,"
                    + "method:PublicSmallArray,method:ReadableLocal"),
            new(
                nameof(BodyShapeFixtureExtensions),
                "method:ProjectedCreation",
                "method:ProjectedCreation",
                "method:ProjectedCreation"),
            new(
                typeof(GenericBodyShapeFixture<>).Name,
                "constructor:.ctor,method:Create",
                "method:.ctor,method:Create",
                "constructor:.ctor,method:Create"),
            new(
                nameof(OverloadedIndexerBodyShapeFixture),
                "constructor:.ctor,property:Item,property:Item",
                "method:.ctor,property:Item,property:Item",
                "constructor:.ctor,property:Item,property:Item"),
        ];

        Assert.Equal(expected, actual);
    }

    private static SectionPipeline<ApiSurface> TypePipeline(TypeOptions options)
    {
        var (preamble, error) = ApiCommand.RunPreamble(options);
        Assert.Null(error);
        Assert.Equal(
            ApiTypeSectionDescriptors.CreatePipeline().AllSectionNames,
            preamble.TypePipeline.AllSectionNames);
        return preamble.TypePipeline;
    }

    private static SectionPipeline<ApiType> MemberPipeline(ApiOptions options)
    {
        var (preamble, error) = ApiCommand.RunPreamble(options);
        Assert.Null(error);
        return preamble.MemberPipeline;
    }

    private async Task<string> ObservePackageDiscoveryAsync()
    {
        var schema = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                Discover = [],
                Schema = true,
            }));
        Assert.Equal(0, schema.ExitCode);
        Assert.Contains(PackageSections.PackageInfo, schema.Output);
        Assert.Empty(schema.Error);

        var effective = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_packagePath],
                Discover = [PackageSections.PackageInfo],
            }));
        Assert.Equal(0, effective.ExitCode);
        Assert.Contains("| Authors | field", effective.Output);
        Assert.Empty(effective.Error);
        return "schema-static-without-target/effective-with-target";
    }

    private async Task<string> ObservePackageLibraryDiscoveryAsync()
    {
        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_packagePath],
                PackageLibrary = "",
                Discover = [],
                Schema = true,
            }));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(SectionNames.LibraryInfo, result.Output);
        Assert.Empty(result.Error);

        var discovery = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_packagePath],
                PackageLibrary = "",
                Discover = [SectionNames.LibraryInfo],
            }));
        Assert.Equal(0, discovery.ExitCode);
        Assert.Contains("| Architecture | field", discovery.Output);
        Assert.Empty(discovery.Error);
        return "schema/discovery-after-package-acquisition";
    }

    private async Task<string> ObservePackageAllLibrariesDiscoveryAsync()
    {
        var render = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_packagePath],
                AllLibraries = true,
                Select = [SectionNames.LibraryInfo],
            }));
        Assert.Equal(0, render.ExitCode);
        Assert.Contains(SectionNames.LibraryInfo, render.Output);
        Assert.Empty(render.Error);

        var result = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_packagePath],
                AllLibraries = true,
                Discover = [],
                Schema = true,
            }));
        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--all-libraries cannot be combined with -D/--discover",
            result.Error);
        return "discovery-rejected";
    }

    private static async Task<string> ObserveLibraryDiscoveryAsync()
    {
        var schema = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                Discover = [],
                Schema = true,
            }));
        Assert.Equal(0, schema.ExitCode);
        Assert.Contains(SectionNames.LibraryInfo, schema.Output);
        Assert.Empty(schema.Error);

        var effective = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = typeof(BodyShapeFixture).Assembly.Location,
                Discover = [SectionNames.LibraryInfo],
                Effective = true,
            }));
        Assert.Equal(0, effective.ExitCode);
        Assert.Contains("| Architecture | field", effective.Output);
        Assert.Empty(effective.Error);
        return "schema-static-without-target/effective-with-target";
    }

    private static async Task<string> ObserveApiDiscoveryAsync(
        ApiOptions options)
    {
        ApiCommand.PreambleResult staticPreamble = null!;
        int? staticExitCode = null;
        var staticOutput = await ConsoleCapture.RunAsync(() =>
        {
            (staticPreamble, staticExitCode) = ApiCommand.RunPreamble(
                options with
                {
                    Discover = [],
                    Schema = true,
                });
        });
        Assert.Null(staticPreamble);
        Assert.Equal(0, staticExitCode);
        Assert.NotEmpty(staticOutput.Output);
        Assert.Empty(staticOutput.Error);

        var (effectivePreamble, effectiveExitCode) =
            ApiCommand.RunPreamble(options with
            {
                Discover = [],
                Schema = false,
            });
        Assert.NotNull(effectivePreamble);
        Assert.Null(effectiveExitCode);
        return "schema-static/effective-deferred";
    }

    private static async Task<RouteObservation> ObserveHiddenRouterAsync()
    {
        string target =
            $"{typeof(BodyShapeFixture).FullName}.{nameof(BodyShapeFixture.Classify)}";
        string[] routedArgs =
            CommandLineBuilder.PreprocessArgs(
                [
                    target,
                    "--library",
                    typeof(BodyShapeFixture).Assembly.Location,
                    "-D",
                    "--schema",
                ]);
        Assert.Equal("router", routedArgs[0]);

        var observations = new ConcurrentQueue<BreadcrumbObservation>();
        using var subscription = BreadcrumbTelemetry.Subscribe(
            new BreadcrumbObserver(observations));
        var root = CommandLineBuilder.CreateRootCommand();
        var parsed = root.Parse(routedArgs);
        Assert.Empty(parsed.Errors);
        var routed = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(parsed, routedArgs));
        Assert.Equal(0, routed.ExitCode);
        Assert.Contains(
            $"| {SectionNames.TypeInfo} | section |",
            routed.Output);
        Assert.DoesNotContain(
            $"| {SectionNames.Signature} | section |",
            routed.Output);
        Assert.Empty(routed.Error);

        BreadcrumbObservation rewrite = Assert.Single(
            observations,
            observation => observation.Stage == "router-rewrite");
        Assert.Contains(" -> member ", rewrite.Detail);

        return new RouteObservation(
            "hidden-router",
            "router-to-member/schema-static",
            "ApiMember",
            "focus=none;discovery=none",
            "none");
    }

    private static string MemberCapabilities(
        ApiType apiType,
        MemberOptions options)
        => $"pdb={MemberCommand.AuthorizesMemberSourceResolution(apiType, options)};"
           + $"source={MemberCommand.AuthorizesMemberSourceContent(apiType, options)}";

    private static RouteObservation Observe<TModel>(
        string route,
        string discovery,
        SectionPipeline<TModel> pipeline,
        HashSet<string> focusedSections,
        string capabilityAuthorization,
        IReadOnlyList<HostQueryDemand>? discoveryDemand = null,
        bool discoveryRejected = false)
    {
        var focusTrace = new InspectionTrace();
        pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            focusedSections,
            trace: focusTrace);

        string focusDemand = FormatDemand(
            focusTrace.QueryDemand.Select(
                item => (item.Section, item.Query.Name)));
        string discoveryDemandText;
        if (discoveryRejected)
        {
            discoveryDemandText = "rejected";
        }
        else
        {
            var discoveryTrace = new InspectionTrace();
            pipeline.GetRequiredQueries(
                Verbosity.Minimal,
                include: null,
                trace: discoveryTrace,
                commandDemand: discoveryDemand,
                excludeUnbounded: true);
            discoveryDemandText = FormatDemand(
                discoveryTrace.QueryDemand
                    .Select(item => (item.Section, item.Query.Name))
                    .Concat(
                        discoveryTrace.CommandQueryDemand.Select(
                            item => (item.Reason, item.Query.Name))));
        }

        return new RouteObservation(
            route,
            discovery,
            IdentifyCatalog(pipeline),
            $"focus={focusDemand};discovery={discoveryDemandText}",
            capabilityAuthorization);
    }

    private static string FormatDemand(
        IEnumerable<(string Reason, string Query)> demand)
    {
        string[] values = demand
            .Select(item => $"{item.Reason}->{item.Query}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 0
            ? "none"
            : string.Join(",", values);
    }

    private static string IdentifyCatalog<TModel>(
        SectionPipeline<TModel> pipeline)
    {
        if (pipeline is SectionPipeline<InspectionResult> package)
        {
            Assert.Same(
                PackageSectionDescriptors.CreateCatalog().Pipeline,
                package);
            return "Package";
        }

        if (pipeline is SectionPipeline<LibraryInspection> library)
        {
            Assert.Same(LibrarySections.CreateCatalog().Pipeline, library);
            return "Library";
        }

        if (pipeline is SectionPipeline<ApiSurface> type)
        {
            Assert.Equal(
                ApiTypeSectionDescriptors.CreatePipeline().AllSectionNames,
                type.AllSectionNames);
            return "ApiType";
        }

        var member = Assert.IsType<SectionPipeline<ApiType>>(pipeline);
        (string Name, SectionPipeline<ApiType> Pipeline)[] catalogs =
        [
            ("ApiMember", ApiMemberSectionDescriptors.CreatePipeline()),
            ("ApiMemberOverload", ApiMemberOverloadSectionDescriptors.CreatePipeline()),
            ("ApiMemberDetail", ApiMemberDetailSectionDescriptors.CreatePipeline()),
        ];
        return Assert.Single(
            catalogs,
            candidate => candidate.Pipeline.AllSectionNames.SequenceEqual(
                member.AllSectionNames,
                StringComparer.Ordinal)).Name;
    }

    private static ApiType Find(ApiSurface surface, int metadataToken)
        => surface.Types.Single(type => type.MetadataToken == metadataToken);

    private static ApiType LoadFixtureApiType()
    {
        using var stream = File.OpenRead(
            typeof(BodyShapeFixture).Assembly.Location);
        using var pe = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(pe);
        return Find(surface, typeof(BodyShapeFixture).MetadataToken);
    }

    private static ApiType Find(ApiSurface surface, Type type)
    {
        string metadataName = type.FullName!.Replace('+', '.');
        return surface.Types.Single(candidate =>
            string.Equals(
                candidate.DefinitionName?.ToMetadataFullName(),
                metadataName,
                StringComparison.Ordinal));
    }

    private static string Members(ApiType type)
        => string.Join(
            ",",
            type.Members
                .Select(member => $"{member.Kind}:{member.Name}")
                .Order(StringComparer.Ordinal));

    private sealed record RouteObservation(
        string Route,
        string Discovery,
        string Catalog,
        string ProducerDemand,
        string CapabilityAuthorization);

    private sealed record DeclarationObservation(
        string Type,
        string Full,
        string Summary,
        string Focused);

    private sealed class BreadcrumbObserver(
        ConcurrentQueue<BreadcrumbObservation> observations)
        : IObserver<BreadcrumbObservation>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(BreadcrumbObservation value)
            => observations.Enqueue(value);
    }
}
