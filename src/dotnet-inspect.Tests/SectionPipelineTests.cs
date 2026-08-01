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

        // Having rows makes the section renderable, not automatic: it is backed by the
        // OptimizationOpportunities scanner, which declares Cost=Unbounded, so it leaves the
        // -v:d ladder and is reached through -S or the @Performance door instead. Asserting both
        // directions keeps this test honest about which of the two properties it is pinning.
        Assert.DoesNotContain(section, effective);
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
            .Add("A", SectionCost.NetworkFree, _ => ran.Add("A"))
            .Add("B", SectionCost.NetworkFree, _ => ran.Add("B"))
            .Add("C", SectionCost.NetworkFree, _ => ran.Add("C"));

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
            .Add("A", SectionCost.NetworkFree, _ => ran = true);

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
            .Add("leaf", SectionCost.NetworkFree, _ => order.Add("leaf"))
            .Add("mid", SectionCost.NetworkFree, _ => order.Add("mid"), "leaf")
            .Add("top", SectionCost.NetworkFree, _ => order.Add("top"), "mid", "leaf");

        registry.RunScanners(["top"], NullScannerContext());

        Assert.Equal(["leaf", "mid", "top"], order);
    }

    [Fact]
    public void RunScanners_SharedPrerequisiteRunsOnceAcrossRequestedScanners()
    {
        List<string> order = [];
        var registry = new ScannerRegistry()
            .Add("leaf", SectionCost.NetworkFree, _ => order.Add("leaf"))
            .Add("a", SectionCost.NetworkFree, _ => order.Add("a"), "leaf")
            .Add("b", SectionCost.NetworkFree, _ => order.Add("b"), "leaf");

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
            .Add("a", SectionCost.NetworkFree, _ => order.Add("a"))
            .Add("b", SectionCost.NetworkFree, _ => order.Add("b"))
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
            .Add("leaf", SectionCost.NetworkFree, _ => { })
            .Add("mid", SectionCost.NetworkFree, _ => { }, "leaf")
            .Add("top", SectionCost.NetworkFree, _ => { }, "mid");

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
            .Add("a", SectionCost.NetworkFree, _ => { }, "typo");

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
        var tolerant = new ScannerRegistry().Add("a", SectionCost.NetworkFree, _ => ran = true);
        tolerant.RunScanners(["a", "not-registered"], NullScannerContext());
        Assert.True(ran);
    }

    [Fact]
    public void RunScanners_ThrowsOnPrerequisiteCycle()
    {
        var registry = new ScannerRegistry()
            .Add("a", SectionCost.NetworkFree, _ => { }, "b")
            .Add("b", SectionCost.NetworkFree, _ => { }, "a");

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
            .Add("a", SectionCost.NetworkFree, _ => { }, "b")
            .Add("b", SectionCost.NetworkFree, _ => { }, "a");

        var ex = Assert.Throws<InvalidOperationException>(() => registry.ExpandRequired(["a"]));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandRequired_AllowsDiamondPrerequisites()
    {
        // A shared prerequisite reached by two paths is not a cycle. Guards against a cycle check
        // that keys off "already seen" rather than "currently being visited".
        var registry = new ScannerRegistry()
            .Add("d", SectionCost.NetworkFree, _ => { })
            .Add("b", SectionCost.NetworkFree, _ => { }, "d")
            .Add("c", SectionCost.NetworkFree, _ => { }, "d")
            .Add("a", SectionCost.NetworkFree, _ => { }, "b", "c");

        Assert.Equal(
            ["a", "b", "c", "d"],
            registry.ExpandRequired(["a"]).OrderBy(k => k, StringComparer.Ordinal));
    }

    // ===== Scanner cost tests =====

    [Fact]
    public void Scanner_CannotTakeTheBodyIndexWithoutDeclaringItsCost()
    {
        // The registry cannot see that a scanner touches the body index, because ctx.BodyIndex is
        // a lazily-invoked method group -- which is how four scanners came to declare NetworkFree
        // while doing whole-assembly IL work. So the declaration is enforced where the cost is
        // incurred, not where it is registered.
        var cheap = new ScannerRegistry()
            .Add("cheap", SectionCost.NetworkFree, ctx => ctx.BodyIndex());

        var ex = Assert.Throws<InvalidOperationException>(
            () => cheap.RunScanners(["cheap"], NullScannerContext()));
        Assert.Contains("body index", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NetworkFree", ex.Message, StringComparison.Ordinal);

        // Non-vacuity: a scanner that DID declare Unbounded must get past the declaration check.
        // Without this the gate would also pass if BodyIndex threw unconditionally. The declared
        // scanner still fails, but on the missing metadata context -- a different error, proving
        // the cost check let it through.
        var declared = new ScannerRegistry()
            .Add("declared", SectionCost.Unbounded, ctx => ctx.BodyIndex());

        var allowed = Assert.Throws<InvalidOperationException>(
            () => declared.RunScanners(["declared"], NullScannerContext()));
        Assert.DoesNotContain("Unbounded", allowed.Message, StringComparison.Ordinal);
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scanner_CannotTakeTheDrillMapWithoutDeclaringItsCost()
    {
        var cheap = new ScannerRegistry()
            .Add("cheap", SectionCost.NetworkFree, ctx => ctx.DrillMap());

        var ex = Assert.Throws<InvalidOperationException>(
            () => cheap.RunScanners(["cheap"], NullScannerContext()));
        Assert.Contains("drill map", ex.Message, StringComparison.Ordinal);

        var declared = new ScannerRegistry()
            .Add("declared", SectionCost.Unbounded, ctx => ctx.DrillMap());

        var allowed = Assert.Throws<InvalidOperationException>(
            () => declared.RunScanners(["declared"], NullScannerContext()));
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScannerDeclaration_DoesNotOutliveTheRun()
    {
        // ctx.BodyIndex is handed to scan methods as a method group, so the Func can outlive the
        // scanner that supplied it and be invoked later while rendering. The declaration must
        // therefore be scoped to scanner execution: left set, the LAST scanner's declaration would
        // govern every later use.
        //
        // An earlier version of this gate ran a cheap scanner after an expensive prerequisite and
        // asserted the cheap one was refused. That proved nothing -- each scanner overwrites
        // Running on entry, so deleting the restore left it green. The observable that actually
        // depends on the restore is the state after the run.
        var registry = new ScannerRegistry()
            .Add("cheap", SectionCost.NetworkFree, _ => { });
        var context = NullScannerContext();

        registry.RunScanners(["cheap"], context);

        // Refused, because the run is over and nothing is left to attribute the work to. The
        // message distinguishes the two reasons a call can be refused, which is what keeps the
        // restore pinned: delete the `finally` in RunWithRequirements and Running stays set to
        // ("cheap", NetworkFree), so the *declaration* message appears here instead.
        var ex = Assert.Throws<InvalidOperationException>(() => context.BodyIndex());
        Assert.Contains("outside a scanner run", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("declares Cost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnscopedCallers_AreRefusedTheBodyIndex()
    {
        // Raised by the GPT review of #3626, as a working exploit rather than a hypothetical.
        //
        // RequireUnboundedDeclaration used to *return* when Running was null, on the reasoning
        // that a caller outside a scanner run "has no declaration to check against". That made
        // the absence of a declaration the one way to escape needing one. GPT reached it from
        // ordinary code: a descriptor's CanRender that captured the ScannerContext called
        // BodyIndex() while rendering -- after RunScanners had restored Running to null -- and
        // the CLI spent 5.2 seconds on whole-assembly work that no section had declared.
        //
        // It also undermined the reachability gate below. That gate cuts its walk at the accessor
        // on the grounds that the accessor is guarded; if the accessor waves through every caller
        // that is not a scanner, the cut launders exactly the violation it claims to exclude.
        //
        // Cost is declared per scanner, so work that cannot be attributed to one cannot be
        // afforded by anything. Both resources refuse.
        var context = NullScannerContext();
        Assert.Null(context.Running);

        var body = Assert.Throws<InvalidOperationException>(() => context.BodyIndex());
        Assert.Contains("outside a scanner run", body.Message, StringComparison.Ordinal);

        var drill = Assert.Throws<InvalidOperationException>(() => context.DrillMap());
        Assert.Contains("outside a scanner run", drill.Message, StringComparison.Ordinal);

        // Non-vacuity: the refusal must be the declaration check, not the missing metadata context
        // that NullScannerContext would also throw on. Declaring Unbounded gets past this check
        // and reaches the context requirement instead, which is a different message.
        context.Running = ("declared", SectionCost.Unbounded);
        var allowed = Assert.Throws<InvalidOperationException>(() => context.DrillMap());
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CostOf_IsTheMaximumOverTheTransitivePrerequisiteClosure()
    {
        // A bundle does no work of its own, so its cost is entirely what it pulls in. Letting it
        // declare its own cost would let it under-state that, which is why AddBundle takes none.
        var registry = new ScannerRegistry()
            .Add("cheap", SectionCost.NetworkFree, _ => { })
            .Add("expensive", SectionCost.Unbounded, _ => { })
            .Add("moderate", SectionCost.Moderated, _ => { })
            .AddBundle("mixed", "cheap", "expensive")
            .AddBundle("allCheap", "cheap")
            .Add("indirect", SectionCost.NetworkFree, _ => { }, "mixed");

        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("cheap"));
        Assert.Equal(SectionCost.Moderated, registry.CostOf("moderate"));
        Assert.Equal(SectionCost.Unbounded, registry.CostOf("expensive"));
        Assert.Equal(SectionCost.Unbounded, registry.CostOf("mixed"));
        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("allCheap"));

        // Transitive: a cheap scanner whose prerequisite is a bundle containing an expensive
        // scanner costs what the run will actually do, not what it declared for itself.
        Assert.Equal(SectionCost.Unbounded, registry.CostOf("indirect"));
    }

    [Fact]
    public void SectionsBackedByUnboundedScanners_LeaveTheDetailedLadderButKeepTheirDoor()
    {
        // Seeded from the REGISTRY, where cost is declared, rather than from the pipeline that
        // consumes it: asking the pipeline which sections it considers unbounded and then checking
        // that it acted on that answer would assert nothing. The registry and the selection code
        // are the two halves this change couples, so the gate holds one fixed and observes the
        // other.
        var registry = LibrarySections.CreateScannerRegistry();
        var pipeline = LibrarySections.CreatePipeline();
        // Presence flags only: the point is that each expensive section CAN render, so its
        // absence from the -v:d ladder below is attributable to cost and nothing else.
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
            HasUnsafeCode = true,
        };

        var unboundedScanners = registry.RegisteredKeys
            .Where(key => registry.CostOf(key) == SectionCost.Unbounded)
            .ToHashSet(StringComparer.Ordinal);

        var expensiveSections = pipeline.ScannerBoundSections
            .Where(section => unboundedScanners.Contains(section.ScannerKey))
            .Select(section => section.Name)
            .ToList();

        // Non-vacuity: an empty expensive set would satisfy every assertion below.
        Assert.NotEmpty(expensiveSections);

        var detailed = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var allPole = pipeline.GetAllSelectorSections(model);

        foreach (var name in expensiveSections)
        {
            Assert.DoesNotContain(name, detailed);
            Assert.DoesNotContain(name, allPole);

            // The other half, and what stops the first from passing for the wrong reason: absence
            // from the ladder must be the cost decision, not an inability to render. Every one of
            // these sections must still be reachable by exact name on the very same model.
            var byName = pipeline.GetEffectiveSections(
                model, Verbosity.Detailed,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name });
            Assert.True(byName.Contains(name), $"'{name}' left the ladder and is unreachable by -S.");
        }
    }

    [Fact]
    public void UseScannerCosts_ThrowsAfterSectionsAreRegistered()
    {
        // Costs are applied to entries as they are added, so wiring the source afterwards would
        // silently leave everything already registered at its declared cost.
        var pipeline = new SectionPipeline<LibraryInspection>()
            .Add<LibrarySections.ExtensionMethods>();

        Assert.Throws<InvalidOperationException>(
            () => pipeline.UseScannerCosts(_ => SectionCost.Unbounded));
    }

    [Fact]
    public void LibraryPipeline_ConsultsScannerCosts()
    {
        // Non-vacuity for the whole strand: LibrarySections.CreatePipeline must actually call
        // UseScannerCosts. Dropping that one line leaves every gate above green except this one,
        // because each scanner-bound section would simply keep its own declared cost.
        var withCosts = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
        };

        // Performance: Boxing declares no cost of its own; it is expensive only because the
        // OptimizationOpportunities scanner behind it is. If the pipeline stopped consulting the
        // registry it would return to the -v:d ladder.
        Assert.DoesNotContain(
            SectionNames.PerformanceBoxing,
            withCosts.GetEffectiveSections(model, Verbosity.Detailed));
    }

    [Fact]
    public void LibrarySections_AboveNetworkFree_AreExactlyTheBodyIndexFamily()
    {
        // GPT review of #3626 caught Switches silently leaving -v:n: its scanner had been declared
        // Moderated, and Moderated means "auto-runs only at -v:d". Nothing failed. The regression
        // was visible only by building origin/main and diffing rendered output, which is far too
        // expensive a way to notice that a section changed verbosity ladder.
        //
        // The literal list is the point: it is a human-reviewed statement of which sections are
        // deliberately not cheap. Any cost change that moves a section across the NetworkFree
        // boundary now fails here and has to be justified in review.
        //
        // The re-review then showed one axis was still open. The first version of this gate read
        // registry.CostOf(section.ScannerKey), which is only one of the two inputs: the raise is
        // one-way, so a descriptor can declare a higher cost than its scanner and leave the ladder
        // on its own. Declaring `Cost => Moderated` on the Switches descriptor reproduced the
        // original defect exactly, with both new gates green. So the primary assertion is on the
        // pipeline's effective cost — the value the ladder actually consults — which subsumes the
        // scanner axis, because a scanner raise always raises the entry.
        var registry = LibrarySections.CreateScannerRegistry();
        var pipeline = LibrarySections.CreatePipeline();

        // The scanner axis: sections that are expensive because the scan behind them is. This is
        // the family this change moved off the ladder.
        string[] expectedBodyIndexFamily =
        [
            SectionNames.ArrayPoolEscapes,
            SectionNames.PerformanceHotspots,
            SectionNames.PerformanceArrays,
            SectionNames.PerformanceAsync,
            SectionNames.PerformanceBoxing,
            SectionNames.PerformanceClosures,
            SectionNames.PerformanceEnumerators,
            SectionNames.PerformanceLoops,
            SectionNames.PerformanceOther,
            SectionNames.TopLeverage,
            SectionNames.UnsafeMembers,
        ];

        var scannerAboveCheap = pipeline.ScannerBoundSections
            .Where(section => registry.CostOf(section.ScannerKey) > SectionCost.NetworkFree)
            .Select(section => section.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            expectedBodyIndexFamily.OrderBy(name => name, StringComparer.Ordinal),
            scannerAboveCheap);

        // The effective axis: everything the ladder will refuse to auto-render, whichever
        // declaration made it so. The Metadata table sections and the SourceLink family were
        // already Unbounded by their own descriptors before this change — they are here because
        // this list is the honest full set, not because this PR moved them.
        string[] expectedAboveCheap =
        [
            .. expectedBodyIndexFamily,
            "Metadata: #Blob",
            "Metadata: #GUID",
            "Metadata: #Strings",
            "Metadata: #US",
            "Metadata: Assembly",
            "Metadata: AssemblyRef",
            "Metadata: Constant",
            "Metadata: CustomAttribute",
            "Metadata: ExportedType",
            "Metadata: Field",
            "Metadata: GenericParam",
            "Metadata: MemberRef",
            "Metadata: MethodDef",
            "Metadata: MethodImpl",
            "Metadata: MethodSpec",
            "Metadata: Module",
            "Metadata: Param",
            "Metadata: StandAloneSig",
            "Metadata: TypeDef",
            "Metadata: TypeRef",
            "Metadata: TypeSpec",
            SectionNames.SourceLinkAvailability,
            SectionNames.SourceLinkFiles,
            SectionNames.SourceLinkIntegrity,
            SectionNames.SourceLinkMissingFiles,
        ];

        var effectivelyAboveCheap = pipeline.SectionCosts
            .Where(section => section.Cost > SectionCost.NetworkFree)
            .Select(section => section.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            expectedAboveCheap.OrderBy(name => name, StringComparer.Ordinal),
            effectivelyAboveCheap);
    }

    [Fact]
    public void ScannerKey_CannotBeRegisteredTwice()
    {
        // Raised as BLOCKING by the GPT review of #3626. SectionPipeline.Add snapshots the
        // scanner's cost into the entry, so a later re-registration that raised the cost would
        // leave the pipeline reading a stale cheap value while CostOf reported the truth -- and
        // the pipeline is what the verbosity ladder consults. GPT demonstrated exactly that:
        // register NetworkFree, add the entry, re-register Unbounded, and SectionCosts still
        // answered NetworkFree.
        //
        // Making a key's cost immutable once declared is what makes the effective axis subsume
        // the scanner axis unconditionally, rather than only for the construction order
        // LibrarySections happens to use today.
        var registry = new ScannerRegistry();
        registry.Add("Solo", SectionCost.NetworkFree, _ => { });

        var raise = Assert.Throws<InvalidOperationException>(
            () => registry.Add("Solo", SectionCost.Unbounded, _ => { }));
        Assert.Contains("already registered", raise.Message, StringComparison.Ordinal);

        // The same key cannot be laundered through a bundle either, in either direction.
        Assert.Throws<InvalidOperationException>(() => registry.AddBundle("Solo", "Other"));

        registry.AddBundle("Bundle", "Solo");
        Assert.Throws<InvalidOperationException>(
            () => registry.Add("Bundle", SectionCost.NetworkFree, _ => { }));

        // The cost that was declared first is the cost that stands.
        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("Solo"));
    }

    [Fact]
    public void PrerequisiteList_CannotBeMutatedAfterRegistration()
    {
        // Raised as BLOCKING by the GPT review of #3626, one level deeper than the re-registration
        // guard. The registry stored the caller's `params string[]` by reference and handed the
        // same array back through RequirementsOf as IReadOnlyList, which casts straight back to
        // string[]. Either alias could be edited after a section had already snapshotted the cost,
        // so CostOf would report Unbounded while SectionCosts kept saying NetworkFree -- the
        // pipeline's value being the one the ladder reads.
        var registry = new ScannerRegistry();
        registry.Add("Cheap", SectionCost.NetworkFree, _ => { });
        registry.Add("Expensive", SectionCost.Unbounded, _ => { });

        // Registration must copy, so editing the caller's array afterwards changes nothing.
        var declared = new[] { "Cheap" };
        registry.Add("Root", SectionCost.NetworkFree, _ => { }, declared);
        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("Root"));

        declared[0] = "Expensive";
        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("Root"));
        Assert.Equal(["Cheap"], registry.RequirementsOf("Root"));

        // And the accessor must not hand out a mutable alias of the stored list. ImmutableArray
        // is the enforcement: there is no cast that reaches the backing store.
        Assert.Equal(["Cheap"], registry.RequirementsOf("Root"));
        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("Root"));
    }

    [Fact]
    public void NoSectionReachesTheBodyIndexExceptThroughTheGatedAccessor()
    {
        // The fourth route into this PR's defect class, raised by the GPT review of #3626 after
        // the first three were closed. RequireUnboundedDeclaration guards ctx.BodyIndex() and
        // ctx.DrillMap(), but a scanner holds ctx.AssemblyPath and can call
        // LibraryBodyIndex.Open(path) itself, doing seconds of whole-assembly work while its
        // section still declares NetworkFree. Nothing in the type system prevents that, and
        // unlike the ImmutableCollectionsMarshal route it is reachable from ordinary code -- so
        // it is a real drift path rather than deliberate subversion.
        //
        // The first version of this gate looked only for *direct* openers in the Sections
        // namespace. GPT's confirmation pass tampered it correctly: a NetworkFree scanner that
        // calls a LibraryMetadataService helper which opens an index passes such a check, and
        // scanners already route their work through that class. So a direct-call gate proves
        // almost nothing about the route it claims to close.
        //
        // What is actually asserted here is the useful property, one step stronger: *every* path
        // from DotnetInspector.Sections to a body-index opener runs through the gated accessor.
        // The walk is a reverse reachability over this assembly's call graph, seeded at the
        // methods that open an index and cut at ScannerContext.BodyIndex/DrillMap -- the two
        // members RequireUnboundedDeclaration guards. Anything in Sections still reachable after
        // that cut has found a way to the expensive work that the cost declaration never sees.
        //
        // Cutting matters: without it this walk reports seven Sections members, and all seven are
        // legitimate (the accessor itself, the four Unbounded scanner lambdas that use it, and
        // their enclosing factory). Those are the sanctioned path, not violations.
        //
        // This uses the repository's own IL analysis over its own compiled assemblies, because no
        // seam can intercept a static call.
        //
        // Every product assembly, not a hand-picked few. The GPT re-review of ca1ac260 escaped the
        // single-assembly version of this gate with ordinary typed code: a NetworkFree scanner
        // calling LeakTriageAnalyzer.AnalyzeAssembly(ctx.AssemblyPath). That helper lives in
        // ILInspector.Analysis and opens the index internally, so the CLI's own call graph shows
        // nothing but a call to a method it knows nothing about -- and the real CLI did 5.1 s of
        // undeclared work with both claims below still green.
        //
        // Merging in a named second assembly fixed that instance and left the shape of it intact:
        // the MAI re-review pointed at ILInspector.Research, which opens an index in
        // AnalysisIndexCache.ForPath and ResearchDiff. So the assembly set is not listed here at
        // all. It is *derived* as the product reference closure of the CLI, which means a new
        // product assembly enters this gate by being referenced rather than by someone remembering
        // to add it. Deriving it also excludes test-support assemblies such as
        // DotnetInspector.Fixtures for a reason rather than by an exception: the CLI does not
        // reference them, so a fixture that opens an index cannot pad the pinned set below.
        static bool IsProductAssembly(string? name)
            => name is not null
                && (name.StartsWith("ILInspector.", StringComparison.Ordinal)
                    || name.StartsWith("DotnetInspector.", StringComparison.Ordinal)
                    || name == "dotnet-inspect");

        var productAssemblies = new Dictionary<string, System.Reflection.Assembly>(StringComparer.Ordinal);
        var toVisit = new Queue<System.Reflection.Assembly>();
        toVisit.Enqueue(typeof(ScannerRegistry).Assembly);
        while (toVisit.Count > 0)
        {
            var assembly = toVisit.Dequeue();
            if (!productAssemblies.TryAdd(assembly.GetName().Name!, assembly))
                continue;

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (IsProductAssembly(reference.Name))
                    toVisit.Enqueue(System.Reflection.Assembly.Load(reference));
            }
        }

        var calls = productAssemblies.Values
            .Select(assembly => assembly.Location)
            .OrderBy(location => location, StringComparer.Ordinal)
            .SelectMany(path => ILInspector.Analysis.LibraryBodyIndex.Open(path).DirectCalls)
            .ToList();

        // The whole graph below is only as good as the product's own member resolution, and a
        // silent decode failure there is invisible to every assertion built on top of it. That is
        // not hypothetical: a MemberRef whose parent is a TypeSpec -- `callvirt
        // IOpener`1<string>::Open` -- used to resolve to a declaring type with an empty namespace
        // *and* an empty name, because a GenericInstance carries its identity on ElementType. The
        // call was recorded, so nothing looked wrong; the edge simply pointed at nothing, and the
        // interface member could never be a node. Three separate escapes rested on it.
        //
        // This assertion is deliberately about the *product*, not about this gate. A declaring
        // type the product cannot name is either a decode failure it must mark `Unsupported` and
        // explain, or a bug. What it must never be is success-shaped empty output.
        //
        // The claim is identifiability, not the presence of a name string. Array-family kinds are
        // legitimately nameless: `int[,]::Get` really is a member of a composed type, and there is
        // no definition to name -- identity lives on ElementType, so they are admitted only when
        // that element is itself named. GenericInstance is deliberately *not* admitted on those
        // terms, because a constructed generic's members belong to the open definition and the
        // product must resolve to it; reverting that fix reappears here as a `GenericInstance` row.
        ILInspector.Analysis.TypeRefKind[] composedOverAnElement =
        [
            ILInspector.Analysis.TypeRefKind.SzArray,
            ILInspector.Analysis.TypeRefKind.Array,
            ILInspector.Analysis.TypeRefKind.ByRef,
            ILInspector.Analysis.TypeRefKind.Pointer,
            ILInspector.Analysis.TypeRefKind.Pinned,
        ];

        var unnameableDeclaringTypes = calls
            .Select(call => call.Callee.DeclaringType)
            .Where(type => type.Name.Length == 0
                && type.Kind != ILInspector.Analysis.TypeRefKind.Unsupported
                && !(composedOverAnElement.Contains(type.Kind) && type.ElementType is { Name.Length: > 0 }))
            .Select(type => $"{type.Kind} namespace=[{type.Namespace}] element=[{type.ElementType?.Name}]")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.Equal([], unnameableDeclaringTypes);

        // A constructed generic's *members* belong to its open definition, so every claim below
        // that keys on a declaring type wants the definition rather than the instantiation.
        static ILInspector.Analysis.TypeRef OpenDefinitionOf(ILInspector.Analysis.TypeRef type)
            => type.Kind == ILInspector.Analysis.TypeRefKind.GenericInstance && type.ElementType is { } definition
                ? definition
                : type;

        // Identity is the *signature*, not the name. GPT's other blocking finding on ca1ac260 was
        // that projecting to Type::Method and calling Distinct() collapses overloads: adding a
        // second MethodBodyInspectionSession.OpenWithFeatures overload that opened an index left
        // the pinned set below unchanged, so the gate stayed green while a new opener existed.
        //
        // Generic arity is part of that signature, and omitting it was the same bug once more.
        // Review escaped the parameter-typed version with `LibraryBodyIndex.Open<T>(string)`,
        // whose parameter list is identical to the existing `Open(string)`, so a brand new opener
        // folded silently into an already-pinned entry. Arity is what separates them.
        //
        // Callees key on OpenSignatureParameters rather than ParameterTypes. That is this
        // repository's own answer to the same problem -- see the doc comment on
        // MemberRef.OpenParameterTypes, which exists so that cross-assembly caller-graph identity
        // reduces a constructed call site and the open definition it targets to one key instead
        // of letting them drift apart per instantiation.
        //
        // Signatures turn out to be load-bearing a second time, across the assembly boundary.
        // ResourceLifecycleAnalysis has two InspectAssembly overloads and they sit on opposite
        // sides of this gate: InspectAssembly(string, ...) opens an index itself, while the
        // InspectAssembly(Func<LibraryBodyIndex>, ...) overload that ScanResourceTriage actually
        // calls opens nothing -- it invokes the gated accessor it was handed. Keyed by name alone
        // the two collapse into one node, and the sanctioned ResourceTriage path gets reported as
        // a violation, which would then have to be excused by an allow list. An allow list here
        // would be the hole: it is exactly where a real escape would go to hide.
        static string SignatureKey(
            ILInspector.Analysis.TypeRef declaringType,
            string name,
            int genericArity,
            IEnumerable<ILInspector.Analysis.TypeRef> parameterTypes)
            => $"{declaringType.Namespace}.{declaringType.Name}::{name}"
                + (genericArity > 0 ? $"`{genericArity}" : "")
                + $"({string.Join(",", parameterTypes)})";

        static string CallerKey(ILInspector.Analysis.MethodIdentity method)
            => SignatureKey(
                method.DeclaringType, method.Name, method.GenericArity, method.ParameterTypes);

        static string CalleeKey(ILInspector.Analysis.MemberRef callee)
            => SignatureKey(
                callee.DeclaringType, callee.Name, callee.GenericArity, callee.OpenSignatureParameters);

        static bool OpensAnIndex(ILInspector.Analysis.DirectCall call)
            => call.Callee.DeclaringType.Name == "LibraryBodyIndex"
                && call.Callee.Name is "Open" or "OpenFromPrefetchedImage";

        const string BodyIndexGate = "DotnetInspector.Sections.ScannerContext::BodyIndex()";
        const string DrillMapGate = "DotnetInspector.Sections.ScannerContext::DrillMap()";

        var definedKeys = calls
            .Select(call => CallerKey(call.Caller))
            .ToHashSet(StringComparer.Ordinal);

        // A caller-derived node set only contains members that have a body. An interface member
        // does not, so `IBodyOpener::Open` was not a node at all -- which silently dropped both the
        // real `callvirt` edge into it and the hierarchy edge out of it, and is why the first
        // attempt at closing interface dispatch stayed green. Product members that appear only as
        // callees are added so abstract and interface declarations can carry edges. The product
        // namespace filter is what keeps the BCL out of the graph.
        definedKeys.UnionWith(calls
            .Select(call => call.Callee)
            .Where(callee => callee.DeclaringType.Namespace.StartsWith("DotnetInspector.", StringComparison.Ordinal)
                || callee.DeclaringType.Namespace.StartsWith("ILInspector.", StringComparison.Ordinal))
            .Select(CalleeKey));

        // Edges run callee -> caller, so the walk below is a reverse reachability from the
        // openers. An edge is recorded only when the callee resolves to a method defined in the
        // product closure; anything else is outside the graph by construction.
        var callersByCallee = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void AddEdge(string calleeKey, string callerKey)
        {
            if (!definedKeys.Contains(calleeKey))
                return;

            if (!callersByCallee.TryGetValue(calleeKey, out var callers))
                callersByCallee[calleeKey] = callers = new HashSet<string>(StringComparer.Ordinal);

            callers.Add(callerKey);
        }

        foreach (var call in calls)
        {
            var callerKey = CallerKey(call.Caller);
            AddEdge(CalleeKey(call.Callee), callerKey);

            // Touching any member of a type can run that type's initializer, and a static
            // constructor has no caller edge of its own: the CLR runs it on first use and no IL
            // instruction references it. Over-approximating here is the safe direction -- a
            // spurious edge can only make this gate redder, never blind.
            AddEdge(
                $"{call.Callee.DeclaringType.Namespace}.{call.Callee.DeclaringType.Name}::.cctor()",
                callerKey);
        }

        // Interface dispatch was the last thing this gate described as "unverified, not closed",
        // and Gemini Pro's review walked straight through it: a `DotnetInspector.Hack.IBodyOpener`
        // interface with a `BodyOpener` implementation calling the already-pinned
        // LeakTriageAnalyzer.AnalyzeAssemblyDetailed. The scanner holds the *interface*, so the IL
        // records `callvirt IBodyOpener::Open` and the reverse walk dead-ends at `BodyOpener::Open`
        // with nothing connecting it back to any section. 366.8 ms -> 954.7 ms, all 175 green.
        //
        // Naming a hole is not closing it. That has now been the wrong call four times out of four
        // on this gate -- callers outside a scanner run, the cross-assembly helper, reflection, and
        // now this -- so the honest reading is that "unverified" here was never a boundary, only an
        // unwritten test.
        //
        // The missing edge is a *type* relationship, and DirectCalls only carries call sites, so it
        // has to come from somewhere else. These assemblies are already loaded above to be walked,
        // so their hierarchy is available directly: for each product type, every product interface
        // it implements and every product base class it derives from yields an edge from the
        // declaring member to the member that overrides or implements it.
        //
        // Direction matters. Edges run callee -> caller, so an implementation is recorded as though
        // the base or interface member *called* it: reaching `BodyOpener::Open` then reaches
        // `IBodyOpener::Open`, and from there its real callers. Feeding the same pairs to the
        // forward walk keeps the two claims consistent.
        //
        // Matching a member by *name* across a hierarchy was the first attempt, and Gemini Pro's
        // fixed-head pass walked through it three separate ways, each for a different reason:
        //
        //   explicit implementation   the compiler names the member `Ns.IFace.Open`, not `Open`
        //   inherited implementation  the member satisfying the slot is declared on a *sibling*
        //                             ancestor the derived type never mentions
        //   generic interface         `IOpener`1::Open` is not a graph node at all -- see below
        //
        // Only the first two are name-matching failures, and `GetInterfaceMap` answers both
        // exactly: it reports the member that actually occupies each interface slot, whatever it
        // is named and wherever it is declared. `GetBaseDefinition` does the same for overrides.
        var hierarchyEdges = new List<(string CallerKey, string CalleeKey)>();
        var unmappableContracts = new List<string>();

        // Reflection reports members; this graph is keyed by IL signatures. Rebuilding an IL
        // parameter display string from a `ParameterInfo` is precisely the silent mismatch that
        // would leave the edge set empty while looking correct, so no key is ever *built* from
        // reflection. Every IL-derived key is instead indexed under a coarse tuple that reflection
        // can also produce -- declaring type, member name, parameter count, generic arity -- and a
        // reflection pair is resolved by looking both ends up in that index. Matching is therefore
        // always IL key against IL key. A coarse tuple can be ambiguous (overloads differing only
        // in parameter *types*); every match is linked, which can only make this gate redder.
        var keysByMember = new Dictionary<(string Type, string Name, int Parameters, int Arity), HashSet<string>>();

        void IndexKey(string typeName, string name, int parameters, int arity, string key)
        {
            if (!keysByMember.TryGetValue((typeName, name, parameters, arity), out var keys))
                keysByMember[(typeName, name, parameters, arity)] = keys = new HashSet<string>(StringComparer.Ordinal);

            keys.Add(key);
        }

        // A type in the global namespace has an empty `Namespace`, and interpolating
        // `{Namespace}.{Name}` unconditionally would spell it `.GlobalBodyOpener` while reflection
        // spells it `GlobalBodyOpener`. Gemini Pro's review used exactly that to make *both* edge
        // mechanisms inert at once -- neither the implementation nor the constructors resolve, so
        // there is nothing to be over-approximate about. Both sides go through this one helper.
        static string CoarseTypeName(ILInspector.Analysis.TypeRef type)
            => type.Namespace.Length == 0 ? type.Name : $"{type.Namespace}.{type.Name}";

        foreach (var call in calls)
        {
            IndexKey(
                CoarseTypeName(call.Caller.DeclaringType),
                call.Caller.Name,
                call.Caller.ParameterTypes.Count(),
                call.Caller.GenericArity,
                CallerKey(call.Caller));

            var callee = call.Callee;
            if (callee.DeclaringType.Namespace.StartsWith("DotnetInspector.", StringComparison.Ordinal)
                || callee.DeclaringType.Namespace.StartsWith("ILInspector.", StringComparison.Ordinal))
            {
                IndexKey(
                    CoarseTypeName(callee.DeclaringType),
                    callee.Name,
                    callee.OpenSignatureParameters.Count(),
                    callee.GenericArity,
                    CalleeKey(callee));
            }
        }

        static string? ReflectionTypeName(Type? type)
            => type is null
                ? null
                : (type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type).FullName;

        IReadOnlyCollection<string> ResolveKeys(System.Reflection.MethodBase member)
        {
            if (ReflectionTypeName(member.DeclaringType) is not { } typeName)
                return [];

            var arity = member.IsGenericMethodDefinition ? member.GetGenericArguments().Length : 0;
            return keysByMember.TryGetValue(
                (typeName, member.Name, member.GetParameters().Length, arity), out var keys)
                ? keys
                : [];
        }

        const System.Reflection.BindingFlags DeclaredMembers =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly;

        var resolvedExplicit = 0;
        var resolvedInherited = 0;
        var resolvedGeneric = 0;
        var reflectionTypeNames = new HashSet<string>(StringComparer.Ordinal);

        // Edges run callee -> caller, so an implementation is recorded as though its ancestor
        // *called* it: reaching `BodyOpener::Open` then reaches `IBodyOpener::Open`, and from there
        // its real callers. The same pairs feed the forward walk so both claims stay consistent.
        void Link(
            IReadOnlyCollection<string> constructorKeys,
            System.Reflection.MethodInfo ancestor,
            System.Reflection.MethodInfo implementation)
        {
            var implementationKeys = ResolveKeys(implementation);
            if (implementationKeys.Count == 0)
                return;

            if (implementation.Name.Contains('.', StringComparison.Ordinal))
                resolvedExplicit++;
            if (implementation.DeclaringType != ancestor.DeclaringType
                && ancestor.DeclaringType?.IsInterface is true
                && implementation.GetBaseDefinition() == implementation)
            {
                resolvedInherited++;
            }

            var ancestorKeys = ResolveKeys(ancestor);
            if (ancestorKeys.Count > 0 && ancestor.DeclaringType?.IsConstructedGenericType is true)
                resolvedGeneric++;

            // Witness that this mechanism is load-bearing: a `static abstract` interface member
            // dispatched through a generic constraint. Nothing is ever constructed, so the
            // construction edges below cannot see it; removing this loop turns that tamper green.
            foreach (var ancestorKey in ancestorKeys)
            {
                foreach (var implementationKey in implementationKeys)
                    hierarchyEdges.Add((ancestorKey, implementationKey));
            }

            // The ancestor edge alone is not enough, and BCL dispatch is why. When a section hands
            // an object to the BCL to invoke -- `list.Sort(new Comparer())` -- no IL instruction in
            // this product ever names `IComparer<string>::Compare`, so the implementation has no
            // incoming call edge and the ancestor is not a node either. Construction is the
            // independent second link: an instance member cannot be dispatched to without an
            // instance, and the `newobj` that creates one is a recorded edge. Attributing to the
            // *concrete* type's constructors is also what covers an implementation inherited from a
            // base class, since it is the derived type that gets constructed.
            //
            // Witness that this mechanism is load-bearing: a type implementing a BCL interface that
            // only the BCL dispatches. Removing this loop turns that tamper green while the others
            // stay red, so the two mechanisms overlap on most shapes but neither subsumes the other.
            //
            // The previously recorded witness here was the constructed-generic interface, and it is
            // no longer valid: once TypeRef.GenericInstance carried a real name, `IOpener`1::Open`
            // became a genuine node with a real incoming edge, and the ancestor mechanism catches
            // that shape on its own. Re-deriving the witness rather than trusting the comment is
            // what exposed the BCL-contract filter that had been disabling this loop.
            if (implementation.IsStatic)
                return;

            foreach (var constructorKey in constructorKeys)
            {
                foreach (var implementationKey in implementationKeys)
                    hierarchyEdges.Add((constructorKey, implementationKey));
            }
        }

        foreach (var assembly in productAssemblies.Values)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException loadFailure)
            {
                types = loadFailure.Types.Where(type => type is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type.FullName is { } reflectionName)
                    reflectionTypeNames.Add(reflectionName);

                if (type.IsInterface)
                    continue;

                var constructorKeys = type
                    .GetConstructors(DeclaredMembers)
                    .SelectMany(ResolveKeys)
                    .ToHashSet(StringComparer.Ordinal);

                // No product-assembly filter on the contract. There was one, and it silently
                // disabled the construction edges below in exactly the case where they are the
                // only possible link: a type that implements a *BCL* interface and is handed to
                // the BCL to dispatch -- `list.Sort(new Comparer())`. No IL instruction in this
                // product ever names IComparer<string>::Compare, so the implementation has no
                // incoming edge, and skipping the contract meant it got no constructor edge
                // either. Filtering the ancestor was pointless as well as harmful: a BCL member is
                // not in the IL index, so ResolveKeys returns empty and the ancestor loop below
                // adds nothing for it regardless.
                foreach (var contract in type.GetInterfaces())
                {
                    System.Reflection.InterfaceMapping map;
                    try
                    {
                        map = type.GetInterfaceMap(contract);
                    }
                    catch (ArgumentException)
                    {
                        // A swallowed mapping failure is a silently disabled mechanism: this type's
                        // interface edges just vanish, and the gate stays green while covering less
                        // than it claims. That is the shape of routes 16 and 17, so the skip is
                        // recorded and asserted empty below rather than left to be discovered by
                        // the next reviewer. Gemini Pro attacked precisely this swallow at
                        // `7cb65eec`, reasoning that `Assembly.GetTypes()` hands back open generic
                        // definitions and `GetInterfaceMap` rejects those. The premise is wrong on
                        // this runtime -- the mapping succeeds and the tamper is caught -- but the
                        // attack was correct that nothing would have told us if it had thrown.
                        unmappableContracts.Add($"{type.FullName} : {contract.FullName}");
                        continue;
                    }
                    catch (InvalidOperationException)
                    {
                        unmappableContracts.Add($"{type.FullName} : {contract.FullName}");
                        continue;
                    }

                    for (var slot = 0; slot < map.InterfaceMethods.Length; slot++)
                        Link(constructorKeys, map.InterfaceMethods[slot], map.TargetMethods[slot]);
                }

                foreach (var method in type.GetMethods(DeclaredMembers))
                {
                    if (!method.IsVirtual)
                        continue;

                    // No product-assembly filter on the base definition either, and for the same
                    // reason as the contract loop above. An override of a *BCL* virtual that only
                    // the BCL invokes -- `ToString` reached through string.Format, `Equals` through
                    // a dictionary lookup, `Stream.Read` through a copy helper -- is named by no
                    // product IL instruction at all. Filtering it here skipped its construction
                    // edge and left the override unreachable from the section that built it.
                    // Resolving a BCL ancestor still yields no keys, so the ancestor loop adds
                    // nothing for it; only the construction edge is gained.
                    var baseDefinition = method.GetBaseDefinition();
                    if (baseDefinition != method)
                        Link(constructorKeys, baseDefinition, method);
                }
            }
        }

        // A contract whose mapping could not be resolved contributes no edges, so if this list is
        // ever non-empty the mechanism above is quietly covering less than the rest of this test
        // assumes. Asserting it empty is what stops that from being invisible; if a legitimate
        // shape ever lands here, pin it deliberately rather than restoring the bare `continue`.
        Assert.Empty(unmappableContracts);

        // Non-vacuity, and `Assert.NotEmpty` is not enough for it. Gemini Pro's review made exactly
        // that point: the product contains plenty of ordinary interface implementations, so the
        // edge list stays non-empty even when the shapes that defeated the previous version resolve
        // to nothing. Each of the three is therefore counted separately against real product code.
        // These are floors on shapes, not on a churning literal.
        Assert.NotEmpty(hierarchyEdges);
        Assert.True(resolvedExplicit > 0, "No explicit interface implementation resolved to an IL key.");
        Assert.True(resolvedInherited > 0, "No inherited interface implementation resolved to an IL key.");
        Assert.True(resolvedGeneric > 0, "No constructed-generic interface slot resolved to an IL key.");

        // The shape floors above are still not enough, and Gemini Pro said so before demonstrating
        // it: they are satisfied by incidental product code, so they cannot protect a *new* type
        // whose name fails to resolve. The global-namespace escape was precisely that -- every
        // floor stayed green while the attacking type resolved to nothing.
        //
        // The property that actually matters is population-wide: reflection and the IL walk must
        // agree on how to spell *every* type, not on three sampled shapes. Any coarse name in the
        // IL index that reflection cannot produce is a type the hierarchy mechanisms are blind to,
        // whether or not anyone has thought of the shape that produces it.
        var unresolvableTypes = keysByMember.Keys
            .Select(member => member.Type)
            .Distinct(StringComparer.Ordinal)
            .Where(type => !reflectionTypeNames.Contains(type))
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();

        Assert.Equal([], unresolvableTypes);

        foreach (var (callerKey, calleeKey) in hierarchyEdges)
            AddEdge(calleeKey, callerKey);

        // A compiler-generated closure or state machine is *constructed* by the method it was
        // generated for -- `newobj ResearchDiff+<BodyIndexEntries>d__26::.ctor(int32)` -- and that
        // newobj is a recorded edge. What is missing is the link from the generated type's own
        // members back to that constructor: an iterator's MoveNext is driven by the runtime
        // through an interface, so no IL instruction connects the two. The walk could therefore
        // see who builds a state machine and never reach the body that does the work.
        //
        // This is not hypothetical. GPT's ResearchDiff.CompareAssemblies exploit survived the
        // product-closure fix on exactly this hole: the opener is
        // `ResearchDiff+<BodyIndexEntries>d__26::MoveNext()`, and without this edge no scanner
        // that calls CompareAssemblies is reachable from it.
        //
        // Attribution goes through the constructor rather than through the origin name the
        // compiler encodes in `<Origin>d__N` / `<Origin>b__N`, because a name carries no
        // signature and so collapses overloads. ResourceLifecycleAnalysis is the live case:
        // InspectAssembly(string, FindingSubject) opens an index through a display-class lambda,
        // while InspectAssembly(Func<LibraryBodyIndex>, FindingSubject) -- the overload
        // ScanResourceTriage actually calls -- opens nothing. Name-keyed attribution reports the
        // sanctioned path as a violation and would need an allow list, which is precisely where a
        // real escape would hide. The constructor is signature-exact and IL-grounded, and lambdas
        // need no special case at all: `ldftn` is already a recorded edge.
        static bool IsCompilerGenerated(ILInspector.Analysis.TypeRef type)
        {
            var name = type.Name;
            var nested = name.LastIndexOf('+');
            return nested >= 0 && nested + 1 < name.Length && name[nested + 1] == '<';
        }

        var constructorsByType = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var caller in calls.Select(call => call.Caller))
        {
            if (caller.Name != ".ctor" || !IsCompilerGenerated(caller.DeclaringType))
                continue;

            var owner = $"{caller.DeclaringType.Namespace}.{caller.DeclaringType.Name}";
            if (!constructorsByType.TryGetValue(owner, out var constructors))
                constructorsByType[owner] = constructors = new HashSet<string>(StringComparer.Ordinal);

            constructors.Add(CallerKey(caller));
        }

        foreach (var generated in calls.Select(call => call.Caller).DistinctBy(CallerKey))
        {
            if (generated.Name is ".ctor" or ".cctor"
                || !constructorsByType.TryGetValue(
                    $"{generated.DeclaringType.Namespace}.{generated.DeclaringType.Name}",
                    out var constructors))
                continue;

            foreach (var constructor in constructors)
                AddEdge(CallerKey(generated), constructor);
        }

        // That edge needs the generated type to *have* a constructor, and a struct-based async
        // state machine has none -- it is a local the origin never allocates, started through
        // `AsyncTaskMethodBuilder.Start<TStateMachine>(ref …)`, so no IL edge exists to attach to.
        //
        // On this toolchain the case does not arise: async methods compile with no state machine
        // type at all, so the call stays in the method body where the walk already sees it, and
        // every generated type declaring a `MoveNext` is a class-based iterator with a `.ctor`.
        // Measured, the only generated types here without a constructor are the two static `<G>$`
        // data holders, whose members are `call`ed directly and need no attribution.
        //
        // That is a property of the compiler, not of this test, so it is asserted rather than
        // assumed. If a future toolchain emits a constructor-less state machine, the attribution
        // edge would silently stop covering it -- the exact failure mode that let five earlier
        // versions of this gate look closed -- and this is the assertion that says so.
        var stateMachines = calls
            .Select(call => call.Caller)
            .Where(method => method.Name == "MoveNext" && IsCompilerGenerated(method.DeclaringType))
            .Select(method => $"{method.DeclaringType.Namespace}.{method.DeclaringType.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var stateMachinesMissingAConstructor = stateMachines
            .Where(type => !constructorsByType.ContainsKey(type))
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(stateMachinesMissingAConstructor);

        // And the check above must have something to check.
        Assert.NotEmpty(stateMachines);

        var openerKeys = calls
            .Where(OpensAnIndex)
            .Select(call => CallerKey(call.Caller))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // PRIMARY CLAIM: the set of methods that open a body index is pinned, across both
        // assemblies and keyed by signature.
        //
        // This is deliberately not a reachability argument, because reachability rests on the call
        // graph being complete and it is not. The GPT review of #3626 escaped an earlier walk by
        // calling a helper on a *constructed generic type* (Helper<int>.Open): that call goes
        // through a MemberRef on a TypeSpec, and DirectCalls records no edge for it whatsoever --
        // not a wrong edge, no edge -- so no backwards walk from an opener can ever find its
        // caller. A static constructor is invisible for a different reason: the CLR runs a type
        // initializer on first use and no IL instruction references it.
        //
        // Pinning the openers sidesteps both. Every escape of that kind has to *add an opener*
        // somewhere in one of these assemblies, and an opener is visible no matter how it is
        // reached, so the enforcement does not depend on knowing who calls it.
        Assert.Equal(
            [
                "DotnetInspector.Commands.DiffCommand::AcquireAuthoredSourceInspectionsAsync(IReadOnlyList<string>,IReadOnlyDictionary<string, ResearchSubjectKey>,DiffOptions,bool,HttpClient,VerboseLogger)",
                "DotnetInspector.Commands.TimelineCommand::InspectAnalysisAssemblies`1(IReadOnlyList<string>,string,string,bool,FindingDescriptor,FindingSubject,Func<LibraryBodyIndex, int, FindingSubject, FindingInspection<T>>)",
                "DotnetInspector.Inspectors.MethodBodyInspectionSession::OpenWithFeatures(string,LibraryBodyAnalysisFeatures,IAssemblyReferenceResolver,IReadOnlySet<int>,Func<TypeRef, bool>)",
                "DotnetInspector.Inspectors.MethodBodyInspectionSession::OpenWithPrefetchedImage(string,PdbContext,LibraryBodyAnalysisFeatures,IAssemblyReferenceResolver)",
                "ILInspector.Analysis.LeakTriageAnalyzer::AnalyzeAssemblyDetailed(string)",
                "ILInspector.Analysis.LibraryBodyIndex::Open(string)",
                "ILInspector.Analysis.LibraryBodyIndex::Open(string,IAssemblyReferenceResolver,bool,bool,IReadOnlySet<int>,Func<TypeRef, bool>)",
                "ILInspector.Analysis.ResourceLifecycleAnalysis+<>c__DisplayClass0_0::<InspectAssembly>b__0()",
                "ILInspector.Research.AnalysisIndexCache::ForPath(string)",
                "ILInspector.Research.ResearchDiff+<BodyIndexEntries>d__26::MoveNext()",
            ],
            openerKeys);

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (var opener in openerKeys)
        {
            if (opener != BodyIndexGate && opener != DrillMapGate && reached.Add(opener))
                pending.Enqueue(opener);
        }

        var gatesOnAPath = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            if (!callersByCallee.TryGetValue(pending.Dequeue(), out var callers))
                continue;

            foreach (var caller in callers)
            {
                // Stop at the gated accessor rather than walking past it. Its callers reach the
                // body index only by going through RequireUnboundedDeclaration, which is the
                // whole point of the mechanism.
                if (caller == BodyIndexGate || caller == DrillMapGate)
                    gatesOnAPath.Add(caller);
                else if (reached.Add(caller))
                    pending.Enqueue(caller);
            }
        }

        var ungatedSectionCallers = reached
            .Where(key => key.StartsWith("DotnetInspector.Sections", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        // SECONDARY CLAIM: no section reaches one of those sanctioned openers except through the
        // gated accessor. The pinned set cannot catch this case, because a scanner calling
        // MethodBodyInspectionSession.OpenWithFeatures -- or LeakTriageAnalyzer.AnalyzeAssembly --
        // adds no new opener. Neither claim subsumes the other, which is why both are kept.
        //
        // Cutting at the accessor is what makes this precise: without the cut the walk reports
        // legitimate Sections members (the accessor itself, the Unbounded scanner lambdas that use
        // it, and their enclosing factory), and excusing those would require an allow list.
        Assert.Empty(ungatedSectionCallers);

        // Non-vacuity assertions, because every gate in this PR that lacked one turned out to be
        // asserting nothing at all.

        // The walk must be able to see openers, or Assert.Empty passes by finding nothing.
        Assert.NotEmpty(openerKeys);

        // The closure must actually be a closure. If it collapsed to the CLI alone, or quietly
        // stopped at one hop, the cross-assembly claim would evaporate while everything else still
        // passed. ILInspector.Research is the useful witness: nothing in the CLI names it in this
        // test, it is reached only by following references, and it owns two of the pinned openers.
        Assert.Contains("ILInspector.Analysis", productAssemblies.Keys);
        Assert.Contains("ILInspector.Research", productAssemblies.Keys);
        Assert.Contains(
            openerKeys,
            key => key.StartsWith("ILInspector.Research.", StringComparison.Ordinal));

        // And it must stay a *product* closure. DotnetInspector.Fixtures sits in the same output
        // directory and matches the same name prefix, so a directory scan would sweep it in and
        // let test-support code pad the pinned set. It is absent because the CLI does not
        // reference it, which is the property worth pinning rather than an exclusion list.
        Assert.DoesNotContain("DotnetInspector.Fixtures", productAssemblies.Keys);

        // Both gated members must still exist under these exact signatures. A rename or an added
        // parameter would silently stop matching, turning the cut into a no-op -- which happens to
        // fail safe here, but would leave the reasoning above describing a mechanism that is no
        // longer wired to anything.
        Assert.Contains(BodyIndexGate, definedKeys);
        Assert.Contains(DrillMapGate, definedKeys);

        // And the cut must be load-bearing: the accessor has to actually sit on a path to an
        // opener. If it stopped reaching one, cutting there would prove nothing about Sections.
        Assert.NotEmpty(gatesOnAPath);

        // Reflection was the one route this gate named as out of scope, and MAI-Code's review
        // walked through it twice. First directly -- `typeof(LibraryBodyIndex).GetMethod("Open")
        // .Invoke(...)` from a NetworkFree scanner, a measured 290.2 ms with both claims green.
        // Then, when that was pinned within DotnetInspector.Sections, through a one-line helper in
        // ILInspector.Analysis that did the reflecting on the scanner's behalf, for 493.4 ms.
        //
        // No IL walk can follow a reflective call, so the obvious reading is that this condemns
        // every static gate equally and has to stay unverified. But the escape is only unreachable
        // *evidence* if the question is "where does this call go". Asked the other way -- "can
        // section code reach a reflection API at all" -- the category is enumerable and visible in
        // the very same graph.
        //
        // The second escape is why this walks forward from Sections over the whole product closure
        // rather than checking Sections alone: pinning the assembly a reviewer just used is the
        // mistake this gate already made once, with assemblies.
        //
        // System.Reflection.Metadata is excluded because it is not runtime reflection -- it is the
        // library this entire product is built on, and including it drowns the signal in 1,312
        // sites. Type::GetTypeFromHandle is excluded because that is `typeof`.
        // `Activator` is a *materialization* primitive: it produces an instance of an arbitrary
        // type without the product naming a constructor, which is exactly what defeats the
        // construction edges the hierarchy walk relies on. It is denied for that reason. Other
        // primitives with the same capability are handled by `IsMaterializationPrimitive` below,
        // which has to pin call sites rather than members because the product uses some of them.
        static bool IsRuntimeReflection(ILInspector.Analysis.MemberRef callee)
        {
            var namespaceName = callee.DeclaringType.Namespace;
            if (namespaceName.StartsWith("System.Reflection.Metadata", StringComparison.Ordinal)
                || namespaceName.StartsWith("System.Reflection.PortableExecutable", StringComparison.Ordinal))
                return false;

            return callee.DeclaringType.ToDisplayString() switch
            {
                "System.Activator" or "Activator" => true,
                "System.Type" or "Type" => callee.Name != "GetTypeFromHandle",
                _ => namespaceName == "System.Reflection",
            };
        }

        // Materialization and typing primitives hand back a reference of a type that no `newobj`
        // in this product ever created. The type's members therefore get no construction edge, and
        // if the section then dispatches through a *BCL* interface there is no ancestor edge
        // either, because a BCL member resolves to no IL keys -- both halves of the hierarchy net
        // fail at once and the implementation is an island. Gemini Pro found two such routes at
        // `3abb2a96`: `RuntimeHelpers.GetUninitializedObject` + `IDisposable`, and
        // `Unsafe.As<T>(object)` + `IDisposable`.
        //
        // The search for further primitives is bounded, which is the only reason this is a fix and
        // not a patch: a primitive can only be *called* on a type in the pinned BCL surface below,
        // and anything else adds an entry there and fails that pin. Auditing those types for "can
        // hand back an instance of a product type the product never constructed" yields exactly
        // `Activator` (denied above), `RuntimeHelpers`, `Unsafe`, and the `*Marshal` casts.
        static bool IsMaterializationPrimitive(ILInspector.Analysis.MemberRef callee)
            => callee.DeclaringType.ToDisplayString() switch
            {
                "System.Runtime.CompilerServices.RuntimeHelpers" or "RuntimeHelpers"
                    => callee.Name == "GetUninitializedObject",
                "System.Runtime.CompilerServices.Unsafe" or "Unsafe"
                    => callee.Name is "As" or "AsRef" or "BitCast",
                "System.Runtime.InteropServices.MemoryMarshal" or "MemoryMarshal"
                    => callee.Name is "AsRef" or "Cast",
                _ => false,
            };

        var forwardCalls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            var calleeKey = CalleeKey(call.Callee);
            if (!definedKeys.Contains(calleeKey))
                continue;

            var callerKey = CallerKey(call.Caller);
            if (!forwardCalls.TryGetValue(callerKey, out var callees))
                forwardCalls[callerKey] = callees = new HashSet<string>(StringComparer.Ordinal);

            callees.Add(calleeKey);
        }

        foreach (var (callerKey, calleeKey) in hierarchyEdges)
        {
            if (!definedKeys.Contains(calleeKey))
                continue;

            if (!forwardCalls.TryGetValue(callerKey, out var callees))
                forwardCalls[callerKey] = callees = new HashSet<string>(StringComparer.Ordinal);

            callees.Add(calleeKey);
        }

        var reachableFromSections = new HashSet<string>(StringComparer.Ordinal);
        var forwardPending = new Queue<string>();
        foreach (var key in definedKeys.Where(key =>
            key.StartsWith("DotnetInspector.Sections", StringComparison.Ordinal)))
        {
            if (reachableFromSections.Add(key))
                forwardPending.Enqueue(key);
        }

        while (forwardPending.Count > 0)
        {
            if (!forwardCalls.TryGetValue(forwardPending.Dequeue(), out var callees))
                continue;

            foreach (var callee in callees)
            {
                if (reachableFromSections.Add(callee))
                    forwardPending.Enqueue(callee);
            }
        }

        // The *API surface* is pinned rather than the call sites. Measured, 2,070 methods are
        // reachable from Sections and they touch exactly two reflection members, both benign:
        // Type::op_Equality is what a record's generated Equals uses, and MemberInfo::get_Name
        // names a member in a diagnostic. Pinning the surface keeps this stable when a record is
        // added -- which would churn a call-site list and train the next reader to update the
        // literal without thinking -- while still going red the moment anything calls GetMethod,
        // Invoke, or CreateInstance.
        var reflectionSurface = calls
            .Where(call => reachableFromSections.Contains(CallerKey(call.Caller)))
            .Where(call => IsRuntimeReflection(call.Callee))
            .Select(call => $"{call.Callee.DeclaringType.ToDisplayString()}::{call.Callee.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["MemberInfo::get_Name", "Type::op_Equality"], reflectionSurface);

        // Pinned by *call site*, not by member, and that difference is the whole point. The
        // product does use `Unsafe.As`/`AsRef` — but only from three compiler-generated
        // `<PrivateImplementationDetails>` inline-array helpers, with no hand-written call
        // anywhere in the section-reachable closure. Pinning the member names would therefore
        // have admitted every future use and reopened the route; pinning the call sites keeps the
        // compiler's uses working while turning any new caller red.
        var materializationSurface = calls
            .Where(call => reachableFromSections.Contains(CallerKey(call.Caller)))
            .Where(call => IsMaterializationPrimitive(call.Callee))
            .Select(call => $"{CallerKey(call.Caller)} -> {call.Callee.DeclaringType.ToDisplayString()}::{call.Callee.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                ".<PrivateImplementationDetails>::InlineArrayAsReadOnlySpan`2(ref TBuffer,int) -> Unsafe::As",
                ".<PrivateImplementationDetails>::InlineArrayAsReadOnlySpan`2(ref TBuffer,int) -> Unsafe::AsRef",
                ".<PrivateImplementationDetails>::InlineArrayElementRef`2(ref TBuffer,int) -> Unsafe::As",
            ],
            materializationSurface);

        // Reflection is not the only late-binding mechanism. Gemini Pro's review raised `dynamic`,
        // which the compiler lowers into the DLR so the IL names Microsoft.CSharp.RuntimeBinder
        // rather than anything under System.Reflection. That one is unreachable -- `dynamic` does
        // not compile anywhere in this product, because src/Directory.Build.props sets
        // IsAotCompatible plus TreatWarningsAsErrors for every project under src/, making a dynamic
        // call site build errors IL2026 and IL3050 ("the 'dynamic' feature requires runtime-code
        // generation, which is incompatible with AOT"). Verified by writing one in the CLI and
        // again in ILInspector.Analysis. That is the gate that claim rests on, and it is a stronger
        // one than a test.
        //
        // The review then landed the same idea somewhere the build does not object:
        // `Delegate.CreateDelegate(typeof(Func<string, LibraryBodyIndex>), typeof(LibraryBodyIndex),
        // "Open")`. It names the target with a string, so there is no ldftn for the walk to follow;
        // its declaring type is System.Delegate, so the reflection predicate below never sees it;
        // and this overload carries no trimming annotation, so it compiles clean. Measured 2.13 s
        // -> 6.35 s on mscorlib.dll with all 175 tests in this class green.
        //
        // The instructive part is *why* that worked. The predicate below special-cases Type and
        // Activator inside the otherwise-allowed System namespace -- which makes it a deny list in
        // the one namespace that holds the most dangerous primitives, and a deny list is exactly
        // where the next mechanism hides. Delegate was simply the entry I did not think of. This
        // PR has now been wrong about "that is the last route" eleven times, so the fix is not to
        // add Delegate; it is to stop enumerating.
        //
        // So the allowed BCL *type* surface is pinned, and anything new fails closed: Delegate,
        // Activator, Marshal, CallSite, Assembly, and AppDomain are all absent from it and cannot
        // be reached without changing this literal.
        //
        // Granularity is chosen by measurement, not taste. System.Reflection.Metadata and
        // System.Reflection.PortableExecutable are excluded and left at namespace granularity: they
        // are the substrate this entire product reads metadata with, they account for 81 of the 146
        // reachable BCL types, they churn with every new metadata table touched, and they contain
        // no invocation primitive. Excluding them turns an unreviewable list into 65 stable entries
        // -- primitives, exceptions, collections, and helpers -- where a genuinely new BCL type is
        // worth the one line of review it costs. Product namespaces are excluded for the same
        // churn reason, at no cost in coverage: a product helper's own BCL calls are already inside
        // this same closure, which is what the previous route proved.
        //
        // This literal grew from 65 to 99 entries across three fixes in this PR, and every addition
        // is a generic collection, delegate type, or value type -- List, Dictionary, Span, Func,
        // ValueTuple, Guid, HashCode. Nothing was removed. That is worth stating plainly: until
        // TypeRef.GenericInstance carried a real name, *every* call whose declaring type was a
        // constructed generic resolved to a nameless type and fell out of this projection, and
        // until the BCL-contract filter came off the interface loop, a type dispatched only by the
        // BCL contributed no edges at all. So this pin -- and the reflection surface pin, and the
        // reverse walk -- were all reasoning over a closure with two systematic holes in it. The
        // gate's own evidence was resting on the defects it exists to catch. A dangerous primitive
        // reached through a constructed generic (say a Lazy<LibraryBodyIndex>) or through a BCL
        // callback would have been invisible to all three claims at once.
        // The projection is to the *open definition*, so Dictionary<int, Foo> and
        // Dictionary<string, Bar> are one entry. That is the granularity the claim is about -- an
        // allowed type surface, not an instantiation surface -- and without it this list is 571
        // entries of per-instantiation noise instead of 97 reviewable types.
        var bclTypeSurface = calls
            .Where(call => reachableFromSections.Contains(CallerKey(call.Caller)))
            .Select(call => OpenDefinitionOf(call.Callee.DeclaringType))
            .Where(type => type.Namespace.Length > 0
                && !type.Namespace.StartsWith("DotnetInspector.", StringComparison.Ordinal)
                && !type.Namespace.StartsWith("ILInspector.", StringComparison.Ordinal)
                && !type.Namespace.StartsWith("Markout", StringComparison.Ordinal)
                && !type.Namespace.StartsWith("System.Reflection.Metadata", StringComparison.Ordinal)
                && !type.Namespace.StartsWith("System.Reflection.PortableExecutable", StringComparison.Ordinal))
            .Select(type => $"{type.Namespace}.{type.ToDisplayString()}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            [
                "System.Action",
                "System.ArgumentException",
                "System.ArgumentNullException",
                "System.ArgumentOutOfRangeException",
                "System.Array",
                "System.BadImageFormatException",
                "System.Buffers.Binary.BinaryPrimitives",
                "System.Collections.Generic.CollectionExtensions",
                "System.Collections.Generic.Comparer",
                "System.Collections.Generic.Dictionary",
                "System.Collections.Generic.Dictionary.Enumerator",
                "System.Collections.Generic.Dictionary.KeyCollection",
                "System.Collections.Generic.Dictionary.KeyCollection.Enumerator",
                "System.Collections.Generic.Dictionary.ValueCollection",
                "System.Collections.Generic.Dictionary.ValueCollection.Enumerator",
                "System.Collections.Generic.EqualityComparer",
                "System.Collections.Generic.HashSet",
                "System.Collections.Generic.HashSet.Enumerator",
                "System.Collections.Generic.IEnumerable",
                "System.Collections.Generic.IEnumerator",
                "System.Collections.Generic.IReadOnlyCollection",
                "System.Collections.Generic.IReadOnlyDictionary",
                "System.Collections.Generic.IReadOnlyList",
                "System.Collections.Generic.IReadOnlySet",
                "System.Collections.Generic.KeyValuePair",
                "System.Collections.Generic.List",
                "System.Collections.Generic.List.Enumerator",
                "System.Collections.Generic.Queue",
                "System.Collections.Generic.SortedSet",
                "System.Collections.Generic.Stack",
                "System.Collections.IEnumerator",
                "System.Collections.Immutable.ImmutableArray",
                "System.Collections.Immutable.ImmutableArray.Builder",
                "System.Collections.Immutable.ImmutableArray.Enumerator",
                "System.Collections.Immutable.ImmutableDictionary",
                "System.Comparison",
                "System.Console",
                "System.Convert",
                "System.Diagnostics.Stopwatch",
                "System.Enum",
                "System.Environment",
                "System.Exception",
                "System.Func",
                "System.Globalization.CultureInfo",
                "System.Guid",
                "System.HashCode",
                "System.IDisposable",
                "System.IO.File",
                "System.IO.Path",
                "System.IO.Stream",
                "System.IO.TextWriter",
                "System.Index",
                "System.InvalidOperationException",
                "System.InvalidProgramException",
                "System.Linq.Enumerable",
                "System.Linq.IGrouping",
                "System.Linq.ImmutableArrayExtensions",
                "System.Math",
                "System.MemoryExtensions",
                "System.NotSupportedException",
                "System.Nullable",
                "System.ObjectDisposedException",
                "System.Predicate",
                "System.Range",
                "System.ReadOnlySpan",
                "System.Reflection.MemberInfo",
                "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
                "System.Runtime.CompilerServices.RuntimeHelpers",
                "System.Runtime.CompilerServices.Unsafe",
                "System.Runtime.InteropServices.CollectionsMarshal",
                "System.Runtime.InteropServices.ImmutableCollectionsMarshal",
                "System.Runtime.InteropServices.MemoryMarshal",
                "System.Security.Cryptography.SHA1",
                "System.Security.Cryptography.SHA256",
                "System.Span",
                "System.StringComparer",
                "System.Text.Encoding",
                "System.Text.StringBuilder",
                "System.Text.StringBuilder.AppendInterpolatedStringHandler",
                "System.Threading.Interlocked",
                "System.Threading.Tasks.Parallel",
                "System.TimeSpan",
                "System.Type",
                "System.ValueTuple",
                "System.bool",
                "System.byte",
                "System.char",
                "System.decimal",
                "System.double",
                "System.float",
                "System.int",
                "System.long",
                "System.object",
                "System.sbyte",
                "System.short",
                "System.string",
                "System.uint",
                "System.ulong",
                "System.ushort",
            ],
            bclTypeSurface);

        // Non-vacuity: the forward walk must actually leave DotnetInspector.Sections, or the pin
        // above only describes one assembly and the transitive-helper escape reopens.
        Assert.Contains(
            reachableFromSections,
            key => key.StartsWith("ILInspector.Analysis.", StringComparison.Ordinal));

        // Boundary, stated rather than implied -- and this comment has now been wrong about its
        // own boundary four times, so it is worth saying what the pattern was. Every item this
        // gate has described as "unverified, not closed" has later turned out to be reachable and
        // closable: the unscoped caller, the cross-assembly helper, reflection, and interface
        // dispatch. In each case "unverified" described a limit of the *walk* and was then read as
        // a limit of the gate. Naming a hole is not closing it, and on this defect it has not once
        // been the end of the story.
        //
        // Interface and virtual dispatch came off the list via hierarchy edges above; reflection
        // came off it not by making the walk follow a reflective call, which is impossible, but by
        // pinning the far smaller BCL type and reflection surfaces section code may touch at all.
        // The assembly stopped being a boundary earlier still: a helper anywhere in the product is
        // in scope.
        //
        // What remains genuinely outside is dispatch through a type the product reference closure
        // does not contain -- an interface implemented only by an assembly loaded at runtime. That
        // is bounded by the product's own constraints rather than by this test: `dynamic` does not
        // compile here (IL2026/IL3050 under IsAotCompatible), and the type pin above admits no
        // loader API. Treat it as unverified, and expect that reading to be wrong too.
        //
        // What is *not* an open edge any more is the unscoped caller. An earlier revision of this
        // comment said the cut asserts only that sections route through the accessor, and that a
        // Func captured during a scan and invoked later -- while rendering, when no scanner is
        // running -- would pass the check. That was true of the permissive gate, and GPT reached
        // it from a descriptor's CanRender predicate for 5.2 s of undeclared work.
        // RequireUnboundedDeclaration now refuses outright when no scanner is running, so the
        // deferred call fails instead of passing; UnscopedCallers_AreRefusedTheBodyIndex and
        // ScannerDeclaration_DoesNotOutliveTheRun pin both halves of that behaviour.
    }

    [Fact]
    public void PrerequisiteCost_CannotShiftAfterSectionsSnapshotIt()
    {
        // GPT's re-review asked whether the re-registration guard reaches one level down: can
        // CostOf's max-over-closure change for a key whose own registration never moved, by
        // raising one of its *prerequisites* after the fact? That is the same defect displaced,
        // and the guard on Add would not obviously cover it.
        //
        // It does, but only in combination with the existing unregistered-prerequisite throw, so
        // both halves are pinned here rather than left to be re-derived.
        var registry = new ScannerRegistry();
        registry.Add("Prereq", SectionCost.NetworkFree, _ => { });
        registry.Add("Consumer", SectionCost.NetworkFree, _ => { }, "Prereq");
        Assert.Equal(SectionCost.NetworkFree, registry.CostOf("Consumer"));

        Assert.Throws<InvalidOperationException>(
            () => registry.Add("Prereq", SectionCost.Unbounded, _ => { }));

        // The other way a closure could move is a forward reference: declare a prerequisite that
        // does not exist yet, snapshot the cheap cost, then register the prerequisite expensively.
        // CostOf refuses to answer at all while the prerequisite is missing, so no entry can
        // snapshot a cost that a later registration would invalidate.
        var forward = new ScannerRegistry();
        forward.Add("Early", SectionCost.NetworkFree, _ => { }, "Later");
        Assert.Throws<InvalidOperationException>(() => forward.CostOf("Early"));
    }

    [Fact]
    public void SectionCost_OrdersFromCheapestToMostExpensive()
    {
        // Raised by GPT review of #3626. The raise-only logic and CostOf both compare tiers with
        // `>`, so the entire mechanism silently inverts if the enum members are reordered or a new
        // one is inserted in the middle. Swapping Moderated and Unbounded left the whole suite
        // green, which means nothing was pinning the one property all of it rests on.
        Assert.True(SectionCost.NetworkFree < SectionCost.Moderated);
        Assert.True(SectionCost.Moderated < SectionCost.Unbounded);

        // Enum.GetValues returns members in numeric order, so this also catches a reordering that
        // preserves the names, and forces a new tier to be placed deliberately rather than
        // appended where its numeric rank would be wrong.
        Assert.Equal(
            [SectionCost.NetworkFree, SectionCost.Moderated, SectionCost.Unbounded],
            Enum.GetValues<SectionCost>());
    }

    [Fact]
    public void LibraryScannerCosts_AreDeclaredForEveryRegisteredScanner()
    {
        // Every registered key must resolve. CostOf throws both for an unregistered key and for a
        // real scanner registered without a declared cost, so this walk is what makes those two
        // holes fail here. GPT review of #3626 showed the earlier version of this test was
        // vacuous: with CostOf defaulting to NetworkFree, adding a costless registration overload
        // and routing a scanner through it left the full suite green.
        var registry = LibrarySections.CreateScannerRegistry();

        foreach (var key in registry.RegisteredKeys)
        {
            var cost = registry.CostOf(key);
            Assert.True(
                Enum.IsDefined(cost),
                $"Scanner '{key}' resolved to an undeclared cost value.");
        }

        // The declared tiers must actually discriminate, or the whole mechanism is decoration.
        var costs = registry.RegisteredKeys.Select(registry.CostOf).Distinct().ToList();
        Assert.True(costs.Count > 1, "Every library scanner declares the same cost.");
        Assert.Contains(SectionCost.Unbounded, costs);
    }

    [Fact]
    public void CostOf_ThrowsOnAnUnregisteredScannerKey()
    {
        // Raised by MAI-Code review of #3626. CostOf answered NetworkFree for a key nobody
        // registered, so a stale or misspelled ScannerKey on a section would resolve to the
        // cheapest tier and quietly return that section to the -v:d ladder -- the exact
        // under-declaration this change exists to prevent, arrived at silently.
        //
        // The library pipeline is protected today by
        // LibraryScannerRegistry_RegistrationMatchesDeclaration, but that is a property of one
        // pipeline, not of CostOf, and any pipeline wired with UseScannerCosts depends on it.
        var registry = new ScannerRegistry()
            .Add("real", SectionCost.Unbounded, _ => { });

        var ex = Assert.Throws<InvalidOperationException>(() => registry.CostOf("typo"));
        Assert.Contains("typo", ex.Message, StringComparison.Ordinal);

        // Non-vacuity: a registered key still resolves, including a bundle, which is registered
        // with a null scan function and carries no cost entry of its own.
        var withBundle = new ScannerRegistry()
            .Add("real", SectionCost.Unbounded, _ => { })
            .AddBundle("bundle", "real");

        Assert.Equal(SectionCost.Unbounded, withBundle.CostOf("real"));
        Assert.Equal(SectionCost.Unbounded, withBundle.CostOf("bundle"));
    }

    [Fact]
    public void LibraryScannerPrerequisites_AreAllRegisteredAndAcyclic()    {
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
            .Add("Boom", SectionCost.NetworkFree, _ => throw new InvalidOperationException("boom"));
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
        Assert.Equal(6, pipeline.AllSectionNames.Length);
    }

    [Fact]
    public void ApiTypePipeline_SectionNamesMatchExpected()
    {
        var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
        var names = pipeline.AllSectionNames;

        Assert.Contains(SectionNames.ApiInfo, names);
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
        Assert.Equal(31, pipeline.AllSectionNames.Length);
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

        Assert.Contains("Type Info", names);
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
