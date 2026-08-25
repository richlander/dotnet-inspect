using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyContextRealizationTests
{
    const string Framework = "net11.0";

    [Fact]
    public void ReferenceAndLibraryAssets_ProduceExactSeparateRoleAssociations()
    {
        byte[] surfaceAndImplementation =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        byte[] implementationOnly =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Role.Sample",
            ("ref/net11.0/Role.Sample.dll", surfaceAndImplementation),
            ("lib/net11.0/Role.Sample.dll", surfaceAndImplementation),
            ("lib/net11.0/Role.Sample.Helper.dll", implementationOnly));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(realization.SharesGroup);
        PackageAssemblyRoleParticipant surface =
            Assert.Single(realization.SurfaceParticipants);
        Assert.Same(package, surface.Package);
        Assert.Equal(
            "ref/net11.0/Role.Sample.dll",
            surface.Asset.Path);
        Assert.Equal(2, realization.ImplementationParticipants.Length);
        PackageAssemblyRoleParticipant implementation =
            realization.ImplementationParticipant(surface)!;
        Assert.Equal(
            "lib/net11.0/Role.Sample.dll",
            implementation.Asset.Path);
        Assert.Same(
            implementation,
            realization.ImplementationParticipants.Single(candidate =>
                candidate.Asset.Path == "lib/net11.0/Role.Sample.dll"));
        Assert.Contains(
            realization.ImplementationParticipants,
            candidate =>
                candidate.Asset.Path == "lib/net11.0/Role.Sample.Helper.dll");
    }

    [Fact]
    public void LibraryOnlyAssets_ReuseOneRoleAndDescriptor()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Shared.Sample",
            ("lib/net11.0/Shared.Sample.dll", image));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(realization.SharesGroup);
        Assert.Same(
            realization.SurfaceGroup,
            realization.ImplementationGroup);
        PackageAssemblyRoleParticipant surface =
            Assert.Single(realization.SurfaceParticipants);
        PackageAssemblyRoleParticipant implementation =
            Assert.Single(realization.ImplementationParticipants);
        Assert.Same(surface.Participant, implementation.Participant);
        Assert.Same(
            implementation,
            realization.ImplementationParticipant(surface));
    }

    [Fact]
    public void LibraryOnlyAssets_WithDifferentSelectorOrdering_ReuseOneRole()
    {
        byte[] firstImage =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Shared.Ordering",
            ("lib/net11.0/Zebra.dll", firstImage),
            ("lib/net11.0/apple.dll", secondImage));
        Assert.Equal(
            ["lib/net11.0/apple.dll", "lib/net11.0/Zebra.dll"],
            package.AssetSelection.Assets.Select(asset => asset.Path));
        Assert.Equal(
            ["lib/net11.0/Zebra.dll", "lib/net11.0/apple.dll"],
            package.AssetSelection.ImplementationAssets.Select(
                asset => asset.Path));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                new PackageAssemblyContextRealizationOptions
                {
                    MaxAggregateRetainedImageBytes =
                        firstImage.Length + secondImage.Length + 1,
                    MaxAssemblyEntryBytes =
                        Math.Max(firstImage.Length, secondImage.Length),
                    RequireDeclaredEntryLengths = true,
                },
                TestContext.Current.CancellationToken);

        Assert.True(realization.SharesGroup);
        Assert.Same(
            realization.SurfaceGroup,
            realization.ImplementationGroup);
        Assert.Equal(2, realization.SurfaceParticipants.Length);
        Assert.Equal(2, realization.ImplementationParticipants.Length);
    }

    [Fact]
    public void ReferenceOnlyAsset_HasNoImplementationRole()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Reference.Only",
            ("ref/net11.0/Reference.Only.dll", image));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageAssemblyRoleParticipant surface =
            Assert.Single(realization.SurfaceParticipants);
        Assert.Null(realization.ImplementationGroup);
        Assert.Empty(realization.ImplementationParticipants);
        Assert.Null(realization.ImplementationParticipant(surface));
    }

    [Fact]
    public void MultiplePackages_PreserveExactPackageAssociationsAndProvenance()
    {
        byte[] firstImage =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageAssemblyContextSelection first = Selection(
            "First.Package",
            ("lib/net11.0/Common.dll", firstImage));
        PackageAssemblyContextSelection second = Selection(
            "Second.Package",
            ("lib/net11.0/Common.dll", secondImage));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [first, second],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageAssemblyRoleParticipant firstParticipant =
            realization.SurfaceParticipants.Single(participant =>
                ReferenceEquals(participant.Package, first));
        PackageAssemblyRoleParticipant secondParticipant =
            realization.SurfaceParticipants.Single(participant =>
                ReferenceEquals(participant.Package, second));
        var firstProvenance =
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
                firstParticipant.Participant.Assembly.Provenance);
        var secondProvenance =
            Assert.IsType<AssemblyResolutionProvenance.PackageAsset>(
                secondParticipant.Participant.Assembly.Provenance);
        Assert.Equal("First.Package", firstProvenance.PackageId);
        Assert.Equal("Second.Package", secondProvenance.PackageId);
        Assert.Equal(
            "lib/net11.0/Common.dll",
            firstParticipant.Asset.Path);
        Assert.Equal(
            "lib/net11.0/Common.dll",
            secondParticipant.Asset.Path);
    }

    [Fact]
    public void MalformedSelectedAsset_RemainsARejectedParticipant()
    {
        PackageAssemblyContextSelection package = Selection(
            "Malformed.Sample",
            ("lib/net11.0/Malformed.Sample.dll", new byte[] { 1, 2, 3 }));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageAssemblyRoleParticipant participant =
            Assert.Single(realization.SurfaceParticipants);
        Assert.Equal(
            "RejectedPackageAsset0",
            participant.Participant.Assembly.Identity.Name);
        AssemblyContextApiSurfaceResult result =
            AssemblyContextApiSurfaceQuery.Execute(realization.SurfaceGroup);
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(result.Assemblies.Assemblies));
    }

    [Fact]
    public void MalformedAssets_UseSafeUniqueRejectionCarrierIdentities()
    {
        PackageAssemblyContextSelection package = Selection(
            "Whitespace.Sample",
            ("lib/net11.0/ .dll", new byte[] { 1, 2, 3 }),
            ("lib/net11.0/RejectedPackageAsset0.dll", new byte[] { 4, 5, 6 }));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["RejectedPackageAsset0", "RejectedPackageAsset1"],
            realization.SurfaceParticipants.Select(
                participant => participant.Participant.Assembly.Identity.Name));
        AssemblyContextApiSurfaceResult result =
            AssemblyContextApiSurfaceQuery.Execute(realization.SurfaceGroup);
        Assert.Equal(2, result.Assemblies.Assemblies.Length);
        Assert.All(
            result.Assemblies.Assemblies,
            entry => Assert.IsType<
                AssemblyContextEntry<AssemblyApiSurface>.Rejected>(entry));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedPairedAsset_RemainsRejectedAndCorrespondenceIsPreserved(
        bool malformedSurface)
    {
        byte[] healthy =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        byte[] malformed = [1, 2, 3];
        PackageAssemblyContextSelection package = Selection(
            "Mixed.Health",
            (
                "ref/net11.0/ILInspector.Metadata.dll",
                malformedSurface ? malformed : healthy),
            (
                "lib/net11.0/ILInspector.Metadata.dll",
                malformedSurface ? healthy : malformed));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageAssemblyRoleParticipant surface =
            Assert.Single(realization.SurfaceParticipants);
        PackageAssemblyRoleParticipant implementation =
            Assert.IsType<PackageAssemblyRoleParticipant>(
                realization.ImplementationParticipant(surface));
        AssemblyContextEntry<AssemblyApiSurface> surfaceEntry =
            Assert.Single(
                AssemblyContextApiSurfaceQuery.Execute(
                    realization.SurfaceGroup)
                .Assemblies.Assemblies);
        AssemblyContextEntry<AssemblyApiSurface> implementationEntry =
            Assert.Single(
                AssemblyContextApiSurfaceQuery.Execute(
                    realization.ImplementationGroup!)
                .Assemblies.Assemblies);

        if (malformedSurface)
        {
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
                surfaceEntry);
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                implementationEntry);
        }
        else
        {
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                surfaceEntry);
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
                implementationEntry);
        }
        Assert.Same(
            implementation,
            realization.ImplementationParticipant(surface));
    }

    [Fact]
    public void IdentityMismatch_CreatesNoPartialRole()
    {
        byte[] surface =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        byte[] implementation =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Mismatch.Sample",
            ("ref/net11.0/Mismatch\u202e.Sample.dll", surface),
            ("lib/net11.0/Mismatch\u202e.Sample.dll", implementation));
        using var workspace = new InspectionWorkspace();

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => workspace.RealizePackageAssemblyContextRoles(
                    [package],
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(
            "different assembly identities",
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain('\u202e', failure.Message);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void EquivalentIdentityCollision_CreatesNoPartialRole()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Collision.Sample",
            ("lib/net11.0/Collision.Sample.dll", image),
            ("lib/net11.0/Collision.Sample.Second.dll", image));
        using var workspace = new InspectionWorkspace();

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => workspace.RealizePackageAssemblyContextRoles(
                    [package],
                    cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(
            "same assembly identity",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void DeclaredRoleBudget_IsCheckedBeforeIdentityDecoding()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Budget.Sample",
            ("lib/net11.0/Budget.Sample.dll", image));
        using var workspace = new InspectionWorkspace();

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => workspace.RealizePackageAssemblyContextRoles(
                    [package],
                    new PackageAssemblyContextRealizationOptions
                    {
                        MaxAggregateRetainedImageBytes = image.Length - 1,
                        MaxAssemblyEntryBytes = image.Length,
                        RequireDeclaredEntryLengths = true,
                    },
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "before assembly identity decoding",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void DeclaredEntryBudget_FailureDoesNotExposeArtifactPath()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        const string path = "lib/net11.0/Budget\u202e.Sample.dll";
        PackageAssemblyContextSelection package = Selection(
            "Budget.Sample",
            (path, image));
        using var workspace = new InspectionWorkspace();

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => workspace.RealizePackageAssemblyContextRoles(
                    [package],
                    new PackageAssemblyContextRealizationOptions
                    {
                        MaxAggregateRetainedImageBytes = image.Length,
                        MaxAssemblyEntryBytes = image.Length - 1,
                        RequireDeclaredEntryLengths = true,
                    },
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "assembly-entry byte limit",
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(path, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202e', failure.Message);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void EntryReads_StayBoundedWhenContentUnderreportsLength()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        const string path = "lib/net11.0/Underreported\u202e.Sample.dll";
        var content = new UnderreportingPackageContent(path, image);
        var package = new PackageAssemblyContextSelection(
            content,
            "Underreported.Sample",
            "1.0.0",
            Framework);
        int limit = image.Length - 1;
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                new PackageAssemblyContextRealizationOptions
                {
                    MaxAggregateRetainedImageBytes = image.Length,
                    MaxAssemblyEntryBytes = limit,
                },
                TestContext.Current.CancellationToken);
        PackageAssemblyRoleParticipant participant =
            Assert.Single(realization.SurfaceParticipants);

        using Stream stream = participant.Participant.Assembly.OpenRead();
        InvalidDataException failure =
            Assert.Throws<InvalidDataException>(
                () => stream.CopyTo(Stream.Null));
        Assert.Contains(
            "assembly-entry byte limit",
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(path, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202e', failure.Message);
    }

    [Fact]
    public void CancellationBeforeDecoding_CreatesNoPartialRole()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        PackageAssemblyContextSelection package = Selection(
            "Cancelled.Sample",
            ("lib/net11.0/Cancelled.Sample.dll", image));
        using var workspace = new InspectionWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: cancellation.Token));
        Assert.Equal(0, GroupCount(workspace));
    }

    static PackageAssemblyContextSelection Selection(
        string packageId,
        params (string Path, byte[] Content)[] entries)
    {
        var content = new InMemoryPackageContent(
            Archive(entries),
            fromCache: false,
            producerKey: "tests");
        var selection = new PackageAssemblyContextSelection(
            content,
            packageId,
            "1.0.0",
            Framework);
        Assert.True(selection.AssetSelection.IsSelected);
        return selection;
    }

    static byte[] Archive(
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream destination =
                    archive.CreateEntry(
                        path,
                        CompressionLevel.NoCompression)
                    .Open();
                destination.Write(content);
            }
        }

        return buffer.ToArray();
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

    sealed class UnderreportingPackageContent(
        string path,
        byte[] content) : IPackageContent
    {
        public string? RootPath => null;
        public string? NupkgPath => null;
        public bool FromCache => false;
        public string ProducerKey => "tests";
        public bool RequiresArchiveTreeMatch => false;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream)
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
                content.Length - 1);
            return true;
        }

        public IEnumerable<string> EnumerateEntries()
        {
            yield return path;
        }
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

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            _source.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            _source.Read(buffer);

        public override int ReadByte() => _source.ReadByte();

        public override long Seek(long offset, SeekOrigin origin) =>
            _source.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _source.Dispose();
            base.Dispose(disposing);
        }
    }
}
