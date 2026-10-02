using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using QuerySpace.Composition;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeDeclaredMethodPopulationInspectionOperationTests
{
    [Fact]
    public void QuerySpace_UsesDistinctCountAndRowsContracts()
    {
        QuerySpaceRequest count =
            TypeDeclaredMethodPopulationQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Count);
        QuerySpaceRequest rows =
            TypeDeclaredMethodPopulationQuery.CreateRequest(
                QuerySpaceTerminalRequirement.Rows);

        Assert.Equal(
            TypeDeclaredMethodPopulationQuery.CountResultContract,
            count.ResultContract);
        Assert.Equal(
            TypeDeclaredMethodPopulationQuery.RowsResultContract,
            rows.ResultContract);
        Assert.IsType<
            TypeDeclaredMethodPopulationQueryResult.Accepted>(
                TypeDeclaredMethodPopulationQuery.ResolveRequest(
                    count,
                    TestContext.Current.CancellationToken));
        Assert.IsType<
            TypeDeclaredMethodPopulationQueryResult.Accepted>(
                TypeDeclaredMethodPopulationQuery.ResolveRequest(
                    rows,
                    TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task
        RealSystemTextJson_CountAndRowsShareCardinalityAndExposeWork()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            MetadataTypeDefinitionName type =
                Name("System.Text.Json", "JsonSerializer");
            TypeDeclaredMethodPopulationOutcome.Counted counted =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Counted>(
                        Execute(
                            group,
                            participant,
                            type,
                            QuerySpaceTerminalRequirement.Count).Content);
            TypeDeclaredMethodPopulationOutcome.Read read =
                Assert.IsType<TypeDeclaredMethodPopulationOutcome.Read>(
                    Execute(
                        group,
                        participant,
                        type,
                        QuerySpaceTerminalRequirement.Rows).Content);

            Assert.Equal(counted.Count, read.Count);
            Assert.Equal(counted.Count, read.Rows.Length);
            Assert.True(counted.Count > 0);
            Assert.Equal(0, counted.Receipt.MethodDefinitionHandlesVisited);
            Assert.Equal(0, counted.Receipt.MethodDefinitionRowsRead);
            Assert.Equal(0, counted.Receipt.MethodNamesDecoded);
            Assert.Equal(0, counted.Receipt.MethodSignaturesDecoded);
            Assert.Equal(0, counted.Receipt.MethodAttributesDecoded);
            Assert.Equal(0, counted.Receipt.ProjectedRows);
            Assert.Equal(
                read.Count,
                read.Receipt.MethodDefinitionHandlesVisited);
            Assert.Equal(read.Count, read.Receipt.ProjectedRows);
            Assert.Equal(0, read.Receipt.MethodDefinitionRowsRead);
            Assert.Equal(0, read.Receipt.MethodNamesDecoded);
            Assert.Equal(0, read.Receipt.MethodSignaturesDecoded);
            Assert.Equal(0, read.Receipt.MethodAttributesDecoded);
            Assert.Equal(counted.Binding, read.Binding);

            var envelope =
                new InspectionEnvelope<
                    TypeDeclaredMethodPopulationOutcome>(
                        counted,
                        new InspectionShare.NonProjectable(
                            "test",
                            "test"));
            string json = JsonSerializer.Serialize(
                envelope,
                TypeDeclaredMethodPopulationInspectionJsonContext.Default
                    .InspectionEnvelopeTypeDeclaredMethodPopulationOutcome);
            InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>?
                roundTrip =
                    JsonSerializer.Deserialize(
                        json,
                        TypeDeclaredMethodPopulationInspectionJsonContext
                            .Default
                            .InspectionEnvelopeTypeDeclaredMethodPopulationOutcome);
            TypeDeclaredMethodPopulationOutcome.Counted
                roundTrippedCounted =
                    Assert.IsType<
                        TypeDeclaredMethodPopulationOutcome.Counted>(
                            roundTrip?.Content);
            Assert.Equal(counted.Count, roundTrippedCounted.Count);
            Assert.Equal(counted.Binding, roundTrippedCounted.Binding);
            Assert.Equal(counted.Receipt, roundTrippedCounted.Receipt);
        }
    }

    [Fact]
    public async Task BoundedRows_IsIncompleteBeforeMethodTraversal()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            TypeDeclaredMethodPopulationOutcome.Incomplete incomplete =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Incomplete>(
                        Execute(
                            group,
                            participant,
                            Name(
                                "System.Text.Json",
                                "JsonSerializer"),
                            QuerySpaceTerminalRequirement.Rows,
                            maximumRows: 1).Content);

            Assert.True(incomplete.Count > incomplete.Limit);
            Assert.Equal(1, incomplete.Limit);
            Assert.NotNull(incomplete.Binding);
            Assert.NotNull(incomplete.Receipt);
            Assert.Equal(
                0,
                incomplete.Receipt.MethodDefinitionHandlesVisited);
            Assert.Equal(0, incomplete.Receipt.ProjectedRows);
        }
    }

    [Fact]
    public async Task MissingType_RemainsVisible()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            TypeDeclaredMethodPopulationOutcome.Rejected rejected =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Rejected>(
                        Execute(
                            group,
                            participant,
                            Name(
                                "System.Text.Json",
                                "DefinitelyMissing"),
                            QuerySpaceTerminalRequirement.Count).Content);

            Assert.Equal(
                TypeDeclaredMethodPopulationRejection.TypeMissing,
                rejected.Reason);
            Assert.NotNull(rejected.Subject);
        }
    }

    [Fact]
    public async Task ForeignQuerySpace_IsRejectedBeforeInspection()
    {
        await using var workspace = new InspectionWorkspace();
        (
            AssemblyContextGroup group,
            AssemblyContextParticipant participant) =
                CreateGroup(workspace);
        using (group)
        {
            QuerySpaceRequest foreign =
                MemberOverloadPopulationQuery.CreateRequest(
                    MemberOverloadAccessibilityFilter.Public,
                    MemberOverloadReceiverFilter.All,
                    includeHidden: false,
                    MemberOverloadOrdering.Metadata,
                    QuerySpaceTerminalRequirement.Count);
            var request =
                new TypeDeclaredMethodPopulationInspectionRequest(
                    group,
                    participant,
                    Name(
                        "System.Text.Json",
                        "JsonSerializer"),
                    foreign);

            TypeDeclaredMethodPopulationOutcome.Rejected rejected =
                Assert.IsType<
                    TypeDeclaredMethodPopulationOutcome.Rejected>(
                        TypeDeclaredMethodPopulationInspectionOperation
                            .Execute(
                                request,
                                TestContext.Current.CancellationToken)
                            .Content);

            Assert.Equal(
                TypeDeclaredMethodPopulationRejection.QueryRejected,
                rejected.Reason);
            Assert.Null(rejected.Subject);
        }
    }

    [Fact]
    public async Task ParticipantOpenFailure_RemainsVisible()
    {
        await using var workspace = new InspectionWorkspace();
        string path = SystemTextJsonPath();
        AssemblyReferenceIdentity identity;
        using (var stream = File.OpenRead(path))
        using (var reader = new PEReader(stream))
        {
            identity = AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        }
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => throw new IOException("Participant open failed."),
                AssemblyResolutionProvenance.Local(
                    "declared-method rejected participant"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        TypeDeclaredMethodPopulationOutcome.Rejected rejected =
            Assert.IsType<
                TypeDeclaredMethodPopulationOutcome.Rejected>(
                    Execute(
                        group,
                        participant,
                        Name("System.Text.Json", "JsonSerializer"),
                        QuerySpaceTerminalRequirement.Count).Content);

        Assert.Equal(
            TypeDeclaredMethodPopulationRejection.ParticipantRejected,
            rejected.Reason);
        Assert.NotNull(rejected.Subject);
    }

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Execute(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionName type,
            QuerySpaceTerminalRequirement terminal,
            int maximumRows = int.MaxValue) =>
        TypeDeclaredMethodPopulationInspectionOperation.Execute(
            new(
                group,
                participant,
                type,
                TypeDeclaredMethodPopulationQuery.CreateRequest(terminal),
                maximumRows),
            TestContext.Current.CancellationToken);

    private static (
        AssemblyContextGroup Group,
        AssemblyContextParticipant Participant) CreateGroup(
            InspectionWorkspace workspace)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                SystemTextJsonPath(),
                AssemblyResolutionProvenance.Local(
                    "declared-method population test"));
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        return (
            workspace.CreateAssemblyContextGroup([participant]),
            participant);
    }

    private static string SystemTextJsonPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "LibraryOverview",
            "System.Text.Json.dll");

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;
}
