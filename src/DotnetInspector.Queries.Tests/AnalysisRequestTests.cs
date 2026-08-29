using System.Collections.Immutable;
using System.Reflection;

namespace DotnetInspector.Queries.Tests;

public sealed class AnalysisRequestTests
{
    [Fact]
    public void AnalysisRequest_DeclaresCompleteClosedFieldSet()
    {
        Assert.Equal(
            [
                nameof(AnalysisRequest.Analysis),
                nameof(AnalysisRequest.Mode),
                nameof(AnalysisRequest.Projection),
                nameof(AnalysisRequest.ReportSurface),
                nameof(AnalysisRequest.Universe),
            ],
            typeof(AnalysisRequest)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order());
        Assert.Equal(
            [
                AnalysisReportSurfaceKind.Member,
                AnalysisReportSurfaceKind.Type,
                AnalysisReportSurfaceKind.Library,
                AnalysisReportSurfaceKind.Root,
                AnalysisReportSurfaceKind.Workspace,
            ],
            Enum.GetValues<AnalysisReportSurfaceKind>());
        Assert.Equal(
            [
                AnalysisQuestionMode.Targeted,
                AnalysisQuestionMode.Census,
            ],
            Enum.GetValues<AnalysisQuestionMode>());
    }

    [Fact]
    public void AnalysisCapability_StructuralDiscoveryDoesNotResolveContentExecuteProducersOrProbeEffectiveness()
    {
        Fixture fixture = new();

        AnalysisDescriptor descriptor = Assert.Single(fixture.Catalog.Analyses);

        Assert.Same(fixture.Analysis, descriptor);
        Assert.Equal(Fixture.IntegrationAnalysisId, descriptor.Id);
        Assert.Equal(0, fixture.ProducerExecutions);
    }

    [Fact]
    public void AnalysisCapability_ProducerExecutionProbeIsObservable()
    {
        Fixture fixture = new();

        fixture.QueryCatalog.Run([fixture.ProducerQuery], new object());

        Assert.Equal(1, fixture.ProducerExecutions);
    }

    [Fact]
    public void AnalysisCapability_ListsConfiguredUnobservedIntegrationDescriptors()
    {
        Fixture fixture = new();
        AnalysisDescriptor descriptor = Assert.Single(fixture.Catalog.Analyses);

        Assert.Equal(
            [fixture.LoggingConcept, fixture.OpenTelemetryConcept],
            descriptor.UniverseRequirements
                .SelectMany(requirement => requirement.Affected)
                .Distinct());
        Assert.Empty(fixture.HealthyUniverse.Failures);
    }

    [Fact]
    public void AnalysisRequest_ReportSurfaceAndUniverseAreIndependent()
    {
        Fixture fixture = new();
        AnalysisReportSurface surface = fixture.WorkspaceSurface();
        AnalysisUniverseDescription narrow = fixture.Universe(
            new UniverseIdentity("narrow"),
            new Boundary("two types"));
        AnalysisUniverseDescription wide = fixture.Universe(
            new UniverseIdentity("wide"),
            new Boundary("twenty types"));

        AnalysisRequestPlan first = fixture.Accepted(
            fixture.Request(surface: surface, universe: narrow));
        AnalysisRequestPlan second = fixture.Accepted(
            fixture.Request(surface: surface, universe: wide));

        Assert.Same(first.ReportSurface, second.ReportSurface);
        Assert.Same(narrow, first.Universe);
        Assert.Same(wide, second.Universe);
    }

    [Fact]
    public void AnalysisRequest_MemberReportMayConsumeWorkspaceUniverse()
    {
        Fixture fixture = new();
        AnalysisReportSurface member = new(
            AnalysisReportSurfaceKind.Member,
            new SurfaceIdentity("member-report"),
            [
                new AnalysisTargetBinding(
                    fixture.MemberAnchor,
                    new TargetIdentity("M:Example.Run")),
            ]);

        AnalysisRequestPlan plan = fixture.Accepted(
            fixture.Request(
                surface: member,
                universe: fixture.HealthyUniverse,
                mode: AnalysisQuestionMode.Targeted));

        Assert.Equal(AnalysisReportSurfaceKind.Member, plan.ReportSurface.Kind);
        Assert.Same(fixture.HealthyUniverse, plan.Universe);
    }

