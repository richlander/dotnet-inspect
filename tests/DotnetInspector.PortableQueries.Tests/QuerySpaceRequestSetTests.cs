using QuerySpace.Composition;
using QuerySpace.Operations;

namespace DotnetInspector.PortableQueries.Tests;

public sealed partial class QueryOperationInfrastructureGateTests
{
    [Fact]
    public void RequestSetRejectsInvalidAssociationsWithoutWork()
    {
        QuerySpaceRequest request = CreateRequestSetRequest();
        QuerySpaceResourceDomainIdentity domain =
            QuerySpaceResourceDomainIdentity.Create();
        QuerySpaceResourceDomainIdentity foreignDomain =
            QuerySpaceResourceDomainIdentity.Create();
        QuerySpaceResourceIdentity resource =
            QuerySpaceResourceIdentity.Create(domain);
        QuerySpaceSourceBindingIdentity foreignSource =
            QuerySpaceSourceBindingIdentity.Create(foreignDomain);
        QuerySpaceRequestAssociationIdentity duplicate =
            QuerySpaceRequestAssociationIdentity.Create();
        var resolved = new ReferenceRequest(
            request,
            request.ResultContract);

        QuerySpaceRequestSetPlanResult<ReferenceRequest> result =
            QuerySpaceRequestSetPlanner.Plan<ReferenceRequest>(
                [
                    new(duplicate, resource, foreignSource, resolved),
                    new(duplicate, null, foreignSource, resolved),
                ]);

        var rejected = Assert.IsType<
            QuerySpaceRequestSetPlanResult<ReferenceRequest>.Rejected>(
                result);
        Assert.Contains(
            rejected.Reasons,
            reason =>
                reason.Reason
                == QuerySpaceRequestSetRejectionReason
                    .ResourceSourceDomainMismatch);
        Assert.Contains(
            rejected.Reasons,
            reason =>
                reason.Reason
                == QuerySpaceRequestSetRejectionReason
                    .DuplicateAssociationIdentity);
        Assert.Contains(
            rejected.Reasons,
            reason =>
                reason.Reason
                == QuerySpaceRequestSetRejectionReason
                    .MissingResourceIdentity);
    }

    [Fact]
    public void CoveringReadRequiresOwnerIdentityAndAcceptedCompletion()
    {
        QuerySpaceRequest request = CreateRequestSetRequest();
        var resolved = new ReferenceRequest(
            request,
            request.ResultContract);
        QuerySpaceResourceDomainIdentity domain =
            QuerySpaceResourceDomainIdentity.Create();
        QuerySpaceResourceIdentity firstResource =
            QuerySpaceResourceIdentity.Create(domain);
        QuerySpaceResourceIdentity secondResource =
            QuerySpaceResourceIdentity.Create(domain);
        QuerySpaceSourceBindingIdentity source =
            QuerySpaceSourceBindingIdentity.Create(domain);
        QuerySpaceRequestAssociationIdentity first =
            QuerySpaceRequestAssociationIdentity.Create();
        QuerySpaceRequestAssociationIdentity second =
            QuerySpaceRequestAssociationIdentity.Create();
        QuerySpaceRequestAssociationIdentity third =
            QuerySpaceRequestAssociationIdentity.Create();

        QuerySpaceRequestSetPlanResult<ReferenceRequest> result =
            QuerySpaceRequestSetPlanner.Plan<ReferenceRequest>(
                [
                    new(first, firstResource, source, resolved),
                    new(second, firstResource, source, resolved),
                    new(third, secondResource, source, resolved),
                ]);

        QuerySpaceRequestSetPlan<ReferenceRequest> plan =
            Assert.IsType<
                    QuerySpaceRequestSetPlanResult<
                        ReferenceRequest>.Accepted>(result)
                .Plan;
        Assert.Equal([first, second, third], plan.Associations
            .Select(static association => association.Association));
        Assert.Equal(2, plan.Groups.Length);
        Assert.Equal(
            [first, second],
            plan.Groups[0].Associations.Select(
                static association => association.Association));
        Assert.Equal(
            [third],
            plan.Groups[1].Associations.Select(
                static association => association.Association));
    }

    [Fact]
    public void RequestSetRejectsResultContractMismatch()
    {
        QuerySpaceRequest request = CreateRequestSetRequest();
        QuerySpaceResourceDomainIdentity domain =
            QuerySpaceResourceDomainIdentity.Create();

        QuerySpaceRequestSetPlanResult<ReferenceRequest> result =
            QuerySpaceRequestSetPlanner.Plan<ReferenceRequest>(
                [
                    new(
                        QuerySpaceRequestAssociationIdentity.Create(),
                        QuerySpaceResourceIdentity.Create(domain),
                        QuerySpaceSourceBindingIdentity.Create(domain),
                        new(request, "foreign-result-contract")),
                ]);

        var rejected = Assert.IsType<
            QuerySpaceRequestSetPlanResult<ReferenceRequest>.Rejected>(
                result);
        Assert.Equal(
            [QuerySpaceRequestSetRejectionReason.ResultContractMismatch],
            rejected.Reasons.Select(static reason => reason.Reason));
    }

    static QuerySpaceRequest CreateRequestSetRequest()
    {
        QueryOperationRoute<TestPredicate, TestPlan> route =
            CreateRoute(
                CreateOperation(new TestVocabulary()),
                FullProfile);
        QuerySpaceDescriptor descriptor =
            QuerySpaceDescriptor.Create(
                "test.request-set",
                route,
                [CreateRowScope()],
                [QuerySpaceTerminalRequirement.Count],
                acceptsContinuation: false,
                [
                    new(
                        QuerySpaceTerminalRequirement.Count,
                        "test.request-set/count/v1"),
                ]);
        return QuerySpaceRequest.Create(
            descriptor,
            PortableQueryIntent.Empty,
            [ResultsRowSet],
            [],
            QuerySpaceTerminalRequirement.Count);
    }

    sealed record ReferenceRequest(
        QuerySpaceRequest StructuralRequest,
        string? ResultContract)
        : IQuerySpaceRequestSetRequest;
}
