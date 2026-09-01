using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

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
            InspectionEngine.ProjectMetadataAssembly(
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
            InspectionEngine.ProjectMetadataAssembly(
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
            InspectionEngine.ProjectMetadataWindow(
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
            InspectionEngine.ProjectHeapListing(
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
            InspectionEngine.ProjectMetadataWindow(
                "Requested.dll",
                (int)TableIndex.TypeDef,
                query);
        AssemblyContextEntry<MetadataHeapEntrySet> heapQuery =
            AssemblyContextMetadataHeapQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                HeapKind.String);
        BrowserHeapListing heapResult =
            InspectionEngine.ProjectHeapListing(
                "Requested.dll",
                HeapKind.String,
                heapQuery);
        var failed = new AssemblyContextEntry<MetadataTableWindow>.Failed(
            query.Subject,
            new InvalidDataException($"failure {bidi} detail"));
        BrowserMetadataWindow failedResult =
            InspectionEngine.ProjectMetadataWindow(
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

    [Theory]
    [InlineData(
        false,
        CandidateOpenFailureKind.UnsupportedMetadataFormat,
        "Assembly unavailable: UnsupportedMetadataFormat.")]
    [InlineData(
        true,
        CandidateOpenFailureKind.InvalidImage,
        "Assembly unavailable: InvalidImage (InvalidSignature).")]
    public void MetadataProjection_PreservesFormatRejection(
        bool malformed,
        CandidateOpenFailureKind expectedKind,
        string expectedError)
    {
        byte[] image = BuildRejectedImage(malformed);
        ResolvedAssemblyReference actual =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(BrowserMetadataOperationsTests).Assembly.Location,
                AssemblyResolutionProvenance.Local(
                    "metadata adapter tests"));
        ResolvedAssemblyReference unsupported =
            ResolvedAssemblyReference.Create(
                actual.Identity,
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "unsupported metadata adapter test"));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        unsupported,
                        new TestBindingPolicy()),
                ]);

        AssemblyContextEntry<MetadataTableWindow> query =
            AssemblyContextMetadataTableQuery.ExecuteParticipant(
                group,
                group.Participants[0],
                new MetadataTableWindowRequest(TableIndex.TypeDef));
        var rejected = Assert.IsType<
            AssemblyContextEntry<MetadataTableWindow>.Rejected>(query);
        BrowserMetadataWindow result =
            InspectionEngine.ProjectMetadataWindow(
                "Unsupported.dll",
                (int)TableIndex.TypeDef,
                query);

        Assert.Equal(
            expectedKind,
            rejected.Failure.Kind);
        Assert.Equal(expectedError, result.Error);
        Assert.Empty(result.Rows);
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

    static byte[] BuildRejectedImage(bool malformed)
    {
        const int fixedMetadataRootPrefixLength = 16;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                malformed
                    ? "v4.0.30319"
                    : "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var imageBuilder = new BlobBuilder();
        peBuilder.Serialize(imageBuilder);
        byte[] image = imageBuilder.ToArray();
        using var peReader = new PEReader(ImmutableArray.Create(image));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        if (malformed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(metadataStart, sizeof(uint)),
                0xDEADBEEF);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    peReader.PEHeaders.CorHeaderStartOffset + 12,
                    sizeof(int)),
                fixedMetadataRootPrefixLength + versionLength);
        }
        return image;
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
    }
}
