using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextMetadataQueriesTests
{
    [Fact]
    public void ImageQuery_ReadsEveryParticipantInOrder()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(AssemblyContextMetadataQueriesTests).Assembly.Location);

        AssemblyContextResult<MetadataImageOverview> result =
            AssemblyContextMetadataImageQuery.Execute(group);

        var available = Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Available>(
                Assert.Single(result.Assemblies));
        Assert.StartsWith("v", available.Value.MetadataVersion.ToString());
        Assert.Equal(
            group.Participants[0].Assembly.Registration,
            available.Subject.Registration);
    }

    [Fact]
    public void ImageQuery_CarriesRejectionBesideHealthyMetadata()
    {
        string path =
            typeof(AssemblyContextMetadataQueriesTests).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        ResolvedAssemblyReference actual =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("available"));
        ResolvedAssemblyReference rejected = ResolvedAssemblyReference.Create(
            actual.Identity with { Name = "WrongIdentity" },
            path: null,
            () => new MemoryStream(bytes, writable: false),
            AssemblyResolutionProvenance.Local("rejected"));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(rejected, policy),
                    new AssemblyContextParticipant(actual, policy),
                ]);

        AssemblyContextResult<MetadataImageOverview> result =
            AssemblyContextMetadataImageQuery.Execute(group);

        var failed = Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Rejected>(
                result.Assemblies[0]);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            failed.Failure.Kind);
        Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Available>(
                result.Assemblies[1]);
    }

    [Fact]
    public void TableQuery_ReturnsRequestedWindowAndCoverage()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(AssemblyContextMetadataQueriesTests).Assembly.Location);
        var request = new MetadataTableWindowRequest(
            TableIndex.TypeDef,
            startRowId: 2,
            maxRows: 1);

        AssemblyContextEntry<MetadataTableWindow> result =
            AssemblyContextMetadataTableQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                request);

        var available = Assert.IsType<
            AssemblyContextEntry<MetadataTableWindow>.Available>(result);
        Assert.Equal(2, available.Value.StartRowId);
        Assert.Equal(1, available.Value.MaxRows);
        MetadataRow row = Assert.Single(available.Value.Table.Rows);
        Assert.Equal(2, row.RowId);
        Assert.NotNull(available.Value.Table.Truncation);
    }

    [Fact]
    public void TableRequest_RejectsUnboundedOrUnsupportedWindows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MetadataTableWindowRequest(
                TableIndex.TypeDef,
                maxRows: MetadataTableWindowRequest.MaxRowsLimit + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MetadataTableWindowRequest((TableIndex)63));
    }

    [Fact]
    public void HeapQuery_CarriesNativeCoverageAndBounds()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(AssemblyContextMetadataQueriesTests).Assembly.Location);

        AssemblyContextEntry<MetadataHeapEntrySet> result =
            AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                HeapKind.String);

        var available = Assert.IsType<
            AssemblyContextEntry<MetadataHeapEntrySet>.Available>(result);
        Assert.Equal(
            MetadataHeapCoverage.ReferencedOnly,
            available.Value.Coverage);
        Assert.InRange(
            available.Value.Entries.Length,
            1,
            MetadataProjectionOptions.DefaultMaxHeapEntries);
    }

    [Fact]
    public void ParticipantQueries_RejectParticipantsFromAnotherGroup()
    {
        string path =
            typeof(AssemblyContextMetadataQueriesTests).Assembly.Location;
        using var firstWorkspace = new InspectionWorkspace();
        using var secondWorkspace = new InspectionWorkspace();
        using AssemblyContextGroup first = Group(firstWorkspace, path);
        using AssemblyContextGroup second = Group(secondWorkspace, path);

        Assert.Throws<ArgumentException>(
            () => AssemblyContextMetadataTableQuery.ExecuteParticipant(
                first,
                second.Participants[0],
                new MetadataTableWindowRequest(TableIndex.TypeDef)));
        Assert.Throws<ArgumentException>(
            () => AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                first,
                second.Participants[0],
                HeapKind.String));
    }

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "metadata query tests")),
                    new TestBindingPolicy()),
            ]);

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}
