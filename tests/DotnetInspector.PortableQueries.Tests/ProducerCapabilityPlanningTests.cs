using System.Reflection;
using QuerySpace.Composition;

namespace DotnetInspector.PortableQueries.Tests;

public sealed class ProducerCapabilityPlanningTests
{
    [Fact]
    public void ProducerCapabilityPlanPreservesEveryRequirement()
    {
        var fixture = new CapabilityFixture();

        ProducerCapabilityPlan plan =
            Accept(fixture.Validate(
                [
                    fixture.RequireRows(
                        fixture.RowsAssociation,
                        fixture.RowDemand),
                    fixture.RequireCount(fixture.CountAssociation),
                ],
                [fixture.RowsProvision, fixture.CountProvision],
                [fixture.RowsCoverCount],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [fixture.RowsProvision.Identity],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.RowsAssociation,
                            fixture.RowsProvision.Identity),
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            fixture.RowsProvision.Identity,
                            [fixture.RowsCoverCount.Identity]),
                    ])));

        Assert.Equal(
            [fixture.RowsAssociation, fixture.CountAssociation],
            plan.Satisfactions.Select(
                static satisfaction =>
                    satisfaction.Requirement.Association));
        Assert.Single(plan.Provisions);
        Assert.Same(
            fixture.RowDemand,
            plan.Requirements[0].Parameters);
        Assert.Null(plan.Requirements[1].Parameters);
        Assert.Equal(
            [
                ProducerCapabilitySatisfactionKind.Direct,
                ProducerCapabilitySatisfactionKind.Covering,
            ],
            plan.Satisfactions.Select(
                static satisfaction => satisfaction.Kind));
    }

    [Fact]
    public void ProducerCapabilityCoverageRequiresOwnerProof()
    {
        var fixture = new CapabilityFixture();
        ProducerCapabilityCoverageIdentity undeclared =
            ProducerCapabilityCoverageIdentity.Create(fixture.Domain);

        ProducerCapabilityPlanResult result =
            fixture.Validate(
                [fixture.RequireCount(fixture.CountAssociation)],
                [fixture.RowsProvision, fixture.CountProvision],
                [fixture.RowsCoverCount],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [fixture.RowsProvision.Identity],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            fixture.RowsProvision.Identity,
                            [undeclared]),
                    ]));

        var rejected =
            Assert.IsType<ProducerCapabilityPlanResult.Rejected>(result);
        Assert.Contains(
            rejected.Reasons,
            static rejection =>
                rejection.Reason
                == ProducerCapabilityPlanRejectionReason
                    .InvalidCoveringPath);
    }

    [Fact]
    public void ProducerCapabilityPlanRejectsInvalidStructureBeforeWork()
    {
        var fixture = new CapabilityFixture();
        ProducerCapabilityIdentity unknown =
            ProducerCapabilityIdentity.Create(fixture.Domain);
        ProducerCapabilityCompletionIdentity stronger =
            ProducerCapabilityCompletionIdentity.Create(fixture.Domain);
        QuerySpaceResourceIdentity foreignResource =
            QuerySpaceResourceIdentity.Create(fixture.ResourceDomain);
        ProducerCapabilityDomainIdentity foreignDomain =
            ProducerCapabilityDomainIdentity.Create();
        ProducerCapabilityScopeIdentity foreignScope =
            ProducerCapabilityScopeIdentity.Create(
                fixture.Domain,
                foreignResource);
        ProducerCapabilityParameterIdentity foreignParameters =
            ProducerCapabilityParameterIdentity.Create(foreignDomain);

        AssertRejected(
            fixture.Validate(
                [
                    new(
                        fixture.CountAssociation,
                        fixture.Resource,
                        fixture.Scope,
                        unknown,
                        fixture.Complete,
                        fixture.CountOutcome,
                        ProducerCapabilityProperties.ExactCardinality),
                ],
                [fixture.CountProvision],
                [],
                fixture.DirectCountCandidate),
            ProducerCapabilityPlanRejectionReason.CapabilityMismatch);
        AssertRejected(
            fixture.Validate(
                [
                    new(
                        fixture.CountAssociation,
                        fixture.Resource,
                        foreignScope,
                        fixture.Count,
                        fixture.Complete,
                        fixture.CountOutcome,
                        ProducerCapabilityProperties.ExactCardinality),
                ],
                [fixture.CountProvision],
                [],
                fixture.DirectCountCandidate),
            ProducerCapabilityPlanRejectionReason.ResourceMismatch);
        AssertRejected(
            fixture.Validate(
                [
                    new(
                        fixture.CountAssociation,
                        fixture.Resource,
                        fixture.Scope,
                        fixture.Count,
                        stronger,
                        fixture.CountOutcome,
                        ProducerCapabilityProperties.ExactCardinality),
                ],
                [fixture.CountProvision],
                [],
                fixture.DirectCountCandidate),
            ProducerCapabilityPlanRejectionReason
                .InsufficientCompletion);
        AssertRejected(
            fixture.Validate(
                [
                    new(
                        fixture.CountAssociation,
                        fixture.Resource,
                        fixture.Scope,
                        fixture.Count,
                        fixture.Complete,
                        fixture.CountOutcome,
                        ProducerCapabilityProperties.ExactCardinality,
                        foreignParameters),
                ],
                [fixture.CountProvision],
                [],
                fixture.DirectCountCandidate),
            ProducerCapabilityPlanRejectionReason
                .ProducerDomainMismatch);

        ProducerCapabilityProvisionIdentity first =
            ProducerCapabilityProvisionIdentity.Create(fixture.Domain);
        ProducerCapabilityProvisionIdentity second =
            ProducerCapabilityProvisionIdentity.Create(fixture.Domain);
        ProducerCapabilityProvisionDeclaration firstDeclaration =
            ProducerCapabilityProvisionDeclaration.Create(
                first,
                fixture.Scope,
                fixture.Count,
                fixture.Complete,
                fixture.CountOutcome,
                fixture.BorrowedResource,
                fixture.DetachedResult,
                ProducerCapabilityProperties.ExactCardinality,
                [second]);
        ProducerCapabilityProvisionDeclaration secondDeclaration =
            ProducerCapabilityProvisionDeclaration.Create(
                second,
                fixture.Scope,
                fixture.Rows,
                fixture.Complete,
                fixture.RowsOutcome,
                fixture.BorrowedResource,
                fixture.DetachedResult,
                dependencies: [first]);
        AssertRejected(
            fixture.Validate(
                [fixture.RequireCount(fixture.CountAssociation)],
                [firstDeclaration, secondDeclaration],
                [],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [first, second],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            first),
                    ])),
            ProducerCapabilityPlanRejectionReason.DependencyOrder);
    }

    [Fact]
    public void ProducerCapabilityCoveringPathKeepsOnlyPropertiesEveryStepEstablishes()
    {
        var fixture = new CapabilityFixture();
        ProducerCapabilityProvisionDeclaration inexactRows =
            ProducerCapabilityProvisionDeclaration.Create(
                ProducerCapabilityProvisionIdentity.Create(fixture.Domain),
                fixture.Scope,
                fixture.Rows,
                fixture.Complete,
                fixture.RowsOutcome,
                fixture.BorrowedResource,
                fixture.DetachedResult,
                ProducerCapabilityProperties.None);
        ProducerCapabilityPlanCandidate CountThroughRows(
            ProducerCapabilityProvisionDeclaration rows) =>
            ProducerCapabilityPlanCandidate.Create(
                fixture.Strategy,
                [rows.Identity],
                [
                    ProducerCapabilitySatisfactionCandidate.Create(
                        fixture.CountAssociation,
                        rows.Identity,
                        [fixture.RowsCoverCount.Identity]),
                ]);

        // An edge that preserves exact cardinality cannot create it from a
        // provision that lacks it.
        AssertRejected(
            fixture.Validate(
                [fixture.RequireCount(fixture.CountAssociation)],
                [inexactRows],
                [fixture.RowsCoverCount],
                CountThroughRows(inexactRows)),
            ProducerCapabilityPlanRejectionReason.RequiredPropertiesMissing);

        // An exact provision through an exact edge still satisfies Count.
        Accept(fixture.Validate(
            [fixture.RequireCount(fixture.CountAssociation)],
            [fixture.RowsProvision],
            [fixture.RowsCoverCount],
            CountThroughRows(fixture.RowsProvision)));
    }

    [Fact]
    public void ProducerCapabilityPlanChecksResourceOfSelectedProvisionsOnly()
    {
        var fixture = new CapabilityFixture();
        ProducerCapabilityScopeIdentity foreignScope =
            ProducerCapabilityScopeIdentity.Create(
                fixture.Domain,
                QuerySpaceResourceIdentity.Create(fixture.ResourceDomain));
        ProducerCapabilityProvisionDeclaration foreignProvision =
            ProducerCapabilityProvisionDeclaration.Create(
                ProducerCapabilityProvisionIdentity.Create(fixture.Domain),
                foreignScope,
                fixture.Count,
                fixture.Complete,
                fixture.CountOutcome,
                fixture.BorrowedResource,
                fixture.DetachedResult,
                ProducerCapabilityProperties.ExactCardinality);

        Accept(fixture.Validate(
            [fixture.RequireCount(fixture.CountAssociation)],
            [fixture.CountProvision, foreignProvision],
            [],
            fixture.DirectCountCandidate));
        AssertRejected(
            fixture.Validate(
                [fixture.RequireCount(fixture.CountAssociation)],
                [fixture.CountProvision, foreignProvision],
                [],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [fixture.CountProvision.Identity, foreignProvision.Identity],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            fixture.CountProvision.Identity),
                    ])),
            ProducerCapabilityPlanRejectionReason.ResourceMismatch);
    }

    [Fact]
    public void ProducerCapabilityPlanPreservesSingletonSpecialization()
    {
        var fixture = new CapabilityFixture();

        ProducerCapabilityPlan plan =
            Accept(fixture.Validate(
                [fixture.RequireCount(fixture.CountAssociation)],
                [fixture.RowsProvision, fixture.CountProvision],
                [fixture.RowsCoverCount],
                fixture.DirectCountCandidate));

        ProducerCapabilitySatisfaction satisfaction =
            Assert.Single(plan.Satisfactions);
        Assert.Same(
            fixture.CountProvision.Identity,
            satisfaction.Provision.Identity);
        Assert.Equal(
            ProducerCapabilitySatisfactionKind.Direct,
            satisfaction.Kind);
    }

    [Fact]
    public void ProducerCapabilityResultsPreserveTypedFailure()
    {
        var fixture = new CapabilityFixture();
        ProducerCapabilityPlan plan =
            Accept(fixture.Validate(
                [
                    fixture.RequireRows(fixture.RowsAssociation),
                    fixture.RequireCount(fixture.CountAssociation),
                ],
                [fixture.RowsProvision, fixture.CountProvision],
                [],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [
                        fixture.RowsProvision.Identity,
                        fixture.CountProvision.Identity,
                    ],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.RowsAssociation,
                            fixture.RowsProvision.Identity),
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            fixture.CountProvision.Identity),
                    ])));
        var failure = new CapabilityFailure("decode failed");

        ProducerCapabilityResultSetResult<string, CapabilityFailure> result =
            ProducerCapabilityResultSetValidator.Validate<
                string,
                CapabilityFailure>(
                plan,
                [
                    new(
                        fixture.RowsAssociation,
                        new ProducerCapabilityOutcome<
                            string,
                            CapabilityFailure>.Completed(
                                "rows",
                                fixture.Complete)),
                    new(
                        fixture.CountAssociation,
                        new ProducerCapabilityOutcome<
                            string,
                            CapabilityFailure>.ProvisionFailed(
                                failure,
                                fixture.CountProvision.Identity)),
                ]);

        var accepted = Assert.IsType<
            ProducerCapabilityResultSetResult<
                string,
                CapabilityFailure>.Accepted>(result);
        Assert.Equal(
            [fixture.RowsAssociation, fixture.CountAssociation],
            accepted.ResultSet.Results.Select(
                static association =>
                    association.Satisfaction.Requirement.Association));
        var failed = Assert.IsType<
            ProducerCapabilityOutcome<
                string,
                CapabilityFailure>.ProvisionFailed>(
                    accepted.ResultSet.Results[1].Outcome);
        Assert.Same(failure, failed.Failure);

        ProducerCapabilityPlan sharedPlan =
            Accept(fixture.Validate(
                [
                    fixture.RequireRows(fixture.RowsAssociation),
                    fixture.RequireCount(fixture.CountAssociation),
                ],
                [fixture.RowsProvision, fixture.CountProvision],
                [fixture.RowsCoverCount],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [fixture.RowsProvision.Identity],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.RowsAssociation,
                            fixture.RowsProvision.Identity),
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            fixture.RowsProvision.Identity,
                            [fixture.RowsCoverCount.Identity]),
                    ])));
        ProducerCapabilityResultSetResult<
            string,
            CapabilityFailure> inconsistent =
                ProducerCapabilityResultSetValidator.Validate<
                    string,
                    CapabilityFailure>(
                    sharedPlan,
                    [
                        new(
                            fixture.RowsAssociation,
                            new ProducerCapabilityOutcome<
                                string,
                                CapabilityFailure>.Completed(
                                    "rows",
                                    fixture.Complete)),
                        new(
                            fixture.CountAssociation,
                            new ProducerCapabilityOutcome<
                                string,
                                CapabilityFailure>.ProvisionFailed(
                                    failure,
                                    fixture.RowsProvision.Identity)),
                    ]);
        var rejected = Assert.IsType<
            ProducerCapabilityResultSetResult<
                string,
                CapabilityFailure>.Rejected>(inconsistent);
        Assert.Contains(
            rejected.Reasons,
            static reason =>
                reason.Reason
                == ProducerCapabilityResultRejectionReason
                    .SharedProvisionFailureMismatch);

        ProducerCapabilityResultSetResult<
            string,
            CapabilityFailure> consistent =
                ProducerCapabilityResultSetValidator.Validate<
                    string,
                    CapabilityFailure>(
                    sharedPlan,
                    [
                        new(
                            fixture.RowsAssociation,
                            new ProducerCapabilityOutcome<
                                string,
                                CapabilityFailure>.ProvisionFailed(
                                    failure,
                                    fixture.RowsProvision.Identity)),
                        new(
                            fixture.CountAssociation,
                            new ProducerCapabilityOutcome<
                                string,
                                CapabilityFailure>.ProvisionFailed(
                                    failure,
                                    fixture.RowsProvision.Identity)),
                    ]);
        Assert.IsType<
            ProducerCapabilityResultSetResult<
                string,
                CapabilityFailure>.Accepted>(consistent);
    }

    [Fact]
    public void ProducerCapabilityResultsRouteSeveralProvisionFailures()
    {
        var fixture = new CapabilityFixture();
        QuerySpaceRequestAssociationIdentity combinedAssociation =
            QuerySpaceRequestAssociationIdentity.Create();
        ProducerCapabilityProvisionDeclaration combinedProvision =
            ProducerCapabilityProvisionDeclaration.Create(
                ProducerCapabilityProvisionIdentity.Create(fixture.Domain),
                fixture.Scope,
                fixture.Count,
                fixture.Complete,
                fixture.CountOutcome,
                fixture.BorrowedResource,
                fixture.DetachedResult,
                ProducerCapabilityProperties.ExactCardinality,
                [
                    fixture.RowsProvision.Identity,
                    fixture.CountProvision.Identity,
                ]);
        ProducerCapabilityPlan plan =
            Accept(fixture.Validate(
                [
                    fixture.RequireRows(fixture.RowsAssociation),
                    fixture.RequireCount(fixture.CountAssociation),
                    fixture.RequireCount(combinedAssociation),
                ],
                [
                    fixture.RowsProvision,
                    fixture.CountProvision,
                    combinedProvision,
                ],
                [],
                ProducerCapabilityPlanCandidate.Create(
                    fixture.Strategy,
                    [
                        fixture.RowsProvision.Identity,
                        fixture.CountProvision.Identity,
                        combinedProvision.Identity,
                    ],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.RowsAssociation,
                            fixture.RowsProvision.Identity),
                        ProducerCapabilitySatisfactionCandidate.Create(
                            fixture.CountAssociation,
                            fixture.CountProvision.Identity),
                        ProducerCapabilitySatisfactionCandidate.Create(
                            combinedAssociation,
                            combinedProvision.Identity),
                    ])));
        var failure = new CapabilityFailure("decode failed");

        ProducerCapabilityResultSetResult<string, CapabilityFailure> Validate(
            ProducerCapabilityOutcome<string, CapabilityFailure> combined) =>
            ProducerCapabilityResultSetValidator.Validate<
                string,
                CapabilityFailure>(
                plan,
                [
                    new(
                        fixture.RowsAssociation,
                        new ProducerCapabilityOutcome<
                            string,
                            CapabilityFailure>.ProvisionFailed(
                                failure,
                                fixture.RowsProvision.Identity)),
                    new(
                        fixture.CountAssociation,
                        new ProducerCapabilityOutcome<
                            string,
                            CapabilityFailure>.ProvisionFailed(
                                failure,
                                fixture.CountProvision.Identity)),
                    new(combinedAssociation, combined),
                ]);

        // Two independent provisions fail; their common dependent may name
        // either one.
        foreach (ProducerCapabilityProvisionIdentity named in new[]
        {
            fixture.RowsProvision.Identity,
            fixture.CountProvision.Identity,
        })
        {
            Assert.IsType<
                ProducerCapabilityResultSetResult<
                    string,
                    CapabilityFailure>.Accepted>(
                Validate(
                    new ProducerCapabilityOutcome<
                        string,
                        CapabilityFailure>.ProvisionFailed(failure, named)));
        }

        // A dependent of a failed provision never reports success, even if it
        // settled first.
        var rejected = Assert.IsType<
            ProducerCapabilityResultSetResult<
                string,
                CapabilityFailure>.Rejected>(
            Validate(
                new ProducerCapabilityOutcome<
                    string,
                    CapabilityFailure>.Completed("2", fixture.Complete)));
        ProducerCapabilityResultRejection reason = Assert.Single(
            rejected.Reasons);
        Assert.Same(combinedAssociation, reason.Association);
        Assert.Equal(
            ProducerCapabilityResultRejectionReason
                .SharedProvisionFailureMismatch,
            reason.Reason);
    }

    [Fact]
    public void ProducerCapabilityPlanIsResourceFree()
    {
        AssertResourceFree(
            typeof(ProducerCapabilityPlan),
            []);
    }

    static ProducerCapabilityPlan Accept(
        ProducerCapabilityPlanResult result) =>
        Assert.IsType<ProducerCapabilityPlanResult.Accepted>(result).Plan;

    static void AssertRejected(
        ProducerCapabilityPlanResult result,
        ProducerCapabilityPlanRejectionReason reason)
    {
        var rejected =
            Assert.IsType<ProducerCapabilityPlanResult.Rejected>(result);
        Assert.Contains(
            rejected.Reasons,
            rejection => rejection.Reason == reason);
    }

    static void AssertResourceFree(Type type, HashSet<Type> visited)
    {
        Assert.False(typeof(Delegate).IsAssignableFrom(type));
        Assert.False(typeof(IDisposable).IsAssignableFrom(type));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
        Assert.NotEqual(typeof(CancellationToken), type);
        Assert.NotEqual(typeof(object), type);

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
                AssertResourceFree(argument, visited);
        }
        if (type.Assembly != typeof(ProducerCapabilityPlan).Assembly
            || !visited.Add(type))
        {
            return;
        }

        foreach (FieldInfo field in type.GetFields(
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic))
        {
            AssertResourceFree(field.FieldType, visited);
        }
    }

    sealed record CapabilityFailure(string Message);

    sealed class CapabilityFixture
    {
        internal CapabilityFixture()
        {
            Domain = ProducerCapabilityDomainIdentity.Create();
            ResourceDomain = QuerySpaceResourceDomainIdentity.Create();
            Resource = QuerySpaceResourceIdentity.Create(ResourceDomain);
            Scope = ProducerCapabilityScopeIdentity.Create(
                Domain,
                Resource);
            Rows = ProducerCapabilityIdentity.Create(Domain);
            Count = ProducerCapabilityIdentity.Create(Domain);
            RowDemand =
                ProducerCapabilityParameterIdentity.Create(Domain);
            Complete =
                ProducerCapabilityCompletionIdentity.Create(Domain);
            RowsOutcome =
                ProducerCapabilityOutcomeIdentity.Create(Domain);
            CountOutcome =
                ProducerCapabilityOutcomeIdentity.Create(Domain);
            BorrowedResource =
                ProducerCapabilityResourceLifetimeIdentity.Create(Domain);
            DetachedResult =
                ProducerCapabilityResultLifetimeIdentity.Create(Domain);
            Strategy =
                ProducerCapabilityStrategyIdentity.Create(Domain);
            RowsProvision =
                ProducerCapabilityProvisionDeclaration.Create(
                    ProducerCapabilityProvisionIdentity.Create(Domain),
                    Scope,
                    Rows,
                    Complete,
                    RowsOutcome,
                    BorrowedResource,
                    DetachedResult,
                    ProducerCapabilityProperties.ExactCardinality);
            CountProvision =
                ProducerCapabilityProvisionDeclaration.Create(
                    ProducerCapabilityProvisionIdentity.Create(Domain),
                    Scope,
                    Count,
                    Complete,
                    CountOutcome,
                    BorrowedResource,
                    DetachedResult,
                    ProducerCapabilityProperties.ExactCardinality);
            RowsCoverCount =
                ProducerCapabilityCoverageDeclaration.Create(
                    ProducerCapabilityCoverageIdentity.Create(Domain),
                    Scope,
                    Rows,
                    Complete,
                    RowsOutcome,
                    Scope,
                    Count,
                    Complete,
                    CountOutcome,
                    ProducerCapabilityProjectionIdentity.Create(Domain),
                    ProducerCapabilityProperties.ExactCardinality);
            DirectCountCandidate =
                ProducerCapabilityPlanCandidate.Create(
                    Strategy,
                    [CountProvision.Identity],
                    [
                        ProducerCapabilitySatisfactionCandidate.Create(
                            CountAssociation,
                            CountProvision.Identity),
                    ]);
        }

        internal ProducerCapabilityDomainIdentity Domain { get; }

        internal QuerySpaceResourceDomainIdentity ResourceDomain { get; }

        internal QuerySpaceResourceIdentity Resource { get; }

        internal ProducerCapabilityScopeIdentity Scope { get; }

        internal ProducerCapabilityIdentity Rows { get; }

        internal ProducerCapabilityIdentity Count { get; }

        internal ProducerCapabilityParameterIdentity RowDemand { get; }

        internal ProducerCapabilityCompletionIdentity Complete
        { get; }

        internal ProducerCapabilityOutcomeIdentity RowsOutcome
        { get; }

        internal ProducerCapabilityOutcomeIdentity CountOutcome
        { get; }

        internal ProducerCapabilityResourceLifetimeIdentity BorrowedResource
        { get; }

        internal ProducerCapabilityResultLifetimeIdentity DetachedResult
        { get; }

        internal ProducerCapabilityStrategyIdentity Strategy
        { get; }

        internal QuerySpaceRequestAssociationIdentity RowsAssociation
        { get; } = QuerySpaceRequestAssociationIdentity.Create();

        internal QuerySpaceRequestAssociationIdentity CountAssociation
        { get; } = QuerySpaceRequestAssociationIdentity.Create();

        internal ProducerCapabilityProvisionDeclaration RowsProvision
        { get; }

        internal ProducerCapabilityProvisionDeclaration CountProvision
        { get; }

        internal ProducerCapabilityCoverageDeclaration RowsCoverCount
        { get; }

        internal ProducerCapabilityPlanCandidate DirectCountCandidate
        { get; }

        internal ProducerCapabilityRequirementCandidate RequireRows(
            QuerySpaceRequestAssociationIdentity association,
            ProducerCapabilityParameterIdentity? parameters = null) =>
            new(
                association,
                Resource,
                Scope,
                Rows,
                Complete,
                RowsOutcome,
                Parameters: parameters);

        internal ProducerCapabilityRequirementCandidate RequireCount(
            QuerySpaceRequestAssociationIdentity association) =>
            new(
                association,
                Resource,
                Scope,
                Count,
                Complete,
                CountOutcome,
                ProducerCapabilityProperties.ExactCardinality);

        internal ProducerCapabilityPlanResult Validate(
            IReadOnlyList<ProducerCapabilityRequirementCandidate?>
                requirements,
            IReadOnlyList<ProducerCapabilityProvisionDeclaration?>
                declarations,
            IReadOnlyList<ProducerCapabilityCoverageDeclaration?>
                coverages,
            ProducerCapabilityPlanCandidate candidate) =>
            ProducerCapabilityPlanValidator.Validate(
                Domain,
                requirements,
                declarations,
                coverages,
                candidate);
    }
}
