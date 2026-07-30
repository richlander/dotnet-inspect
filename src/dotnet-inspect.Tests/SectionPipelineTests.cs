using System.Globalization;
using DotnetInspector.Output;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using ILInspector.Findings;
using ILInspector.Research;
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
            "Library Info", "Symbols", "Signals", "References",
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
                     "Top Leverage", "Unsafe Members", "Dependencies", "SourceLink: Integrity",
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
                // @Metadata table sections are data-gated: a table with no rows in this image is
                // legitimately not discoverable, and listing it would advertise an empty section.
                // Derived from the fixture image rather than hard-coded, so a table that gains or
                // loses rows moves the exclusion with it and every non-empty table stays required.
                .Except(EmptyMetadataSectionsInFixtureImage(), StringComparer.OrdinalIgnoreCase)
            : registered;
        var missing = expected
            .Where(name => !discoverable.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{command} -D missed selectable section(s): {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The <c>@Metadata</c> sections whose table has no rows in the image
    /// <see cref="DiscoverablePipelineCases"/> seeds the library fixture from. These are the only
    /// metadata sections allowed to be absent from discovery.
    /// </summary>
    private static string[] EmptyMetadataSectionsInFixtureImage()
    {
        using var session = AssemblyInspectionSession.Open(typeof(SectionPipelineTests).Assembly.Location);
        var overview = session.MetadataImage();
        if (overview is null)
            return [];

        return [.. overview.Tables
            .Where(table => table.RowCount == 0)
            .Select(table => MetadataSectionNames.ForTable(table.Index))];
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
        // declaring a key nobody honors (its data silently never collected) and a collection step
        // no section asks for (dead code). Derived from the pipeline, the registry, and the shared
        // read rather than restated as a literal list, so adding a section or a scanner cannot
        // drift past this test.
        //
        // The right-hand side is a union because a key is satisfied one of two ways: a registered
        // scanner that runs after the shared metadata read, or the read itself. #3453 asserted
        // equality against the registry alone, which is only sound while no section declares work
        // the read performs; the References section does, so the read declares its keys rather
        // than the test knowing them.
        var registry = LibrarySections.CreateScannerRegistry();
        var pipeline = LibrarySections.CreatePipeline();

        var honored = registry.RegisteredKeys
            .Union(LibraryMetadataService.SharedReadScannerKeys, StringComparer.Ordinal);

        Assert.Equal(
            pipeline.DeclaredScannerKeys.OrderBy(k => k, StringComparer.Ordinal),
            honored.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// Proves the two sources the registration gate unions are disjoint.
    /// </summary>
    /// <remarks>
    /// This is the invariant that makes <see cref="LibraryScannerRegistry_RegistrationMatchesDeclaration"/>
    /// able to see a deletion at all. That gate asserts equality against a *union*, and a union
    /// cannot detect the loss of a key that reaches it from both sides.
    /// <c>ScannerTransitiveRefs</c> was such a key — registered as a scanner *and* named by the
    /// shared read — so deleting its registration left the union unchanged, the gate passed, and
    /// <c>Dependencies</c> silently stopped producing its transitive tree. Keeping the two sides
    /// disjoint is what makes each side's contribution observable, so re-conflating them fails
    /// here rather than going quiet until someone reads the output.
    /// </remarks>
    [Fact]
    public void TheRegistrationGatesTwoSourcesOfHonor_AreDisjoint()
    {
        var registry = LibrarySections.CreateScannerRegistry();

        var reachableFromBothSides = registry.RegisteredKeys
            .Intersect(LibraryMetadataService.SharedReadScannerKeys, StringComparer.Ordinal);

        Assert.Empty(reachableFromBothSides);
    }

    /// <summary>
    /// Proves every member of <see cref="LibraryMetadataService.KeysTheReadOnlyFeeds"/> is a key
    /// the read actually reads.
    /// </summary>
    /// <remarks>
    /// The set exists only to be subtracted from <see cref="LibraryMetadataService.ReferenceReadingScannerKeys"/>,
    /// so a member outside that set subtracts nothing. Such an entry is not merely useless: it
    /// reads as a declaration that some key is fed-but-not-honored when no such key exists, which
    /// is the same "declaration that does not describe reality" this whole area exists to prevent.
    /// A review found the previous revision accepted a bogus member with every test still green.
    /// </remarks>
    [Fact]
    public void EveryKeyTheReadOnlyFeeds_IsAKeyTheReadReads()
    {
        Assert.NotEmpty(LibraryMetadataService.KeysTheReadOnlyFeeds);
        Assert.All(
            LibraryMetadataService.KeysTheReadOnlyFeeds,
            key => Assert.Contains(key, LibraryMetadataService.ReferenceReadingScannerKeys));
    }

    /// <summary>
    /// Proves the shared read does not claim a key no section declares.
    /// </summary>
    /// <remarks>
    /// The gates around this one all run in the direction "a declared key must be honored by
    /// something". None ran the other way, so an entry added to
    /// <see cref="LibraryMetadataService.ReferenceReadingScannerKeys"/> that names nothing was
    /// invisible: a reviewer added a bogus key and every gate still passed. That direction is not
    /// cosmetic. The set feeds <see cref="LibraryMetadataService.SharedReadScannerKeys"/>, which
    /// is one of the two sources that permit a section to declare a key with no registered
    /// scanner -- so a wrong entry here re-creates the exact defect this PR fixes: a section that
    /// passes every check and renders empty.
    /// <para>
    /// Deriving <c>SharedReadScannerKeys</c> from this set stops the two from drifting apart, but
    /// it cannot make a hand-written literal true; only comparing it against the sections can.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryKeyTheReadClaims_IsDeclaredBySomeSection()
    {
        var declared = LibrarySections.CreatePipeline().DeclaredScannerKeys;

        Assert.NotEmpty(LibraryMetadataService.ReferenceReadingScannerKeys);
        Assert.All(
            LibraryMetadataService.ReferenceReadingScannerKeys,
            key => Assert.Contains(key, declared));
    }

    /// <summary>
    /// Pins the membership of both sets that govern the shared reference read.
    /// </summary>
    /// <remarks>
    /// The other gates all check a key against something else in the code, so they cannot see an
    /// addition that is internally consistent. A reviewer added <c>ScannerInfoCounts</c> -- a real,
    /// registered, declared key -- to <see cref="LibraryMetadataService.ReferenceReadingScannerKeys"/>
    /// and <see cref="LibraryMetadataService.KeysTheReadOnlyFeeds"/>, where it cancels in the
    /// derived <see cref="LibraryMetadataService.SharedReadScannerKeys"/>. Every gate passed, the
    /// full suite passed, and <c>-S "Library Info" --json</c> silently began emitting a
    /// <c>references</c> array it had never emitted before.
    /// <para>
    /// No derived rule can catch that, because membership here <em>causes</em> the behavior it
    /// would be checked against: add a key and the read starts serving it, so declaration and
    /// reality agree again at the new, wrong value. Only a fixed expectation breaks that circle.
    /// Adding a member is a real change to what every section declaring that key collects, so it
    /// should require saying so here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSetsGoverningTheSharedRead_HaveExactlyTheseMembers()
    {
        Assert.Equal(
            new[]
            {
                LibrarySections.ScannerAuditSignals,
                LibrarySections.ScannerReferences,
                LibrarySections.ScannerTransitiveRefs
            },
            LibraryMetadataService.ReferenceReadingScannerKeys.OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal(
            new[]
            {
                LibrarySections.ScannerAuditSignals,
                LibrarySections.ScannerTransitiveRefs
            },
            LibraryMetadataService.KeysTheReadOnlyFeeds.OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>
    /// Proves every declared key the shared read does not satisfy has a registered scanner.
    /// </summary>
    /// <remarks>
    /// The complement of <see cref="TheRegistrationGatesTwoSourcesOfHonor_AreDisjoint"/>: that test
    /// says the two sources do not overlap, this one says together they still cover everything
    /// declared, so excluding a key from the read's published set cannot quietly leave it
    /// uncollected.
    /// </remarks>
    [Fact]
    public void EveryDeclaredKeyTheReadDoesNotSatisfy_HasARegisteredScanner()
    {
        var registry = LibrarySections.CreateScannerRegistry();
        var pipeline = LibrarySections.CreatePipeline();

        var needsARegisteredScanner = pipeline.DeclaredScannerKeys
            .Where(k => !LibraryMetadataService.SharedReadScannerKeys.Contains(k))
            .ToList();

        Assert.NotEmpty(needsARegisteredScanner);
        Assert.All(needsARegisteredScanner, key => Assert.Contains(key, registry.RegisteredKeys));
    }

    /// <summary>
    /// Proves every key <see cref="LibraryMetadataService.SharedReadScannerKeys"/> publishes is
    /// one the shared metadata read actually acts on, by requesting each key alone and checking
    /// that references were extracted.
    /// </summary>
    /// <remarks>
    /// This is the gate that stops <c>LibraryScannerRegistry_RegistrationMatchesDeclaration</c>
    /// from being satisfiable by assertion. That test asks whether a declared key is *claimed* to
    /// be honored; adding a key to the published set and to a section would satisfy it while
    /// nothing collected the data — the same silence #3453 found, one level up. This test asks
    /// whether the read *does* honor it, so a key can only join the set by changing behavior.
    /// Iterating the published set rather than naming keys means a new member is covered the
    /// moment it is added.
    /// </remarks>
    [Fact]
    public async Task SharedReadScannerKeys_EachKeyActuallyDrivesTheRead()
    {
        var path = typeof(SectionPipelineTests).Assembly.Location;
        using var httpClient = new HttpClient();
        var logger = new DotnetInspector.Output.VerboseLogger(false);

        Assert.NotEmpty(LibraryMetadataService.SharedReadScannerKeys);

        foreach (var key in LibraryMetadataService.SharedReadScannerKeys)
        {
            var inspection = await LibraryMetadataService.InspectAsync(
                path,
                new LibraryOptions(),
                logger,
                null,
                null,
                httpClient,
                scanners: new HashSet<string>(StringComparer.Ordinal) { key },
                scannerRegistry: LibrarySections.CreateScannerRegistry());

            Assert.NotNull(inspection);
            Assert.True(
                inspection!.AssemblyReferenceInspection.HasFindings(),
                $"'{key}' is published as honored by the shared read, but requesting it alone " +
                "extracted no assembly references. Either wire it into the read or stop " +
                "publishing it.");
        }
    }

    /// <summary>
    /// The converse of <see cref="SharedReadScannerKeys_EachKeyActuallyDrivesTheRead"/>: a key the
    /// declaration does NOT name must not drive the read either.
    /// </summary>
    /// <remarks>
    /// Every other gate constrains the two SETS. None of them constrains the CODE that consults
    /// them, so widening the condition itself -- <c>scanners is { Count: > 0 }</c> in place of
    /// <c>scanners?.Any(ReferenceReadingScannerKeys.Contains)</c> -- left both sets untouched, the
    /// literal pin passing, and every unrelated section quietly collecting and serializing
    /// references. A reviewer measured the difference as 0 references to 49 on a single section.
    /// <para>
    /// Pinning the sets cannot catch that. Only asserting the biconditional can: references are
    /// extracted if and only if the requested key is one the declaration names. This is the gate
    /// that ties the declaration to the behavior rather than to more declaration.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OnlyTheDeclaredKeys_DriveTheSharedRead()
    {
        // Quantified over the inspected assembly as well as over the request. Every earlier
        // version of this gate inspected only the test assembly, so a condition keyed on which
        // assembly is being read escaped it entirely: adding
        // `|| (!path.Contains("dotnet-inspect.Tests") && scanners is { Count: > 0 })` makes real
        // assemblies extract references for sections that never declared the key, and leaves all
        // 2556 tests green. The second fixture is a real framework assembly from the shared
        // framework, so neither its name nor its location resembles the first, and no predicate
        // over the inspected path can tell "the fixture" from "a real assembly".
        //
        // Read at Quiet verbosity. A real framework assembly carries SourceLink, so at Normal and
        // above the source plan authorizes a cache-first PDB read, which reaches a disk cache only
        // the CLI entry point initializes -- the inspection then returns null and the gate observes
        // nothing. Calling CoreCache.Initialize here instead does work, but it is process-global
        // and resets the base path other tests in this assembly rely on: it made two
        // PackageAcquisitionConcurrencyTests fail in a full run while passing in isolation.
        // Quiet keeps the read cache-free and network-free, and does not touch what is under test,
        // since the condition being gated is keyed on the requested scanners alone.
        var (platformPath, _, _, platformError) = PlatformResolver.ResolveAssembly(
            "System.Text.Json",
            useRuntimeAssemblies: true);
        Assert.True(
            platformError is null && platformPath is not null,
            $"Could not resolve a framework assembly, so the gate would only ever see the test "
                + $"assembly and a condition keyed on the inspected path would escape it: {platformError}");

        await AssertOnlyDeclaredKeysDriveTheSharedRead(
            [typeof(SectionPipelineTests).Assembly.Location, platformPath!]);
    }

    /// <summary>
    /// Options this sweep cannot give a meaningful non-default value. Named rather than skipped
    /// quietly: a new option is swept by default, and exempting one has to be a visible decision.
    /// </summary>
    private static readonly HashSet<string> OptionsTheSweepCannotVary = new(StringComparer.Ordinal)
    {
        // The two flags by which a user explicitly asks for references. They are part of the
        // read's declared contract, so the sweep's expectation already accounts for them.
        nameof(LibraryOptions.IncludeReferences),
        nameof(LibraryOptions.IncludeDependencies),
    };

    /// <summary>
    /// The shared read consults the requested scanner keys and the two explicit reference flags,
    /// and nothing else about the request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other gate in this file quantifies over scanner key <em>sets</em> while holding the
    /// rest of the request at a single shape, which made all of them blind to a condition keyed on
    /// any other part of it. Adding <c>|| !string.IsNullOrWhiteSpace(options.AssemblyName)</c> to
    /// the read compiled, made an ordinary <c>-v:n</c> inspection start rendering
    /// <c>## References</c>, and left all 2561 tests green -- because the gate never set a name
    /// and the CLI always does.
    /// </para>
    /// <para>
    /// So this varies the request instead of the keys. It reflects over every settable option, so
    /// an option added later is swept without anyone remembering to add it here, and asserts set
    /// equality against the exemptions above so a stale or missing exemption fails rather than
    /// silently shrinking the sweep. The expected answer is computed from the declaration, not by
    /// calling the product predicate, which would agree with any implementation of it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The request-scoped arguments of <c>InspectAsync</c>, as singles plus the maximal set.
    /// </summary>
    /// <remarks>
    /// Two rounds were lost to this axis. The sweep reflects over <c>LibraryOptions</c>, so a read
    /// keyed on something that is a PARAMETER rather than an option is invisible to it: round 21
    /// was `isPlatformAssembly`, and round 25 was `packageName`, where
    /// <c>|| packageName != null</c> built clean and left all 2576 tests green while making every
    /// package inspection extract references unconditionally.
    /// Singles catch a read keyed on one argument; the maximal row catches one keyed on a
    /// conjunction, for the same reason the option sweep probes its maximal set.
    /// </remarks>
    private static readonly (string? PackageName, string? PackageVersion, bool IsPlatformAssembly, bool DiscoveryOnly, string Label)[]
        InspectAsyncArgumentVariants =
    [
        (null, null, false, false, "defaults"),
        ("sweep.package", null, false, false, "packageName set"),
        (null, "1.2.3", false, false, "packageVersion set"),
        (null, null, true, false, "isPlatformAssembly set"),
        (null, null, false, true, "discoveryOnly set"),
        ("sweep.package", "1.2.3", true, true, "all four set"),
    ];

    /// <summary>
    /// Every parameter of <c>InspectAsync</c> is either varied by the sweep or named here with a
    /// reason. Asserting SET EQUALITY against the method's own signature is the point: a parameter
    /// added later is in neither set, so this fails until someone decides which side it belongs on.
    /// Listing only the varied ones would let a new parameter join the method silently, which is
    /// exactly how the two escapes above happened.
    /// </summary>
    [Fact]
    public void TheSharedReadSweep_CoversEveryInspectAsyncParameter()
    {
        var notVaried = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["path"] = "the inspected file; varied by the corpus gate, not this sweep",
            ["options"] = "the subject of the sweep -- reflected over property by property",
            ["logger"] = "a sink, not request state",
            ["httpClient"] = "a transport, not request state",
            ["scanners"] = "varied as `request`, which is what the read is allowed to consult",
            ["scannerRegistry"] = "the registry under test; varied by the structural gates",
        };

        var varied = InspectAsyncArgumentVariants
            .SelectMany(v => new[] { nameof(v.PackageName), nameof(v.PackageVersion), nameof(v.IsPlatformAssembly), nameof(v.DiscoveryOnly) })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var declared = typeof(LibraryMetadataService)
            .GetMethod(nameof(LibraryMetadataService.InspectAsync))!
            .GetParameters()
            .Select(p => p.Name!)
            .ToList();

        var unaccounted = declared
            .Where(name => !varied.Contains(name) && !notVaried.ContainsKey(name))
            .ToList();

        Assert.True(
            unaccounted.Count == 0,
            $"InspectAsync grew parameters the shared-read sweep neither varies nor exempts: "
                + $"{string.Join(", ", unaccounted)}. The read may now consult them without any "
                + "gate noticing -- vary them in InspectAsyncArgumentVariants, or exempt them here "
                + "with a reason.");

        var stale = notVaried.Keys.Where(name => !declared.Contains(name, StringComparer.Ordinal)).ToList();
        Assert.True(
            stale.Count == 0,
            $"These exemptions no longer name an InspectAsync parameter: {string.Join(", ", stale)}.");
    }

    [Fact]
    public async Task TheSharedRead_ConsultsNothingButTheScannerKeysAndTheExplicitFlags()
    {
        using var httpClient = new HttpClient();
        var logger = new DotnetInspector.Output.VerboseLogger(false);
        var declared = LibrarySections.CreatePipeline().DeclaredScannerKeys.ToList();

        var readingKey = declared.First(LibraryMetadataService.ReferenceReadingScannerKeys.Contains);
        var quietKey = declared.First(k => !LibraryMetadataService.ReferenceReadingScannerKeys.Contains(k));
        var path = typeof(SectionPipelineTests).Assembly.Location;

        // nonPublic: true. GetSetMethod(nonPublic: true) answers null for `internal set`, and LibraryOptions
        // lives in the same assembly as the parser that fills it, so an internal setter is an
        // axis a user can reach. Skipping those did not fail the couldNotVary assertion either:
        // the property never entered the sweep at all, so a read keyed on one passed silently.
        var settable = typeof(LibraryOptions).GetProperties()
            .Where(p => p.CanWrite && p.GetSetMethod(nonPublic: true) is not null)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(settable);

        var couldNotVary = new List<string>();
        var leaked = new List<string>();

        async Task ProbeAsync(Action<LibraryOptions> mutate, string label)
        {
            foreach (var (request, shouldRead) in new[]
            {
                (new List<string> { quietKey }, false),
                (new List<string> { readingKey }, true),
            })
            foreach (var (packageName, packageVersion, isPlatformAssembly, discoveryOnly, argLabel)
                in InspectAsyncArgumentVariants)
            {
                // Every probe carries an explicit section selection, which is what keeps
                // LibrarySourcePlan cache- and network-free. Without it, sweeping verbosity
                // up to Normal authorizes a cached PDB read, and the probe stops being a
                // pure metadata read.
                var options = new LibraryOptions
                {
                    UserVerbosityOverride = Verbosity.Quiet,
                    IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Library Info",
                    },
                };
                mutate(options);

                var inspection = await LibraryMetadataService.InspectAsync(
                    path,
                    options,
                    logger,
                    packageName,
                    packageVersion,
                    httpClient,
                    scanners: new HashSet<string>(request, StringComparer.Ordinal),
                    scannerRegistry: LibrarySections.CreateScannerRegistry(),
                    isPlatformAssembly: isPlatformAssembly,
                    discoveryOnly: discoveryOnly);

                if (inspection is null)
                {
                    couldNotVary.Add(label);
                    return;
                }

                if (inspection.AssemblyReferenceInspection.HasFindings() != shouldRead)
                {
                    leaked.Add(
                        $"{label} with {string.Join("+", request)} "
                            + $"({argLabel}, "
                            + $"expected reference extraction: {shouldRead})");
                }
            }
        }

        var sweepable = settable
            .Where(p => !OptionsTheSweepCannotVary.Contains(p.Name))
            .ToList();

        foreach (var property in sweepable)
        {
            if (NonDefaultValuesFor(property) is { Count: > 0 } values)
            {
                foreach (var value in values)
                    await ProbeAsync(o => property.SetValue(o, value), $"{property.Name}={value}");
            }
            else
            {
                couldNotVary.Add(property.Name);
            }
        }

        // The maximal probe: every sweepable option moved off its default at once. Varying one
        // option at a time cannot observe a condition keyed on a CONJUNCTION of two of them, and a
        // conjunction is exactly what a drifting read grows. Asserting over the maximal set fails
        // every such condition at once, rather than only the pairing a tamper happened to pick.
        var maximalMutations = sweepable
            .Select(property => (property, values: NonDefaultValuesFor(property)))
            .Where(pair => pair.values is { Count: > 0 })
            .Select(pair => (pair.property, value: pair.values![0]))
            .ToList();

        Assert.NotEmpty(maximalMutations);

        await ProbeAsync(
            o =>
            {
                foreach (var (property, value) in maximalMutations)
                    property.SetValue(o, value);
            },
            $"all {maximalMutations.Count} sweepable options set at once");

        // Only the first few leaks are printed. One drifting condition leaks under nearly every
        // mutation, so the full list is thousands of near-identical rows that bury the axis name.
        var shown = string.Join(" | ", leaked.Take(6));
        var more = leaked.Count > 6 ? $" (+{leaked.Count - 6} more)" : string.Empty;

        Assert.True(
            leaked.Count == 0,
            "Setting an option unrelated to the scanner keys changed whether the shared read "
                + $"extracted assembly references: {shown}{more}. The read's "
                + "condition has grown a dependency on the request beyond its declared contract.");

        Assert.True(
            couldNotVary.Count == 0,
            "The sweep could not exercise these options, so nothing constrains a condition keyed "
                + $"on them: {string.Join(", ", couldNotVary.Distinct())}. Give them a value here "
                + $"or name them in {nameof(OptionsTheSweepCannotVary)}.");
    }

    /// <summary>
    /// Every value for <paramref name="property"/> that differs from what a default
    /// <see cref="LibraryOptions"/> carries. All of them, not just the first: a condition keyed on
    /// one particular enum member survives a sweep that only tries one other member, which is how
    /// <c>&amp;&amp; options.UserVerbosity != Verbosity.Detailed</c> escaped an earlier revision.
    /// An empty result fails the sweep rather than skipping, so an unexercised option is never
    /// silent.
    /// </summary>
    private static List<object> NonDefaultValuesFor(System.Reflection.PropertyInfo property)
    {
        var current = property.GetValue(new LibraryOptions());

        return CandidateValuesFor(property.PropertyType)
            .Where(candidate => !Equals(candidate, current))
            .ToList();
    }

    private static IEnumerable<object> CandidateValuesFor(Type type) => CandidateValuesFor(type, depth: 0);

    private static IEnumerable<object> CandidateValuesFor(Type type, int depth)
    {
        // A nested option type can name its own complex members, so the recursion needs a floor.
        // Two levels reaches LibraryOptions -> PerformanceTriageOptions -> its scalars, which is
        // every option shape the product declares today; a deeper one would have to widen this.
        if (depth > 2)
            yield break;

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            yield return true;
            yield return false;
            yield break;
        }

        if (underlying == typeof(string))
        {
            yield return "sweep-probe";
            yield break;
        }

        if (underlying == typeof(string[]))
        {
            yield return new[] { "sweep-probe" };
            yield break;
        }

        if (underlying == typeof(int))
        {
            yield return 1;
            yield return 2;
            yield break;
        }

        if (underlying == typeof(HashSet<string>))
        {
            yield return new HashSet<string>(StringComparer.Ordinal) { "sweep-probe" };
            yield break;
        }

        if (underlying.IsEnum)
        {
            foreach (var value in Enum.GetValues(underlying))
                yield return value!;
            yield break;
        }

        // Types that publish their own instances -- RowSelector.First, RowWindow.Take(...),
        // PerformanceTriageOptions.Default. Reading them off the type keeps this sweep working for
        // an option type added later without anyone editing this method.
        foreach (var factory in underlying.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (factory.PropertyType == underlying && factory.GetValue(null) is { } instance)
                yield return instance;
        }

        if (!underlying.IsAbstract && underlying.GetConstructor(Type.EmptyTypes) is not null
            && Activator.CreateInstance(underlying) is { } constructed)
        {
            yield return constructed;

            // A record whose only published instance is its default (PerformanceTriageOptions.Default
            // is `new()`) needs one of its own properties moved before it counts as a variation.
            // Every writable member, and every candidate value for each: stopping at the first one
            // is how `options.PerformanceTriage.Top.HasValue` escaped an earlier revision, since
            // LoopOnly is declared first and was the only member this ever moved.
            foreach (var nested in underlying.GetProperties())
            {
                if (!nested.CanWrite || nested.GetSetMethod(nonPublic: true) is null)
                    continue;

                foreach (var nestedValue in CandidateValuesFor(nested.PropertyType, depth + 1))
                {
                    var mutated = Activator.CreateInstance(underlying)!;
                    if (Equals(nested.GetValue(mutated), nestedValue))
                        continue;

                    nested.SetValue(mutated, nestedValue);
                    yield return mutated;
                }
            }

            // ...and one instance with EVERY writable member moved at once. Varying members one at
            // a time cannot observe a condition keyed on a CONJUNCTION of them: a read gated on
            // `PerformanceTriage.LoopOnly && PerformanceTriage.Top.HasValue` is false under either
            // single mutation and only becomes true when both are set. This is the nested form of
            // the maximal-set probe below.
            var maximal = Activator.CreateInstance(underlying)!;
            var movedAny = false;
            foreach (var nested in underlying.GetProperties())
            {
                if (!nested.CanWrite || nested.GetSetMethod(nonPublic: true) is null)
                    continue;

                foreach (var nestedValue in CandidateValuesFor(nested.PropertyType, depth + 1))
                {
                    if (Equals(nested.GetValue(maximal), nestedValue))
                        continue;

                    nested.SetValue(maximal, nestedValue);
                    movedAny = true;
                    break;
                }
            }

            if (movedAny)
                yield return maximal;
        }

        // Types whose instances come from factory methods -- RowWindow.Head(int), .Tail(int).
        foreach (var factory in underlying.GetMethods(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (factory.ReturnType != underlying || factory.IsGenericMethod)
                continue;

            var parameters = factory.GetParameters();
            if (parameters.Length == 0 || !parameters.All(p =>
                (Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType) == typeof(int)))
            {
                continue;
            }

            if (factory.Invoke(null, [.. parameters.Select(object (_) => 1)]) is { } produced)
                yield return produced;
        }
    }

    private static async Task AssertOnlyDeclaredKeysDriveTheSharedRead(IReadOnlyList<string> paths)
    {
        using var httpClient = new HttpClient();
        var logger = new DotnetInspector.Output.VerboseLogger(false);
        var declared = LibrarySections.CreatePipeline().DeclaredScannerKeys.ToList();

        Assert.NotEmpty(declared);

        // The property is about the intersection, so the request has to be quantified over sets,
        // not over keys. A gate that only ever asks for one key at a time is vacuous against any
        // condition keyed on how many sections were requested: widening the consuming condition to
        // `|| scanners?.Count > 1` changes behavior for real users and leaves a single-key gate
        // completely green. The maximal non-reading set generalises that -- it is the largest
        // request that must still not read, so it fails every `Count > k` variant at once, not
        // just the k the tamper happened to pick.
        var reading = declared.Where(LibraryMetadataService.ReferenceReadingScannerKeys.Contains).ToList();
        var notReading = declared.Where(k => !LibraryMetadataService.ReferenceReadingScannerKeys.Contains(k)).ToList();

        Assert.NotEmpty(reading);
        Assert.NotEmpty(notReading);

        var requests = new List<List<string>>();
        requests.AddRange(declared.Select(k => new List<string> { k }));
        for (int i = 0; i < declared.Count; i++)
        {
            for (int j = i + 1; j < declared.Count; j++)
            {
                requests.Add([declared[i], declared[j]]);
            }
        }

        // Every cardinality, on BOTH sides of the biconditional. Two earlier versions got this
        // half right. Pairs plus one maximal set left `Count == 3` green, because the request
        // sizes were only 1, 2 and n. Walking k over the non-reading keys fixed that for the
        // must-not-read side and left the must-read side with sizes 1, 2 and n only, so
        // `&& scanners.Count != 3` -- a condition that silently drops References and Dependencies
        // whenever exactly three sections are selected -- still passed. Each k now contributes a
        // set of that size that reads and a set of that size that must not, so no predicate on
        // request size survives on either side.
        for (int k = 1; k <= declared.Count; k++)
        {
            if (k <= notReading.Count)
                requests.Add(notReading.Take(k).ToList());

            if (k - 1 <= notReading.Count)
                requests.Add([reading[0], .. notReading.Take(k - 1)]);
        }

        requests.Add(declared);

        var unexpected = new List<string>();
        var missing = new List<string>();

        foreach (var path in paths)
        {
            foreach (var request in requests)
            {
                var inspection = await LibraryMetadataService.InspectAsync(
                    path,
                    new LibraryOptions { UserVerbosityOverride = Verbosity.Quiet },
                    logger,
                    null,
                    null,
                    httpClient,
                    scanners: new HashSet<string>(request, StringComparer.Ordinal),
                    scannerRegistry: LibrarySections.CreateScannerRegistry());

                Assert.NotNull(inspection);

                var readReferences = inspection!.AssemblyReferenceInspection.HasFindings();
                var shouldRead = request.Any(LibraryMetadataService.ReferenceReadingScannerKeys.Contains);

                var label = $"{Path.GetFileName(path)}: {string.Join("+", request)}";
                if (readReferences && !shouldRead)
                    unexpected.Add(label);
                else if (!readReferences && shouldRead)
                    missing.Add(label);
            }
        }

        Assert.True(
            unexpected.Count == 0,
            $"These requests contain no key declared as reference-reading, but extracted assembly "
                + $"references anyway: {string.Join(" | ", unexpected)}. The condition that "
                + "consults ReferenceReadingScannerKeys has drifted wider than the declaration.");

        Assert.True(
            missing.Count == 0,
            $"These requests contain a key declared as reference-reading but extracted nothing: "
                + $"{string.Join(" | ", missing)}.");
    }

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

    // Assembly reference names come from the inspected assembly's metadata and are joined onto a
    // directory and probed, so they are an untrusted path component. See
    // docs/design/untrusted-data-threat-model.md, "Derived paths".
    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc/hosts")]
    [InlineData("C:evil")]
    [InlineData("C:\\Windows\\System32\\kernel32")]
    [InlineData("\\\\server\\share\\evil")]
    [InlineData("evil\u0000name")]
    [InlineData("evil\nname")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NUL")]
    [InlineData("con")]
    [InlineData("COM1")]
    [InlineData("LPT9.dll")]
    // Windows strips trailing spaces and dots from a path component, so these name something
    // other than what the metadata spells. "CON " reaches the CON device; "Foo." opens "Foo";
    // "System.Text.Json " collides with the real assembly while denoting a different one.
    [InlineData("CON ")]
    [InlineData("con.")]
    [InlineData("NUL   ")]
    [InlineData("Foo.")]
    [InlineData("System.Text.Json ")]
    [InlineData("System.Text.Json.")]
    [InlineData(" System.Text.Json")]
    [InlineData(".")]
    // Windows reserves these exact strings as device names. It is not that a superscript folds to
    // a digit -- the matcher uppercases ASCII and strips trailing dots and spaces, and does no
    // Unicode normalization -- so only the LITERAL superscripts one, two and three are reserved.
    // COM\u2074 and COM\uff11 are ordinary names; see the accept theory.
    [InlineData("COM\u00b9")]
    [InlineData("COM\u00b2.txt")]
    [InlineData("COM\u00b3")]
    [InlineData("LPT\u00b9")]
    [InlineData("LPT\u00b2.dll")]
    [InlineData("lpt\u00b3")]
    // Non-ASCII edge whitespace is not stripped by the host, but it renders identically to the
    // unpadded name while denoting a different assembly.
    [InlineData("System.Text.Json\u00a0")]
    [InlineData("\u00a0System.Text.Json")]
    [InlineData("System.Text.Json\u3000")]
    [InlineData("\u3000System.Text.Json")]
    [InlineData("CON\u00a0")]
    // Format characters are invisible or reorder what follows, so the rendered name is not the
    // resolved name (Trojan Source, CVE-2021-42574).
    [InlineData("System.Text.Json\u200b")]
    [InlineData("\u200bSystem.Text.Json")]
    [InlineData("System.\u202eJson")]
    [InlineData("COM1\u200b")]
    [InlineData("\ufeffSystem.Text.Json")]
    // Names made only of dots are host-special rather than ordinary components. "." and ".." are
    // covered above; this is the case the narrowed rule must still refuse.
    [InlineData("...")]
    // Trailing dots stay refused, but by the trailing-dot rule that runs earlier -- the host
    // strips them, so this would denote "TrailingDots". Narrowing the dot rule does not reach it.
    [InlineData("TrailingDots..")]
    // Windows strips trailing dots and spaces from the STEM, so these reach the device. The space
    // is interior to the whole name, so the edge-whitespace rule above cannot see it; only the
    // stem TrimEnd does. Without that, these three are accepted.
    [InlineData("COM1 .txt")]
    [InlineData("CON .dll")]
    [InlineData("COM1 . .ext")]
    // Plane 14 tag characters are Format but each is a surrogate pair, so a per-char scan reports
    // Surrogate for both halves and accepts a name that renders as nothing.
    [InlineData("Valid\U000E0020Dependency")]
    [InlineData("System.Text.Json\U000E0041")]
    // Line and paragraph separators are neither Control nor Format, and mid-name they sit where
    // the edge-whitespace rule cannot see them, so all three earlier rules accepted them. A
    // consumer that honours U+2028 breaks the name across two lines, which is the same failure
    // the Format rule above exists to stop: the name displayed is not the name resolved, and the
    // second half reads as a dependency that does not exist.
    [InlineData("Ab\u2028Cd")]
    [InlineData("Ab\u2029Cd")]
    public void UnsafeAssemblyReferenceName_IsRefusedAsPathComponent(string name)
    {
        Assert.False(LibraryMetadataService.IsSafeAssemblySimpleName(name));
    }

    /// <summary>
    /// The length cap is a real rule, so it needs a case that reaches it. No theory row exceeded
    /// 256 characters, so deleting the cap left the whole suite green -- the rule was shipped
    /// unguarded. The 256-character control keeps the boundary honest in both directions: a cap
    /// moved to an arbitrarily small value fails the accept side rather than passing silently.
    /// </summary>
    [Fact]
    public void OverlongAssemblyReferenceName_IsRefused()
    {
        Assert.False(LibraryMetadataService.IsSafeAssemblySimpleName(new string('A', 257)));
        Assert.True(LibraryMetadataService.IsSafeAssemblySimpleName(new string('A', 256)));
    }

    /// <summary>
    /// Negative control for the separator rule. Rejecting every Unicode separator would also
    /// "fix" the rows above, so the ordinary ASCII space -- which is a Separator too, and which
    /// real assembly names in the close-negative set below carry -- has to stay accepted for the
    /// rule to mean what it claims. Only the LINE and PARAGRAPH separators forge a line.
    /// </summary>
    [Fact]
    public void InteriorSpaceInAssemblyReferenceName_StaysAccepted()
    {
        Assert.Equal(UnicodeCategory.SpaceSeparator, char.GetUnicodeCategory(' '));
        Assert.True(LibraryMetadataService.IsSafeAssemblySimpleName("Ab Cd"));
        Assert.False(LibraryMetadataService.IsSafeAssemblySimpleName("Ab\u2028Cd"));
    }

    // Close negatives: real assembly names, including ones with dots, digits, dashes, unicode and
    // a device name only as a prefix. Over-rejecting here would silently drop real dependencies.
    [Theory]
    [InlineData("System.Text.Json")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("Newtonsoft.Json")]
    [InlineData("System.Runtime.CompilerServices.Unsafe")]
    [InlineData("My-Assembly_1.0")]
    [InlineData("NULlable.Helpers")]
    [InlineData("CONtoso.Library")]
    [InlineData("COM1Plus")]
    [InlineData("mscorlib")]
    [InlineData("\u00dcmlaut.Assembly")]
    // Interior spaces and dots are not canonicalized away, so these stay distinct and legitimate.
    [InlineData("My Assembly.Core")]
    [InlineData("CON Toso.Library")]
    // COM4-COM9 take no superscript form, and a superscript outside a device stem is just a
    // character. Folding the superscripts must not grow into rejecting these.
    [InlineData("COM\u00b9Plus")]
    [InlineData("Contoso.V\u00b2")]
    [InlineData("COM\u00b94")]
    // Windows reserves the LITERAL superscript spellings only; it applies no Unicode
    // normalization and no best-fit mapping to a path it opens. So every other numeric spelling
    // names an ordinary file, and refusing it costs a real dependency. GPT built an SDK project
    // with <AssemblyName>COM\uff14</AssemblyName>, which compiled and produced COM\uff14.dll --
    // an earlier fold refused it and the tree showed the node unresolved, with no company and no
    // children. Superscript four and nine are here for the same reason: only one, two and three
    // have superscript forms Windows reserves.
    [InlineData("COM\uff11")]
    [InlineData("COM\uff14")]
    [InlineData("COM\u2074")]
    [InlineData("LPT\u2079")]
    [InlineData("COM\u0661")]
    [InlineData("com\u2460")]
    [InlineData("\uff23\uff2f\uff2d1")]
    [InlineData("COM\uff11Plus")]
    [InlineData("Contoso.V\u2074")]
    [InlineData("COM\uff114")]
    // Well-formed supplementary-plane characters are ordinary. The rune scan rejects by CATEGORY,
    // not by plane, so it must not refuse an emoji or a CJK extension character.
    [InlineData("Assembly\U0001F600")]
    [InlineData("Assembly\U00020000")]
    // Interior non-ASCII whitespace is not padding and is not canonicalized.
    [InlineData("My\u00a0Assembly.Core")]
    // Consecutive dots inside a name are not traversal: this becomes one path component, and a
    // component with no separator cannot leave its directory. The C# compiler accepts and emits
    // these, and refusing them cost the node its resolution, company and children.
    [InlineData("Valid..Dependency")]
    [InlineData("Foo..Bar..Baz")]
    [InlineData("..LeadingDots")]
    public void LegitimateAssemblyReferenceName_IsAccepted(string name)
    {
        Assert.True(LibraryMetadataService.IsSafeAssemblySimpleName(name));
    }

    /// <summary>
    /// A platform assembly's own dependency is platform, not local. Recursion replaces the source
    /// directory with the resolved parent's directory, so the "is it beside the source directory?"
    /// probe answers a different question at depth 3 than at depth 0: inside the shared framework
    /// it answers yes for every platform assembly. Before provenance was derived from the resolved
    /// file's location, this reported 101 assemblies in /usr/local/share/dotnet as "local" -- which
    /// reads as "shipped beside the assembly you inspected" -- including System.Private.CoreLib.
    /// </summary>
    /// <remarks>
    /// The root is a <em>platform</em> assembly on purpose. An earlier version of this test used
    /// the test assembly, which is local, so depth 0 was legitimately "local" and the whole
    /// platform-root case -- where every dependency sits beside a platform parent -- was outside
    /// the fixture. That version passed against code that labelled all 27 of System.Text.Json's
    /// dependencies "local", so the test name claimed a property the fixture could not observe.
    /// </remarks>
    [Fact]
    public void PlatformAssemblyDependencies_AreNotReportedAsLocal()
    {
        var (platformPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        Assert.True(error is null && platformPath is not null, $"Could not resolve a platform assembly: {error}");

        var sharedRoot = PlatformResolver.GetSharedDirectory();
        Assert.False(string.IsNullOrEmpty(sharedRoot), "No shared framework directory on this machine.");

        var (references, _) = AssemblyInspector.ExtractReferencesAndCompany(platformPath!);

        var nodes = LibraryMetadataService.BuildTransitiveReferences(
            references,
            Path.GetDirectoryName(platformPath!),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new DotnetInspector.Output.VerboseLogger(false),
            deduplicate: true);

        var mislabelled = nodes
            .Where(n => n.ResolvedFrom == "local"
                && n.Path is not null
                && Path.GetFullPath(n.Path).StartsWith(Path.GetFullPath(sharedRoot!), StringComparison.OrdinalIgnoreCase))
            .Select(n => $"{n.Name} (depth {n.Depth}) -> {n.Path}")
            .ToList();

        Assert.True(
            mislabelled.Count == 0,
            "These live under the shared framework but are reported as resolved 'local': "
                + string.Join("; ", mislabelled));

        // Positive control: the walk actually resolved things. Without this, a walk that resolved
        // nothing would satisfy the assertion above.
        Assert.Contains(nodes, n => n.ResolvedFrom == "platform");
    }

    /// <summary>
    /// The other direction of the same rule: an assembly that ships beside a local root is local.
    /// Deriving provenance from the file's location must not relabel an application's own
    /// dependencies as platform, which would be a worse error than the one being fixed.
    /// </summary>
    [Fact]
    public void LocalAssemblyDependencies_AreNotReportedAsPlatform()
    {
        var path = typeof(SectionPipelineTests).Assembly.Location;
        var sourceDir = Path.GetDirectoryName(path)!;
        var (references, _) = AssemblyInspector.ExtractReferencesAndCompany(path);

        var nodes = LibraryMetadataService.BuildTransitiveReferences(
            references,
            sourceDir,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new DotnetInspector.Output.VerboseLogger(false),
            deduplicate: true);

        var mislabelled = nodes
            .Where(n => n.ResolvedFrom == "platform"
                && n.Path is not null
                && string.Equals(Path.GetDirectoryName(Path.GetFullPath(n.Path)), sourceDir, StringComparison.Ordinal))
            .Select(n => $"{n.Name} -> {n.Path}")
            .ToList();

        Assert.True(
            mislabelled.Count == 0,
            "These sit beside the inspected assembly but are reported as 'platform': "
                + string.Join("; ", mislabelled));

        Assert.Contains(nodes, n => n.ResolvedFrom == "local");
    }

    /// <summary>
    /// Provenance is a function of the resolved file, so every node the walk emits must carry
    /// exactly what the file's own location implies. That is the property the walk has to keep:
    /// under deduplication the shared visited set means the first route to an assembly wins, so
    /// any route-derived component in the answer would surface as a node disagreeing with its own
    /// path.
    /// </summary>
    /// <remarks>
    /// The first version of this test tried to prove route-independence by walking the same graph
    /// with the reference list reversed. That asserted nothing: the walk sorts each level with
    /// <c>OrderBy(r =&gt; r.Name)</c>, so both runs visit in identical order and the comparison was
    /// between a walk and itself. Reversing the input cannot produce a second route; only a
    /// different graph could, and building one requires compiling fixture assemblies. Comparing
    /// each node against the pure function tests the same property without the fixture, and fails
    /// for any node whose kind came from anywhere but its path.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReportedProvenance_MatchesWhatThePathAloneImplies(bool rootIsPlatform)
    {
        string rootPath;
        if (rootIsPlatform)
        {
            var (platformPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
            Assert.True(error is null && platformPath is not null, $"Could not resolve a platform assembly: {error}");
            rootPath = platformPath!;
        }
        else
        {
            rootPath = typeof(SectionPipelineTests).Assembly.Location;
        }

        var (references, _) = AssemblyInspector.ExtractReferencesAndCompany(rootPath);

        var nodes = LibraryMetadataService.BuildTransitiveReferences(
            references,
            Path.GetDirectoryName(rootPath),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new DotnetInspector.Output.VerboseLogger(false),
            deduplicate: true);

        var resolved = nodes.Where(n => n.Path is not null).ToList();

        // The expectation is computed HERE, from roots this test enumerates itself, rather than by
        // calling the classifier again. Asking ProvenanceOf for both sides compared the classifier
        // with itself: `return "platform";` satisfied it, because both sides moved together. An
        // independent oracle is what makes the two comparable.
        var platformRoots = PlatformResolver.GetAllSharedDirectories()
            .Concat(PlatformResolver.GetAllPacksDirectories())
            .Where(Directory.Exists)
            .Select(d => Path.TrimEndingDirectorySeparator(Path.GetFullPath(d)) + Path.DirectorySeparatorChar)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(platformRoots);

        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        // Deliberately no anonymous type here. Anonymous types are emitted ahead of the declared
        // ones, so introducing one anywhere in this project displaces MethodDef 0x06000001 -- and
        // LibraryCommand_IlOffsetsFile_* hard-code that token against this very assembly, so they
        // failed with an unrelated member. Tracked separately; this local function keeps the
        // trap from being re-sprung here.
        string ExpectedFor(string path) =>
            platformRoots.Any(r => Path.GetFullPath(path).StartsWith(r, comparison))
                ? "platform"
                : "local";

        var disagreements = resolved
            .Where(n => n.ResolvedFrom != ExpectedFor(n.Path!))
            .Select(n => $"{n.Name} (depth {n.Depth}) reported {n.ResolvedFrom}, "
                + $"but its path implies {ExpectedFor(n.Path!)}: {n.Path}")
            .ToList();

        Assert.True(
            disagreements.Count == 0,
            "These nodes carry a provenance their own path does not imply: "
                + string.Join("; ", disagreements));

        // Positive control: the walk resolved a non-trivial graph, so the assertion above ranged
        // over real nodes rather than an empty set.
        Assert.True(resolved.Count > 5, $"Only {resolved.Count} references resolved; too few to be meaningful.");
    }

    /// <summary>
    /// Classification asks every shared-framework root on the machine, not the one this process
    /// would run on. Resolving the preferred root answers "which runtime is preferred", and
    /// returns only its first hit, so pointing DOTNET_ROOT at another install reported every
    /// dependency of a real platform assembly as local -- the mislabelling this is meant to
    /// prevent, reachable through an environment variable.
    /// </summary>
    /// <summary>
    /// A platform assembly reached through a symlinked ANCESTOR is still platform. The classifier
    /// compares against canonical roots, so a path that is not itself canonicalized matches none
    /// of them and falls through to "local".
    /// </summary>
    /// <remarks>
    /// The link is planted several levels above the assembly, which is the case the previous
    /// implementation missed: it resolved the leaf and its immediate parent only, while claiming
    /// in its doc comment to resolve the whole chain. Neither of those two is a link here, so the
    /// path came back unresolved and System.Private.CoreLib reported resolved_from: "local".
    /// Skips rather than passes where symlinks are unavailable, so it can never assert nothing.
    /// </remarks>
    [Fact]
    public void PlatformClassification_ResolvesInstallsReachedThroughASymlinkedAncestor()
    {
        var shared = PlatformResolver.GetAllSharedDirectories().FirstOrDefault(Directory.Exists);
        Assert.SkipWhen(shared is null, "No shared framework directory on this machine.");

        var assembly = Directory
            .EnumerateFiles(shared!, "System.Private.CoreLib.dll", SearchOption.AllDirectories)
            .FirstOrDefault();
        Assert.SkipWhen(assembly is null, "No platform assembly under the shared framework.");

        // Sanity: the direct path must already classify as platform, or the test proves nothing
        // about the link.
        Assert.Equal("platform", LibraryMetadataService.ProvenanceOf(assembly!));

        var scratch = Path.Combine(Path.GetTempPath(), $"di-symlink-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            var link = Path.Combine(scratch, "linked");
            try
            {
                Directory.CreateSymbolicLink(link, shared!);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("This host does not permit creating symbolic links.");
                return;
            }

            var throughLink = Path.Combine(link, Path.GetRelativePath(shared!, assembly!));
            Assert.True(File.Exists(throughLink), $"Planted link did not expose {throughLink}.");

            Assert.Equal("platform", LibraryMetadataService.ProvenanceOf(throughLink));
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void PlatformClassification_DoesNotDependOnWhichInstallIsPreferred()
    {
        // Regression: this test used to plant a second install and point DOTNET_ROOT at it. It
        // asserted nothing, because it called ProvenanceOf once beforehand to establish the
        // expected value, and that first call initialized the cached root list from the original
        // environment -- so the variable it set could no longer affect anything. Replacing the
        // whole root enumeration with the single preferred root left it green.
        //
        // The property does not need an environment at all. GetSharedDirectory answers "which
        // runtime is preferred" and returns one shared directory; it never returns a reference
        // pack, and never returns a non-preferred runtime version. So any assembly under those
        // roots classifies as platform only if classification enumerates every root, which is
        // exactly the property. Both cases below fail if the enumeration collapses to the
        // preferred root.
        var preferred = PlatformResolver.GetSharedDirectory();
        var probes = new List<(string Path, string Why)>();

        var packAssembly = PlatformResolver.GetAllPacksDirectories()
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "System.Runtime.dll", SearchOption.AllDirectories))
            .FirstOrDefault();
        if (packAssembly is not null)
            probes.Add((packAssembly, "a reference pack, which is never the preferred shared directory"));

        var otherRuntime = PlatformResolver.GetAllSharedDirectories()
            .Where(Directory.Exists)
            .Where(d => preferred is null || !string.Equals(
                Path.TrimEndingDirectorySeparator(d),
                Path.TrimEndingDirectorySeparator(preferred),
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(d => Directory.EnumerateFiles(d, "System.Runtime.dll", SearchOption.AllDirectories))
            .FirstOrDefault();
        if (otherRuntime is not null)
            probes.Add((otherRuntime, "an installed runtime that is not the preferred one"));

        Assert.True(
            probes.Count > 0,
            "Found neither a reference pack nor a second installed runtime, so this machine cannot "
                + "distinguish 'every root' from 'the preferred root' and the test would assert nothing.");

        foreach (var (path, why) in probes)
        {
            Assert.True(
                LibraryMetadataService.ProvenanceOf(path) == "platform",
                $"{path} lives under {why}, so it is a platform assembly; reporting it as local "
                    + "means classification consulted only the preferred root.");
        }
    }

    /// <summary>
    /// The case-sensitivity probe reports what the filesystem actually does, checked against an
    /// independent oracle: create a file, then ask for it under the opposite spelling.
    /// </summary>
    /// <remarks>
    /// This runs on whatever volume the test host uses, so it pins the probe against reality on
    /// every machine the suite runs on rather than against an assumption about the OS. It is the
    /// gate for "asks the volume": hard-coding either comparison fails it on a host of the other
    /// kind, and hard-coding the host default fails it on a case-sensitive macOS volume, which is
    /// the case that was reported wrong.
    /// </remarks>
    [Fact]
    public void CaseSensitivityProbe_AgreesWithTheFilesystem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "di-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "probe.marker"), "x");
            var filesystemIgnoresCase = File.Exists(Path.Combine(dir, "PROBE.MARKER"));

            var expected = filesystemIgnoresCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            Assert.Equal(expected, LibraryMetadataService.ComparisonForVolumeHolding(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A case-sensitive volume yields an ordinal comparison. No test on a case-insensitive host can
    /// produce such a volume, so the existence check is supplied; the test above is what pins the
    /// real overload to the real filesystem.
    /// </summary>
    /// <remarks>
    /// This is the gate that fails when the comparison is hard-coded to the host default: a
    /// case-sensitive volume answers only to the exact spelling, and that must produce
    /// <see cref="StringComparison.Ordinal"/> on every host, macOS included.
    /// </remarks>
    [Fact]
    public void CaseSensitivityProbe_ReportsOrdinal_ForAVolumeThatDistinguishesCase()
    {
        const string Root = "/volumes/case-sensitive/dotnet/shared/";

        Assert.Equal(
            StringComparison.Ordinal,
            LibraryMetadataService.ComparisonForVolumeHolding(
                Root,
                p => Path.TrimEndingDirectorySeparator(p) == Path.TrimEndingDirectorySeparator(Root),
                _ => ["shared"]));

        Assert.Equal(
            StringComparison.OrdinalIgnoreCase,
            LibraryMetadataService.ComparisonForVolumeHolding(
                Root,
                _ => true,
                _ => ["shared"]));
    }

    /// <summary>
    /// A case-sensitive volume can hold <c>shared</c> and <c>SHARED</c> as two different
    /// directories. Both spellings then resolve, so existence alone says "case-insensitive" and
    /// files under one tree get reported as belonging to the other.
    /// </summary>
    /// <remarks>
    /// This is the case the first version of the probe got wrong, found by building a
    /// case-sensitive APFS image and planting both spellings. The parent listing is what
    /// distinguishes an alias from a genuinely distinct sibling, so this test fails if the probe
    /// goes back to asking only whether the flipped path exists.
    /// </remarks>
    [Fact]
    public void CaseSensitivityProbe_ReportsOrdinal_WhenBothSpellingsExistAsDistinctDirectories()
    {
        const string Root = "/volumes/case-sensitive/dotnet/shared/";

        Assert.Equal(
            StringComparison.Ordinal,
            LibraryMetadataService.ComparisonForVolumeHolding(
                Root,
                _ => true,
                _ => ["shared", "SHARED"]));
    }

    /// <summary>
    /// Classification honours the comparison each root carries. A case-sensitive volume was
    /// reported as platform for a path that differs from the root only in case -- a genuinely
    /// different directory there.
    /// </summary>
    /// <remarks>
    /// The roots are supplied rather than discovered because no test can create a case-sensitive
    /// APFS volume, and the defect only shows on one. Both directions are asserted, so replacing
    /// the per-root comparison with either constant fails this test.
    /// </remarks>
    [Fact]
    public void CaseDifferingPath_IsPlatformOnlyWhenTheRootsVolumeIgnoresCase()
    {
        // Canonicalized exactly as PlatformRoots canonicalizes a real root. Without this the
        // fixture is not comparable to the probe path, which classification canonicalizes: on
        // macOS the temp directory sits under /var, a link to /private/var, so the two spellings
        // could not prefix-match even ignoring case.
        var root = LibraryMetadataService.Canonicalize(
            Path.Combine(Path.GetTempPath(), "di-roots", "Shared")) + Path.DirectorySeparatorChar;
        var differingOnlyInCase = Path.Combine(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(root))!,
            "SHARED",
            "System.Private.CoreLib.dll");

        Assert.Equal(
            "local",
            LibraryMetadataService.ProvenanceOf(
                differingOnlyInCase,
                [new LibraryMetadataService.PlatformRoot(root, StringComparison.Ordinal)]));

        Assert.Equal(
            "platform",
            LibraryMetadataService.ProvenanceOf(
                differingOnlyInCase,
                [new LibraryMetadataService.PlatformRoot(root, StringComparison.OrdinalIgnoreCase)]));
    }

    // Artifact canary for the predicate above: exercises the real resolution walk rather than the
    // predicate alone, and proves the refusal is what stops the read. A payload is planted exactly
    // where the traversal would land; on unguarded code the walk resolves and reads it, populating
    // Path/ResolvedFrom/Company from a file outside the assembly's own directory.
    [Fact]
    public async Task BuildTransitiveReferences_TraversingName_RefusesToResolveOutsideSourceDir()
    {
        var root = Directory.CreateTempSubdirectory("di-traversal-");
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;

            // A real, readable assembly one level above the assembly directory.
            var realAssembly = typeof(SectionPipelineTests).Assembly.Location;
            Assert.True(File.Exists(realAssembly));
            File.Copy(realAssembly, Path.Combine(root.FullName, "payload.dll"), overwrite: true);

            // ...and a legitimately-named one inside it, as the positive control.
            File.Copy(realAssembly, Path.Combine(sourceDir, "Legit.Neighbor.dll"), overwrite: true);

            // ...and one whose name embeds "..", which is a legal assembly simple name. This is the
            // over-rejection control: it sits in the same directory as the guard's own assembly, so
            // it must resolve. A rule that refuses embedded ".." leaves it unresolved instead.
            File.Copy(realAssembly, Path.Combine(sourceDir, "Valid..Dependency.dll"), overwrite: true);

            var references = new List<AssemblyReference>
            {
                new("../payload", "1.0.0.0", null, null),
                new("Legit.Neighbor", "1.0.0.0", null, null),
                new("Valid..Dependency", "1.0.0.0", null, null)
            };

            // The refusal writes to Console.Error unconditionally, so this has to run under
            // ConsoleCapture even though it asserts nothing about the text. Console.Error is
            // process-global: an uncaptured write here lands in whichever OTHER test happens to
            // hold the redirect, and that test then fails on output it never produced. That is
            // exactly the order-dependent flake ConsoleCapture was introduced to end, and leaving
            // this call outside it made four unrelated tests fail intermittently.
            List<AssemblyReferenceNode> nodes = [];
            await ConsoleCapture.RunAsync(() =>
            {
                nodes = LibraryMetadataService.BuildTransitiveReferences(
                    references,
                    sourceDir,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new VerboseLogger(false));
            });

            var traversing = Assert.Single(nodes, n => n.Name == "../payload");
            Assert.Null(traversing.Path);
            Assert.Null(traversing.ResolvedFrom);
            Assert.Null(traversing.Company);

            // The reference is still reported -- refusing to resolve must not hide the evidence.
            Assert.Contains(nodes, n => n.Name == "../payload");

            // Positive control: a normal sibling in the same directory still resolves, so the guard
            // is refusing the traversal specifically and not simply disabling local resolution.
            var doubleDot = Assert.Single(nodes, n => n.Name == "Valid..Dependency");
            Assert.NotNull(doubleDot.Path);
            Assert.Equal(sourceDir, Path.GetDirectoryName(doubleDot.Path));

            var legit = Assert.Single(nodes, n => n.Name == "Legit.Neighbor");
            Assert.Equal("local", legit.ResolvedFrom);
            Assert.NotNull(legit.Path);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // Refusing to resolve is a security decision, and the node it produces is indistinguishable
    // from an ordinary unresolved reference -- same name, empty Path, empty ResolvedFrom. Routing
    // the reason through VerboseLogger.Log therefore hid it at every verbosity a user actually
    // runs: the tree rendered a plausible-looking unresolved dependency and said nothing about
    // having declined it. The message goes through Warn, which is not verbosity-gated.
    [Fact]
    public async Task BuildTransitiveReferences_TraversingName_ReportsTheRefusalWithoutVerbose()
    {
        var root = Directory.CreateTempSubdirectory("di-traversal-visible-");
        try
        {
            var sourceDir = Directory.CreateDirectory(Path.Combine(root.FullName, "app")).FullName;
            var realAssembly = typeof(SectionPipelineTests).Assembly.Location;
            File.Copy(realAssembly, Path.Combine(root.FullName, "payload.dll"), overwrite: true);
            File.Copy(realAssembly, Path.Combine(sourceDir, "Legit.Neighbor.dll"), overwrite: true);

            var (_, error) = await ConsoleCapture.RunAsync(() =>
            {
                LibraryMetadataService.BuildTransitiveReferences(
                    [
                        new AssemblyReference("../payload", "1.0.0.0", null, null),
                        new AssemblyReference("Legit.Neighbor", "1.0.0.0", null, null)
                    ],
                    sourceDir,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    // Not verbose: this is the configuration every default invocation uses.
                    new VerboseLogger(false));
            });

            Assert.Contains("refusing to resolve", error, StringComparison.Ordinal);
            Assert.Contains("../payload", error, StringComparison.Ordinal);

            // The legitimate neighbour resolved silently, so the warning is about the refusal and
            // not a message this path emits for every reference.
            Assert.DoesNotContain("Legit.Neighbor", error, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
