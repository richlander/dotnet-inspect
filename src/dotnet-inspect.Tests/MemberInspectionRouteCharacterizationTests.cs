using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
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
        // independently exercises its current product branch. Demand and capability cells
        // distinguish the route's focused plan from the discovery execution observed beside it.
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
        var packageDiscoveryOptions = new InspectionOptions
        {
            Discover = [PackageSections.PackageInfo],
        };
        var packageProducerOptions = PackageCommand.CreateProducerOptions(
            packageDiscoveryOptions with { Verbosity = Verbosity.Detailed },
            packageDiscoveryOptions.Verbosity,
            packagePipeline);
        var packageDiscoveryDemand = new ProducerDemandPlan(
            packageProducerOptions.Verbosity,
            packageProducerOptions.IncludeSections,
            ExcludeUnbounded: true);

        var libraryPipeline = LibrarySections.CreateCatalog().Pipeline;
        HashSet<string> librarySections =
        [
            SectionNames.LibraryInfo,
            SectionNames.SourceLinkFiles,
        ];
        var libraryFocusAuthorization =
            LibrarySourcePlans.For(Verbosity.Minimal, librarySections);
        string libraryFocusCapabilities =
            FormatLibraryCapabilities(libraryFocusAuthorization);
        HashSet<string> libraryInfoSections = [SectionNames.LibraryInfo];
        var libraryInfoFocusAuthorization =
            LibrarySourcePlans.For(Verbosity.Minimal, libraryInfoSections);
        var libraryInfoDiscoveryAuthorization =
            LibrarySourcePlans.For(Verbosity.Normal, libraryInfoSections);

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
        var overloadOptions = new MemberOptions
        {
            TypeName = typeof(OverloadedIndexerBodyShapeFixture).FullName!,
            AssemblyPath = typeof(BodyShapeFixture).Assembly.Location,
            MemberFilter = ["Item"],
            IncludeSections = [SectionNames.MemberIndex],
            TipLevel = TipLevel.Quiet,
        };
        var detailOptions = memberTypeOptions with
        {
            MemberFilter = [nameof(BodyShapeFixture.Classify)],
            OverloadIndex = 1,
        };
        ApiType apiType = LoadFixtureApiType();
        ApiType overloadedApiType =
            LoadFixtureApiType(typeof(OverloadedIndexerBodyShapeFixture));

        RouteObservation[] actual =
        [
            Observe(
                "package",
                await ObservePackageDiscoveryAsync(packageDiscoveryOptions),
                packagePipeline,
                packageSections,
                "focus:vulnerability-traffic="
                    + PackageCommand.AllowsVulnerabilityTraffic(packageOptions)
                    + ";discovery:vulnerability-traffic="
                    + PackageCommand.AllowsVulnerabilityTraffic(
                        packageProducerOptions),
                packageDiscoveryDemand),
            Observe(
                "package-single-library",
                await ObservePackageLibraryDiscoveryAsync(),
                libraryPipeline,
                librarySections,
                $"focus:{libraryFocusCapabilities};discovery:not-reached"),
            Observe(
                "package-all-libraries",
                await ObservePackageAllLibrariesDiscoveryAsync(),
                libraryPipeline,
                libraryInfoSections,
                $"focus:{FormatLibraryCapabilities(libraryInfoFocusAuthorization)};"
                    + "discovery:rejected",
                discoveryRejected: true),
            Observe(
                "direct-library",
                await ObserveLibraryDiscoveryAsync(),
                libraryPipeline,
                librarySections,
                $"focus:{libraryFocusCapabilities};"
                    + "discovery:"
                    + FormatLibraryCapabilities(
                        libraryInfoDiscoveryAuthorization)),
            Observe(
                "assembly-type-list",
                await ObserveApiDiscoveryAsync(assemblyTypeOptions),
                TypePipeline(assemblyTypeOptions),
                [SectionNames.ApiInfo],
                "focus:none"),
            Observe(
                "type-member-list",
                await ObserveApiDiscoveryAsync(typeMemberOptions),
                MemberPipeline(typeMemberOptions),
                typeMemberOptions.IncludeSections!,
                $"focus:pdb={TypeCommand.AuthorizesPdbAcquisition(apiType, typeMemberOptions)};"
                    + $"source={TypeCommand.AuthorizesSourceInfoAcquisition(
                        apiType,
                        typeMemberOptions)}"),
            Observe(
                "member-type-view",
                await ObserveApiDiscoveryAsync(memberTypeOptions),
                MemberPipeline(memberTypeOptions),
                memberTypeOptions.IncludeSections!,
                $"focus:{MemberCapabilities(apiType, memberTypeOptions)}"),
            Observe(
                "overload-inventory",
                await ObserveOverloadInventoryAsync(overloadOptions),
                MemberPipeline(overloadOptions),
                overloadOptions.IncludeSections!,
                $"focus:{MemberCapabilities(overloadedApiType, overloadOptions)}"),
            Observe(
                "exact-member-detail",
                await ObserveApiDiscoveryAsync(detailOptions),
                MemberPipeline(detailOptions),
                detailOptions.IncludeSections!,
                $"focus:{MemberCapabilities(apiType, detailOptions)}"),
            await ObserveHiddenRouterAsync(),
        ];

        RouteObservation[] expected =
        [
            new(
                "package",
                "schema-static-without-target/effective-with-target",
                "Package[schema:54:A1719441C232]",
                "focus=SourceLink: Availability->SourceLink availability;"
                    + "discovery=none",
                "focus:vulnerability-traffic=True;"
                    + "discovery:vulnerability-traffic=False"),
            new(
                "package-single-library",
                "schema/discovery-after-package-acquisition",
                "Library[schema:166:64FBBBE37EC7]",
                "focus=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders;"
                    + "discovery=none",
                "focus:pdb=True;source=True;cached=False;"
                    + "discovery:not-reached"),
            new(
                "package-all-libraries",
                "discovery-rejected",
                "Library[render:Library Info]",
                "focus=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders;discovery=rejected",
                "focus:pdb=False;source=False;cached=False;"
                    + "discovery:rejected"),
            new(
                "direct-library",
                "schema-static-without-target/effective-with-target",
                "Library[schema:166:64FBBBE37EC7]",
                "focus=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders;"
                    + "discovery=Library Info->Classified methods,"
                    + "Library Info->Custom attributes,"
                    + "Library Info->Extension methods,Library Info->Resources,"
                    + "Library Info->Type forwarders,"
                    + "References applicability->Assembly references,"
                    + "discovery catalog->Metadata image",
                "focus:pdb=True;source=True;cached=False;"
                    + "discovery:pdb=False;source=False;cached=False"),
            new(
                "assembly-type-list",
                "schema-static/effective-deferred",
                "ApiType[schema:16:35B2A603B562]",
                "focus=none;discovery=none",
                "focus:none"),
            new(
                "type-member-list",
                "schema-static/effective-deferred",
                "ApiMember[schema:66:FE9290184C28]",
                "focus=none;discovery=none",
                "focus:pdb=True;source=False"),
            new(
                "member-type-view",
                "schema-static/effective-deferred",
                "ApiMember[schema:66:FE9290184C28]",
                "focus=none;discovery=none",
                "focus:pdb=False;source=False"),
            new(
                "overload-inventory",
                "schema-static/effective-deferred/executed-multiple-overloads",
                "ApiMemberOverload[schema:81:0B835ECE3CFC]",
                "focus=none;discovery=none",
                "focus:pdb=False;source=False"),
            new(
                "exact-member-detail",
                "schema-static/effective-deferred",
                "ApiMemberDetail[schema:57:9CF9EB2E407B]",
                "focus=none;discovery=none",
                "focus:pdb=True;source=True"),
            new(
                "hidden-router",
                "router-to-member/schema-static",
                "ApiMember[schema:66:FE9290184C28]",
                "focus=none;discovery=none",
                "focus:none"),
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
                "constructor:.ctor,"
                    + "property:Item[signature=string this[int index] { get; set; }],"
                    + "property:Item[signature=string this[string key] { get; set; }]",
                "method:.ctor,property:Item[signature=<erased>],"
                    + "property:Item[signature=<erased>]",
                "constructor:.ctor,"
                    + "property:Item[signature=string this[int index] { get; set; }],"
                    + "property:Item[signature=string this[string key] { get; set; }]"),
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

    private async Task<DiscoveryObservation> ObservePackageDiscoveryAsync(
        InspectionOptions discoveryOptions)
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
        var schemaTree = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                Discover = [],
                Schema = true,
                Tree = true,
            }));
        Assert.Equal(0, schemaTree.ExitCode);
        Assert.Empty(schemaTree.Error);

        var effective = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(discoveryOptions with
            {
                PackageArgs = [_packagePath],
            }));
        Assert.Equal(0, effective.ExitCode);
        Assert.Contains("| Authors | field", effective.Output);
        Assert.Empty(effective.Error);
        return new(
            "schema-static-without-target/effective-with-target",
            IdentifyCatalog(schema.Output, schemaTree.Output));
    }

    private async Task<DiscoveryObservation> ObservePackageLibraryDiscoveryAsync()
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
        var schemaTree = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(new InspectionOptions
            {
                PackageArgs = [_packagePath],
                PackageLibrary = "",
                Discover = [],
                Schema = true,
                Tree = true,
            }));
        Assert.Equal(0, schemaTree.ExitCode);
        Assert.Empty(schemaTree.Error);

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
        return new(
            "schema/discovery-after-package-acquisition",
            IdentifyCatalog(result.Output, schemaTree.Output));
    }

    private async Task<DiscoveryObservation> ObservePackageAllLibrariesDiscoveryAsync()
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
        return new(
            "discovery-rejected",
            IdentifyCatalogFromRenderedSection(
                render.Output,
                SectionNames.LibraryInfo));
    }

    private static async Task<DiscoveryObservation> ObserveLibraryDiscoveryAsync()
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
        var schemaTree = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                Discover = [],
                Schema = true,
                Tree = true,
            }));
        Assert.Equal(0, schemaTree.ExitCode);
        Assert.Empty(schemaTree.Error);

        var effective = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = typeof(BodyShapeFixture).Assembly.Location,
                Discover = [SectionNames.LibraryInfo],
                Effective = true,
                Trace = true,
            }));
        Assert.Equal(0, effective.ExitCode);
        Assert.Contains("| Architecture | field", effective.Output);
        Assert.Contains("trace: library", effective.Error);
        return new(
            "schema-static-without-target/effective-with-target",
            IdentifyCatalog(schema.Output, schemaTree.Output),
            FormatDemandFromTrace(effective.Error));
    }

    private static async Task<DiscoveryObservation> ObserveApiDiscoveryAsync(
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
        var treeOutput = await ConsoleCapture.RunAsync(() =>
        {
            (ApiCommand.PreambleResult? treePreamble, int? treeExitCode) =
                ApiCommand.RunPreamble(
                    options with
                    {
                        Discover = [],
                        Schema = true,
                        Tree = true,
                    });
            Assert.Null(treePreamble);
            Assert.Equal(0, treeExitCode);
        });
        Assert.NotEmpty(treeOutput.Output);
        Assert.Empty(treeOutput.Error);

        var (effectivePreamble, effectiveExitCode) =
            ApiCommand.RunPreamble(options with
            {
                Discover = [],
                Schema = false,
            });
        Assert.NotNull(effectivePreamble);
        Assert.Null(effectiveExitCode);
        return new(
            "schema-static/effective-deferred",
            IdentifyCatalog(staticOutput.Output, treeOutput.Output));
    }

    private static async Task<DiscoveryObservation> ObserveOverloadInventoryAsync(
        MemberOptions options)
    {
        DiscoveryObservation discovery = await ObserveApiDiscoveryAsync(options);
        var result = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(".Item(int)", result.Output);
        Assert.Contains(".Item(string)", result.Output);
        Assert.DoesNotContain(SectionNames.PdbSource, result.Output);
        Assert.Empty(result.Error);
        return discovery with
        {
            Mode = $"{discovery.Mode}/executed-multiple-overloads",
        };
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
        string[] treeArgs =
            CommandLineBuilder.PreprocessArgs(
                [
                    target,
                    "--library",
                    typeof(BodyShapeFixture).Assembly.Location,
                    "-D",
                    "--schema",
                    "--tree",
                ]);
        Assert.Equal("router", treeArgs[0]);
        var treeRoot = CommandLineBuilder.CreateRootCommand();
        var treeParsed = treeRoot.Parse(treeArgs);
        Assert.Empty(treeParsed.Errors);
        var tree = await ConsoleCapture.RunAsync(
            () => CommandLineBuilder.InvokeAsync(treeParsed, treeArgs));
        Assert.Equal(0, tree.ExitCode);
        Assert.Empty(tree.Error);

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
            IdentifyCatalog(routed.Output, tree.Output),
            "focus=none;discovery=none",
            "focus:none");
    }

    private static string MemberCapabilities(
        ApiType apiType,
        MemberOptions options)
        => $"pdb={MemberCommand.AuthorizesMemberSourceResolution(apiType, options)};"
           + $"source={MemberCommand.AuthorizesMemberSourceContent(apiType, options)}";

    private static string FormatLibraryCapabilities(LibrarySourcePlan plan)
        => $"pdb={plan.AllowPdbDownload};"
           + $"source={plan.CollectSourceFiles};"
           + $"cached={plan.ReadCachedPdb}";

    private static RouteObservation Observe<TModel>(
        string route,
        DiscoveryObservation discovery,
        SectionPipeline<TModel> pipeline,
        HashSet<string> focusedSections,
        string capabilityAuthorization,
        ProducerDemandPlan? discoveryDemand = null,
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
        else if (discovery.ProducerDemand is { } observedDemand)
        {
            discoveryDemandText = observedDemand;
        }
        else if (discoveryDemand is null)
        {
            discoveryDemandText = "none";
        }
        else
        {
            var discoveryTrace = new InspectionTrace();
            pipeline.GetRequiredQueries(
                discoveryDemand.Verbosity,
                discoveryDemand.Include,
                trace: discoveryTrace,
                commandDemand: discoveryDemand.CommandDemand,
                excludeUnbounded: discoveryDemand.ExcludeUnbounded);
            discoveryDemandText = FormatDemand(
                discoveryTrace.QueryDemand
                    .Select(item => (item.Section, item.Query.Name))
                    .Concat(
                        discoveryTrace.CommandQueryDemand.Select(
                            item => (item.Reason, item.Query.Name))));
        }

        return new RouteObservation(
            route,
            discovery.Mode,
            discovery.Catalog,
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

    private static string FormatDemandFromTrace(string trace)
    {
        List<(string Reason, string Query)> demand = [];
        bool readingDemand = false;
        foreach (string line in trace.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed is "  sections demanding a query"
                or "  queries demanded by the command")
            {
                readingDemand = true;
                continue;
            }

            if (trimmed.StartsWith("  ", StringComparison.Ordinal)
                && !trimmed.StartsWith("    ", StringComparison.Ordinal))
            {
                readingDemand = false;
                continue;
            }

            if (!readingDemand
                || !trimmed.StartsWith("    ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = trimmed.Trim().Split(
                " -> ",
                2,
                StringSplitOptions.None);
            Assert.Equal(2, parts.Length);
            demand.Add((parts[0], parts[1]));
        }

        return FormatDemand(demand);
    }

    private static string IdentifyCatalog(
        string schemaOutput,
        string treeOutput)
    {
        string[][] rows = schemaOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 4)
            .ToArray();
        string[] sections = rows
            .Where(parts => parts[2] == "section")
            .Select(parts => parts[1])
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(sections);
        string[] categories = rows
            .Where(parts => parts[2] == "category")
            .Select(parts => parts[1])
            .Order(StringComparer.Ordinal)
            .ToArray();
        (string[] TreeCategories, string[] Edges) =
            ParseCategoryStructure(treeOutput);
        Assert.Equal(categories, TreeCategories);

        string[] structure =
        [
            .. sections.Select(section => $"section:{section}"),
            .. categories.Select(category => $"category:{category}"),
            .. Edges.Select(edge => $"edge:{edge}"),
        ];
        return FormatCatalog(IdentifyCatalogName(sections), structure);
    }

    private static (string[] Categories, string[] Edges)
        ParseCategoryStructure(string treeOutput)
    {
        List<string> categories = [];
        List<string> edges = [];
        string? currentCategory = null;
        foreach (string line in treeOutput.Split(
                     '\n',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            int marker = line.LastIndexOf("\u2500 ", StringComparison.Ordinal);
            if (marker < 0)
                continue;

            string node = line[(marker + 2)..].TrimEnd('\r');
            if (marker == 1)
            {
                currentCategory = node.EndsWith(
                    " (category)",
                    StringComparison.Ordinal)
                    ? node[..^" (category)".Length]
                    : null;
                if (currentCategory is not null)
                    categories.Add(currentCategory);
                continue;
            }

            if (currentCategory is not null)
                edges.Add($"{currentCategory}->{node}");
        }

        return (
            categories.Order(StringComparer.Ordinal).ToArray(),
            edges.Order(StringComparer.Ordinal).ToArray());
    }

    private static string IdentifyCatalogFromRenderedSection(
        string output,
        string section)
    {
        Assert.Contains(section, output);
        string name = Assert.Single(
            Catalogs(),
            candidate => candidate.Sections.Contains(
                section,
                StringComparer.Ordinal)).Name;
        return $"{name}[render:{section}]";
    }

    private static string IdentifyCatalogName(IEnumerable<string> sectionNames)
    {
        HashSet<string> sections = sectionNames.ToHashSet(StringComparer.Ordinal);
        var matches = Catalogs()
            .Select(candidate => new
            {
                candidate.Name,
                Distance = candidate.Sections
                    .Union(sections, StringComparer.Ordinal)
                    .Count(section =>
                        !candidate.Sections.Contains(section, StringComparer.Ordinal)
                        || !sections.Contains(section)),
            })
            .OrderBy(candidate => candidate.Distance)
            .ToArray();
        Assert.True(
            matches.Length > 0
                && (matches.Length == 1
                    || matches[0].Distance < matches[1].Distance),
            $"Schema sections did not identify one catalog: "
                + string.Join(", ", sections.Order(StringComparer.Ordinal)));
        return matches[0].Name;
    }

    private static string FormatCatalog(
        string name,
        IReadOnlyCollection<string> structure)
    {
        string value = string.Join(
            "\n",
            structure.Order(StringComparer.Ordinal));
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
        return $"{name}[schema:{structure.Count}:{fingerprint}]";
    }

    private static (string Name, IReadOnlyList<string> Sections)[] Catalogs()
        =>
        [
            ("Package", PackageSectionDescriptors.CreateCatalog().Pipeline.AllSectionNames),
            ("Library", LibrarySections.CreateCatalog().Pipeline.AllSectionNames),
            ("ApiType", ApiTypeSectionDescriptors.CreatePipeline().AllSectionNames),
            ("ApiMember", ApiMemberSectionDescriptors.CreatePipeline().AllSectionNames),
            ("ApiMemberOverload", ApiMemberOverloadSectionDescriptors.CreatePipeline().AllSectionNames),
            ("ApiMemberDetail", ApiMemberDetailSectionDescriptors.CreatePipeline().AllSectionNames),
        ];

    private static ApiType Find(ApiSurface surface, int metadataToken)
        => surface.Types.Single(type => type.MetadataToken == metadataToken);

    private static ApiType LoadFixtureApiType()
        => LoadFixtureApiType(typeof(BodyShapeFixture));

    private static ApiType LoadFixtureApiType(Type type)
    {
        using var stream = File.OpenRead(
            typeof(BodyShapeFixture).Assembly.Location);
        using var pe = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(pe);
        return Find(surface, type.MetadataToken);
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
    {
        HashSet<(string Kind, string Name)> overloaded = type.Members
            .GroupBy(member => (member.Kind, member.Name))
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();
        return string.Join(
            ",",
            type.Members
                .Select(member =>
                    overloaded.Contains((member.Kind, member.Name))
                        ? $"{member.Kind}:{member.Name}"
                            + $"[signature={member.Signature ?? "<erased>"}]"
                        : $"{member.Kind}:{member.Name}")
                .Order(StringComparer.Ordinal));
    }

    private sealed record RouteObservation(
        string Route,
        string Discovery,
        string Catalog,
        string ProducerDemand,
        string CapabilityAuthorization);

    private sealed record DiscoveryObservation(
        string Mode,
        string Catalog,
        string? ProducerDemand = null);

    private sealed record ProducerDemandPlan(
        Verbosity Verbosity,
        HashSet<string>? Include,
        IReadOnlyList<HostQueryDemand>? CommandDemand = null,
        bool ExcludeUnbounded = false);

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
