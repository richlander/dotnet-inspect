using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.Findings;
using ILInspector.Research;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;
using System.Collections.Immutable;
using System.Text.Json;
using InertText;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class SectionPipelineTests
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
    public void IntegrationInventory_IsExplicitNetworkFreeVerboseSection()
    {
        DocumentSchema schema = IntegrationInventorySections.CreateSchema();
        SectionCatalog<IntegrationInventoryProjectionResult> catalog =
            IntegrationInventorySections.CreateCatalog();

        Assert.Equal(
            IntegrationInventorySections.Inventory,
            IntegrationInventorySections.InventoryRows.Name);
        Assert.False(
            IntegrationInventorySections.InventoryRows.IsExpensive);
        Assert.True(
            IntegrationInventorySections.InventoryRows.ExplicitOnly);
        Assert.Equal(
            SectionSizeClass.Verbose,
            IntegrationInventorySections.InventoryRows.SizeClass);
        Assert.Equal(
            SectionCost.NetworkFree,
            IntegrationInventorySections.InventoryRows.Cost);
        Assert.Equal(
            [
                "Concept",
                "Relationship",
                "Source",
                "Source Assembly",
                "Source Provenance",
                "Source Parent",
                "Binding Context",
                "Peer",
                "Peer Scope",
                "Terminal",
                "Terminal Assembly",
                "Terminal Provenance",
                "Terminal Parent",
                "Forwarding Hops",
                "Disposition",
                "Out Reason",
                "Producer Policies",
            ],
            schema.GetSection(IntegrationInventorySections.Inventory)!
                .Items.Select(static item => item.Name));
        Assert.Equal(
            [IntegrationInventorySections.Inventory],
            catalog.AllSectionNames);
        Assert.Empty(
            catalog.Pipeline.GetCandidateSections(Verbosity.Detailed));
        Assert.Equal(
            [IntegrationInventorySections.Inventory],
            catalog.Pipeline.GetCandidateSections(
                Verbosity.Normal,
                [IntegrationInventorySections.Inventory]));
    }

    [Fact]
    public void IntegrationInventory_DoesNotWidenLibraryIntegrationsCategory()
    {
        SectionCatalog<LibraryInspection> catalog =
            LibrarySections.CreateCatalog().Sections;

        Assert.DoesNotContain(
            IntegrationInventorySections.Inventory,
            catalog.AllSectionNames);
        Assert.DoesNotContain(
            IntegrationInventorySections.Inventory,
            catalog.CategoryMap[SectionCategoryNames.Integrations]);
    }

    [Fact]
    public void LibraryPipeline_HasExpectedSectionCount()
    {
        var pipeline = LibrarySections.CreatePipeline();

        // The non-metadata sections stay pinned to a literal, so an accidental addition still
        // trips this. The @Metadata family is derived from MetadataTableProjector.ProjectedTables
        // (see MetadataSectionNames), so it is counted by derivation rather than re-pinned here —
        // otherwise adding a table to the projector would fail an unrelated test.
        Assert.Equal(55 + MetadataSectionNames.All.Length, pipeline.AllSectionNames.Length);
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
    public void LibraryPipeline_CatalogHiddenSections_AreOutsideBaseScope()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var hidden = pipeline.GetCatalogHiddenSections();

        // The flat -D section list is the base-category union. Domain-only sections remain behind
        // their authored doors even when they are cheap or applicable.

        // Visible spine members are never catalog-hidden — including the now-size-classed
        // sections that used to be opt-in (Switches, Custom Attributes, Non-normalized Paths, ...).
        var visible = new List<string>
        {
            "Library Info", "Symbols", "Signals", "References",
            "Async Methods", "Custom Attributes", "Extension Methods",
            "P/Invoke Methods", "Type Forwarders", "Union Types",
            "Switches", "Resources"
        };
        foreach (var name in visible)
            Assert.DoesNotContain(name, hidden);

        // Performance, integrations, SourceLink, audit-only, exact-only, and coordinate context
        // sections are outside the base scope and therefore hidden from the flat base catalog.
        foreach (var kind in PerformanceKinds.Sections)
            Assert.Contains(kind, hidden);
        foreach (var integration in LibraryIntegrationCatalog.CategorySections.Append(IntegrationSectionNames.Opportunities))
            Assert.Contains(integration, hidden);
        foreach (var footgun in new[]
                 {
                     "Top Leverage", "Unsafe Members", "SourceLink: Integrity",
                     "SourceLink: Files", "SourceLink: Availability", "SourceLink: Missing Files",
                     "Context: Member", "Non-normalized Paths", "Array Pool Escapes"
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

    [Fact]
    public void LibraryPipeline_UnsafeMembersAndBodyShapesAreTheOnlyUncategorizedSections()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var categories = pipeline.GetCategoryMap()
            .Where(pair => pair.Key is not SectionPipeline<LibraryInspection>.AllCategory
                and not SectionPipeline<LibraryInspection>.HiddenCategory)
            .ToArray();
        var categorized = categories
            .SelectMany(pair => pair.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncategorized = pipeline.SelectableSectionNames
            .Where(name => !categorized.Contains(name))
            .ToArray();

        Assert.Equal([SectionNames.UnsafeMembers, SectionNames.BodyShapes], uncategorized);
    }

    [Fact]
    public void LibraryPipeline_BaseScopeIsDerivedFromLibraryAndSurfaceCategories()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var categories = pipeline.GetCategoryMap();

        Assert.Equal(
            [SectionCategoryNames.Library, SectionCategoryNames.Surface],
            pipeline.GetBaseCategoryDoors().OrderBy(name => name, StringComparer.Ordinal));

        var expected = categories[SectionCategoryNames.Library]
            .Concat(categories[SectionCategoryNames.Surface])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.BaseSectionNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryPipeline_SeparateDomainsStayOutsideTheBaseScope()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var baseSections = pipeline.BaseSectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var section in PerformanceKinds.Sections
                     .Concat(MetadataSectionNames.All)
                     .Concat(LibraryIntegrationCatalog.CategorySections)
                     .Concat([
                         IntegrationSectionNames.Opportunities,
                         SectionNames.SourceLinkFiles,
                         SectionNames.SourceLinkAvailability,
                         SectionNames.SourceLinkMissingFiles,
                         SectionNames.SourceLinkIntegrity,
                         SectionNames.TopLeverage,
                         SectionNames.ArrayPoolEscapes
                     ]))
        {
            Assert.DoesNotContain(section, baseSections);
        }
    }

    [Fact]
    public void LibraryPipeline_AutomaticViewsRequestOnlyBaseCategoryProducers()
    {
        var pipeline = LibrarySections.CreatePipeline();

        var detailedQueries = pipeline.GetRequiredQueries(Verbosity.Detailed);

        Assert.DoesNotContain(MetadataImageQuery.Definition, detailedQueries);
        Assert.DoesNotContain(
            AssemblyContextIntegrationsQuery.Definition,
            detailedQueries);
        Assert.DoesNotContain(
            AssemblyContextIntegrationOpportunitiesQuery.Definition,
            detailedQueries);
        Assert.DoesNotContain(BodyShapesQuery.Definition, detailedQueries);
        Assert.DoesNotContain(ResourceTriageQuery.Definition, detailedQueries);
        Assert.DoesNotContain(
            OptimizationOpportunitiesQuery.Definition,
            detailedQueries);
        Assert.DoesNotContain(TopLeverageQuery.Definition, detailedQueries);
    }

    [Fact]
    public void LibraryPipeline_ExplicitDomainOrDirectSelectionStillRequestsItsQueries()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var performance = pipeline.GetCategoryMap()[SectionCategoryNames.Performance]
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, performance);

        Assert.Contains(ResourceTriageQuery.Definition, queries);
        Assert.Contains(
            TopLeverageQuery.Definition,
            queries);
        Assert.Contains(
            OptimizationOpportunitiesQuery.Definition,
            queries);

        Assert.Contains(
            BodyShapesQuery.Definition,
            pipeline.GetRequiredQueries(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    SectionNames.BodyShapes,
                }));
    }

    [Fact]
    public void ResourceTriageQuery_NoMetadata_DoesNotAcquireBodyIndex()
    {
        bool acquired = false;

        ResourceTriageResult result =
            LibrarySections.ExecuteResourceTriageQuery(
                hasMetadata: false,
                () =>
                {
                    acquired = true;
                    throw new InvalidOperationException("must not acquire");
                },
                new FindingSubject("native.dll", "native.dll"));

        Assert.IsType<ResourceTriageResult.NoMetadata>(result);
        Assert.False(acquired);
    }

    [Fact]
    public void ResourceTriageQuery_CompleteEmptyJsonRemainsDistinctFromNoMetadata()
    {
        var inspection = new LibraryInspection();
        var complete =
            new FindingInspection<Analysis.ResourceLifecycleOccurrence>.Complete([]);

        LibraryMetadataService.ApplyResourceTriageResult(
            inspection,
            new ResourceTriageResult.Available(complete, []),
            () => new Dictionary<
                int,
                (string? Stable, string Visibility, string Selector)>());

        string completeJson = JsonSerializer.Serialize(
            inspection,
            JsonContext.Default.LibraryInspection);
        using (JsonDocument document = JsonDocument.Parse(completeJson))
        {
            JsonElement resourceTriage =
                document.RootElement.GetProperty("resource_triage");
            Assert.Equal(JsonValueKind.Array, resourceTriage.ValueKind);
            Assert.Equal(0, resourceTriage.GetArrayLength());
        }

        LibraryMetadataService.ApplyResourceTriageResult(
            inspection,
            new ResourceTriageResult.NoMetadata(),
            () => throw new InvalidOperationException(
                "NoMetadata must not acquire the drill map"));

        string noMetadataJson = JsonSerializer.Serialize(
            inspection,
            JsonContext.Default.LibraryInspection);
        using JsonDocument noMetadataDocument =
            JsonDocument.Parse(noMetadataJson);
        Assert.False(
            noMetadataDocument.RootElement.TryGetProperty(
                "resource_triage",
                out _));
    }

    [Fact]
    public void ResourceTriageQuery_FailureProjectsToArrayPoolEscapes()
    {
        var inspection = new LibraryInspection();
        var error = new InspectionError(
            new FindingSubject("broken.dll", "broken.dll"),
            Analysis.AnalysisFindings.ResourceLifecycleDescriptor,
            "body index failed");

        LibraryMetadataService.ApplyResourceTriageResult(
            inspection,
            new ResourceTriageResult.Failed(error),
            () => throw new InvalidOperationException(
                "failed results must not acquire the drill map"));

        var failed =
            Assert.IsType<ResourceTriageResult.Failed>(
                inspection.ResourceTriageQueryResult);
        Assert.Same(error, failed.Error);
        var projected = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(SectionNames.ArrayPoolEscapes, projected.Section);
        Assert.Equal(
            Analysis.AnalysisFindings.ResourceLifecycleDescriptor.Title,
            projected.Finding);
        Assert.Equal(error.Reason, projected.Reason);
        Assert.Empty(inspection.ResourceTriageAssessments);
        Assert.Null(inspection.ResourceTriage);
    }

    [Fact]
    public void BodyShapesQuery_CompleteEmptyJsonRemainsDistinctFromNoMetadata()
    {
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyBodyShapesResult(
            inspection,
            new Output.VerboseLogger(false),
            new BodyShapesResult.Available(new BodyShapeSearchResult([], [], 0)));

        string completeJson = JsonSerializer.Serialize(
            inspection,
            JsonContext.Default.LibraryInspection);
        using (JsonDocument document = JsonDocument.Parse(completeJson))
        {
            JsonElement bodyShapes =
                document.RootElement.GetProperty("body_shapes");
            Assert.Equal(JsonValueKind.Array, bodyShapes.ValueKind);
            Assert.Equal(0, bodyShapes.GetArrayLength());
        }

        LibraryMetadataService.ApplyBodyShapesResult(
            inspection,
            new Output.VerboseLogger(false),
            new BodyShapesResult.NoMetadata());

        string noMetadataJson = JsonSerializer.Serialize(
            inspection,
            JsonContext.Default.LibraryInspection);
        using JsonDocument noMetadataDocument =
            JsonDocument.Parse(noMetadataJson);
        Assert.False(
            noMetadataDocument.RootElement.TryGetProperty(
                "body_shapes",
                out _));
        Assert.Null(inspection.BodyShapeSearchResult);
    }

    [Fact]
    public void BodyShapesQuery_FailureProjectsToInspectionFailures()
    {
        var inspection = new LibraryInspection();
        var error = new IOException("decompilation failed");

        LibraryMetadataService.ApplyBodyShapesResult(
            inspection,
            new Output.VerboseLogger(false),
            new BodyShapesResult.Failed(error));

        var failed = Assert.IsType<BodyShapesResult.Failed>(
            inspection.BodyShapesQueryResult);
        Assert.Same(error, failed.Error);
        LibraryInspectionFailureJson projected =
            Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(SectionNames.BodyShapes, projected.Section);
        Assert.Equal(BodyShapesQuery.Definition.Name, projected.Finding);
        Assert.Equal(error.Message, projected.Reason);
        Assert.Null(inspection.BodyShapeSearchResult);
    }

    [Fact]
    public void BodyShapesQuery_TypedAbsenceOverridesCompatibilityProjection()
    {
        var compatibility = new BodyShapeSearchResult([], [], 0);
        var inspection = new LibraryInspection
        {
            BodyShapeSearchResult = compatibility,
            BodyShapesQueryResult = new BodyShapesResult.NoMetadata(),
        };

        string json = JsonSerializer.Serialize(
            inspection,
            JsonContext.Default.LibraryInspection);

        Assert.DoesNotContain("\"body_shapes\"", json, StringComparison.Ordinal);
        Assert.Null(new LibraryInspectionView(inspection).BodyShapesSection);
        Assert.False(LibrarySections.BodyShapes.CanRender(inspection));

        inspection.BodyShapesQueryResult = null;

        Assert.Same(compatibility, inspection.EffectiveBodyShapeSearchResult);
        Assert.NotNull(new LibraryInspectionView(inspection).BodyShapesSection);
        Assert.True(LibrarySections.BodyShapes.CanRender(inspection));
    }

    [Fact]
    public void LibraryPipeline_FixedOverviewComesFromBaseCategories()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(
            [SectionNames.LibraryInfo, SectionNames.Symbols, SectionNames.Signals],
            pipeline.FixedOverviewSectionNames);
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

        Assert.DoesNotContain("Integration: Opportunities", effective);
        Assert.Contains("Integration: Opportunities", selected);
    }

    [Fact]
    public void CanRender_OptimizationOpportunities_UsesScannedRows()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            PerformanceTriageOpportunities =
            [
                PerformanceOpportunity("capturing-delegate"),
            ]
        };

        // capturing-delegate buckets into the "Closures and Delegates" kind section.
        const string section = "Performance: Closures and Delegates";
        var effective = pipeline.GetEffectiveSections(model, Verbosity.Detailed);
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section });

        // Having rows makes the section renderable, not automatic: it is backed by the
        // Optimization Opportunities query, which declares Cost=Unbounded, so it leaves the
        // -v:d ladder and is reached through -S or the @Performance door instead. Asserting both
        // directions keeps this test honest about which of the two properties it is pinning.
        Assert.DoesNotContain(section, effective);
        Assert.Contains(section, selected);
    }

    [Fact]
    public void PerformanceDiscovery_StructuralCapabilityDoesNotBecomeFullEffectiveness()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
            PerformanceTriageOpportunities =
            [
                PerformanceOpportunity("capturing-delegate"),
            ]
        };

        var structural = pipeline.GetDiscoverableSections(model);
        var effective = pipeline.GetAvailableSections(model);

        Assert.Contains(SectionNames.PerformanceBoxing, structural);
        Assert.Contains(SectionNames.PerformanceClosures, structural);
        Assert.Contains(SectionNames.TopLeverage, structural);
        Assert.Contains(SectionNames.PerformanceClosures, effective);
        Assert.DoesNotContain(SectionNames.PerformanceBoxing, effective);
        Assert.DoesNotContain(SectionNames.TopLeverage, effective);
        Assert.DoesNotContain(SectionNames.ArrayPoolEscapes, effective);
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
    public void LibrarySourcePlan_InternalDiscoveryScopeDoesNotAuthorizeNetwork()
    {
        var synthesizedBaseScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Library Info",
            "Signals",
            "Symbols",
        };
        var options = new LibraryOptions
        {
            Verbosity = Verbosity.Detailed,
            UserVerbosityOverride = Verbosity.Minimal,
            IncludeSections = synthesizedBaseScope,
            UserIncludeSectionsOverride = [],
        };

        var plan = LibrarySourcePlans.For(options);

        Assert.False(plan.AllowPdbDownload);
        Assert.False(plan.CollectSourceFiles);
        Assert.False(plan.ReadCachedPdb);

        plan = LibrarySourcePlans.For(options with
        {
            UserIncludeSectionsOverride =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" },
        });
        Assert.True(plan.AllowPdbDownload);
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
    public void LibrarySourcePlan_ExplicitLocalDiagnosticsReadCachedPdbWithoutDownloading()
    {
        foreach (string section in new[]
        {
            SectionNames.SourceLinkDiagnostics,
            SectionNames.NonNormalizedPaths,
        })
        {
            var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section };
            var plan = LibrarySourcePlans.For(Verbosity.Quiet, include);

            Assert.False(plan.AllowPdbDownload);
            Assert.False(plan.CollectSourceFiles);
            Assert.True(plan.ReadCachedPdb);
        }
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

                Assert.Equal(
                    expectedPdb,
                    plan.AllowPdbDownload);
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
            Assert.True(section.DownloadPdb || section.ReadCachedPdb);
            Assert.False(section.CollectSourceFiles && !section.DownloadPdb);
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
    public void LibraryPipeline_SignalsDiscoverySeparatesApplicabilityFromEffectiveness()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection { AssemblyInfo = new AssemblyInfo() };

        Assert.Contains(SectionNames.Signals, pipeline.GetDiscoverableSections(model));
        Assert.DoesNotContain(SectionNames.Signals, pipeline.GetAvailableSections(model));

        model.AuditSignals = [new AuditSignal("Provenance", "SourceLink", "Present", "test")];

        Assert.Contains(SectionNames.Signals, pipeline.GetAvailableSections(model));
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

    [Fact]
    public void GetRequiredQueries_ExcludeUnbounded_PreservesTypedBoundedSelection()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string>
        {
            SectionNames.TopLeverage,
            LibrarySections.PInvokeMethods.Name,
        };

        Assert.Equal(
            [
                ClassifiedMethodsQuery.Definition,
                TopLeverageQuery.Definition,
            ],
            pipeline.GetRequiredQueries(Verbosity.Detailed, include));
        Assert.Equal(
            [ClassifiedMethodsQuery.Definition],
            pipeline.GetRequiredQueries(
                Verbosity.Detailed,
                include,
                excludeUnbounded: true));
    }

    [Fact]
    public void LibraryPipeline_UnsafeMembers_UsesTypedQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Unsafe Members", "P/Invoke Methods" };

        Assert.Equal(
            [
                ClassifiedMethodsQuery.Definition,
                UnsafeEvidenceQuery.Definition,
            ],
            pipeline.GetRequiredQueries(Verbosity.Minimal, include));
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
            ("package", PackageSectionDescriptors.CreatePipeline().AllSectionNames,
                PackageSectionDescriptors.CreatePipeline().GetCategoryMap()),
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
                SectionNames.PInvokeMethods,
                SectionNames.NonNormalizedPaths,
                SectionNames.SourceLinkDiagnostics,
                SectionNames.Signals,
                SectionNames.IdentifierConfusion,
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
    public void LibraryPipeline_TargetedCustomAttributes_OnlyRequiresItsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Custom Attributes" };

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, include);

        Assert.Equal([CustomAttributesQuery.Definition], queries);
    }

    [Fact]
    public void LibraryPipeline_TargetedResources_OnlyRequiresItsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Resources" };

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, include);

        Assert.Equal([ResourcesQuery.Definition], queries);
    }

    [Fact]
    public void LibraryPipeline_TargetedTypeForwarders_OnlyRequiresItsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Type Forwarders" };

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, include);

        Assert.Equal([TypeForwardersQuery.Definition], queries);
    }

    [Fact]
    public void LibraryPipeline_TargetedUnionTypes_OnlyRequiresItsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Union Types" };

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, include);

        Assert.Equal([UnionTypesQuery.Definition], queries);
    }

    [Fact]
    public void LibraryPipeline_TargetedSwitches_OnlyRequiresItsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { "Switches" };

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, include);

        Assert.Equal([SwitchesQuery.Definition], queries);
    }

    [Theory]
    [InlineData(SectionNames.PInvokeMethods)]
    [InlineData(SectionNames.AsyncMethods)]
    public void LibraryPipeline_TargetedClassifiedMethodSection_OnlyRequiresItsQuery(
        string section)
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { section };

        var queries = pipeline.GetRequiredQueries(Verbosity.Minimal, include);

        Assert.Equal([ClassifiedMethodsQuery.Definition], queries);
    }

    [Fact]
    public void LibraryPipeline_Signals_DeclaresOnlyItsTypedInputs()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string> { SectionNames.Signals };

        Assert.Equal(
            [
                AssemblyReferencesQuery.Definition,
                AuditMetadataQuery.Definition,
                ClassifiedMethodsQuery.Definition,
            ],
            pipeline.GetRequiredQueries(Verbosity.Minimal, include)
                .OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryQueryRegistry_RegistrationMatchesDeclaration()
    {
        LibrarySectionCatalog catalog = LibrarySections.CreateCatalog();
        var pipeline = catalog.Pipeline;
        HashSet<InspectionQueryDefinition> perAssemblyQueries =
        [
            .. pipeline.DeclaredQueries.Where(
                catalog.QueryCatalog.RegisteredQueries.Contains),
        ];
        HashSet<InspectionQueryDefinition> groupQueries =
        [
            .. pipeline.DeclaredQueries.Where(
                catalog.GroupQueryCatalog.RegisteredQueries.Contains),
        ];
        HashSet<InspectionQueryDefinition> commandQueries =
        [
            .. LibraryCommand.DiscoveryQueries.Select(demand => demand.Query),
            .. LibraryCommand.BareDiscoveryQueries.Select(demand => demand.Query),
        ];
        perAssemblyQueries.UnionWith(commandQueries);
        HashSet<InspectionQueryDefinition> closure =
            catalog.QueryCatalog.ExpandRequired(perAssemblyQueries);
        closure.UnionWith(
            catalog.GroupQueryCatalog.ExpandRequired(groupQueries));
        HashSet<InspectionQueryDefinition> registered =
        [
            .. catalog.QueryCatalog.RegisteredQueries,
            .. catalog.GroupQueryCatalog.RegisteredQueries,
        ];

        Assert.Empty(
            catalog.QueryCatalog.RegisteredQueries.Intersect(
                catalog.GroupQueryCatalog.RegisteredQueries));
        Assert.Equal(
            closure.OrderBy(q => q.Name, StringComparer.Ordinal),
            registered.OrderBy(q => q.Name, StringComparer.Ordinal));
        Assert.Equal(
            pipeline.DeclaredQueries.Union(commandQueries).OrderBy(
                query => query.Name,
                StringComparer.Ordinal),
            perAssemblyQueries.Union(groupQueries).OrderBy(
                query => query.Name,
                StringComparer.Ordinal));
        Assert.Equal(
            [
                AssemblyContextIntegrationOpportunitiesQuery.Definition,
                AssemblyContextIntegrationsQuery.Definition,
                AssemblyReferencesQuery.Definition,
                AuditMetadataQuery.Definition,
                BodyShapesQuery.Definition,
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                MetadataImageQuery.Definition,
                OptimizationOpportunitiesQuery.Definition,
                ResourceTriageQuery.Definition,
                ResourcesQuery.Definition,
                SourceAvailabilityQuery.Definition,
                SourceIntegrityQuery.Definition,
                SwitchesQuery.Definition,
                TopLeverageQuery.Definition,
                TypeForwardersQuery.Definition,
                UnionTypesQuery.Definition,
                UnsafeEvidenceQuery.Definition,
            ],
            pipeline.DeclaredQueries.OrderBy(q => q.Name, StringComparer.Ordinal));
        Assert.Equal(
            [OptimizationOpportunitiesQuery.Definition],
            catalog.QueryCatalog.OptionalDependenciesOf(
                BodyShapesQuery.Definition));
    }

    [Fact]
    public void LibrarySourceLinkSections_DemandSharedTypedQueries()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var registry = LibrarySections.CreateQueryRegistry();

        HashSet<InspectionQueryDefinition> availability = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.SourceLinkAvailability,
                SectionNames.SourceLinkMissingFiles,
            });
        HashSet<InspectionQueryDefinition> integrity = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.SourceLinkIntegrity,
            });

        Assert.Equal([SourceAvailabilityQuery.Definition], availability);
        Assert.Equal([SourceIntegrityQuery.Definition], integrity);
        Assert.Equal(
            [SourceAvailabilityQuery.Definition, SourceLinkDocumentsQuery.Definition],
            registry.ExpandRequired(availability).OrderBy(q => q.Name, StringComparer.Ordinal));
        Assert.Equal(
            [SourceLinkDocumentsQuery.Definition, SourceIntegrityQuery.Definition],
            registry.ExpandRequired(integrity).OrderBy(q => q.Name, StringComparer.Ordinal));
        Assert.Equal(InspectionCost.Moderated, registry.CostOf(SourceLinkDocumentsQuery.Definition));
        Assert.Equal(InspectionCost.Unbounded, registry.CostOf(SourceAvailabilityQuery.Definition));
        Assert.Equal(InspectionCost.Unbounded, registry.CostOf(SourceIntegrityQuery.Definition));
    }

    [Fact]
    public void PackageSourceLinkSections_ShareTheQueryFamily()
    {
        PackageSectionCatalog catalog = PackageSectionDescriptors.CreateCatalog();
        HashSet<InspectionQueryDefinition> availability = catalog.Pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkMissingFiles,
            });
        HashSet<InspectionQueryDefinition> integrity = catalog.Pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.SourceLinkIntegrity,
            });

        Assert.Equal([SourceAvailabilityQuery.Definition], availability);
        Assert.Equal([SourceIntegrityQuery.Definition], integrity);
        Assert.Equal(
            catalog.QueryCatalog
                .ExpandRequired(availability.Concat(integrity))
                .OrderBy(q => q.Name, StringComparer.Ordinal),
            catalog.QueryCatalog.RegisteredQueries.OrderBy(q => q.Name, StringComparer.Ordinal));

        var categories = catalog.Pipeline.GetCategoryMap();
        Assert.Equal(
            [
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkFiles,
                PackageSections.SourceLinkIntegrity,
                PackageSections.SourceLinkMissingFiles,
            ],
            categories[SectionCategoryNames.SourceLink].OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void DiffQueryCatalog_RegistrationMatchesDeclaration()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();

        HashSet<InspectionQueryDefinition> closure =
            catalog.QueryCatalog.ExpandRequired(catalog.Sections.DeclaredQueries);

        Assert.Equal(
            closure.OrderBy(query => query.Name, StringComparer.Ordinal),
            catalog.QueryCatalog.RegisteredQueries.OrderBy(
                query => query.Name,
                StringComparer.Ordinal));
        Assert.Equal(
            [
                ApiComparisonQuery.Definition,
                BodySignalComparisonQuery.Definition,
                ImplementationComparisonQuery.Definition,
            ],
            catalog.Pipeline.DeclaredQueries);
    }

    [Fact]
    public void DiffSectionCatalog_UsesCompiledDomainLens()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();

        Assert.Same(DiffSections.Domain, catalog.Lens.Domain);
        Assert.Same(DiffSections.Lens, catalog.Lens);
        Assert.Same(DiffSections.QueryCatalog, catalog.QueryCatalog);
        Assert.Same(DiffSections.SectionCatalog, catalog.Sections);
    }

    [Fact]
    public void DiffComparisonSections_DemandTheirProducerQueriesAndCosts()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();
        var changes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.Changes.Name,
        };
        var analysis = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.AnalysisDiff.Name,
        };
        var implementation = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.ImplementationDiff.Name,
        };

        Assert.Equal(
            [ApiComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(Verbosity.Minimal, changes));
        Assert.Equal(
            [ApiComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(Verbosity.Minimal));
        Assert.Equal(
            [BodySignalComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(Verbosity.Minimal, analysis));
        Assert.Equal(
            [ImplementationComparisonQuery.Definition],
            catalog.Pipeline.GetRequiredQueries(
                Verbosity.Minimal,
                implementation));
        Assert.Equal(
            SectionCost.NetworkFree,
            Assert.Single(
                catalog.Pipeline.SectionCosts,
                section => section.Name == DiffSections.Changes.Name).Cost);
        Assert.Equal(
            SectionCost.Unbounded,
            Assert.Single(
                catalog.Pipeline.SectionCosts,
                section => section.Name == DiffSections.AnalysisDiff.Name).Cost);
        Assert.Equal(
            SectionCost.Unbounded,
            Assert.Single(
                catalog.Pipeline.SectionCosts,
                section => section.Name
                    == DiffSections.ImplementationDiff.Name).Cost);
    }

    [Fact]
    public void DiffQueryCatalog_RunsOnlySelectedSectionDemand()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();
        var analysis = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DiffSections.AnalysisDiff.Name,
        };
        int analysisInputsCreated = 0;
        var analysisContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            () =>
            {
                analysisInputsCreated++;
                return new BodySignalComparisonInput([], []);
            });
        List<InspectionQueryDefinition> analysisExecuted = [];

        catalog.Lens.Plan(
            Verbosity.Minimal,
            analysis).Run(
            analysisContext,
            (query, _) => analysisExecuted.Add(query));

        Assert.Equal([BodySignalComparisonQuery.Definition], analysisExecuted);
        Assert.Equal(1, analysisInputsCreated);

        var changesContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            () => throw new InvalidOperationException(
                "Changes-only demand must not acquire Analysis indexes."),
            () => throw new InvalidOperationException(
                "Changes-only demand must not acquire Implementation inputs."));
        List<InspectionQueryDefinition> changesExecuted = [];
        catalog.Lens.Plan(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.Changes.Name,
                }).Run(
            changesContext,
            (query, _) => changesExecuted.Add(query));

        Assert.Equal([ApiComparisonQuery.Definition], changesExecuted);

        int implementationInputsCreated = 0;
        var implementationContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            createImplementationComparisonInput: () =>
            {
                implementationInputsCreated++;
                return new ImplementationComparisonInput([], []);
            });
        List<InspectionQueryDefinition> implementationExecuted = [];
        catalog.Lens.Plan(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.ImplementationDiff.Name,
                }).Run(
            implementationContext,
            (query, _) => implementationExecuted.Add(query));

        Assert.Equal(
            [ImplementationComparisonQuery.Definition],
            implementationExecuted);
        Assert.Equal(1, implementationInputsCreated);

        int analysisComposedInputsCreated = 0;
        int implementationComposedInputsCreated = 0;
        var composedContext = new DiffQueryContext(
            new ApiSurface(),
            new ApiSurface(),
            () =>
            {
                analysisComposedInputsCreated++;
                return new BodySignalComparisonInput([], []);
            },
            () =>
            {
                implementationComposedInputsCreated++;
                return new ImplementationComparisonInput([], []);
            });
        List<InspectionQueryDefinition> composedExecuted = [];
        catalog.Lens.Plan(
                Verbosity.Minimal,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    DiffSections.Changes.Name,
                    DiffSections.AnalysisDiff.Name,
                    DiffSections.ImplementationDiff.Name,
                }).Run(
            composedContext,
            (query, _) => composedExecuted.Add(query));

        Assert.Equal(
            [
                ApiComparisonQuery.Definition,
                BodySignalComparisonQuery.Definition,
                ImplementationComparisonQuery.Definition,
            ],
            composedExecuted);
        Assert.Equal(1, analysisComposedInputsCreated);
        Assert.Equal(1, implementationComposedInputsCreated);
    }

    [Fact]
    public void DiffCommand_AllocRegressionsRequestsAnalysisWithoutUnusedChanges()
    {
        DiffSectionCatalog catalog = DiffSections.CreateCatalog();

        CompiledInspectionPlan<DiffQueryContext> singleSection =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    AllocRegressionsOnly = true,
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.Changes.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> composedDocument =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    AllocRegressionsOnly = true,
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.Changes.Name,
                        DiffSections.AnalysisDiff.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> implementationSelection =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    AllocRegressionsOnly = true,
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.ImplementationDiff.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> implementationOnly =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.ImplementationDiff.Name,
                    },
                });
        CompiledInspectionPlan<DiffQueryContext> findingTransitionsOnly =
            DiffCommand.GetRequestedQueryPlan(
                catalog,
                new DiffOptions
                {
                    IncludeSections = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase)
                    {
                        DiffSections.FindingTransitions.Name,
                    },
                });

        Assert.Equal(
            [BodySignalComparisonQuery.Definition],
            singleSection.QueryPlan.Queries);
        Assert.Equal(
            [BodySignalComparisonQuery.Definition],
            implementationSelection.QueryPlan.Queries);
        Assert.Equal(
            [ImplementationComparisonQuery.Definition],
            implementationOnly.QueryPlan.Queries);
        Assert.Equal(
            [
                ApiComparisonQuery.Definition,
                BodySignalComparisonQuery.Definition,
            ],
            composedDocument.QueryPlan.Queries);
        Assert.Empty(findingTransitionsOnly.RequestedQueries);
        Assert.Empty(findingTransitionsOnly.QueryPlan.Queries);
    }

    [Fact]
    public async Task PackageIntegrityExitCode_FailsForMismatchesAndAuditFailures()
    {
        var clean = new InspectionResult
        {
            SourceIntegrity = new PackageSourceIntegrity(
                1,
                1,
                Verified: 0,
                Mismatched: 0,
                LineEndingNormalized: 0,
                Unverifiable: 1,
                MismatchedFiles: null,
                UnavailableLibraries: null,
                FailedLibraries: null),
        };
        var mismatch = new InspectionResult
        {
            SourceIntegrity = clean.SourceIntegrity with { Mismatched = 1 },
        };
        var auditFailure = new InspectionResult
        {
            IdentifierConfusionFailure =
                IdentifierConfusionAuditFailureKind
                    .PackageMetadataUnavailable,
        };

        var (_, error) = await ConsoleCapture.RunAsync(() =>
        {
            Assert.Equal(0, PackageCommand.PackageIntegrityExitCode(clean));
            Assert.Equal(1, PackageCommand.PackageIntegrityExitCode(clean, mismatch));
            Assert.Equal(
                1,
                PackageCommand.PackageIntegrityExitCode(
                    clean,
                    auditFailure));
            Assert.Equal(1, PackageCommand.PackageIntegrityExitCode(1, clean));
            Assert.Equal(7, PackageCommand.PackageIntegrityExitCode(7, clean));
            Assert.Equal(1, PackageCommand.PackageIntegrityExitCode(0, mismatch));
        });

        Assert.Equal(
            "Warning: Identifier audit failed for package input #2: "
            + "package registry metadata unavailable"
            + Environment.NewLine,
            error);
    }

    [Theory]
    [InlineData(PackageSections.AuditIdentifierConfusion)]
    [InlineData(PackageSections.AuditArtifactText)]
    public void MultiPackageCount_CountsSelectedAuditRows(
        string section)
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-audit-count-{Guid.NewGuid():N}.txt");
        InspectionResult Result(string suffix) =>
            section == PackageSections.AuditIdentifierConfusion
                ? new InspectionResult
                {
                    PackageName = $"\u0405ystem.{suffix}",
                    Version = "1.0.0",
                }
                : new InspectionResult
                {
                    PackageName = $"Package.{suffix}",
                    Version = "1.0.0",
                    PackageFiles =
                    [
                        new PackageFile(
                            $"lib/{suffix}\u001b.dll",
                            1),
                    ],
                };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [Result("One"), Result("Two")],
                new InspectionOptions
                {
                    Count = true,
                    JsonOutput = true,
                    IncludeSections =
                        new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            section,
                        },
                    OutputPath = outputPath,
                },
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal(
                "2",
                File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_PreservesSelectedSectionMap()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-count-map-{Guid.NewGuid():N}.txt");
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            IncludeSections =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.PackageInfo,
                    PackageSections.TargetFrameworks,
                },
            OutputPath = outputPath,
        };
        var results = new[]
        {
            new InspectionResult
            {
                PackageName = "One",
                Version = "1.0.0",
                TargetFrameworks = ["net8.0"],
            },
            new InspectionResult
            {
                PackageName = "Two",
                Version = "1.0.0",
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                results,
                options,
                PackageSectionDescriptors.CreatePipeline());
            string output = File.ReadAllText(outputPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("| Section | Count |", output);
            Assert.Contains("| Package Info |", output);
            Assert.Contains("| Target Frameworks | 1 |", output);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_PreservesFixedOverviewMap()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-fixed-count-map-{Guid.NewGuid():N}.txt");
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult
                    {
                        PackageName = "One",
                        Version = "1.0.0",
                    },
                ],
                new InspectionOptions
                {
                    Count = true,
                    JsonOutput = true,
                    FixedOverview = true,
                    OutputPath = outputPath,
                },
                pipeline);
            string output = File.ReadAllText(outputPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("| Section | Count |", output);
            foreach (string section in pipeline.BareSelectSectionNames)
                Assert.Contains($"| {section} |", output);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_PreservesIntegrityMismatchExitCode()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"package-count-{Guid.NewGuid():N}.txt");
        var clean = new InspectionResult
        {
            PackageName = "Clean",
            SourceIntegrity = new PackageSourceIntegrity(
                1,
                1,
                Verified: 1,
                Mismatched: 0,
                LineEndingNormalized: 0,
                Unverifiable: 0,
                MismatchedFiles: null,
                UnavailableLibraries: null,
                FailedLibraries: null),
        };
        var mismatch = new InspectionResult
        {
            PackageName = "Mismatch",
            SourceIntegrity = clean.SourceIntegrity with { Mismatched = 1 },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [clean, mismatch],
                new InspectionOptions
                {
                    Count = true,
                    IncludeSections = new HashSet<string> { PackageSections.Files },
                    OutputPath = outputPath,
                },
                PackageSectionDescriptors.CreateCatalog().Pipeline);

            Assert.Equal(1, exitCode);
            Assert.Equal("2", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_AggregatesSelectedSignatureRows()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-signature-count-{Guid.NewGuid():N}.txt");
        var signature = new SignatureVerificationResult
        {
            AuthorVerified = true,
            Publisher = "Publisher",
            Repository = "nuget.org",
            RepositoryVerified = true,
        };
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            OutputPath = outputPath,
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.Signature,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult
                    {
                        PackageName = "First",
                        SignatureResult = signature,
                    },
                    new InspectionResult
                    {
                        PackageName = "Second",
                        SignatureResult = signature,
                    },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal("10", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_AppliesRowWindowToCombinedPackageInfoRows()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-info-count-{Guid.NewGuid():N}.txt");
        var options = new InspectionOptions
        {
            Count = true,
            OutputPath = outputPath,
            Rows = RowWindow.Head(1),
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.PackageInfo,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult { PackageName = "First" },
                    new InspectionResult { PackageName = "Second" },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal("1", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_JsonFileSelectionUsesCombinedRowShape()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-file-count-{Guid.NewGuid():N}.txt");
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            OutputPath = outputPath,
            Rows = RowWindow.Head(1),
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.FilesReadme,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult { PackageName = "First" },
                    new InspectionResult { PackageName = "Second" },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            Assert.Equal("1", File.ReadAllText(outputPath).Trim());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void MultiPackageCount_AggregatesMultipleSelectedSections()
    {
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"package-section-counts-{Guid.NewGuid():N}.txt");
        var signature = new SignatureVerificationResult
        {
            AuthorVerified = true,
            Publisher = "Publisher",
            Repository = "nuget.org",
            RepositoryVerified = true,
        };
        var options = new InspectionOptions
        {
            Count = true,
            JsonOutput = true,
            OutputPath = outputPath,
            IncludeSections = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                PackageSections.PackageInfo,
                PackageSections.Signature,
            },
        };

        try
        {
            int exitCode = PackageCommand.WriteMultiPackageCount(
                [
                    new InspectionResult
                    {
                        PackageName = "First",
                        SignatureResult = signature,
                    },
                    new InspectionResult
                    {
                        PackageName = "Second",
                        SignatureResult = signature,
                    },
                ],
                options,
                PackageSectionDescriptors.CreatePipeline());

            Assert.Equal(0, exitCode);
            string output = File.ReadAllText(outputPath);
            Assert.Contains("| Package Info |", output);
            Assert.Contains("| Signature | 10 |", output);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void LibraryReferencesSection_DemandsTypedAssemblyReferencesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        HashSet<string> references =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.References,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            references);

        Assert.Equal([AssemblyReferencesQuery.Definition], required);
    }

    [Fact]
    public void LibraryIdentifierConfusionSection_DemandsTypedAssemblyReferencesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        HashSet<string> identifierAudit =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.IdentifierConfusion,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            identifierAudit);

        Assert.Equal([AssemblyReferencesQuery.Definition], required);
    }

    [Fact]
    public void AssemblyReferencesQuery_ReturnsDirectReferencesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);

        var result = Assert.IsType<AssemblyReferencesResult.Available>(
            AssemblyReferencesQuery.Execute(session));

        Assert.Equal(
            session.AssemblyReferenceIdentities().OrderBy(reference => reference.Name),
            result.Identities.OrderBy(reference => reference.Name));
    }

    [Fact]
    public void LibraryInfoAndExtensionMethodsSections_ShareTypedExtensionMethodsQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                ExtensionMethodsQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.ExtensionMethods,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.ExtensionMethods, SectionNames.LibraryInfo],
            boundSections);
        Assert.Equal(
            [
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryInfoAndCustomAttributesSections_ShareTypedCustomAttributesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                CustomAttributesQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.CustomAttributes,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.CustomAttributes, SectionNames.LibraryInfo],
            boundSections);
        Assert.Equal(
            [
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryInfoAndResourcesSections_ShareTypedResourcesQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                ResourcesQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.Resources,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.LibraryInfo, SectionNames.Resources],
            boundSections);
        Assert.Equal(
            [
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void LibraryInfoAndTypeForwardersSections_ShareTypedTypeForwardersQuery()
    {
        var pipeline = LibrarySections.CreatePipeline();
        string[] boundSections = pipeline.QueryBoundSections
            .Where(binding => ReferenceEquals(
                binding.Query,
                TypeForwardersQuery.Definition))
            .Select(binding => binding.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> sections =
            new(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.LibraryInfo,
                SectionNames.TypeForwarders,
            };

        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal,
            sections);

        Assert.Equal(
            [SectionNames.LibraryInfo, SectionNames.TypeForwarders],
            boundSections);
        Assert.Equal(
            [
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                ResourcesQuery.Definition,
                TypeForwardersQuery.Definition,
            ],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void TypeForwardersQuery_ReturnsMetadataOrderedForwardersFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);

        var result = Assert.IsType<TypeForwardersResult.Available>(
            TypeForwardersQuery.Execute(session));

        Assert.Contains(
            result.Forwarders,
            forwarder =>
                forwarder.TypeName == "ILInspector.Metadata.SignatureBlobGuard"
                && forwarder.TargetAssembly == "ILInspector.MetadataPrimitives");
        Assert.Equal(session.TypeForwarders(), result.Forwarders);
    }

    [Fact]
    public void TypeForwardersQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TypeForwardersQuery.Definition],
            context);
        var forwarders = Assert.IsType<TypeForwardersResult.Available>(
            results.Get(TypeForwardersQuery.Definition));

        Assert.Contains(
            forwarders.Forwarders,
            forwarder => forwarder.TypeName == "ILInspector.Metadata.SignatureBlobGuard");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void TypeForwardersQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TypeForwardersQuery.Definition],
            context);
        var failure = Assert.IsType<TypeForwardersResult.Failed>(
            results.Get(TypeForwardersQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void TypeForwardersQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<TypeForwardersResult.Failed>(
            TypeForwardersQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void TypeForwardersQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary = typeof(AssemblyInspectionSession).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            Assert.NotEmpty(canarySession.TypeForwarders());
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TypeForwardersQuery.Definition],
            context);
        var failure = Assert.IsType<TypeForwardersResult.Failed>(
            results.Get(TypeForwardersQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void TypeForwardersQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<TypeForwardersResult.Failed>(
            TypeForwardersQuery.Execute(session));
        LibraryMetadataService.ApplyTypeForwardersResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<TypeForwarderInfo>.Failed>(
            model.TypeForwarderInspection!.Value);
        Assert.Null(model.TypeForwarders);
        Assert.Equal("Type Forwarders", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void UnionTypesQuery_ReturnsMetadataOrderedUnionsFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SampleDiscoveredUnion).Assembly.Location);

        var result = Assert.IsType<UnionTypesResult.Available>(
            UnionTypesQuery.Execute(session));

        Assert.Contains(
            result.Unions,
            union => union.TypeName == typeof(SampleDiscoveredUnion).FullName);
        Assert.Equal(
            session.UnionTypes().Select(union =>
                (union.TypeName,
                    union.Kind,
                    union.ImplementsIUnion,
                    Cases: string.Join('\n', union.CaseTypes))),
            result.Unions.Select(union =>
                (union.TypeName,
                    union.Kind,
                    union.ImplementsIUnion,
                    Cases: string.Join('\n', union.CaseTypes))));
    }

    [Fact]
    public void UnionTypesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(SampleDiscoveredUnion).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnionTypesQuery.Definition],
            context);
        var unions = Assert.IsType<UnionTypesResult.Available>(
            results.Get(UnionTypesQuery.Definition));

        Assert.Contains(
            unions.Unions,
            union => union.TypeName == typeof(SampleDiscoveredUnion).FullName);
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void UnionTypesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnionTypesQuery.Definition],
            context);
        var failure = Assert.IsType<UnionTypesResult.Failed>(
            results.Get(UnionTypesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void UnionTypesQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(SampleDiscoveredUnion).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<UnionTypesResult.Failed>(
            UnionTypesQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void UnionTypesQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary = typeof(SampleDiscoveredUnion).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            Assert.Contains(
                canarySession.UnionTypes(),
                union => union.TypeName == typeof(SampleDiscoveredUnion).FullName);
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnionTypesQuery.Definition],
            context);
        var failure = Assert.IsType<UnionTypesResult.Failed>(
            results.Get(UnionTypesQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void UnionTypesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<UnionTypesResult.Failed>(
            UnionTypesQuery.Execute(session));
        LibraryMetadataService.ApplyUnionTypesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<UnionTypeInfo>.Failed>(
            model.UnionTypeInspection!.Value);
        Assert.Null(model.UnionTypes);
        Assert.Equal("Union Types", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void ClassifiedMethodsQuery_ReturnsMetadataOrderedMethodsFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SampleUnsafeClass).Assembly.Location);

        var result = Assert.IsType<ClassifiedMethodsResult.Available>(
            ClassifiedMethodsQuery.Execute(session));

        Assert.Contains(
            result.Methods,
            method => method.MethodName == nameof(SampleUnsafeClass.UnsafePointerMethod)
                && method.Classification == MethodClassification.Unsafe);
        Assert.Equal(session.ClassifiedMethods(), result.Methods);
    }

    [Fact]
    public void ClassifiedMethodsQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(SampleUnsafeClass).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ClassifiedMethodsQuery.Definition],
            context);
        var methods = Assert.IsType<ClassifiedMethodsResult.Available>(
            results.Get(ClassifiedMethodsQuery.Definition));

        Assert.Contains(
            methods.Methods,
            method => method.MethodName == nameof(SampleUnsafeClass.UnsafePointerMethod));
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void ClassifiedMethodsQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ClassifiedMethodsQuery.Definition],
            context);
        var failure = Assert.IsType<ClassifiedMethodsResult.Failed>(
            results.Get(ClassifiedMethodsQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ClassifiedMethodsQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(SampleUnsafeClass).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<ClassifiedMethodsResult.Failed>(
            ClassifiedMethodsQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void ClassifiedMethodsQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary = typeof(SampleUnsafeClass).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            var canary = Assert.IsType<ClassifiedMethodsResult.Available>(
                ClassifiedMethodsQuery.Execute(canarySession));
            Assert.Contains(
                canary.Methods,
                method => method.MethodName == nameof(SampleUnsafeClass.UnsafePointerMethod));
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ClassifiedMethodsQuery.Definition],
            context);
        var failure = Assert.IsType<ClassifiedMethodsResult.Failed>(
            results.Get(ClassifiedMethodsQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ClassifiedMethodsQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<ClassifiedMethodsResult.Failed>(
            ClassifiedMethodsQuery.Execute(session));
        LibraryMetadataService.ApplyClassifiedMethodsResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(
            model.ClassifiedMethodInspection!.Value);
        Assert.Null(model.PInvokeMethods);
        Assert.Null(model.AsyncMethods);
        Assert.Equal("Classified Methods", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void AuditMetadataQuery_ReturnsFactsFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(MethodClassificationScannerTests).Assembly.Location);

        var result = Assert.IsType<AuditMetadataResult.Available>(
            AuditMetadataQuery.Execute(session));

        Assert.True(result.Metadata.PInvokeMethodCount >= 2);
    }

    [Fact]
    public void AuditMetadataQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(MethodClassificationScannerTests).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [AuditMetadataQuery.Definition],
            context);
        var metadata = Assert.IsType<AuditMetadataResult.Available>(
            results.Get(AuditMetadataQuery.Definition));

        Assert.True(metadata.Metadata.PInvokeMethodCount >= 2);
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void AuditMetadataQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [AuditMetadataQuery.Definition],
            context);
        var failure = Assert.IsType<AuditMetadataResult.Failed>(
            results.Get(AuditMetadataQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void AuditMetadataQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(MethodClassificationScannerTests).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<AuditMetadataResult.Failed>(
            AuditMetadataQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void AuditMetadataQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary =
            typeof(MethodClassificationScannerTests).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            var canary = Assert.IsType<AuditMetadataResult.Available>(
                AuditMetadataQuery.Execute(canarySession));
            Assert.True(canary.Metadata.PInvokeMethodCount >= 2);
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [AuditMetadataQuery.Definition],
            context);
        var failure = Assert.IsType<AuditMetadataResult.Failed>(
            results.Get(AuditMetadataQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void AuditMetadataQuery_FailureStillComposesModelDerivedSignals()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection
        {
            HasSourceLink = false,
            PdbLocation = "standalone",
        };

        var result = Assert.IsType<AuditMetadataResult.Failed>(
            AuditMetadataQuery.Execute(session));
        LibraryMetadataService.ApplyAuditMetadataResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.Null(model.AuditMetadata);
        Assert.NotNull(model.AuditSignals);
        Assert.Contains(
            model.AuditSignals,
            signal => signal.Signal == "SourceLink"
                && signal.Value == "Not found");
    }

    [Fact]
    public void SwitchesQuery_ReturnsOrderedCompositeSwitchesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location);

        var result = Assert.IsType<SwitchesResult.Available>(
            SwitchesQuery.Execute(session));

        Assert.Contains(
            result.Switches,
            item => item is
            {
                Kind: "AppContext",
                Switch: "DotnetInspector.Fixtures.AppContextOnly",
            });
        Assert.Single(
            result.Switches,
            item => item.Switch == "DotnetInspector.Fixtures.Duplicate");
        Assert.DoesNotContain(
            result.Switches,
            item => item.Switch.StartsWith("TestSwitch.", StringComparison.Ordinal)
                || item.Switch.StartsWith("Switch.", StringComparison.Ordinal)
                || item.Switch.StartsWith(
                    "System.Resources.UseSystemResourceKeys",
                    StringComparison.Ordinal));
        Assert.Equal(
            result.Switches
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Switch, StringComparer.Ordinal)
                .ThenBy(item => item.Api, StringComparer.Ordinal),
            result.Switches);
    }

    [Fact]
    public void SwitchesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [SwitchesQuery.Definition],
            context);
        var switches = Assert.IsType<SwitchesResult.Available>(
            results.Get(SwitchesQuery.Definition));

        Assert.Contains(
            switches.Switches,
            item => item.Switch == "DotnetInspector.Fixtures.AppContextOnly");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void SwitchesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [SwitchesQuery.Definition],
            context);
        var failure = Assert.IsType<SwitchesResult.Failed>(
            results.Get(SwitchesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void SwitchesQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<SwitchesResult.Failed>(
            SwitchesQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void SwitchesQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        string reopenCanary =
            typeof(DotnetInspector.Fixtures.AppContextSwitchFixture).Assembly.Location;
        using (var canarySession = AssemblyInspectionSession.Open(reopenCanary))
        {
            var canary = Assert.IsType<SwitchesResult.Available>(
                SwitchesQuery.Execute(canarySession));
            Assert.Contains(
                canary.Switches,
                item => item.Switch == "DotnetInspector.Fixtures.AppContextOnly");
        }

        using var context = new InspectionQueryContext
        {
            AssemblyPath = reopenCanary,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [SwitchesQuery.Definition],
            context);
        var failure = Assert.IsType<SwitchesResult.Failed>(
            results.Get(SwitchesQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void SwitchesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<SwitchesResult.Failed>(
            SwitchesQuery.Execute(session));
        LibraryMetadataService.ApplySwitchesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<SwitchInfo>.Failed>(
            model.SwitchInspection!.Value);
        Assert.Null(model.Switches);
        Assert.Equal("Switches", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void ResourcesQuery_ReturnsManifestResourcesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(LibraryInspection).Assembly.Location);

        var result = Assert.IsType<ResourcesResult.Available>(
            ResourcesQuery.Execute(session));

        Assert.Contains(
            result.Resources,
            resource => resource.Name.Contains("SKILL.md", StringComparison.Ordinal));
        Assert.Equal(session.Resources(), result.Resources);
    }

    [Fact]
    public void ResourcesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ResourcesQuery.Definition],
            context);
        var resources = Assert.IsType<ResourcesResult.Available>(
            results.Get(ResourcesQuery.Definition));

        Assert.Contains(
            resources.Resources,
            resource => resource.Name.Contains("SKILL.md", StringComparison.Ordinal));
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void ResourcesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ResourcesQuery.Definition],
            context);
        var failure = Assert.IsType<ResourcesResult.Failed>(
            results.Get(ResourcesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ResourcesQuery_DisposedBorrowedSessionRemainsTyped()
    {
        using var lender = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        var session = AssemblyInspectionSession.Borrow(lender);
        session.Dispose();

        var failure = Assert.IsType<ResourcesResult.Failed>(
            ResourcesQuery.Execute(session));

        Assert.IsType<ObjectDisposedException>(failure.Error);
    }

    [Fact]
    public void ResourcesQuery_RetainedImageFailureDoesNotReopenPath()
    {
        using var metadataContext = PdbContext.Open(
            typeof(LibraryInspection).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(AssemblyInspectionSession).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };
        metadataContext.Dispose();

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ResourcesQuery.Definition],
            context);
        var failure = Assert.IsType<ResourcesResult.Failed>(
            results.Get(ResourcesQuery.Definition));

        Assert.IsType<ObjectDisposedException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ResourcesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<ResourcesResult.Failed>(
            ResourcesQuery.Execute(session));
        LibraryMetadataService.ApplyResourcesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<ManifestResourceInfo>.Failed>(
            model.ResourceInspection!.Value);
        Assert.Null(model.Resources);
        Assert.Equal("Resources", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void CustomAttributesQuery_ReturnsMetadataOrderedAttributesFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);

        var result = Assert.IsType<CustomAttributesResult.Available>(
            CustomAttributesQuery.Execute(session));

        Assert.Contains(
            result.Attributes,
            attribute => attribute.Name == "InternalsVisibleTo");
        Assert.Equal(session.CustomAttributes(), result.Attributes);
    }

    [Fact]
    public void CustomAttributesQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(AssemblyInspectionSession).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [CustomAttributesQuery.Definition],
            context);
        var customAttributes = Assert.IsType<CustomAttributesResult.Available>(
            results.Get(CustomAttributesQuery.Definition));

        Assert.Contains(
            customAttributes.Attributes,
            attribute => attribute.Name == "InternalsVisibleTo");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void CustomAttributesQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [CustomAttributesQuery.Definition],
            context);
        var failure = Assert.IsType<CustomAttributesResult.Failed>(
            results.Get(CustomAttributesQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void CustomAttributesQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<CustomAttributesResult.Failed>(
            CustomAttributesQuery.Execute(session));
        LibraryMetadataService.ApplyCustomAttributesResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<AssemblyAttributeInfo>.Failed>(
            model.AssemblyAttributeInspection!.Value);
        Assert.Null(model.CustomAttributes);
        Assert.Equal("Custom Attributes", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void ExtensionMethodsQuery_ReturnsDeclaredMembersFromBorrowedContent()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);

        var result = Assert.IsType<ExtensionMethodsResult.Available>(
            ExtensionMethodsQuery.Execute(session));

        Assert.Contains(
            result.Methods,
            method => method.MethodName == "ToUpperCase");
    }

    [Fact]
    public void ExtensionMethodsQuery_UsesTheCommandsOpenImage()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var metadataContext = PdbContext.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ExtensionMethodsQuery.Definition],
            context);
        var extensionMethods = Assert.IsType<ExtensionMethodsResult.Available>(
            results.Get(ExtensionMethodsQuery.Definition));

        Assert.Contains(
            extensionMethods.Methods,
            method => method.MethodName == "ToUpperCase");
        Assert.Equal(1, context.SharedQueryCount);
    }

    [Fact]
    public void ExtensionMethodsQuery_OpenFailureRemainsTyped()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-{Guid.NewGuid():N}.dll");
        using var context = new InspectionQueryContext
        {
            AssemblyPath = missingPath,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [ExtensionMethodsQuery.Definition],
            context);
        var failure = Assert.IsType<ExtensionMethodsResult.Failed>(
            results.Get(ExtensionMethodsQuery.Definition));

        Assert.IsType<FileNotFoundException>(failure.Error);
        Assert.Equal(0, context.SharedQueryCount);
    }

    [Fact]
    public void ExtensionMethodsQuery_FailureRemainsTypedAndProjectsFindingFailure()
    {
        var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();
        var model = new LibraryInspection();

        var result = Assert.IsType<ExtensionMethodsResult.Failed>(
            ExtensionMethodsQuery.Execute(session));
        LibraryMetadataService.ApplyExtensionMethodsResult(
            "disposed.dll",
            model,
            new Output.VerboseLogger(false),
            result);

        Assert.IsType<FindingInspection<ExtensionMemberObservation>.Failed>(
            model.ExtensionMemberInspection!.Value);
        Assert.Null(model.ExtensionMethods);
        Assert.Equal("Extension Methods", Assert.Single(model.InspectionFailures!).Section);
    }

    [Fact]
    public void TypedQueryRegistry_BindsByIdentityAndReturnsTypedCurrency()
    {
        var prerequisite = new InspectionQuery<int>("same display name", InspectionCost.Moderated);
        var query = new InspectionQuery<InertString>(
            "same display name",
            InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ => 42)
            .Add(
                query,
                (_, results) => InertString.Format(
                    TextPolicy.Field,
                    $"answer {results.Get(prerequisite)}"),
                prerequisite);

        InspectionQueryResults results = registry.Run([query], context: null);
        InertString answer = results.Get(query);

        Assert.Equal("answer 42", answer.ToString());
        Assert.Equal(InspectionCost.Moderated, registry.CostOf(query));
        Assert.NotSame(prerequisite, query);
    }

    [Fact]
    public void TypedQueryRegistry_CompileProducesImmutableCatalogSnapshot()
    {
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var later = new InspectionQuery<int>("later", InspectionCost.Moderated);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(first, _ => 1);

        InspectionQueryCatalog<object?> catalog = registry.Compile();

        Assert.Same(catalog, registry.Compile());
        Assert.Equal([first], catalog.RegisteredQueries);

        registry.Add(later, _ => 2);
        InspectionQueryCatalog<object?> extended = registry.Compile();

        Assert.NotSame(catalog, extended);
        Assert.Equal([first], catalog.RegisteredQueries);
        Assert.Equal([first, later], extended.RegisteredQueries);
    }

    [Fact]
    public void TypedQueryCatalog_PrecomputesSingleQueryPlan()
    {
        var prerequisite = new InspectionQuery<int>(
            "prerequisite",
            InspectionCost.Moderated);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        InspectionQueryCatalog<object?> catalog =
            new InspectionQueryRegistry<object?>()
                .Add(prerequisite, _ => 41)
                .Add(
                    query,
                    (_, results) => results.Get(prerequisite) + 1,
                    prerequisite)
                .Compile();

        InspectionQueryPlan<object?> plan = catalog.Plan(query);

        Assert.Same(plan, catalog.Plan(query));
        Assert.Equal([prerequisite, query], plan.Queries);
        Assert.Equal(InspectionCost.Moderated, plan.Cost);
        Assert.Equal(42, plan.Run(context: null).Get(query));
    }

    [Fact]
    public void TypedQueryPlan_ReusesPlanWithoutSharingRunState()
    {
        var query = new InspectionQuery<string>(
            "query",
            InspectionCost.NetworkFree);
        InspectionQueryPlan<string> plan =
            new InspectionQueryRegistry<string>()
                .Add(query, context => context)
                .Compile()
                .Plan(query);

        InspectionQueryResults first = plan.Run("first");
        InspectionQueryResults second = plan.Run("second");

        Assert.NotSame(first, second);
        Assert.Equal("first", first.Get(query));
        Assert.Equal("second", second.Get(query));
    }

    [Fact]
    public void CompiledDomain_MultipleLensesShareOneQueryCatalog()
    {
        var first = new InspectionQuery<int>(
            "first",
            InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>(
            "second",
            InspectionCost.NetworkFree);
        InspectionQueryCatalog<object?> queryCatalog =
            new InspectionQueryRegistry<object?>()
                .Add(first, _ => 1)
                .Add(second, _ => 2)
                .Compile();
        var domain = new CompiledInspectionDomain<object?>(queryCatalog);

        CompiledInspectionLens<object?, TestModel> firstLens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline.Add<QueryBackedSection>(first));
        CompiledInspectionLens<object?, TestModel> secondLens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline.Add<QueryBackedSection>(second));

        Assert.Same(queryCatalog, firstLens.QueryCatalog);
        Assert.Same(queryCatalog, secondLens.QueryCatalog);
        Assert.Same(domain, firstLens.Domain);
        Assert.Same(domain, secondLens.Domain);
        Assert.Equal(
            [first],
            firstLens.Plan(Verbosity.Minimal).RequestedQueries);
        Assert.Equal(
            [second],
            secondLens.Plan(Verbosity.Minimal).RequestedQueries);
    }

    [Fact]
    public void CompiledLens_RejectsQueryOutsideProducerDomain()
    {
        var registered = new InspectionQuery<int>(
            "registered",
            InspectionCost.NetworkFree);
        var foreign = new InspectionQuery<int>(
            "foreign",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .Add(registered, _ => 1)
                .Compile());

        InspectionQueryException exception =
            Assert.Throws<InspectionQueryException>(
                () => domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(foreign)));

        Assert.Contains("foreign", exception.Message);
        Assert.Contains("outside the compiled inspection domain", exception.Message);
    }

    [Fact]
    public void CompiledLens_InstallsPrerequisiteAwareCostsBeforeRegistration()
    {
        var prerequisite = new InspectionQuery<int>(
            "prerequisite",
            InspectionCost.Moderated);
        var query = new InspectionQuery<int>(
            "query",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .Add(prerequisite, _ => 1)
                .Add(
                    query,
                    (_, results) => results.Get(prerequisite),
                    prerequisite)
                .Compile());

        CompiledInspectionLens<object?, TestModel> lens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline.Add<QueryBackedSection>(query));

        Assert.Equal(
            SectionCost.Moderated,
            Assert.Single(lens.Sections.Pipeline.SectionCosts).Cost);
        Assert.Throws<InvalidOperationException>(
            () => domain.CompileLens<TestModel>(
                pipeline => pipeline.UseQueryCosts(
                    _ => InspectionCost.NetworkFree)));
    }

    [Fact]
    public void CompiledLens_LowersEmptySingleAndMultiQueryDemand()
    {
        var first = new InspectionQuery<int>(
            "first",
            InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>(
            "second",
            InspectionCost.NetworkFree);
        var host = new InspectionQuery<int>(
            "host",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .Add(first, _ => 1)
                .Add(second, _ => 2)
                .Add(host, _ => 3)
                .Compile());
        CompiledInspectionLens<object?, TestModel> lens =
            domain.CompileLens<TestModel>(
                pipeline => pipeline
                    .Add<QueryBackedSection>(first)
                    .Add<DetailedSection>(second));
        var firstOnly = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            QueryBackedSection.Name,
        };
        var both = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            QueryBackedSection.Name,
            DetailedSection.Name,
        };
        HostQueryDemand hostDemand = new("test host", host);

        CompiledInspectionPlan<object?> empty =
            lens.Plan(Verbosity.Quiet);
        CompiledInspectionPlan<object?> single =
            lens.Plan(Verbosity.Minimal, firstOnly);
        CompiledInspectionPlan<object?> multi =
            lens.Plan(Verbosity.Minimal, both);
        CompiledInspectionPlan<object?> attributedHost =
            lens.Plan(
                Verbosity.Quiet,
                hostDemand: [hostDemand]);
        CompiledInspectionPlan<object?> overlappingHost =
            lens.Plan(
                Verbosity.Minimal,
                firstOnly,
                hostDemand: [new HostQueryDemand("same query", first)]);

        Assert.Empty(empty.RequestedQueries);
        Assert.Empty(empty.QueryPlan.Queries);
        Assert.Same(
            domain.QueryCatalog.Plan(Array.Empty<InspectionQueryDefinition>()),
            empty.QueryPlan);
        Assert.Equal([first], single.RequestedQueries);
        Assert.Equal([first], single.QueryPlan.Queries);
        Assert.Same(domain.QueryCatalog.Plan(first), single.QueryPlan);
        Assert.Equal([first, second], multi.RequestedQueries);
        Assert.Equal([first, second], multi.QueryPlan.Queries);
        Assert.Equal([hostDemand], attributedHost.HostDemand);
        Assert.Equal([host], attributedHost.RequestedQueries);
        Assert.Equal([host], attributedHost.QueryPlan.Queries);
        Assert.Equal(
            [new HostQueryDemand("same query", first)],
            overlappingHost.HostDemand);
        Assert.Equal([first], overlappingHost.RequestedQueries);
        Assert.Equal([first], overlappingHost.QueryPlan.Queries);
    }

    [Fact]
    public void CompiledInspectionPlan_DefaultValueFailsExplicitly()
    {
        CompiledInspectionPlan<object?> plan = default;

        Assert.True(plan.IsDefault);
        Assert.Empty(plan.HostDemand);
        Assert.Empty(plan.RequestedQueries);
        Assert.Throws<InvalidOperationException>(() => plan.Run(context: null));
    }

    [Fact]
    public void CompiledExecution_DoesNotTransformTypedQueryResults()
    {
        var query = new InspectionQuery<object>(
            "context",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<object>(
            new InspectionQueryRegistry<object>()
                .Add(query, context => context)
                .Compile());
        CompiledInspectionPlan<object> plan =
            domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(query))
                .Plan(Verbosity.Minimal);
        var expected = new object();

        InspectionQueryResults results = plan.Run(expected);

        Assert.Same(expected, results.Get(query));
    }

    [Fact]
    public async Task CompiledExecution_ForwardsAsyncCancellation()
    {
        var query = new InspectionQuery<int>(
            "async",
            InspectionCost.NetworkFree);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        var domain = new CompiledInspectionDomain<object?>(
            new InspectionQueryRegistry<object?>()
                .AddAsync(
                    query,
                    async (_, cancellationToken) =>
                    {
                        observed = cancellationToken;
                        entered.SetResult(true);
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            cancellationToken);
                        return 1;
                    })
                .Compile());
        CompiledInspectionPlan<object?> plan =
            domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(query))
                .Plan(Verbosity.Minimal);
        using var cancellation = new CancellationTokenSource();

        Task<InspectionQueryResults> execution = plan.RunAsync(
            context: null,
            cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution);
        Assert.Equal(cancellation.Token, observed);
    }

    [Fact]
    public void CompiledExecution_DoesNotRetainOrDisposeSuppliedContext()
    {
        var query = new InspectionQuery<string>(
            "value",
            InspectionCost.NetworkFree);
        var domain = new CompiledInspectionDomain<DisposableQueryContext>(
            new InspectionQueryRegistry<DisposableQueryContext>()
                .Add(query, context => context.Value)
                .Compile());
        CompiledInspectionPlan<DisposableQueryContext> plan =
            domain.CompileLens<TestModel>(
                    pipeline => pipeline.Add<QueryBackedSection>(query))
                .Plan(Verbosity.Minimal);
        var first = new DisposableQueryContext("first");
        var second = new DisposableQueryContext("second");

        InspectionQueryResults firstResults = plan.Run(first);
        InspectionQueryResults secondResults = plan.Run(second);

        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.Equal("first", firstResults.Get(query));
        Assert.Equal("second", secondResults.Get(query));

        WeakReference releasedContext = RunAndReleaseContext(plan);
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(
            GC.MaxGeneration,
            GCCollectionMode.Forced,
            blocking: true,
            compacting: true);

        Assert.False(releasedContext.IsAlive);
    }

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

    [Fact]
    public void LibraryQueryCatalog_RepeatedAcquisitionAndPlanningAllocateNothing()
    {
        InspectionQueryCatalog<InspectionQueryContext> queryCatalog =
            LibrarySections.QueryCatalog;
        InspectionQueryCatalog<AssemblyContextGroup> groupQueryCatalog =
            LibrarySections.GroupQueryCatalog;
        InspectionQueryPlan<InspectionQueryContext> plan =
            queryCatalog.Plan(BodyShapesQuery.Definition);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(queryCatalog, LibrarySections.QueryCatalog)
                || !ReferenceEquals(
                    groupQueryCatalog,
                    LibrarySections.GroupQueryCatalog)
                || !ReferenceEquals(
                    plan,
                    queryCatalog.Plan(BodyShapesQuery.Definition)))
            {
                throw new InvalidOperationException(
                    "The library query catalog or its precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CompiledSectionCatalog_FreezesBuilderAndSnapshotsEnumeration()
    {
        string[] categoryMembers = [AlwaysSection.Name];
        var pipeline = CreateTestPipeline()
            .AddCategory("@Core", categoryMembers);
        categoryMembers[0] = DetailedSection.Name;

        SectionCatalog<TestModel> catalog = pipeline.Compile();

        Assert.Same(catalog, pipeline.Compile());
        Assert.Equal(
            [AlwaysSection.Name, NormalSection.Name, DetailedSection.Name],
            catalog.AllSectionNames);
        Assert.Equal([AlwaysSection.Name], catalog.CategoryMap["@Core"]);
        Assert.Equal(["@All", "@Core"], catalog.CategoryNames);
        Assert.Throws<InvalidOperationException>(
            () => pipeline.Add<QueryBackedSection>());
        Assert.Throws<InvalidOperationException>(
            () => pipeline.AddCategory("@More", AlwaysSection.Name));
        Assert.Throws<InvalidOperationException>(
            () => pipeline.UseCuratedCatalog());
        Assert.Throws<InvalidOperationException>(
            () => pipeline.UseQueryCosts(
                _ => InspectionCost.NetworkFree));
        Assert.Throws<InvalidOperationException>(
            () => pipeline.WithoutComputedPoles());
    }

    [Fact]
    public void LibrarySectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            LibrarySections.CreateCatalog().Sections);

    [Fact]
    public void PackageSectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            PackageSectionDescriptors.CreateCatalog().Sections);

    [Fact]
    public void DiffSectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            DiffSections.CreateCatalog().Sections);

    [Fact]
    public void PackageProfileSectionCatalog_QueryPlansMatchMutablePipeline()
        => AssertSectionCatalogQueryPlansMatch(
            PackageProfileSections.CreateCatalog().Sections);

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

    [Fact]
    public void PackageCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        PackageSectionCatalog packageCatalog =
            PackageSectionDescriptors.CreateCatalog();
        SectionCatalog<InspectionResult> sectionCatalog =
            packageCatalog.Sections;
        InspectionQueryCatalog<SourceLinkQueryContext> queryCatalog =
            packageCatalog.QueryCatalog;
        SectionQueryPlan automaticPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal);
        HashSet<string> exactSelection = new(StringComparer.OrdinalIgnoreCase)
        {
            PackageSections.SourceLinkAvailability,
        };
        SectionQueryPlan exactPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal, exactSelection);
        InspectionQueryPlan<SourceLinkQueryContext> exactQueryPlan =
            queryCatalog.Plan(exactPlan.Queries[0]);
        InspectionQueryPlan<SourceLinkQueryContext> emptyQueryPlan =
            queryCatalog.Plan(Array.Empty<InspectionQueryDefinition>());
        ImmutableArray<string> categoryMembers =
            sectionCatalog.CategoryMap[SectionCategoryNames.SourceLink];
        HashSet<string> categorySelection =
            new(categoryMembers, StringComparer.OrdinalIgnoreCase);
        SectionQueryPlan categoryPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal, categorySelection);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(
                    packageCatalog,
                    PackageSectionDescriptors.CreateCatalog())
                || !ReferenceEquals(
                    sectionCatalog,
                    PackageSectionDescriptors.SectionCatalog)
                || !ReferenceEquals(
                    queryCatalog,
                    PackageSectionDescriptors.QueryCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    sectionCatalog.PlanQueries(Verbosity.Normal))
                || !ReferenceEquals(
                    exactPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Normal,
                        exactSelection))
                || !ReferenceEquals(
                    exactQueryPlan,
                    queryCatalog.Plan(exactPlan.Queries[0]))
                || !ReferenceEquals(
                    emptyQueryPlan,
                    queryCatalog.Plan(
                        Array.Empty<InspectionQueryDefinition>()))
                || !ReferenceEquals(
                    categoryPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Normal,
                        categorySelection)))
            {
                throw new InvalidOperationException(
                    "The package catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void DiffCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        DiffSectionCatalog diffCatalog = DiffSections.CreateCatalog();
        CompiledInspectionLens<DiffQueryContext, DiffDiscoveryModel> lens =
            diffCatalog.Lens;
        SectionCatalog<DiffDiscoveryModel> sectionCatalog =
            diffCatalog.Sections;
        InspectionQueryCatalog<DiffQueryContext> queryCatalog =
            diffCatalog.QueryCatalog;
        SectionQueryPlan automaticPlan =
            sectionCatalog.PlanQueries(Verbosity.Minimal);
        HashSet<string> changesSelection =
            new(StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.Changes.Name,
            };
        HashSet<string> analysisSelection =
            new(StringComparer.OrdinalIgnoreCase)
            {
                DiffSections.AnalysisDiff.Name,
            };
        SectionQueryPlan changesSectionPlan =
            sectionCatalog.PlanQueries(
                Verbosity.Minimal,
                changesSelection);
        SectionQueryPlan analysisSectionPlan =
            sectionCatalog.PlanQueries(
                Verbosity.Minimal,
                analysisSelection);
        CompiledInspectionPlan<DiffQueryContext> changesCompiledPlan =
            lens.Plan(
                Verbosity.Minimal,
                changesSelection);
        CompiledInspectionPlan<DiffQueryContext> analysisCompiledPlan =
            lens.Plan(
                Verbosity.Minimal,
                analysisSelection);
        InspectionQueryPlan<DiffQueryContext> changesQueryPlan =
            queryCatalog.Plan(changesSectionPlan.Queries[0]);
        InspectionQueryPlan<DiffQueryContext> analysisQueryPlan =
            queryCatalog.Plan(analysisSectionPlan.Queries[0]);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(
                    diffCatalog,
                    DiffSections.CreateCatalog())
                || !ReferenceEquals(
                    lens,
                    DiffSections.Lens)
                || !ReferenceEquals(
                    sectionCatalog,
                    DiffSections.SectionCatalog)
                || !ReferenceEquals(
                    queryCatalog,
                    DiffSections.QueryCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    sectionCatalog.PlanQueries(Verbosity.Minimal))
                || !ReferenceEquals(
                    changesSectionPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Minimal,
                        changesSelection))
                || !ReferenceEquals(
                    analysisSectionPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Minimal,
                        analysisSelection))
                || !ReferenceEquals(
                    changesCompiledPlan.QueryPlan,
                    lens.Plan(
                        Verbosity.Minimal,
                        changesSelection).QueryPlan)
                || !ReferenceEquals(
                    analysisCompiledPlan.QueryPlan,
                    lens.Plan(
                        Verbosity.Minimal,
                        analysisSelection).QueryPlan)
                || !ReferenceEquals(
                    changesQueryPlan,
                    queryCatalog.Plan(changesSectionPlan.Queries[0]))
                || !ReferenceEquals(
                    analysisQueryPlan,
                    queryCatalog.Plan(analysisSectionPlan.Queries[0])))
            {
                throw new InvalidOperationException(
                    "The Diff catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PackageProfileCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        PackageProfileSectionCatalog profileCatalog =
            PackageProfileSections.CreateCatalog();
        SectionCatalog<PackageProfileView> sectionCatalog =
            profileCatalog.Sections;
        InspectionQueryCatalog<PackageProfileQueryContext> queryCatalog =
            profileCatalog.QueryCatalog;
        SectionQueryPlan automaticPlan =
            sectionCatalog.PlanQueries(Verbosity.Normal);
        HashSet<string> packageSelection =
            new(StringComparer.OrdinalIgnoreCase)
            {
                PackageProfileSections.Packages,
            };
        SectionQueryPlan packageSectionPlan =
            sectionCatalog.PlanQueries(
                Verbosity.Normal,
                packageSelection);
        InspectionQueryPlan<PackageProfileQueryContext> packageQueryPlan =
            profileCatalog.Lens
                .Plan(Verbosity.Normal, packageSelection)
                .QueryPlan;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(
                    profileCatalog,
                    PackageProfileSections.CreateCatalog())
                || !ReferenceEquals(
                    sectionCatalog,
                    PackageProfileSections.SectionCatalog)
                || !ReferenceEquals(
                    queryCatalog,
                    PackageProfileSections.QueryCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    sectionCatalog.PlanQueries(Verbosity.Normal))
                || !ReferenceEquals(
                    packageSectionPlan,
                    sectionCatalog.PlanQueries(
                        Verbosity.Normal,
                        packageSelection))
                || !ReferenceEquals(
                    packageQueryPlan,
                    profileCatalog.Lens
                        .Plan(Verbosity.Normal, packageSelection)
                        .QueryPlan))
            {
                throw new InvalidOperationException(
                    "The package-profile catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void LibrarySectionCatalog_RepeatedAcquisitionAndCommonPlanningAllocateNothing()
    {
        LibrarySectionCatalog libraryCatalog = LibrarySections.CreateCatalog();
        SectionCatalog<LibraryInspection> catalog = libraryCatalog.Sections;
        SectionQueryPlan automaticPlan = catalog.PlanQueries(Verbosity.Normal);
        HashSet<string> exactSelection = new(StringComparer.OrdinalIgnoreCase)
        {
            catalog.SelectableSectionNames[0],
        };
        SectionQueryPlan exactPlan =
            catalog.PlanQueries(Verbosity.Normal, exactSelection);
        ImmutableArray<string> categoryMembers = catalog.CategoryMap.Values.First();
        HashSet<string> categorySelection =
            new(categoryMembers, StringComparer.OrdinalIgnoreCase);
        SectionQueryPlan categoryPlan =
            catalog.PlanQueries(Verbosity.Normal, categorySelection);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1_000; iteration++)
        {
            if (!ReferenceEquals(libraryCatalog, LibrarySections.CreateCatalog())
                || !ReferenceEquals(catalog, LibrarySections.SectionCatalog)
                || !ReferenceEquals(
                    automaticPlan,
                    catalog.PlanQueries(Verbosity.Normal))
                || !ReferenceEquals(
                    exactPlan,
                    catalog.PlanQueries(Verbosity.Normal, exactSelection))
                || !ReferenceEquals(
                    categoryPlan,
                    catalog.PlanQueries(Verbosity.Normal, categorySelection)))
            {
                throw new InvalidOperationException(
                    "The library section catalog or a precomputed plan changed identity.");
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CompiledSectionQueryPlan_PreservesTraceAttributionAndCommandDemand()
    {
        var sectionQuery = new InspectionQuery<int>(
            "section query",
            InspectionCost.NetworkFree);
        var commandQuery = new InspectionQuery<int>(
            "command query",
            InspectionCost.NetworkFree);
        var pipeline = new SectionPipeline<TestModel>()
            .Add<QueryBackedSection>(sectionQuery);
        SectionCatalog<TestModel> catalog = pipeline.Compile();
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            QueryBackedSection.Name,
        };
        List<HostQueryDemand> commandDemand =
        [
            new("test command", commandQuery),
        ];
        var expectedTrace = new InspectionTrace();
        var actualTrace = new InspectionTrace();

        HashSet<InspectionQueryDefinition> expected = pipeline.GetRequiredQueries(
            Verbosity.Normal,
            include,
            trace: expectedTrace,
            commandDemand: commandDemand);
        HashSet<InspectionQueryDefinition> actual = catalog
            .PlanQueries(Verbosity.Normal, include)
            .Activate(actualTrace, commandDemand);

        Assert.True(expected.SetEquals(actual));
        Assert.Equal(expectedTrace.QueryDemand, actualTrace.QueryDemand);
        Assert.Equal(expectedTrace.CommandQueryDemand, actualTrace.CommandQueryDemand);
        Assert.Equal(expectedTrace.RequestedQueries, actualTrace.RequestedQueries);
    }

    [Fact]
    public void QueryBackedSection_InheritsDependencyClosureCost()
    {
        var prerequisite = new InspectionQuery<int>(
            "moderated prerequisite",
            InspectionCost.Moderated);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ => 1)
            .Add(query, (_, results) => results.Get(prerequisite), prerequisite);
        var pipeline = new SectionPipeline<TestModel>()
            .UseQueryCosts(registry.CostOf)
            .Add<QueryBackedSection>(query);

        var section = Assert.Single(pipeline.SectionCosts);
        Assert.Equal(SectionCost.Moderated, section.Cost);
    }

    [Fact]
    public void QueryBackedSection_MultipleQueries_InheritsMaximumCostAndDemandsEach()
    {
        var networkFree = new InspectionQuery<int>(
            "network-free",
            InspectionCost.NetworkFree);
        var moderated = new InspectionQuery<int>(
            "moderated",
            InspectionCost.Moderated);
        var pipeline = new SectionPipeline<TestModel>()
            .UseQueryCosts(query => query.Cost)
            .Add<QueryBackedSection>([networkFree, moderated]);

        var section = Assert.Single(pipeline.SectionCosts);
        HashSet<InspectionQueryDefinition> required = pipeline.GetRequiredQueries(
            Verbosity.Minimal);

        Assert.Equal(SectionCost.Moderated, section.Cost);
        Assert.Equal(
            [moderated, networkFree],
            required.OrderBy(query => query.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void QueryBackedSection_MultipleQueries_RejectsDuplicateIdentity()
    {
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);

        Assert.Throws<ArgumentException>(() =>
            new SectionPipeline<TestModel>().Add<QueryBackedSection>([query, query]));
    }

    [Fact]
    public void GetRequiredQueries_ExcludeUnbounded_PreservesExplicitBoundedSelection()
    {
        var bounded = new InspectionQuery<int>("bounded", InspectionCost.NetworkFree);
        var unbounded = new InspectionQuery<int>("unbounded", InspectionCost.Unbounded);
        var pipeline = new SectionPipeline<TestModel>()
            .UseCuratedCatalog()
            .UseQueryCosts(query => query.Cost)
            .Add(new SectionEntry<TestModel>
            {
                Name = "Bounded",
                IsExpensive = false,
                SizeClass = SectionSizeClass.Terse,
                Cost = SectionCost.NetworkFree,
                Queries = [bounded],
                IsApplicable = _ => true,
                CanRender = _ => true,
            })
            .Add(new SectionEntry<TestModel>
            {
                Name = "Unbounded",
                IsExpensive = false,
                SizeClass = SectionSizeClass.Terse,
                Cost = SectionCost.NetworkFree,
                Queries = [unbounded],
                IsApplicable = _ => true,
                CanRender = _ => true,
            });
        var include = new HashSet<string> { "Bounded", "Unbounded" };

        var renderQueries = pipeline.GetRequiredQueries(Verbosity.Detailed, include);
        var discoveryQueries = pipeline.GetRequiredQueries(
            Verbosity.Detailed,
            include,
            excludeUnbounded: true);

        Assert.Contains(bounded, renderQueries);
        Assert.Contains(unbounded, renderQueries);
        Assert.Equal([bounded], discoveryQueries);
    }

    [Fact]
    public void TypedQueryRegistry_ExecutesPrerequisitesOnceInDeclaredOrder()
    {
        List<string> order = [];
        var prerequisite = new InspectionQuery<int>("prerequisite", InspectionCost.NetworkFree);
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>("second", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ =>
            {
                order.Add("prerequisite");
                return 1;
            })
            .Add(first, (_, results) =>
            {
                order.Add("first");
                return results.Get(prerequisite) + 1;
            }, prerequisite)
            .Add(second, (_, results) =>
            {
                order.Add("second");
                return results.Get(prerequisite) + results.Get(first);
            }, first);

        InspectionQueryResults results = registry.Run([first, second], context: null);

        Assert.Equal(["prerequisite", "first", "second"], order);
        Assert.Equal(3, results.Get(second));
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_ExecutesMixedQueriesInDeclaredOrder()
    {
        List<string> order = [];
        var prerequisite = new InspectionQuery<int>("prerequisite", InspectionCost.NetworkFree);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ =>
            {
                order.Add("prerequisite");
                return 1;
            })
            .AddAsync(
                query,
                (_, results, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    order.Add("query");
                    return ValueTask.FromResult(results.Get(prerequisite) + 1);
                },
                prerequisite);

        InspectionQueryResults results = await registry.RunAsync(
            [query],
            context: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["prerequisite", "query"], order);
        Assert.Equal(2, results.Get(query));
    }

    [Fact]
    public void TypedQueryRegistry_OptionalDependencyRunsOnlyWhenIndependentlyRequested()
    {
        var optional = new InspectionQuery<int>("optional", InspectionCost.Unbounded);
        var consumer = new InspectionQuery<int>("consumer", InspectionCost.NetworkFree);
        List<string> order = [];
        var registry = new InspectionQueryRegistry<object?>()
            .AddWithOptional(
                consumer,
                (_, results) =>
                {
                    order.Add("consumer");
                    return results.TryGet(optional, out int value) ? value : 0;
                },
                [optional])
            .Add(optional, _ =>
            {
                order.Add("optional");
                return 42;
            });

        InspectionQueryResults withoutOptional = registry.Run([consumer], null);

        Assert.Equal(0, withoutOptional.Get(consumer));
        Assert.Equal(["consumer"], order);
        Assert.Equal([consumer], registry.ExpandRequired([consumer]));
        Assert.Equal(InspectionCost.NetworkFree, registry.CostOf(consumer));

        order.Clear();
        InspectionQueryResults withOptional = registry.Run(
            [consumer, optional],
            null);

        Assert.Equal(42, withOptional.Get(consumer));
        Assert.Equal(["optional", "consumer"], order);
        Assert.Equal([optional], registry.OptionalDependenciesOf(consumer));
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_OrdersActiveOptionalDependency()
    {
        var optional = new InspectionQuery<int>("optional", InspectionCost.NetworkFree);
        var consumer = new InspectionQuery<int>("consumer", InspectionCost.NetworkFree);
        List<string> order = [];
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsyncWithOptional(
                consumer,
                (_, results, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    order.Add("consumer");
                    return ValueTask.FromResult(results.Get(optional));
                },
                [optional])
            .Add(optional, _ =>
            {
                order.Add("optional");
                return 42;
            });

        InspectionQueryResults results = await registry.RunAsync(
            [consumer, optional],
            context: null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, results.Get(consumer));
        Assert.Equal(["optional", "consumer"], order);
    }

    [Fact]
    public void TypedQueryRegistry_RejectsActiveOptionalDependencyCycle()
    {
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>("second", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddWithOptional(first, (_, _) => 1, [second])
            .AddWithOptional(second, (_, _) => 2, [first]);

        InspectionQueryException exception = Assert.Throws<InspectionQueryException>(
            () => registry.Run([first, second], context: null));

        Assert.Contains("active dependency cycle", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQueryRegistry_RunRejectsAsynchronousQueries()
    {
        var query = new InspectionQuery<int>("async", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsync(query, (_, _) => ValueTask.FromResult(1));

        var exception = Assert.Throws<InspectionQueryException>(
            () => registry.Run([query], context: null));

        Assert.Contains("must be executed with RunAsync", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_PropagatesCancellation()
    {
        bool ran = false;
        var query = new InspectionQuery<int>("async", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsync(query, (_, _) =>
            {
                ran = true;
                return ValueTask.FromResult(1);
            });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => registry.RunAsync([query], context: null, cancellationToken: cancellation.Token));
        Assert.False(ran);
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_EmptyDemandPropagatesCancellation()
    {
        var registered = new InspectionQuery<int>(
            "registered",
            InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(registered, _ => 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => registry.RunAsync(
                [],
                context: null,
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task TypedQueryRegistry_RunAsync_RejectsUndeclaredResultDependencies()
    {
        var hidden = new InspectionQuery<int>("hidden", InspectionCost.Unbounded);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .AddAsync(hidden, (_, _) => ValueTask.FromResult(42))
            .AddAsync(
                query,
                (_, results, _) => ValueTask.FromResult(results.Get(hidden)));

        var exception = await Assert.ThrowsAsync<InspectionQueryException>(
            () => registry.RunAsync(
                [hidden, query],
                context: null,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("not a declared prerequisite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQueryRegistry_RejectsUndeclaredResultDependencies()
    {
        var hidden = new InspectionQuery<int>("hidden", InspectionCost.Unbounded);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(hidden, _ => 42)
            .Add(query, (_, results) => results.Get(hidden));

        var present = Assert.Throws<InspectionQueryException>(
            () => registry.Run([hidden, query], context: null));
        var absent = Assert.Throws<InspectionQueryException>(
            () => registry.Run([query], context: null));

        Assert.Contains("not a declared prerequisite", present.Message, StringComparison.Ordinal);
        Assert.Equal(present.Message, absent.Message);
        Assert.Equal(InspectionCost.NetworkFree, registry.CostOf(query));
    }

    [Fact]
    public void TypedQueryRegistry_PrerequisiteGraphIsImmutableAndFailVisible()
    {
        var prerequisite = new InspectionQuery<int>("prerequisite", InspectionCost.NetworkFree);
        var replacement = new InspectionQuery<int>("replacement", InspectionCost.Unbounded);
        var query = new InspectionQuery<int>("query", InspectionCost.NetworkFree);
        InspectionQueryDefinition[] declared = [prerequisite];
        var registry = new InspectionQueryRegistry<object?>()
            .Add(prerequisite, _ => 1)
            .Add(replacement, _ => 2)
            .Add(query, (_, results) => results.Get(prerequisite), declared);

        declared[0] = replacement;

        Assert.Equal([prerequisite], registry.RequirementsOf(query));
        Assert.Equal(InspectionCost.NetworkFree, registry.CostOf(query));

        var missing = new InspectionQuery<int>("missing", InspectionCost.NetworkFree);
        Assert.Throws<InspectionQueryException>(() => registry.ExpandRequired([missing]));
    }

    [Fact]
    public void TypedQueryRegistry_RejectsPrerequisiteCycles()
    {
        var first = new InspectionQuery<int>("first", InspectionCost.NetworkFree);
        var second = new InspectionQuery<int>("second", InspectionCost.NetworkFree);
        var registry = new InspectionQueryRegistry<object?>()
            .Add(first, _ => 1, second)
            .Add(second, _ => 2, first);

        Assert.Throws<InspectionQueryException>(() => registry.ExpandRequired([first]));
    }

    [Fact]
    public void InspectionCost_OrdersFromCheapestToMostExpensive()
    {
        Assert.Equal(
            [InspectionCost.NetworkFree, InspectionCost.Moderated, InspectionCost.Unbounded],
            Enum.GetValues<InspectionCost>());
    }

    [Fact]
    public void TypedQuery_CannotTakeTheBodyIndexWithoutDeclaringItsTransitiveCost()
    {
        var cheap = new InspectionQuery<int>("cheap", InspectionCost.NetworkFree);
        var cheapRegistry = LibrarySections.CreateQueryRegistry()
            .Add(cheap, ctx =>
            {
                ctx.BodyIndex();
                return 0;
            });

        var refused = Assert.Throws<QueryCostDeclarationException>(
            () => cheapRegistry.Run([cheap], NullQueryContext()));
        Assert.Contains("Query 'cheap'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("body index", refused.Message, StringComparison.Ordinal);
        Assert.Contains("NetworkFree", refused.Message, StringComparison.Ordinal);

        var unboundedPrerequisite = new InspectionQuery<int>(
            "unbounded prerequisite",
            InspectionCost.Unbounded);
        var transitivelyUnbounded = new InspectionQuery<int>(
            "transitively unbounded",
            InspectionCost.NetworkFree);
        var declaredRegistry = LibrarySections.CreateQueryRegistry()
            .Add(unboundedPrerequisite, _ => 1)
            .Add(
                transitivelyUnbounded,
                ctx =>
                {
                    ctx.BodyIndex();
                    return 0;
                },
                unboundedPrerequisite);

        var allowed = Assert.Throws<InvalidOperationException>(
            () => declaredRegistry.Run([transitivelyUnbounded], NullQueryContext()));
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("transitively unbounded", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQuery_CannotTakeTheDrillMapWithoutDeclaringItsCost()
    {
        var cheap = new InspectionQuery<int>("cheap", InspectionCost.NetworkFree);
        var cheapRegistry = LibrarySections.CreateQueryRegistry()
            .Add(cheap, ctx =>
            {
                ctx.DrillMap();
                return 0;
            });

        var refused = Assert.Throws<QueryCostDeclarationException>(
            () => cheapRegistry.Run([cheap], NullQueryContext()));
        Assert.Contains("drill map", refused.Message, StringComparison.Ordinal);

        var declared = new InspectionQuery<int>("declared", InspectionCost.Unbounded);
        var declaredRegistry = LibrarySections.CreateQueryRegistry()
            .Add(declared, ctx =>
            {
                ctx.DrillMap();
                return 0;
            });

        var allowed = Assert.Throws<InvalidOperationException>(
            () => declaredRegistry.Run([declared], NullQueryContext()));
        Assert.Contains("metadata context", allowed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedQueryDeclaration_DoesNotOutliveTheExecutor()
    {
        var query = new InspectionQuery<int>("cheap", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add(query, _ => 1);
        var context = NullQueryContext();

        registry.Run([query], context);

        var ex = Assert.Throws<InvalidOperationException>(() => context.BodyIndex());
        Assert.Contains("metadata context", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("NetworkFree", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataImageQuery_CarriesInertStringInItsTypedResult()
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(SectionPipelineTests).Assembly.Location);

        var available = Assert.IsType<MetadataImageResult.Available>(
            MetadataImageQuery.Execute(session));
        InertString metadataVersion = available.Overview.MetadataVersion;

        Assert.True(InertString.IsPermitted(TextPolicy.Field, metadataVersion.ToString()));
        Assert.False(metadataVersion.IsEmpty);
    }

    [Fact]
    public void MetadataImageQuery_FailureRemainsTypedAndAffectsEveryMetadataSection()
    {
        var model = new LibraryInspection();
        Assert.Null(model.InspectionFailures);

        var error = new InvalidDataException("metadata image failed");
        model.MetadataImageResult = new MetadataImageResult.Failed(error);

        Assert.Null(model.MetadataOverview);
        Assert.Same(error, Assert.IsType<MetadataImageResult.Failed>(
            model.MetadataImageResult).Error);
        LibraryInspectionFailureJson failure = Assert.Single(model.InspectionFailures!);
        Assert.Equal(MetadataSectionNames.Image, failure.Section);
        Assert.Equal(MetadataImageQuery.Definition.Name, failure.Finding);
        Assert.Equal(error.Message, failure.Reason);
        Assert.All(
            MetadataSectionNames.All,
            section => Assert.True(LibraryCommand.FailureAffectsSection(
                failure.Section,
                section)));
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_DoesNotSwallowDeclarationViolation()
    {
        var query = new InspectionQuery<TopLeverageResult>(
            "cheap",
            InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add(query, LibrarySections.ExecuteTopLeverageQuery);
        using var httpClient = new HttpClient();

        await Assert.ThrowsAsync<QueryCostDeclarationException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspector.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: registry.Compile()));
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_DoesNotSwallowExecutorFailure()
    {
        var query = new InspectionQuery<int>("failing", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add<int>(query, _ => throw new IOException("executor failed"));
        using var httpClient = new HttpClient();

        var ex = await Assert.ThrowsAsync<InspectionQueryException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspector.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: registry.Compile()));

        Assert.Contains("query execution", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_PreservesCancellation()
    {
        var query = new InspectionQuery<int>("cancelled", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add<int>(query, _ => throw new OperationCanceledException("cancelled"));
        using var httpClient = new HttpClient();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspector.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: registry.Compile()));
    }

    [Fact]
    public async Task ProductionQueryCatchBoundary_DoesNotSwallowUnknownDemand()
    {
        var query = new InspectionQuery<int>("unregistered", InspectionCost.NetworkFree);
        using var httpClient = new HttpClient();

        var ex = await Assert.ThrowsAsync<InspectionQueryException>(() =>
            LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions(),
                new DotnetInspector.Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries: [query],
                queryCatalog: LibrarySections.QueryCatalog));

        Assert.Contains("unregistered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPipeline_ConsultsQueryCosts()
    {
        // Non-vacuity: Performance: Boxing declares no cost of its own and is expensive only
        // because the Optimization Opportunities query behind it is.
        var withCosts = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
        };

        Assert.DoesNotContain(
            SectionNames.PerformanceBoxing,
            withCosts.GetEffectiveSections(model, Verbosity.Detailed));
    }

    [Fact]
    public void LibrarySections_AboveNetworkFree_AreExplicitlyPinned()
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
        // The primary assertion is on the pipeline's effective cost — the value the ladder
        // actually consults. A descriptor can raise its own cost above its query, while a query
        // raise always raises the entry.
        var pipeline = LibrarySections.CreatePipeline();

        string[] expectedQueryBodyIndexFamily =
        [
            SectionNames.ArrayPoolEscapes,
            SectionNames.BodyShapes,
            SectionNames.PerformanceHotspots,
            SectionNames.PerformanceArrays,
            SectionNames.PerformanceAsync,
            SectionNames.PerformanceBoxing,
            SectionNames.PerformanceClosures,
            SectionNames.PerformanceEnumerators,
            SectionNames.PerformanceLoops,
            SectionNames.PerformanceOther,
        ];

        // The effective axis: everything the ladder will refuse to auto-render, whichever
        // declaration made it so. Metadata and SourceLink declare their own cost; Integrations
        // inherits the group query's Unbounded cost. This is the honest full set, so either kind
        // of cost declaration crossing the boundary requires an explicit review update.
        string[] expectedAboveCheap =
        [
            .. expectedQueryBodyIndexFamily,
            SectionNames.TopLeverage,
            SectionNames.UnsafeMembers,
            .. LibraryIntegrationCatalog.CategorySections,
            IntegrationSectionNames.Opportunities,
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
            SectionNames.IdentifierConfusion,
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
    public void IntegrationSections_BindToGroupQueriesByIdentity()
    {
        LibrarySectionCatalog catalog = LibrarySections.CreateCatalog();
        SectionPipeline<LibraryInspection> pipeline = catalog.Pipeline;
        Assert.Equal(
            IntegrationConceptCatalog.Concepts,
            LibraryIntegrationCatalog.All.Select(
                descriptor => descriptor.Concept));
        Assert.Contains(
            AssemblyContextIntegrationsQuery.Definition,
            catalog.GroupQueryCatalog.RegisteredQueries);
        Assert.Contains(
            AssemblyContextIntegrationOpportunitiesQuery.Definition,
            catalog.GroupQueryCatalog.RegisteredQueries);
        Assert.DoesNotContain(
            AssemblyContextIntegrationsQuery.Definition,
            catalog.QueryCatalog.RegisteredQueries);

        foreach (string section in LibraryIntegrationCatalog.CategorySections)
        {
            var include = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                section,
            };
            HashSet<InspectionQueryDefinition> queries =
                pipeline.GetRequiredQueries(Verbosity.Minimal, include);

            Assert.Contains(
                AssemblyContextIntegrationsQuery.Definition,
                queries);
        }

        var opportunities = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            IntegrationSectionNames.Opportunities,
        };
        Assert.Contains(
            AssemblyContextIntegrationOpportunitiesQuery.Definition,
            pipeline.GetRequiredQueries(Verbosity.Minimal, opportunities));
        Assert.DoesNotContain(
            AssemblyContextIntegrationsQuery.Definition,
            pipeline.GetRequiredQueries(Verbosity.Minimal, opportunities));
        Assert.Equal(
            [AssemblyContextIntegrationsQuery.Definition],
            catalog.GroupQueryCatalog.RequirementsOf(
                AssemblyContextIntegrationOpportunitiesQuery.Definition));
        Assert.Equal(
            InspectionCost.Unbounded,
            catalog.GroupQueryCatalog.CostOf(
                AssemblyContextIntegrationOpportunitiesQuery.Definition));
    }

    [Fact]
    public void ClassifiedAndAuditQueries_ObserveOneSession()
    {
        var queryRegistry = LibrarySections.CreateQueryRegistry();
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
        };

        InspectionQueryResults results = queryRegistry.Run(
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

        Assert.Equal(2, context.SharedQueryCount);
        Assert.NotNull(context.Session());
        Assert.NotNull(context.Model.ClassifiedMethodInspection);
        Assert.NotNull(context.Model.AuditSignals);
    }

    [Fact]
    public void Trace_RecordsClassifiedMethodsAsDirectQueryDemand()
    {
        var registry = LibrarySections.CreateQueryRegistry();
        var pipeline = LibrarySections.CreatePipeline();
        var trace = new InspectionTrace();
        var include = new HashSet<string> { SectionNames.PInvokeMethods };

        HashSet<InspectionQueryDefinition> requested =
            pipeline.GetRequiredQueries(
                Verbosity.Minimal,
                include,
                trace: trace);
        trace.RecordQueryClosure(registry.ExpandRequired(requested));

        Assert.Equal(
            [ClassifiedMethodsQuery.Definition],
            trace.RequestedQueries);
        Assert.Contains(
            trace.QueryDemand,
            demand => demand is
            {
                Section: SectionNames.PInvokeMethods,
                Query: var query,
            } && ReferenceEquals(query, ClassifiedMethodsQuery.Definition));
        Assert.Equal(trace.RequestedQueries, trace.QueryClosure);
    }

    [Fact]
    public void Trace_ExplainsEveryQueryThatRan_AndRendersInertLines()
    {
        InspectionQueryCatalog<InspectionQueryContext> queryCatalog =
            LibrarySections.QueryCatalog;
        var pipeline = LibrarySections.CreatePipeline();
        var trace = new InspectionTrace
        {
            Target = new InertString(TextPolicy.Field, "target\nError: FORGED"),
        };
        HostQueryDemand[] commandDemand =
        [
            new("discovery catalog", MetadataImageQuery.Definition),
            new("source availability", SourceAvailabilityQuery.Definition),
        ];

        HashSet<InspectionQueryDefinition> requested = pipeline.GetRequiredQueries(
            Verbosity.Detailed,
            trace: trace,
            commandDemand: commandDemand);
        InspectionQueryPlan<InspectionQueryContext> plan =
            queryCatalog.Plan(requested);
        trace.RecordQueryClosure(plan.Queries);

        var claimed = trace.QueryDemand.Select(d => d.Query)
            .Concat(trace.CommandQueryDemand.Select(d => d.Query))
            .ToHashSet();
        var reachable = new HashSet<InspectionQueryDefinition>(claimed);
        var queue = new Queue<InspectionQueryDefinition>(claimed);
        while (queue.Count > 0)
        {
            foreach (InspectionQueryDefinition requirement in
                queryCatalog.RequirementsOf(queue.Dequeue()))
            {
                if (reachable.Add(requirement))
                    queue.Enqueue(requirement);
            }
        }

        Assert.DoesNotContain(SourceLinkDocumentsQuery.Definition, requested);
        Assert.Contains(SourceLinkDocumentsQuery.Definition, trace.QueryClosure);
        Assert.Equal(
            reachable.OrderBy(query => query.Name, StringComparer.Ordinal),
            trace.QueryClosure);
        Assert.Equal(
            [
                AssemblyReferencesQuery.Definition,
                AuditMetadataQuery.Definition,
                ClassifiedMethodsQuery.Definition,
                CustomAttributesQuery.Definition,
                ExtensionMethodsQuery.Definition,
                MetadataImageQuery.Definition,
                ResourcesQuery.Definition,
                SourceAvailabilityQuery.Definition,
                SwitchesQuery.Definition,
                TypeForwardersQuery.Definition,
                UnionTypesQuery.Definition,
            ],
            requested.OrderBy(query => query.Name, StringComparer.Ordinal));

        IEnumerable<InertString> lines = trace.RenderLines();
        Assert.All(
            lines,
            line => Assert.True(
                InertString.IsPermitted(TextPolicy.Field, line.ToString())));
        Assert.Contains(lines, line => line.ToString().Contains(@"\^J", StringComparison.Ordinal));
    }

    [Fact]
    public void Trace_RecordsNoBodyIndexForClassifiedMethodsQuery()
    {
        // The negative half of the minimum-work claim, and the one worth gating. A regression that
        // makes a metadata-only scan open the whole-assembly IL index costs seconds and changes no
        // output at all, so no other test in the suite would notice. Its absence from the resource
        // list is the observable.
        var registry = LibrarySections.CreateQueryRegistry();
        var trace = new InspectionTrace();
        using var metadataContext = PdbContext.Open(typeof(SectionPipelineTests).Assembly.Location);
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = metadataContext,
            Trace = trace,
        };

        registry.Run(
            [ClassifiedMethodsQuery.Definition],
            context,
            trace.RecordQueryExecution);

        Assert.Contains(trace.Resources, r => r.Resource == "metadata session");
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "body index");
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "drill map");
    }

    [Fact]
    public void UnsafeEvidenceQuery_RecordsAndReturnsTheBodyIndexItBuilds()
    {
        // Paired positive. Without it the negative above is satisfied by a trace that never
        // records a body index under any circumstances, which would pass while observing nothing.
        // The index needs the prefetched image the command opens for exactly this reason; a plain
        // PdbContext.Open cannot back it, and the scanner would swallow the failure and render an
        // empty section. Opening it the way InspectAsync does is what makes this a real positive.
        var registry = LibrarySections.CreateQueryRegistry();
        var trace = new InspectionTrace();
        using var service = SourceLinkService.OpenPrefetched(
            typeof(SectionPipelineTests).Assembly.Location,
            _ => { });
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = service.Context,
            BodyAnalysisFeatures = Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [UnsafeEvidenceQuery.Definition],
            context,
            trace.RecordQueryExecution);

        var available = Assert.IsType<UnsafeEvidenceResult.Available>(
            results.Get(UnsafeEvidenceQuery.Definition));
        Assert.NotEmpty(available.Evidence);
        var bodyIndex = Assert.Single(trace.Resources, r => r.Resource == "body index");
        Assert.StartsWith("built in", bodyIndex.Detail.ToString());
        Assert.Contains("MethodEvidence", bodyIndex.Detail.ToString());
    }

    [Fact]
    public void UnsafeEvidenceQuery_BodyIndexFailureRemainsTyped()
    {
        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnsafeEvidenceQuery.Definition],
            NullQueryContext());

        var failed = Assert.IsType<UnsafeEvidenceResult.Failed>(
            results.Get(UnsafeEvidenceQuery.Definition));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public void UnsafeEvidenceQuery_NoMetadata_DoesNotAcquireBodyIndex()
    {
        bool acquired = false;

        UnsafeEvidenceResult result = LibrarySections.ExecuteUnsafeEvidenceQuery(
            hasMetadata: false,
            () =>
            {
                acquired = true;
                throw new InvalidOperationException("must not acquire");
            });

        Assert.IsType<UnsafeEvidenceResult.NoMetadata>(result);
        Assert.False(acquired);
    }

    [Fact]
    public void UnsafeEvidenceQuery_FailureProjectsToInspectionFailure()
    {
        var inspection = new LibraryInspection();
        var error = new IOException("body index failed");

        LibraryMetadataService.ApplyUnsafeEvidenceResult(
            "broken.dll",
            inspection,
            new Output.VerboseLogger(false),
            new UnsafeEvidenceResult.Failed(error));

        var failed = Assert.IsType<FindingInspection<Analysis.UnsafeEvidence>.Failed>(
            inspection.UnsafeEvidenceInspection?.Value);
        Assert.Contains("body index failed", failed.Error.Reason, StringComparison.Ordinal);
        var projected = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(SectionNames.UnsafeMembers, projected.Section);
        Assert.True(LibraryCommand.FailureAffectsSection(
            projected.Section,
            SectionNames.UnsafeMembers));
        Assert.Null(inspection.UnsafeMembers);
    }

    [Fact]
    public void UnsafeEvidenceQuery_NoMetadata_DoesNotProjectFailure()
    {
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyUnsafeEvidenceResult(
            "native.dll",
            inspection,
            new Output.VerboseLogger(false),
            new UnsafeEvidenceResult.NoMetadata());

        Assert.Null(inspection.UnsafeEvidenceInspection);
        Assert.Null(inspection.UnsafeMembers);
        Assert.Null(inspection.InspectionFailures);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_RecordsAndReturnsTheBodyIndexItBuilds()
    {
        var registry = LibrarySections.CreateQueryRegistry();
        var trace = new InspectionTrace();
        using var service = SourceLinkService.OpenPrefetched(
            typeof(SectionPipelineTests).Assembly.Location,
            _ => { });
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = service.Context,
            BodyAnalysisFeatures =
                Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [OptimizationOpportunitiesQuery.Definition],
            context,
            trace.RecordQueryExecution);

        var available =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                results.Get(OptimizationOpportunitiesQuery.Definition));
        Assert.NotEmpty(available.Opportunities);
        Assert.Empty(available.AllocationFanoutOpportunities);
        var bodyIndex = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body index");
        Assert.StartsWith("built in", bodyIndex.Detail.ToString());
        Assert.Contains(
            "OptimizationOpportunities",
            bodyIndex.Detail.ToString());
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "drill map");
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_AllocationFanoutRemainsOptIn()
    {
        var index = Analysis.LibraryBodyIndex.Open(
            typeof(SectionPipelineTests).Assembly.Location,
            Analysis.LibraryBodyAnalysisFeatures.OptimizationOpportunities);

        var ordinary =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                OptimizationOpportunitiesQuery.Execute(
                    index,
                    includeAllocationFanout: false));
        var fanout =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                OptimizationOpportunitiesQuery.Execute(
                    index,
                    includeAllocationFanout: true));

        Assert.Empty(ordinary.AllocationFanoutOpportunities);
        Assert.NotEmpty(fanout.AllocationFanoutOpportunities);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_BodyIndexFailureRemainsTyped()
    {
        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [OptimizationOpportunitiesQuery.Definition],
            NullQueryContext());

        var failed =
            Assert.IsType<OptimizationOpportunitiesResult.Failed>(
                results.Get(OptimizationOpportunitiesQuery.Definition));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_NoMetadata_DoesNotAcquireBodyIndex()
    {
        bool acquired = false;

        OptimizationOpportunitiesResult result =
            LibrarySections.ExecuteOptimizationOpportunitiesQuery(
                hasMetadata: false,
                () =>
                {
                    acquired = true;
                    throw new InvalidOperationException("must not acquire");
                },
                includeAllocationFanout: false);

        Assert.IsType<OptimizationOpportunitiesResult.NoMetadata>(result);
        Assert.False(acquired);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_FailureProjectsToPerformanceSections()
    {
        var inspection = new LibraryInspection();
        var error = new IOException("body index failed");

        LibraryMetadataService.ApplyOptimizationOpportunitiesResult(
            "broken.dll",
            inspection,
            new Output.VerboseLogger(false),
            new OptimizationOpportunitiesResult.Failed(error));

        var failed =
            Assert.IsType<OptimizationOpportunitiesResult.Failed>(
                inspection.OptimizationOpportunitiesQueryResult);
        Assert.Same(error, failed.Error);
        var projected = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(SectionNames.PerformanceTriage, projected.Section);
        Assert.Equal(
            OptimizationOpportunitiesQuery.Definition.Name,
            projected.Finding);
        foreach (string section in PerformanceKinds.Sections)
        {
            Assert.True(LibraryCommand.FailureAffectsSection(
                projected.Section,
                section));
        }
        Assert.Empty(inspection.PerformanceTriageOpportunities);
        Assert.Null(inspection.OptimizationOpportunities);
    }

    [Fact]
    public async Task ComposedBodyShapes_QueryFailureDoesNotProduceEmptySuccess()
    {
        var error = new IOException("body index failed");
        var registry = new InspectionQueryRegistry<InspectionQueryContext>()
            .Add(
                OptimizationOpportunitiesQuery.Definition,
                _ => new OptimizationOpportunitiesResult.Failed(error))
            .AddWithOptional(
                BodyShapesQuery.Definition,
                LibrarySections.ExecuteBodyShapesQuery,
                [OptimizationOpportunitiesQuery.Definition]);
        using var httpClient = new HttpClient();

        LibraryInspection inspection = Assert.IsType<LibraryInspection>(
            await LibraryMetadataService.InspectAsync(
                typeof(SectionPipelineTests).Assembly.Location,
                new LibraryOptions
                {
                    BodyKindQuery = new BodyKindQueryOptions
                    {
                        Kind = "ArrayCreationExpression",
                    },
                    PerformanceTriage = new PerformanceTriageOptions
                    {
                        Shapes = ["small-array"],
                    },
                },
                new Output.VerboseLogger(false),
                packageName: null,
                packageVersion: null,
                httpClient,
                queries:
                [
                    BodyShapesQuery.Definition,
                    OptimizationOpportunitiesQuery.Definition,
                ],
                queryCatalog: registry.Compile()));

        Assert.IsType<BodyShapesResult.DependencyUnavailable>(
            inspection.BodyShapesQueryResult);
        Assert.Null(inspection.BodyShapeSearchResult);
        var bodyShapesFailure = Assert.Single(
            inspection.InspectionFailures!,
            failure => failure.Section == SectionNames.BodyShapes);
        Assert.Equal(
            OptimizationOpportunitiesQuery.Definition.Name,
            bodyShapesFailure.Finding);
        Assert.Equal(error.Message, bodyShapesFailure.Reason);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_NoMetadataDoesNotProjectFailure()
    {
        var inspection = new LibraryInspection();

        LibraryMetadataService.ApplyOptimizationOpportunitiesResult(
            "native.dll",
            inspection,
            new Output.VerboseLogger(false),
            new OptimizationOpportunitiesResult.NoMetadata());

        Assert.IsType<OptimizationOpportunitiesResult.NoMetadata>(
            inspection.OptimizationOpportunitiesQueryResult);
        Assert.Empty(inspection.PerformanceTriageOpportunities);
        Assert.Null(inspection.OptimizationOpportunities);
        Assert.Null(inspection.InspectionFailures);
    }

    [Fact]
    public void ResourceTriageQuery_RecordsBodyIndexAndDrillMapDuringExecution()
    {
        var registry = LibrarySections.CreateQueryRegistry();
        var trace = new InspectionTrace();
        using var service = SourceLinkService.OpenPrefetched(
            typeof(SectionPipelineTests).Assembly.Location,
            _ => { });
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = service.Context,
            BodyAnalysisFeatures = Analysis.LibraryBodyAnalysisFeatures.LeakTriage,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [ResourceTriageQuery.Definition],
            context,
            trace.RecordQueryExecution);

        Assert.IsType<ResourceTriageResult.Available>(
            results.Get(ResourceTriageQuery.Definition));
        var bodyIndex = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body index");
        Assert.Contains("LeakTriage", bodyIndex.Detail.ToString());
        Assert.Single(
            trace.Resources,
            resource => resource.Resource == "drill map");
    }

    [Fact]
    public void TopLeverageQuery_RecordsAndReturnsTheBodyIndexItBuilds()
    {
        var registry = LibrarySections.CreateQueryRegistry();
        var trace = new InspectionTrace();
        using var service = SourceLinkService.OpenPrefetched(
            typeof(SectionPipelineTests).Assembly.Location,
            _ => { });
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = service.Context,
            BodyAnalysisFeatures = Analysis.LibraryBodyAnalysisFeatures.MethodEvidence,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [TopLeverageQuery.Definition],
            context,
            trace.RecordQueryExecution);

        var available = Assert.IsType<TopLeverageResult.Available>(
            results.Get(TopLeverageQuery.Definition));
        Assert.NotEmpty(available.Methods);
        var bodyIndex = Assert.Single(trace.Resources, r => r.Resource == "body index");
        Assert.StartsWith("built in", bodyIndex.Detail.ToString());
        Assert.Contains("MethodEvidence", bodyIndex.Detail.ToString());
        var drillMap = Assert.Single(trace.Resources, r => r.Resource == "drill map");
        Assert.StartsWith("built in", drillMap.Detail.ToString());
    }

    [Fact]
    public void TopLeverageQuery_BodyIndexFailureRemainsTyped()
    {
        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TopLeverageQuery.Definition],
            NullQueryContext());

        var failed = Assert.IsType<TopLeverageResult.Failed>(
            results.Get(TopLeverageQuery.Definition));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public void TopLeverageQuery_NoMetadata_DoesNotAcquireBodyIndex()
    {
        bool acquired = false;

        TopLeverageResult result = LibrarySections.ExecuteTopLeverageQuery(
            hasMetadata: false,
            () =>
            {
                acquired = true;
                throw new InvalidOperationException("must not acquire");
            });

        Assert.IsType<TopLeverageResult.NoMetadata>(result);
        Assert.False(acquired);
    }

    [Fact]
    public void TopLeverageQuery_FailureProjectsToInspectionFailure()
    {
        var inspection = new LibraryInspection();
        var error = new IOException("body index failed");
        bool acquiredDrillMap = false;

        LibraryMetadataService.ApplyTopLeverageResult(
            "broken.dll",
            inspection,
            new Output.VerboseLogger(false),
            new TopLeverageResult.Failed(error),
            () =>
            {
                acquiredDrillMap = true;
                throw new InvalidOperationException("must not acquire");
            });

        var failed = Assert.IsType<TopLeverageResult.Failed>(
            inspection.TopLeverageQueryResult);
        Assert.Same(error, failed.Error);
        var projected = Assert.Single(inspection.InspectionFailures!);
        Assert.Equal(SectionNames.TopLeverage, projected.Section);
        Assert.Equal(TopLeverageQuery.Definition.Name, projected.Finding);
        Assert.True(LibraryCommand.FailureAffectsSection(
            projected.Section,
            SectionNames.TopLeverage));
        Assert.False(acquiredDrillMap);
        Assert.Null(inspection.TopLeverage);
    }

    [Fact]
    public void TopLeverageQuery_NoMetadata_DoesNotProjectFailureOrAcquireDrillMap()
    {
        var inspection = new LibraryInspection();
        bool acquiredDrillMap = false;

        LibraryMetadataService.ApplyTopLeverageResult(
            "native.dll",
            inspection,
            new Output.VerboseLogger(false),
            new TopLeverageResult.NoMetadata(),
            () =>
            {
                acquiredDrillMap = true;
                throw new InvalidOperationException("must not acquire");
            });

        Assert.IsType<TopLeverageResult.NoMetadata>(
            inspection.TopLeverageQueryResult);
        Assert.False(acquiredDrillMap);
        Assert.Null(inspection.TopLeverage);
        Assert.Null(inspection.InspectionFailures);
    }

    [Fact]
    public void Trace_RecordsAQueryThatThrew()
    {
        // The report is written in a finally, so a run that failed still says what it had done by
        // the time it failed. If the throwing query were dropped from the record, the trace would
        // implicate whichever query ran last before it.
        var boom = new InspectionQuery<int>("Boom", InspectionCost.NetworkFree);
        var registry = LibrarySections.CreateQueryRegistry()
            .Add<int>(boom, _ => throw new InvalidOperationException("boom"));
        var trace = new InspectionTrace();
        using var context = new InspectionQueryContext
        {
            AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            Trace = trace,
        };

        Assert.Throws<InvalidOperationException>(
            () => registry.Run([boom], context, trace.RecordQueryExecution));

        Assert.Equal([boom], trace.QueryExecutions.Select(e => e.Query));
    }

    [Fact]
    public void Tracing_DoesNotChangeTheWorkTheRunDoes()
    {
        // A diagnostic that perturbs what it measures is worse than none. Held against the shared
        // query scan count, which is the observable the atomicity gates already rely on.
        static int RunAndCountSharedScans(InspectionTrace? trace)
        {
            var registry = LibrarySections.CreateQueryRegistry();
            using var context = new InspectionQueryContext
            {
                AssemblyPath = typeof(SectionPipelineTests).Assembly.Location,
                Model = new LibraryInspection(),
                Logger = new Output.VerboseLogger(false),
                Trace = trace,
            };

            Action<InspectionQueryDefinition, TimeSpan>? recordExecution =
                trace is null
                    ? null
                    : (query, elapsed) => trace.RecordQueryExecution(query, elapsed);
            registry.Run(
                [AuditMetadataQuery.Definition],
                context,
                recordExecution);
            return context.SharedQueryCount;
        }

        Assert.Equal(RunAndCountSharedScans(trace: null), RunAndCountSharedScans(new InspectionTrace()));
    }

    [Fact]
    public void SharedSessionQueries_MapTheirOwnFailuresRatherThanThrowing()
    {
        // A query must map an inspected-artifact fault into its typed result rather than escaping
        // query execution and degrading the whole command to one generic failure.
        //
        // A disposed session is the fault injector: AssemblyImage.EnsureAlive throws
        // ObjectDisposedException on every facet, so it faults each scanner at the point where it
        // touches metadata, deterministically and on every platform.
        var session = AssemblyInspectionSession.Open(typeof(SectionPipelineTests).Assembly.Location);
        session.Dispose();

        var logger = new Output.VerboseLogger(false);
        const string Path = "disposed.dll";

        var classifiedResult = Assert.IsType<ClassifiedMethodsResult.Failed>(
            ClassifiedMethodsQuery.Execute(session));
        var classifiedModel = new LibraryInspection();
        LibraryMetadataService.ApplyClassifiedMethodsResult(
            Path,
            classifiedModel,
            logger,
            classifiedResult);
        Assert.IsType<FindingInspection<ClassifiedMethodObservation>.Failed>(
            classifiedModel.ClassifiedMethodInspection!.Value);

        var auditResult = Assert.IsType<AuditMetadataResult.Failed>(
            AuditMetadataQuery.Execute(session));
        var auditModel = new LibraryInspection();
        LibraryMetadataService.ApplyAuditMetadataResult(
            Path,
            auditModel,
            logger,
            auditResult);

        Assert.Null(auditModel.AuditMetadata);
        Assert.NotNull(auditModel.AuditSignals);
    }

    [Fact]
    public void SharedSessionQueries_DoNotObserveAPathRetargetedMidRun()
    {
        // The actual attack, run in-process rather than described in a comment.
        //
        // A directory link points at assembly A. One query runs, which opens the shared session.
        // The link is then retargeted to assembly B and the remaining query runs. Both queries
        // must still report A: an open handle keeps reading its original target, so sharing one
        // open is what makes the run coherent. Without it each query reopens through the link
        // and picks up B, and the command still exits 0 with output that looks correct.
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

            Assert.NotEqual(expectedA.Audit, expectedB.Audit);

            var queryRegistry = LibrarySections.CreateQueryRegistry();
            var model = new LibraryInspection();
            using var context = new InspectionQueryContext
            {
                AssemblyPath = linkedAssembly,
                Model = model,
                Logger = new Output.VerboseLogger(false),
            };

            InspectionQueryResults classifiedResults = queryRegistry.Run(
                [ClassifiedMethodsQuery.Definition],
                context);
            LibraryMetadataService.ApplyClassifiedMethodsResult(
                linkedAssembly,
                model,
                context.Logger,
                classifiedResults.Get(ClassifiedMethodsQuery.Definition));

            Assert.True(TryLinkDirectory(link, dirB), "Could not retarget the directory link.");

            InspectionQueryResults auditResults = queryRegistry.Run(
                [AuditMetadataQuery.Definition],
                context);
            LibraryMetadataService.ApplyAuditMetadataResult(
                linkedAssembly,
                model,
                context.Logger,
                auditResults.Get(AuditMetadataQuery.Definition));

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
    public void SharedSessionQueries_ObserveTheImageTheCommandAlreadyOpened()
    {
        // The wider half of the same attack, and the reason the shared session borrows instead of
        // opening. A command opens the assembly once for identity, presence flags, and debug
        // directory facts, then hands that PdbContext to the queries. If the query session
        // opened AssemblyPath again, everything between the two opens would be a window in which
        // the path can be retargeted, and the command would report one assembly's identity beside
        // another assembly's counts -- with a zero exit code.
        //
        // Sharing one session among queries does not close that window; it only moves it
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

            Assert.NotEqual(expectedA.Audit, expectedB.Audit);

            // Stand in for the command's own open: identity is read here, queries run later.
            using var metadataContext = PdbContext.Open(linkedAssembly);
            var identity = metadataContext.ExtractAssemblyInfo();

            // Everything between the command's open and the scanner run is the window under test.
            Assert.True(TryLinkDirectory(link, dirB), "Could not retarget the directory link.");

            var model = new LibraryInspection();
            using var context = new InspectionQueryContext
            {
                AssemblyPath = linkedAssembly,
                Model = model,
                Logger = new Output.VerboseLogger(false),
                MetadataContext = metadataContext,
            };

            RunClassifiedAndAuditQueries(context);

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
        // reading unmapped memory. The dangerous shapes are a MethodBodySource and declaration
        // index obtained WHILE the lender was alive: each captures the reader, so it survives the
        // borrow's own disposal flag being false and can read through a released handle. That is
        // an AccessViolationException, which is uncatchable and kills the process -- so if a
        // warmed reader-backed path stops consulting the lender, this test does not merely fail,
        // it takes the test host down. Either way it stops the build.
        //
        // Found by review: an earlier version of this gate touched MethodBodies only AFTER
        // disposal, so the cold property threw from the disposed PEReader and the missing lender
        // check went unnoticed. Warming it first is the whole point.
        var path = typeof(SectionPipelineTests).Assembly.Location;

        foreach (var prefetched in new[] { false, true })
        {
            // SourceLinkService is how commands open an assembly, and it owns the PdbContext the
            // queries borrow. Both open modes are covered because they map the image differently.
            var service = prefetched
                ? SourceLinkService.OpenPrefetched(path)
                : SourceLinkService.Open(path);
            var lender = service.Context;

            var borrowed = AssemblyInspectionSession.Borrow(lender);

            // Warm both reader-backed paths while the lender is still alive.
            var bodies = borrowed.MethodBodies;
            Assert.NotEmpty(bodies.EnumerateMethods());
            MetadataTypeDefinitionName declarationName =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "DotnetInspector.Tests",
                        ["SectionPipelineTests"]))
                    .Name;
            Assert.IsType<TypeDeclarationResult.Defined>(
                borrowed.ProbeDeclaration(declarationName));

            service.Dispose();

            Assert.Throws<ObjectDisposedException>(() => bodies.EnumerateMethods());
            Assert.Throws<ObjectDisposedException>(() => borrowed.MethodBodies);
            Assert.Throws<ObjectDisposedException>(
                () => borrowed.ProbeDeclaration(declarationName));

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

    // ===== Presence flag / CanRender discovery tests =====

    [Fact]
    public void CanRender_ExtensionMethods_UsesPresenceFlag()
    {
        var pipeline = LibrarySections.CreatePipeline();
        // The query has not run (ExtensionMethods is null), but the presence flag is set.
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
    public void Discoverable_UnsafeMembers_UsesDegradedDecodeStatusAfterNegativePresenceProbe()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
            UnsafeEvidencePresent = false,
            UnsafeSignatureDecodeStatus = SignatureDecodeStatus.Degraded
        };

        var discoverable = pipeline.GetDiscoverableSections(model);

        Assert.Contains("Unsafe Members", discoverable);
    }

    [Fact]
    public void Discoverable_UnsafeMembers_NegativePresenceProbeOverridesMethodBodyFallback()
    {
        var pipeline = LibrarySections.CreatePipeline();
        var model = new LibraryInspection
        {
            AssemblyInfo = new AssemblyInfo(),
            HasMethodBodies = true,
            UnsafeEvidencePresent = false
        };

        var discoverable = pipeline.GetDiscoverableSections(model);

        Assert.DoesNotContain("Unsafe Members", discoverable);
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

        Assert.DoesNotContain("Integration: OpenTelemetry", effective);
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

        Assert.DoesNotContain(prefixed, effective);
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
    public void PackagePipeline_EverySelectableSectionBelongsToAnAuthoredCategory()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categorized = pipeline.GetCategoryMap()
            .SelectMany(pair => pair.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncategorized = pipeline.SelectableSectionNames
            .Where(name => !categorized.Contains(name))
            .ToArray();

        Assert.True(
            uncategorized.Length == 0,
            $"Package section(s) have no authored category: {string.Join(", ", uncategorized)}");
    }

    [Fact]
    public void PackagePipeline_BaseScopeIsDerivedFromPackageAndFilesCategories()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var categories = pipeline.GetCategoryMap();

        Assert.Equal(
            [SectionCategoryNames.Files, SectionCategoryNames.Package],
            pipeline.GetBaseCategoryDoors().OrderBy(name => name, StringComparer.Ordinal));

        var expected = categories[SectionCategoryNames.Package]
            .Concat(categories[SectionCategoryNames.Files])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.BaseSectionNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void PackagePipeline_CategoryCompositionMatchesPackageEvidenceDomains()
    {
        var categories = PackageSectionDescriptors.CreatePipeline().GetCategoryMap();

        Assert.Equal(
            [
                PackageSections.PackageInfo,
                PackageSections.Signals,
                PackageSections.Statistics,
                PackageSections.TargetFrameworks,
                PackageSections.Signature,
                PackageSections.Dependencies,
                PackageSections.Vulnerabilities,
                PackageSections.Manifest,
                PackageSections.RuntimeDependencies,
                PackageSections.Files
            ],
            categories[SectionCategoryNames.Package]);
        Assert.Equal(
            [PackageSections.Dependencies, PackageSections.RuntimeDependencies],
            categories[SectionCategoryNames.Dependencies]);
        Assert.Equal(
            [
                PackageSections.Signals,
                PackageSections.AuditArtifactText,
                PackageSections.AuditFindings,
                PackageSections.AuditIdentifierConfusion,
                PackageSections.Signature,
                PackageSections.Vulnerabilities,
                PackageSections.SourceLinkAvailability,
                PackageSections.SourceLinkMissingFiles,
                PackageSections.SourceLinkIntegrity
            ],
            categories[SectionCategoryNames.Audit]);
    }

    [Theory]
    [InlineData("ordinary text", false)]
    [InlineData("C:\\tmp\\package", false)]
    [InlineData("literal \\u202E text", false)]
    [InlineData("concerning\u202Etext", true)]
    public void PackagePipeline_ArtifactTextAuditEffectivenessUsesTypedConcerns(
        string packageName,
        bool expected)
    {
        var model = new InspectionResult
        {
            PackageName = packageName,
            Version = "1.0.0",
        };

        Assert.Equal(
            expected,
            PackageSectionDescriptors.AuditArtifactText.CanRender(model));
    }

    [Fact]
    public void PackagePipeline_PackageContentAuditRendersOnlyWithFindings()
    {
        var model = new InspectionResult
        {
            PackageContentAudit = new PackageContentAuditResult(
                [new PackageContentAuditFinding(
                    "README.md",
                    PackageContentFindingKind.NonGraphicText,
                    TextConcern.Format,
                    new InertString(TextPolicy.Field, "encoded"))],
                EligibleFiles: 1,
                ScannedFiles: 1,
                ScannedBytes: 7,
                Complete: true),
        };

        Assert.True(PackageSectionDescriptors.AuditFindings.CanRender(model));
        model.PackageContentAudit = model.PackageContentAudit with { Findings = [] };
        Assert.False(PackageSectionDescriptors.AuditFindings.CanRender(model));
    }

    [Theory]
    [InlineData("Contoso.Utilities", false)]
    [InlineData("C:\\tmp\\package", false)]
    [InlineData("Δelta.Tools", true)]
    [InlineData("Ѕystem.Text.Json", true)]
    public void PackagePipeline_IdentifierConfusionEffectivenessUsesTypedConcerns(
        string packageName,
        bool expected)
    {
        var model = new InspectionResult
        {
            PackageName = packageName,
            Version = "1.0.0",
        };

        Assert.Equal(
            expected,
            PackageSectionDescriptors.AuditIdentifierConfusion.CanRender(model));
    }

    [Fact]
    public void PackagePipeline_BaseCategoriesPreserveAutomaticCandidateSets()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(
            [PackageSections.Summary],
            pipeline.GetCandidateSections(Verbosity.Quiet));
        Assert.Equal(
            [PackageSections.PackageInfo],
            pipeline.GetCandidateSections(Verbosity.Minimal));
        Assert.Equal(
            new[]
            {
                PackageSections.Summary,
                PackageSections.PackageInfo,
                PackageSections.FilesReadme,
                PackageSections.TargetFrameworks,
                PackageSections.FilesNuspec,
                PackageSections.FilesSkills,
                PackageSections.Signature,
                PackageSections.Dependencies,
                PackageSections.Manifest,
                PackageSections.RuntimeDependencies
            }.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.GetCandidateSections(Verbosity.Normal)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                PackageSections.Summary,
                PackageSections.PackageInfo,
                PackageSections.FilesReadme,
                PackageSections.Signals,
                PackageSections.Statistics,
                PackageSections.TargetFrameworks,
                PackageSections.FilesNuspec,
                PackageSections.FilesSkills,
                PackageSections.Signature,
                PackageSections.Dependencies,
                PackageSections.Vulnerabilities,
                PackageSections.Manifest,
                PackageSections.RuntimeDependencies
            }.OrderBy(name => name, StringComparer.Ordinal),
            pipeline.GetCandidateSections(Verbosity.Detailed)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(
            [
                PackageSections.PackageInfo,
                PackageSections.FilesReadme,
                PackageSections.FilesNuspec,
                PackageSections.Signature,
                PackageSections.Manifest
            ],
            pipeline.BareSelectSectionNames);
    }

    [Fact]
    public void PackagePipeline_HasExpectedSectionCount()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        Assert.Equal(21, pipeline.AllSectionNames.Length);
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
        Assert.Contains(PackageSections.AuditArtifactText, names);
        Assert.Contains(PackageSections.AuditFindings, names);
        Assert.Contains(PackageSections.AuditIdentifierConfusion, names);
        Assert.Contains("Target Frameworks", names);
        Assert.Contains("Package nuspec file", names);
        Assert.Contains("Statistics", names);
        Assert.Contains("Dependencies", names);
        Assert.Contains("Package files", names);
        Assert.Contains("Package skill files", names);
        Assert.Contains(PackageSections.SourceLinkFiles, names);
        Assert.Contains(PackageSections.SourceLinkAvailability, names);
        Assert.Contains(PackageSections.SourceLinkMissingFiles, names);
        Assert.Contains(PackageSections.SourceLinkIntegrity, names);
        Assert.Contains("Vulnerabilities", names);
        Assert.Contains("Manifest", names);
        Assert.Contains("Runtime Dependencies", names);
    }

    [Fact]
    public void SigningSection_FieldCatalogMatchesCombinedRows()
    {
        var schema = InspectionContext.Default
            .GetSchemaInfo<InspectionResultView>()!
            .ToDocumentSchema()
            .GetSection(PackageSections.Signature);
        var section = new SigningSection
        {
            AuthorVerified = "Yes",
            Publisher = "Publisher",
            Repository = "Repository",
            RepositoryVerified = "Yes",
            Signed = "Yes",
            Status = "Status",
        };

        Assert.Equal(
            SigningSection.FieldNames,
            section.ToMarkoutFields().Select(field => field.Key));
        Assert.Equal(
            SigningSection.FieldNames,
            schema!.Items.Select(item => item.Name));
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
        Assert.DoesNotContain("Summary", effective);
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
    public void PackagePipeline_CandidatesSeparateQuietSummaryFromMinimalInfo()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();

        Assert.Equal(
            [PackageSections.Summary],
            pipeline.GetCandidateSections(Verbosity.Quiet));
        Assert.Equal(
            [PackageSections.PackageInfo],
            pipeline.GetCandidateSections(Verbosity.Minimal));
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
    public void PackagePipeline_IdentifierConfusionAudit_DemandsRegistrationMetadata()
    {
        var pipeline = PackageSectionDescriptors.CreatePipeline();
        var options = new InspectionOptions
        {
            IncludeSections =
            [
                PackageSections.AuditIdentifierConfusion,
            ],
        };

        Assert.True(
            PackageCommand.RequiresPackageMetadata(options, pipeline));
        Assert.True(
            PackageCommand.AllowsVulnerabilityTraffic(options));
        Assert.Equal(
            Verbosity.Detailed,
            pipeline.GetRequiredVerbosity(options.IncludeSections));
        Assert.True(
            PackageCommand.RequiresPackageMetadata(
                options with
                {
                    IncludeSections = null,
                    Discover =
                    [
                        PackageSections.AuditIdentifierConfusion,
                    ],
                },
                pipeline));
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
                AssemblyName = "Ѕystem.Test",
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
            AssemblyCount = 1
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
        Assert.Equal(7, pipeline.AllSectionNames.Length);
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
        Assert.Contains(
            SectionNames.InspectionFailures,
            names);
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
        Assert.Equal(32, pipeline.AllSectionNames.Length);
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
        Assert.Contains("Body Shapes", names);
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
        Assert.Contains("PDB Source", names);
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
        Assert.DoesNotContain(SectionNames.SourceLocations, overloadPipeline.GetCostAnnotations());

        var detailPipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();
        Assert.Contains(SectionNames.SourceLocations, detailPipeline.AllSectionNames);
        Assert.DoesNotContain(SectionNames.SourceLocations, detailPipeline.GetCostAnnotations());
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
        Assert.DoesNotContain("PDB Source", minimal);
        Assert.Contains("Decompiled Source", normal);
        Assert.Contains("IL", normal);
        Assert.DoesNotContain("Annotated Source", normal);
        Assert.DoesNotContain("PDB Source", normal);
        Assert.Contains("Decompiled Source", detailed);
        Assert.Contains("PDB Source", detailed);
        Assert.Contains("IL", detailed);
        Assert.DoesNotContain("Annotated Source", detailed);
        var annotations = pipeline.GetCostAnnotations();
        Assert.DoesNotContain("Calls", annotations);
        Assert.DoesNotContain("Exception Regions", annotations);
        Assert.DoesNotContain("Callers", annotations);
        Assert.DoesNotContain("Call Graph", annotations);
        Assert.DoesNotContain("Facts", annotations);
        Assert.DoesNotContain("Unsafe Operations", annotations);
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
    public void ApiMemberDetailPipeline_FixedOverview_IsExactlySignature()
    {
        var pipeline = ApiMemberDetailSectionDescriptors.CreatePipeline();

        Assert.Equal([SectionNames.Signature], pipeline.FixedOverviewSectionNames);
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
                SectionNames.PdbSource,
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
                SectionNames.PdbSource,
                SectionNames.SourceDiff,
                SectionNames.IL
            ],
            categories[SectionCategoryNames.Source]);
    }
}
