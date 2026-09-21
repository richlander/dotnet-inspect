using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using CSharpText.MemberSlicing;
using DotnetInspector.Libraries;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextLibraryAdapterTests
{
    // Focused image/companion cases are PR-fast.
    [Fact]
    public async Task SuppliedPortablePdb_PreservesContentProvenanceAndRetirement()
    {
        var (source, pdb) = MemberSlicingInput();
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy());
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        var completed = Assert.IsType<AssemblyContextLibraryAdapterResult.Completed>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                CompanionLimits(source, pdb), pdb,
                TestContext.Current.CancellationToken));
        LibraryOperationLease? operation = null;
        try
        {
            LibraryCompanionCorrespondence correspondence =
                Assert.Single(completed.Reference.CompanionCorrespondences);
            Assert.Equal(LibraryContentRole.PortablePdb, correspondence.Role);
            Assert.Same(completed.Association.PublishedContent, correspondence.Assembly);
            Assert.Same(pdb.Provenance, correspondence.Content.Provenance);
            Assert.Same(
                completed.Association.PublishedContent.Generation,
                correspondence.Content.Generation);
            LibraryContentReference companion = Assert.Single(
                completed.Reference.Contents,
                content => content.HasRole(LibraryContentRole.PortablePdb));
            Assert.Same(completed.Reference.ApiAssembly, companion.AssociatedAssembly);
            Assert.Same(correspondence.Content, companion.ArtifactReference);
            Assert.Equal(2, completed.Reference.Contents.Count);
            Assert.Equal(1, source.OpenCount);

            operation = Issued(completed.Owner, completed.Reference);
            group.Dispose();
            source.Disable();
            Task libraryRetirement = completed.Owner.DisposeAsync().AsTask();
            Task artifactRetirement = completed.Artifacts.DisposeAsync().AsTask();
            Assert.False(libraryRetirement.IsCompleted);
            Assert.False(artifactRetirement.IsCompleted);
            Assert.True(operation.Snapshot(
                companion, pdb.Image,
                static (view, expected, _) => view.Content.SequenceEqual(expected.AsSpan()),
                TestContext.Current.CancellationToken));
            Assert.True(operation.Snapshot(
                completed.Reference.ApiAssembly, source.Bytes,
                static (view, expected, _) => view.Content.SequenceEqual(expected),
                TestContext.Current.CancellationToken));
            operation.Dispose();
            operation = null;
            await libraryRetirement.WaitAsync(TestContext.Current.CancellationToken);
            await artifactRetirement.WaitAsync(TestContext.Current.CancellationToken);
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
    public async Task SuppliedPortablePdb_DoesNotPromoteApiOnlyInput()
    {
        var (source, pdb) = MemberSlicingInput();
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy());
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(
            async () => await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.ApiOnly,
                CompanionLimits(source, pdb), pdb, TestContext.Current.CancellationToken));
        Assert.Equal("portablePdb", failure.ParamName);
        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public async Task SuppliedPortablePdb_EnablesRealSourceHouseMember()
    {
        var (source, pdb) = MemberSlicingInput();
        byte[] sourceBytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "RealAssets", "LibraryAdapter", "MemberTextSlicer.cs"));
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy());
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        var target = Assert.IsType<AssemblyImageAccessResult<SourceHouseTarget.MemberTarget>.Available>(
            group.UseAssemblySession(participant, TestContext.Current.CancellationToken, (session, _) =>
            {
                ApiType type = Assert.Single(session.ApiSurface(includeAll: true).Types,
                    type => type.DefinitionName?.ToMetadataFullName() == typeof(MemberTextSlicer).FullName);
                ApiMember member = Assert.Single(type.Members,
                    member => member.Name == nameof(MemberTextSlicer.ExtractMemberText));
                return new SourceHouseTarget.MemberTarget(
                    type.DefinitionName!, ApiMemberIdentity.GetMemberAnchor(type, member),
                    member.MetadataToken!.Value);
            })).Value;
        var completed = Assert.IsType<AssemblyContextLibraryAdapterResult.Completed>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                CompanionLimits(source, pdb), pdb, TestContext.Current.CancellationToken));
        try
        {
            LibraryOperationLease operation = Issued(completed.Owner, completed.Reference);
            group.Dispose();
            source.Disable();
            var request = new SourceHouseAuthoredRequest(
                SourceHouseRequestIdentity.Create("adapted-member"),
                completed.Reference,
                completed.Reference.ImplementationAssembly!,
                target,
                new SourceHouseOperationPlan(
                    SourceHouseOperationPlanIdentity.Create("repository-source"),
                    SourceHousePolicyGeneration.Create("adapter-test"),
                    new SourceHouseLimits(
                        source.Bytes.Length, pdb.Image.Length,
                        new ApiSurfaceExtractionBounds(2_000, 40_000, 1_000, 1_000, 500_000, 8_000_000),
                        new SourceLinkReadLimits(1_000_000, 1_000_000, 1_000),
                        1_000, 1_000, 1, sourceBytes.Length, sourceBytes.Length),
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    [new RepositorySourceCapability(sourceBytes)]));
            var result = Assert.IsType<SourceHouseOutcome.Available>(
                await SourceHouse.SourceHouse.ExecuteAuthoredAsync(
                    request, operation, TestContext.Current.CancellationToken));
            Assert.Equal(SourceHousePdbContributionKind.SuppliedCompanion, result.PdbContribution.Kind);
            Assert.Equal(SourceHouseSourceUnitScope.ExactMember, result.Source.Mapping!.Scope);
            Assert.StartsWith("public static string? ExtractMemberText(", result.Source.Text.TrimStart());
            Assert.Same(completed.Reference, result.Receipt.Request.Library);
            Assert.Throws<ObjectDisposedException>(() => operation.Snapshot(
                completed.Reference.ApiAssembly, static (view, _) => view.Content.Length,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            await RetireAsync(completed);
        }
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("malformed")]
    [InlineData("truncated")]
    public async Task SuppliedPortablePdb_RejectionRetainsNativeEvidence(string kind)
    {
        var (source, pdb) = MemberSlicingInput();
        byte[] bytes = kind switch
        {
            "foreign" => File.ReadAllBytes(Path.ChangeExtension(typeof(PdbContext).Assembly.Location, ".pdb")),
            "malformed" => [1, 2, 3, 4],
            "truncated" => [0x42, 0x53, 0x4a],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var supplied = new AssemblyContextLibraryPortablePdb(
            ImmutableArray.CreateRange(bytes), pdb.Provenance);
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy());
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        var rejected = Assert.IsType<AssemblyContextLibraryAdapterResult.PortablePdbRejected>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                CompanionLimits(source, supplied), supplied, TestContext.Current.CancellationToken));
        Assert.Same(source.Assembly.Registration, rejected.SourceRegistration);
        Assert.Same(supplied.Provenance, rejected.Provenance);
        Assert.NotEmpty(rejected.Observations);
        if (kind == "foreign")
            Assert.Contains(rejected.Observations, observation => observation.Contains("identity mismatch"));
        Assert.Empty(rejected.CleanupFailures);
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            group.UseAssemblyImage(source.Assembly, static image => image.Content.Length));
    }

    [Fact]
    public async Task SuppliedPortablePdb_WithoutPortableCodeViewCannotEstablishCorrespondence()
    {
        var (_, pdb) = MemberSlicingInput();
        byte[] image = EmitAssembly("Adapter.NoPortableIdentity", Guid.NewGuid());
        AssemblySource source = AssemblySource.FromBytes(image, ReadIdentity(image), "no portable identity");
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy());
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        var rejected = Assert.IsType<AssemblyContextLibraryAdapterResult.PortablePdbRejected>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                CompanionLimits(source, pdb), pdb, TestContext.Current.CancellationToken));
        Assert.Contains("The selected assembly has no Portable CodeView identity.", rejected.Observations);
        Assert.Empty(rejected.CleanupFailures);
    }

    [Fact]
    public async Task SuppliedPortablePdb_BoundsApplyPerImageAndToCombinedRetention()
    {
        var (source, pdb) = MemberSlicingInput();
        byte[] padded = new byte[Math.Max(source.Bytes.Length, pdb.Image.Length) + 1];
        pdb.Image.CopyTo(padded);
        var supplied = new AssemblyContextLibraryPortablePdb(
            ImmutableArray.CreateRange(padded), pdb.Provenance);
        long totalBytes = (long)source.Bytes.Length + padded.Length;
        await using var workspace = new InspectionWorkspace();
        var participant = new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy());
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup([participant]);
        var incomplete = Assert.IsType<AssemblyContextLibraryAdapterResult.Incomplete>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                new(padded.Length - 1, totalBytes), supplied, TestContext.Current.CancellationToken));
        Assert.Equal(LibraryContentRole.PortablePdb, incomplete.ContentRole);
        Assert.Equal(padded.Length, incomplete.RequiredImageBytes);
        Assert.Equal(padded.Length - 1, incomplete.MaxCapturedImageBytes);

        var capacity = Assert.IsType<AssemblyContextLibraryAdapterResult.ArtifactNotPublished>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                new(padded.Length, totalBytes - 1), supplied, TestContext.Current.CancellationToken));
        Assert.Contains(capacity.Publication.Failures,
            failure => failure.Diagnostic.Code == "artifact.session.byte-limit");
        Assert.Empty(capacity.CleanupFailures);
        var completed = Assert.IsType<AssemblyContextLibraryAdapterResult.Completed>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.Implementation,
                new(padded.Length, totalBytes), supplied, TestContext.Current.CancellationToken));
        await RetireAsync(completed);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        CancellationAfterGroupSnapshot_RemainsCancellationWithoutHandoff(bool supplyPdb)
    {
        var (source, pdb) = MemberSlicingInput();
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
                    AssemblyContextLibraryRole.Implementation,
                    CompanionLimits(source, pdb),
                    supplyPdb ? pdb : null,
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

    static (AssemblySource Source, AssemblyContextLibraryPortablePdb Pdb) MemberSlicingInput()
    {
        string assemblyPath = typeof(MemberTextSlicer).Assembly.Location;
        byte[] image = File.ReadAllBytes(assemblyPath);
        byte[] pdb = File.ReadAllBytes(Path.ChangeExtension(assemblyPath, ".pdb"));
        return (
            AssemblySource.FromBytes(image, ReadIdentity(image), "pathless MemberSlicing"),
            new AssemblyContextLibraryPortablePdb(
                ImmutableArray.CreateRange(pdb), new PdbProvenance("MemberSlicing symbols")));
    }

    static AssemblyContextLibraryMaterializationLimits CompanionLimits(
        AssemblySource source, AssemblyContextLibraryPortablePdb pdb) =>
        new(Math.Max(source.Bytes.Length, pdb.Image.Length), (long)source.Bytes.Length + pdb.Image.Length);

    sealed record PdbProvenance(string Name) : IArtifactProvenance;

    sealed class RepositorySourceCapability(byte[] content) : ISourceHouseSourceCapability
    {
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create("repository-document");
        public SourceHouseCapabilityCategory Category => SourceHouseCapabilityCategory.Repository;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate, int maximumBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(content.Length <= maximumBytes);
            return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                new SourceHouseCapabilityOutcome.Available(content));
        }
    }

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