    [Fact]
    public void AnalysisRequest_UniverseBreadthCannotWidenReportSurface()
    {
        Fixture fixture = new();
        AnalysisReportSurface surface = fixture.WorkspaceSurface();
        AnalysisRequestPlan plan = fixture.Accepted(
            fixture.Request(
                surface: surface,
                universe: fixture.Universe(
                    new UniverseIdentity("project-graph"),
                    new Boundary("all project packages"))));

        Assert.Same(surface, plan.ReportSurface);
        Assert.Equal(AnalysisReportSurfaceKind.Workspace, plan.ReportSurface.Kind);
    }

    [Fact]
    public void AnalysisRequest_TargetedRequiresAcceptedAnchor()
    {
        Fixture fixture = new();
        AnalysisReportSurface surface = new(
            AnalysisReportSurfaceKind.Member,
            new SurfaceIdentity("member-report"),
            []);

        AnalysisRequestRejection rejection = fixture.Rejected(
            fixture.Request(
                surface: surface,
                mode: AnalysisQuestionMode.Targeted));

        Assert.Equal(AnalysisRequestRejectionReason.InvalidMode, rejection.Reason);
        Assert.Empty(rejection.TargetRoles);
    }

    [Fact]
    public void AnalysisRequest_CensusRejectsPrivilegedContainedAnchor()
    {
        Fixture fixture = new(allowCensusAnchor: true);
        AnalysisReportSurface surface = new(
            AnalysisReportSurfaceKind.Workspace,
            new SurfaceIdentity("workspace"),
            [
                new AnalysisTargetBinding(
                    fixture.WorkspaceDomain,
                    new TargetIdentity("workspace")),
                new AnalysisTargetBinding(
                    fixture.MemberAnchor,
                    new TargetIdentity("M:Example.Run")),
            ]);

        AnalysisRequestRejection rejection = fixture.Rejected(
            fixture.Request(surface: surface));

        Assert.Equal(AnalysisRequestRejectionReason.InvalidMode, rejection.Reason);
    }

    [Fact]
    public void AnalysisRequest_ModeValidationDerivesFromDeclaredTargetFunctions()
    {
        Fixture anchor = new(
            memberTargetFunction: AnalysisTargetFunction.PrivilegedAnchor);
        Fixture domain = new(
            memberTargetFunction: AnalysisTargetFunction.ReportDomain);
        AnalysisReportSurface anchorSurface = anchor.MemberSurface();
        AnalysisReportSurface domainSurface = domain.MemberSurface();

        AnalysisRequestPlan accepted = anchor.Accepted(
            anchor.Request(
                surface: anchorSurface,
                mode: AnalysisQuestionMode.Targeted));
        AnalysisRequestRejection rejected = domain.Rejected(
            domain.Request(
                surface: domainSurface,
                mode: AnalysisQuestionMode.Targeted));

        Assert.Equal(anchor.MemberAnchor.Id, domain.MemberAnchor.Id);
        Assert.Equal(
            AnalysisTargetFunction.PrivilegedAnchor,
            accepted.ReportSurface.Targets.Single().Role.Function);
        Assert.Equal(
            AnalysisRequestRejectionReason.InvalidMode,
            rejected.Reason);
    }

