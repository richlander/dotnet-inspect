using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;

using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class SparsePackageAssemblyProjectionTests
{
    const string Framework = "net11.0";
    const string AssetPath = "lib/net11.0/Sparse.Sample.dll";

    [Fact]
    public async Task Projection_PublishesOneExactParticipantForCanonicalAsset()
    {
        byte[] image = IntegrationAssembly("Sparse.Sample", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Sample", content);
        // Compile-asset selection is the binding's own frozen work; the
        // projection may add none of its own.
        int enumerationsBeforeProjection = content.EnumerationRequests;
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        SparsePackageAssemblyProjectionOutcome outcome =
            await workspace.ProjectSelectedPackageAssemblyAsync(
                binding,
                SelectedAsset(binding),
                Bounds(image.LongLength),
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<SparsePackageAssemblyProjectionOutcome.Available>(
                outcome);
        using SparsePackageAssemblyRealization realization =
            available.Realization;
        Assert.Same(SelectedAsset(binding), realization.Asset);
        Assert.Same(
            realization.Participant,
            Assert.Single(realization.Group.Participants));
        Assert.True(realization.IdentityDecoded);
        var projected =
            Assert.IsType<ArtifactAssemblyProjectionOutcome.Projected>(
                realization.Admission);
        Assert.Equal("Sparse.Sample", projected.Value.Identity.Name);
        Assert.NotEqual(Guid.Empty, projected.Value.Registration.ModuleVersionId);

        // Exactly one source entry is opened, and no sibling is enumerated or
        // reselected after binding.
        Assert.Equal(1, content.EntryOpenRequests);
        Assert.Equal(
            enumerationsBeforeProjection,
            content.EnumerationRequests);
    }

    [Fact]
    public async Task Projection_RetainsProducerPinnedPackageProvenance()
    {
        byte[] image = IntegrationAssembly("Sparse.Provenance", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("sparse.provenance", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        using SparsePackageAssemblyRealization realization =
            await ProjectAsync(workspace, binding, image.LongLength);

        var registration =
            Assert.IsType<DotnetInspector.Artifacts.ArtifactAcquisitionRegistration>(
                realization.Participant.Assembly.Registration
                    .ArtifactRegistration);
        var provenance =
            Assert.IsType<PackageAssemblyArtifactProvenance>(
                registration.Provenance);
        Assert.Equal(binding.Coordinate, provenance.Coordinate);
        Assert.Same(
            binding.ContentGenerationIdentity,
            provenance.ContentGenerationIdentity);
        Assert.Same(binding.SelectionIdentity, provenance.SelectionIdentity);
        Assert.Same(realization.Asset, provenance.Asset);

        // The retained admission names this exact artifact and generation, so
        // a consumer copying it out records closed facts about the image it
        // actually queried.
        AssemblyProjectionRegistration admission =
            Assert.IsType<ArtifactAssemblyProjectionOutcome.Projected>(
                realization.Admission).Value.Registration;
        Assert.Same(registration.Artifact, admission.Artifact);
        Assert.Same(registration.Generation, admission.Generation);
        Assert.Equal(
            realization.Participant.Assembly.Registration.ModuleVersionId,
            admission.ModuleVersionId);
    }

    [Fact]
    public async Task Projection_RejectsReconstructedOrForeignSelectedAsset()
    {
        byte[] image = IntegrationAssembly("Sparse.Foreign", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Foreign", content);
        PackageCompileAsset canonical = SelectedAsset(binding);
        var reconstructed = new PackageCompileAsset(
            canonical.Id,
            canonical.Path,
            canonical.AssemblyName,
            canonical.TargetFramework,
            canonical.Kind);
        Assert.Equal(canonical, reconstructed);

        PackageRootBinding foreign = Binding(
            "Sparse.Foreign",
            new TrackingPackageContent((AssetPath, image)));
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        foreach (PackageCompileAsset rejected in
            new[] { reconstructed, SelectedAsset(foreign) })
        {
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.InvalidSelectedAsset>(
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    rejected,
                    Bounds(image.LongLength),
                    TestContext.Current.CancellationToken));
        }

        // Rejection happens before any content access.
        Assert.Equal(0, content.EntryOpenRequests);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_RejectsGenerationMismatchBeforeContentAccess()
    {
        byte[] image = IntegrationAssembly("Sparse.Generation", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Generation", content);
        PackageCompileAsset asset = SelectedAsset(binding);
        content.ReplaceGeneration();
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        Assert.IsType<SparsePackageAssemblyProjectionOutcome.InvalidBinding>(
            await workspace.ProjectSelectedPackageAssemblyAsync(
                binding,
                asset,
                Bounds(image.LongLength),
                TestContext.Current.CancellationToken));
        Assert.Equal(0, content.EntryOpenRequests);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task Projection_AggregatePartitionAdmitsAtTwiceTheImage(
        int delta,
        bool admitted)
    {
        byte[] image = IntegrationAssembly("Sparse.Budget", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Budget", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        SparsePackageAssemblyProjectionOutcome outcome =
            await workspace.ProjectSelectedPackageAssemblyAsync(
                binding,
                SelectedAsset(binding),
                new SparsePackageAssemblyProjectionOptions
                {
                    MaxSelectedEntryBytes = image.LongLength,
                    MaxAggregateRetainedImageBytes =
                        (2 * image.LongLength) + delta,
                },
                TestContext.Current.CancellationToken);

        if (admitted)
        {
            var available =
                Assert.IsType<
                    SparsePackageAssemblyProjectionOutcome.Available>(
                    outcome);
            available.Realization.Dispose();
            return;
        }

        var exceeded =
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.EntryByteLimitExceeded>(
                outcome);
        Assert.Null(exceeded.Cleanup);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_DeclaredEntryLimitRejectsBeforeOpen()
    {
        byte[] image = IntegrationAssembly("Sparse.Declared", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Declared", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        var exceeded =
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.EntryByteLimitExceeded>(
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    SelectedAsset(binding),
                    new SparsePackageAssemblyProjectionOptions
                    {
                        MaxSelectedEntryBytes = image.LongLength - 1,
                        MaxAggregateRetainedImageBytes = 8 * image.LongLength,
                    },
                    TestContext.Current.CancellationToken));

        Assert.Null(exceeded.Cleanup);
        Assert.Equal(0, content.EntryOpenRequests);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_ObservedBytesBeyondDeclaredLengthExceedLimit()
    {
        byte[] image = IntegrationAssembly("Sparse.Observed", "SampleType");
        var content = new UnderreportingPackageContent(AssetPath, image);
        PackageRootBinding binding = Binding("Sparse.Observed", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        var exceeded =
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.EntryByteLimitExceeded>(
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    SelectedAsset(binding),
                    new SparsePackageAssemblyProjectionOptions
                    {
                        MaxSelectedEntryBytes = image.LongLength - 1,
                        MaxAggregateRetainedImageBytes =
                            2 * (image.LongLength - 1),
                    },
                    TestContext.Current.CancellationToken));

        Assert.Null(exceeded.Cleanup);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_ReportsSelectedEntryUnavailableWithoutContent()
    {
        byte[] image = IntegrationAssembly("Sparse.Missing", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image))
        {
            RefuseOpen = true,
        };
        PackageRootBinding binding = Binding("Sparse.Missing", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        var unavailable =
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.SelectedEntryUnavailable>(
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    SelectedAsset(binding),
                    Bounds(image.LongLength),
                    TestContext.Current.CancellationToken));

        Assert.Null(unavailable.Cleanup);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_ReportsArtifactPublicationFailureWithOwnerCodes()
    {
        byte[] image = IntegrationAssembly("Sparse.Publication", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image))
        {
            FailOpen = () => new InvalidOperationException(
                "synthetic materialization failure"),
        };
        PackageRootBinding binding = Binding("Sparse.Publication", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        var failed =
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.ArtifactPublicationFailed>(
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    SelectedAsset(binding),
                    Bounds(image.LongLength),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            "artifact.session.materialization-failed",
            Assert.Single(failed.Failures).Diagnostic.Code);
        Assert.Null(failed.Cleanup);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_IncompleteCleanupProducesBoundedResourceFreeReceipt()
    {
        byte[] image = IntegrationAssembly("Sparse.Cleanup", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image))
        {
            FailDispose = true,
            FailOpen = () => new InvalidOperationException(
                "synthetic materialization failure"),
        };
        PackageRootBinding binding = Binding("Sparse.Cleanup", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        var failed =
            Assert.IsType<
                SparsePackageAssemblyProjectionOutcome.ArtifactPublicationFailed>(
                await workspace.ProjectSelectedPackageAssemblyAsync(
                    binding,
                    SelectedAsset(binding),
                    Bounds(image.LongLength),
                    TestContext.Current.CancellationToken));

        SparsePackageProjectionCleanupReceipt? receipt = failed.Cleanup;
        Assert.NotNull(receipt);
        SparsePackageProjectionCleanupEvidence evidence =
            Assert.Single(receipt.IncompleteStages);
        Assert.Equal(
            SparsePackageProjectionCleanupStage.ArtifactSession,
            evidence.Stage);
        Assert.Equal(1, evidence.FailureCount);
        Assert.Equal(1, receipt.FailureCount);
        AssertResourceFree(receipt);
    }

    [Fact]
    public async Task Projection_CancellationKeepsCancellationAndAttachesReceipt()
    {
        byte[] image = IntegrationAssembly("Sparse.Cancel", "SampleType");
        using var cancellation = new CancellationTokenSource();
        var content = new TrackingPackageContent((AssetPath, image))
        {
            FailDispose = true,
            OnRead = cancellation.Cancel,
        };
        PackageRootBinding binding = Binding("Sparse.Cancel", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        OperationCanceledException cancelled =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () =>
                    await workspace.ProjectSelectedPackageAssemblyAsync(
                        binding,
                        SelectedAsset(binding),
                        Bounds(image.LongLength),
                        cancellation.Token));

        // The original cancellation and token survive; disposal evidence is
        // strictly secondary.
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
        Exception disposal = Assert.Single(
            ArtifactSetSession.GetCleanupFailures(cancelled));
        Assert.IsType<IOException>(disposal);
        SparsePackageProjectionCleanupReceipt? receipt =
            SparsePackageProjectionCleanupReceipt.FromException(cancelled);
        Assert.NotNull(receipt);
        Assert.Equal(
            SparsePackageProjectionCleanupStage.ArtifactSession,
            Assert.Single(receipt.IncompleteStages).Stage);
        AssertResourceFree(receipt);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public async Task Projection_RunsProducerThroughOwnerAuthorizedQueryView()
    {
        byte[] image = IntegrationAssembly("Sparse.Query", "QueryType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Query", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        using SparsePackageAssemblyRealization realization =
            await ProjectAsync(workspace, binding, image.LongLength);

        ArtifactAssemblyQueryOutcome<string> outcome =
            realization.ExecuteAssemblyQuery(
                (session, _) => session.IdentityNames().Name,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            "Sparse.Query",
            Assert.IsType<ArtifactAssemblyQueryOutcome<string>.Validated>(
                outcome).Value);
        // The retained artifact serves the query; no entry is reopened.
        Assert.Equal(1, content.EntryOpenRequests);
    }

    [Fact]
    public async Task Projection_RejectedCarrierRefusesQueryWithOwnerFailure()
    {
        byte[] valid = File.ReadAllBytes(
            typeof(SparsePackageAssemblyProjectionTests).Assembly.Location);
        byte[] malformed = new byte[valid.Length];
        var content = new TrackingPackageContent((AssetPath, malformed));
        PackageRootBinding binding = Binding("Sparse.Rejected", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        using SparsePackageAssemblyRealization realization =
            await ProjectAsync(workspace, binding, malformed.LongLength);

        // The exact Metadata-owned classification is retained verbatim; the
        // adapter neither reclassifies it nor infers it from the carrier.
        var notAssembly =
            Assert.IsType<ArtifactAssemblyProjectionOutcome.NotAssembly>(
                realization.Admission);
        Assert.Equal(ArtifactNonAssemblyKind.NativeImage, notAssembly.Kind);
        // A carrier is published as one participant, but is never inferred to
        // be projectable from its decoded-identity flag.
        Assert.False(realization.IdentityDecoded);
        Assert.NotNull(realization.Participant);

        int producerRuns = 0;
        ArtifactAssemblyQueryOutcome<int> outcome =
            realization.ExecuteAssemblyQuery(
                (_, _) => ++producerRuns,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ArtifactNonAssemblyKind.NativeImage,
            Assert.IsType<ArtifactAssemblyQueryOutcome<int>.NotAssembly>(
                outcome).Kind);
        Assert.Equal(0, producerRuns);
    }

    [Fact]
    public async Task Projection_CloseWaitsForActiveQueryThenDeniesAccess()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = IntegrationAssembly("Sparse.Lifetime", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Lifetime", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        using SparsePackageAssemblyRealization realization =
            await ProjectAsync(workspace, binding, image.LongLength);

        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ArtifactAssemblyQueryOutcome<int>> query = Task.Run(
            () => realization.ExecuteAssemblyQuery(
                (_, _) =>
                {
                    entered.SetResult();
                    resume.Task.Wait(cancellationToken);
                    return 7;
                },
                cancellationToken),
            cancellationToken);
        await entered.Task.WaitAsync(cancellationToken);

        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        realization.Dispose();
        Assert.False(close.IsCompleted);
        resume.SetResult();

        Assert.Equal(
            7,
            Assert.IsType<ArtifactAssemblyQueryOutcome<int>.Validated>(
                await query).Value);
        InspectionWorkspaceCloseReport report = await close;
        Assert.Empty(report.ArtifactSessionCleanupFailures);
        Assert.All(
            report.Groups,
            group => Assert.Null(
                Assert.IsType<InspectionWorkspaceDirectGroupCloseResult>(
                    group).Failure));
        Assert.Throws<ObjectDisposedException>(
            () => realization.ExecuteAssemblyQuery(
                (_, _) => 0,
                cancellationToken));
        Assert.Throws<ObjectDisposedException>(
            () => realization.Participant.Assembly.OpenRead());
    }

    [Fact]
    public async Task ReacquisitionRequest_IsExactResourceFreeAndSeparatesTargets()
    {
        byte[] image = IntegrationAssembly("Sparse.Request", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding(
            "Sparse.Request",
            content,
            runtimeIdentifier: "linux-x64");

        PackageRootReacquisitionRequest request =
            binding.CreateReacquisitionRequest();

        Assert.Equal(request, binding.CreateReacquisitionRequest());
        Assert.Equal(
            request.GetHashCode(),
            binding.CreateReacquisitionRequest().GetHashCode());
        Assert.Equal("sparse.request", request.Coordinate.PackageId);
        Assert.Equal("tests", request.Coordinate.Producer);
        Assert.Equal("1.0.0", request.Coordinate.Version);
        Assert.Equal(Framework, request.Coordinate.Framework);
        Assert.Equal(Framework, request.SelectionTargetFramework);
        Assert.Equal("linux-x64", request.SelectionRuntimeIdentifier);

        // A selection target that is not acquisition-target text keeps
        // acquisition framework-neutral while the selection stays exact, which
        // is precisely what a realized coordinate alone cannot express.
        PackageRootReacquisitionRequest neutral = PackageRootBinding
            .CreateFromSource(
                new AcquiredPackageSourcePayload(
                    PackageSourceCoordinate.Create("sparse.request", "1.0.0"),
                    new TrackingPackageContent((AssetPath, image)),
                    "tests",
                    PackagePayloadOrigin.Download),
                ".NETStandard,Version=v2.0")
            .CreateReacquisitionRequest();
        Assert.Null(neutral.Coordinate.Framework);
        Assert.Null(neutral.Coordinate.RuntimeIdentifier);
        Assert.Equal("netstandard2.0", neutral.SelectionTargetFramework);
        Assert.NotEqual(request, neutral);
        AssertResourceFree(neutral);

        Assert.NotEqual(
            request,
            Binding(
                    "Sparse.Request",
                    new TrackingPackageContent((AssetPath, image)),
                    runtimeIdentifier: "win-x64")
                .CreateReacquisitionRequest());
        AssertResourceFree(request);
    }

    [Fact]
    public async Task ReacquisitionRequest_SurvivesCandidateWorkspaceDisposal()
    {
        byte[] image = IntegrationAssembly("Sparse.Replace", "SampleType");
        var content = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding binding = Binding("Sparse.Replace", content);
        PackageRootReacquisitionRequest request =
            binding.CreateReacquisitionRequest();
        await using (InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous())
        {
            using SparsePackageAssemblyRealization realization =
                await ProjectAsync(workspace, binding, image.LongLength);
            Assert.NotNull(realization.Participant);
            await workspace.CloseAsync();
        }

        // A replacement generation is a new binding, and the request still
        // matches it exactly because it names no generation.
        var replacementContent = new TrackingPackageContent((AssetPath, image));
        PackageRootBinding replacement =
            Binding("Sparse.Replace", replacementContent);
        Assert.NotSame(
            binding.ContentGenerationIdentity,
            replacement.ContentGenerationIdentity);
        Assert.Equal(request, replacement.CreateReacquisitionRequest());

        await using InspectionWorkspace reopened =
            InspectionWorkspace.CreateAsynchronous();
        using SparsePackageAssemblyRealization second =
            await ProjectAsync(reopened, replacement, image.LongLength);
        Assert.Equal(
            "Sparse.Replace",
            Assert.IsType<ArtifactAssemblyQueryOutcome<string>.Validated>(
                second.ExecuteAssemblyQuery(
                    (session, _) => session.IdentityNames().Name,
                    TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public void ProjectionOptions_RequirePositiveExplicitBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SparsePackageAssemblyProjectionOptions
            {
                MaxSelectedEntryBytes = 0,
                MaxAggregateRetainedImageBytes = 16,
            }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SparsePackageAssemblyProjectionOptions
            {
                MaxSelectedEntryBytes = 16,
                MaxAggregateRetainedImageBytes = 1,
            }.Validate());
    }

    static async Task<SparsePackageAssemblyRealization> ProjectAsync(
        InspectionWorkspace workspace,
        PackageRootBinding binding,
        long imageLength) =>
        Assert.IsType<SparsePackageAssemblyProjectionOutcome.Available>(
            await workspace.ProjectSelectedPackageAssemblyAsync(
                binding,
                SelectedAsset(binding),
                Bounds(imageLength),
                TestContext.Current.CancellationToken)).Realization;

    static SparsePackageAssemblyProjectionOptions Bounds(long imageLength) =>
        new()
        {
            MaxSelectedEntryBytes = imageLength,
            MaxAggregateRetainedImageBytes = 2 * imageLength,
        };

    static PackageCompileAsset SelectedAsset(PackageRootBinding binding) =>
        binding.Root.AssetSelection.Assets.Single();

    /// <summary>
    /// Asserts that a closed owner fact carries only value-shaped data: no
    /// stream, session, lease, group, participant, workspace, package content,
    /// exception, or delegate is reachable from its public surface.
    /// </summary>
    static void AssertResourceFree(object fact)
    {
        foreach (PropertyInfo property in fact.GetType().GetProperties(
            BindingFlags.Public | BindingFlags.Instance))
        {
            object? value = property.GetValue(fact);
            AssertResourceFreeValue(property.Name, value);
        }
    }

    static void AssertResourceFreeValue(string name, object? value)
    {
        switch (value)
        {
            case null or string or bool or int or long or Enum:
                return;
            case IDisposable or IAsyncDisposable or Exception or Delegate
                or IPackageContent or Stream:
                Assert.Fail(
                    $"'{name}' exposes a resource of type {value.GetType()}.");
                return;
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            int index = 0;
            foreach (object? item in sequence)
                AssertResourceFreeValue($"{name}[{index++}]", item);
            return;
        }

        if (value.GetType().Assembly == typeof(PackageRootBinding).Assembly
            || value.GetType().Assembly
                == typeof(PackageCompileAsset).Assembly)
        {
            AssertResourceFree(value);
        }
    }

    static PackageRootBinding Binding(
        string packageId,
        IPackageContent content,
        string? runtimeIdentifier = null) =>
        PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(packageId, "1.0.0"),
                content,
                "tests",
                PackagePayloadOrigin.Download),
            Framework,
            runtimeIdentifier);

    static byte[] IntegrationAssembly(string assemblyName, string typeName)
    {
        var assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assemblyBuilder.DefineDynamicModule(assemblyName);
        TypeBuilder type = module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();

        using var stream = new MemoryStream();
        assemblyBuilder.Save(stream);
        return stream.ToArray();
    }

    static int GroupCount(InspectionWorkspace workspace)
    {
        FieldInfo field =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "InspectionWorkspace._groups was not found.");
        return ((System.Collections.ICollection)field.GetValue(workspace)!)
            .Count;
    }

    sealed class TrackingPackageContent : IPackageContent, IPackageContentEntryManifest
    {
        readonly (string Path, byte[] Content)[] _entries;
        PackageContentGenerationIdentity _generation = new();

        internal TrackingPackageContent(
            params (string Path, byte[] Content)[] entries) =>
            _entries = entries;

        internal int EntryOpenRequests { get; private set; }

        internal int EnumerationRequests { get; private set; }

        internal bool RefuseOpen { get; init; }

        internal bool FailDispose { get; init; }

        internal Func<Exception>? FailOpen { get; init; }

        internal Action? OnRead { get; init; }

        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "tests";
        public bool RequiresArchiveTreeMatch => false;

        public PackageContentGenerationIdentity GenerationIdentity =>
            _generation;

        internal void ReplaceGeneration() => _generation = new();

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (RefuseOpen)
            {
                stream = null;
                return false;
            }
            foreach ((string path, byte[] content) in _entries)
            {
                if (!relativePath.Equals(path, StringComparison.Ordinal))
                    continue;

                EntryOpenRequests++;
                stream = new HookedStream(content, FailDispose, FailOpen, OnRead);
                return true;
            }

            stream = null;
            return false;
        }

        public IEnumerable<string> EnumerateEntries()
        {
            EnumerationRequests++;
            return _entries.Select(entry => entry.Path);
        }

        public bool TryGetEntryLength(string relativePath, out long length)
        {
            foreach ((string path, byte[] content) in _entries)
            {
                if (!relativePath.Equals(path, StringComparison.Ordinal))
                    continue;

                length = content.LongLength;
                return true;
            }

            length = 0;
            return false;
        }

        public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths()
        {
            EnumerationRequests++;
            return
            [
                .. _entries.Select(entry =>
                    new PackageContentEntry(entry.Path, entry.Content.LongLength)),
            ];
        }
    }

    /// <summary>
    /// A benign entry stream with existing outcome-level hooks: it can fail
    /// its read, cancel mid-read, or fail disposal. The bytes are always a
    /// normal compiler-generated image.
    /// </summary>
    sealed class HookedStream(
        byte[] content,
        bool failDispose,
        Func<Exception>? failRead,
        Action? onRead) : Stream
    {
        readonly MemoryStream _source = new(content, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _source.Length;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            onRead?.Invoke();
            if (failRead is not null)
                throw failRead();
            return _source.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            onRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (failRead is not null)
                throw failRead();
            return ValueTask.FromResult(_source.Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            _source.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _source.Dispose();
                if (failDispose)
                {
                    throw new IOException(
                        "synthetic entry stream disposal failure");
                }
            }

            base.Dispose(disposing);
        }
    }

    sealed class UnderreportingPackageContent(
        string path,
        byte[] content) : IPackageContent, IPackageContentEntryManifest
    {
        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "tests";
        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (!relativePath.Equals(path, StringComparison.Ordinal))
            {
                stream = null;
                return false;
            }

            stream = new UnderreportingLengthStream(
                content,
                content.LongLength - 1);
            return true;
        }

        public IEnumerable<string> EnumerateEntries()
        {
            yield return path;
        }

        public bool TryGetEntryLength(string relativePath, out long length)
        {
            length = relativePath.Equals(path, StringComparison.Ordinal)
                ? content.LongLength - 1
                : 0;
            return relativePath.Equals(path, StringComparison.Ordinal);
        }

        public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths() =>
            [new(path, content.LongLength - 1)];
    }

    sealed class UnderreportingLengthStream(
        byte[] content,
        long reportedLength) : Stream
    {
        readonly MemoryStream _source = new(content, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => reportedLength;

        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _source.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _source.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) =>
            _source.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _source.Dispose();
            base.Dispose(disposing);
        }
    }
}
