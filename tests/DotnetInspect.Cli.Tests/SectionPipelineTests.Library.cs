using ILInspector.Decompiler;
using ILInspector.Metadata;
using Inspector.Findings;
using ILInspector.Research;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
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

public partial class SectionPipelineTests
{
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
        Assert.Equal(51 + MetadataSectionNames.All.Length, pipeline.AllSectionNames.Length);
        Assert.Contains(SectionNames.CloneCandidates, pipeline.AllSectionNames);
        Assert.Contains(IntegrationSectionNames.Integrations, pipeline.AllSectionNames);
        Assert.Contains("Context: Callsite", pipeline.AllSectionNames);
        Assert.Contains("Context: Allocation", pipeline.AllSectionNames);
        Assert.Contains("Context: Safety", pipeline.AllSectionNames);
        Assert.Contains("Context: Cost", pipeline.AllSectionNames);
        Assert.Contains("Context: Exception", pipeline.AllSectionNames);
        Assert.Contains("Context: Instruction", pipeline.AllSectionNames);
        Assert.Contains("Context: Source Location", pipeline.AllSectionNames);
        Assert.Contains("Context: Member", pipeline.AllSectionNames);
        Assert.Contains(ReadyToRunSectionNames.Image, pipeline.AllSectionNames);
        Assert.Contains(ReadyToRunSectionNames.Sections, pipeline.AllSectionNames);
        Assert.Contains(IntegrationSectionNames.Opportunities, pipeline.AllSectionNames);
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
    public void LibraryPipeline_MeasuredBaseInventoriesAreVerbose()
    {
        Assert.Equal(SectionSizeClass.Verbose, LibrarySections.References.SizeClass);
        Assert.Equal(SectionSizeClass.Verbose, LibrarySections.Switches.SizeClass);
        Assert.Equal(SectionSizeClass.Verbose, LibrarySections.PInvokeMethods.SizeClass);
        Assert.Equal(SectionSizeClass.Verbose, LibrarySections.TypeForwarders.SizeClass);
        Assert.Equal(SectionSizeClass.Verbose, LibrarySections.UnionTypes.SizeClass);
    }

    [Fact]
    public void LibraryPipeline_InspectionFailuresRemainTerseAndVisible()
    {
        Assert.Equal(SectionSizeClass.Terse, LibrarySections.InspectionFailures.SizeClass);

        var pipeline = LibrarySections.CreatePipeline();

        Assert.Contains(
            SectionNames.InspectionFailures,
            pipeline.GetCandidateSections(Verbosity.Normal));
    }

    [Fact]
    public void LibraryPipeline_BaseCandidatesFollowMeasuredGrowthClasses()
    {
        var pipeline = LibrarySections.CreatePipeline();

        Assert.Equal(
            new[]
            {
                SectionNames.EcosystemDependencies,
                SectionNames.LibraryInfo,
                SectionNames.InspectionFailures,
                SectionNames.Signals,
                SectionNames.Symbols,
                SectionNames.CustomAttributes,
                SectionNames.Resources,
            }.OrderBy(static name => name, StringComparer.Ordinal),
            pipeline.GetCandidateSections(Verbosity.Normal)
                .OrderBy(static name => name, StringComparer.Ordinal));
        Assert.Equal(
            new[]
            {
                SectionNames.EcosystemDependencies,
                SectionNames.LibraryInfo,
                SectionNames.InspectionFailures,
                SectionNames.References,
                SectionNames.Signals,
                SectionNames.Symbols,
                SectionNames.AsyncMethods,
                SectionNames.CustomAttributes,
                SectionNames.ExtensionMethods,
                SectionNames.PInvokeMethods,
                SectionNames.Resources,
                SectionNames.Switches,
                SectionNames.TypeForwarders,
                SectionNames.UnionTypes,
            }.OrderBy(static name => name, StringComparer.Ordinal),
            pipeline.GetCandidateSections(Verbosity.Detailed)
                .OrderBy(static name => name, StringComparer.Ordinal));
        Assert.Equal(
            [SectionNames.LibraryInfo, SectionNames.Symbols, SectionNames.Signals],
            pipeline.BareSelectSectionNames);
    }

