using DotnetInspector.DocumentationHouse;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries.Tests;

public sealed class TypeInspectionMembersQueryTests
{
    [Fact]
    public void QuerySpaceDeclaresReceiverFacetAndPeerTerminals()
    {
        QuerySpaceDescriptor descriptor =
            TypeInspectionMembersQuery.QuerySpace.Descriptor;

        Assert.Equal(
            TypeInspectionMembersQuery.QuerySpaceIdentity,
            descriptor.Identity);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            descriptor.Terminals);
        QuerySpaceRowScopeDescriptor scope =
            Assert.Single(descriptor.RowScopes);
        QuerySpaceRowFacetDescriptor receiver =
            Assert.Single(scope.Facets);
        Assert.Equal(
            TypeInspectionMembersQuery.ReceiverTermKey,
            receiver.Key);
        Assert.Equal(
            [
                PortableQueryOperator.Equal,
                PortableQueryOperator.NotEqual,
            ],
            receiver.Operators);
        Assert.Equal(
            [
                TypeInspectionMembersQuery.StaticReceiverValue,
                TypeInspectionMembersQuery.ThisReceiverValue,
                TypeInspectionMembersQuery.ExtensionReceiverValue,
            ],
            receiver.Values);
        Assert.False(descriptor.AcceptsContinuation);
        Assert.Equal(
            TypeInspectionMembersQuery.RowsResultContractIdentity,
            descriptor.ResultContracts.Single(
                contract => contract.Terminal
                    is QuerySpaceTerminalRequirement.Rows)
                .Identity);
        Assert.Equal(
            TypeInspectionMembersQuery.CountResultContractIdentity,
            descriptor.ResultContracts.Single(
                contract => contract.Terminal
                    is QuerySpaceTerminalRequirement.Count)
                .Identity);
    }

    [Fact]
    public void ResolveRequestBindsReceiverEqualityAndExclusion()
    {
        foreach (bool exclude in new[] { false, true })
        {
            QuerySpaceRequest request =
                TypeInspectionMembersQuery.CreateRequest(
                    QuerySpaceTerminalRequirement.Rows,
                    TypeInspectionMembersQuery.CreateRowIntent(
                        TypeMemberReceiver.Extension,
                        exclude));

            TypeMembersQueryPlan plan =
                Assert.IsType<TypeMembersQueryRequestResult.Accepted>(
                    TypeInspectionMembersQuery.ResolveRequest(
                        request,
                        TestContext.Current.CancellationToken))
                .Plan;

            Assert.Equal(request, plan.Request);
            Assert.Single(plan.RowIntent.Terms);
            Assert.Equal(
                exclude
                    ? PortableQueryOperator.NotEqual
                    : PortableQueryOperator.Equal,
                plan.RowIntent.Terms[0].Operator);
        }
    }

    [Fact]
    public void ResolveRequestRejectsUnknownReceiverValue()
    {
        PortableQueryIntent intent = PortableQueryIntent.Create(
            [
                new(
                    TypeInspectionMembersQuery.ReceiverTermKey,
                    PortableQueryOperator.Equal,
                    "unknown"),
            ],
            [],
            [],
            []);
        QuerySpaceRequest request =
            TypeInspectionMembersQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Rows,
                intent);

        TypeMembersQueryIntentRejection failure =
            Assert.IsType<TypeMembersQueryRequestResult.IntentRejected>(
                TypeInspectionMembersQuery.ResolveRequest(
                    request,
                    TestContext.Current.CancellationToken))
            .Failure;

        Assert.Equal(RowQueryFailureReason.InvalidValue, failure.Reason);
        Assert.Equal(
            RowQueryOperationKind.Predicate,
            failure.OperationKind);
    }

    [Fact]
    public void ResolveRequestRejectsForeignStructureAndResultContract()
    {
        QuerySpaceRequest foreign =
            DocumentationQuery.CreateRequest(
                DocumentationDemand.CompiledXml);
        Assert.Equal(
            TypeMembersQueryRequestRejectionKind.QuerySpaceMismatch,
            Assert.IsType<TypeMembersQueryRequestResult.Rejected>(
                TypeInspectionMembersQuery.ResolveRequest(
                    foreign,
                    TestContext.Current.CancellationToken))
            .Kind);

        QuerySpaceBinding foreignRowsBinding = QuerySpaceBinding.Create(
            TypeInspectionMembersQuery.QuerySpaceIdentity,
            DocumentationQuery.OperationRoute,
            [DocumentationQuery.DocumentationRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    TypeInspectionMembersQuery
                        .RowsResultContractIdentity),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    TypeInspectionMembersQuery
                        .CountResultContractIdentity),
            ]);
        QuerySpaceRequest foreignRows = QuerySpaceRequest.Create(
            foreignRowsBinding.Descriptor,
            DocumentationQuery.CreateIntent(
                DocumentationDemand.CompiledXml),
            [DocumentationQuery.DocumentationRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            TypeMembersQueryRequestRejectionKind
                .ParticipatingRowSetsMismatch,
            Assert.IsType<TypeMembersQueryRequestResult.Rejected>(
                TypeInspectionMembersQuery.ResolveRequest(
                    foreignRows,
                    TestContext.Current.CancellationToken))
            .Kind);

        QuerySpaceBinding noAssociationBinding = QuerySpaceBinding.Create(
            TypeInspectionMembersQuery.QuerySpaceIdentity,
            TypeInspectionMembersQuery.OperationRoute,
            [TypeInspectionMembersQuery.MembersRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    TypeInspectionMembersQuery
                        .RowsResultContractIdentity),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    TypeInspectionMembersQuery
                        .CountResultContractIdentity),
            ]);
        QuerySpaceRequest noAssociation = QuerySpaceRequest.Create(
            noAssociationBinding.Descriptor,
            TypeInspectionMembersQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Rows).Operation,
            [TypeInspectionMembersQuery.MembersRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            TypeMembersQueryRequestRejectionKind
                .RowIntentAssociationMismatch,
            Assert.IsType<TypeMembersQueryRequestResult.Rejected>(
                TypeInspectionMembersQuery.ResolveRequest(
                    noAssociation,
                    TestContext.Current.CancellationToken))
            .Kind);

        QuerySpaceBinding wrongResultBinding = QuerySpaceBinding.Create(
            TypeInspectionMembersQuery.QuerySpaceIdentity,
            TypeInspectionMembersQuery.OperationRoute,
            [TypeInspectionMembersQuery.MembersRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    "type-inspection/members/foreign-rows/v1"),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    TypeInspectionMembersQuery
                        .CountResultContractIdentity),
            ]);
        QuerySpaceRequest wrongResult = QuerySpaceRequest.Create(
            wrongResultBinding.Descriptor,
            TypeInspectionMembersQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Rows).Operation,
            [TypeInspectionMembersQuery.MembersRowSet],
            [
                new(
                    TypeInspectionMembersQuery.MembersRowScopeIdentity,
                    PortableQueryIntent.Create([], [], [], []),
                    [TypeInspectionMembersQuery.MembersRowSet]),
            ],
            QuerySpaceTerminalRequirement.Rows);
        Assert.Equal(
            TypeMembersQueryRequestRejectionKind.ResultContractMismatch,
            Assert.IsType<TypeMembersQueryRequestResult.Rejected>(
                TypeInspectionMembersQuery.ResolveRequest(
                    wrongResult,
                    TestContext.Current.CancellationToken))
            .Kind);
    }
}
