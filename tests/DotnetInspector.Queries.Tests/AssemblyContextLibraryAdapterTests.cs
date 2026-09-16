using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextLibraryAdapterTests
{
    [Fact]
    public async Task
        SystemTextJsonImplementation_UsesRetainedSnapshotAndTransfersIndependentAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        var policy = new TestBindingPolicy();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                policy);
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            group.UseAssemblyImage(
                source.Assembly,
                static image => image.Content.Length));
        source.Disable();

        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.Implementation,
                    Limits(source.Bytes.Length),
                    cancellationToken));
        LibraryOperationLease? operation = null;
        try
        {
            LibraryReference library = completed.Reference;
            AssemblyContextLibraryAssociation association =
                completed.Association;
            Assert.True(library.IsDirectArtifact);
            Assert.Same(
                library.ApiAssembly,
                library.ImplementationAssembly);
            Assert.Equal(
                [
                    LibraryContentRole.ApiAssembly,
                    LibraryContentRole.ImplementationAssembly,
                ],
                library.ApiAssembly.Roles);
            Assert.Same(
                source.Assembly.Registration,
                association.SourceRegistration);
            Assert.Same(
                policy.Version,
                association.BindingPolicyVersion);
            Assert.Same(
                library,
                association.PublishedReference);
            Assert.Same(
                library.ApiAssembly.ArtifactReference,
                association.PublishedContent);
            Assert.Same(
                association.PublishedContent.Registration,
                association.PublishedRegistration);
            Assert.NotSame(
                (object)association.SourceRegistration,
                association.PublishedRegistration);
            Assert.Equal(
                ReadMvid(source.Bytes),
                association.Projection.Registration
                    .ModuleVersionId);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Assembly.Identity,
                    library.ApiAssembly.AssemblyIdentity!.Identity));
            var provenance =
                Assert.IsType<
                    AssemblyContextLibraryArtifactProvenance>(
                        association.PublishedContent.Provenance);
            Assert.Same(
                source.Assembly.Registration,
                provenance.SourceRegistration);
            Assert.Same(
                policy.Version,
                provenance.BindingPolicyVersion);
            Assert.Same(
                source.Assembly.Provenance,
                provenance.SourceProvenance);
            Assert.Equal(1, source.OpenCount);

            Assert.IsType<AssemblyImageAccessResult<int>.Available>(
                group.UseAssemblyImage(
                    source.Assembly,
                    static image => image.Content.Length));
            group.Dispose();
            Assert.Throws<ObjectDisposedException>(
                () => group.UseAssemblyImage(
                    source.Assembly,
                    static image => image.Content.Length));

            operation = Issued(
                completed.Owner,
                library);
            Task libraryRetirement =
                completed.Owner.DisposeAsync().AsTask();
            Task artifactRetirement =
                completed.Artifacts.DisposeAsync().AsTask();
            Assert.False(libraryRetirement.IsCompleted);
            Assert.False(artifactRetirement.IsCompleted);
            Assert.Equal(
                ((byte)'M', source.Bytes.Length),
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) =>
                        (view.Content[0], view.Content.Length),
                    cancellationToken));
            Assert.True(
                operation.Snapshot(
                    library.ApiAssembly,
                    source.Bytes,
                    static (view, expected, _) =>
                        view.Content.SequenceEqual(expected),
                    cancellationToken));

            operation.Dispose();
            operation = null;
            await libraryRetirement.WaitAsync(cancellationToken);
            await artifactRetirement.WaitAsync(cancellationToken);
            Assert.Equal(
                LibraryContentOwnerState.Released,
                completed.Owner.State);
            Assert.Empty(completed.Owner.CleanupFailures);
            Assert.Empty(completed.Artifacts.CleanupFailures);
        }
        finally
        {
            operation?.Dispose();
            await RetireAsync(completed);
        }
    }

    [Fact]
    public async Task ApiOnlyPathlessInput_AssignsOnlyApiRole()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                new TestBindingPolicy());
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    TestContext.Current.CancellationToken));
        try
        {
            Assert.Null(source.Assembly.Path);
            Assert.Null(completed.Reference.ImplementationAssembly);
            Assert.Equal(
                [LibraryContentRole.ApiAssembly],
                completed.Reference.ApiAssembly.Roles);
            Assert.Single(completed.Reference.Contents);
        }
        finally
        {
            await RetireAsync(completed);
        }
    }

    [Fact]
    public async Task EqualIdentityDifferentParticipant_IsRejected()
    {
        AssemblySource selected =
            AssemblySource.FromPathlessRuntimeImage();
        AssemblySource equal =
            AssemblySource.FromBytes(
                selected.Bytes,
                selected.Assembly.Identity,
                "equal identity foreign participant");
        await using var workspace = new InspectionWorkspace();
        var policy = new TestBindingPolicy();
        var selectedParticipant =
            new AssemblyContextParticipant(
                selected.Assembly,
                policy);
        var foreignParticipant =
            new AssemblyContextParticipant(
                equal.Assembly,
                policy);
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [selectedParticipant]);

        await Assert.ThrowsAsync<ArgumentException>(
            async () =>
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    foreignParticipant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(selected.Bytes.Length),
                    TestContext.Current.CancellationToken));
        Assert.Equal(0, selected.OpenCount);
        Assert.Equal(0, equal.OpenCount);
    }

    [Fact]
    public async Task
        Bounds_DistinguishIncompleteArtifactCapacityAndSuccess()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                new TestBindingPolicy());
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var incomplete = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Incomplete>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    new(
                        source.Bytes.Length - 1,
                        source.Bytes.Length),
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            source.Bytes.Length,
            incomplete.RequiredImageBytes);
        Assert.Equal(
            source.Bytes.Length - 1,
            incomplete.MaxCapturedImageBytes);
        Assert.Empty(incomplete.CleanupFailures);

        var capacity = Assert.IsType<
            AssemblyContextLibraryAdapterResult.ArtifactNotPublished>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    new(
                        source.Bytes.Length,
                        source.Bytes.Length - 1),
                    TestContext.Current.CancellationToken));
        Assert.Contains(
            capacity.Publication.Failures,
            failure =>
                failure.Kind
                    == ArtifactSetAdmissionFailureKind.Rejected
                && failure.Diagnostic.Code
                    == "artifact.session.artifact-byte-limit");
        Assert.Empty(capacity.CleanupFailures);

        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    TestContext.Current.CancellationToken));
        try
        {
            Assert.Equal(1, source.OpenCount);
        }
        finally
        {
            await RetireAsync(completed);
        }
    }

    [Fact]
    public async Task
        CancellationAfterGroupSnapshot_RemainsCancellationWithoutHandoff()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                new TestBindingPolicy());
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        using var cancellation = new CancellationTokenSource();
        source.BeforeOpen = cancellation.Cancel;

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellation.Token));

        Assert.Equal(1, source.OpenCount);
        Assert.Equal(
            source.Bytes.Length,
            group.RetainedImageBytes);
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            group.UseAssemblyImage(
                source.Assembly,
                static image => image.Content.Length));
    }

    [Fact]
    public async Task EmptyMvidAssembly_ReturnsTypedMetadataNonProjection()
    {
        byte[] image =
            EmitAssembly(
                "Adapter.EmptyMvid",
                Guid.Empty);
        AssemblySource source =
            AssemblySource.FromBytes(
                image,
                ReadIdentity(image),
                "empty MVID adapter input");
        await using var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                new TestBindingPolicy());
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var rejected = Assert.IsType<
            AssemblyContextLibraryAdapterResult.MetadataNotProjected>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(image.Length),
                    TestContext.Current.CancellationToken));
        var projection = Assert.IsType<
            ArtifactAssemblyProjectionOutcome.Rejected>(
                rejected.Projection);
        Assert.Equal(
            ArtifactAssemblyProjectionFailureKind
                .EmptyModuleVersionId,
            projection.Failure.Kind);
        Assert.Empty(rejected.CleanupFailures);
        Assert.Equal(1, source.OpenCount);
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            group.UseAssemblyImage(
                source.Assembly,
                static input => input.Content.Length));
    }

    [Fact]
    public async Task SnapshotRejection_PreservesCandidateOpenFailure()
    {
        AssemblySource identitySource =
            AssemblySource.FromPathlessRuntimeImage();
        AssemblySource malformed =
            AssemblySource.FromBytes(
                [1, 2, 3, 4],
                identitySource.Assembly.Identity,
                "malformed adapter input");
        await using var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                malformed.Assembly,
                new TestBindingPolicy());
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        var rejected = Assert.IsType<
            AssemblyContextLibraryAdapterResult.SnapshotRejected>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(identitySource.Bytes.Length),
                    TestContext.Current.CancellationToken));

        Assert.Same(
            malformed.Assembly.Registration,
            rejected.SourceRegistration);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Empty(rejected.CleanupFailures);
    }

    static AssemblyContextLibraryMaterializationLimits Limits(
        long imageBytes) =>
        new(imageBytes, imageBytes);

    static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<
            LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;

    static async Task RetireAsync(
        AssemblyContextLibraryAdapterResult.Completed completed)
    {
        try
        {
            await completed.Owner.DisposeAsync();
        }
        finally
        {
            await completed.Artifacts.DisposeAsync();
        }
    }

    static AssemblyReferenceIdentity ReadIdentity(
        byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static Guid ReadMvid(byte[] image)
    {
        using var pe = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        return metadata.GetGuid(
            metadata.GetModuleDefinition().Mvid);
    }

    static byte[] EmitAssembly(
        string name,
        Guid moduleVersionId)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString(name + ".dll"),
            metadata.GetOrAddGuid(moduleVersionId),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(
            attributes: default,
            @namespace: default,
            name: metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class AssemblySource
    {
        int _openCount;
        bool _disabled;

        AssemblySource(
            byte[] bytes,
            ResolvedAssemblyReference assembly)
        {
            Bytes = bytes;
            Assembly = assembly;
        }

        internal byte[] Bytes { get; }
        internal ResolvedAssemblyReference Assembly { get; }
        internal Action? BeforeOpen { get; set; }
        internal int OpenCount =>
            Volatile.Read(ref _openCount);

        internal static AssemblySource
            FromPathlessRuntimeImage()
        {
            string path =
                typeof(JsonSerializer).Assembly.Location;
            byte[] bytes = File.ReadAllBytes(path);
            ResolvedAssemblyReference descriptor =
                ResolvedAssemblyReference.CreateFromPath(
                    path,
                    AssemblyResolutionProvenance.Platform(
                        "Microsoft.NETCore.App",
                        Environment.Version.ToString(),
                        "runtime System.Text.Json"));
            return FromBytes(
                bytes,
                descriptor.Identity,
                "pathless runtime System.Text.Json");
        }

        internal static AssemblySource FromBytes(
            byte[] bytes,
            AssemblyReferenceIdentity identity,
            string provenance)
        {
            AssemblySource? source = null;
            ResolvedAssemblyReference assembly =
                ResolvedAssemblyReference.Create(
                    identity,
                    path: null,
                    () =>
                    {
                        Interlocked.Increment(
                            ref source!._openCount);
                        source.BeforeOpen?.Invoke();
                        if (source._disabled)
                        {
                            throw new IOException(
                                "The original source is no longer available.");
                        }
                        return new MemoryStream(
                            source.Bytes,
                            writable: false);
                    },
                    AssemblyResolutionProvenance.Designated(
                        provenance));
            source = new AssemblySource(bytes, assembly);
            return source;
        }

        internal void Disable() => _disabled = true;
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
        }
    }
}
