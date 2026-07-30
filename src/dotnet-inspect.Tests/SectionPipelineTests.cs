using ILInspector.Metadata;
using ILInspector.Findings;
using ILInspector.Research;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

public class SectionPipelineTests
{
    // Simple test model
    private record TestModel(string? Name, int Count);

    // Test descriptors
    private sealed class AlwaysSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Always";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(TestModel model) => true;
    }

    private sealed class DetailedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Detailed";
        public static bool IsExpensive => true;
        public static string? ScannerKey => "DetailedScanner";
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private sealed class NormalSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Normal";
        public static bool IsExpensive => false;
        public static string? ScannerKey => "NormalScanner";
        public static bool CanRender(TestModel model) => model.Name != null;
    }

    private sealed class StructurallyApplicableSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Structural";
        public static bool IsExpensive => false;
        public static string? ScannerKey => null;
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private sealed class UnprobedSection : ISectionDescriptor<TestModel>
    {
        public static string Name => "Unprobed";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool ProbeEffectiveness => false;
        public static string? ScannerKey => null;
        public static bool CanRender(TestModel model) => model.Count > 0;
    }

    private static SectionPipeline<TestModel> CreateTestPipeline() =>
        new SectionPipeline<TestModel>()
            .Add<AlwaysSection>()
            .Add<NormalSection>()
            .Add<DetailedSection>();

    [Fact]
    public void AllSectionNames_ReturnsRegisteredNames()
    {
        var pipeline = CreateTestPipeline();

        var names = pipeline.AllSectionNames;

        Assert.Equal(["Always", "Normal", "Detailed"], names);
    }

    [Fact]
    public void Add_RuntimeEntry_PreservesPipelineSemantics()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add(new SectionEntry<TestModel>
            {
                Name = "Runtime",
                IsExpensive = false,
                ExplicitOnly = false,
                Info = true,
                ProbeEffectiveness = true,
                Capabilities = SectionCapabilities.None,
                ScannerKey = null,
                HasExplicitApplicability = true,
                IsApplicable = model => model.Name != null,
                CanRender = model => model.Count > 0,
            });

        Assert.Equal(["Runtime"], pipeline.AllSectionNames);
        Assert.Equal(["Runtime"], pipeline.GetDiscoverableSections(new TestModel("target", 0)));
        Assert.Empty(pipeline.GetEffectiveSections(new TestModel("target", 0), Verbosity.Minimal));
        Assert.Equal(["Runtime"], pipeline.GetEffectiveSections(new TestModel("target", 1), Verbosity.Minimal));
    }

    [Fact]
    public void Add_UnprobedRuntimeEntry_AllowsStructuralApplicability()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add(new SectionEntry<TestModel>
            {
                Name = "Heavy",
                IsExpensive = true,
                ExplicitOnly = false,
                Info = false,
                ProbeEffectiveness = false,
                Capabilities = SectionCapabilities.None,
                ScannerKey = null,
                HasExplicitApplicability = true,
                IsApplicable = model => model.Name != null,
                CanRender = model => model.Count > 0,
            });

        Assert.Equal(["Heavy"], pipeline.GetDiscoverableSections(new TestModel("target", 0)));
        Assert.Contains("Heavy", pipeline.GetUnprobedSections());
    }

    [Fact]
    public void GetEffectiveSections_MinimalVerbosity_ReturnsPrimarySections()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Primary threshold = 1 (everything before first expensive at index 2)
        Assert.Equal(["Always", "Normal"], effective);
    }

    [Fact]
    public void GetEffectiveSections_NormalVerbosity_ReturnsMinimalAndNormal()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Equal(["Always", "Normal"], effective);
    }

    [Fact]
    public void GetEffectiveSections_DetailedVerbosity_ReturnsAll()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Equal(["Always", "Normal", "Detailed"], effective);
    }

    [Fact]
    public void GetEffectiveSections_CanRenderFalse_ExcludesSection()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel(null, 0); // NormalSection and DetailedSection CanRender = false

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Equal(["Always"], effective);
    }

    [Fact]
    public void GetApplicableSections_UsesRegistrationApplicability()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add<StructurallyApplicableSection>(model => model.Name != null);
        var model = new TestModel("target", 0);

        var applicable = pipeline.GetApplicableSections(model);
        var available = pipeline.GetAvailableSections(model);
        var explicitlyApplicable = pipeline.GetExplicitlyApplicableSections(model);

        Assert.Equal(["Structural"], applicable);
        Assert.Empty(available);
        Assert.Equal(["Structural"], explicitlyApplicable);
    }

    [Fact]
    public void GetDiscoverableSections_UnprobedStillRequiresApplicability()
    {
        var pipeline = new SectionPipeline<TestModel>()
            .Add<UnprobedSection>();

        Assert.Empty(pipeline.GetDiscoverableSections(new TestModel("target", 0)));
        Assert.Equal(["Unprobed"], pipeline.GetDiscoverableSections(new TestModel("target", 1)));
    }

    [Fact]
    public void GetEffectiveSections_IncludeOverridesVerbosity()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);
        var include = new HashSet<string> { "Detailed" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal, include);

        Assert.Equal(["Detailed"], effective);
    }

    [Fact]
    public void ComputeIncludeSections_AllEffective_ReturnsNull()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        var result = pipeline.ComputeIncludeSections(model, Verbosity.Detailed);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeIncludeSections_SubsetEffective_ReturnsHashSet()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);

        // At Minimal, primary sections (Always + Normal) are effective but not Detailed
        var result = pipeline.ComputeIncludeSections(model, Verbosity.Minimal);

        Assert.NotNull(result);
        Assert.Contains("Always", result);
        Assert.Contains("Normal", result);
        Assert.DoesNotContain("Detailed", result);
    }

    [Fact]
    public void ComputeIncludeSections_WithIncludeFilter_ReturnsOnlyMatching()
    {
        var pipeline = CreateTestPipeline();
        var model = new TestModel("test", 5);
        var include = new HashSet<string> { "Detailed" };

        var result = pipeline.ComputeIncludeSections(model, Verbosity.Minimal, include);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Contains("Detailed", result);
    }

    [Fact]
    public void GetRequiredVerbosity_NullInclude_ReturnsQuiet()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(null);

        Assert.Equal(Verbosity.Quiet, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_EmptyInclude_ReturnsQuiet()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity([]);

        Assert.Equal(Verbosity.Quiet, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_PrimarySection_ReturnsQuiet()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Always" });

        Assert.Equal(Verbosity.Quiet, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_DetailedSection_ReturnsDetailed()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Detailed" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    [Fact]
    public void GetRequiredVerbosity_MixedSections_ReturnsHighest()
    {
        var pipeline = CreateTestPipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Always", "Detailed" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    // ===== Library pipeline integration tests =====

    [Fact]
    public void LibraryPipeline_HasExpectedSectionCount()
    {
        var pipeline = LibrarySections.CreatePipeline();

        // The non-metadata sections stay pinned to a literal, so an accidental addition still
        // trips this. The @Metadata family is derived from MetadataTableProjector.ProjectedTables
        // (see MetadataSectionNames), so it is counted by derivation rather than re-pinned here —
        // otherwise adding a table to the projector would fail an unrelated test.
        Assert.Equal(53 + MetadataSectionNames.All.Length, pipeline.AllSectionNames.Length);
        Assert.Contains("Integration: AI", pipeline.AllSectionNames);
        Assert.Contains("Integration: ASP.NET Core", pipeline.AllSectionNames);
        Assert.Contains("Integration: Aspire", pipeline.AllSectionNames);
        Assert.Contains("Integration: Authentication", pipeline.AllSectionNames);
        Assert.Contains("Context: Callsite", pipeline.AllSectionNames);
        Assert.Contains("Context: Allocation", pipeline.AllSectionNames);
        Assert.Contains("Context: Safety", pipeline.AllSectionNames);
        Assert.Contains("Context: Cost", pipeline.AllSectionNames);
        Assert.Contains("Integration: Configuration", pipeline.AllSectionNames);
        Assert.Contains("Integration: Dependency Injection", pipeline.AllSectionNames);
        Assert.Contains("Context: Exception", pipeline.AllSectionNames);
        Assert.Contains("Integration: Health Checks", pipeline.AllSectionNames);
        Assert.Contains("Integration: Hosting", pipeline.AllSectionNames);
        Assert.Contains("Integration: HTTP Client", pipeline.AllSectionNames);
        Assert.Contains("Context: Instruction", pipeline.AllSectionNames);
        Assert.Contains("Context: Source Location", pipeline.AllSectionNames);
        Assert.Contains("Context: Member", pipeline.AllSectionNames);
        Assert.Contains("Integration: Opportunities", pipeline.AllSectionNames);
        Assert.Contains("Integration: Logging", pipeline.AllSectionNames);
        Assert.Contains("Integration: OpenAPI", pipeline.AllSectionNames);
        Assert.Contains("Integration: OpenTelemetry", pipeline.AllSectionNames);
        Assert.Contains("Integration: Options", pipeline.AllSectionNames);
        Assert.Contains("SourceLink: Files", pipeline.AllSectionNames);
        Assert.Contains("SourceLink: Availability", pipeline.AllSectionNames);
        Assert.Contains("SourceLink: Missing Files", pipeline.AllSectionNames);
        Assert.Contains("SourceLink: Integrity", pipeline.AllSectionNames);
        Assert.Contains("Switches", pipeline.AllSectionNames);
        Assert.Contains("Top Leverage", pipeline.AllSectionNames);
        Assert.Contains("Performance: Boxing", pipeline.AllSectionNames);
        Assert.Contains("Performance: Arrays", pipeline.AllSectionNames);
        Assert.Contains("Performance: Closures and Delegates", pipeline.AllSectionNames);
        Assert.Contains("Performance: Enumerators", pipeline.AllSectionNames);
        Assert.Contains("Performance: Loop Hot Paths", pipeline.AllSectionNames);
        Assert.Contains("Performance: Allocation Hotspots", pipeline.AllSectionNames);
        Assert.Contains("Performance: Async", pipeline.AllSectionNames);
        Assert.Contains("Performance: Other", pipeline.AllSectionNames);
        Assert.DoesNotContain("Performance Triage", pipeline.AllSectionNames);
        Assert.Contains("Array Pool Escapes", pipeline.AllSectionNames);
        Assert.Contains("Context: Return Address", pipeline.AllSectionNames);
        Assert.Contains("Union Types", pipeline.AllSectionNames);
    }

    [Fact]
    public void LibraryPipeline_CatalogHiddenSections_ExcludeAllMembersIncludeFeeders()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var hidden = pipeline.GetCatalogHiddenSections();

        // Curated catalog: the -D top level lists the visible "spine" (size-classed, bounded,
        // network-free sections) plus the topical category doors. Catalog-hidden are the sections
        // that must never appear in the flat top level: the unbounded/expensive footguns
        // (reached through a category door or by exact name) and the coordinate-gated IL context
        // sections (reached only when an --il-offset makes them applicable).

        // Visible spine members are never catalog-hidden — including the now-size-classed
        // sections that used to be opt-in (Switches, Custom Attributes, Non-normalized Paths, ...).
        var visible = new List<string>
        {
            "Library Info", "Symbols", "Signals", "References", "Dependencies",
            "Async Methods", "Custom Attributes", "Extension Methods",
            "P/Invoke Methods", "Type Forwarders", "Union Types",
            "Switches", "Resources", "Non-normalized Paths"
        };
        foreach (var name in visible)
            Assert.DoesNotContain(name, hidden);

        // Footguns (unbounded/expensive), the kind-scoped performance sub-group (kept behind the
        // @Performance door via ListedInCatalog=false), the ecosystem integration sub-group (kept
        // behind the @Integrations door the same way), and coordinate IL-context sections ARE
        // catalog-hidden.
        foreach (var kind in PerformanceKinds.Sections)
            Assert.Contains(kind, hidden);
        foreach (var integration in LibraryIntegrationCatalog.CategorySections.Append(IntegrationSectionNames.Opportunities))
            Assert.Contains(integration, hidden);
        foreach (var footgun in new[]
                 {
                     "Top Leverage", "Unsafe Members", "SourceLink: Integrity",
                     "SourceLink: Files", "SourceLink: Availability", "SourceLink: Missing Files",
                     "Context: Member"
                 })
        {
            Assert.Contains(footgun, hidden);
        }

        // Every catalog-hidden section is still registered and selectable by name.
        foreach (var name in hidden)
        {
            Assert.Contains(name, pipeline.AllSectionNames);
        }
    }

    [Theory]
    [MemberData(nameof(DiscoverablePipelineCases))]
    public void DiscoverableSections_ContainEverySelectableSection(
        string command,
        string[] registered,
        IReadOnlyCollection<string> discoverable)
    {
        var expected = command == "library"
            ? registered
                .Except(["Context: Source Location", "Inspection Failures", "Context: Member", "Context: Instruction", "Context: Exception", "Context: Callsite", "Context: Return Address", "Context: Allocation", "Context: Safety", "Context: Cost"], StringComparer.OrdinalIgnoreCase)
                // Coordinate-gated like the Context: sections above: without --heap there is no
                // value to show, so the section is legitimately not discoverable.
                .Except([MetadataSectionNames.Heap], StringComparer.OrdinalIgnoreCase)
                // @Metadata table and heap sections are data-gated: a table with no rows or a heap
                // with no bytes in this image is legitimately not discoverable, and listing it
                // would advertise an empty section. Derived from the fixture image rather than
                // hard-coded, so one that gains or loses content moves the exclusion with it and
                // every non-empty one stays required.
                .Except(EmptyMetadataSectionsInFixtureImage(), StringComparer.OrdinalIgnoreCase)
            : registered;
        var missing = expected
            .Where(name => !discoverable.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{command} -D missed selectable section(s): {string.Join(", ", missing)}");
    }

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

    [Fact]
    public void MemberOverloadPipeline_MultiOverload_DoesNotDiscoverSingleOverloadSections()
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        var type = new ApiType
        {
            Namespace = "N",
            Name = "T",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "method", Name = "M" },
                new ApiMember { Kind = "method", Name = "M" }
            ]
        };

        var discoverable = pipeline.GetDiscoverableSections(type);

        foreach (var section in SingleOverloadSections)
            Assert.DoesNotContain(section, discoverable);
    }

    [Fact]
    public void MemberOverloadPipeline_WithFinalizer_DiscoversFinalizerSection()
    {
        // Regression guard (adversarial review): an unindexed finalizer query
        // (`member ... -m Finalize`) resolves the member but rendered nothing
        // because the overload-inventory pipeline never registered the Finalizer
        // section (it was only in the type pipeline). Selecting `-S Finalizer`
        // reported "not found".
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        var type = new ApiType
        {
            Namespace = "N",
            Name = "T",
            Kind = "class",
            Members =
            [
                new ApiMember { Kind = "finalizer", Name = "Finalize", IsFinalizer = true }
            ]
        };

        var discoverable = pipeline.GetDiscoverableSections(type);

        Assert.Contains(SectionNames.Finalizer, discoverable);
    }

    [Fact]
    public void MemberPipeline_NoMemberType_DoesNotDiscoverMethodBodySections()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var type = new ApiType
        {
            Namespace = "N",
            Name = "Empty",
            Kind = "interface"
        };

        var discoverable = pipeline.GetDiscoverableSections(type);

        Assert.DoesNotContain(SectionNames.TopLeverage, discoverable);
        Assert.DoesNotContain(SectionNames.PerformanceTriage, discoverable);
        Assert.DoesNotContain(SectionNames.Facts, discoverable);
        Assert.DoesNotContain(SectionNames.CostOverlay, discoverable);
        Assert.DoesNotContain(SectionNames.SemanticsOverlay, discoverable);
        Assert.DoesNotContain(SectionNames.IL, discoverable);
        Assert.DoesNotContain(SectionNames.SourceFiles, discoverable);
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
        SectionNames.OriginalSource,
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

    [Fact]
    public void MemberDetailPipeline_OptimizationOpportunities_IsStructurallyDiscoverable()
    {
        // -D must over-report: Performance Triage is index-backed and unprobed
        // (ProbeEffectiveness=false), so it must be listed structurally in the single-member
        // detail pipeline for any type with method-like members, even without selection.
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var type = new ApiType
        {
            Namespace = "N",
            Name = "T",
            Kind = "class",
            Members = [new ApiMember { Kind = "method", Name = "M" }]
        };

        var applicable = pipeline.GetApplicableSections(type);
        var unprobed = pipeline.GetUnprobedSections();

        Assert.Contains(SectionNames.PerformanceTriage, applicable);
        Assert.Contains(SectionNames.PerformanceTriage, unprobed);
    }

    [Fact]
    public void MemberDetailPipeline_TopLeverage_IsStructurallyDiscoverable()
    {
        // Top Leverage mirrors Performance Triage: index-backed and unprobed
        // (ProbeEffectiveness=false), so -D must list it structurally in the single-member
        // detail pipeline for any type with method-like members (#1264).
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var type = new ApiType
        {
            Namespace = "N",
            Name = "T",
            Kind = "class",
            Members = [new ApiMember { Kind = "method", Name = "M" }]
        };

        var applicable = pipeline.GetApplicableSections(type);
        var unprobed = pipeline.GetUnprobedSections();

        Assert.Contains(SectionNames.TopLeverage, applicable);
        Assert.Contains(SectionNames.TopLeverage, unprobed);
    }

    [Fact]
    public void CanRender_IntegrationOpportunities_UsesScannedRows()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            IntegrationOpportunities =
            [
                new IntegrationOpportunityInfo("Aspire", "Amazon.S3.AmazonS3Client", "AppHost resource builder", "IResourceBuilder<T>, Add*, *Resource")
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Integration: Opportunities" });

        Assert.Contains("Integration: Opportunities", effective);
        Assert.Contains("Integration: Opportunities", selected);
    }

    [Fact]
    public void CanRender_OptimizationOpportunities_UsesScannedRows()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            OptimizationOpportunities =
            [
                new OptimizationOpportunitySummary
                {
                    Member = "Some.Type.Method()",
                    Shape = "capturing-delegate",
                    Evidence = "delegate over a captured receiver or closure",
                    Fix = "Each call allocates a closure delegate; a static local function with explicit state parameters avoids it.",
                    Confidence = "high",
                    Loop = "",
                }
            ]
        };

        // capturing-delegate buckets into the "Closures and Delegates" kind section.
        const string section = "Performance: Closures and Delegates";
        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section });

        Assert.Contains(section, effective);
        Assert.Contains(section, selected);
    }

    [Fact]
    public void CanRender_Switches_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasSwitches = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Switches" });

        Assert.Contains("Switches", effective);
        Assert.Contains("Switches", selected);
    }

    [Fact]
    public void LibraryPipeline_LibraryInfoShowsAtMinimal()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Library Info", effective);
    }

    [Fact]
    public void LibraryPipeline_SourceIntegrityNeverAutoSelectedByVerbosity()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            SourceIntegrityChecked = true
        };

        // Even at Detailed, an ExplicitOnly section must not be auto-selected.
        var detailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        Assert.DoesNotContain("SourceLink: Integrity", detailed);

        // It renders only when explicitly included.
        var included = pipeline.GetEffectiveSections(model, Verbosity.Normal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SourceLink: Integrity" });
        Assert.Contains("SourceLink: Integrity", included);
    }

    [Fact]
    public void LibraryPipeline_SourceLinkAuditDiscovery_UsesSymbolDependentApplicability()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            PdbPath = "Library.pdb",
            // A resolved PDB that exposes a SourceLink document is the symbol-dependent gate
            // that makes the SourceLink family discoverable (network-free) under -D.
            HasSourceLink = true
        };

        var applicable = pipeline.GetApplicableSections(model);
        var renderable = pipeline.GetAvailableSections(model);

        Assert.Contains("SourceLink: Availability", applicable);
        Assert.Contains("SourceLink: Missing Files", applicable);
        Assert.Contains("SourceLink: Integrity", applicable);
        Assert.DoesNotContain("SourceLink: Availability", renderable);
        Assert.DoesNotContain("SourceLink: Missing Files", renderable);
        Assert.DoesNotContain("SourceLink: Integrity", renderable);
    }

    [Fact]
    public void LibraryPipeline_SourceLinkFamily_NotDiscoverableWithoutSourceLink()
    {
        var pipeline = LibrarySections.CreatePipeline();
        // A recorded PDB path with no resolvable SourceLink document must NOT list the
        // SourceLink family in discovery (hyper-subscribe: the @SourceLink door disappears).
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            PdbPath = "Library.pdb",
            HasSourceLink = false
        };

        var applicable = pipeline.GetApplicableSections(model);

        Assert.DoesNotContain("SourceLink: Files", applicable);
        Assert.DoesNotContain("SourceLink: Availability", applicable);
        Assert.DoesNotContain("SourceLink: Missing Files", applicable);
        Assert.DoesNotContain("SourceLink: Integrity", applicable);
    }

    [Fact]
    public void LibraryPipeline_ILCoordinateSections_RequireResolvedCoordinate()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true
        };

        var applicable = pipeline.GetApplicableSections(model);
        var renderable = pipeline.GetAvailableSections(model);

        Assert.DoesNotContain("Context: Source Location", applicable);
        Assert.DoesNotContain("Context: Member", applicable);
        Assert.DoesNotContain("Context: Instruction", applicable);
        Assert.DoesNotContain("Context: Exception", applicable);
        Assert.DoesNotContain("Context: Callsite", applicable);
        Assert.DoesNotContain("Context: Return Address", applicable);
        Assert.DoesNotContain("Context: Source Location", renderable);
        Assert.DoesNotContain("Context: Member", renderable);
        Assert.DoesNotContain("Context: Instruction", renderable);
        Assert.DoesNotContain("Context: Exception", renderable);
        Assert.DoesNotContain("Context: Callsite", renderable);
        Assert.DoesNotContain("Context: Return Address", renderable);

        model.ILOffset = new ILOffsetProjection
        {
            MemberContext = new ILOffsetMemberContext(),
            InstructionContext = new ILOffsetInstructionContext(),
            ExceptionContext = [new ILOffsetExceptionContext()],
            CallsiteContext = new ILOffsetCallsiteContext(),
            ReturnAddressContext = new ILOffsetReturnAddressContext()
        };

        applicable = pipeline.GetApplicableSections(model);
        renderable = pipeline.GetAvailableSections(model);

        Assert.Contains("Context: Source Location", applicable);
        Assert.Contains("Context: Member", applicable);
        Assert.Contains("Context: Instruction", applicable);
        Assert.Contains("Context: Exception", applicable);
        Assert.Contains("Context: Callsite", applicable);
        Assert.Contains("Context: Return Address", applicable);
        Assert.Contains("Context: Source Location", renderable);
        Assert.Contains("Context: Member", renderable);
        Assert.Contains("Context: Instruction", renderable);
        Assert.Contains("Context: Exception", renderable);
        Assert.Contains("Context: Callsite", renderable);
        Assert.Contains("Context: Return Address", renderable);
    }

    [Fact]
    public void LibrarySourcePlan_SourceIntegrityAuthorizedOnlyByExplicitSelection()
    {
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SourceLink: Integrity" };

        Assert.False(LibrarySourcePlans.For(Verbosity.Detailed, null).RunIntegrity);
        Assert.True(LibrarySourcePlans.For(Verbosity.Normal, include).RunIntegrity);
    }

    [Fact]
    public void LibrarySourcePlan_PdbDownloadAuthorizedByDetailedOrInclude()
    {
        Assert.False(LibrarySourcePlans.For(Verbosity.Normal, null).AllowPdbDownload);
        Assert.True(LibrarySourcePlans.For(Verbosity.Detailed, null).AllowPdbDownload);
        Assert.True(LibrarySourcePlans.For(
            Verbosity.Normal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" })
            .AllowPdbDownload);
    }

    [Fact]
    public void LibrarySourcePlan_SourceAuditAuthorizedByExplicitSourceLinkSections()
    {
        // The HEAD availability audit is now consumed only by the explicit Source Link audit
        // sections; Signals no longer carries a network-dependent availability row, so it does not
        // trigger the audit at any verbosity.
        var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" };
        var availability = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "SourceLink: Availability" };

        Assert.False(LibrarySourcePlans.For(Verbosity.Normal, null).RunHeadAudit);
        Assert.False(LibrarySourcePlans.For(Verbosity.Detailed, null).RunHeadAudit);
        Assert.False(LibrarySourcePlans.For(Verbosity.Normal, signals).RunHeadAudit);
        Assert.True(LibrarySourcePlans.For(Verbosity.Normal, availability).RunHeadAudit);
    }

    [Fact]
    public void LibrarySourcePlan_ReadsCachedPdbAtNormalAndAbove()
    {
        // Cache-only PDB reads are network-free, so they are authorized from Normal up (bare -S)
        // for the auto-rendered symbol sections. Explicit selection authorizes a real download
        // instead, so it does not set the cache-only flag.
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" };

        Assert.False(LibrarySourcePlans.For(Verbosity.Quiet, null).ReadCachedPdb);
        Assert.False(LibrarySourcePlans.For(Verbosity.Minimal, null).ReadCachedPdb);
        Assert.True(LibrarySourcePlans.For(Verbosity.Normal, null).ReadCachedPdb);
        Assert.True(LibrarySourcePlans.For(Verbosity.Detailed, null).ReadCachedPdb);
        Assert.False(LibrarySourcePlans.For(Verbosity.Normal, include).ReadCachedPdb);
    }

    [Fact]
    public void LibrarySourcePlan_PreservesAuthorizationForEverySelection()
    {
        string[] sourceSections =
        [
            SectionNames.ILOffset,
            "SourceLink: Files",
            "Symbols",
            "Signals",
            "SourceLink: Availability",
            "SourceLink: Missing Files",
            "SourceLink: Integrity",
        ];

        foreach (var verbosity in Enum.GetValues<Verbosity>())
        {
            for (int selection = 0; selection < 1 << sourceSections.Length; selection++)
            {
                HashSet<string>? include = selection == 0
                    ? null
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < sourceSections.Length; index++)
                {
                    if ((selection & (1 << index)) != 0)
                        include!.Add(sourceSections[index]);
                }

                var plan = LibrarySourcePlans.For(verbosity, include);
                bool expectedPdb = include is null
                    ? verbosity >= Verbosity.Detailed
                    : include.Overlaps(sourceSections);
                bool expectedAudit = include is not null
                    && include.Overlaps(
                        ["SourceLink: Availability", "SourceLink: Missing Files"]);
                bool expectedIntegrity = include?.Contains("SourceLink: Integrity") == true;

                Assert.Equal(
                    expectedPdb,
                    plan.AllowPdbDownload);
                Assert.Equal(expectedAudit, plan.RunHeadAudit);
                Assert.Equal(expectedIntegrity, plan.RunIntegrity);
                Assert.Equal(
                    include?.Contains("SourceLink: Files") == true,
                    plan.CollectSourceFiles);
            }
        }
    }

    [Fact]
    public void LibrarySourcePlans_HaveUniqueNamesAndValidModes()
    {
        HashSet<string> sectionNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (var section in LibrarySourcePlans.Sections)
        {
            Assert.True(sectionNames.Add(section.Name));
            Assert.NotEqual(LibrarySourcePlanModes.None, section.Modes);
            Assert.True(section.DownloadPdb);
        }
    }

    [Fact]
    public void LibraryPipeline_SignalsDoesNotShowAtMinimal()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            AuditSignals = [new AuditSignal("Provenance", "SourceLink", "Present", "test")]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Library Info", effective);
        Assert.DoesNotContain("Signals", effective);
    }

    [Fact]
    public void LibraryPipeline_QuietShowsNoSections()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Quiet);

        // Quiet renders hero line via view model, pipeline returns no sections
        Assert.Empty(effective);
    }

    [Fact]
    public void LibraryPipeline_CustomAttributesRequiresNormalWhenSelected()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "Custom Attributes" });

        Assert.Equal(Verbosity.Normal, verbosity);
    }

    [Fact]
    public void LibraryPipeline_SourceLinkAvailabilityRequiresDetailed()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var verbosity = pipeline.GetRequiredVerbosity(new HashSet<string> { "SourceLink: Availability" });

        Assert.Equal(Verbosity.Detailed, verbosity);
    }

    // ===== Scanner tests =====

    [Fact]
    public void GetRequiredScanners_MinimalVerbosity_ReturnsPrimaryScanners()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal);

        // NormalSection (index 1) is within primary threshold, has ScannerKey
        Assert.Single(scanners);
        Assert.Contains("NormalScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_NormalVerbosity_ReturnsNormalScanner()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Normal);

        Assert.Single(scanners);
        Assert.Contains("NormalScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_DetailedVerbosity_ReturnsBothScanners()
    {
        var pipeline = CreateTestPipeline();

        var scanners = pipeline.GetRequiredScanners(Verbosity.Detailed);

        Assert.Equal(2, scanners.Count);
        Assert.Contains("NormalScanner", scanners);
        Assert.Contains("DetailedScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_IncludeOverridesVerbosity()
    {
        var pipeline = CreateTestPipeline();
        var include = new HashSet<string> { "Detailed" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Single(scanners);
        Assert.Contains("DetailedScanner", scanners);
    }

    [Fact]
    public void GetRequiredScanners_NullScannerKeyExcluded()
    {
        var pipeline = CreateTestPipeline();
        var include = new HashSet<string> { "Always" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Empty(scanners);
    }

    [Fact]
    public void LibraryPipeline_UnsafeMembers_UsesOwnScanner()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Unsafe Members", "P/Invoke Methods" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Equal(2, scanners.Count);
        Assert.Contains(LibrarySections.ScannerUnsafeMembers, scanners);
        Assert.Contains(LibrarySections.ScannerClassifiedMethods, scanners);
    }

    /// <summary>
    /// Non-vacuity gate for the <see cref="SectionPipeline{TModel}.AddCategory"/> membership
    /// validation. Category membership is declared by name, so without this check a rename that
    /// updated a descriptor but missed a membership list would silently drop the section out of
    /// its category rather than fail. This test is what proves that validation is still wired.
    /// </summary>
    [Fact]
    public void SectionPipeline_AddCategory_WithUnregisteredSectionName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LibrarySections.CreatePipeline().AddCategory("@Bogus", "No Such Section"));

        Assert.Contains("No Such Section", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-vacuity gate for the Cost/@All consistency check in
    /// <see cref="SectionPipeline{TModel}.Add(SectionEntry{TModel})"/>. Membership in the
    /// <c>@All</c> pole is computed from <c>IsExpensive</c>/<c>ExplicitOnly</c>, not from
    /// <c>Cost</c>, so the two axes could otherwise drift and let a section costing unbounded
    /// work be rendered by <c>-S @All</c>. This test is what proves that check is still wired.
    /// </summary>
    [Fact]
    public void SectionPipeline_Add_UnboundedCostSectionThatWouldJoinAll_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new SectionPipeline<InspectionResult>().Add(new SectionEntry<InspectionResult>
            {
                Name = "Bogus Unbounded",
                IsExpensive = false,
                ExplicitOnly = false,
                Cost = SectionCost.Unbounded,
                ScannerKey = null,
                HasExplicitApplicability = true,
                IsApplicable = static _ => true,
                CanRender = static _ => true,
            }));

        Assert.Contains("Bogus Unbounded", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Cost=Unbounded", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every declared category member must resolve to a registered section in every pipeline that
    /// declares categories. Complements the constructor-time check by covering pipelines this
    /// suite would not otherwise build.
    /// </summary>
    [Fact]
    public void AllPipelines_CategoryMembers_ResolveToRegisteredSections()
    {
        var pipelines = new (string Name, string[] All, IReadOnlyDictionary<string, string[]> Categories)[]
        {
            ("library", LibrarySections.CreatePipeline().AllSectionNames,
                LibrarySections.CreatePipeline().GetCategoryMap()),
            ("api-type", ApiTypeSectionDescriptors.CreatePipeline().AllSectionNames,
                ApiTypeSectionDescriptors.CreatePipeline().GetCategoryMap()),
            ("api-member", ApiMemberSectionDescriptors.CreatePipeline().AllSectionNames,
                ApiMemberSectionDescriptors.CreatePipeline().GetCategoryMap()),
            ("api-member-detail", ApiMemberDetailSectionDescriptors.CreatePipeline().AllSectionNames,
                ApiMemberDetailSectionDescriptors.CreatePipeline().GetCategoryMap()),
            ("api-member-overload", ApiMemberOverloadSectionDescriptors.CreatePipeline().AllSectionNames,
                ApiMemberOverloadSectionDescriptors.CreatePipeline().GetCategoryMap()),
        };

        foreach (var (name, all, categories) in pipelines)
        {
            var known = all.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, members) in categories)
                foreach (var member in members)
                    Assert.True(known.Contains(member),
                        $"{name}: category {category} lists unregistered section '{member}'.");
        }
    }

    [Fact]
    public void LibraryPipeline_AuditCategory_MapsToAuditWorkflowSections()
    {
        var categories = LibrarySections.CreatePipeline().GetCategoryMap();

        Assert.True(categories.TryGetValue(SectionCategoryNames.Audit, out var sections));
        Assert.Equal(
            [
                SectionNames.UnsafeMembers,
                SectionNames.PInvokeMethods,
                SectionNames.NonNormalizedPaths,
                SectionNames.Signals,
                SectionNames.Symbols
            ],
            sections);
    }

    /// <summary>
    /// The whole <c>SourceLink:</c> prefix family is reachable through the <c>@SourceLink</c> door.
    /// A prefix advertises membership, so a prefixed section outside its own category is a
    /// discoverability hole — this pins the family and the door together.
    /// </summary>
    [Fact]
    public void LibraryPipeline_SourceLinkCategory_ContainsEverySourceLinkPrefixedSection()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var categories = pipeline.GetCategoryMap();

        Assert.True(categories.TryGetValue(SectionCategoryNames.SourceLink, out var sections));
        Assert.Equal(
            pipeline.AllSectionNames
                .Where(n => n.StartsWith("SourceLink:", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal),
            sections.OrderBy(n => n, StringComparer.Ordinal));
    }

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
    [Fact]
    public void PackageFilesCategory_ContainsEverySectionNamedAsAFileListing()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categories = pipeline.GetCategoryMap();

        Assert.True(categories.TryGetValue(SectionCategoryNames.Files, out var sections));
        Assert.Equal(
            PackageFileFamily.SectionNames.OrderBy(n => n, StringComparer.Ordinal),
            sections.OrderBy(n => n, StringComparer.Ordinal));

        var namedAsFiles = pipeline.AllSectionNames
            .Where(n => n.EndsWith(" file", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(" files", StringComparison.OrdinalIgnoreCase))
            // A "Group: Leaf" prefix claims the section for that group's door instead:
            // "SourceLink: Files" is SourceLink data, not a package file listing.
            .Where(n => !n.Contains(':'))
            .ToArray();
        Assert.NotEmpty(namedAsFiles);

        var expected = namedAsFiles
            .Where(n => !n.Equals(PackageSections.Files, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(expected, sections.OrderBy(n => n, StringComparer.Ordinal));

        // The superset is named like the family but is deliberately outside the door.
        Assert.Contains(PackageSections.Files, namedAsFiles);
        Assert.DoesNotContain(PackageSections.Files, sections);
    }

    /// <summary>
    /// Every family member declares a predicate, and every predicate reaches a registered section.
    /// This is what lets the view, the descriptors, and the command all read membership from one
    /// place instead of keeping three copies of the same path rules in sync.
    /// </summary>
    [Fact]
    public void PackageFileFamily_Members_AreAllRegisteredSections()
    {
        var all = PackageSectionDescriptors.CreatePipeline().AllSectionNames.ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(PackageFileFamily.Members);
        foreach (var (section, predicate) in PackageFileFamily.Members)
        {
            Assert.Contains(section, all);
            Assert.NotNull(predicate);
            Assert.Same(predicate, PackageFileFamily.PredicateFor(section));
            Assert.True(PackageFileFamily.IsFamilySection(section));
        }

        // Plain "Files" is deliberately outside the family: it is the unfiltered superset.
        Assert.False(PackageFileFamily.IsFamilySection(PackageSections.Files));
    }

    /// <summary>
    /// The package and library commands surface the same SourceLink data from the same
    /// collector, so they must spell it the same way. This pins the agreement: renaming one
    /// side without the other fails here rather than silently reintroducing the split where
    /// package called it "Source Files" and library called it "SourceLink: Files".
    /// </summary>
    [Fact]
    public void PackageAndLibraryPipelines_AgreeOnTheSourceLinkFilesSectionName()
    {
        Assert.Equal(SectionNames.SourceLinkFiles, PackageSections.SourceLinkFiles);

        var package = PackageSectionDescriptors.CreatePipeline();
        Assert.Contains(SectionNames.SourceLinkFiles, package.AllSectionNames);

        // The prefix advertises a door, so the package command has to root it too.
        var categories = package.GetCategoryMap();
        Assert.True(categories.TryGetValue(SectionCategoryNames.SourceLink, out var sections));
        Assert.Equal(
            package.AllSectionNames
                .Where(n => n.StartsWith("SourceLink:", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal),
            sections.OrderBy(n => n, StringComparer.Ordinal));
    }

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
    [Fact]
    public void PackageFileFamily_EveryMember_ProducesRowsThroughTheView()
    {
        var model = new InspectionResult
        {
            PackageName = "Test",
            PackageFiles =
            [
                new PackageFile("lib/net8.0/Test.dll", 1),
                new PackageFile("ref/net8.0/Test.dll", 1),
                new PackageFile("runtimes/win-x64/native/test.txt", 1),
                new PackageFile("README.md", 1, IsReadme: true),
                new PackageFile("Test.nuspec", 1),
                new PackageFile("skills/demo/SKILL.md", 1),
                new PackageFile("skills/demo/SKILL.md", 1)
            ]
        };
        model.Files = model.PackageFiles;

        var view = new InspectionResultView(model);
        var properties = typeof(InspectionResultView).GetProperties();

        foreach (var section in PackageFileFamily.SectionNames)
        {
            var property = properties.SingleOrDefault(p =>
                p.GetCustomAttributesData().Any(a =>
                    a.AttributeType.Name == nameof(MarkoutSectionAttribute)
                    && a.NamedArguments.Any(n =>
                        n.MemberName == nameof(MarkoutSectionAttribute.Name)
                        && (string?)n.TypedValue.Value == section)));

            Assert.True(property != null, $"No view projection is attributed for section '{section}'.");

            var rows = property!.GetValue(view) as System.Collections.IEnumerable;
            Assert.True(rows != null, $"Section '{section}' projected null rows.");
            Assert.True(
                rows!.Cast<object>().Any(),
                $"Section '{section}' produced no rows for a model that contains a matching file.");
        }
    }

    [Fact]
    public void LibraryPipeline_IntegrationsCategory_ExcludesUnionTypes()
    {
        var categories = LibrarySections.CreatePipeline().GetCategoryMap();

        Assert.True(categories.TryGetValue(SectionCategoryNames.Integrations, out var sections));
        Assert.DoesNotContain("Union Types", sections);
    }

    [Fact]
    public void LibraryPipeline_TargetedSection_OnlyRequiredScanner()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Custom Attributes" };

        var scanners = pipeline.GetRequiredScanners(Verbosity.Minimal, include);

        Assert.Single(scanners);
        Assert.Contains(LibrarySections.ScannerCustomAttributes, scanners);
    }

    // ===== Scanner registry tests =====

    [Fact]
    public void ScannerRegistry_RunsOnlyRequestedScanners()
    {
        var ran = new HashSet<string>();
        var registry = new ScannerRegistry()
            .Add("A", _ => ran.Add("A"))
            .Add("B", _ => ran.Add("B"))
            .Add("C", _ => ran.Add("C"));

        registry.RunScanners(["A", "C"], new ScannerContext
        {
            AssemblyPath = "test.dll",
            Model = new LibraryInspection(),
            Logger = new DotnetInspector.Output.VerboseLogger(false),
        });

        Assert.Equal(2, ran.Count);
        Assert.Contains("A", ran);
        Assert.Contains("C", ran);
        Assert.DoesNotContain("B", ran);
    }

    [Fact]
    public void ScannerRegistry_EmptySet_RunsNothing()
    {
        var ran = false;
        var registry = new ScannerRegistry()
            .Add("A", _ => ran = true);

        registry.RunScanners([], new ScannerContext
        {
            AssemblyPath = "test.dll",
            Model = new LibraryInspection(),
            Logger = new DotnetInspector.Output.VerboseLogger(false),
        });

        Assert.False(ran);
    }

    [Fact]
    public void LibraryScannerRegistry_RegistrationMatchesDeclaration()
    {
        // Set equality, not containment, so both failure directions are caught: a section
        // declaring a key nobody registered (its data silently never collected) and a registered
        // scanner no section asks for (dead code). Derived from the pipeline and the registry
        // rather than restated as a literal list, so adding a section or a scanner cannot drift
        // past this test.
        var registry = LibrarySections.CreateScannerRegistry();
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(
            pipeline.DeclaredScannerKeys.OrderBy(k => k, StringComparer.Ordinal),
            registry.RegisteredKeys.OrderBy(k => k, StringComparer.Ordinal));
    }

    // ===== Scanner prerequisite tests =====

    [Fact]
    public void RunScanners_RunsPrerequisitesFirstAndEachScannerOnce()
    {
        // The property that replaced the fan-out: a scanner declares what it reads, and the
        // registry runs that prerequisite before it and exactly once for the whole run, however
        // many other scanners also require it. Without this, deduping is impossible and a
        // scanner has to defensively re-scan.
        List<string> order = [];
        var registry = new ScannerRegistry()
            .Add("leaf", _ => order.Add("leaf"))
            .Add("mid", _ => order.Add("mid"), "leaf")
            .Add("top", _ => order.Add("top"), "mid", "leaf");

        registry.RunScanners(["top"], NullScannerContext());

        Assert.Equal(["leaf", "mid", "top"], order);
    }

    [Fact]
    public void RunScanners_SharedPrerequisiteRunsOnceAcrossRequestedScanners()
    {
        List<string> order = [];
        var registry = new ScannerRegistry()
            .Add("leaf", _ => order.Add("leaf"))
            .Add("a", _ => order.Add("a"), "leaf")
            .Add("b", _ => order.Add("b"), "leaf");

        registry.RunScanners(["a", "b"], NullScannerContext());

        Assert.Equal(["leaf", "a", "b"], order);
    }

    [Fact]
    public void AddBundle_RunsItsPrerequisitesAndNoWorkOfItsOwn()
    {
        // A bundle exists only because ISectionDescriptor.ScannerKey names a single key, so a
        // section fed by several scanners needs one key that stands for all of them.
        List<string> order = [];
        var registry = new ScannerRegistry()
            .Add("a", _ => order.Add("a"))
            .Add("b", _ => order.Add("b"))
            .AddBundle("bundle", "a", "b");

        registry.RunScanners(["bundle"], NullScannerContext());

        Assert.Equal(["a", "b"], order);
    }

    [Fact]
    public void ExpandRequired_IncludesTransitivePrerequisites()
    {
        // Callers that reason about the work a run will do — body-analysis feature selection in
        // particular — must see prerequisites, or they narrow away work the run still performs.
        var registry = new ScannerRegistry()
            .Add("leaf", _ => { })
            .Add("mid", _ => { }, "leaf")
            .Add("top", _ => { }, "mid");

        Assert.Equal(
            ["leaf", "mid", "top"],
            registry.ExpandRequired(["top"]).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void ExpandRequired_ThrowsOnUnregisteredPrerequisite()
    {
        // A prerequisite naming a scanner that does not exist is a typo or a stale rename, and it
        // silently drops a dependency: the scanner runs without the data it declared it needs and
        // produces output that looks correct. Requested keys are different -- callers derive those
        // from descriptors across registries and an unknown one is skipped on purpose -- so only
        // the prerequisite edge is validated here.
        var registry = new ScannerRegistry()
            .Add("a", _ => { }, "typo");

        var expand = Assert.Throws<InvalidOperationException>(
            () => registry.ExpandRequired(["a"]));
        Assert.Contains("typo", expand.Message, StringComparison.Ordinal);

        // RunScanners is reachable without expanding first, so it enforces the same rule.
        var run = Assert.Throws<InvalidOperationException>(
            () => registry.RunScanners(["a"], NullScannerContext()));
        Assert.Contains("typo", run.Message, StringComparison.Ordinal);

        // Non-vacuity: an unregistered key that was merely REQUESTED must still be skipped, or
        // this test would be passing for the wrong reason.
        var ran = false;
        var tolerant = new ScannerRegistry().Add("a", _ => ran = true);
        tolerant.RunScanners(["a", "not-registered"], NullScannerContext());
        Assert.True(ran);
    }

    [Fact]
    public void RunScanners_ThrowsOnPrerequisiteCycle()
    {
        var registry = new ScannerRegistry()
            .Add("a", _ => { }, "b")
            .Add("b", _ => { }, "a");

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.RunScanners(["a"], NullScannerContext()));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandRequired_ThrowsOnPrerequisiteCycle()
    {
        // Regression: ExpandRequired used to short-circuit on an already-added key, so a cycle
        // terminated quietly and returned a plausible closure. That made the acyclicity half of
        // LibraryScannerPrerequisites_AreAllRegisteredAndAcyclic vacuous.
        var registry = new ScannerRegistry()
            .Add("a", _ => { }, "b")
            .Add("b", _ => { }, "a");

        var ex = Assert.Throws<InvalidOperationException>(() => registry.ExpandRequired(["a"]));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandRequired_AllowsDiamondPrerequisites()
    {
        // A shared prerequisite reached by two paths is not a cycle. Guards against a cycle check
        // that keys off "already seen" rather than "currently being visited".
        var registry = new ScannerRegistry()
            .Add("d", _ => { })
            .Add("b", _ => { }, "d")
            .Add("c", _ => { }, "d")
            .Add("a", _ => { }, "b", "c");

        Assert.Equal(
            ["a", "b", "c", "d"],
            registry.ExpandRequired(["a"]).OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryScannerPrerequisites_AreAllRegisteredAndAcyclic()
    {
        // Derived from the registry rather than restated, so a new prerequisite naming a key that
        // does not exist fails here instead of silently never running. RunScanners skips an
        // unregistered prerequisite, so nothing else would notice.
        var registry = LibrarySections.CreateScannerRegistry();
        var registered = registry.RegisteredKeys.ToHashSet(StringComparer.Ordinal);

        foreach (var key in registered)
        {
            foreach (var required in registry.RequirementsOf(key))
                Assert.Contains(required, registered);
        }

        // ExpandRequired throws on a cycle, so this both closes the graph and proves it acyclic.
        // It used to short-circuit instead, which made the acyclicity claim vacuous; see
        // ExpandRequired_ThrowsOnPrerequisiteCycle.
        Assert.Equal(registered, registry.ExpandRequired(registered));
    }

    [Fact]
    public void IntegrationOpportunities_DeclaresIntegrationsPrerequisite()
    {
        // This pins the declaration, not the behavior, and that is a deliberate weakening.
        // LibrarySections_RenderIdenticallyAloneAndTogether covers every other prerequisite
        // behaviorally, but it cannot cover this one.
        //
        // Not because the section never renders offline — it does; System.Data.Common yields two
        // rows (DbDataSource under Aspire and Health Checks). It is because the failure mode is
        // EXTRA rows rather than missing ones: without Integrations the existing-integration set
        // is empty, so already-integrated categories stop being suppressed. Distinguishing the
        // two therefore needs an assembly that both renders opportunities AND carries an existing
        // integration in one of those same categories, so that dropping the prerequisite makes a
        // suppressed row reappear. No assembly available offline does both.
        //
        // Closing the gap properly needs a purpose-built fixture with that combination. Until
        // then this catches the realistic failure — someone deleting the declaration — and
        // nothing more.
        var registry = LibrarySections.CreateScannerRegistry();

        Assert.Contains(
            LibrarySections.ScannerIntegrations,
            registry.RequirementsOf(LibrarySections.ScannerIntegrationOpportunities));
    }

    [Fact]
    public void SharedSessionScanners_AllObserveOneSession()
    {
        // Named by ScannerContext.SharedScanCount as the gate for its atomicity claim.
        //
        // Each of the three fan-out sites this change deleted held its callees inside ONE open, so
        // a run could not mix two assemblies. Prerequisites restore the ordering but not, by
        // themselves, the single open: a registration that calls the path overload reopens the
        // file, and retargeting the path between opens (symlink swap, or a build replacing the
        // file) then yields an incoherent result with exit code 0.
        //
        // That regression is invisible to every other test — the output still looks correct — so
        // it needs its own gate. The set below is pinned rather than derived because the property
        // is historical: it is exactly what the deleted fan-out covered. Reverting any one of
        // these registrations to LibraryMetadataService.ScanX(ctx.AssemblyPath, ...) drops the
        // count and fails here.
        //
        // What this does NOT do is simulate a concurrent retarget. AssemblyImage.Open uses
        // File.OpenRead (FileShare.Read), so a live session blocks delete and rename on Windows,
        // and directory symlinks need Developer Mode. Routing through the shared session is the
        // observable that stands in for it.
        string[] sharedSessionScanners =
        [
            // was ScanInfoCounts's five-way fan-out
            LibrarySections.ScannerExtensionMethods,
            LibrarySections.ScannerClassifiedMethods,
            LibrarySections.ScannerResources,
            LibrarySections.ScannerCustomAttributes,
            LibrarySections.ScannerTypeForwarders,
            // was ScanIntegrationOpportunities re-running ScanIntegrations
            LibrarySections.ScannerIntegrations,
            LibrarySections.ScannerIntegrationOpportunities,
            // was PopulateLibraryAudit running ScanClassifiedMethods on its own session
            LibrarySections.ScannerAuditSignals,
        ];

        var registry = LibrarySections.CreateScannerRegistry();
        using var context = new ScannerContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        registry.RunScanners(registry.ExpandRequired(sharedSessionScanners), context);

        Assert.Equal(sharedSessionScanners.Length, context.SharedScanCount);
        Assert.NotNull(context.Session());
    }

    [Fact]
    public void SharedSession_FallsBackToReopenWhenAssemblyCannotBeOpened()
    {
        // The shared session returns null rather than throwing so each scanner keeps its own
        // open-failure mapping. Without this, SharedSessionScanners_AllObserveOneSession could be
        // satisfied by a Session() that throws, and an unopenable assembly would surface as one
        // generic failure instead of a typed failed inspection per scanner.
        var registry = LibrarySections.CreateScannerRegistry();
        var model = new LibraryInspection();
        using var context = new ScannerContext
        {
            AssemblyPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"),
            Model = model,
            Logger = new Output.VerboseLogger(false),
        };

        registry.RunScanners([LibrarySections.ScannerResources], context);

        Assert.Null(context.Session());
        Assert.Equal(0, context.SharedScanCount);
        Assert.NotNull(model.ResourceInspection);
        Assert.IsType<FindingInspection<ManifestResourceInfo>.Failed>(model.ResourceInspection!.Value);
    }

    [Fact]
    public void Trace_RecordsWhatRan_AndMarksBundlesAsDoingNoWorkOfTheirOwn()
    {
        // InfoCounts is a bundle: it does no work itself and exists only to pull in five scanners.
        // A trace that reported it as an ordinary scanner would attribute the bundle's dispatch
        // cost to a step that has none, and hide that the real work belongs to its prerequisites.
        var registry = LibrarySections.CreateScannerRegistry();
        var trace = new InspectionTrace();
        using var context = new ScannerContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            Trace = trace,
        };

        var closure = registry.ExpandRequired([LibrarySections.ScannerInfoCounts]);
        registry.RunScanners(closure, context);

        Assert.Equal(
            closure.OrderBy(k => k, StringComparer.Ordinal),
            trace.Executions.Select(e => e.Key).OrderBy(k => k, StringComparer.Ordinal));

        var bundles = trace.Executions.Where(e => e.IsBundle).Select(e => e.Key).ToArray();
        Assert.Equal([LibrarySections.ScannerInfoCounts], bundles);
    }

    [Fact]
    public void Trace_SeparatesDirectDemandFromPrerequisiteExpansion()
    {
        // The distinction is the point of the report: a key in the closure but not in the request
        // is work no section asked for by name, which is where an unintended cost creeps in.
        var pipeline = LibrarySections.CreatePipeline();
        var registry = LibrarySections.CreateScannerRegistry();
        var trace = new InspectionTrace();

        var requested = pipeline.GetRequiredScanners(Verbosity.Minimal, trace: trace);
        trace.RecordClosure(registry.ExpandRequired(requested));

        // Minimal selects the target section only, and it demands exactly the bundle.
        Assert.Equal([LibrarySections.ScannerInfoCounts], trace.Requested);
        Assert.All(trace.Demand, d => Assert.Equal(LibrarySections.ScannerInfoCounts, d.Scanner));

        // Everything the bundle names is expansion, not demand.
        var added = trace.Closure.Except(trace.Requested, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            registry.RequirementsOf(LibrarySections.ScannerInfoCounts).ToHashSet(StringComparer.Ordinal),
            added);
    }

    [Fact]
    public void Trace_ExplainsEveryScannerThatRan()
    {
        // The report attributes each scanner to one of three mechanisms: a section named it, the
        // command named it, or a declared prerequisite pulled it in. That attribution is the report's
        // entire value, and it is the part with no other check on it -- a wrong bucket still renders
        // a plausible-looking report and sends whoever chases an unexpected scan to a declaration
        // that does not exist. Discovery mode's Metadata scan was exactly that bug.
        //
        // The asymmetry is what makes this a gate rather than a restatement: the closure comes from
        // what the run actually *did* (ExpandRequired over the returned set), while reachability is
        // seeded from what the trace *claims* (recorded section and command demands). Seeding from
        // trace.Requested instead would re-derive ExpandRequired's own input and assert X is a subset
        // of X -- which an earlier version of this test did, and which stayed green under tampering.
        var registry = LibrarySections.CreateScannerRegistry();
        (string, string)[] discoveryDemand = [("discovery catalog", LibrarySections.ScannerMetadata)];

        foreach (var commandDemand in new[] { null, discoveryDemand })
        {
            var pipeline = LibrarySections.CreatePipeline();
            var trace = new InspectionTrace();
            var requested = pipeline.GetRequiredScanners(
                Verbosity.Detailed, trace: trace, commandDemand: commandDemand);

            trace.RecordClosure(registry.ExpandRequired(requested));

            var claimed = trace.Demand.Select(d => d.Scanner)
                .Concat(trace.CommandDemand.Select(c => c.Scanner))
                .ToHashSet(StringComparer.Ordinal);

            var reachable = new HashSet<string>(claimed, StringComparer.Ordinal);
            var queue = new Queue<string>(claimed);
            while (queue.Count > 0)
            {
                foreach (var requirement in registry.RequirementsOf(queue.Dequeue()))
                {
                    if (reachable.Add(requirement))
                        queue.Enqueue(requirement);
                }
            }

            Assert.Empty(trace.Closure.Except(reachable, StringComparer.Ordinal));
        }

        // Non-vacuity: the discovery case has to actually pull in a scanner no section named, or the
        // second iteration proves nothing the first did not.
        var plain = LibrarySections.CreatePipeline().GetRequiredScanners(Verbosity.Detailed);
        var withDiscovery = LibrarySections.CreatePipeline()
            .GetRequiredScanners(Verbosity.Detailed, commandDemand: discoveryDemand);
        Assert.Equal([LibrarySections.ScannerMetadata], withDiscovery.Except(plain, StringComparer.Ordinal));
    }

    [Fact]
    public void Trace_RecordsNoBodyIndexForAScanThatDoesNotNeedOne()
    {
        // The negative half of the minimum-work claim, and the one worth gating. A regression that
        // makes a metadata-only scan open the whole-assembly IL index costs seconds and changes no
        // output at all, so no other test in the suite would notice. Its absence from the resource
        // list is the observable.
        var registry = LibrarySections.CreateScannerRegistry();
        var trace = new InspectionTrace();
        using var metadataContext = PdbContext.Open(typeof(SectionPipelineTests).Assembly.Location);
        using var context = new ScannerContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
            Trace = trace,
        };

        registry.RunScanners(registry.ExpandRequired([LibrarySections.ScannerInfoCounts]), context);

        Assert.Contains(trace.Resources, r => r.Resource == "metadata session");
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "body index");
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "drill map");
    }

    [Fact]
    public void Trace_RecordsTheBodyIndexWhenAScannerActuallyBuildsIt()
    {
        // Paired positive. Without it the negative above is satisfied by a trace that never
        // records a body index under any circumstances, which would pass while observing nothing.
        // The index needs the prefetched image the command opens for exactly this reason; a plain
        // PdbContext.Open cannot back it, and the scanner would swallow the failure and render an
        // empty section. Opening it the way InspectAsync does is what makes this a real positive.
        var registry = LibrarySections.CreateScannerRegistry();
        var trace = new InspectionTrace();
        using var service = SourceLinkService.OpenPrefetched(
            typeof(SectionPipelineTests).Assembly.Location,
            _ => { });
        using var context = new ScannerContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = service.Context,
            Trace = trace,
        };

        registry.RunScanners(registry.ExpandRequired([LibrarySections.ScannerUnsafeMembers]), context);

        var bodyIndex = Assert.Single(trace.Resources, r => r.Resource == "body index");
        Assert.StartsWith("built in", bodyIndex.Detail);
    }

    [Fact]
    public void Trace_RecordsAScannerThatThrew()
    {
        // The report is written in a finally, so a run that failed still says what it had done by
        // the time it failed. If the throwing scanner were dropped from the record, the trace would
        // implicate whichever scanner ran last before it.
        var registry = new ScannerRegistry()
            .Add("Boom", _ => throw new InvalidOperationException("boom"));
        var trace = new InspectionTrace();
        using var context = new ScannerContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            Trace = trace,
        };

        Assert.Throws<InvalidOperationException>(() => registry.RunScanners(["Boom"], context));

        Assert.Equal(["Boom"], trace.Executions.Select(e => e.Key));
    }

    [Fact]
    public void Tracing_DoesNotChangeTheWorkTheRunDoes()
    {
        // A diagnostic that perturbs what it measures is worse than none. Held against the shared
        // scan count, which is the observable the atomicity gates already rely on.
        static int RunAndCountSharedScans(InspectionTrace? trace)
        {
            var registry = LibrarySections.CreateScannerRegistry();
            using var context = new ScannerContext
            {
                AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
                Model = new LibraryInspection(),
                Logger = new Output.VerboseLogger(false),
                Trace = trace,
            };

            registry.RunScanners(registry.ExpandRequired(SharedSessionScannerKeys), context);
            return context.SharedScanCount;
        }

        Assert.Equal(RunAndCountSharedScans(trace: null), RunAndCountSharedScans(new InspectionTrace()));
    }

    [Fact]
    public void SharedSessionScanners_MapTheirOwnFailuresRatherThanThrowing()
    {
        // Routing a scanner through the shared session means it runs its SESSION overload where it
        // used to run its PATH overload. The path overloads all wrap their work in try/catch and
        // produce a typed per-scanner failure; the session overloads have to do the same, or a
        // single scanner fault escapes RunScanners into InspectAsync's broad catch and the whole
        // command degrades to one generic "Could not read library".
        //
        // ScanIntegrationOpportunities' session overload did NOT catch, so the shared-session
        // change silently dropped that mapping for it. This gate is why that was found.
        //
        // A disposed session is the fault injector: AssemblyImage.EnsureAlive throws
        // ObjectDisposedException on every facet, so it faults each scanner at the point where it
        // touches metadata, deterministically and on every platform.
        var session = AssemblyInspectionSession.Open(typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();

        var logger = new Output.VerboseLogger(false);
        const string Path = "disposed.dll";

        // Each scanner runs against its OWN model and is asserted on the exact field it alone must
        // populate. Found by review: a single shared model let a typed failure written by an
        // earlier scanner satisfy an assertion nominally about a later one -- deleting
        // MarkIntegrationFailuresIfMissing from ScanIntegrationOpportunities' catch left this gate
        // green, because ScanIntegrations had already set the same two fields and the mapping uses
        // ??=.
        var scans = new (string Name, Action<LibraryInspection> Run, Action<LibraryInspection> Assert)[]
        {
            ("ExtensionMethods",
                m => m.Apply(LibraryMetadataService.ScanExtensionMembers(session, Path, logger)),
                m => Xunit.Assert.IsType<FindingInspection<ExtensionMemberObservation>.Failed>(
                    m.ExtensionMemberInspection!.Value)),

            ("ClassifiedMethods",
                m => m.Apply(LibraryMetadataService.ScanClassifiedMethods(session, Path, logger)),
                m => Xunit.Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(
                    m.ClassifiedMethodInspection!.Value)),

            ("Resources",
                m => m.ResourceInspection = LibraryMetadataService.ScanResources(session, Path, logger),
                m => Xunit.Assert.IsType<FindingInspection<ManifestResourceInfo>.Failed>(
                    m.ResourceInspection!.Value)),

            ("CustomAttributes",
                m => m.Apply(LibraryMetadataService.ScanCustomAttributes(session, Path, logger)),
                m => Xunit.Assert.IsType<FindingInspection<AssemblyAttributeInfo>.Failed>(
                    m.AssemblyAttributeInspection!.Value)),

            ("TypeForwarders",
                m => m.TypeForwarderInspection = LibraryMetadataService.ScanTypeForwarders(session, Path, logger),
                m => Xunit.Assert.IsType<FindingInspection<TypeForwarderInfo>.Failed>(
                    m.TypeForwarderInspection!.Value)),

            ("Integrations",
                m => LibraryMetadataService.ScanIntegrations(session, Path, m, logger),
                m =>
                {
                    Xunit.Assert.IsType<FindingInspection<OpenTelemetrySignalInfo>.Failed>(
                        m.OpenTelemetryInspection!.Value);
                    Xunit.Assert.IsType<FindingInspection<EcosystemIntegrationSignalInfo>.Failed>(
                        m.EcosystemIntegrationInspection!.Value);
                }),

            ("IntegrationOpportunities",
                m => LibraryMetadataService.ScanIntegrationOpportunities(session, Path, m, logger),
                m =>
                {
                    // Nothing else ran against this model, so these can only come from the
                    // opportunity scanner's own catch.
                    Xunit.Assert.IsType<FindingInspection<OpenTelemetrySignalInfo>.Failed>(
                        m.OpenTelemetryInspection!.Value);
                    Xunit.Assert.IsType<FindingInspection<EcosystemIntegrationSignalInfo>.Failed>(
                        m.EcosystemIntegrationInspection!.Value);
                }),

            ("AuditSignals",
                m => AuditSignalBuilder.PopulateLibraryAudit(session, Path, m, logger),
                m =>
                {
                    // A failed audit scan must not cache metadata, or RefreshLibraryAudit would
                    // reuse a value the scan never produced instead of falling back.
                    Xunit.Assert.Null(m.AuditMetadata);
                    Xunit.Assert.NotNull(m.AuditSignals);
                }),
        };

        foreach (var (name, run, assert) in scans)
        {
            var model = new LibraryInspection();

            var ex = Record.Exception(() => run(model));
            Assert.True(ex is null, $"{name} let {ex?.GetType().Name} escape its session overload.");

            // Not just "did not throw": the fault has to be visible as a typed failure, otherwise a
            // scanner could satisfy this test by swallowing the error into success-shaped empty
            // output.
            var mapping = Record.Exception(() => assert(model));
            Assert.True(mapping is null, $"{name} did not map its own failure: {mapping?.Message}");
        }
    }

    [Fact]
    public void SharedSessionScanners_DoNotObserveAPathRetargetedMidRun()
    {
        // The actual attack, run in-process rather than described in a comment.
        //
        // A directory link points at assembly A. One scanner runs, which opens the shared session.
        // The link is then retargeted to assembly B and the remaining scanners run. Every scanner
        // must still report A: an open handle keeps reading its original target, so sharing one
        // open is what makes the run coherent. Without it each scanner reopens through the link
        // and picks up B, and the command still exits 0 with output that looks correct.
        //
        // The counter in SharedSessionScanners_AllObserveOneSession cannot see this, because
        // anything that lives inside ScannerContext.Scan can be defeated by editing Scan. This
        // test observes only scanner OUTPUT, so no edit to the plumbing can fake it.
        var pathA = typeof(SectionPipelineTests).Assembly.Location;
        var pathB = typeof(AssemblyInspectionSession).Assembly.Location;

        var root = Path.Combine(Path.GetTempPath(), $"retarget-{Guid.NewGuid():N}");
        var dirA = Path.Combine(root, "a");
        var dirB = Path.Combine(root, "b");
        var link = Path.Combine(root, "active");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        File.Copy(pathA, Path.Combine(dirA, "lib.dll"));
        File.Copy(pathB, Path.Combine(dirB, "lib.dll"));

        try
        {
            if (!TryLinkDirectory(link, dirA))
            {
                // Deliberately not Assert.Skip: a silent skip here would retire the gate. Windows
                // needs Developer Mode or admin for symbolic links; the junction fallback covers
                // the rest. If both fail the environment cannot host this test at all.
                throw new InvalidOperationException(
                    $"Could not create a directory link at '{link}'. On Windows this needs " +
                    "Developer Mode, admin, or working `mklink /J`.");
            }

            var linkedAssembly = Path.Combine(link, "lib.dll");

            // Control: what each assembly looks like when nothing moves underneath it.
            var expectedA = CensusSignature(pathA);
            var expectedB = CensusSignature(pathB);

            // Non-vacuity: if the two fixtures censused the same, the retarget could not be seen
            // and this test would pass no matter what the product did.
            Assert.NotEqual(expectedA.Full, expectedB.Full);

            // The action-based scanners must distinguish the fixtures on their own. Found by
            // review: asserting only the combined signature let the five value-returning census
            // scanners carry the whole assertion, so a tamper confined to the void Scan overload
            // -- which is how Audit Signals, Integrations and Integration Opportunities run --
            // left this gate green while three scanners reopened the path.
            Assert.NotEqual(expectedA.Actions, expectedB.Actions);

            var registry = LibrarySections.CreateScannerRegistry();
            var model = new LibraryInspection();
            using var context = new ScannerContext
            {
                AssemblyPath = linkedAssembly,
                Model = model,
                Logger = new Output.VerboseLogger(false),
            };

            // First scanner opens the shared session against A.
            registry.RunScanners([LibrarySections.ScannerExtensionMethods], context);

            Assert.True(TryLinkDirectory(link, dirB), "Could not retarget the directory link.");

            registry.RunScanners(
                registry.ExpandRequired(
                    SharedSessionScannerKeys
                        .Where(key => key != LibrarySections.ScannerExtensionMethods)),
                context);

            Assert.Equal(expectedA.Full, SignatureOf(model));
        }
        finally
        {
            // Delete the link before the tree so the target is not followed.
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SharedSessionScanners_ObserveTheImageTheCommandAlreadyOpened()
    {
        // The wider half of the same attack, and the reason the shared session borrows instead of
        // opening. A command opens the assembly once for identity, presence flags, and debug
        // directory facts, then hands that PdbContext to the scanners. If the scanner session
        // opened AssemblyPath again, everything between the two opens would be a window in which
        // the path can be retargeted, and the command would report one assembly's identity beside
        // another assembly's counts -- with a zero exit code.
        //
        // Sharing one session among the scanners does not close that window; it only moves it
        // earlier. Borrowing the already-open image removes it, because there is nothing left to
        // race: no second open of the path happens at all.
        var pathA = typeof(SectionPipelineTests).Assembly.Location;
        var pathB = typeof(AssemblyInspectionSession).Assembly.Location;

        var root = Path.Combine(Path.GetTempPath(), $"borrow-{Guid.NewGuid():N}");
        var dirA = Path.Combine(root, "a");
        var dirB = Path.Combine(root, "b");
        var link = Path.Combine(root, "active");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        File.Copy(pathA, Path.Combine(dirA, "lib.dll"));
        File.Copy(pathB, Path.Combine(dirB, "lib.dll"));

        try
        {
            if (!TryLinkDirectory(link, dirA))
            {
                throw new InvalidOperationException(
                    $"Could not create a directory link at '{link}'. On Windows this needs " +
                    "Developer Mode, admin, or working `mklink /J`.");
            }

            var linkedAssembly = Path.Combine(link, "lib.dll");

            var expectedA = CensusSignature(pathA);
            var expectedB = CensusSignature(pathB);
            Assert.NotEqual(expectedA.Full, expectedB.Full);

            // The action-based scanners must distinguish the fixtures on their own, or a tamper
            // confined to the void Scan overload would be invisible here.
            Assert.NotEqual(expectedA.Actions, expectedB.Actions);

            // Stand in for the command's own open: identity is read here, scanners run later.
            using var metadataContext = PdbContext.Open(linkedAssembly);
            var identity = metadataContext.ExtractAssemblyInfo();

            // Everything between the command's open and the scanner run is the window under test.
            Assert.True(TryLinkDirectory(link, dirB), "Could not retarget the directory link.");

            var model = new LibraryInspection();
            using var context = new ScannerContext
            {
                AssemblyPath = linkedAssembly,
                Model = model,
                Logger = new Output.VerboseLogger(false),
                MetadataContext = metadataContext,
            };

            var registry = LibrarySections.CreateScannerRegistry();
            registry.RunScanners(registry.ExpandRequired(SharedSessionScannerKeys), context);

            // Identity and counts have to describe the same assembly, not merely each be valid.
            Assert.Equal(
                Path.GetFileNameWithoutExtension(pathA),
                identity.AssemblyName);
            Assert.Equal(expectedA.Full, SignatureOf(model));
        }
        finally
        {
            if (Directory.Exists(link))
                Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BorrowedSession_FailsLoudlyAfterTheLenderIsDisposed()
    {
        // A borrow that outlives its lender must fail with an exception a caller can map, not by
        // reading unmapped memory. The dangerous shape is a MethodBodySource obtained WHILE the
        // lender was alive: it captures the reader and its liveness check, so it survives the
        // borrow's own disposal flag being false and reads through a released handle. That is an
        // AccessViolationException, which is uncatchable and kills the process -- so if the
        // liveness check on AssemblyImage stops consulting the lender, this test does not merely
        // fail, it takes the test host down. Either way it stops the build.
        //
        // Found by review: an earlier version of this gate touched MethodBodies only AFTER
        // disposal, so the cold property threw from the disposed PEReader and the missing lender
        // check went unnoticed. Warming it first is the whole point.
        var path = typeof(SectionPipelineTests).Assembly.Location;

        foreach (var prefetched in new[] { false, true })
        {
            // SourceLinkService is how commands open an assembly, and it owns the PdbContext the
            // scanners borrow. Both open modes are covered because they map the image differently.
            var service = prefetched
                ? SourceLinkService.OpenPrefetched(path)
                : SourceLinkService.Open(path);
            var lender = service.Context;

            var borrowed = AssemblyInspectionSession.Borrow(lender);

            // Warm the body source while the lender is still alive.
            var bodies = borrowed.MethodBodies;
            Assert.NotEmpty(bodies.EnumerateMethods());

            service.Dispose();

            Assert.Throws<ObjectDisposedException>(() => bodies.EnumerateMethods());
            Assert.Throws<ObjectDisposedException>(() => borrowed.MethodBodies);

            // Borrowing from an already-disposed lender is refused rather than deferred.
            Assert.Throws<ObjectDisposedException>(() => AssemblyInspectionSession.Borrow(lender));

            borrowed.Dispose();
        }
    }

    [Fact]
    public void BorrowedSession_DoesNotDisposeTheOwningContext()
    {
        // A borrow that disposed the shared reader would break the command that lent it. The
        // opposite direction -- a borrow outliving its lender -- is
        // BorrowedSession_FailsLoudlyAfterTheLenderIsDisposed.
        var path = typeof(SectionPipelineTests).Assembly.Location;
        using var metadataContext = PdbContext.Open(path);

        var borrowed = AssemblyInspectionSession.Borrow(metadataContext);
        var attributeCount = borrowed.CustomAttributes().Count;
        borrowed.Dispose();

        // The lender is unaffected by the borrow ending.
        Assert.NotNull(metadataContext.ExtractAssemblyInfo().AssemblyName);

        using var second = AssemblyInspectionSession.Borrow(metadataContext);
        Assert.Equal(attributeCount, second.CustomAttributes().Count);
        Assert.NotEmpty(second.MethodBodies.EnumerateMethods());
    }

    /// <summary>
    /// Runs the shared-session scanners over an untouched path and returns their signature, split
    /// so a caller can assert that the action-based scanners on their own distinguish the two
    /// fixtures. Without that split, the five value-returning census scanners could carry the whole
    /// signature and a tamper confined to the void <c>Scan</c> overload would stay invisible.
    /// </summary>
    private static (string Full, string Actions) CensusSignature(string assemblyPath)
    {
        var model = new LibraryInspection();
        using var context = new ScannerContext
        {
            AssemblyPath = assemblyPath,
            Model = model,
            Logger = new Output.VerboseLogger(false),
        };

        var registry = LibrarySections.CreateScannerRegistry();
        registry.RunScanners(registry.ExpandRequired(SharedSessionScannerKeys), context);

        return (SignatureOf(model), ActionSignatureOf(model));
    }

    /// <summary>
    /// Every scanner the fan-out held inside one session. Both retarget gates drive this whole set
    /// so the three action-based scanners are covered, not just the five that return a value.
    /// </summary>
    private static readonly string[] SharedSessionScannerKeys =
    [
        LibrarySections.ScannerExtensionMethods,
        LibrarySections.ScannerClassifiedMethods,
        LibrarySections.ScannerResources,
        LibrarySections.ScannerCustomAttributes,
        LibrarySections.ScannerTypeForwarders,
        LibrarySections.ScannerIntegrations,
        LibrarySections.ScannerIntegrationOpportunities,
        LibrarySections.ScannerAuditSignals,
    ];

    private static string SignatureOf(LibraryInspection model) => string.Join(
        "|",
        $"ext={model.ExtensionMethods?.Count}",
        $"attrs={model.CustomAttributes?.Count}",
        $"classified={PayloadCount(model.ClassifiedMethodInspection)}",
        $"resources={PayloadCount(model.ResourceInspection)}",
        $"forwarders={PayloadCount(model.TypeForwarderInspection)}",
        ActionSignatureOf(model));

    /// <summary>
    /// Output of the scanners that run through the void <c>Scan</c> overload. Audit signals are
    /// compared by VALUE, not by count: the signal rows are a fixed catalog, so two different
    /// assemblies produce the same number of them and a count would make this signature identical
    /// for every input — which is exactly how the first version of this gate lost its coverage.
    /// </summary>
    private static string ActionSignatureOf(LibraryInspection model) => string.Join(
        "|",
        $"audit=[{string.Join(",", model.AuditSignals?.Select(s => $"{s.Signal}={s.Value}") ?? [])}]",
        $"otel={PayloadCount(model.OpenTelemetryInspection)}",
        $"ecosystem={PayloadCount(model.EcosystemIntegrationInspection)}",
        $"opportunities=[{string.Join(",", model.IntegrationOpportunities?.Select(o => $"{o.Integration}:{o.Api}") ?? [])}]");

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

    [Fact]
    public void AuditSignalRefresh_DoesNotReopenTheAssembly()
    {
        // GPT's finding: the Signals section was NOT protected by the shared session. InspectAsync
        // recomputes audit signals after the source-audit and integrity passes, and each recompute
        // used to call PopulateLibraryAudit(path, ...) — a fresh open, AFTER the ScannerContext was
        // disposed. So Signals could still mix two assemblies (proved out-of-process by retargeting
        // a junction during the recompute), and a healthy run opened the assembly up to four times.
        //
        // Only the model-derived half of the computation changes between recomputes, so the
        // assembly-derived half is captured once and reused. Refresh must therefore work against a
        // path that can no longer be opened at all: if it still reopens, this fails.
        var model = new LibraryInspection();
        AuditSignalBuilder.PopulateLibraryAudit(
            typeof(SectionPipelineTests).Assembly.Location,
            model,
            new Output.VerboseLogger(false));

        Assert.NotNull(model.AuditMetadata);
        var captured = model.AuditMetadata;
        var signals = model.AuditSignals;
        Assert.NotNull(signals);

        // A path that cannot be opened. A reopen would null out the metadata and change signals.
        AuditSignalBuilder.RefreshLibraryAudit(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"),
            model,
            new Output.VerboseLogger(false));

        Assert.Same(captured, model.AuditMetadata);
        Assert.Equal(signals!.Count, model.AuditSignals!.Count);
    }

    private static ScannerContext NullScannerContext() => new()
    {
        AssemblyPath = "unused.dll",
        Model = new LibraryInspection(),
        Logger = new Output.VerboseLogger(false),
    };

    // ===== Presence flag / CanRender discovery tests =====

    [Fact]
    public void CanRender_ExtensionMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        // No scanner has run (ExtensionMethods list is null), but flag is set
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasExtensionTypes = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Extension Methods" });

        Assert.Contains("Extension Methods", effective);
        Assert.Contains("Extension Methods", selected);
    }

    [Fact]
    public void CanRender_ExtensionMethods_FalseWhenNoFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasExtensionTypes = false
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.DoesNotContain("Extension Methods", effective);
    }

    [Fact]
    public void CanRender_UnsafeMembers_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasUnsafeCode = true
        };

        var effective = pipeline.GetEffectiveSections(
            model,
            Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unsafe Members" });

        Assert.Contains("Unsafe Members", effective);
    }

    [Fact]
    public void CanRender_UnsafeMembers_UsesDegradedDecodeStatus()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            UnsafeSignatureDecodeStatus = SignatureDecodeStatus.Degraded
        };

        var effective = pipeline.GetEffectiveSections(
            model,
            Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unsafe Members" });

        Assert.Contains("Unsafe Members", effective);
    }

    [Fact]
    public void CanRender_PInvokeMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasPInvokeImports = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P/Invoke Methods" });

        Assert.Contains("P/Invoke Methods", effective);
        Assert.Contains("P/Invoke Methods", selected);
    }

    [Fact]
    public void CanRender_AsyncMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasRuntimeAsync = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Async Methods" });

        Assert.Contains("Async Methods", effective);
        Assert.Contains("Async Methods", selected);
    }

    [Fact]
    public void FailedClassifiedMethods_AreContainedAndReportedInsteadOfRendered()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasPInvokeImports = true,
            ClassifiedMethodInspection = new FindingInspection<ClassifiedMethodObservation>.Failed(
                new InspectionError(
                    FindingTestData.Subject,
                    MetadataFindings.ClassifiedMethodDescriptor,
                    "method scan failed")),
        };
        HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase)
        {
            "P/Invoke Methods",
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var (empty, requested) = pipeline.GetEmptySections(
            model,
            Verbosity.Normal,
            selected);

        Assert.Contains("Inspection Failures", effective);
        Assert.DoesNotContain("P/Invoke Methods", effective);
        Assert.Equal(1, requested);
        Assert.Equal(["P/Invoke Methods"], empty);
    }

    [Fact]
    public void CanRender_Resources_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasManifestResources = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Resources" });

        Assert.Contains("Resources", effective);
        Assert.Contains("Resources", selected);
    }

    [Fact]
    public void CanRender_OpenTelemetry_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasOpenTelemetrySupport = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Integration: OpenTelemetry" });

        Assert.Contains("Integration: OpenTelemetry", effective);
        Assert.Contains("Integration: OpenTelemetry", selected);
    }

    [Theory]
    [InlineData("Aspire")]
    [InlineData("ASP.NET Core")]
    [InlineData("Authentication")]
    [InlineData("Configuration")]
    [InlineData("Dependency Injection")]
    [InlineData("AI")]
    [InlineData("Logging")]
    [InlineData("OpenAPI")]
    [InlineData("Options")]
    [InlineData("Hosting")]
    [InlineData("Health Checks")]
    [InlineData("HTTP Client")]
    public void CanRender_EcosystemIntegrationSections_UsePresenceFlags(string sectionName)
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasAspireSupport = sectionName == "Aspire",
            HasAspNetCoreSupport = sectionName == "ASP.NET Core",
            HasAuthenticationSupport = sectionName == "Authentication",
            HasConfigurationSupport = sectionName == "Configuration",
            HasAISupport = sectionName == "AI",
            HasDependencyInjectionSupport = sectionName == "Dependency Injection",
            HasLoggingSupport = sectionName == "Logging",
            HasOpenApiSupport = sectionName == "OpenAPI",
            HasOptionsSupport = sectionName == "Options",
            HasHostingSupport = sectionName == "Hosting",
            HasHealthChecksSupport = sectionName == "Health Checks",
            HasHttpClientSupport = sectionName == "HTTP Client",
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var prefixed = IntegrationSectionNames.Prefix + sectionName;
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { prefixed });

        Assert.Contains(prefixed, effective);
        Assert.Contains(prefixed, selected);
    }

    [Fact]
    public void CanRender_CustomAttributes_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasAssemblyAttributes = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Custom Attributes" });

        Assert.Contains("Custom Attributes", effective);
        Assert.Contains("Custom Attributes", selected);
    }

    [Fact]
    public void CanRender_TypeForwarders_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasExportedTypeForwarders = true
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Type Forwarders" });

        Assert.Contains("Type Forwarders", effective);
        Assert.Contains("Type Forwarders", selected);
    }

    [Fact]
    public void CanRender_AllFlagsFalse_OnlyAlwaysOnSections()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        // Only sections that don't depend on scanners or presence flags
        Assert.Contains("Library Info", effective);
        Assert.Contains("Symbols", effective);
        Assert.DoesNotContain("Extension Methods", effective);
        Assert.DoesNotContain("Unsafe Members", effective);
        Assert.DoesNotContain("P/Invoke Methods", effective);
        Assert.DoesNotContain("Resources", effective);
        Assert.DoesNotContain("Custom Attributes", effective);
        Assert.DoesNotContain("Type Forwarders", effective);
    }

    // ===== Package pipeline tests =====

    [Fact]
    public void PackagePipeline_HasExpectedSectionCount()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        Assert.Equal(15, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void PackagePipeline_SectionNamesMatchConstants()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Summary", names);
        Assert.Contains("Package Info", names);
        Assert.Contains("Package README file", names);
        Assert.Contains("Signals", names);
        Assert.Contains("Target Frameworks", names);
        Assert.Contains("Package nuspec file", names);
        Assert.Contains("Statistics", names);
        Assert.Contains("Dependencies", names);
        Assert.Contains("Package files", names);
        Assert.Contains("Package skill files", names);
        Assert.Contains("Vulnerabilities", names);
        Assert.Contains("Manifest", names);
        Assert.Contains("Runtime Dependencies", names);
    }

    [Fact]
    public void PackagePipeline_Quiet_IncludesSummaryOnly()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Quiet);

        // Quiet includes the headless Summary section for compact field rendering
        Assert.Single(effective);
        Assert.Equal("Summary", effective[0]);
    }

    [Fact]
    public void PackagePipeline_Minimal_ShowsPackageAndConditionalSections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Package is always renderable at Minimal
        Assert.Contains("Package Info", effective);
        // Statistics requires TotalDownloads (Normal verbosity anyway)
        Assert.DoesNotContain("Statistics", effective);
        // Target Frameworks requires target framework data
        Assert.DoesNotContain("Target Frameworks", effective);
        // Dependencies requires DependencyGroups (Normal verbosity anyway)
        Assert.DoesNotContain("Dependencies", effective);
        // Vulnerabilities is Detailed
        Assert.DoesNotContain("Vulnerabilities", effective);
        // Files is Detailed
        Assert.DoesNotContain("Package files", effective);
    }

    [Fact]
    public void PackagePipeline_SignalsDoesNotShowAtMinimal()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        Assert.Contains("Package Info", effective);
        Assert.Contains("Package Info", effective);
        Assert.DoesNotContain("Signals", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsManifestWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Manifest", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsRuntimeDepsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            RuntimeDependencies = [new PackageDependency { Id = "Dep", Version = "1.0.0" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Runtime Dependencies", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsStatisticsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Statistics", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_HidesStatistics()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TotalDownloads = 1000
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Statistics", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_ShowsPackageDepsWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Dependencies", effective);
    }

    [Fact]
    public void PackagePipeline_Normal_HidesVulnerabilities()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.DoesNotContain("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_ShowsVulnerabilitiesWhenPresent()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_Detailed_HidesVulnerabilitiesWhenEmpty()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult { PackageName = "Test", Version = "1.0.0" };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.DoesNotContain("Vulnerabilities", effective);
    }

    [Fact]
    public void PackagePipeline_VerbosityAutoPromote_ForVulnerabilities()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Vulnerabilities" });

        Assert.Equal(Verbosity.Detailed, required);
    }

    [Fact]
    public void PackagePipeline_VerbosityAutoPromote_ForPackage()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Package Info" });

        // Curated ladder: Quiet renders only the headless Summary preamble, so the identity
        // table first becomes available at Minimal.
        Assert.Equal(Verbosity.Minimal, required);
    }

    [Fact]
    public void PackagePipeline_ComputeIncludeSections_FiltersExplicitOnlySections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0"],
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }],
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }],
            RuntimeDependencies = [new PackageDependency { Id = "Dep2", Version = "2.0" }],
            LibraryFiles = ["lib/net8.0/test.dll"],
            Files = [new PackageFile("lib/net8.0/test.dll", 1234)],
            SignatureResult = new DotnetInspector.Services.SignatureVerificationResult { RepositoryVerified = true, Repository = "nuget.org" },
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        // At Detailed with all default-renderable data populated, Unbounded-cost sections stay
        // filtered: the whole-package listing and the PDB-backed SourceLink listing are reachable
        // only by exact name or their category door, never by turning verbosity up.
        var include = pipeline.ComputeIncludeSections(model, Verbosity.Detailed);

        Assert.NotNull(include);
        Assert.DoesNotContain("Package files", include);
        Assert.DoesNotContain("SourceLink: Files", include);
    }

    [Fact]
    public void PackagePipeline_AllSelectorSections_DefaultFirstThenRemainingAlpha()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var model = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            TargetFrameworks = ["net8.0"],
            TotalDownloads = 1000,
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            LibraryFiles = ["lib/net8.0/test.dll"],
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")]
        };

        var sections = pipeline.GetAllSelectorSections(model);

        Assert.Equal("Package Info", sections[0]);
        Assert.DoesNotContain("Summary", sections);
        // SourceLink: Files and Package files are reached through their door or by exact name,
        // so they are not members of the visible @All pole.
        Assert.Equal(["Dependencies", "Manifest", "Signals", "Statistics", "Target Frameworks"], sections.Skip(1).ToArray());
    }

    [Fact]
    public void PackagePipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(["Package Info"], pipeline.InfoSectionNames);
    }

    public static IEnumerable<object[]> DiscoverablePipelineCases()
    {
        var libraryPipeline = LibrarySections.CreatePipeline();
        var library = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo
            {
                References = [new AssemblyReference("System.Runtime", "1.0.0.0", null, null)],
                TransitiveReferences = [new AssemblyReferenceNode { Name = "System.Runtime", Version = "1.0.0.0" }]
            },
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
            library.MetadataOverview = session.MetadataImage();
        yield return DiscoverableCase("library", libraryPipeline, library);

        var packagePipeline = PackageSectionDescriptors.CreatePipeline();
        var package = new InspectionResult
        {
            PackageName = "Test",
            Version = "1.0.0",
            PackageReadmeFile = "README.md",
            PackageFiles =
            [
                new PackageFile("README.md", 1, IsReadme: true),
                new PackageFile("docs/guide.md", 1),
                new PackageFile("lib/net8.0/Test.dll", 1),
                new PackageFile("ref/net8.0/Test.dll", 1),
                new PackageFile("runtimes/win-x64/native/Test.dll", 1),
                new PackageFile("Test.nuspec", 1),
                new PackageFile("skills/demo/SKILL.md", 1)
            ],
            AuditSignals = [new AuditSignal("Package", "Assemblies", "1", "test")],
            TotalDownloads = 1,
            TargetFrameworks = ["net8.0"],
            LibraryFiles = ["lib/net8.0/Test.dll"],
            SourceFiles = [new PackageSourceFileInfo("lib/net8.0/Test.dll", "T", "https://example.com/T.cs")],
            SignatureResult = new SignatureVerificationResult { AuthorVerified = true, Publisher = "test" },
            DependencyGroups = [new DependencyGroup { TargetFramework = "net8.0", Dependencies = [new PackageDependency { Id = "Dep", Version = "1.0" }] }],
            Vulnerabilities = [new PackageVulnerability { AdvisoryUrl = "https://example.com", Severity = "High" }],
            RuntimeIdentifierPackages = [new RidPackageReference { RuntimeIdentifier = "win-x64", PackageId = "Test.win-x64" }],
            RuntimeDependencies = [new PackageDependency { Id = "Runtime.Dep", Version = "1.0" }],
            Files = [new PackageFile("lib/net8.0/Test.dll", 1)],
            AssemblyCount = 1
        };
        yield return DiscoverableCase("package", packagePipeline, package);

        var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var surface = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
            ]
        };
        yield return DiscoverableCase("type", typePipeline, surface);

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
            Members = [new ApiMember { Name = "Method", Kind = "method" }]
        };
        var memberPipeline = ApiMemberSectionDescriptors.CreatePipeline();
        yield return DiscoverableCase("member", memberPipeline, apiType, detailType);
        var overloadPipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        yield return DiscoverableCase("member-overload", overloadPipeline, apiType, detailType);
        yield return DiscoverableCase("member-detail", detailPipeline, detailType);

        var diffPipeline = DiffSections.CreatePipeline();
        yield return DiscoverableCase("diff", diffPipeline, new DiffDiscoveryModel());
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

    // ===== API type-list pipeline tests =====

    [Fact]
    public void ApiTypePipeline_HasExpectedSectionCount()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        Assert.Equal(5, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void ApiTypePipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Classes", names);
        Assert.Contains("Structs", names);
        Assert.Contains("Interfaces", names);
        Assert.Contains("Enums", names);
        Assert.Contains("Delegates", names);
    }

    [Fact]
    public void ApiTypePipeline_ShowsClassesWhenPresent()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface { Types = [new ApiType { Name = "Foo", Kind = "class" }] };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        Assert.Contains("Classes", effective);
        Assert.DoesNotContain("Structs", effective);
    }

    [Fact]
    public void ApiTypePipeline_EmptyTypes_NoSections()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface { Types = [] };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Empty(effective);
    }

    [Fact]
    public void ApiTypePipeline_AllKindsPresent()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var model = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Equal(5, effective.Count);
    }

    // ===== API member pipeline tests =====

    [Fact]
    public void ApiMemberPipeline_HasExpectedSectionCount()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        Assert.Equal(30, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void LibraryPipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(["Library Info"], pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberPipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains("Values", names);
        Assert.Contains("Type Parameters", names);
        Assert.Contains("Interfaces", names);
        Assert.Contains("Performance Triage", names);
        Assert.Contains("Baseclass", names);
        Assert.Contains("Constructors", names);
        Assert.Contains("Fields", names);
        Assert.Contains("Properties", names);
        Assert.Contains("Method Groups", names);
        Assert.Contains("Methods", names);
        Assert.Contains("Operators", names);
        Assert.Contains("Explicit Interface Implementations", names);
        Assert.Contains("Extension Methods", names);
        Assert.Contains("Events", names);
        Assert.Contains("Source Files", names);
        Assert.Contains("IL", names);
        Assert.Contains("Decompiled Source", names);
        Assert.Contains("Original Source", names);
        Assert.Contains("Source Diff", names);
        Assert.Contains("Custom Attributes", names);
        Assert.Contains("Called Types", names);
        Assert.Contains("Top Leverage", names);
    }

    [Fact]
    public void ApiMemberPipeline_EnumValues_PrimaryAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Color", Kind = "enum",
            Members = [new ApiMember { Name = "Red", Kind = "field", EnumValue = 0 }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var atNormal = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        // Values is index 0 (primary) — shown at Minimal for enums
        Assert.Contains("Values", atMinimal);
        Assert.Contains("Values", atNormal);
    }

    [Fact]
    public void ApiMemberPipeline_TypeParameters_ShowsAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "List", Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // TypeParameters is within primary threshold (before first expensive)
        Assert.Contains("Type Parameters", atMinimal);
    }

    [Fact]
    public void ApiMemberPipeline_Interfaces_ShowsAtMinimal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Interfaces = ["IDisposable"]
        };

        var atMinimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);

        // Interfaces is within primary threshold (before first expensive)
        Assert.Contains("Interfaces", atMinimal);
    }

    [Fact]
    public void ApiMemberPipeline_Baseclass_RequiresDetailedAndNonTrivial()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Trivial base (System.Object) should not render
        var trivialModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "System.Object" };
        var trivialEffective = pipeline.GetEffectiveSections(trivialModel, Verbosity.Detailed);
        Assert.DoesNotContain("Baseclass", trivialEffective);

        // Real base should render at Detailed
        var realModel = new ApiType { Name = "Foo", Kind = "class", BaseType = "MyBase" };
        var realEffective = pipeline.GetEffectiveSections(realModel, Verbosity.Detailed);
        Assert.Contains("Baseclass", realEffective);
    }

    [Fact]
    public void ApiMemberPipeline_MemberSections_AtNormal()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Foo", Kind = "class",
            Members =
            [
                new ApiMember { Name = ".ctor", Kind = "constructor" },
                new ApiMember { Name = "Count", Kind = "property" },
                new ApiMember { Name = "GetValue", Kind = "method" },
                new ApiMember { Name = "op_Equality", Kind = "operator" },
            ]
        };

        var effective = pipeline.GetEffectiveSections(model, Verbosity.Normal);

        Assert.Contains("Constructors", effective);
        Assert.Contains("Properties", effective);
        Assert.Contains("Method Groups", effective);
        Assert.Contains("Methods", effective);
        Assert.Contains("Operators", effective);
        Assert.DoesNotContain("Fields", effective);
        Assert.DoesNotContain("Events", effective);
    }

    [Fact]
    public void ApiMemberPipeline_VerbosityAutoPromote_ForInterfaces()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        // Interfaces is within primary threshold — no promotion needed
        var required = pipeline.GetRequiredVerbosity(new HashSet<string> { "Interfaces" });

        Assert.Equal(Verbosity.Quiet, required);
    }

    [Fact]
    public void ApiMemberPipeline_SourceLocations_AreMemberGroupAndDetailOnly()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        // Remote Source section was removed; SourceLink location rows live on the
        // member group/detail pipelines instead of the broad type/member-list view.
        Assert.DoesNotContain("Remote Source", names);
        Assert.DoesNotContain(SectionNames.SourceLocations, names);

        var overloadPipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        Assert.Contains(SectionNames.SourceLocations, overloadPipeline.AllSectionNames);
        Assert.Equal("opt-in", Assert.Contains(SectionNames.SourceLocations, overloadPipeline.GetCostAnnotations()));

        var detailPipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        Assert.Contains(SectionNames.SourceLocations, detailPipeline.AllSectionNames);
        Assert.Equal("opt-in", Assert.Contains(SectionNames.SourceLocations, detailPipeline.GetCostAnnotations()));
    }

    [Fact]
    public void ApiMemberPipeline_InfoPreset_UsesMethodGroups()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();

        Assert.Contains("Method Groups", pipeline.InfoSectionNames);
        Assert.Contains("Operators", pipeline.InfoSectionNames);
        Assert.Contains("Explicit Interface Implementations", pipeline.InfoSectionNames);
        Assert.Contains("Extension Methods", pipeline.InfoSectionNames);
        Assert.DoesNotContain("Methods", pipeline.InfoSectionNames);
        Assert.Equal("verbose", Assert.Contains("Methods", pipeline.GetCostAnnotations()));
    }

    [Fact]
    public void ApiMemberPipeline_AlternateMemberRows_UseExplicitApplicability()
    {
        var pipeline = ApiMemberSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample",
            Kind = "class",
            Members =
            [
                new ApiMember { Name = "Run", Kind = "method" },
                new ApiMember { Name = "op_Equality", Kind = "operator" },
                new ApiMember { Name = "IFoo.Bar", Kind = "explicit-interface-implementation" },
                new ApiMember { Name = "Ext", Kind = "extension-method" }
            ]
        };

        var explicitlyApplicable = pipeline.GetExplicitlyApplicableSections(model);

        Assert.Contains("Methods", explicitlyApplicable);
        Assert.Contains("Operators", explicitlyApplicable);
        Assert.Contains("Explicit Interface Implementations", explicitlyApplicable);
        Assert.Contains("Extension Methods", explicitlyApplicable);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_InfoPreset_UsesMethods()
    {
        var pipeline = ApiMemberOverloadSectionDescriptors.CreatePipeline();

        Assert.Contains("Methods", pipeline.InfoSectionNames);
        Assert.DoesNotContain("Method Groups", pipeline.InfoSectionNames);
        Assert.Contains("Call Graph", pipeline.AllSectionNames);
        Assert.DoesNotContain("Caller Graph", pipeline.AllSectionNames);
        Assert.Contains("Unsafe Operations", pipeline.AllSectionNames);
    }

    [Fact]
    public void ApiMemberDetailPipeline_NormalIncludesLocalImplementationSections()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        var model = new ApiType
        {
            Name = "Sample", Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method" }]
        };

        var minimal = pipeline.GetEffectiveSections(model, Verbosity.Minimal);
        var normal = pipeline.GetEffectiveSections(model, Verbosity.Normal);
        var detailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);

        Assert.Contains("Signature", minimal);
        Assert.DoesNotContain("Decompiled Source", minimal);
        Assert.DoesNotContain("Original Source", minimal);
        Assert.Contains("Decompiled Source", normal);
        Assert.Contains("IL", normal);
        Assert.DoesNotContain("Annotated Source", normal);
        Assert.DoesNotContain("Original Source", normal);
        Assert.Contains("Decompiled Source", detailed);
        Assert.Contains("Original Source", detailed);
        Assert.Contains("IL", detailed);
        Assert.DoesNotContain("Annotated Source", detailed);
        var optIn = pipeline.GetCostAnnotations();
        Assert.Equal("opt-in", Assert.Contains("Calls", optIn));
        Assert.Equal("opt-in", Assert.Contains("Exception Regions", optIn));
        Assert.Equal("opt-in", Assert.Contains("Callers", optIn));
        Assert.Equal("opt-in", Assert.Contains("Call Graph", optIn));
        Assert.Equal("opt-in", Assert.Contains("Facts", optIn));
        Assert.Equal("opt-in", Assert.Contains("Unsafe Operations", optIn));
        Assert.DoesNotContain("Calls", normal);
        Assert.DoesNotContain("Exception Regions", normal);
        Assert.DoesNotContain("Callers", normal);
        Assert.DoesNotContain("Call Graph", normal);
        Assert.DoesNotContain("Facts", normal);
        Assert.DoesNotContain("Unsafe Operations", normal);
        Assert.DoesNotContain("Calls", detailed);
        Assert.DoesNotContain("Exception Regions", detailed);
        Assert.DoesNotContain("Callers", detailed);
        Assert.DoesNotContain("Call Graph", detailed);
        Assert.DoesNotContain("Facts", detailed);
        Assert.DoesNotContain("Unsafe Operations", detailed);
    }

    [Fact]
    public void ApiMemberDetailPipeline_InfoPreset_HasDenseSections()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();

        Assert.Equal(["Signature", "Decompiled Source"], pipeline.InfoSectionNames);
    }

    [Fact]
    public void ApiMemberDetailPipeline_SourceCategory_MapsToSourceViews()
    {
        var categories = ApiMemberDetailSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.DoesNotContain(SectionCategoryNames.Audit, categories.Keys);
        Assert.Equal(
            [
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.IL
            ],
            categories[SectionCategoryNames.Source]);
    }

    [Fact]
    public void ApiMemberOverloadPipeline_SourceCategory_MapsToSourceViews()
    {
        var categories = ApiMemberOverloadSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.Equal(
            [
                SectionNames.DecompiledSource,
                SectionNames.AnnotatedSource,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.IL
            ],
            categories[SectionCategoryNames.Source]);
    }
}
