using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Findings;
using ILInspector.Research;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public partial class SectionPipelineTests
{
    // Simple test model
    private record TestModel(string? Name, int Count);

    private sealed class DisposableQueryContext(string value) : IDisposable
    {
        public string Value { get; } = value;
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    // Test descriptors
    private sealed class AlwaysSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Always";
        public static bool IsExpensive => false;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class DetailedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Detailed";
        public static bool IsExpensive => true;
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private sealed class QueryBackedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Query-backed";
        public static bool IsExpensive => false;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class NormalSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Normal";
        public static bool IsExpensive => false;
        public static bool CanRender(TestModel model) => model.Name != null;
    }

    private sealed class StructurallyApplicableSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Structural";
        public static bool IsExpensive => false;
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private sealed class UnprobedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Unprobed";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private static SectionPipeline<TestModel> CreateTestPipeline() =>
        new SectionPipeline<TestModel>()
            .Add<AlwaysSection>()
            .Add<NormalSection>()
            .Add<DetailedSection>();

    /// <summary>
    /// The <c>@Metadata</c> sections with no content in the image
    /// <see cref="DiscoverablePipelineCases"/> seeds the library fixture from: tables with no rows
    /// and heaps with no bytes. These are the only data-gated metadata sections allowed to be
    /// absent from discovery.
    /// </summary>
    private static string[] EmptyMetadataSectionsInFixtureImage()
    {
        using var session = AssemblyInspectionSession.Open(typeof(SectionPipelineTests).Assembly.Location);
        var overview = session.MetadataImage();
        if (overview is null)
            return [];

        return
        [
            .. overview.Tables
                .Where(table => table.RowCount == 0)
                .Select(table => MetadataSectionNames.ForTable(table.Index)),
            .. overview.Heaps
                .Where(heap => heap.SizeInBytes == 0)
                .Select(heap => MetadataSectionNames.ForHeap(heap.Heap)),
        ];
    }

    private static readonly string[] SingleOverloadSections =
    [
        SectionNames.Signature,
        SectionNames.CustomAttributes,
        SectionNames.DecompiledSource,
        SectionNames.FidelityCauses,
        SectionNames.AnnotatedSource,
        SectionNames.CostOverlay,
        SectionNames.SemanticsOverlay,
        SectionNames.PdbSource,
        SectionNames.Calls,
        SectionNames.ExceptionRegions,
        SectionNames.Callers,
        SectionNames.CallGraph,
        SectionNames.UnsafeOperations,
        SectionNames.TopLeverage,
        SectionNames.PerformanceTriage,
        SectionNames.Facts,
        SectionNames.IL
    ];

    /// <summary>
    /// Non-vacuity gate for the <see cref="SectionPipeline{TModel}.AddCategory"/> membership
    /// validation. Category membership is declared by name, so without this check a rename that
    /// updated a descriptor but missed a membership list would silently drop the section out of
    /// its category rather than fail. This test is what proves that validation is still wired.
    /// </summary>
    /// <summary>
    /// Non-vacuity gate for the Cost/@All consistency check in
    /// <see cref="SectionPipeline{TModel}.Add(SectionEntry{TModel})"/>. Membership in the
    /// <c>@All</c> pole is computed from <c>IsExpensive</c>/<c>ExplicitOnly</c>, not from
    /// <c>Cost</c>, so the two axes could otherwise drift and let a section costing unbounded
    /// work be rendered by <c>-S @All</c>. This test is what proves that check is still wired.
    /// </summary>
    /// <summary>
    /// Every declared category member must resolve to a registered section in every pipeline that
    /// declares categories. Complements the constructor-time check by covering pipelines this
    /// suite would not otherwise build.
    /// </summary>
    /// <summary>
    /// The whole <c>SourceLink:</c> prefix family is reachable through the <c>@SourceLink</c> door.
    /// A prefix advertises membership, so a prefixed section outside its own category is a
    /// discoverability hole — this pins the family and the door together.
    /// </summary>
    /// <summary>
    /// The package file family and the <c>@Files</c> door are two halves of one claim.
    /// These sections read as noun phrases rather than carrying a <c>Group: Leaf</c> prefix,
    /// so the family is identified by the trailing "file"/"files" noun: every registered
    /// section named that way must either be behind the door or be the one deliberate
    /// exception, plain <c>Package files</c>, which is the unfiltered superset. A section
    /// carrying a <c>Group: Leaf</c> prefix is claimed by that group's door instead.
    ///
    /// The membership list is not restated here. It is derived from the section names on one
    /// side and from <see cref="PackageFileFamily.SectionNames"/> on the other, so adding a
    /// "Package X files" section without wiring the door fails rather than quietly opening a
    /// discoverability hole.
    /// </summary>
    /// <summary>
    /// Every family member declares a predicate, and every predicate reaches a registered section.
    /// This is what lets the view, the descriptors, and the command all read membership from one
    /// place instead of keeping three copies of the same path rules in sync.
    /// </summary>
    /// <summary>
    /// The package and library commands surface the same SourceLink data from the same
    /// collector, so they must spell it the same way. This pins the agreement: renaming one
    /// side without the other fails here rather than silently reintroducing the split where
    /// package called it "Source Files" and library called it "SourceLink: Files".
    /// </summary>
    /// <summary>
    /// Every family member must reach a real view projection. Membership is declared in one
    /// place, but the <see cref="InspectionResultView"/> property and the command's projection
    /// switch are separate hand-edited sites, so a member can be declared and still render
    /// nothing. This drives the check from the declaration: it feeds a model containing one
    /// matching file per member and asserts each section produces rows.
    ///
    /// This is a non-vacuity gate, not a formatting test — it caught <c>Package skill files</c>
    /// returning zero rows for a package that ships four of them.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RunAndReleaseContext(
        CompiledInspectionPlan<DisposableQueryContext> plan)
    {
        var context = new DisposableQueryContext("released");
        _ = plan.Run(context);
        if (context.IsDisposed)
            throw new InvalidOperationException("Composition disposed the supplied context.");
        return new WeakReference(context);
    }

    private static void AssertSectionCatalogQueryPlansMatch<TModel>(
        SectionCatalog<TModel> catalog)
    {
        SectionPipeline<TModel> pipeline = catalog.Pipeline;

        foreach (Verbosity verbosity in Enum.GetValues<Verbosity>())
        {
            AssertPlansMatch(verbosity, include: null, fixedOverview: false);
            AssertPlansMatch(verbosity, include: null, fixedOverview: true);
            AssertPlansMatch(
                verbosity,
                include: null,
                fixedOverview: false,
                excludeUnbounded: true);
            AssertPlansMatch(
                verbosity,
                include: null,
                fixedOverview: true,
                excludeUnbounded: true);
        }

        foreach (string section in catalog.SelectableSectionNames)
        {
            AssertPlansMatch(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section },
                fixedOverview: false);
            AssertPlansMatch(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section },
                fixedOverview: false,
                excludeUnbounded: true);
        }

        foreach (ImmutableArray<string> sections in catalog.CategoryMap.Values)
        {
            AssertPlansMatch(
                Verbosity.Normal,
                new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase),
                fixedOverview: false);
            AssertPlansMatch(
                Verbosity.Normal,
                new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase),
                fixedOverview: false,
                excludeUnbounded: true);
        }

        AssertPlansMatch(
            Verbosity.Detailed,
            [catalog.SelectableSectionNames[0], catalog.SelectableSectionNames[^1]],
            fixedOverview: false);
        AssertPlansMatch(
            Verbosity.Normal,
            new HashSet<string>
            {
                catalog.SelectableSectionNames[0].ToLowerInvariant(),
            },
            fixedOverview: false);

        void AssertPlansMatch(
            Verbosity verbosity,
            HashSet<string>? include,
            bool fixedOverview,
            bool excludeUnbounded = false)
        {
            HashSet<InspectionQueryDefinition> expected = pipeline.GetRequiredQueries(
                verbosity,
                include,
                fixedOverview,
                excludeUnbounded: excludeUnbounded);
            SectionQueryPlan actual = catalog.PlanQueries(
                verbosity,
                include,
                fixedOverview,
                excludeUnbounded);

            Assert.True(expected.SetEquals(actual.Queries));
        }
    }

    /// <summary>
    /// Runs the classified-method and audit-metadata queries over an untouched path.
    /// </summary>
    private static (string Full, string Audit) CensusSignature(string assemblyPath)
    {
        var model = new LibraryInspection();
        using var context = new InspectionQueryContext
        {
            AssemblyPath = assemblyPath,
            Model = model,
            Logger = new Output.VerboseLogger(false),
        };

        RunClassifiedAndAuditQueries(context);

        return (SignatureOf(model), AuditSignatureOf(model));
    }

    private static void RunClassifiedAndAuditQueries(InspectionQueryContext context)
    {
        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [
                AuditMetadataQuery.Definition,
                ClassifiedMethodsQuery.Definition,
            ],
            context);
        LibraryMetadataService.ApplyClassifiedMethodsResult(
            context.AssemblyPath,
            context.Model,
            context.Logger,
            results.Get(ClassifiedMethodsQuery.Definition));
        LibraryMetadataService.ApplyAuditMetadataResult(
            context.AssemblyPath,
            context.Model,
            context.Logger,
            results.Get(AuditMetadataQuery.Definition));
    }

    private static string SignatureOf(LibraryInspection model) => string.Join(
        "|",
        $"classified={PayloadCount(model.ClassifiedMethodInspection)}",
        AuditSignatureOf(model));

    private static string AuditSignatureOf(LibraryInspection model) =>
        $"audit=[{string.Join(",", model.AuditSignals?.Select(s => $"{s.Signal}={s.Value}") ?? [])}]";

    private static int? PayloadCount<T>(FindingInspection<T>? inspection) where T : notnull
        => inspection?.Value is FindingInspection<T>.Complete complete ? complete.Findings.Length : null;

    /// <summary>
    /// Points <paramref name="link"/> at <paramref name="target"/>, replacing any existing link.
    /// Prefers a symbolic link and falls back to a Windows junction, which needs no privilege.
    /// </summary>
    private static bool TryLinkDirectory(string link, string target)
    {
        if (Directory.Exists(link))
            Directory.Delete(link);

        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process!.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(link);
        }
    }

    private static InspectionQueryContext NullQueryContext() => new()
    {
        AssemblyPath = "unused.dll",
        Model = new LibraryInspection(),
        Logger = new Output.VerboseLogger(false),
    };

    private static Analysis.OptimizationOpportunity PerformanceOpportunity(
        string shape)
        => new(
            new Analysis.MethodIdentity(
                "Test",
                Guid.Empty,
                Analysis.TypeRef.Definition("Test", "Some", "Type"),
                "Method",
                [],
                Analysis.TypeRef.CoreLib("System", "Void"),
                0x06000001,
                IsStatic: true),
            shape,
            "delegate over a captured receiver or closure",
            "Use a static local function.",
            "high",
            InLoop: false,
            ILOffset: 0,
            Caveat: null);

    public static IEnumerable<object[]> DiscoverablePipelineCases()
    {
        var libraryPipeline = LibrarySections.CreatePipeline();
        var library = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo
            {
                AssemblyName = "Ѕystem.Test",
                References = [new AssemblyReference("System.Runtime", "1.0.0.0", null, null)],
                TransitiveReferences = [new AssemblyReferenceNode { Name = "System.Runtime", Version = "1.0.0.0" }]
            },
            EcosystemDependencyRecognitionInspection =
                LibraryEcosystemDependencyRecognitionInspection.Execute(
                    new ExactLibrarySourceCoordinate.Local(
                        new ManagedMetadataIdentity.Assembly(
                            new AssemblyReferenceIdentity(
                                "System.Test",
                                new Version(1, 0, 0, 0),
                                null,
                                null))),
                    new AssemblyReferencesResult.Available(
                        [
                            new AssemblyReferenceIdentity(
                                "System.Runtime",
                                new Version(1, 0, 0, 0),
                                null,
                                null),
                        ])),
            PdbPath = "test.pdb",
            HasSourceLink = true,
            HasEmbeddedPdb = true,
            HasSwitches = true,
            HasExtensionTypes = true,
            HasUnsafeCode = true,
            HasMethodBodies = true,
            HasPInvokeImports = true,
            HasRuntimeAsync = true,
            HasStateMachineAsync = true,
            HasManifestResources = true,
            HasAssemblyAttributes = true,
            HasExportedTypeForwarders = true,
            HasUnionTypes = true,
            HasAspNetCoreSupport = true,
            HasAspireSupport = true,
            HasOpenTelemetrySupport = true,
            HasAISupport = true,
            HasAuthenticationSupport = true,
            HasConfigurationSupport = true,
            HasDependencyInjectionSupport = true,
            HasLoggingSupport = true,
            HasOptionsSupport = true,
            HasHostingSupport = true,
            HasHealthChecksSupport = true,
            HasHttpClientSupport = true,
            HasOpenApiSupport = true,
            IntegrationCount = 1,
            SourceFiles = [new SourceFileInfo("T", "https://example.com/T.cs")],
            AllSourcesAccessible = true,
            TotalSourceFiles = 1,
            MissingSourceFiles = ["missing.cs"],
            SourceIntegrityChecked = true,
            AuditSignals = [new AuditSignal("Provenance", "SourceLink", "Present", "test")],
            SwitchInspection = MetadataFindings.InspectSwitches(
                [new SwitchInfo("Feature Switch", "Switch", "Api")],
                FindingTestData.Subject),
            UnsafeMembers = [new UnsafeMemberSummary { Member = "T.M()", Reason = "Unsafe signature", Detail = "int*", Kind = "signature" }],
            TopLeverageQueryResult = new TopLeverageResult.Available(
                [
                    new Analysis.MethodLeverage(
                        new Analysis.MethodIdentity(
                            "Test",
                            Guid.Empty,
                            Analysis.TypeRef.Definition("Test", "", "T"),
                            "M",
                            [],
                            Analysis.TypeRef.CoreLib("System", "Void"),
                            0x06000001,
                            IsStatic: true),
                        DirectCallerCount: 1,
                        Fanout: 0,
                        MaxDepth: 1,
                        LoopCallCount: 0)
                ],
                ImmutableHashSet<Analysis.TypeRef>.Empty,
                []),
            TopLeverage = [new MethodLeverageSummary { Member = "T.M()", Callers = 1 }],
            OptimizationOpportunities =
            [
                new OptimizationOpportunitySummary
                {
                    Member = "T.M()",
                    Shape = "capturing-delegate",
                    Evidence = "delegate over a captured receiver or closure",
                    Fix = "Use a static local function.",
                    Confidence = "high"
                }
            ],
            PerformanceTriageOpportunities =
            [
                PerformanceOpportunity("capturing-delegate"),
            ],
            PInvokeMethods = [new ClassifiedMethodSummary { MethodName = "P", DeclaringType = "T", Signature = "void P()" }],
            AsyncMethods = [new AsyncMethodSummary { MethodName = "A", DeclaringType = "T", Signature = "void A()" }],
            ResourceInspection = MetadataFindings.InspectResources(
                [new ManifestResourceInfo("res", IsPublic: true, IsEmbedded: true, Size: 1)],
                FindingTestData.Subject),
            TypeForwarderInspection = MetadataFindings.InspectTypeForwarders(
                [new TypeForwarderInfo("T", "Other")],
                FindingTestData.Subject),
            NonNormalizedPaths = ["C:\\src\\T.cs"],
            IntegrationOpportunities = [new IntegrationOpportunityInfo("Aspire", "T", "Builder", "Add*")],
            EcosystemIntegrationInspection = MetadataFindings.InspectEcosystemIntegrations(
                [
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.AI, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.AspNetCore, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Authentication, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Aspire, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Configuration, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.DependencyInjection, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Logging, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.OpenAPI, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Options, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.Hosting, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.HealthChecks, "T", "M"),
                    new EcosystemIntegrationSignalInfo(EcosystemIntegrationNames.HttpClient, "T", "M"),
                ],
                FindingTestData.Subject),
            OpenTelemetryInspection = MetadataFindings.InspectOpenTelemetrySignals(
                [new OpenTelemetrySignalInfo("T", "M")],
                FindingTestData.Subject),
        };
        library.SetAssemblyAttributeInspection(
            MetadataFindings.InspectAssemblyAttributes(
                [new AssemblyAttributeInfo("Attr", "Assembly", null)],
                FindingTestData.Subject),
            jsonOrder: null);
        ExtensionMethodInfo[] extensionMembers =
        [
            FindingTestData.ExtensionMember("Ext", "Target"),
        ];
        library.SetExtensionMemberInspection(
            MetadataFindings.InspectExtensionMembers(
                extensionMembers,
                FindingTestData.Subject),
            extensionMembers);
        // The @Metadata lens gates on real per-table row counts rather than a Has* flag, so this
        // fixture seeds them from an actual image. A hand-built overview would have to restate the
        // projector's table list, which is exactly the drift MetadataSectionNames exists to
        // prevent; reading a real assembly keeps the fixture correct as tables are added.
        using (var session = AssemblyInspectionSession.Open(typeof(SectionPipelineTests).Assembly.Location))
            library.MetadataImageResult = session.MetadataImage() is { } overview
                ? new MetadataImageResult.Available(overview)
                : new MetadataImageResult.NoMetadata();
        yield return DiscoverableCase("library", libraryPipeline, library);

        var packagePipeline = PackageSectionDescriptors.CreatePipeline();
        var package = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Owners = ["audit\u202Ecase"],
            PackageReadmeFile = "README.md",
            PackageFiles =
            [
                new PackageFile("README.md", 1, IsReadme: true),
                new PackageFile("docs/guide.md", 1),
                new PackageFile("lib/net8.0/Test.dll", 1),
                new PackageFile("ref/net8.0/Test.dll", 1),
                new PackageFile("runtimes/win-x64/native/Test.dll", 1),
                new PackageFile("Test.nuspec", 1),
                new PackageFile("LICENSE", 1, IsLicense: true),
                new PackageFile("skills/demo/SKILL.md", 1)
            ],
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")],
            TotalDownloads = 1,
            TargetFrameworks = ["net8.0"],
            LibraryFiles = ["lib/net8.0/Test.dll"],
            SourceFiles = [new PackageSourceFileInfo("lib/net8.0/Test.dll", "T", "https://example.com/T.cs")],
            SignatureResult = new SignatureVerificationResult { AuthorVerified = true, Publisher = "test" },
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Ѕystem.Dep", Version = "1.0" }] }],
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }],
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }],
            RuntimeDependencies = [new PackageDependency { Id = "Runtime.Dep", Version = "1.0" }],
            Files = [new PackageFile("lib/net8.0/Test.dll", 1)],
            AssemblyCount = 1,
            DependencyHierarchyProjection =
                CreateEmptyDependencyHierarchyProjection(),
        };
        yield return DiscoverableCase("package", packagePipeline, package);

        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var surface = new ApiSurface
        {
            InspectionFailures =
            [
                new ApiSurfaceInspectionFailure(
                    "test",
                    0,
                    MetadataTypeNameFailureMechanism.Metadata,
                    "Rejected",
                    "test"),
            ],
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
            ]
        };
        var forwardingSurface = new ApiSurface
        {
            TypeForwarders =
            [
                new TypeForwarder
                {
                    TypeName = "Forwarded",
                    TargetAssembly = "Target",
                },
            ],
        };
        yield return DiscoverableCase(
            "type",
            typePipeline,
            surface,
            forwardingSurface);

        var apiType = new ApiType
        {
            Name = "Sample",
            Kind = "enum",
            BaseType = "Base",
            Interfaces = ["IDisposable"],
            SourceUrl = "https://example.com/Sample.cs",
            AdditionalSourceFiles =
            [
                new PartialSourceFileInfo
                {
                    FilePath = "Sample.Other.cs",
                    SourceUrl = "https://example.com/Sample.Other.cs"
                }
            ],
            TypeParameters = [new TypeParameter { Name = "T" }],
            Members =
            [
                new ApiMember { Name = "Value", Kind = "field", EnumValue = 1 },
                new ApiMember { Name = ".ctor", Kind = "constructor" },
                new ApiMember { Name = "Finalize", Kind = "finalizer" },
                new ApiMember { Name = "Field", Kind = "field" },
                new ApiMember { Name = "Property", Kind = "property" },
                new ApiMember { Name = "Method", Kind = "method" },
                new ApiMember { Name = "op_Equality", Kind = "operator" },
                new ApiMember { Name = "IFoo.Bar", Kind = "explicit-interface-implementation" },
                new ApiMember { Name = "Ext", Kind = "extension-method" },
                new ApiMember { Name = "Changed", Kind = "event" }
            ]
        };
        var detailPipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var detailType = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members = [new ApiMember { Name = "Method", Kind = "method", HasMethodBody = true }]
        };
        var memberPipeline = ApiMemberSectionDescriptors.CreatePipeline();
        yield return DiscoverableCase("member", memberPipeline, apiType, detailType);
        var overloadPipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        yield return DiscoverableCase("member-overload", overloadPipeline, apiType, detailType);
        yield return DiscoverableCase("member-detail", detailPipeline, detailType);

        var diffPipeline = DiffSections.CreatePipeline();
        yield return DiscoverableCase("diff", diffPipeline, new DiffDiscoveryModel());
    }

    private static DependsAssetProjection
        CreateEmptyDependencyHierarchyProjection()
    {
        var summary = new DependencyInspectionSummary(
            DependencyInspectionRootSetCompletion.Complete,
            RequestedRoots: 0,
            AdmittedRoots: 0,
            FailedRoots: 0,
            DependencyInspectionTraversalCompletion.Complete,
            RequestedDepth: null,
            HierarchyOccurrences: 0,
            CanonicalNodes: 0,
            Relationships: 0,
            DependencyInspectionEvidencePhaseCompletion.NotRequested,
            DependencyInspectionEvidencePhaseCompletion.NotRequested,
            DependencyInspectionPruningSummary.NotRequested,
            IsPrefixRootSet: false,
            PackagePrefix: null);
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Empty;
        var content = new DependencyInspectionContent(
            summary,
            hierarchy,
            Roots: [],
            Dependencies: [],
            Pruning: [],
            Failures: []);
        var inspection =
            new InspectionEnvelope<DependencyInspectionContent>(
                new ResourcePath("asset-dependencies"),
                InspectionContentKind.Document,
                content,
                new InspectionPortableProjection.NonProjectable(
                    InspectionPortableProjectionFailureReason.NotSupported));
        return new DependsAssetProjection(
            inspection,
            summary,
            hierarchy.BackingGraph,
            hierarchy,
            HierarchyRows: [],
            Roots: [],
            Dependencies: [],
            Pruning: [],
            RestoredEdges: [],
            Failures: [],
            DependencyGroups: [],
            RestoredPackages: [],
            Enriched: null);
    }

    private static object[] DiscoverableCase<TModel>(
        string command,
        SectionPipeline<TModel> pipeline,
        params TModel[] models)
    {
        var discoverable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
            discoverable.UnionWith(pipeline.GetDiscoverableSections(model));

        return [command, pipeline.SelectableSectionNames, discoverable.ToArray()];
    }

}
