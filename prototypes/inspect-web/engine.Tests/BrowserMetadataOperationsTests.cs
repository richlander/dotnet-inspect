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
        var entries = MetadataEntries(group);

        BrowserAssemblyMetadata result =
            Assert.IsType<BrowserAssemblyMetadata>(
                MetadataExports.ProjectMetadataAssembly(
                    "InspectWeb.Engine.Tests.dll",
                    entries.Cli,
                    entries.Manifest,
                    entries.ReadyToRun));
        BrowserMetadataImage root = Assert.Single(result.MetadataRoots);

        Assert.Equal(nameof(MetadataRootKind.Cli), root.RequestedRoot);
        Assert.StartsWith("v", root.MetadataVersion);
        Assert.False(root.MetadataVersionTruncated);
        Assert.Contains(
            root.Tables,
            table => table.Index == (int)TableIndex.TypeDef);
        Assert.Contains(
            root.Heaps,
            heap => heap.Name == nameof(HeapKind.String));
        Assert.Null(result.ReadyToRun);
        Assert.Null(result.ReadyToRunError);
    }

    [Fact]
    public void MetadataOverview_PreservesVersionTruncation()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        var entries = MetadataEntries(group);
        var cliEntry = Assert.IsType<
            AssemblyContextEntry<MetadataImageResult>.Available>(
                entries.Cli);
        var cli = Assert.IsType<MetadataImageResult.Available>(
            cliEntry.Value);
        MetadataImageOverview overview = cli.Overview;
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
            Assert.IsType<BrowserAssemblyMetadata>(
                MetadataExports.ProjectMetadataAssembly(
                    "InspectWeb.Engine.Tests.dll",
                    new AssemblyContextEntry<MetadataImageResult>.Available(
                        cliEntry.Subject,
                        new MetadataImageResult.Available(
                            truncated,
                            cli.Root)),
                    entries.Manifest,
                    entries.ReadyToRun));
        BrowserMetadataImage root = Assert.Single(result.MetadataRoots);

        Assert.Equal("v4.0.30319", root.MetadataVersion);
        Assert.True(root.MetadataVersionTruncated);
    }

    [Fact]
    public void MetadataOverview_ProjectsRuntimeReadyToRunAndManifestRoot()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(object).Assembly.Location);
        var entries = MetadataEntries(group);
        BrowserAssemblyMetadata result =
            Assert.IsType<BrowserAssemblyMetadata>(
                MetadataExports.ProjectMetadataAssembly(
                    "System.Private.CoreLib.dll",
                    entries.Cli,
                    entries.Manifest,
                    entries.ReadyToRun));

        Assert.Contains(
            result.MetadataRoots,
            root => root.RequestedRoot
                == nameof(MetadataRootKind.Cli));
        BrowserMetadataImage manifest = Assert.Single(
            result.MetadataRoots,
            root => root.RequestedRoot
                == nameof(MetadataRootKind.ReadyToRunManifest));
        Assert.NotNull(manifest.CanonicalRoot);
        BrowserReadyToRunImage readyToRun =
            Assert.IsType<BrowserReadyToRunImage>(result.ReadyToRun);
        Assert.NotNull(readyToRun.ManifestMetadata);
        Assert.Contains(
            readyToRun.Sections,
            section => section.TypeValue
                == (uint)ReadyToRunSectionType.ManifestMetadata);
        Assert.Null(result.ManifestMetadataError);
        Assert.Null(result.ReadyToRunError);

        BrowserMetadataTable table = manifest.Tables.First(
            candidate => candidate.IsProjected && candidate.RowCount > 0);
        AssemblyContextParticipant participant = group.Participants[0];
        BrowserMetadataWindow window = MetadataExports.ProjectMetadataWindow(
            "System.Private.CoreLib.dll",
            table.Index,
            AssemblyContextMetadataTableQuery.ExecuteParticipant(
                group,
                participant,
                new MetadataTableWindowRequest(
                    (TableIndex)table.Index,
                    maxRows: 1),
                MetadataRootKind.ReadyToRunManifest));
        Assert.Null(window.Error);
        Assert.Equal(table.Index, window.Index);

        BrowserHeapListing heap = MetadataExports.ProjectHeapListing(
            "System.Private.CoreLib.dll",
            HeapKind.String,
            AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                group,
                participant,
                HeapKind.String,
                MetadataRootKind.ReadyToRunManifest));
        Assert.Null(heap.Error);
        Assert.Equal(nameof(HeapKind.String), heap.Heap);
    }

    [Fact]
    public void MetadataOverview_PreservesManifestWhenCliRootIsAbsent()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(
            workspace,
            typeof(object).Assembly.Location);
        var entries = MetadataEntries(group);
        var manifest = Assert.IsType<
            AssemblyContextEntry<MetadataImageResult>.Available>(
                entries.Manifest);

        BrowserAssemblyMetadata result =
            Assert.IsType<BrowserAssemblyMetadata>(
                MetadataExports.ProjectMetadataAssembly(
                    "System.Private.CoreLib.dll",
                    new AssemblyContextEntry<MetadataImageResult>.Available(
                        manifest.Subject,
                        new MetadataImageResult.NoMetadata()),
                    manifest,
                    entries.ReadyToRun));

        BrowserMetadataImage root = Assert.Single(result.MetadataRoots);
        Assert.Equal(
            nameof(MetadataRootKind.ReadyToRunManifest),
            root.RequestedRoot);
        Assert.Null(result.CliMetadataError);
    }

    [Fact]
    public void MetadataOverview_PreservesHealthyCliRootBesideIndependentFailures()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace);
        var entries = MetadataEntries(group);
        var cli = Assert.IsType<
            AssemblyContextEntry<MetadataImageResult>.Available>(
                entries.Cli);

        BrowserAssemblyMetadata result =
            Assert.IsType<BrowserAssemblyMetadata>(
                MetadataExports.ProjectMetadataAssembly(
                    "InspectWeb.Engine.Tests.dll",
                    cli,
                    new AssemblyContextEntry<MetadataImageResult>.Available(
                        cli.Subject,
                        new MetadataImageResult.Failed(
                            new BadImageFormatException(
                                "Malformed manifest root."))),
                    new AssemblyContextEntry<ReadyToRunImageResult>.Available(
                        cli.Subject,
                        new ReadyToRunImageResult.Failed(
                            new BadImageFormatException(
                                "Malformed ReadyToRun header.")))));

        Assert.Single(result.MetadataRoots);
        Assert.StartsWith(
            "Assembly inspection failed",
            result.ManifestMetadataError);
        Assert.StartsWith(
            "Assembly inspection failed",
            result.ReadyToRunError);
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
        Group(
            workspace,
            typeof(BrowserMetadataOperationsTests).Assembly.Location);

    static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        string path) =>
        workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "metadata adapter tests")),
                    new TestBindingPolicy()),
            ]);

    static (
        AssemblyContextEntry<MetadataImageResult> Cli,
        AssemblyContextEntry<MetadataImageResult> Manifest,
        AssemblyContextEntry<ReadyToRunImageResult> ReadyToRun)
        MetadataEntries(AssemblyContextGroup group)
    {
        AssemblyContextParticipant participant = group.Participants[0];
        return (
            AssemblyContextMetadataImageQuery.ExecuteParticipant(
                group,
                participant,
                MetadataRootKind.Cli),
            AssemblyContextMetadataImageQuery.ExecuteParticipant(
                group,
                participant,
                MetadataRootKind.ReadyToRunManifest),
            AssemblyContextReadyToRunImageQuery.ExecuteParticipant(
                group,
                participant));
    }

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
