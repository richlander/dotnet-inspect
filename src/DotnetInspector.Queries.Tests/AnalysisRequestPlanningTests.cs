using System.Collections.Immutable;
using System.Reflection;
using DotnetInspector.Queries;

namespace DotnetInspector.Queries.Tests;

public sealed class AnalysisRequestPlanningTests
{
    [Fact]
    public void AnalysisCapability_ListsConfiguredUnobservedIntegrationDescriptors()
    {
        Scenario scenario = new();
        var second = new AnalysisDescriptor(
            "Dependency census",
            "v1",
            [AnalysisQuestionMode.Census],
            [
                new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    scenario.DomainRole,
                    AnalysisTargetFunction.ReportDomain),
            ],
            [scenario.RowsProjection]);

        var planner = new AnalysisRequestPlanner([scenario.Analysis, second], [scenario.Prerequisite]);

        Assert.Equal([scenario.Analysis, second], planner.Descriptors);
        var integration =
            Assert.IsType<IntegrationAnalysisDescriptor>(planner.Descriptors[0]);
        Assert.Equal(scenario.Concepts, integration.Concepts);
    }

    [Fact]
    public void AnalysisPlan_RetainsExactRequestFieldsAndDescriptorRequirements()
    {
        Scenario scenario = new();
        AnalysisReportSurface<TargetIdentity> surface = scenario.ValidTargetedSurface();
        AnalysisUniverseDescription<UniverseBoundary, UniverseState> universe =
            scenario.ValidUniverse();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            surface,
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var validated = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(request));

        Assert.Same(scenario.Analysis, validated.Plan.Analysis);
        Assert.Same(surface, validated.Plan.ReportSurface);
        Assert.Same(universe, validated.Plan.Universe);
        Assert.Equal(AnalysisQuestionMode.Targeted, validated.Plan.Mode);
        Assert.Same(scenario.RowsProjection, validated.Plan.Projection);
        Assert.Equal(scenario.Analysis.UniverseRequirements, validated.Plan.UniverseRequirements);
        Assert.Equal(
            scenario.Analysis.StructuralPrerequisites,
            validated.Plan.StructuralPrerequisites);
        Assert.Equal(scenario.Analysis.PreflightRequirements, validated.Plan.PreflightRequirements);
        Assert.Same(universe.RequestedBoundary, validated.Plan.Universe.RequestedBoundary);
        Assert.Same(universe.RealizedBoundary, validated.Plan.Universe.RealizedBoundary);
    }

    [Fact]
    public void AnalysisPlan_RetainsUniverseCompletenessAndFailureInputs()
    {
        Scenario scenario = new();
        UniverseState state = new("Partial", ["package-a failed"]);
        var universe = new AnalysisUniverseDescription<UniverseBoundary, UniverseState>(
            AnalysisUniverseBoundKind.Finite,
            new("requested"),
            new("realized"),
            [scenario.SubjectCapability, scenario.EvidenceCapability],
            state);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var validated = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(request));

        Assert.Same(universe.ProviderState, validated.Plan.Universe.ProviderState);
        Assert.Equal("Partial", validated.Plan.Universe.ProviderState.Completeness);
        Assert.Equal(["package-a failed"], validated.Plan.Universe.ProviderState.Failures);
    }

    [Fact]
    public void Plan_ValidatesIntegrationShapedWorkspaceCensus()
    {
        Scenario scenario = new();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidCensusSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Census,
            scenario.MatrixProjection);

        var validated = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(request));

        Assert.Equal(AnalysisReportSurfaceKind.Workspace, validated.Plan.ReportSurface.Kind);
        Assert.Equal(AnalysisQuestionMode.Census, validated.Plan.Mode);
        Assert.Same(scenario.MatrixProjection, validated.Plan.Projection);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedModeBeforeProducerExecution()
    {
        Scenario scenario = new();
        AnalysisDescriptor censusOnly = scenario.CreateAnalysis(
            supportedModes: [AnalysisQuestionMode.Census],
            targetRoles:
            [
                new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    scenario.DomainRole,
                    AnalysisTargetFunction.ReportDomain),
            ]);
        var planner = new AnalysisRequestPlanner([censusOnly], [scenario.Prerequisite]);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            censusOnly,
            scenario.Surface(
                AnalysisReportSurfaceKind.Workspace,
                scenario.DomainRole,
                "workspace"),
            universe: null,
            AnalysisQuestionMode.Targeted,
            scenario.UnsupportedProjection);

        AnalysisRequestRejection rejection = Reject(planner.Plan(request));

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedMode>(rejection);
        Assert.Equal(AnalysisQuestionMode.Targeted, unsupported.Mode);
        Assert.NotEmpty(unsupported.Guidance);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedSurfaceBeforeProducerExecution()
    {
        Scenario scenario = new();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.Surface(AnalysisReportSurfaceKind.Type, scenario.AnchorRole, "type"),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedSurface>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(AnalysisReportSurfaceKind.Type, unsupported.SurfaceKind);
        Assert.NotEmpty(unsupported.Guidance);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedTargetRoleBeforeProducerExecution()
    {
        Scenario scenario = new();
        var sameNamedRole = new AnalysisTargetRoleDescriptor(scenario.AnchorRole.Name);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.Surface(AnalysisReportSurfaceKind.Library, sameNamedRole, "library"),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedTargetRole>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Same(sameNamedRole, unsupported.Role);
        Assert.NotEmpty(unsupported.Guidance);
    }

    [Fact]
    public void AnalysisRequest_TargetedRequiresAcceptedAnchor()
    {
        Scenario scenario = new();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.Surface(
                AnalysisReportSurfaceKind.Library,
                scenario.TargetedDomainRole,
                "library"),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var invalid = Assert.IsType<AnalysisRequestRejection.InvalidMode>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(AnalysisModeViolation.TargetedMissingPrivilegedAnchor, invalid.Violation);
        Assert.NotEmpty(invalid.Guidance);
    }

    [Fact]
    public void AnalysisRequest_CensusRejectsPrivilegedContainedAnchor()
    {
        Scenario scenario = new();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.Surface(
                AnalysisReportSurfaceKind.Workspace,
                scenario.CensusAnchorRole,
                "workspace"),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Census,
            scenario.RowsProjection);

        var invalid = Assert.IsType<AnalysisRequestRejection.InvalidMode>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(AnalysisModeViolation.CensusContainsPrivilegedAnchor, invalid.Violation);
        Assert.NotEmpty(invalid.Guidance);
    }

    [Fact]
    public void AnalysisRequest_RejectsMissingOrUnboundedUniverseBeforeProducerExecution()
    {
        Scenario scenario = new();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            universe: null,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        AnalysisRequestRejection rejection = Reject(scenario.Planner.Plan(request));

        Assert.IsType<AnalysisRequestRejection.MissingUniverse>(rejection);
        Assert.NotEmpty(rejection.Guidance);

        var unboundedRequest = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            scenario.Universe(
                AnalysisUniverseBoundKind.Unbounded,
                [scenario.SubjectCapability, scenario.EvidenceCapability]),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        AnalysisRequestRejection unboundedRejection =
            Reject(scenario.Planner.Plan(unboundedRequest));

        Assert.IsType<AnalysisRequestRejection.UnboundedUniverse>(unboundedRejection);
        Assert.NotEmpty(unboundedRejection.Guidance);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsatisfiedUniverseBeforeProducerExecution()
    {
        Scenario scenario = new();
        var sameNamedSubjectCapability =
            new AnalysisUniverseCapabilityDescriptor(scenario.SubjectCapability.Name);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            scenario.Universe(AnalysisUniverseBoundKind.Finite, [sameNamedSubjectCapability]),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var unsatisfied = Assert.IsType<AnalysisRequestRejection.UnsatisfiedUniverse>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(
            [scenario.SubjectRequirement, scenario.EvidenceRequirement],
            unsatisfied.Requirements);
        Assert.NotEmpty(unsatisfied.Guidance);
    }

    [Fact]
    public void Plan_RetainsDistinctOwnerScopedRequirementsSharingOneCapability()
    {
        Scenario scenario = new();
        var registrations = new IntegrationConceptDescriptor("Registrations");
        var clientConstruction = new IntegrationConceptDescriptor("Client construction");
        var attributes = new IntegrationProducerPolicyDescriptor("Attribute policy");
        var calls = new IntegrationProducerPolicyDescriptor("Call policy");
        var attributeRequirement = new IntegrationUniverseRequirementDescriptor(
            "Attribute evidence",
            scenario.EvidenceCapability,
            attributes,
            [registrations, clientConstruction]);
        var callRequirement = new IntegrationUniverseRequirementDescriptor(
            "Call evidence",
            scenario.EvidenceCapability,
            calls,
            [clientConstruction]);
        AnalysisDescriptor analysis = scenario.CreateAnalysis(
            universeRequirements:
            [
                scenario.SubjectRequirement,
                attributeRequirement,
                callRequirement,
            ]);
        var planner = new AnalysisRequestPlanner([analysis], [scenario.Prerequisite]);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            analysis,
            scenario.ValidTargetedSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var plan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                planner.Plan(request)).Plan;

        Assert.Equal(
            [scenario.SubjectRequirement, attributeRequirement, callRequirement],
            plan.UniverseRequirements);
        AnalysisDescriptor discovered = Assert.Single(planner.Descriptors);
        var discoveredAttributeRequirement =
            Assert.IsType<IntegrationUniverseRequirementDescriptor>(
                discovered.UniverseRequirements[1]);
        Assert.Equal(
            [registrations, clientConstruction],
            discoveredAttributeRequirement.Concepts);
        Assert.Same(attributes, attributeRequirement.Policy);
        Assert.Equal([registrations, clientConstruction], attributeRequirement.Concepts);
        Assert.Same(calls, callRequirement.Policy);
        Assert.Equal([clientConstruction], callRequirement.Concepts);

        var missingEvidenceRequest =
            new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
                analysis,
                scenario.ValidTargetedSurface(),
                scenario.Universe(
                    AnalysisUniverseBoundKind.Finite,
                    [scenario.SubjectCapability]),
                AnalysisQuestionMode.Targeted,
                scenario.RowsProjection);
        var unsatisfied = Assert.IsType<AnalysisRequestRejection.UnsatisfiedUniverse>(
            Reject(planner.Plan(missingEvidenceRequest)));
        Assert.Equal([attributeRequirement, callRequirement], unsatisfied.Requirements);
    }

    [Fact]
    public void AnalysisCapability_RejectsMissingStructuralPrerequisiteBeforeProducerExecution()
    {
        Scenario scenario = new();
        var planner = new AnalysisRequestPlanner([scenario.Analysis], []);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var missing = Assert.IsType<AnalysisRequestRejection.MissingStructuralPrerequisites>(
            Reject(planner.Plan(request)));

        Assert.Equal([scenario.Prerequisite], missing.Prerequisites);
        Assert.NotEmpty(missing.Guidance);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedProjectionBeforeProducerExecution()
    {
        Scenario scenario = new();
        var sameNamedProjection =
            new AnalysisProjectionDescriptor(scenario.RowsProjection.Name);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            sameNamedProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedProjection>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Same(sameNamedProjection, unsupported.Projection);
        Assert.NotEmpty(unsupported.Guidance);
    }

    [Fact]
    public void AnalysisCapability_AllDeclaredRejectionsPrecedeProducerExecution()
    {
        Scenario scenario = new();
        var providerState = new PoisonProviderState();
        var universe = new AnalysisUniverseDescription<UniverseBoundary, PoisonProviderState>(
            AnalysisUniverseBoundKind.Finite,
            new("requested"),
            new("realized"),
            [scenario.SubjectCapability, scenario.EvidenceCapability],
            providerState);
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, PoisonProviderState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var validated = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, PoisonProviderState>
                .Validated>(scenario.Planner.Plan(request));

        Assert.Same(providerState, validated.Plan.Universe.ProviderState);
        Assert.Equal(0, providerState.ExecutionCount);
        Assert.DoesNotContain(
            typeof(Delegate),
            typeof(AnalysisRequestPlanner)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(GetMemberTypes));
    }

    [Fact]
    public void AnalysisRequest_ReportSurfaceAndUniverseAreIndependent()
    {
        Scenario scenario = new();
        AnalysisReportSurface<TargetIdentity> targetedSurface = scenario.ValidTargetedSurface();
        AnalysisReportSurface<TargetIdentity> censusSurface = scenario.ValidCensusSurface();
        AnalysisUniverseDescription<UniverseBoundary, UniverseState> universe =
            scenario.ValidUniverse();

        var targeted = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            targetedSurface,
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);
        var census = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            censusSurface,
            universe,
            AnalysisQuestionMode.Census,
            scenario.MatrixProjection);

        var targetedPlan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(targeted)).Plan;
        var censusPlan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(census)).Plan;

        Assert.Same(targetedSurface, targetedPlan.ReportSurface);
        Assert.Same(censusSurface, censusPlan.ReportSurface);
        Assert.Same(scenario.RowsProjection, targetedPlan.Projection);
        Assert.Same(scenario.MatrixProjection, censusPlan.Projection);
        Assert.NotEqual(targetedPlan.Mode, censusPlan.Mode);
    }

    [Fact]
    public void DescriptorAndCatalog_FreezeDeclarationCollections()
    {
        Scenario scenario = new();
        AnalysisQuestionMode[] modes = [AnalysisQuestionMode.Targeted];
        AnalysisTargetRoleDeclaration[] roles =
        [
            new(
                AnalysisReportSurfaceKind.Library,
                AnalysisQuestionMode.Targeted,
                scenario.AnchorRole,
                AnalysisTargetFunction.PrivilegedAnchor),
        ];
        AnalysisProjectionDescriptor[] projections = [scenario.RowsProjection];
        var descriptor = new AnalysisDescriptor(
            "Frozen",
            "v1",
            modes,
            roles,
            projections);
        AnalysisDescriptor[] descriptors = [descriptor];
        var planner = new AnalysisRequestPlanner(descriptors, []);

        modes[0] = AnalysisQuestionMode.Census;
        roles[0] = new(
            AnalysisReportSurfaceKind.Workspace,
            AnalysisQuestionMode.Census,
            scenario.DomainRole,
            AnalysisTargetFunction.ReportDomain);
        projections[0] = scenario.MatrixProjection;
        descriptors[0] = scenario.Analysis;

        Assert.Equal([AnalysisQuestionMode.Targeted], descriptor.SupportedModes);
        Assert.Same(scenario.AnchorRole, Assert.Single(descriptor.TargetRoles).Role);
        Assert.Same(scenario.RowsProjection, Assert.Single(descriptor.SupportedProjections));
        Assert.Same(descriptor, Assert.Single(planner.Descriptors));
    }

    [Fact]
    public void AnalysisRequest_DeclaresCompleteClosedFieldSet()
    {
        Assert.Equal(
            ["Analysis", "Mode", "Projection", "ReportSurface", "Universe"],
            typeof(AnalysisRequest<,,>)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
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
            [AnalysisQuestionMode.Targeted, AnalysisQuestionMode.Census],
            Enum.GetValues<AnalysisQuestionMode>());

        Type[] rejectionFamilies = typeof(AnalysisRequestRejection)
            .GetNestedTypes(BindingFlags.Public)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                typeof(AnalysisRequestRejection.InvalidMode),
                typeof(AnalysisRequestRejection.MissingStructuralPrerequisites),
                typeof(AnalysisRequestRejection.MissingUniverse),
                typeof(AnalysisRequestRejection.UnboundedUniverse),
                typeof(AnalysisRequestRejection.UnsatisfiedUniverse),
                typeof(AnalysisRequestRejection.UnsupportedMode),
                typeof(AnalysisRequestRejection.UnsupportedProjection),
                typeof(AnalysisRequestRejection.UnsupportedSurface),
                typeof(AnalysisRequestRejection.UnsupportedTargetRole),
            ],
            rejectionFamilies);
    }

    [Fact]
    public void AnalysisRequest_MemberReportMayConsumeWorkspaceUniverse()
    {
        Scenario scenario = new();
        AnalysisReportSurface<TargetIdentity> memberSurface = scenario.Surface(
            AnalysisReportSurfaceKind.Member,
            scenario.AnchorRole,
            "method");
        AnalysisUniverseDescription<UniverseBoundary, UniverseState> workspaceUniverse =
            scenario.Universe(
                AnalysisUniverseBoundKind.Finite,
                [scenario.SubjectCapability, scenario.EvidenceCapability],
                providerKind: "Workspace");
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            memberSurface,
            workspaceUniverse,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var plan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(request)).Plan;

        Assert.Equal(AnalysisReportSurfaceKind.Member, plan.ReportSurface.Kind);
        Assert.Equal("Workspace", plan.Universe.ProviderState.ProviderKind);
    }

    [Fact]
    public void AnalysisRequest_UniverseBreadthCannotWidenReportSurface()
    {
        Scenario scenario = new();
        AnalysisReportSurface<TargetIdentity> memberSurface = scenario.Surface(
            AnalysisReportSurfaceKind.Member,
            scenario.AnchorRole,
            "method");
        AnalysisUniverseDescription<UniverseBoundary, UniverseState> broadUniverse =
            scenario.Universe(
                AnalysisUniverseBoundKind.Finite,
                [scenario.SubjectCapability, scenario.EvidenceCapability],
                requested: "entire-workspace",
                realized: "entire-workspace");
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            memberSurface,
            broadUniverse,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var plan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(request)).Plan;

        Assert.Same(memberSurface, plan.ReportSurface);
        Assert.Equal(AnalysisReportSurfaceKind.Member, plan.ReportSurface.Kind);
        Assert.Same(broadUniverse, plan.Universe);
    }

    [Fact]
    public void AnalysisRequest_ModeValidationDerivesFromDeclaredTargetFunctions()
    {
        Scenario scenario = new();
        var sharedRole = new AnalysisTargetRoleDescriptor("Shared role");
        AnalysisDescriptor analysis = scenario.CreateAnalysis(
            targetRoles:
            [
                new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Targeted,
                    sharedRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    sharedRole,
                    AnalysisTargetFunction.ReportDomain),
            ]);
        var planner = new AnalysisRequestPlanner([analysis], [scenario.Prerequisite]);
        AnalysisReportSurface<TargetIdentity> surface = scenario.Surface(
            AnalysisReportSurfaceKind.Workspace,
            sharedRole,
            "workspace");

        var targeted = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            analysis,
            surface,
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);
        var census = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            analysis,
            surface,
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Census,
            scenario.RowsProjection);

        Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                planner.Plan(targeted));
        Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                planner.Plan(census));
    }

    [Fact]
    public void AnalysisCapability_StructuralDiscoveryDoesNotResolveContentExecuteProducersOrProbeEffectiveness()
    {
        Scenario scenario = new();

        var planner = new AnalysisRequestPlanner([scenario.Analysis], [scenario.Prerequisite]);

        Assert.Same(scenario.Analysis, Assert.Single(planner.Descriptors));
        Assert.DoesNotContain(
            typeof(Delegate),
            typeof(AnalysisRequestPlanner)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(GetMemberTypes));
    }

    [Fact]
    public void AnalysisCapability_RejectionDoesNotUseFindingInspectionState()
    {
        Type[] contractTypes =
        [
            typeof(AnalysisRequestRejection),
            .. typeof(AnalysisRequestRejection).GetNestedTypes(BindingFlags.Public),
        ];

        string[] referencedTypeNames = contractTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.PropertyType.FullName ?? property.PropertyType.Name)
            .ToArray();

        Assert.DoesNotContain(
            referencedTypeNames,
            name => name.Contains("Finding", StringComparison.Ordinal)
                || name.Contains("InspectionState", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalysisProjection_RowsAndGraphRetainOneAnalysisIdentity()
    {
        Scenario scenario = new();
        var rowsRequest = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);
        var graphRequest = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidTargetedSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.GraphProjection);

        var rowsPlan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(rowsRequest)).Plan;
        var graphPlan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(graphRequest)).Plan;

        Assert.Same(scenario.Analysis, rowsPlan.Analysis);
        Assert.Same(rowsPlan.Analysis, graphPlan.Analysis);
        Assert.Same(scenario.RowsProjection, rowsPlan.Projection);
        Assert.Same(scenario.GraphProjection, graphPlan.Projection);
    }

    [Fact]
    public void AnalysisUniverseProviderKindDoesNotChangeRequestFieldSemantics()
    {
        Scenario scenario = new();
        var prefixRequest = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            scenario.Analysis,
            scenario.ValidCensusSurface(),
            scenario.Universe(
                AnalysisUniverseBoundKind.Finite,
                [scenario.SubjectCapability, scenario.EvidenceCapability],
                providerKind: "Package prefix"),
            AnalysisQuestionMode.Census,
            scenario.RowsProjection);
        var projectGraphRequest =
            new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
                scenario.Analysis,
                scenario.ValidCensusSurface(),
                scenario.Universe(
                    AnalysisUniverseBoundKind.Finite,
                    [scenario.SubjectCapability, scenario.EvidenceCapability],
                    providerKind: "Project graph"),
                AnalysisQuestionMode.Census,
                scenario.RowsProjection);

        var prefixPlan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(prefixRequest)).Plan;
        var graphPlan = Assert.IsType<
            AnalysisRequestPlanningResult<TargetIdentity, UniverseBoundary, UniverseState>.Validated>(
                scenario.Planner.Plan(projectGraphRequest)).Plan;

        Assert.Same(prefixPlan.Analysis, graphPlan.Analysis);
        Assert.Equal(prefixPlan.ReportSurface.Kind, graphPlan.ReportSurface.Kind);
        Assert.Equal(prefixPlan.Mode, graphPlan.Mode);
        Assert.Same(prefixPlan.Projection, graphPlan.Projection);
        Assert.NotEqual(
            prefixPlan.Universe.ProviderState.ProviderKind,
            graphPlan.Universe.ProviderState.ProviderKind);
    }

    [Fact]
    public void Descriptor_RejectsMalformedDeclarations()
    {
        Scenario scenario = new();

        Assert.Throws<ArgumentException>(() => new AnalysisDescriptor(
            "Malformed",
            "v1",
            [AnalysisQuestionMode.Targeted],
            [
                new(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Census,
                    scenario.DomainRole,
                    AnalysisTargetFunction.ReportDomain),
            ],
            [scenario.RowsProjection]));
        Assert.Throws<ArgumentException>(() => new AnalysisDescriptor(
            "Malformed",
            "v1",
            [AnalysisQuestionMode.Targeted],
            [
                new(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    scenario.AnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    scenario.AnchorRole,
                    AnalysisTargetFunction.ReportDomain),
            ],
            [scenario.RowsProjection]));
    }

    [Fact]
    public void Planner_RejectsDescriptorOutsideCatalog()
    {
        Scenario scenario = new();
        AnalysisDescriptor outside = scenario.CreateAnalysis();
        var request = new AnalysisRequest<TargetIdentity, UniverseBoundary, UniverseState>(
            outside,
            scenario.ValidTargetedSurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => scenario.Planner.Plan(request));

        Assert.Equal("request", exception.ParamName);
    }

    private static AnalysisRequestRejection Reject<TTargetIdentity, TUniverseBoundary, TUniverseState>(
        AnalysisRequestPlanningResult<TTargetIdentity, TUniverseBoundary, TUniverseState> result)
        => Assert.IsType<
            AnalysisRequestPlanningResult<TTargetIdentity, TUniverseBoundary, TUniverseState>
                .Rejected>(result).Rejection;

    private static IEnumerable<Type> GetMemberTypes(MemberInfo member)
        => member switch
        {
            MethodInfo method => method
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            PropertyInfo property => [property.PropertyType],
            _ => [],
        };

    private sealed class Scenario
    {
        public Scenario()
        {
            SubjectRequirement = new("Selected Types", SubjectCapability);
            EvidenceRequirement = new("Structured evidence", EvidenceCapability);
            Analysis = new IntegrationAnalysisDescriptor(
                "Integration census",
                "v1",
                [AnalysisQuestionMode.Targeted, AnalysisQuestionMode.Census],
                DefaultTargetRoles(),
                [RowsProjection, MatrixProjection, GraphProjection],
                [SubjectRequirement, EvidenceRequirement],
                [Prerequisite],
                [Preflight],
                Concepts);
            Planner = new([Analysis], [Prerequisite]);
        }

        public AnalysisTargetRoleDescriptor AnchorRole { get; } = new("Hub");

        public AnalysisTargetRoleDescriptor TargetedDomainRole { get; } = new("Library domain");

        public AnalysisTargetRoleDescriptor DomainRole { get; } = new("Workspace");

        public AnalysisTargetRoleDescriptor CensusAnchorRole { get; } = new("Census hub");

        public AnalysisUniverseCapabilityDescriptor SubjectCapability { get; } =
            new("Selected-Type membership");

        public AnalysisUniverseCapabilityDescriptor EvidenceCapability { get; } =
            new("Integration structured evidence");

        public AnalysisUniverseRequirementDescriptor SubjectRequirement { get; }

        public AnalysisUniverseRequirementDescriptor EvidenceRequirement { get; }

        public ImmutableArray<IntegrationConceptDescriptor> Concepts { get; } =
        [
            new("Registrations"),
            new("Client construction"),
            new("Pipeline hooks"),
        ];

        public AnalysisStructuralPrerequisiteDescriptor Prerequisite { get; } =
            new("Integration producer catalog");

        public AnalysisPreflightRequirementDescriptor Preflight { get; } =
            new("Expensive analysis authorization");

        public AnalysisProjectionDescriptor RowsProjection { get; } = new("Rows");

        public AnalysisProjectionDescriptor MatrixProjection { get; } = new("Matrix");

        public AnalysisProjectionDescriptor GraphProjection { get; } = new("Graph");

        public AnalysisProjectionDescriptor UnsupportedProjection { get; } = new("Tree");

        public AnalysisDescriptor Analysis { get; }

        public AnalysisRequestPlanner Planner { get; }

        public AnalysisDescriptor CreateAnalysis(
            IReadOnlyList<AnalysisQuestionMode>? supportedModes = null,
            IReadOnlyList<AnalysisTargetRoleDeclaration>? targetRoles = null,
            IReadOnlyList<AnalysisUniverseRequirementDescriptor>? universeRequirements = null)
            => new(
                "Integration census",
                "v1",
                supportedModes
                    ?? [AnalysisQuestionMode.Targeted, AnalysisQuestionMode.Census],
                targetRoles ?? DefaultTargetRoles(),
                [RowsProjection, MatrixProjection, GraphProjection],
                universeRequirements ?? [SubjectRequirement, EvidenceRequirement],
                [Prerequisite],
                [Preflight]);

        private ImmutableArray<AnalysisTargetRoleDeclaration> DefaultTargetRoles()
            =>
            [
                new(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    AnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new(
                    AnalysisReportSurfaceKind.Member,
                    AnalysisQuestionMode.Targeted,
                    AnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    TargetedDomainRole,
                    AnalysisTargetFunction.ReportDomain),
                new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    DomainRole,
                    AnalysisTargetFunction.ReportDomain),
                new(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    CensusAnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
            ];

        public AnalysisReportSurface<TargetIdentity> ValidTargetedSurface()
            => Surface(AnalysisReportSurfaceKind.Library, AnchorRole, "library");

        public AnalysisReportSurface<TargetIdentity> ValidCensusSurface()
            => Surface(AnalysisReportSurfaceKind.Workspace, DomainRole, "workspace");

        public AnalysisReportSurface<TargetIdentity> Surface(
            AnalysisReportSurfaceKind kind,
            AnalysisTargetRoleDescriptor role,
            string value)
            => new(kind, [new(role, new(value))]);

        public AnalysisUniverseDescription<UniverseBoundary, UniverseState> ValidUniverse()
            => Universe(
                AnalysisUniverseBoundKind.Finite,
                [SubjectCapability, EvidenceCapability]);

        public AnalysisUniverseDescription<UniverseBoundary, UniverseState> Universe(
            AnalysisUniverseBoundKind boundKind,
            IReadOnlyList<AnalysisUniverseCapabilityDescriptor> capabilities,
            string providerKind = "Workspace",
            string requested = "requested",
            string realized = "realized")
            => new(
                boundKind,
                new(requested),
                new(realized),
                capabilities,
                new("Complete", [], providerKind));
    }

    private sealed record TargetIdentity(string Value);

    private sealed record UniverseBoundary(string Value);

    private sealed record UniverseState(
        string Completeness,
        ImmutableArray<string> Failures,
        string ProviderKind = "Workspace");

    private sealed class IntegrationConceptDescriptor(string name)
        : AnalysisRequestDefinition(name);

    private sealed class IntegrationAnalysisDescriptor : AnalysisDescriptor
    {
        public IntegrationAnalysisDescriptor(
            string name,
            string revision,
            IReadOnlyList<AnalysisQuestionMode> supportedModes,
            IReadOnlyList<AnalysisTargetRoleDeclaration> targetRoles,
            IReadOnlyList<AnalysisProjectionDescriptor> supportedProjections,
            IReadOnlyList<AnalysisUniverseRequirementDescriptor> universeRequirements,
            IReadOnlyList<AnalysisStructuralPrerequisiteDescriptor> structuralPrerequisites,
            IReadOnlyList<AnalysisPreflightRequirementDescriptor> preflightRequirements,
            IReadOnlyList<IntegrationConceptDescriptor> concepts)
            : base(
                name,
                revision,
                supportedModes,
                targetRoles,
                supportedProjections,
                universeRequirements,
                structuralPrerequisites,
                preflightRequirements)
        {
            Concepts = [.. concepts];
        }

        public ImmutableArray<IntegrationConceptDescriptor> Concepts { get; }
    }

    private sealed class IntegrationProducerPolicyDescriptor(string name)
        : AnalysisRequestDefinition(name);

    private sealed class IntegrationUniverseRequirementDescriptor
        : AnalysisUniverseRequirementDescriptor
    {
        public IntegrationUniverseRequirementDescriptor(
            string name,
            AnalysisUniverseCapabilityDescriptor capability,
            IntegrationProducerPolicyDescriptor policy,
            IReadOnlyList<IntegrationConceptDescriptor> concepts)
            : base(name, capability)
        {
            Policy = policy;
            Concepts = [.. concepts];
        }

        public IntegrationProducerPolicyDescriptor Policy { get; }

        public ImmutableArray<IntegrationConceptDescriptor> Concepts { get; }
    }

    private sealed class PoisonProviderState
    {
        public int ExecutionCount { get; private set; }

        public void Execute()
        {
            ExecutionCount++;
            throw new InvalidOperationException("Planning executed a provider.");
        }
    }
}