    [Theory]
    [InlineData(SectionNames.References)]
    [InlineData(SectionNames.Switches)]
    [InlineData(SectionNames.PInvokeMethods)]
    [InlineData(SectionNames.TypeForwarders)]
    [InlineData(SectionNames.UnionTypes)]
    public void LibraryPipeline_MeasuredVerboseBaseInventoryRemainsExplicitlySelectable(
        string section)
    {
        var pipeline = LibrarySections.CreatePipeline();
        var include = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section };

        Assert.Equal(Verbosity.Detailed, pipeline.GetRequiredVerbosity(include));
        Assert.Equal([section], pipeline.GetCandidateSections(Verbosity.Detailed, include));
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
            "Library Info", "Symbols", "Signals", "References", "Ecosystem Dependencies",
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
        foreach (var integration in new[]
                 {
                     IntegrationSectionNames.Integrations,
                     IntegrationSectionNames.Opportunities,
                 })
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
    public void LibraryPipeline_QuerySectionsRemainUncategorized()
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

        Assert.Equal(
            [
                SectionNames.UnsafeMembers,
                SectionNames.MemberMetrics,
                SectionNames.BodyShapes,
                SectionNames.BodyShapeSummary,
                SectionNames.CloneCandidates,
            ],
            uncategorized);
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
                     .Concat([
                         IntegrationSectionNames.Integrations,
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
    public void ResourceTriageQuery_NoMetadata_DoesNotAcquireLifecycleAnalysis()
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
            "body analysis failed");

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
                // Coordinate-gated like the Context: sections above: without a heap coordinate
                // there is no value to show, so the section is legitimately not discoverable.
                .Except([MetadataSectionNames.Heap], StringComparer.OrdinalIgnoreCase)
                // @Metadata table and heap sections are data-gated: a table with no rows or a heap
                // with no bytes in this image is legitimately not discoverable, and listing it
                // would advertise an empty section. Derived from the fixture image rather than
                // hard-coded, so one that gains or loses content moves the exclusion with it and
                // every non-empty one stays required.
                .Except(EmptyMetadataSectionsInFixtureImage(), StringComparer.OrdinalIgnoreCase)
                // The synthetic library fixture is not backed by a ReadyToRun image. These
                // explicit-only sections are covered by the CoreLib-backed ReadyToRun lens tests.
                .Except(ReadyToRunSectionNames.All, StringComparer.OrdinalIgnoreCase)
                // The hierarchy is structurally discoverable without traversal; effective
                // discovery deliberately does not acquire it merely to prove applicability.
                .Except([SectionNames.ReferenceHierarchy], StringComparer.OrdinalIgnoreCase)
            : registered;
        var missing = expected
            .Where(name => !discoverable.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{command} -D missed selectable section(s): {string.Join(", ", missing)}");
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
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                IntegrationSectionNames.Opportunities,
            });

        Assert.DoesNotContain(IntegrationSectionNames.Opportunities, effective);
        Assert.Contains(IntegrationSectionNames.Opportunities, selected);
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

    [Fact]
    public void SectionPipeline_AddCategory_WithUnregisteredSectionName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LibrarySections.CreatePipeline().AddCategory("@Bogus", "No Such Section"));

        Assert.Contains("No Such Section", ex.Message, StringComparison.Ordinal);
    }

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
                new PackageFile("LICENSE", 1, IsLicense: true),
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
                ImplementationProfilesQuery.Definition,
                MetadataImageQuery.Definition,
                OptimizationOpportunitiesQuery.Definition,
                ReadyToRunImageQuery.Definition,
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
}
