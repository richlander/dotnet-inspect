using System.Reflection.Metadata.Ecma335;
using System.Runtime.Versioning;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

using InspectWeb.Engine.MetadataFacade;

namespace InspectWeb.Engine.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserMetadataOperationsTests
{
    [Fact]
    public void MetadataOverview_ProjectsProductQueryResult()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        var available = Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Available>(
                Assert.Single(
                    AssemblyContextMetadataImageQuery.Execute(group)
                        .Assemblies));

        BrowserAssemblyMetadata result =
            MetadataExports.ProjectMetadataAssembly(
                "InspectWeb.Engine.Tests.dll",
                available.Value);

        Assert.StartsWith("v", result.MetadataVersion);
        Assert.False(result.MetadataVersionTruncated);
        Assert.Contains(
            result.Tables,
            table => table.Index == (int)TableIndex.TypeDef);
        Assert.Contains(
            result.Heaps,
            heap => heap.Name == nameof(HeapKind.String));
    }

    [Fact]
    public void MetadataOverview_PreservesVersionTruncation()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        MetadataImageOverview overview = Assert.IsType<
            AssemblyContextEntry<MetadataImageOverview>.Available>(
                Assert.Single(
                    AssemblyContextMetadataImageQuery.Execute(group)
                        .Assemblies))
            .Value;
        var truncated = new MetadataImageOverview(
            new InertString(
                TextPolicy.Field,
                "v4.0.30319-extra",
                maxLength: 10),
            overview.Kind,
            overview.IsAssembly,
            overview.MetadataOffset,
            overview.MetadataSize,
            overview.Heaps,
            overview.Tables,
            overview.Headers);

        BrowserAssemblyMetadata result =
            MetadataExports.ProjectMetadataAssembly(
                "InspectWeb.Engine.Tests.dll",
                truncated);

        Assert.Equal("v4.0.30319", result.MetadataVersion);
        Assert.True(result.MetadataVersionTruncated);
    }

    [Fact]
    public void MetadataTableWindow_ProjectsTypedRowsAndCoverage()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextEntry<MetadataTableWindow> query =
            AssemblyContextMetadataTableQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                new MetadataTableWindowRequest(
                    TableIndex.TypeDef,
                    startRowId: 2,
                    maxRows: 1));

        BrowserMetadataWindow result =
            MetadataExports.ProjectMetadataWindow(
                "InspectWeb.Engine.Tests.dll",
                (int)TableIndex.TypeDef,
                query);

        Assert.Null(result.Error);
        Assert.Equal(2, result.StartRowId);
        Assert.True(result.Truncated);
        Assert.Equal(2, Assert.Single(result.Rows).RowId);
        Assert.NotEmpty(result.Columns);
    }

    [Fact]
    public void MetadataHeapListing_PreservesPartiality()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        AssemblyContextEntry<MetadataHeapEntrySet> query =
            AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                HeapKind.String);

        BrowserHeapListing result =
            MetadataExports.ProjectHeapListing(
                "InspectWeb.Engine.Tests.dll",
                HeapKind.String,
                query);

        Assert.Null(result.Error);
        Assert.Equal(
            nameof(MetadataHeapCoverage.ReferencedOnly),
            result.Coverage);
        Assert.NotEmpty(result.Entries);
    }

    [Fact]
    public void MetadataProjection_KeepsParticipantFailureVisibleWithoutArtifactText()
    {
        const char bidi = '\u202E';
        string path = typeof(BrowserMetadataOperationsTests).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        ResolvedAssemblyReference actual =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("metadata adapter tests"));
        ResolvedAssemblyReference rejected = ResolvedAssemblyReference.Create(
            actual.Identity with { Name = $"Wrong{bidi}Identity" },
            path: null,
            () => new MemoryStream(bytes, writable: false),
            AssemblyResolutionProvenance.Local("rejected"));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        rejected,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextEntry<MetadataTableWindow> query =
            AssemblyContextMetadataTableQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                new MetadataTableWindowRequest(TableIndex.TypeDef));

        BrowserMetadataWindow result =
            MetadataExports.ProjectMetadataWindow(
                "Requested.dll",
                (int)TableIndex.TypeDef,
                query);
        AssemblyContextEntry<MetadataHeapEntrySet> heapQuery =
            AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                HeapKind.String);
        BrowserHeapListing heapResult =
            MetadataExports.ProjectHeapListing(
                "Requested.dll",
                HeapKind.String,
                heapQuery);
        var failed = new AssemblyContextEntry<MetadataTableWindow>.Failed(
            query.Subject,
            new InvalidDataException($"failure {bidi} detail"));
        BrowserMetadataWindow failedResult =
            MetadataExports.ProjectMetadataWindow(
                "Requested.dll",
                (int)TableIndex.TypeDef,
                failed);
        string tableError = Assert.IsType<string>(result.Error);
        string heapError = Assert.IsType<string>(heapResult.Error);
        string failedError = Assert.IsType<string>(failedResult.Error);

        Assert.Equal("Assembly unavailable: InvalidImage.", tableError);
        Assert.Equal("Assembly unavailable: InvalidImage.", heapError);
        Assert.Equal(
            "Assembly inspection failed (InvalidDataException).",
            failedError);
        Assert.DoesNotContain(bidi, tableError);
        Assert.DoesNotContain(bidi, heapError);
        Assert.DoesNotContain(bidi, failedError);
        Assert.Empty(result.Rows);
        Assert.Empty(heapResult.Entries);
    }

    static AssemblyContextGroup Group(InspectionWorkspace workspace) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        typeof(BrowserMetadataOperationsTests)
                            .Assembly.Location,
                        AssemblyResolutionProvenance.Local(
                            "metadata adapter tests")),
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
