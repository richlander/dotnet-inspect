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
using DotnetInspector.Fixtures;
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

        var integrations = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            IntegrationSectionNames.Integrations,
        };
        Assert.Contains(
            AssemblyContextIntegrationsQuery.Definition,
            pipeline.GetRequiredQueries(Verbosity.Minimal, integrations));

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
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "body analysis");
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "body index");
        Assert.DoesNotContain(trace.Resources, r => r.Resource == "drill map");
    }

    [Fact]
    public void UnsafeEvidenceQuery_RecordsFocusedAnalysisWithoutBodyIndex()
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
            [UnsafeEvidenceQuery.Definition],
            context,
            trace.RecordQueryExecution);

        var available = Assert.IsType<UnsafeEvidenceResult.Available>(
            results.Get(UnsafeEvidenceQuery.Definition));
        Assert.NotEmpty(available.Evidence);
        var analysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.StartsWith("built in", analysis.Detail.ToString());
        Assert.Contains("MethodEvidence", analysis.Detail.ToString());
        Assert.DoesNotContain(
            "ImplementationProfiles",
            analysis.Detail.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
    }

    [Fact]
    public void UnsafeEvidenceQuery_AnalysisFailureRemainsTyped()
    {
        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [UnsafeEvidenceQuery.Definition],
            NullQueryContext());

        var failed = Assert.IsType<UnsafeEvidenceResult.Failed>(
            results.Get(UnsafeEvidenceQuery.Definition));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public void UnsafeEvidenceQuery_NoMetadata_DoesNotAcquireAnalysis()
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
    [Trait("Speed", "Slow")]
    public void MigratedAnalysisQueries_ShareExecutionWithoutBodyIndex()
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
                Analysis.LibraryBodyAnalysisFeatures.MethodEvidence
                | Analysis.LibraryBodyAnalysisFeatures
                    .ImplementationProfiles
                | Analysis.LibraryBodyAnalysisFeatures
                    .OptimizationOpportunities,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [
                UnsafeEvidenceQuery.Definition,
                ImplementationProfilesQuery.Definition,
                OptimizationOpportunitiesQuery.Definition,
            ],
            context,
            trace.RecordQueryExecution);

        Assert.IsType<UnsafeEvidenceResult.Available>(
            results.Get(UnsafeEvidenceQuery.Definition));
        Assert.IsType<ImplementationProfilesResult.Available>(
            results.Get(ImplementationProfilesQuery.Definition));
        Assert.IsType<OptimizationOpportunitiesResult.Available>(
            results.Get(OptimizationOpportunitiesQuery.Definition));
        var analysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.Contains(
            "ImplementationProfiles",
            analysis.Detail.ToString());
        Assert.Contains(
            "OptimizationOpportunities",
            analysis.Detail.ToString());
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
    }

    [Fact]
    public void ImplementationProfilesQuery_RunsOnlyItsFocusedProducers()
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
                Analysis.LibraryBodyAnalysisFeatures
                    .ImplementationProfiles,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [ImplementationProfilesQuery.Definition],
            context,
            trace.RecordQueryExecution);

        Assert.IsType<ImplementationProfilesResult.Available>(
            results.Get(ImplementationProfilesQuery.Definition));
        var analysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.Contains(
            "ImplementationProfiles",
            analysis.Detail.ToString());
        Assert.DoesNotContain(
            "Allocations",
            analysis.Detail.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "OptimizationOpportunities",
            analysis.Detail.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
    }

    [Fact]
    public void ImplementationProfilesQuery_NoMetadata_DoesNotAcquireAnalysis()
    {
        bool acquired = false;

        ImplementationProfilesResult result =
            LibrarySections.ExecuteImplementationProfilesQuery(
                hasMetadata: false,
                () =>
                {
                    acquired = true;
                    throw new InvalidOperationException(
                        "must not acquire");
                });

        Assert.IsType<ImplementationProfilesResult.NoMetadata>(
            result);
        Assert.False(acquired);
    }

    [Fact]
    public void LibraryMetricsQuery_RunsOnlyItsFocusedProducer()
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
                Analysis.LibraryBodyAnalysisFeatures
                    .ImplementationProfiles,
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [LibraryMetricsQuery.Definition],
            context,
            trace.RecordQueryExecution);

        Assert.IsType<LibraryMetricsResult.Available>(
            results.Get(LibraryMetricsQuery.Definition));
        var analysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.Contains(
            "ImplementationProfiles",
            analysis.Detail.ToString());
        Assert.DoesNotContain(
            "OptimizationOpportunities",
            analysis.Detail.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "drill map");
    }

    [Fact]
    public void LibraryMetricsQuery_NoMetadata_DoesNotAcquireAnalysis()
    {
        bool acquired = false;

        LibraryMetricsResult result =
            LibrarySections.ExecuteLibraryMetricsQuery(
                hasMetadata: false,
                () =>
                {
                    acquired = true;
                    throw new InvalidOperationException(
                        "must not acquire");
                });

        Assert.IsType<LibraryMetricsResult.NoMetadata>(
            result);
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
    [Trait("Speed", "Slow")]
    public void OptimizationOpportunitiesQuery_UsesFocusedBodyAnalysis()
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
        var bodyAnalysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.StartsWith(
            "built in",
            bodyAnalysis.Detail.ToString());
        Assert.Contains(
            "OptimizationOpportunities",
            bodyAnalysis.Detail.ToString());
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "drill map");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void OptimizationOpportunitiesQuery_AllocationFanoutRemainsOptIn()
    {
        Analysis.LibraryBodyAnalysisExecution execution =
            Analysis.LibraryBodyAnalysisService.ExecutePath(
                typeof(SectionPipelineTests).Assembly.Location,
                Analysis.LibraryBodyAnalysisRequest.Create(
                    Analysis.LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));

        var ordinary =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                OptimizationOpportunitiesQuery.Execute(
                    execution.Optimization,
                    includeAllocationFanout: false));
        var fanout =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                OptimizationOpportunitiesQuery.Execute(
                    execution.Optimization,
                    includeAllocationFanout: true));

        Assert.Empty(ordinary.AllocationFanoutOpportunities);
        Assert.NotEmpty(fanout.AllocationFanoutOpportunities);
    }

    [Fact]
    public void OptimizationOpportunitiesQuery_BodyAnalysisFailureRemainsTyped()
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
    public void OptimizationOpportunitiesQuery_NoMetadata_DoesNotAcquireBodyAnalysis()
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
    public void ResourceTriageAndTopLeverage_ShareFocusedAnalysisWithoutBodyIndex()
    {
        var registry = LibrarySections.CreateQueryRegistry();
        var trace = new InspectionTrace();
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        using var service = SourceLinkService.OpenPrefetched(
            path,
            _ => { });
        using var context = new InspectionQueryContext
        {
            AssemblyPath = path,
            Model = new LibraryInspection(),
            Logger = new Output.VerboseLogger(false),
            MetadataContext = service.Context,
            BodyReferenceResolver = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(path)),
            BodyAnalysisRequest =
                Analysis.LibraryBodyAnalysisRequest
                    .CreateResourceLifecycle(
                        Analysis.ArrayPoolResourceEffectModel.Create()),
            Trace = trace,
        };

        InspectionQueryResults results = registry.Run(
            [
                ResourceTriageQuery.Definition,
                TopLeverageQuery.Definition,
            ],
            context,
            trace.RecordQueryExecution);

        Assert.IsType<ResourceTriageResult.Available>(
            results.Get(ResourceTriageQuery.Definition));
        Assert.IsType<TopLeverageResult.Available>(
            results.Get(TopLeverageQuery.Definition));
        var bodyAnalysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.Contains(
            "resource lifecycle: True",
            bodyAnalysis.Detail.ToString());
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
        Assert.Single(
            trace.Resources,
            resource => resource.Resource == "drill map");
    }

    [Fact]
    public void TopLeverageQuery_RecordsFocusedAnalysisWithoutBodyIndex()
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
        var bodyAnalysis = Assert.Single(
            trace.Resources,
            resource => resource.Resource == "body analysis");
        Assert.StartsWith("built in", bodyAnalysis.Detail.ToString());
        Assert.Contains(
            "MethodEvidence",
            bodyAnalysis.Detail.ToString());
        Assert.DoesNotContain(
            trace.Resources,
            resource => resource.Resource == "body index");
        var drillMap = Assert.Single(trace.Resources, r => r.Resource == "drill map");
        Assert.StartsWith("built in", drillMap.Detail.ToString());
    }

    [Fact]
    public void TopLeverageQuery_AnalysisFailureRemainsTyped()
    {
        InspectionQueryResults results = LibrarySections.CreateQueryRegistry().Run(
            [TopLeverageQuery.Definition],
            NullQueryContext());

        var failed = Assert.IsType<TopLeverageResult.Failed>(
            results.Get(TopLeverageQuery.Definition));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public void TopLeverageQuery_MissingProducerRemainsTyped()
    {
        Analysis.LibraryBodyAnalysisExecution execution =
            Analysis.LibraryBodyAnalysisService.ExecutePath(
                typeof(SectionPipelineTests).Assembly.Location,
                Analysis.LibraryBodyAnalysisRequest.Create(
                    Analysis.LibraryBodyAnalysisFeatures.None));

        var failed = Assert.IsType<TopLeverageResult.Failed>(
            TopLeverageQuery.Execute(execution.Leverage));
        Assert.IsType<InvalidOperationException>(failed.Error);
    }

    [Fact]
    public void TopLeverageQuery_NoMetadata_DoesNotAcquireAnalysis()
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
                        "DotnetInspect.Cli.Tests",
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
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                IntegrationSectionNames.Integrations,
            });

        Assert.DoesNotContain(IntegrationSectionNames.Integrations, effective);
        Assert.Contains(IntegrationSectionNames.Integrations, selected);
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
    public void CanRender_IntegrationsSection_UsesConceptPresenceFlags(
        string sectionName)
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
        var selected = pipeline.GetEffectiveSections(model, Verbosity.Detailed,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                IntegrationSectionNames.Integrations,
            });

        Assert.DoesNotContain(IntegrationSectionNames.Integrations, effective);
        Assert.Contains(IntegrationSectionNames.Integrations, selected);
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
}
