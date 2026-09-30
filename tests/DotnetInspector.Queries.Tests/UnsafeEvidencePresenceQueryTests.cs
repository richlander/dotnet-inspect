using DotnetInspector.Queries;
using ILInspector.Analysis.Planning;
using QuerySpace.Composition;

namespace DotnetInspector.Queries.Tests;

public sealed class UnsafeEvidencePresenceQueryTests
{
    [Fact]
    public void OwnerRequestClosesTheProducerWithExists()
    {
        QuerySpaceRequest request =
            UnsafeEvidencePresenceQuery.CreateRequest();

        Assert.Equal(
            UnsafeEvidencePresenceQuery.QuerySpaceIdentity,
            request.QuerySpace);
        Assert.Equal(
            QuerySpaceTerminalRequirement.Exists,
            request.Terminal);
        Assert.Equal(
            UnsafeEvidencePresenceQuery.ResultContract,
            request.ResultContract);
        Assert.Equal(
            [UnsafeEvidencePresenceQuery.MethodDefinitionsRowSet],
            request.ParticipatingRowSets);
        Assert.Empty(request.RowIntents);

        var accepted =
            Assert.IsType<UnsafeEvidencePresenceRequestResolution.Accepted>(
                UnsafeEvidencePresenceQuery.ResolveRequest(request));
        Assert.Equal(request.Operation, accepted.Plan.Intent);
        Assert.Equal(
            ProducerTerminal.Exists,
            accepted.Work.TerminalOf(
                UnsafeEvidencePresenceProducer.Instance));
    }

    [Fact]
    public void QuerySpaceAdvertisesOnlyTheSupportedClosing()
    {
        QuerySpaceDescriptor descriptor =
            UnsafeEvidencePresenceQuery.QuerySpace.Descriptor;

        Assert.Equal(
            [QuerySpaceTerminalRequirement.Exists],
            descriptor.Terminals);
        Assert.Equal(
            UnsafeEvidencePresenceQuery
                .MethodDefinitionsRowScopeIdentity,
            Assert.Single(descriptor.RowScopes).Identity);
        Assert.True(
            descriptor.TryGetResultContract(
                QuerySpaceTerminalRequirement.Exists,
                out QuerySpaceResultContractDescriptor? contract));
        Assert.Equal(
            UnsafeEvidencePresenceQuery.ResultContract,
            contract.Identity);
        Assert.DoesNotContain(
            QuerySpaceTerminalRequirement.Rows,
            descriptor.Terminals);
        Assert.DoesNotContain(
            QuerySpaceTerminalRequirement.Count,
            descriptor.Terminals);
    }
}
