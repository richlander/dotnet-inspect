using System.Collections.Immutable;
using System.Reflection;
using DotnetInspector.Queries;

namespace DotnetInspector.Queries.Tests;

public sealed class AnalysisRequestPlanningTests
{
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

        Type[] rejectionFamilies = RejectionFamilies();
        Assert.Equal(
            [
                typeof(AnalysisRequestRejection.InvalidMode),
                typeof(AnalysisRequestRejection.InvalidReportSurface),
                typeof(AnalysisRequestRejection.MissingStructuralPrerequisites),
                typeof(AnalysisRequestRejection.MissingUniverse),
                typeof(AnalysisRequestRejection.UnboundedUniverse),
                typeof(AnalysisRequestRejection.UnconfiguredAnalysis),
                typeof(AnalysisRequestRejection.UnsatisfiedUniverse),
                typeof(AnalysisRequestRejection.UnsupportedMode),
                typeof(AnalysisRequestRejection.UnsupportedProjection),
                typeof(AnalysisRequestRejection.UnsupportedSurface),
                typeof(AnalysisRequestRejection.UnsupportedTargetRole),
            ],
            rejectionFamilies);
        Assert.All(rejectionFamilies, family => Assert.True(family.IsSealed));
        Assert.All(
            rejectionFamilies,
            family => Assert.Empty(
                family.GetConstructors(BindingFlags.Public | BindingFlags.Instance)));
        Assert.All(
            typeof(AnalysisRequestRejection)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(constructor.IsPrivate));
        Assert.True(typeof(AnalysisTargetRoleDescriptor).IsAbstract);
        Assert.True(typeof(AnalysisTargetRoleDeclaration).IsAbstract);
        Assert.True(typeof(AnalysisDescriptor).IsAbstract);
        Assert.True(typeof(AnalysisUniverseDescription).IsAbstract);
    }

    [Fact]
    public void AnalysisRequest_ReportSurfaceAndUniverseAreIndependent()
    {
        Scenario scenario = new();
        AnalysisReportSurface<LibraryIdentity> surface = scenario.ValidLibrarySurface();
        WorkspaceTypeUniverse universe = scenario.ValidUniverse(
            requested: "workspace-request",
            realized: "workspace-realized");

        var plan = Validate(
            scenario.Planner.Plan(
                scenario.Request(
                    scenario.Analysis,
                    surface,
                    universe,
                    AnalysisQuestionMode.Targeted,
                    scenario.RowsProjection)));

        Assert.Same(surface, plan.ReportSurface);
        Assert.Same(universe, plan.Universe);
        Assert.Equal(AnalysisReportSurfaceKind.Library, plan.ReportSurface.Kind);
        Assert.Equal("workspace-request", plan.Universe.RequestedBoundary.Value);
    }

    [Fact]
    public void AnalysisRequest_MemberReportMayConsumeWorkspaceUniverse()
    {
        Scenario scenario = new();
        AnalysisReportSurface<MemberIdentity> memberSurface = scenario.Surface(
            AnalysisReportSurfaceKind.Member,
            scenario.MemberAnchorRole,
            new("method"));
        WorkspaceTypeUniverse workspaceUniverse =
            scenario.ValidUniverse(providerKind: "Workspace");

        AnalysisValidatedPlan<IntegrationAnalysisDescriptor, MemberIdentity, WorkspaceTypeUniverse>
            plan = Validate(
                scenario.Planner.Plan(
                    new AnalysisRequest<
                        IntegrationAnalysisDescriptor,
                        MemberIdentity,
                        WorkspaceTypeUniverse>(
                            scenario.Analysis,
                            memberSurface,
                            workspaceUniverse,
                            AnalysisQuestionMode.Targeted,
                            scenario.RowsProjection)));

        Assert.Equal(AnalysisReportSurfaceKind.Member, plan.ReportSurface.Kind);
        Assert.Equal("Workspace", plan.Universe.ProviderKind);
    }

    [Fact]
    public void AnalysisRequest_UniverseBreadthCannotWidenReportSurface()
    {
        Scenario scenario = new();
        AnalysisReportSurface<MemberIdentity> memberSurface = scenario.Surface(
            AnalysisReportSurfaceKind.Member,
            scenario.MemberAnchorRole,
            new("method"));
        WorkspaceTypeUniverse broadUniverse = scenario.ValidUniverse(
            requested: "entire-workspace",
            realized: "entire-workspace");

        AnalysisValidatedPlan<IntegrationAnalysisDescriptor, MemberIdentity, WorkspaceTypeUniverse>
            plan = Validate(
                scenario.Planner.Plan(
                    new AnalysisRequest<
                        IntegrationAnalysisDescriptor,
                        MemberIdentity,
                        WorkspaceTypeUniverse>(
                            scenario.Analysis,
                            memberSurface,
                            broadUniverse,
                            AnalysisQuestionMode.Targeted,
                            scenario.RowsProjection)));

        Assert.Same(memberSurface, plan.ReportSurface);
        Assert.Equal(AnalysisReportSurfaceKind.Member, plan.ReportSurface.Kind);
        Assert.Same(broadUniverse, plan.Universe);
    }

    [Fact]
    public void AnalysisRequest_TargetedRequiresAcceptedAnchor()
    {
        Scenario scenario = new();
        var request = scenario.Request(
            scenario.Analysis,
            scenario.Surface(
                AnalysisReportSurfaceKind.Library,
                scenario.LibraryDomainRole,
                new("library")),
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
        var request = new AnalysisRequest<
            IntegrationAnalysisDescriptor,
            WorkspaceIdentity,
            WorkspaceTypeUniverse>(
                scenario.Analysis,
                scenario.Surface(
                    AnalysisReportSurfaceKind.Workspace,
                    scenario.WorkspaceAnchorRole,
                    new("workspace")),
                scenario.ValidUniverse(),
                AnalysisQuestionMode.Census,
                scenario.RowsProjection);

        var invalid = Assert.IsType<AnalysisRequestRejection.InvalidMode>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(AnalysisModeViolation.CensusContainsPrivilegedAnchor, invalid.Violation);
        Assert.NotEmpty(invalid.Guidance);
    }

    [Fact]
    public void AnalysisRequest_ModeValidationDerivesFromDeclaredTargetFunctions()
    {
        Scenario scenario = new();
        var role = new AnalysisTargetRoleDescriptor<WorkspaceIdentity>("Shared role");
        var analysis = new TestAnalysisDescriptor(
            "Shared-role analysis",
            "v1",
            [AnalysisQuestionMode.Targeted, AnalysisQuestionMode.Census],
            [
                new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Targeted,
                    role,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    role,
                    AnalysisTargetFunction.ReportDomain),
            ],
            [scenario.RowsProjection],
            [scenario.SubjectRequirement],
            [scenario.Prerequisite]);
        var planner = new AnalysisRequestPlanner([analysis], [scenario.Prerequisite]);
        AnalysisReportSurface<WorkspaceIdentity> surface = scenario.Surface(
            AnalysisReportSurfaceKind.Workspace,
            role,
            new("workspace"));
        WorkspaceTypeUniverse universe = scenario.ValidUniverse();

        Validate(
            planner.Plan(
                new AnalysisRequest<
                    TestAnalysisDescriptor,
                    WorkspaceIdentity,
                    WorkspaceTypeUniverse>(
                        analysis,
                        surface,
                        universe,
                        AnalysisQuestionMode.Targeted,
                        scenario.RowsProjection)));
        Validate(
            planner.Plan(
                new AnalysisRequest<
                    TestAnalysisDescriptor,
                    WorkspaceIdentity,
                    WorkspaceTypeUniverse>(
                        analysis,
                        surface,
                        universe,
                        AnalysisQuestionMode.Census,
                        scenario.RowsProjection)));
    }

    [Fact]
    public void AnalysisRequest_RejectsMissingOrUnboundedUniverseBeforeProducerExecution()
    {
        Scenario scenario = new();

        Assert.IsType<AnalysisRequestRejection.MissingUniverse>(
            Reject(
                scenario.Planner.Plan(
                    scenario.Request(
                        scenario.Analysis,
                        scenario.ValidLibrarySurface(),
                        universe: null,
                        AnalysisQuestionMode.Targeted,
                        scenario.RowsProjection))));
        Assert.IsType<AnalysisRequestRejection.UnboundedUniverse>(
            Reject(
                scenario.Planner.Plan(
                    scenario.Request(
                        scenario.Analysis,
                        scenario.ValidLibrarySurface(),
                        scenario.ValidUniverse(boundKind: AnalysisUniverseBoundKind.Unbounded),
                        AnalysisQuestionMode.Targeted,
                        scenario.RowsProjection))));
    }

    [Fact]
    public void AnalysisCapability_StructuralDiscoveryDoesNotResolveContentExecuteProducersOrProbeEffectiveness()
    {
        Scenario scenario = new();
        var planner = new AnalysisRequestPlanner([scenario.Analysis], [scenario.Prerequisite]);

        Assert.Same(scenario.Analysis, Assert.Single(planner.Descriptors));
        Assert.Equal(scenario.Concepts, scenario.Analysis.Concepts);
        Assert.DoesNotContain(AnalysisContractMemberTypes(), ContainsDelegate);
    }

    [Fact]
    public void AnalysisCapability_ListsConfiguredUnobservedIntegrationDescriptors()
    {
        Scenario scenario = new();
        var second = new TestAnalysisDescriptor(
            "Dependency census",
            "v1",
            [AnalysisQuestionMode.Census],
            [
                new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    scenario.WorkspaceDomainRole,
                    AnalysisTargetFunction.ReportDomain),
            ],
            [scenario.RowsProjection]);
        var planner = new AnalysisRequestPlanner(
            [scenario.Analysis, second],
            [scenario.Prerequisite]);

        Assert.Equal([scenario.Analysis, second], planner.Descriptors);
        Assert.Same(scenario.Analysis, planner.Descriptors[0]);
        Assert.Equal(scenario.Concepts, scenario.Analysis.Concepts);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnconfiguredAnalysisBeforeProducerExecution()
    {
        Scenario scenario = new();
        IntegrationAnalysisDescriptor foreign = scenario.CreateAnalysis();
        var request = scenario.Request(
            foreign,
            scenario.ValidLibrarySurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var rejection = Assert.IsType<AnalysisRequestRejection.UnconfiguredAnalysis>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Same(foreign, rejection.Analysis);
        Assert.NotEmpty(rejection.Guidance);
    }

    [Fact]
    public void AnalysisRequest_RejectsInvalidReportSurfaceCardinalityBeforeProducerExecution()
    {
        Scenario scenario = new();
        var empty = new AnalysisReportSurface<LibraryIdentity>(
            AnalysisReportSurfaceKind.Library,
            []);
        var multipleWorkspaces = new AnalysisReportSurface<WorkspaceIdentity>(
            AnalysisReportSurfaceKind.Workspace,
            [
                new(scenario.WorkspaceDomainRole, new("workspace-a")),
                new(scenario.WorkspaceDomainRole, new("workspace-b")),
            ]);

        var missing = Assert.IsType<AnalysisRequestRejection.InvalidReportSurface>(
            Reject(
                scenario.Planner.Plan(
                    scenario.Request(
                        scenario.Analysis,
                        empty,
                        scenario.ValidUniverse(),
                        AnalysisQuestionMode.Targeted,
                        scenario.RowsProjection))));
        var multiple = Assert.IsType<AnalysisRequestRejection.InvalidReportSurface>(
            Reject(
                scenario.Planner.Plan(
                    new AnalysisRequest<
                        IntegrationAnalysisDescriptor,
                        WorkspaceIdentity,
                        WorkspaceTypeUniverse>(
                            scenario.Analysis,
                            multipleWorkspaces,
                            scenario.ValidUniverse(),
                            AnalysisQuestionMode.Census,
                            scenario.RowsProjection))));

        Assert.Equal(AnalysisReportSurfaceCardinalityViolation.MissingTarget, missing.Violation);
        Assert.Equal(
            AnalysisReportSurfaceCardinalityViolation.WorkspaceRequiresSingleTarget,
            multiple.Violation);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedModeBeforeProducerExecution()
    {
        Scenario scenario = new();
        TestAnalysisDescriptor analysis = scenario.CensusOnlyAnalysis();
        var planner = new AnalysisRequestPlanner([analysis], [scenario.Prerequisite]);
        var request = new AnalysisRequest<
            TestAnalysisDescriptor,
            WorkspaceIdentity,
            WorkspaceTypeUniverse>(
                analysis,
                scenario.ValidWorkspaceSurface(),
                universe: null,
                AnalysisQuestionMode.Targeted,
                scenario.UnsupportedProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedMode>(
            Reject(planner.Plan(request)));

        Assert.Equal(AnalysisQuestionMode.Targeted, unsupported.Mode);
        Assert.NotEmpty(unsupported.Guidance);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedSurfaceBeforeProducerExecution()
    {
        Scenario scenario = new();
        var typeRole = new AnalysisTargetRoleDescriptor<TypeIdentity>("Type");
        var request = new AnalysisRequest<
            IntegrationAnalysisDescriptor,
            TypeIdentity,
            WorkspaceTypeUniverse>(
                scenario.Analysis,
                scenario.Surface(
                    AnalysisReportSurfaceKind.Type,
                    typeRole,
                    new("type")),
                scenario.ValidUniverse(),
                AnalysisQuestionMode.Targeted,
                scenario.RowsProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedSurface>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(AnalysisReportSurfaceKind.Type, unsupported.SurfaceKind);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedTargetRoleBeforeProducerExecution()
    {
        Scenario scenario = new();
        var sameNamedRole =
            new AnalysisTargetRoleDescriptor<LibraryIdentity>(scenario.LibraryAnchorRole.Name);
        var request = scenario.Request(
            scenario.Analysis,
            scenario.Surface(
                AnalysisReportSurfaceKind.Library,
                sameNamedRole,
                new("library")),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedTargetRole>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Same(sameNamedRole, unsupported.Role);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsatisfiedUniverseBeforeProducerExecution()
    {
        Scenario scenario = new();
        var sameNamedCapability =
            new AnalysisUniverseCapabilityDescriptor(scenario.SubjectCapability.Name);
        WorkspaceTypeUniverse universe = scenario.ValidUniverse(
            capabilities: [sameNamedCapability]);
        var request = scenario.Request(
            scenario.Analysis,
            scenario.ValidLibrarySurface(),
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var unsatisfied = Assert.IsType<AnalysisRequestRejection.UnsatisfiedUniverse>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Equal(scenario.Analysis.Requirements, unsatisfied.Requirements);
    }

    [Fact]
    public void AnalysisCapability_RejectsMissingStructuralPrerequisiteBeforeProducerExecution()
    {
        Scenario scenario = new();
        var planner = new AnalysisRequestPlanner([scenario.Analysis], []);

        var missing = Assert.IsType<AnalysisRequestRejection.MissingStructuralPrerequisites>(
            Reject(planner.Plan(scenario.ValidTargetedRequest())));

        Assert.Equal([scenario.Prerequisite], missing.Prerequisites);
    }

    [Fact]
    public void AnalysisCapability_RejectsUnsupportedProjectionBeforeProducerExecution()
    {
        Scenario scenario = new();
        var sameNamedProjection =
            new AnalysisProjectionDescriptor(scenario.RowsProjection.Name);
        var request = scenario.Request(
            scenario.Analysis,
            scenario.ValidLibrarySurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            sameNamedProjection);

        var unsupported = Assert.IsType<AnalysisRequestRejection.UnsupportedProjection>(
            Reject(scenario.Planner.Plan(request)));

        Assert.Same(sameNamedProjection, unsupported.Projection);
    }

    [Fact]
    public void AnalysisCapability_AllDeclaredRejectionsPrecedeProducerExecution()
    {
        Scenario scenario = new();

        ImmutableArray<AnalysisRequestRejection> rejections = scenario.AllRejections();

        Assert.Equal(
            RejectionFamilies(),
            rejections
                .Select(rejection => rejection.GetType())
                .Distinct()
                .OrderBy(type => type.Name, StringComparer.Ordinal));
        Assert.Equal(
            Enum.GetValues<AnalysisModeViolation>(),
            rejections
                .OfType<AnalysisRequestRejection.InvalidMode>()
                .Select(rejection => rejection.Violation)
                .Order());
        Assert.All(rejections, rejection => Assert.NotEmpty(rejection.Guidance));
        Assert.Equal(0, scenario.ProviderExecutionCount);
        Type[] contractTypes = AnalysisContractTypes();
        Type[] planningResultCases = typeof(AnalysisRequestPlanningResult<,,>)
            .GetNestedTypes(BindingFlags.Public);
        Assert.All(
            RejectionFamilies().Concat(planningResultCases),
            nestedCase => Assert.Contains(nestedCase, contractTypes));
        Assert.DoesNotContain(AnalysisContractMemberTypes(), ContainsDelegate);
    }

    [Fact]
    public void AnalysisCapability_RejectionDoesNotUseFindingInspectionState()
    {
        string[] referencedTypeNames = RejectionFamilies()
            .Prepend(typeof(AnalysisRequestRejection))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.PropertyType.FullName ?? property.PropertyType.Name)
            .ToArray();

        Assert.DoesNotContain(
            referencedTypeNames,
            name => name.Contains("Finding", StringComparison.Ordinal)
                || name.Contains("InspectionState", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalysisPlan_RetainsExactRequestFieldsAndDescriptorRequirements()
    {
        Scenario scenario = new();
        AnalysisReportSurface<LibraryIdentity> surface = scenario.ValidLibrarySurface();
        WorkspaceTypeUniverse universe = scenario.ValidUniverse();
        var request = scenario.Request(
            scenario.Analysis,
            surface,
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var plan = Validate(scenario.Planner.Plan(request));

        Assert.Same(scenario.Analysis, plan.Analysis);
        Assert.Same(surface, plan.ReportSurface);
        Assert.Same(universe, plan.Universe);
        Assert.Equal(AnalysisQuestionMode.Targeted, plan.Mode);
        Assert.Same(scenario.RowsProjection, plan.Projection);
        Assert.Equal(scenario.Analysis.UniverseRequirements, plan.UniverseRequirements);
        Assert.Equal(scenario.Analysis.Requirements, plan.UniverseRequirements);
        Assert.Equal(
            scenario.Analysis.StructuralPrerequisites,
            plan.StructuralPrerequisites);
        Assert.Equal(scenario.Analysis.PreflightRequirements, plan.PreflightRequirements);
        Assert.Same(scenario.SubjectRequirement, plan.Analysis.Requirements[0]);
        Assert.Same(scenario.Concepts[0], plan.Analysis.Requirements[0].Concepts[0]);
    }

    [Fact]
    public void AnalysisPlan_RetainsUniverseCompletenessAndFailureInputs()
    {
        Scenario scenario = new();
        var requested = new RequestedTypeBoundary("requested");
        var realized = new RealizedTypeBoundary("realized");
        var completeness = new UniverseCompleteness("Partial");
        var failure = new UniverseFailure("package-a failed");
        WorkspaceTypeUniverse universe = scenario.ValidUniverse(
            requestedBoundary: requested,
            realizedBoundary: realized,
            completeness: completeness,
            failures: [failure]);
        var request = scenario.Request(
            scenario.Analysis,
            scenario.ValidLibrarySurface(),
            universe,
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);

        var plan = Validate(scenario.Planner.Plan(request));

        Assert.Same(requested, plan.Universe.RequestedBoundary);
        Assert.Same(realized, plan.Universe.RealizedBoundary);
        Assert.Same(completeness, plan.Universe.Completeness);
        Assert.Same(failure, Assert.Single(plan.Universe.Failures));
    }

    [Fact]
    public void AnalysisProjection_RowsAndGraphRetainOneAnalysisIdentity()
    {
        Scenario scenario = new();
        var rowsRequest = scenario.Request(
            scenario.Analysis,
            scenario.ValidLibrarySurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.RowsProjection);
        var graphRequest = scenario.Request(
            scenario.Analysis,
            scenario.ValidLibrarySurface(),
            scenario.ValidUniverse(),
            AnalysisQuestionMode.Targeted,
            scenario.GraphProjection);

        var rowsPlan = Validate(scenario.Planner.Plan(rowsRequest));
        var graphPlan = Validate(scenario.Planner.Plan(graphRequest));

        Assert.Same(scenario.Analysis, rowsPlan.Analysis);
        Assert.Same(rowsPlan.Analysis, graphPlan.Analysis);
        Assert.Same(scenario.RowsProjection, rowsPlan.Projection);
        Assert.Same(scenario.GraphProjection, graphPlan.Projection);
    }

    [Fact]
    public void AnalysisUniverseProviderKindDoesNotChangeRequestFieldSemantics()
    {
        Scenario scenario = new();
        var prefixRequest = new AnalysisRequest<
            IntegrationAnalysisDescriptor,
            WorkspaceIdentity,
            WorkspaceTypeUniverse>(
                scenario.Analysis,
                scenario.ValidWorkspaceSurface(),
                scenario.ValidUniverse(providerKind: "Package prefix"),
                AnalysisQuestionMode.Census,
                scenario.RowsProjection);
        var projectGraphRequest = new AnalysisRequest<
            IntegrationAnalysisDescriptor,
            WorkspaceIdentity,
            WorkspaceTypeUniverse>(
                scenario.Analysis,
                scenario.ValidWorkspaceSurface(),
                scenario.ValidUniverse(providerKind: "Project graph"),
                AnalysisQuestionMode.Census,
                scenario.RowsProjection);

        AnalysisValidatedPlan<
            IntegrationAnalysisDescriptor,
            WorkspaceIdentity,
            WorkspaceTypeUniverse> prefixPlan = Validate(
                scenario.Planner.Plan(prefixRequest));
        AnalysisValidatedPlan<
            IntegrationAnalysisDescriptor,
            WorkspaceIdentity,
            WorkspaceTypeUniverse> graphPlan = Validate(
                scenario.Planner.Plan(projectGraphRequest));

        Assert.Same(prefixPlan.Analysis, graphPlan.Analysis);
        Assert.Equal(prefixPlan.ReportSurface.Kind, graphPlan.ReportSurface.Kind);
        Assert.Equal(prefixPlan.Mode, graphPlan.Mode);
        Assert.Same(prefixPlan.Projection, graphPlan.Projection);
        Assert.NotEqual(prefixPlan.Universe.ProviderKind, graphPlan.Universe.ProviderKind);
    }

    [Fact]
    public void AnalysisDeclarations_RejectUndefinedEnums()
    {
        Scenario scenario = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnalysisTargetRoleDeclaration<LibraryIdentity>(
                AnalysisReportSurfaceKind.Library,
                AnalysisQuestionMode.Targeted,
                scenario.LibraryAnchorRole,
                (AnalysisTargetFunction)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AnalysisReportSurface<LibraryIdentity>(
                (AnalysisReportSurfaceKind)99,
                [new(scenario.LibraryAnchorRole, new("library"))]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scenario.ValidUniverse(boundKind: (AnalysisUniverseBoundKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scenario.Request(
                scenario.Analysis,
                scenario.ValidLibrarySurface(),
                scenario.ValidUniverse(),
                (AnalysisQuestionMode)99,
                scenario.RowsProjection));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TestAnalysisDescriptor(
                "Malformed",
                "v1",
                [(AnalysisQuestionMode)99],
                [
                    new AnalysisTargetRoleDeclaration<LibraryIdentity>(
                        AnalysisReportSurfaceKind.Library,
                        AnalysisQuestionMode.Targeted,
                        scenario.LibraryAnchorRole,
                        AnalysisTargetFunction.PrivilegedAnchor),
                ],
                [scenario.RowsProjection]));
    }

    [Fact]
    public void AnalysisPlanningResults_HavePlannerOwnedConstruction()
    {
        Type openResult = typeof(AnalysisRequestPlanningResult<,,>);
        Type validated = openResult.GetNestedType(
            nameof(AnalysisRequestPlanningResult<
                IntegrationAnalysisDescriptor,
                LibraryIdentity,
                WorkspaceTypeUniverse>.Validated))!;
        Type rejected = openResult.GetNestedType(
            nameof(AnalysisRequestPlanningResult<
                IntegrationAnalysisDescriptor,
                LibraryIdentity,
                WorkspaceTypeUniverse>.Rejected))!;

        Assert.Empty(validated.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(rejected.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.False(validated.GetProperty("Plan")!.CanWrite);
        Assert.False(rejected.GetProperty("Rejection")!.CanWrite);
        Assert.All(
            openResult.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance),
            constructor => Assert.True(constructor.IsPrivate));
    }

    [Fact]
    public void AnalysisUniverseCapabilities_RejectNullDeclaration()
    {
        Scenario scenario = new();
        var zeroRequirementAnalysis = new TestAnalysisDescriptor(
            "Zero requirements",
            "v1",
            [AnalysisQuestionMode.Targeted],
            [
                new AnalysisTargetRoleDeclaration<LibraryIdentity>(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    scenario.LibraryAnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
            ],
            [scenario.RowsProjection]);
        var planner = new AnalysisRequestPlanner([zeroRequirementAnalysis], []);

        Assert.Throws<ArgumentNullException>(() =>
        {
            var universe = new WorkspaceTypeUniverse(
                AnalysisUniverseBoundKind.Finite,
                capabilities: null!,
                new("requested"),
                new("realized"),
                new("Complete"),
                [],
                "Workspace",
                () => throw new InvalidOperationException());
            planner.Plan(
                new AnalysisRequest<
                    TestAnalysisDescriptor,
                    LibraryIdentity,
                    WorkspaceTypeUniverse>(
                        zeroRequirementAnalysis,
                        scenario.ValidLibrarySurface(),
                        universe,
                        AnalysisQuestionMode.Targeted,
                        scenario.RowsProjection));
        });
    }

    [Fact]
    public void AnalysisDescriptorsAndCatalogs_FreezeDeclarations()
    {
        Scenario scenario = new();
        AnalysisQuestionMode[] modes = [AnalysisQuestionMode.Targeted];
        AnalysisTargetRoleDeclaration[] roles =
        [
            new AnalysisTargetRoleDeclaration<LibraryIdentity>(
                AnalysisReportSurfaceKind.Library,
                AnalysisQuestionMode.Targeted,
                scenario.LibraryAnchorRole,
                AnalysisTargetFunction.PrivilegedAnchor),
        ];
        AnalysisProjectionDescriptor[] projections = [scenario.RowsProjection];
        var descriptor = new TestAnalysisDescriptor(
            "Frozen",
            "v1",
            modes,
            roles,
            projections);
        AnalysisDescriptor[] descriptors = [descriptor];
        var planner = new AnalysisRequestPlanner(descriptors, []);

        modes[0] = AnalysisQuestionMode.Census;
        roles[0] = new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
            AnalysisReportSurfaceKind.Workspace,
            AnalysisQuestionMode.Census,
            scenario.WorkspaceDomainRole,
            AnalysisTargetFunction.ReportDomain);
        projections[0] = scenario.GraphProjection;
        descriptors[0] = scenario.Analysis;

        Assert.Equal([AnalysisQuestionMode.Targeted], descriptor.SupportedModes);
        Assert.Same(scenario.LibraryAnchorRole, Assert.Single(descriptor.TargetRoles).Role);
        Assert.Same(scenario.RowsProjection, Assert.Single(descriptor.SupportedProjections));
        Assert.Same(descriptor, Assert.Single(planner.Descriptors));
    }

    [Fact]
    public void AnalysisRequirements_SharedCapabilitiesRetainTypedOwnerScope()
    {
        Scenario scenario = new();
        IntegrationConceptDescriptor registrations = scenario.Concepts[0];
        IntegrationConceptDescriptor clients = scenario.Concepts[1];
        var attributes = new IntegrationProducerPolicyDescriptor("Attribute policy");
        var calls = new IntegrationProducerPolicyDescriptor("Call policy");
        var attributeRequirement = new IntegrationUniverseRequirementDescriptor(
            "Attribute evidence",
            scenario.EvidenceCapability,
            attributes,
            [registrations, clients]);
        var callRequirement = new IntegrationUniverseRequirementDescriptor(
            "Call evidence",
            scenario.EvidenceCapability,
            calls,
            [clients]);
        IntegrationAnalysisDescriptor analysis = scenario.CreateAnalysis(
            requirements:
            [
                scenario.SubjectRequirement,
                attributeRequirement,
                callRequirement,
            ]);
        var planner = new AnalysisRequestPlanner([analysis], [scenario.Prerequisite]);
        var plan = Validate(
            planner.Plan(
                scenario.Request(
                    analysis,
                    scenario.ValidLibrarySurface(),
                    scenario.ValidUniverse(),
                    AnalysisQuestionMode.Targeted,
                    scenario.RowsProjection)));

        Assert.Equal(
            [scenario.SubjectRequirement, attributeRequirement, callRequirement],
            plan.Analysis.Requirements);
        Assert.Equal([registrations, clients], attributeRequirement.Concepts);
        Assert.Same(calls, callRequirement.Policy);

        WorkspaceTypeUniverse missingEvidence = scenario.ValidUniverse(
            capabilities: [scenario.SubjectCapability]);
        var unsatisfied = Assert.IsType<AnalysisRequestRejection.UnsatisfiedUniverse>(
            Reject(
                planner.Plan(
                    scenario.Request(
                        analysis,
                        scenario.ValidLibrarySurface(),
                        missingEvidence,
                        AnalysisQuestionMode.Targeted,
                        scenario.RowsProjection))));
        IntegrationUniverseRequirementDescriptor[] typedUnmet = analysis.Requirements
            .Where(
                requirement => unsatisfied.Requirements.Any(
                    unmet => ReferenceEquals(unmet, requirement)))
            .ToArray();
        Assert.Equal([attributeRequirement, callRequirement], typedUnmet);
    }

    private static AnalysisValidatedPlan<TAnalysis, TIdentity, TUniverse> Validate<
        TAnalysis,
        TIdentity,
        TUniverse>(
        AnalysisRequestPlanningResult<TAnalysis, TIdentity, TUniverse> result)
        where TAnalysis : AnalysisDescriptor
        where TUniverse : AnalysisUniverseDescription
        => Assert.IsType<
            AnalysisRequestPlanningResult<TAnalysis, TIdentity, TUniverse>.Validated>(
                result).Plan;

    private static AnalysisRequestRejection Reject<TAnalysis, TIdentity, TUniverse>(
        AnalysisRequestPlanningResult<TAnalysis, TIdentity, TUniverse> result)
        where TAnalysis : AnalysisDescriptor
        where TUniverse : AnalysisUniverseDescription
        => Assert.IsType<
            AnalysisRequestPlanningResult<TAnalysis, TIdentity, TUniverse>.Rejected>(
                result).Rejection;

    private static Type[] RejectionFamilies()
        => typeof(AnalysisRequestRejection)
            .GetNestedTypes(BindingFlags.Public)
            .Where(type => type.IsAssignableTo(typeof(AnalysisRequestRejection)))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

    private static Type[] AnalysisContractTypes()
    {
        return typeof(AnalysisRequestPlanner).Assembly
            .GetExportedTypes()
            .Where(
                type => type.Namespace == typeof(AnalysisRequestPlanner).Namespace
                    && DeclaringTypeChain(type).Any(
                        candidate => candidate.Name.StartsWith(
                            "Analysis",
                            StringComparison.Ordinal)))
            .ToArray();
    }

    private static Type[] AnalysisContractMemberTypes()
    {
        Type[] contractTypes = AnalysisContractTypes();
        return
        [
            .. contractTypes.SelectMany(
                type => type
                    .GetConstructors(
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static)
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType)),
            .. contractTypes.SelectMany(
                type => type
                    .GetMethods(
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.DeclaredOnly)
                    .SelectMany(
                        method => method
                            .GetParameters()
                            .Select(parameter => parameter.ParameterType)
                            .Append(method.ReturnType))),
            .. contractTypes.SelectMany(
                type => type
                    .GetProperties(
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static)
                    .Select(property => property.PropertyType)),
            .. contractTypes.SelectMany(
                type => type
                    .GetFields(
                        BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static)
                    .Select(field => field.FieldType)),
        ];
    }

    private static IEnumerable<Type> DeclaringTypeChain(Type type)
    {
        for (Type? current = type; current is not null; current = current.DeclaringType)
            yield return current;
    }

    private static bool ContainsDelegate(Type type)
    {
        var pending = new Stack<Type>();
        var visited = new HashSet<Type>();
        pending.Push(type);
        while (pending.TryPop(out Type? current))
        {
            if (!visited.Add(current))
                continue;
            if (typeof(Delegate).IsAssignableFrom(current))
                return true;
            if (current.HasElementType && current.GetElementType() is Type element)
                pending.Push(element);
            foreach (Type argument in current.GetGenericArguments())
                pending.Push(argument);
        }

        return false;
    }

    private sealed class Scenario
    {
        public Scenario()
        {
            SubjectRequirement = new(
                "Selected Types",
                SubjectCapability,
                SubjectPolicy,
                Concepts);
            EvidenceRequirement = new(
                "Structured evidence",
                EvidenceCapability,
                EvidencePolicy,
                Concepts);
            Analysis = CreateAnalysis();
            Planner = new([Analysis], [Prerequisite]);
        }

        public AnalysisTargetRoleDescriptor<LibraryIdentity> LibraryAnchorRole { get; } =
            new("Hub");

        public AnalysisTargetRoleDescriptor<LibraryIdentity> LibraryDomainRole { get; } =
            new("Library domain");

        public AnalysisTargetRoleDescriptor<MemberIdentity> MemberAnchorRole { get; } =
            new("Member");

        public AnalysisTargetRoleDescriptor<WorkspaceIdentity> WorkspaceDomainRole { get; } =
            new("Workspace");

        public AnalysisTargetRoleDescriptor<WorkspaceIdentity> WorkspaceAnchorRole { get; } =
            new("Workspace hub");

        public AnalysisUniverseCapabilityDescriptor SubjectCapability { get; } =
            new("Selected-Type membership");

        public AnalysisUniverseCapabilityDescriptor EvidenceCapability { get; } =
            new("Integration structured evidence");

        public IntegrationProducerPolicyDescriptor SubjectPolicy { get; } =
            new("Subject policy");

        public IntegrationProducerPolicyDescriptor EvidencePolicy { get; } =
            new("Evidence policy");

        public ImmutableArray<IntegrationConceptDescriptor> Concepts { get; } =
        [
            new("Registrations"),
            new("Client construction"),
            new("Pipeline hooks"),
        ];

        public IntegrationUniverseRequirementDescriptor SubjectRequirement { get; }

        public IntegrationUniverseRequirementDescriptor EvidenceRequirement { get; }

        public AnalysisStructuralPrerequisiteDescriptor Prerequisite { get; } =
            new("Integration producer catalog");

        public AnalysisPreflightRequirementDescriptor Preflight { get; } =
            new("Expensive analysis authorization");

        public AnalysisProjectionDescriptor RowsProjection { get; } = new("Rows");

        public AnalysisProjectionDescriptor MatrixProjection { get; } = new("Matrix");

        public AnalysisProjectionDescriptor GraphProjection { get; } = new("Graph");

        public AnalysisProjectionDescriptor UnsupportedProjection { get; } = new("Tree");

        public IntegrationAnalysisDescriptor Analysis { get; }

        public AnalysisRequestPlanner Planner { get; }

        public int ProviderExecutionCount { get; private set; }

        public IntegrationAnalysisDescriptor CreateAnalysis(
            IReadOnlyList<IntegrationUniverseRequirementDescriptor>? requirements = null)
            => new(
                "Integration census",
                "v1",
                [AnalysisQuestionMode.Targeted, AnalysisQuestionMode.Census],
                DefaultTargetRoles(),
                [RowsProjection, MatrixProjection, GraphProjection],
                requirements ?? [SubjectRequirement, EvidenceRequirement],
                [Prerequisite],
                [Preflight],
                Concepts);

        public TestAnalysisDescriptor CensusOnlyAnalysis()
            => new(
                "Census only",
                "v1",
                [AnalysisQuestionMode.Census],
                [
                    new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
                        AnalysisReportSurfaceKind.Workspace,
                        AnalysisQuestionMode.Census,
                        WorkspaceDomainRole,
                        AnalysisTargetFunction.ReportDomain),
                ],
                [RowsProjection],
                [SubjectRequirement, EvidenceRequirement],
                [Prerequisite]);

        public AnalysisRequest<
            IntegrationAnalysisDescriptor,
            LibraryIdentity,
            WorkspaceTypeUniverse> ValidTargetedRequest()
            => Request(
                Analysis,
                ValidLibrarySurface(),
                ValidUniverse(),
                AnalysisQuestionMode.Targeted,
                RowsProjection);

        public AnalysisRequest<
            IntegrationAnalysisDescriptor,
            LibraryIdentity,
            WorkspaceTypeUniverse> Request(
                IntegrationAnalysisDescriptor analysis,
                AnalysisReportSurface<LibraryIdentity> reportSurface,
                WorkspaceTypeUniverse? universe,
                AnalysisQuestionMode mode,
                AnalysisProjectionDescriptor projection)
            => new(analysis, reportSurface, universe, mode, projection);

        public AnalysisReportSurface<LibraryIdentity> ValidLibrarySurface()
            => Surface(
                AnalysisReportSurfaceKind.Library,
                LibraryAnchorRole,
                new("library"));

        public AnalysisReportSurface<WorkspaceIdentity> ValidWorkspaceSurface()
            => Surface(
                AnalysisReportSurfaceKind.Workspace,
                WorkspaceDomainRole,
                new("workspace"));

        public AnalysisReportSurface<TIdentity> Surface<TIdentity>(
            AnalysisReportSurfaceKind kind,
            AnalysisTargetRoleDescriptor<TIdentity> role,
            TIdentity identity)
            => new(kind, [new(role, identity)]);

        public WorkspaceTypeUniverse ValidUniverse(
            AnalysisUniverseBoundKind boundKind = AnalysisUniverseBoundKind.Finite,
            IReadOnlyList<AnalysisUniverseCapabilityDescriptor>? capabilities = null,
            string providerKind = "Workspace",
            string requested = "requested",
            string realized = "realized",
            RequestedTypeBoundary? requestedBoundary = null,
            RealizedTypeBoundary? realizedBoundary = null,
            UniverseCompleteness? completeness = null,
            IReadOnlyList<UniverseFailure>? failures = null)
            => new(
                boundKind,
                capabilities ?? [SubjectCapability, EvidenceCapability],
                requestedBoundary ?? new(requested),
                realizedBoundary ?? new(realized),
                completeness ?? new("Complete"),
                failures ?? [],
                providerKind,
                ExecuteProvider);

        public ImmutableArray<AnalysisRequestRejection> AllRejections()
        {
            IntegrationAnalysisDescriptor foreign = CreateAnalysis();
            AnalysisRequestRejection unconfigured = Reject(
                Planner.Plan(
                    Request(
                        foreign,
                        ValidLibrarySurface(),
                        ValidUniverse(),
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            AnalysisRequestRejection missingTarget = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        new(AnalysisReportSurfaceKind.Library, []),
                        ValidUniverse(),
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            AnalysisRequestRejection multipleWorkspaces = Reject(
                Planner.Plan(
                    new AnalysisRequest<
                        IntegrationAnalysisDescriptor,
                        WorkspaceIdentity,
                        WorkspaceTypeUniverse>(
                            Analysis,
                            new(
                                AnalysisReportSurfaceKind.Workspace,
                                [
                                    new(WorkspaceDomainRole, new("a")),
                                    new(WorkspaceDomainRole, new("b")),
                                ]),
                            ValidUniverse(),
                            AnalysisQuestionMode.Census,
                            RowsProjection)));

            TestAnalysisDescriptor censusOnly = CensusOnlyAnalysis();
            var censusOnlyPlanner = new AnalysisRequestPlanner(
                [censusOnly],
                [Prerequisite]);
            AnalysisRequestRejection unsupportedMode = Reject(
                censusOnlyPlanner.Plan(
                    new AnalysisRequest<
                        TestAnalysisDescriptor,
                        WorkspaceIdentity,
                        WorkspaceTypeUniverse>(
                            censusOnly,
                            ValidWorkspaceSurface(),
                            ValidUniverse(),
                            AnalysisQuestionMode.Targeted,
                            RowsProjection)));

            var typeRole = new AnalysisTargetRoleDescriptor<TypeIdentity>("Type");
            AnalysisRequestRejection unsupportedSurface = Reject(
                Planner.Plan(
                    new AnalysisRequest<
                        IntegrationAnalysisDescriptor,
                        TypeIdentity,
                        WorkspaceTypeUniverse>(
                            Analysis,
                            Surface(
                                AnalysisReportSurfaceKind.Type,
                                typeRole,
                                new("type")),
                            ValidUniverse(),
                            AnalysisQuestionMode.Targeted,
                            RowsProjection)));

            var foreignRole = new AnalysisTargetRoleDescriptor<LibraryIdentity>("Hub");
            AnalysisRequestRejection unsupportedRole = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        Surface(
                            AnalysisReportSurfaceKind.Library,
                            foreignRole,
                            new("library")),
                        ValidUniverse(),
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            AnalysisRequestRejection targetedMode = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        Surface(
                            AnalysisReportSurfaceKind.Library,
                            LibraryDomainRole,
                            new("library")),
                        ValidUniverse(),
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            AnalysisRequestRejection censusMode = Reject(
                Planner.Plan(
                    new AnalysisRequest<
                        IntegrationAnalysisDescriptor,
                        WorkspaceIdentity,
                        WorkspaceTypeUniverse>(
                            Analysis,
                            Surface(
                                AnalysisReportSurfaceKind.Workspace,
                                WorkspaceAnchorRole,
                                new("workspace")),
                            ValidUniverse(),
                            AnalysisQuestionMode.Census,
                            RowsProjection)));
            AnalysisRequestRejection missingUniverse = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        ValidLibrarySurface(),
                        universe: null,
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            AnalysisRequestRejection unboundedUniverse = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        ValidLibrarySurface(),
                        ValidUniverse(boundKind: AnalysisUniverseBoundKind.Unbounded),
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            AnalysisRequestRejection unsatisfiedUniverse = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        ValidLibrarySurface(),
                        ValidUniverse(capabilities: []),
                        AnalysisQuestionMode.Targeted,
                        RowsProjection)));
            var missingPrerequisitePlanner = new AnalysisRequestPlanner([Analysis], []);
            AnalysisRequestRejection missingPrerequisite = Reject(
                missingPrerequisitePlanner.Plan(ValidTargetedRequest()));
            AnalysisRequestRejection unsupportedProjection = Reject(
                Planner.Plan(
                    Request(
                        Analysis,
                        ValidLibrarySurface(),
                        ValidUniverse(),
                        AnalysisQuestionMode.Targeted,
                        UnsupportedProjection)));

            return
            [
                unconfigured,
                missingTarget,
                multipleWorkspaces,
                unsupportedMode,
                unsupportedSurface,
                unsupportedRole,
                targetedMode,
                censusMode,
                missingUniverse,
                unboundedUniverse,
                unsatisfiedUniverse,
                missingPrerequisite,
                unsupportedProjection,
            ];
        }

        private ImmutableArray<AnalysisTargetRoleDeclaration> DefaultTargetRoles()
            =>
            [
                new AnalysisTargetRoleDeclaration<LibraryIdentity>(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    LibraryAnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new AnalysisTargetRoleDeclaration<LibraryIdentity>(
                    AnalysisReportSurfaceKind.Library,
                    AnalysisQuestionMode.Targeted,
                    LibraryDomainRole,
                    AnalysisTargetFunction.ReportDomain),
                new AnalysisTargetRoleDeclaration<MemberIdentity>(
                    AnalysisReportSurfaceKind.Member,
                    AnalysisQuestionMode.Targeted,
                    MemberAnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
                new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    WorkspaceDomainRole,
                    AnalysisTargetFunction.ReportDomain),
                new AnalysisTargetRoleDeclaration<WorkspaceIdentity>(
                    AnalysisReportSurfaceKind.Workspace,
                    AnalysisQuestionMode.Census,
                    WorkspaceAnchorRole,
                    AnalysisTargetFunction.PrivilegedAnchor),
            ];

        private void ExecuteProvider()
        {
            ProviderExecutionCount++;
            throw new InvalidOperationException("Planning executed a universe provider.");
        }
    }

    private sealed record LibraryIdentity(string Value);

    private sealed record MemberIdentity(string Value);

    private sealed record TypeIdentity(string Value);

    private sealed record WorkspaceIdentity(string Value);

    private sealed record RequestedTypeBoundary(string Value);

    private sealed record RealizedTypeBoundary(string Value);

    private sealed record UniverseCompleteness(string Value);

    private sealed record UniverseFailure(string Value);

    private sealed class WorkspaceTypeUniverse : AnalysisUniverseDescription
    {
        private readonly Action _executeProvider;

        public WorkspaceTypeUniverse(
            AnalysisUniverseBoundKind boundKind,
            IReadOnlyList<AnalysisUniverseCapabilityDescriptor> capabilities,
            RequestedTypeBoundary requestedBoundary,
            RealizedTypeBoundary realizedBoundary,
            UniverseCompleteness completeness,
            IReadOnlyList<UniverseFailure> failures,
            string providerKind,
            Action executeProvider)
            : base(boundKind, capabilities)
        {
            RequestedBoundary = requestedBoundary;
            RealizedBoundary = realizedBoundary;
            Completeness = completeness;
            Failures = [.. failures];
            ProviderKind = providerKind;
            _executeProvider = executeProvider;
        }

        public RequestedTypeBoundary RequestedBoundary { get; }

        public RealizedTypeBoundary RealizedBoundary { get; }

        public UniverseCompleteness Completeness { get; }

        public ImmutableArray<UniverseFailure> Failures { get; }

        public string ProviderKind { get; }

        public void ExecuteProvider() => _executeProvider();
    }

    private sealed class IntegrationConceptDescriptor(string name)
        : AnalysisRequestDefinition(name);

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

    private sealed class IntegrationAnalysisDescriptor : AnalysisDescriptor
    {
        public IntegrationAnalysisDescriptor(
            string name,
            string revision,
            IReadOnlyList<AnalysisQuestionMode> supportedModes,
            IReadOnlyList<AnalysisTargetRoleDeclaration> targetRoles,
            IReadOnlyList<AnalysisProjectionDescriptor> supportedProjections,
            IReadOnlyList<IntegrationUniverseRequirementDescriptor> requirements,
            IReadOnlyList<AnalysisStructuralPrerequisiteDescriptor> structuralPrerequisites,
            IReadOnlyList<AnalysisPreflightRequirementDescriptor> preflightRequirements,
            IReadOnlyList<IntegrationConceptDescriptor> concepts)
            : base(
                name,
                revision,
                supportedModes,
                targetRoles,
                supportedProjections,
                requirements,
                structuralPrerequisites,
                preflightRequirements)
        {
            Requirements = [.. requirements];
            Concepts = [.. concepts];
        }

        public ImmutableArray<IntegrationUniverseRequirementDescriptor> Requirements { get; }

        public ImmutableArray<IntegrationConceptDescriptor> Concepts { get; }
    }

    private sealed class TestAnalysisDescriptor : AnalysisDescriptor
    {
        public TestAnalysisDescriptor(
            string name,
            string revision,
            IReadOnlyList<AnalysisQuestionMode> supportedModes,
            IReadOnlyList<AnalysisTargetRoleDeclaration> targetRoles,
            IReadOnlyList<AnalysisProjectionDescriptor> supportedProjections,
            IReadOnlyList<AnalysisUniverseRequirementDescriptor>? universeRequirements = null,
            IReadOnlyList<AnalysisStructuralPrerequisiteDescriptor>? structuralPrerequisites = null)
            : base(
                name,
                revision,
                supportedModes,
                targetRoles,
                supportedProjections,
                universeRequirements,
                structuralPrerequisites)
        {
        }
    }

}
