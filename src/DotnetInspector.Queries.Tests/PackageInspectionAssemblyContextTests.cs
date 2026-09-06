using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection.PortableExecutable;

using DotnetInspector.Artifacts;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageInspectionAssemblyContextTests
{
    [Fact]
    public async Task ExplicitSelection_PreservesOrderContextsAndAcquiredIdentityWithoutCompileAuthority()
    {
        byte[] image = Image();
        PackageInspectionAssembly[] requested =
        [
            new("lib/net11.0/Sample.dll", "net11.0", "lib/net11.0"),
            new("lib/net11.0/x64/Sample.dll", "net11.0", "lib/net11.0/x64"),
            new("tools/net11.0/any/Sample.dll", "net11.0", "tools/net11.0/any"),
            new("lib/net10.0/Sample.dll", "net10.0", "lib/net10.0"),
            new("lib/net35-Unity Full v3.5/Sample.dll", "net35-Unity Full v3.5",
                "lib/net35-Unity Full v3.5"),
        ];
        var content = new Content(
            [("ref/net11.0/_._", []), .. requested.Select(entry => (entry.Path, image))]);
        var binding = PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create("Selected.Sample", "1.0.0"),
                content, content.ProducerKey, PackagePayloadOrigin.Cache),
            "net11.0");
        Assert.Equal(PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            binding.Root.AssetSelection.Status);
        PackageInspectionInput input = PackageInspectionInput.CreateFromBinding(binding);
        PackageInspectionSelection selection = input.SelectAssemblies(requested);
        requested[0] = new("not-selected.dll", null);
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext realization =
            await workspace.RealizePackageInspectionAsync(
                selection, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, realization.Groups.Length);
        Assert.Equal(selection.Assemblies, realization.Assemblies.Select(item => item.Selection));
        Assert.Equal(selection.Assemblies.Select(item => item.Path), content.Opened);
        foreach (PackageInspectionAssemblyOutcome result in realization.Assemblies)
        {
            var available = Assert.IsType<PackageInspectionAssemblyOutcome.Available>(result);
            var registration = Assert.IsType<ArtifactAcquisitionRegistration>(
                available.Participant.Assembly.Registration.ArtifactRegistration);
            var provenance = Assert.IsType<PackageInspectionArtifactProvenance>(registration.Provenance);
            Assert.Equal(binding.Coordinate, provenance.Coordinate);
            Assert.Same(binding.ContentGenerationIdentity, provenance.ContentGenerationIdentity);
            Assert.Same(selection.Identity, provenance.SelectionIdentity);
            Assert.Same(result.Selection, provenance.Assembly);
            Assert.Null(available.Participant.Assembly.Path);
            Assert.Equal(result.Selection.TargetFramework,
                Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
                    available.Participant.Assembly.Provenance).Tfm);
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(AssemblyContextApiSurfaceQuery.Execute(available.Group)
                    .Assemblies.Assemblies));
        }
        Assert.Equal(PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            binding.Root.AssetSelection.Status);
        Assert.Equal(5, content.Opened.Count);
    }

    [Fact]
    public async Task ExplicitSelection_UnreadableAndMissingEntriesDoNotEraseNeighbors()
    {
        var content = new Content(("lib/Good.dll", Image()), ("lib/Locked.dll", Image()))
        {
            BeforeOpen = path =>
            {
                if (path == "lib/Locked.dll")
                    throw new IOException("entry unavailable");
            },
        };
        PackageInspectionSelection selection = PackageInspectionInput.CreateLocal(content)
            .SelectAssemblies([
                new("lib/Locked.dll", null), new("lib/Missing.dll", null),
                new("lib/Good.dll", null)]);
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            selection, cancellationToken: TestContext.Current.CancellationToken);

        var unreadable = Assert.IsType<PackageInspectionAssemblyOutcome.Unavailable>(result.Assemblies[0]);
        Assert.Contains("could not be read", unreadable.Reason);
        Assert.NotEmpty(unreadable.PublicationFailures);
        Assert.IsType<PackageInspectionAssemblyOutcome.Unavailable>(result.Assemblies[1]);
        var available = Assert.IsType<PackageInspectionAssemblyOutcome.Available>(result.Assemblies[2]);
        Assert.IsType<AssemblyResolutionProvenance.LocalAsset>(available.Participant.Assembly.Provenance);
        Assert.Null(selection.Input.Coordinate);
        Assert.Equal(["lib/Locked.dll", "lib/Good.dll"], content.Opened);
        Assert.Single(result.Groups);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("malformed")]
    [InlineData("native")]
    public async Task ExplicitSelection_UsesMetadataCompatibilityOutcomes(string kind)
    {
        byte[] image = kind == "malformed" ? [1, 2, 3] : Image();
        if (kind == "native")
        {
            using var pe = new PEReader(new MemoryStream(image));
            int directoriesOffset = pe.PEHeaders.PEHeader!.Magic == PEMagic.PE32Plus ? 112 : 96;
            Array.Clear(image, pe.PEHeaders.PEHeaderStartOffset + directoriesOffset + 14 * 8, 8);
        }
        var content = new Content(("lib/Sample.dll", image));
        var input = PackageInspectionInput.CreateLocal(content, "Selected.Sample", "1.2");
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            input.SelectAssemblies([new("lib/Sample.dll", null)]),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1.2.0", input.PackageVersion);
        Assert.Null(input.Coordinate);
        if (kind == "valid")
        {
            var available = Assert.IsType<PackageInspectionAssemblyOutcome.Available>(
                Assert.Single(result.Assemblies));
            Assert.NotNull(available.Participant.Assembly.Registration.ArtifactRegistration);
            AssemblyContextEntry<AssemblyApiSurface> entry = Assert.Single(
                AssemblyContextApiSurfaceQuery.Execute(available.Group).Assemblies.Assemblies);
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(entry);
        }
        else
        {
            var withoutAssembly = Assert.IsType<PackageInspectionAssemblyOutcome.WithoutAssembly>(
                Assert.Single(result.Assemblies));
            Assert.Empty(result.Groups);
            if (kind == "native")
                Assert.Equal(ArtifactNonAssemblyKind.NativeImage,
                    Assert.IsType<ArtifactAssemblyProjectionOutcome.NotAssembly>(
                        withoutAssembly.Projection).Kind);
            else
                Assert.IsType<ArtifactAssemblyProjectionOutcome.Rejected>(withoutAssembly.Projection);
        }
        Assert.Single(content.Opened);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task ExplicitSelection_EnforcesExactAggregateImagePartition(int adjustment, bool admitted)
    {
        byte[] image = Image();
        var content = new Content(("lib/Sample.dll", image));
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            PackageInspectionInput.CreateLocal(content).SelectAssemblies([new("lib/Sample.dll", null)]),
            options: new PackageAssemblyContextRealizationOptions
            {
                MaxAggregateRetainedImageBytes = 2 * image.LongLength + adjustment,
                MaxAssemblyEntryBytes = image.LongLength,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        if (admitted)
        {
            var available = Assert.IsType<PackageInspectionAssemblyOutcome.Available>(
                Assert.Single(result.Assemblies));
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(AssemblyContextApiSurfaceQuery.Execute(available.Group).Assemblies.Assemblies));
        }
        else
        {
            Assert.IsType<PackageInspectionAssemblyOutcome.Unavailable>(Assert.Single(result.Assemblies));
            Assert.Empty(result.Groups);
        }
        Assert.Single(content.Opened);
    }

    [Fact]
    public async Task ExplicitSelection_BudgetRejectionLeavesCapacityForSmallerNeighbor()
    {
        byte[] image = Image();
        var content = new Content(
            ("lib/Large.dll", new byte[image.Length + 1]), ("lib/Small.dll", image));
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            PackageInspectionInput.CreateLocal(content).SelectAssemblies(
                [new("lib/Large.dll", null), new("lib/Small.dll", null)]),
            options: new PackageAssemblyContextRealizationOptions
            {
                MaxAggregateRetainedImageBytes = 2 * image.LongLength,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<PackageInspectionAssemblyOutcome.Unavailable>(result.Assemblies[0]);
        Assert.IsType<PackageInspectionAssemblyOutcome.Available>(result.Assemblies[1]);
        Assert.Single(result.Groups);
        Assert.Equal(2, content.Opened.Count);
    }

    [Fact]
    public async Task ExplicitSelection_BoundsObservedNonSeekableEntryBytes()
    {
        byte[] image = Image();
        var content = new Content(("lib/Sample.dll", image)) { NonSeekable = true };
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            PackageInspectionInput.CreateLocal(content).SelectAssemblies([new("lib/Sample.dll", null)]),
            options: new PackageAssemblyContextRealizationOptions
            {
                MaxAssemblyEntryBytes = image.Length - 1,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.IsType<PackageInspectionAssemblyOutcome.Unavailable>(Assert.Single(result.Assemblies));
        Assert.Empty(result.Groups);
        Assert.Single(content.Opened);
        Assert.All(content.Streams, stream => Assert.False(stream.CanRead));
    }

    [Fact]
    public async Task ExplicitSelection_UsesRetainedInMemoryManifestAndCaseInsensitiveContextKeys()
    {
        byte[] image = Image();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in new[] { "lib/First.dll", "lib/Second.dll" })
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(image);
            }
        }
        var content = new InMemoryPackageContent(buffer.ToArray(), false, "local-memory");
        var input = PackageInspectionInput.CreateLocal(content);
        PackageInspectionSelection selection = input.SelectAssemblies(
            [new("lib/First.dll", "net11.0", "LIB"), new("lib/Second.dll", "net11.0", "lib")]);
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            selection,
            options: new PackageAssemblyContextRealizationOptions
            {
                RequireDeclaredEntryLengths = true,
                MaxAggregateRetainedImageBytes = 4 * image.LongLength,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(result.Groups);
        Assert.Equal(2, result.Groups[0].Participants.Length);
        Assert.Same(content.GenerationIdentity, input.ContentGenerationIdentity);
        Assert.All(result.Assemblies, item =>
            Assert.IsType<PackageInspectionAssemblyOutcome.Available>(item));

        using PackageInspectionAssemblyContext overLimit = await workspace.RealizePackageInspectionAsync(
            selection,
            options: new PackageAssemblyContextRealizationOptions
            {
                RequireDeclaredEntryLengths = true,
                MaxAssemblyEntryBytes = image.LongLength - 1,
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(overLimit.Groups);
        Assert.All(overLimit.Assemblies, item =>
            Assert.IsType<PackageInspectionAssemblyOutcome.Unavailable>(item));
    }

    [Fact]
    public async Task ExplicitSelection_CloseWaitsForIndependentGroupsAndRevokesAllArtifacts()
    {
        var content = new Content(("lib/First.dll", Image()), ("tools/Second.dll", Image()));
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using PackageInspectionAssemblyContext result = await workspace.RealizePackageInspectionAsync(
            PackageInspectionInput.CreateLocal(content).SelectAssemblies(
                [new("lib/First.dll", null, "lib"), new("tools/Second.dll", null, "tools")]),
            cancellationToken: TestContext.Current.CancellationToken);
        var first = Assert.IsType<PackageInspectionAssemblyOutcome.Available>(result.Assemblies[0]);
        var second = Assert.IsType<PackageInspectionAssemblyOutcome.Available>(result.Assemblies[1]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> query = AssemblyContextIntegrationsQuery.ExecuteParticipantAsync(
            second.Group, second.Participant, async (_, _) =>
            {
                entered.SetResult();
                await resume.Task;
                using Stream retained = second.Participant.Assembly.OpenRead();
                return (int)retained.Length;
            });
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            result.Dispose();
            using (Stream retained = first.Participant.Assembly.OpenRead())
                Assert.True(retained.Length > 0);
            Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
            Assert.False(close.IsCompleted);
            resume.SetResult();
            Assert.True(await query > 0);
            Assert.Empty((await close).ArtifactSessionCleanupFailures);
            Assert.Throws<ObjectDisposedException>(() => first.Participant.Assembly.OpenRead());
            Assert.Throws<ObjectDisposedException>(() => second.Participant.Assembly.OpenRead());
            Assert.Equal(2, content.Opened.Count);
        }
        finally
        {
            resume.TrySetResult();
            await query;
        }
    }

    [Fact]
    public async Task ExplicitSelection_CancellationCleansEarlierPublicationsWithoutGroups()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var content = new Content(("lib/First.dll", Image()), ("lib/Second.dll", Image()))
        {
            BeforeOpen = path =>
            {
                if (path == "lib/Second.dll")
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
            },
        };
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await workspace.RealizePackageInspectionAsync(
                PackageInspectionInput.CreateLocal(content).SelectAssemblies(
                    [new("lib/First.dll", null), new("lib/Second.dll", null)]),
                cancellationToken: cancellation.Token));
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(2, content.Opened.Count);
        Assert.All(content.Streams, stream => Assert.False(stream.CanRead));
        Assert.Empty((await workspace.CloseAsync()).ArtifactSessionCleanupFailures);
    }

    [Fact]
    public void ExplicitSelection_RejectsAmbiguousOrNonRelativePathsBeforeOpening()
    {
        var content = new Content(("lib/Sample.dll", Image()));
        var input = PackageInspectionInput.CreateLocal(content);
        Assert.Throws<ArgumentException>(() => input.SelectAssemblies(
            [new("../Sample.dll", null)]));
        Assert.Throws<ArgumentException>(() => input.SelectAssemblies(
            [new("lib/Sample.dll", null), new("lib/Sample.dll", "net11.0")]));
        Assert.Empty(content.Opened);
    }

    static byte[] Image() => File.ReadAllBytes(typeof(PackageInspectionAssemblyContextTests).Assembly.Location);

    sealed class Content(params (string Path, byte[] Bytes)[] entries) : IPackageContent
    {
        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "tests";
        public bool RequiresArchiveTreeMatch => false;
        public List<string> Opened { get; } = [];
        public List<Stream> Streams { get; } = [];
        public Action<string>? BeforeOpen { get; init; }
        public bool NonSeekable { get; init; }
        public IEnumerable<string> EnumerateEntries() => entries.Select(entry => entry.Path);
        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }
        public bool TryOpenEntry(string path, [NotNullWhen(true)] out Stream? stream)
        {
            Opened.Add(path);
            BeforeOpen?.Invoke(path);
            byte[]? image = entries.FirstOrDefault(entry => entry.Path == path).Bytes;
            stream = image is null ? null
                : NonSeekable ? new ForwardOnlyStream(image)
                : new MemoryStream(image, writable: false);
            if (stream is not null)
                Streams.Add(stream);
            return stream is not null;
        }
    }

    sealed class ForwardOnlyStream(byte[] image) : MemoryStream(image, writable: false)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}