    [Fact]
    public void AnalysisRequest_RejectsMissingOrUnboundedUniverseBeforeProducerExecution()
    {
        Fixture fixture = new();

        Assert.Equal(
            AnalysisRequestRejectionReason.MissingUniverse,
            fixture.Rejected(
                fixture.Request() with
                {
                    Universe = null,
                }).Reason);
        Assert.Equal(
            AnalysisRequestRejectionReason.UnboundedUniverse,
            fixture.Rejected(
                fixture.Request(
                    universe: fixture.Universe(
                        new UniverseIdentity("unbounded"),
                        new Boundary("unbounded"),
                        isFinite: false))).Reason);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsatisfiedUniverseBeforeProducerExecution()
    {
        Fixture fixture = new();
        AnalysisUniverseDescription universe = fixture.Universe(
            new UniverseIdentity("partial"),
            new Boundary("selected types"),
            capabilities:
            [
                fixture.SelectedTypesCapability,
                fixture.OrderedParticipantsCapability,
                new AnalysisUniverseCapabilityDescriptor(
                    fixture.ObservedEvidenceCapability.Id,
                    "same textual identity"),
            ]);

        AnalysisRequestRejection rejection = fixture.Rejected(
            fixture.Request(universe: universe));

        Assert.Equal(
            AnalysisRequestRejectionReason.UnsatisfiedUniverse,
            rejection.Reason);
        AnalysisUniverseRequirementDescriptor requirement =
            Assert.Single(rejection.UniverseRequirements);
        Assert.Same(fixture.ObservedEvidenceRequirement, requirement);
        Assert.Equal(
            [fixture.LoggingConcept, fixture.OpenTelemetryConcept],
            requirement.Affected);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedModeBeforeProducerExecution()
    {
        Assert.Equal(
            AnalysisRequestRejectionReason.UnsupportedMode,
            UnsupportedMode());
    }

    [Fact]
    public void AnalysisCapability_RequiresConfiguredOwnerIssuedDescriptorIdentity()
    {
        Fixture fixture = new();
        Fixture other = new();

        AnalysisRequestRejection rejection = fixture.Rejected(
            fixture.Request() with
            {
                Analysis = other.Analysis,
            });

        Assert.Equal(
            AnalysisRequestRejectionReason.InvalidRequest,
            rejection.Reason);
        Assert.Equal(fixture.Analysis.Id, other.Analysis.Id);
    }

    [Fact]
    public void AnalysisDescriptor_RejectsModeWithoutSatisfiableSurfaceOrProjection()
    {
        var reportDomain = new AnalysisTargetRoleDescriptor(
            new AnalysisDeclarationId("target.domain"),
            AnalysisTargetFunction.ReportDomain,
            1,
            1);
        var anchor = new AnalysisTargetRoleDescriptor(
            new AnalysisDeclarationId("target.anchor"),
            AnalysisTargetFunction.PrivilegedAnchor,
            1,
            1);
        var rows = new AnalysisProjectionDescriptor(
            new AnalysisDeclarationId("projection.rows"));

        Assert.Throws<ArgumentException>(() => new AnalysisDescriptor(
            new AnalysisDeclarationId("analysis.impossible-surface"),
            revision: 1,
            InspectionCost.NetworkFree,
            [AnalysisQuestionMode.Targeted],
            [
                new AnalysisReportSurfaceSupport(
                    AnalysisReportSurfaceKind.Type,
                    AnalysisQuestionMode.Targeted,
                    [anchor]),
                new AnalysisReportSurfaceSupport(
                    AnalysisReportSurfaceKind.Member,
                    AnalysisQuestionMode.Targeted,
                    [reportDomain]),
            ],
            [],
            [],
            [],
            [
                new AnalysisProjectionSupport(
                    rows,
                    [AnalysisQuestionMode.Targeted]),
            ]));
        Assert.Throws<ArgumentException>(() => new AnalysisDescriptor(
            new AnalysisDeclarationId("analysis.missing-projection"),
            revision: 1,
            InspectionCost.NetworkFree,
            [
                AnalysisQuestionMode.Targeted,
                AnalysisQuestionMode.Census,
            ],
            [
                new AnalysisReportSurfaceSupport(
                    AnalysisReportSurfaceKind.Member,
                    AnalysisQuestionMode.Targeted,
                    [anchor]),
                new AnalysisReportSurfaceSupport(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    [reportDomain]),
            ],
            [],
            [],
            [],
            [
                new AnalysisProjectionSupport(
                    rows,
                    [AnalysisQuestionMode.Targeted]),
            ]));
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedSurfaceBeforeProducerExecution()
    {
        Assert.Equal(
            AnalysisRequestRejectionReason.UnsupportedSurface,
            UnsupportedSurface());
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedTargetRoleBeforeProducerExecution()
    {
        Assert.Equal(
            AnalysisRequestRejectionReason.UnsupportedTargetRole,
            UnsupportedTargetRole());
    }

    [Fact]
    public void AnalysisCapability_RejectsMissingStructuralPrerequisiteBeforeProducerExecution()
    {
        Fixture fixture = new();
        var lookalikeQuery = new InspectionQuery<int>(
            fixture.ProducerQuery.Name,
            fixture.ProducerQuery.Cost);
        IInspectionQueryCatalog lookalikeCatalog =
            new InspectionQueryRegistry<object>()
                .Add(lookalikeQuery, static _ => 0)
                .Compile();

        AnalysisRequestRejection rejection = fixture.Rejected(
            fixture.Request(),
            new AnalysisPlanningEnvironment(
                lookalikeCatalog,
                [
                    fixture.ProducerPrerequisite,
                ]));

        Assert.Equal(
            AnalysisRequestRejectionReason.MissingStructuralPrerequisite,
            rejection.Reason);
        Assert.Equal([fixture.QueryPrerequisite], rejection.StructuralPrerequisites);

        AnalysisRequestRejection producerRejection = fixture.Rejected(
            fixture.Request(),
            new AnalysisPlanningEnvironment(fixture.QueryCatalog));
        Assert.Equal(
            AnalysisRequestRejectionReason.MissingStructuralPrerequisite,
            producerRejection.Reason);
        Assert.Equal(
            [fixture.ProducerPrerequisite],
            producerRejection.StructuralPrerequisites);
    }

    [Fact]
    public void AnalysisCapability_ModeScopesUniverseRequirementsAndProjections()
    {
        Fixture fixture = new(
            observedRequirementModes: [AnalysisQuestionMode.Targeted],
            graphProjectionModes: [AnalysisQuestionMode.Targeted]);
        AnalysisUniverseDescription censusUniverse = fixture.Universe(
            new UniverseIdentity("census"),
            new Boundary("selected types"),
            capabilities:
            [
                fixture.SelectedTypesCapability,
                fixture.OrderedParticipantsCapability,
            ]);

        AnalysisRequestPlan accepted = fixture.Accepted(
            fixture.Request(universe: censusUniverse));
        AnalysisRequestRejection rejected = fixture.Rejected(
            fixture.Request(
                universe: censusUniverse,
                projection: fixture.GraphProjection));

        Assert.DoesNotContain(
            fixture.ObservedEvidenceRequirement,
            accepted.UniverseRequirements);
        Assert.Equal(
            AnalysisRequestRejectionReason.UnsupportedProjection,
            rejected.Reason);
    }

    [Fact]
    public void AnalysisCapability_RejectsTargetRoleCardinalityMismatch()
    {
        Fixture fixture = new();
        var requiredDomain = new AnalysisTargetRoleDescriptor(
            new AnalysisDeclarationId("target.required-domain"),
            AnalysisTargetFunction.ReportDomain,
            1,
            1);
        AnalysisDescriptor analysis = fixture.WithWorkspaceRoles(
            fixture.WorkspaceDomain,
            requiredDomain);
        AnalysisReportSurface missing = new(
            AnalysisReportSurfaceKind.Workspace,
            new SurfaceIdentity("workspace"),
            [
                new AnalysisTargetBinding(
                    fixture.WorkspaceDomain,
                    new TargetIdentity("workspace")),
            ]);
        AnalysisReportSurface duplicate = new(
            AnalysisReportSurfaceKind.Workspace,
            new SurfaceIdentity("workspace"),
            [
                new AnalysisTargetBinding(
                    fixture.WorkspaceDomain,
                    new TargetIdentity("workspace-1")),
                new AnalysisTargetBinding(
                    fixture.WorkspaceDomain,
                    new TargetIdentity("workspace-2")),
                new AnalysisTargetBinding(
                    requiredDomain,
                    new TargetIdentity("required")),
            ]);
        var catalog = new AnalysisCapabilityCatalog([analysis]);

        AnalysisRequestRejection missingRejection = Assert.IsType<
            AnalysisRequestPlanResult.Rejected>(
                catalog.Plan(
                    fixture.Request(
                        analysis: analysis,
                        surface: missing),
                    fixture.Environment)).Rejection;
        AnalysisRequestRejection duplicateRejection = Assert.IsType<
            AnalysisRequestPlanResult.Rejected>(
                catalog.Plan(
                    fixture.Request(
                        analysis: analysis,
                        surface: duplicate),
                    fixture.Environment)).Rejection;

        Assert.Equal([requiredDomain], missingRejection.TargetRoles);
        Assert.Equal(
            [fixture.WorkspaceDomain],
            duplicateRejection.TargetRoles);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedProjectionBeforeProducerExecution()
    {
        Assert.Equal(
            AnalysisRequestRejectionReason.UnsupportedProjection,
            UnsupportedProjection());
    }

    [Fact]
    public void AnalysisCapability_AllDeclaredRejectionsPrecedeProducerExecution()
    {
        var seen = new HashSet<AnalysisRequestRejectionReason>
        {
            InvalidRequest(),
            UnsupportedMode(),
            UnsupportedSurface(),
            UnsupportedTargetRole(),
            InvalidMode(),
            MissingUniverse(),
            UnboundedUniverse(),
            UnsatisfiedUniverse(),
            MissingStructuralPrerequisite(),
            UnsupportedProjection(),
        };

        Assert.Equal(
            Enum.GetValues<AnalysisRequestRejectionReason>().Order(),
            seen.Order());
    }

    [Fact]
    public void AnalysisCapability_RejectionDoesNotUseFindingInspectionState()
    {
        Assert.DoesNotContain(
            Enum.GetNames<AnalysisRequestRejectionReason>(),
            name => name is "Complete" or "Absent" or "Failed" or "Missing");
    }

    [Fact]
    public void AnalysisPlan_RetainsExactRequestFieldsAndDescriptorRequirements()
    {
        Fixture fixture = new();
        AnalysisReportSurface surface = fixture.WorkspaceSurface();
        AnalysisRequest request = fixture.Request(surface: surface);
        AnalysisRequestPlan plan = fixture.Accepted(request);

        Assert.Same(request, plan.Request);
        Assert.Same(fixture.Analysis, plan.Analysis);
        Assert.Same(surface, plan.ReportSurface);
        Assert.Same(fixture.HealthyUniverse, plan.Universe);
        Assert.Equal(AnalysisQuestionMode.Census, plan.Mode);
        Assert.Same(fixture.RowsProjection, plan.Projection);
        Assert.Equal(
            fixture.Analysis.UniverseRequirements,
            plan.UniverseRequirements);
        Assert.Equal(
            fixture.Analysis.StructuralPrerequisites,
            plan.StructuralPrerequisites);
        Assert.Equal(fixture.Analysis.HostRequirements, plan.HostRequirements);
        Assert.Equal(InspectionCost.Unbounded, plan.Cost);
        Assert.Equal(0, fixture.ProducerExecutions);
    }

    [Fact]
    public void AnalysisPlan_CostIsMaximumOfAnalysisAndTransitiveQueries()
    {
        Fixture queryDominated = new();
        Fixture analysisDominated = new(
            analysisCost: InspectionCost.Unbounded,
            queryDependencyCost: InspectionCost.NetworkFree);

        AnalysisRequestPlan queryPlan = queryDominated.Accepted(
            queryDominated.Request());
        AnalysisRequestPlan analysisPlan = analysisDominated.Accepted(
            analysisDominated.Request());

        Assert.Equal(InspectionCost.Unbounded, queryPlan.Cost);
        Assert.Equal(InspectionCost.Unbounded, analysisPlan.Cost);
    }

    [Fact]
    public void AnalysisPlan_RetainsUniverseCompletenessAndFailureInputs()
    {
        Fixture fixture = new();
        Completeness completeness = new("partial");
        UniverseFailure failure = new("one participant rejected");
        AnalysisUniverseDescription universe = fixture.Universe(
            new UniverseIdentity("partial"),
            new Boundary("selected types"),
            completeness: completeness,
            failures: [failure]);

        AnalysisRequestPlan plan = fixture.Accepted(
            fixture.Request(universe: universe));

        Assert.Same(completeness, plan.UniverseCompleteness);
        Assert.Equal([failure], plan.UniverseFailures);
    }

    [Fact]
    public void AnalysisProjection_RowsAndGraphRetainOneAnalysisIdentity()
    {
        Fixture fixture = new();
        AnalysisRequestPlan rows = fixture.Accepted(
            fixture.Request(projection: fixture.RowsProjection));
        AnalysisRequestPlan graph = fixture.Accepted(
            fixture.Request(projection: fixture.GraphProjection));

        Assert.Same(rows.Analysis, graph.Analysis);
        Assert.Equal(rows.Analysis.Revision, graph.Analysis.Revision);
        Assert.NotSame(rows.Projection, graph.Projection);
    }

    [Fact]
    public void AnalysisUniverseProviderKindDoesNotChangeRequestFieldSemantics()
    {
        Fixture fixture = new();
        AnalysisUniverseDescription packageUniverse = fixture.Universe(
            new PackageUniverseIdentity(),
            new Boundary("two packages"));
        AnalysisUniverseDescription projectUniverse = fixture.Universe(
            new ProjectUniverseIdentity(),
            new Boundary("one project graph"));
        AnalysisReportSurface surface = fixture.WorkspaceSurface();

        AnalysisRequestPlan package = fixture.Accepted(
            fixture.Request(surface: surface, universe: packageUniverse));
        AnalysisRequestPlan project = fixture.Accepted(
            fixture.Request(surface: surface, universe: projectUniverse));

        Assert.Same(package.Analysis, project.Analysis);
        Assert.Same(surface, package.ReportSurface);
        Assert.Same(surface, project.ReportSurface);
        Assert.Equal(package.Mode, project.Mode);
        Assert.Same(package.Projection, project.Projection);
    }

    static AnalysisRequestRejectionReason InvalidRequest()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request() with
            {
                Analysis = null,
            }).Reason;
    }

    static AnalysisRequestRejectionReason UnsupportedMode()
    {
        Fixture fixture = new(supportedModes: [AnalysisQuestionMode.Census]);
        return fixture.Rejected(
            fixture.Request(mode: AnalysisQuestionMode.Targeted)).Reason;
    }

    static AnalysisRequestRejectionReason UnsupportedSurface()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request(
                surface: new AnalysisReportSurface(
                    AnalysisReportSurfaceKind.Root,
                    new SurfaceIdentity("root"),
                    []))).Reason;
    }

    static AnalysisRequestRejectionReason UnsupportedTargetRole()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request(
                surface: new AnalysisReportSurface(
                    AnalysisReportSurfaceKind.Workspace,
                    new SurfaceIdentity("workspace"),
                    [
                        new AnalysisTargetBinding(
                            new AnalysisTargetRoleDescriptor(
                                fixture.WorkspaceDomain.Id,
                                AnalysisTargetFunction.ReportDomain,
                                1,
                                1),
                            new TargetIdentity("workspace")),
                    ]))).Reason;
    }

    static AnalysisRequestRejectionReason InvalidMode()
    {
        Fixture fixture = new(allowCensusAnchor: true);
        return fixture.Rejected(
            fixture.Request(
                surface: new AnalysisReportSurface(
                    AnalysisReportSurfaceKind.Workspace,
                    new SurfaceIdentity("workspace"),
                    [
                        new AnalysisTargetBinding(
                            fixture.WorkspaceDomain,
                            new TargetIdentity("workspace")),
                        new AnalysisTargetBinding(
                            fixture.MemberAnchor,
                            new TargetIdentity("member")),
                    ]))).Reason;
    }

    static AnalysisRequestRejectionReason MissingUniverse()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request() with
            {
                Universe = null,
            }).Reason;
    }

    static AnalysisRequestRejectionReason UnboundedUniverse()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request(
                universe: fixture.Universe(
                    new UniverseIdentity("unbounded"),
                    new Boundary("unbounded"),
                    isFinite: false))).Reason;
    }

    static AnalysisRequestRejectionReason UnsatisfiedUniverse()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request(
                universe: fixture.Universe(
                    new UniverseIdentity("empty"),
                    new Boundary("empty"),
                    capabilities: []))).Reason;
    }

    static AnalysisRequestRejectionReason MissingStructuralPrerequisite()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request(),
            new AnalysisPlanningEnvironment()).Reason;
    }

    static AnalysisRequestRejectionReason UnsupportedProjection()
    {
        Fixture fixture = new();
        return fixture.Rejected(
            fixture.Request(
                projection: new AnalysisProjectionDescriptor(
                    fixture.RowsProjection.Id))).Reason;
    }

    sealed class Fixture
    {
        public static readonly AnalysisDeclarationId IntegrationAnalysisId =
            new("analysis.integrations");

        public Fixture(
            bool allowCensusAnchor = false,
            IEnumerable<AnalysisQuestionMode>? supportedModes = null,
            AnalysisTargetFunction memberTargetFunction =
                AnalysisTargetFunction.PrivilegedAnchor,
            IEnumerable<AnalysisQuestionMode>? observedRequirementModes = null,
            IEnumerable<AnalysisQuestionMode>? graphProjectionModes = null,
            InspectionCost analysisCost = InspectionCost.NetworkFree,
            InspectionCost queryDependencyCost = InspectionCost.Unbounded)
        {
            AnalysisQuestionMode[] modes =
                supportedModes?.ToArray()
                ?? [AnalysisQuestionMode.Targeted, AnalysisQuestionMode.Census];
            LoggingConcept = new AffectedIdentity("integration.logging");
            OpenTelemetryConcept =
                new AffectedIdentity("integration.opentelemetry");
            SelectedTypesCapability = Capability("universe.selected-types");
            OrderedParticipantsCapability =
                Capability("universe.ordered-participants");
            ObservedEvidenceCapability =
                Capability("integration.observed-evidence");
            SelectedTypesRequirement = Requirement(
                "requirement.selected-types",
                SelectedTypesCapability,
                modes,
                LoggingConcept,
                OpenTelemetryConcept);
            OrderedParticipantsRequirement = Requirement(
                "requirement.ordered-participants",
                OrderedParticipantsCapability,
                modes,
                LoggingConcept,
                OpenTelemetryConcept);
            ObservedEvidenceRequirement = Requirement(
                "requirement.integration-observed",
                ObservedEvidenceCapability,
                observedRequirementModes ?? modes,
                LoggingConcept,
                OpenTelemetryConcept);
            WorkspaceDomain = new AnalysisTargetRoleDescriptor(
                new AnalysisDeclarationId("target.workspace-domain"),
                AnalysisTargetFunction.ReportDomain,
                1,
                1);
            MemberAnchor = new AnalysisTargetRoleDescriptor(
                new AnalysisDeclarationId("target.member"),
                memberTargetFunction,
                allowCensusAnchor ? 0 : 1,
                1);
            FallbackAnchor = new AnalysisTargetRoleDescriptor(
                new AnalysisDeclarationId("target.fallback"),
                AnalysisTargetFunction.PrivilegedAnchor,
                0,
                1);
            RowsProjection = Projection("projection.rows");
            GraphProjection = Projection("projection.graph");
            ProducerQuery = new InspectionQuery<int>(
                "Integration producer",
                InspectionCost.NetworkFree);
            ProducerPrerequisite = new AnalysisProducerPrerequisiteDescriptor(
                new AnalysisDeclarationId("producer.integrations"));
            QueryPrerequisite = new AnalysisQueryPrerequisiteDescriptor(
                new AnalysisDeclarationId("query.integrations"),
                ProducerQuery);
            var surfaces = new List<AnalysisReportSurfaceSupport>
            {
            };
            if (modes.Contains(AnalysisQuestionMode.Census))
            {
                surfaces.Add(new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    allowCensusAnchor
                        ? [WorkspaceDomain, MemberAnchor]
                        : [WorkspaceDomain]));
            }
            if (modes.Contains(AnalysisQuestionMode.Targeted))
            {
                surfaces.Add(
                    new AnalysisReportSurfaceSupport(
                        AnalysisReportSurfaceKind.Member,
                        AnalysisQuestionMode.Targeted,
                        [MemberAnchor, FallbackAnchor]));
            }

            Analysis = new AnalysisDescriptor(
                IntegrationAnalysisId,
                revision: 1,
                analysisCost,
                modes,
                surfaces,
                [
                    SelectedTypesRequirement,
                    OrderedParticipantsRequirement,
                    ObservedEvidenceRequirement,
                ],
                [ProducerPrerequisite, QueryPrerequisite],
                [
                    new AnalysisHostRequirementDescriptor(
                        new AnalysisDeclarationId("host.explicit-analysis")),
                ],
                [
                    new AnalysisProjectionSupport(RowsProjection, modes),
                    new AnalysisProjectionSupport(
                        GraphProjection,
                        graphProjectionModes ?? modes),
                ]);
            Catalog = new AnalysisCapabilityCatalog([Analysis]);
            HealthyUniverse = Universe(
                new UniverseIdentity("workspace-types"),
                new Boundary("three selected types"));
            var expensiveDependency = new InspectionQuery<int>(
                "Integration evidence",
                queryDependencyCost);
            QueryCatalog = new InspectionQueryRegistry<object>()
                .Add(expensiveDependency, static _ => 0)
                .Add(
                    ProducerQuery,
                    _ =>
                    {
                        ProducerExecutions++;
                        return 0;
                    },
                    expensiveDependency)
                .Compile();
            Environment = new AnalysisPlanningEnvironment(
                QueryCatalog,
                [ProducerPrerequisite]);
        }

        public AffectedIdentity LoggingConcept { get; }
        public AffectedIdentity OpenTelemetryConcept { get; }
        public AnalysisUniverseCapabilityDescriptor SelectedTypesCapability { get; }
        public AnalysisUniverseCapabilityDescriptor OrderedParticipantsCapability { get; }
        public AnalysisUniverseCapabilityDescriptor ObservedEvidenceCapability { get; }
        public AnalysisUniverseRequirementDescriptor SelectedTypesRequirement { get; }
        public AnalysisUniverseRequirementDescriptor OrderedParticipantsRequirement { get; }
        public AnalysisUniverseRequirementDescriptor ObservedEvidenceRequirement { get; }
        public AnalysisTargetRoleDescriptor WorkspaceDomain { get; }
        public AnalysisTargetRoleDescriptor MemberAnchor { get; }
        public AnalysisTargetRoleDescriptor FallbackAnchor { get; }
        public AnalysisProjectionDescriptor RowsProjection { get; }
        public AnalysisProjectionDescriptor GraphProjection { get; }
        public InspectionQuery<int> ProducerQuery { get; }
        public AnalysisProducerPrerequisiteDescriptor ProducerPrerequisite { get; }
        public AnalysisQueryPrerequisiteDescriptor QueryPrerequisite { get; }
        public AnalysisDescriptor Analysis { get; }
        public AnalysisCapabilityCatalog Catalog { get; }
        public AnalysisUniverseDescription HealthyUniverse { get; }
        public AnalysisPlanningEnvironment Environment { get; }
        public InspectionQueryCatalog<object> QueryCatalog { get; }
        public int ProducerExecutions { get; private set; }

        public AnalysisReportSurface WorkspaceSurface() =>
            new(
                AnalysisReportSurfaceKind.Workspace,
                new SurfaceIdentity("workspace"),
                [
                    new AnalysisTargetBinding(
                        WorkspaceDomain,
                        new TargetIdentity("workspace")),
                ]);

        public AnalysisReportSurface MemberSurface() =>
            new(
                AnalysisReportSurfaceKind.Member,
                new SurfaceIdentity("member"),
                [
                    new AnalysisTargetBinding(
                        MemberAnchor,
                        new TargetIdentity("member")),
                ]);

        public AnalysisDescriptor WithWorkspaceRoles(
            params AnalysisTargetRoleDescriptor[] roles) =>
            new(
                Analysis.Id,
                Analysis.Revision,
                Analysis.Cost,
                Analysis.Modes,
                [
                    new AnalysisReportSurfaceSupport(
                        AnalysisReportSurfaceKind.Workspace,
                        AnalysisQuestionMode.Census,
                        roles),
                    new AnalysisReportSurfaceSupport(
                        AnalysisReportSurfaceKind.Member,
                        AnalysisQuestionMode.Targeted,
                        [MemberAnchor, FallbackAnchor]),
                ],
                Analysis.UniverseRequirements,
                Analysis.StructuralPrerequisites,
                Analysis.HostRequirements,
                Analysis.Projections);

        public AnalysisRequest Request(
            AnalysisDescriptor? analysis = default,
            AnalysisReportSurface? surface = default,
            AnalysisUniverseDescription? universe = default,
            AnalysisQuestionMode mode = AnalysisQuestionMode.Census,
            AnalysisProjectionDescriptor? projection = default) =>
            new(
                analysis ?? Analysis,
                surface ?? WorkspaceSurface(),
                universe ?? HealthyUniverse,
                mode,
                projection ?? RowsProjection);

        public AnalysisUniverseDescription Universe(
            IAnalysisUniverseIdentity identity,
            IAnalysisUniverseBoundary boundary,
            bool isFinite = true,
            IEnumerable<AnalysisUniverseCapabilityDescriptor>? capabilities = null,
            IAnalysisUniverseCompleteness? completeness = null,
            IEnumerable<IAnalysisUniverseFailure>? failures = null) =>
            new(
                identity,
                boundary,
                boundary,
                isFinite,
                capabilities
                    ?? [
                        SelectedTypesCapability,
                        OrderedParticipantsCapability,
                        ObservedEvidenceCapability,
                    ],
                completeness ?? new Completeness("complete"),
                failures);

        public AnalysisRequestPlan Accepted(
            AnalysisRequest request,
            AnalysisPlanningEnvironment? environment = null)
        {
            AnalysisRequestPlan plan =
                Assert.IsType<AnalysisRequestPlanResult.Accepted>(
                    Catalog.Plan(request, environment ?? Environment)).Plan;
            Assert.Equal(0, ProducerExecutions);
            return plan;
        }

        public AnalysisRequestRejection Rejected(
            AnalysisRequest request,
            AnalysisPlanningEnvironment? environment = null)
        {
            AnalysisRequestRejection rejection =
                Assert.IsType<AnalysisRequestPlanResult.Rejected>(
                    Catalog.Plan(request, environment ?? Environment)).Rejection;
            Assert.Equal(0, ProducerExecutions);
            return rejection;
        }

        static AnalysisUniverseCapabilityDescriptor Capability(string id) =>
            new(new AnalysisDeclarationId(id), id);

        static AnalysisUniverseRequirementDescriptor Requirement(
            string id,
            AnalysisUniverseCapabilityDescriptor capability,
            IEnumerable<AnalysisQuestionMode> modes,
            params IAnalysisRequirementAffectedIdentity[] affected) =>
            new(
                new AnalysisDeclarationId(id),
                capability,
                modes,
                affected);

        static AnalysisProjectionDescriptor Projection(string id) =>
            new(new AnalysisDeclarationId(id));

    }

    sealed record SurfaceIdentity(string Value)
        : IAnalysisReportSurfaceIdentity;

    sealed record TargetIdentity(string Value)
        : IAnalysisTargetIdentity;

    sealed record UniverseIdentity(string Value)
        : IAnalysisUniverseIdentity;

    sealed record PackageUniverseIdentity : IAnalysisUniverseIdentity;

    sealed record ProjectUniverseIdentity : IAnalysisUniverseIdentity;

    sealed record Boundary(string Value)
        : IAnalysisUniverseBoundary;

    sealed record Completeness(string Value)
        : IAnalysisUniverseCompleteness;

    sealed record UniverseFailure(string Value)
        : IAnalysisUniverseFailure;

    sealed record AffectedIdentity(string Value)
        : IAnalysisRequirementAffectedIdentity;
}
