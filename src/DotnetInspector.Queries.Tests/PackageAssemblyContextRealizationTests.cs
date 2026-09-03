using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;

using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Workspaces;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageAssemblyContextRealizationTests
{
    const string Framework = "net11.0";

    [Fact]
    public void PackageWithoutCompileAssets_RetainsRootWithoutAssemblyRoles()
    {
        PackageRootRealization package = RootSelection(
            "Tool.Pointer",
            ("tools/net11.0/any/Tool.Pointer.dll", [0x01]));
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            package.AssetSelection.Status);

        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(realization.HasAssemblyContexts);
        Assert.Empty(realization.SurfaceParticipants);
        Assert.Empty(realization.ImplementationParticipants);
        Assert.Null(realization.ImplementationGroup);
        Assert.False(realization.SharesGroup);
        Assert.Throws<InvalidOperationException>(() => realization.SurfaceGroup);
        Assert.Equal(Framework, package.RequestedTargetFramework);
        Assert.Equal("tests", package.ProducerKey);
        Assert.False(package.FromCache);
    }

    [Fact]
    public void PackageWorkspaceIntegrationsQuery_RejectsRootOnlyRealization()
    {
        PackageRootRealization package = RootSelection(
            "Tool.Pointer",
            ("tools/net11.0/any/Tool.Pointer.dll", [0x01]));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => PackageWorkspaceIntegrationsQuery.Execute(realization));

        Assert.Contains("selected compile asset", error.Message);
    }

    [Fact]
    public void ExplicitEmptyCompileGroup_RetainsRootWithoutAssemblyRoles()
    {
        PackageRootRealization package = RootSelection(
            "Empty.Compile.Group",
            ("ref/net11.0/_._", []),
            ("lib/net11.0/Empty.Compile.Group.dll", [0x01]));
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            package.AssetSelection.Status);

        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(realization.HasAssemblyContexts);
        Assert.Empty(realization.SurfaceParticipants);
        Assert.Empty(realization.ImplementationParticipants);
    }

    [Fact]
    public void NoMatchingFramework_RetainsRequestedRootWithoutAssemblyRoles()
    {
        PackageRootRealization package = RootSelection(
            "Future.Library",
            "net10.0",
            ("lib/net11.0/Future.Library.dll", [0x01]));
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
            package.AssetSelection.Status);

        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(realization.HasAssemblyContexts);
        Assert.Equal("net10.0", package.RequestedTargetFramework);
        Assert.Equal("net10.0", package.AssetSelection.TargetFramework);
    }

    [Fact]
    public void InvalidImplementationLayout_RetainsFailedRootWithoutAssemblyRoles()
    {
        PackageRootRealization package = RootSelection(
            "Invalid.Layout",
            ("lib/net11.0/Invalid.Layout.dll", [0x01]),
            ("LIB/NET11.0/invalid.layout.dll", [0x02]));
        Assert.Equal(
            PackageCompileAssetSelectionStatus.InvalidImplementationAssets,
            package.AssetSelection.Status);
        Assert.NotNull(package.AssetSelection.Message);

        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(realization.HasAssemblyContexts);
        Assert.Empty(realization.SurfaceParticipants);
    }

    [Fact]
    public void MixedPackages_CreateRolesOnlyForSelectedCompileAssets()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        PackageRootRealization selected = Selection(
            "Library.Package",
            ("lib/net11.0/Library.Package.dll", image));
        PackageRootRealization rootOnly = RootSelection(
            "Tool.Pointer",
            ("tools/net11.0/any/Tool.Pointer.dll", [0x01]));

        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [selected, rootOnly],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(realization.HasAssemblyContexts);
        PackageAssemblyRoleParticipant participant =
            Assert.Single(realization.SurfaceParticipants);
        Assert.Same(selected.Identity, participant.Package);
        Assert.DoesNotContain(
            realization.SurfaceParticipants,
            candidate => ReferenceEquals(candidate.Package, rootOnly.Identity));
    }

    [Fact]
    public void PackageRootIdentity_DistinguishesRequestedFrameworksByReference()
    {
        PackageRootRealization net10 = RootSelection(
            "Multi.Targeted",
            "net10.0",
            ("tools/net10.0/any/Multi.Targeted.dll", [0x01]));
        PackageRootRealization net11 = RootSelection(
            "Multi.Targeted",
            "net11.0",
            ("tools/net11.0/any/Multi.Targeted.dll", [0x01]));

        Assert.NotSame(net10.Identity, net11.Identity);
        Assert.Equal("net10.0", net10.Identity.RequestedTargetFramework);
        Assert.Equal("net11.0", net11.Identity.RequestedTargetFramework);
    }

    [Fact]
    public async Task PackageRootGenerationIdentity_ReplacementChangesIdentity()
    {
        const string packageId = "generation.sample";
        const string version = "1.0.0";
        const string producer = "tests";
        var store = new InMemoryPackageStore();
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(packageId, version);
        await using var firstArchive = new MemoryStream(
            Archive(("lib/net11.0/First.dll", [0x01])));
        IPackageContent first = await store.CommitAsync(
            packageId,
            version,
            producer,
            firstArchive,
            TestContext.Current.CancellationToken);
        var firstPayload = new AcquiredPackageSourcePayload(
            coordinate,
            first,
            producer,
            PackagePayloadOrigin.Download);
        PackageRootBinding firstBinding =
            PackageRootBinding.CreateFromSource(firstPayload, Framework);
        IPackageContent cachedFirst = Assert.IsAssignableFrom<IPackageContent>(
            store.TryGetCached(
                packageId,
                version,
                [producer]));
        Assert.Same(
            firstBinding.ContentGenerationIdentity,
            cachedFirst.GenerationIdentity);

        await using var replacementArchive = new MemoryStream(
            Archive(("lib/net11.0/Second.dll", [0x02])));
        IPackageContent replacement = await store.CommitAsync(
            packageId,
            version,
            producer,
            replacementArchive,
            TestContext.Current.CancellationToken);
        var replacementPayload = new AcquiredPackageSourcePayload(
            coordinate,
            replacement,
            producer,
            PackagePayloadOrigin.Download);
        PackageRootBinding replacementBinding =
            PackageRootBinding.CreateFromSource(replacementPayload, Framework);

        Assert.NotSame(
            firstBinding.ContentGenerationIdentity,
            replacementBinding.ContentGenerationIdentity);
        Assert.NotEqual(
            firstBinding.Root.AssetSelection.DefaultAsset?.Path,
            replacementBinding.Root.AssetSelection.DefaultAsset?.Path);
    }

    [Fact]
    public void PackageRootSelectionIdentity_DifferentAssetsChangeIdentity()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("selection.sample", "1.0.0");
        var content = new InMemoryPackageContent(
            Archive(
                ("lib/net10.0/Net10.dll", [0x01]),
                ("lib/net11.0/Net11.dll", [0x02])),
            fromCache: false,
            producerKey: "tests");
        var payload = new AcquiredPackageSourcePayload(
            coordinate,
            content,
            "tests",
            PackagePayloadOrigin.Download);

        PackageRootBinding net10 =
            PackageRootBinding.CreateFromSource(payload, "net10.0");
        PackageRootBinding net11 =
            PackageRootBinding.CreateFromSource(payload, "net11.0");

        Assert.Same(
            net10.ContentGenerationIdentity,
            net11.ContentGenerationIdentity);
        Assert.NotSame(net10.SelectionIdentity, net11.SelectionIdentity);
        Assert.Equal(
            ["lib/net10.0/Net10.dll"],
            net10.Root.AssetSelection.Assets.Select(asset => asset.Path));
        Assert.Equal(
            ["lib/net11.0/Net11.dll"],
            net11.Root.AssetSelection.Assets.Select(asset => asset.Path));
    }

    [Fact]
    public void PackageRootSelectionIdentity_SelectionSequencesAreImmutable()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("selection.immutable", "1.0.0");
        var payload = new AcquiredPackageSourcePayload(
            coordinate,
            new InMemoryPackageContent(
                Archive(("lib/net11.0/Immutable.dll", [0x01])),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(payload, Framework);

        IList<PackageCompileAsset> assets =
            Assert.IsAssignableFrom<IList<PackageCompileAsset>>(
                binding.Root.AssetSelection.Assets);
        IList<PackageCompileAsset> implementationAssets =
            Assert.IsAssignableFrom<IList<PackageCompileAsset>>(
                binding.Root.AssetSelection.ImplementationAssets);
        IList<string> frameworks =
            Assert.IsAssignableFrom<IList<string>>(
                binding.Root.AssetSelection.AvailableTargetFrameworks);

        Assert.True(assets.IsReadOnly);
        Assert.True(implementationAssets.IsReadOnly);
        Assert.True(frameworks.IsReadOnly);
        Assert.Throws<NotSupportedException>(
            () => assets.Add(assets[0]));
        Assert.Throws<NotSupportedException>(
            () => implementationAssets.Add(implementationAssets[0]));
        Assert.Throws<NotSupportedException>(
            () => frameworks.Add(frameworks[0]));
    }

    [Fact]
    public void RealizedPackageCoordinate_ReacquisitionContractIsCoherent()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("coordinate.sample", "1.0.0");
        var firstPayload = new AcquiredPackageSourcePayload(
            coordinate,
            new InMemoryPackageContent(
                Archive(("lib/net11.0/First.dll", [0x01])),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);
        var secondPayload = new AcquiredPackageSourcePayload(
            coordinate,
            new InMemoryPackageContent(
                Archive(("lib/net11.0/Second.dll", [0x02])),
                fromCache: true,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Cache);

        PackageRootBinding first =
            PackageRootBinding.CreateFromSource(firstPayload, Framework);
        PackageRootBinding second =
            PackageRootBinding.CreateFromSource(secondPayload, Framework);

        Assert.Equal(first.Coordinate, second.Coordinate);
        Assert.NotSame(
            first.ContentGenerationIdentity,
            second.ContentGenerationIdentity);
    }

    [Fact]
    public void PackageRootBinding_RootOnlyOutcomeRemainsValid()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("root.only", "1.0.0");
        var content = new InMemoryPackageContent(
            Archive(("tools/net11.0/any/root-only.dll", [0x01])),
            fromCache: false,
            producerKey: "tests");
        var payload = new AcquiredPackageSourcePayload(
            coordinate,
            content,
            "tests",
            PackagePayloadOrigin.Download);

        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(payload);

        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            binding.Root.AssetSelection.Status);
        Assert.Null(binding.Coordinate.Framework);
        Assert.True(binding.Root.ReferencesContent(content));
    }

    [Fact]
    public void PackageRootBinding_UnrequestedFrameworkDoesNotUsePackageFolderAsCoordinate()
    {
        const string untrustedFramework = "net8.0\u202e";
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("untrusted.framework", "1.0.0");
        var payload = new AcquiredPackageSourcePayload(
            coordinate,
            new InMemoryPackageContent(
                Archive(
                    ($"lib/{untrustedFramework}/Untrusted.Framework.dll", [0x01])),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);

        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(payload);

        Assert.Null(binding.Coordinate.Framework);
        Assert.Equal(
            untrustedFramework,
            binding.Root.AssetSelection.TargetFramework);
    }

    [Fact]
    public void PackageRootBinding_UnrepresentableSelectionTargetUsesFrameworkNeutralCoordinate()
    {
        const string selectionTarget = ".NETFramework,Version=v4.8";
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("rich.framework", "1.0.0");
        var payload = new AcquiredPackageSourcePayload(
            coordinate,
            new InMemoryPackageContent(
                Archive(("lib/net11.0/Rich.Framework.dll", [0x01])),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);

        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(payload, selectionTarget);

        Assert.Null(binding.Coordinate.Framework);
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoMatchingTargetFramework,
            binding.Root.AssetSelection.Status);
        Assert.Equal(
            selectionTarget,
            binding.Root.AssetSelection.TargetFramework);
    }

    [Fact]
    public void PackageRootBinding_ResolvedCoordinatePreservesAcquisitionTargetAndRuntime()
    {
        var resolved = new ResolvedPackageCoordinate(
            "resolved.sample",
            "1.0.0",
            "net11.0",
            "linux-x64",
            [PackageSource.NuGetOrg],
            wasFloating: false);
        var payload = new AcquiredPackagePayload(
            resolved,
            new InMemoryPackageContent(
                Archive(
                    ("lib/net10.0/Net10.dll", [0x01]),
                    ("runtimes/linux-x64/lib/net10.0/Net10.dll", [0x03]),
                    ("lib/net11.0/Net11.dll", [0x02])),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);

        PackageRootBinding binding =
            PackageRootBinding.CreateFromResolved(
                payload,
                "net10.0",
                "Resolved.Sample");

        Assert.Equal("Resolved.Sample", binding.Root.PackageId);
        Assert.Equal("net11.0", binding.Coordinate.Framework);
        Assert.Equal("linux-x64", binding.Coordinate.RuntimeIdentifier);
        Assert.Equal("net10.0", binding.Root.RequestedTargetFramework);
        Assert.Equal(
            ["lib/net10.0/Net10.dll"],
            binding.Root.AssetSelection.Assets.Select(asset => asset.Path));
        Assert.Equal(
            ["runtimes/linux-x64/lib/net10.0/Net10.dll"],
            binding.Root.AssetSelection.ImplementationAssets.Select(
                asset => asset.Path));
        Assert.Equal(
            "runtimes/linux-x64/lib/net10.0/Net10.dll",
            binding.Root.AssetSelection.FindImplementationAsset(
                Assert.Single(binding.Root.AssetSelection.Assets))!.Path);
        Assert.Equal(
            "linux-x64",
            binding.Root.RequestedRuntimeIdentifier);
        Assert.Throws<ArgumentException>(
            () => PackageRootBinding.CreateFromResolved(
                payload,
                "net10.0",
                "Different.Package"));
    }

    [Fact]
    public void PackageRootBinding_SourceRuntimeRequiresFramework()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("runtime.sample", "1.0.0");
        var payload = new AcquiredPackageSourcePayload(
            coordinate,
            new InMemoryPackageContent(
                Archive(
                    ("runtimes/linux-x64/lib/net11.0/Runtime.dll", [0x01])),
                fromCache: false,
                producerKey: "tests"),
            "tests",
            PackagePayloadOrigin.Download);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => PackageRootBinding.CreateFromSource(
                payload,
                selectionTargetFramework: null,
                runtimeIdentifier: "linux-x64"));

        Assert.Equal("selectionTargetFramework", failure.ParamName);
    }

    [Fact]
    public void PackageRootBinding_AcquiredPayloadsAreConstructionControlled()
    {
        Assert.Empty(typeof(AcquiredPackagePayload).GetConstructors());
        Assert.Empty(typeof(AcquiredPackageSourcePayload).GetConstructors());
        Assert.All(
            typeof(AcquiredPackagePayload).GetProperties(),
            property => Assert.False(property.CanWrite));
        Assert.All(
            typeof(AcquiredPackageSourcePayload).GetProperties(),
            property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void PackageRootBinding_RejectsProducerMismatch()
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create("producer.sample", "1.0.0");

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new AcquiredPackageSourcePayload(
                coordinate,
                new InMemoryPackageContent(
                    Archive(("lib/net11.0/Producer.dll", [0x01])),
                    fromCache: false,
                    producerKey: "other"),
                "tests",
                PackagePayloadOrigin.Download));

        Assert.Contains("different producers", failure.Message);
    }

    [Fact]
    public void ReferenceAndLibraryAssets_ProduceExactSeparateRoleAssociations()
    {
        byte[] surfaceAndImplementation =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        byte[] implementationOnly =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageRootRealization package = Selection(
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
        Assert.Same(package.Identity, surface.Package);
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
        PackageRootRealization package = Selection(
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
    public void RidSpecificImplementation_UsesSeparateNeutralCompileRole()
    {
        byte[] selectedImage =
            IntegrationAssembly("Rid.Sample", "SelectedType");
        byte[] unrelatedImage =
            IntegrationAssembly("Unrelated.Rid.Sample", "UnrelatedType");
        PackageRootRealization package = new(
            new InMemoryPackageContent(
                Archive(
                    ("lib/net11.0/Rid.Sample.dll", selectedImage),
                    ("lib/net11.0/shadow/Rid.Sample.dll", unrelatedImage),
                    ("runtimes/linux-x64/lib/net11.0/Rid.Sample.dll", selectedImage)),
                fromCache: false,
                producerKey: "tests"),
            "Rid.Sample",
            "1.0.0",
            Framework,
            "linux-x64");
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(realization.SharesGroup);
        PackageAssemblyRoleParticipant surface =
            Assert.Single(realization.SurfaceParticipants);
        PackageAssemblyRoleParticipant implementation =
            realization.ImplementationParticipants.Single(candidate =>
                candidate.Asset.Path
                    == "runtimes/linux-x64/lib/net11.0/Rid.Sample.dll");
        Assert.Equal("lib/net11.0/Rid.Sample.dll", surface.Asset.Path);
        Assert.Equal(
            "runtimes/linux-x64/lib/net11.0/Rid.Sample.dll",
            implementation.Asset.Path);
        Assert.Contains(
            realization.ImplementationParticipants,
            candidate =>
                candidate.Asset.Path
                    == "lib/net11.0/shadow/Rid.Sample.dll");
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
        PackageRootRealization package = Selection(
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
        PackageRootRealization package = Selection(
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
        PackageRootRealization first = Selection(
            "First.Package",
            ("lib/net11.0/Common.dll", firstImage));
        PackageRootRealization second = Selection(
            "Second.Package",
            ("lib/net11.0/Common.dll", secondImage));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [first, second],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageAssemblyRoleParticipant firstParticipant =
            realization.SurfaceParticipants.Single(participant =>
                ReferenceEquals(participant.Package, first.Identity));
        PackageAssemblyRoleParticipant secondParticipant =
            realization.SurfaceParticipants.Single(participant =>
                ReferenceEquals(participant.Package, second.Identity));
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
    public void PackageWorkspaceIntegrationsQuery_UsesImplementationRoleAndReferenceFallback()
    {
        byte[] surface = IntegrationAssembly(
            "Primary.Integrations",
            "Example.Surface");
        byte[] implementation = IntegrationAssembly(
            "Primary.Integrations",
            "Microsoft.Extensions.Logging.CustomLogger");
        byte[] helper = IntegrationAssembly(
            "Primary.Integrations.Helper",
            "OpenTelemetry.CustomTracer");
        byte[] referenceOnly = IntegrationAssembly(
            "Reference.Only.Integrations",
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");
        PackageRootRealization primary = Selection(
            "Primary.Package",
            ("ref/net11.0/Primary.Integrations.dll", surface),
            ("lib/net11.0/Primary.Integrations.dll", implementation),
            ("lib/net11.0/Primary.Integrations.Helper.dll", helper));
        PackageRootRealization secondary = Selection(
            "Secondary.Package",
            ("ref/net11.0/Reference.Only.Integrations.dll", referenceOnly));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [primary, secondary],
                cancellationToken: TestContext.Current.CancellationToken);

        var registry =
            new InspectionQueryRegistry<PackageAssemblyContextRealization>()
                .Add(
                    PackageWorkspaceIntegrationsQuery.Definition,
                    PackageWorkspaceIntegrationsQuery.Execute);
        Assert.Equal(
            InspectionCost.Unbounded,
            registry.CostOf(PackageWorkspaceIntegrationsQuery.Definition));
        PackageWorkspaceIntegrationsResult result =
            registry.Run(
                    [PackageWorkspaceIntegrationsQuery.Definition],
                    realization)
                .Get(PackageWorkspaceIntegrationsQuery.Definition);

        Assert.True(result.IsComplete);
        Assert.Equal(
            [
                "Primary.Package",
                "Primary.Package",
                "Secondary.Package",
            ],
            result.Libraries.Select(entry => entry.Subject.PackageId));
        Assert.Equal(
            [
                "lib/net11.0/Primary.Integrations.Helper.dll",
                "lib/net11.0/Primary.Integrations.dll",
                "ref/net11.0/Reference.Only.Integrations.dll",
            ],
            result.Libraries.Select(entry => entry.Subject.Asset.Path));

        PackageWorkspaceIntegrationsEntry primaryLibrary =
            result.Libraries.Single(entry =>
                entry.Subject.Asset.Path
                == "lib/net11.0/Primary.Integrations.dll");
        var primaryResult =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                primaryLibrary.Integrations);
        Assert.Contains(
            primaryResult.EcosystemSignals,
            signal =>
                signal.Integration
                == EcosystemIntegrationNames.Logging);
        Assert.DoesNotContain(
            primaryResult.EcosystemSignals,
            signal => signal.Name == "Example.Surface");

        PackageWorkspaceIntegrationsEntry helperLibrary =
            result.Libraries.Single(entry =>
                entry.Subject.Asset.Path
                == "lib/net11.0/Primary.Integrations.Helper.dll");
        var helperResult =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                helperLibrary.Integrations);
        Assert.Contains(
            helperResult.OpenTelemetrySignals,
            signal => signal.Name == "OpenTelemetry.CustomTracer");

        PackageWorkspaceIntegrationsEntry referenceLibrary =
            result.Libraries.Single(entry =>
                entry.Subject.Asset.Path
                == "ref/net11.0/Reference.Only.Integrations.dll");
        var referenceResult =
            Assert.IsType<AssemblyIntegrationsEntry.Available>(
                referenceLibrary.Integrations);
        Assert.Contains(
            referenceResult.EcosystemSignals,
            signal =>
                signal.Integration
                == EcosystemIntegrationNames.DependencyInjection);
    }

    [Fact]
    public void PackageWorkspaceIntegrationsQuery_SharedRoleDoesNotDuplicateLibraries()
    {
        byte[] implementation = IntegrationAssembly(
            "Shared.Integrations",
            "Microsoft.Extensions.Logging.CustomLogger");
        PackageRootRealization package = Selection(
            "Shared.Package",
            ("lib/net11.0/Shared.Integrations.dll", implementation));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageWorkspaceIntegrationsResult result =
            PackageWorkspaceIntegrationsQuery.Execute(realization);

        PackageWorkspaceIntegrationsEntry library =
            Assert.Single(result.Libraries);
        Assert.Equal("Shared.Package", library.Subject.PackageId);
        Assert.Equal("1.0.0", library.Subject.PackageVersion);
        Assert.Equal(
            "lib/net11.0/Shared.Integrations.dll",
            library.Subject.Asset.Path);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void PackageWorkspaceIntegrationsQuery_PreservesExactRootIdentity()
    {
        PackageRootRealization first = Selection(
            "Same.Package",
            ("lib/net11.0/Same.dll", IntegrationAssembly(
                "First.Same",
                "Microsoft.Extensions.Logging.First")));
        PackageRootRealization second = Selection(
            "Same.Package",
            ("lib/net11.0/Same.dll", IntegrationAssembly(
                "Second.Same",
                "OpenTelemetry.Second")));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [first, second],
                cancellationToken: TestContext.Current.CancellationToken);

        PackageWorkspaceIntegrationsResult result =
            PackageWorkspaceIntegrationsQuery.Execute(realization);

        Assert.Equal(2, result.Libraries.Length);
        Assert.Contains(
            result.Libraries,
            entry => ReferenceEquals(entry.Subject.Package, first.Identity));
        Assert.Contains(
            result.Libraries,
            entry => ReferenceEquals(entry.Subject.Package, second.Identity));
        Assert.NotEqual(
            result.Libraries[0].Subject,
            result.Libraries[1].Subject);
    }

    [Fact]
    public void ReferenceCorrespondence_UsesPackageIdentityAndCaseInsensitiveName()
    {
        byte[] firstImage =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        byte[] secondImage =
            File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location);
        PackageRootRealization first = Selection(
            "First.Reference.Package",
            ("ref/net11.0/COMMON.dll", firstImage),
            ("lib/net11.0/common.dll", firstImage));
        PackageRootRealization second = Selection(
            "Second.Reference.Package",
            ("ref/net11.0/COMMON.dll", secondImage),
            ("lib/net11.0/common.dll", secondImage));
        using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            workspace.RealizePackageAssemblyContextRoles(
                [first, second],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(
            realization.SurfaceParticipants,
            surface =>
            {
                PackageAssemblyRoleParticipant implementation =
                    Assert.IsType<PackageAssemblyRoleParticipant>(
                        realization.ImplementationParticipant(surface));
                Assert.Same(surface.Package, implementation.Package);
                Assert.Equal(
                    "lib/net11.0/common.dll",
                    implementation.Asset.Path);
            });
    }

    [Fact]
    public void MalformedSelectedAsset_RemainsARejectedParticipant()
    {
        PackageRootRealization package = Selection(
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
    public async Task ArtifactBackedPackageRealization_PreservesMixedParticipantsAndExactLifetime()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] valid =
            File.ReadAllBytes(
                typeof(PackageAssemblyContextRealizationTests)
                    .Assembly.Location);
        byte[] malformed = new byte[valid.Length];
        var content = new TrackingPackageContent(
            ("ref/net11.0/Artifact.Mixed.Sample.dll", valid),
            ("lib/net11.0/Artifact.Mixed.Sample.dll", malformed));
        PackageRootBinding binding =
            Binding("Artifact.Mixed.Sample", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        using PackageAssemblyContextRealization realization =
            await workspace.RealizePackageAssemblyContextRolesAsync(
                binding,
                new PackageAssemblyContextRealizationOptions
                {
                    MaxAggregateRetainedImageBytes =
                        2 * (valid.LongLength + malformed.LongLength),
                    MaxAssemblyEntryBytes = valid.LongLength,
                },
                cancellationToken);

        Assert.Equal(2, content.EntryOpenRequests);
        PackageAssemblyRoleParticipant surfaceParticipant =
            Assert.Single(realization.SurfaceParticipants);
        PackageAssemblyRoleParticipant implementationParticipant =
            Assert.Single(realization.ImplementationParticipants);
        Assert.NotSame(
            realization.SurfaceGroup,
            realization.ImplementationGroup);
        ArtifactAcquisitionRegistration[] registrations =
        [
            Assert.IsType<ArtifactAcquisitionRegistration>(
                surfaceParticipant.Participant.Assembly.Registration
                    .ArtifactRegistration),
            Assert.IsType<ArtifactAcquisitionRegistration>(
                implementationParticipant.Participant.Assembly.Registration
                    .ArtifactRegistration),
        ];
        Assert.Same(
            registrations[0].Generation,
            registrations[1].Generation);
        Assert.All(
            registrations,
            registration =>
            {
                PackageAssemblyArtifactProvenance provenance =
                    Assert.IsType<PackageAssemblyArtifactProvenance>(
                        registration.Provenance);
                Assert.Equal(binding.Coordinate, provenance.Coordinate);
                Assert.Same(
                    binding.ContentGenerationIdentity,
                    provenance.ContentGenerationIdentity);
                Assert.Same(
                    binding.SelectionIdentity,
                    provenance.SelectionIdentity);
            });

        AssemblyContextApiSurfaceResult surfaceResult =
            AssemblyContextApiSurfaceQuery.Execute(
                realization.SurfaceGroup);
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(surfaceResult.Assemblies.Assemblies));
        AssemblyContextApiSurfaceResult implementationResult =
            AssemblyContextApiSurfaceQuery.Execute(
                realization.ImplementationGroup!);
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(implementationResult.Assemblies.Assemblies));
        Assert.Equal(2, content.EntryOpenRequests);

        Assert.Equal(
            typeof(PackageAssemblyContextRealizationTests)
                .Assembly.GetName().Name,
            surfaceParticipant.Participant.Assembly.Identity.Name);
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackResume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<AssemblyImageAccessResult<int>> operation =
            realization.SurfaceGroup.UseAndReleaseAssemblySessionAsync(
                surfaceParticipant.Participant.Assembly,
                async (_, _) =>
                {
                    callbackEntered.SetResult();
                    await callbackResume.Task.WaitAsync(
                        cancellationToken);
                    return 1;
                });
        await callbackEntered.Task.WaitAsync(cancellationToken);

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();

        Assert.False(close.IsCompleted);
        realization.Dispose();
        long retainedArtifactLength = 0;
        try
        {
            using Stream retainedArtifact =
                surfaceParticipant.Participant.Assembly.OpenRead();
            retainedArtifactLength = retainedArtifact.Length;
        }
        finally
        {
            callbackResume.SetResult();
        }
        Assert.IsType<AssemblyImageAccessResult<int>.Available>(
            await operation);
        InspectionWorkspaceCloseReport report = await close;
        Assert.Equal(valid.LongLength, retainedArtifactLength);
        Assert.Empty(report.ArtifactSessionCleanupFailures);
        Assert.Equal(2, content.EntryOpenRequests);
        Assert.Throws<ObjectDisposedException>(
            () => surfaceParticipant.Participant.Assembly.OpenRead());
    }

    [Fact]
    public async Task ArtifactBackedPackageRealization_RejectsAggregateBudgetWithoutPartialGroup()
    {
        var content = new TrackingPackageContent(
            ("lib/net11.0/First.dll", new byte[] { 1, 2 }),
            ("lib/net11.0/Second.dll", new byte[] { 3, 4 }));
        PackageRootBinding binding =
            Binding("Artifact.Budget.Sample", content);
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await workspace.RealizePackageAssemblyContextRolesAsync(
                        binding,
                        new PackageAssemblyContextRealizationOptions
                        {
                            MaxAggregateRetainedImageBytes = 6,
                            MaxAssemblyEntryBytes = 2,
                        },
                        TestContext.Current.CancellationToken));

        IReadOnlyList<ArtifactSetAdmissionFailure> admissionFailures =
            Assert.IsAssignableFrom<
                IReadOnlyList<ArtifactSetAdmissionFailure>>(
                    failure.Data[
                        "DotnetInspector.Artifacts.Workspaces.AdmissionFailures"]);
        ArtifactSetAdmissionFailure admissionFailure =
            Assert.Single(admissionFailures);
        Assert.Equal(
            ArtifactSetAdmissionFailureKind.Rejected,
            admissionFailure.Kind);
        Assert.Equal(
            "artifact.session.byte-limit",
            admissionFailure.Diagnostic.Code);
        Assert.Equal(2, content.EntryOpenRequests);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void MalformedAssets_UseSafeUniqueRejectionCarrierIdentities()
    {
        PackageRootRealization package = Selection(
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
        PackageRootRealization package = Selection(
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
        PackageRootRealization package = Selection(
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
        PackageRootRealization package = Selection(
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
        PackageRootRealization package = Selection(
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
    public void AssemblyCountLimit_IsCheckedBeforeEntryPreflightOrOpen()
    {
        string[] paths =
        [
            .. Enumerable.Range(0, 257).Select(
                index => $"lib/net11.0/Asset{index:D3}.dll"),
        ];
        var content = new CountingPackageContent(paths);
        var package = new PackageRootRealization(
            content,
            "Count.Sample",
            "1.0.0",
            Framework);
        using var workspace = new InspectionWorkspace();

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(
                () => workspace.RealizePackageAssemblyContextRoles(
                    [package],
                    new PackageAssemblyContextRealizationOptions
                    {
                        MaxAssembliesPerRole = 256,
                    },
                    TestContext.Current.CancellationToken));

        Assert.Contains(
            "assembly-count limit",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, content.EntryLengthRequests);
        Assert.Equal(0, content.EntryOpenRequests);
        Assert.Equal(0, GroupCount(workspace));
    }

    [Fact]
    public void DeclaredEntryBudget_FailureDoesNotExposeArtifactPath()
    {
        byte[] image =
            File.ReadAllBytes(typeof(PackageAssemblyContextRealizationTests).Assembly.Location);
        const string path = "lib/net11.0/Budget\u202e.Sample.dll";
        PackageRootRealization package = Selection(
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
        var package = new PackageRootRealization(
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
        PackageRootRealization package = Selection(
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

    static PackageRootRealization Selection(
        string packageId,
        params (string Path, byte[] Content)[] entries)
    {
        PackageRootRealization selection =
            RootSelection(packageId, entries);
        Assert.True(selection.AssetSelection.IsSelected);
        return selection;
    }

    static PackageRootRealization RootSelection(
        string packageId,
        params (string Path, byte[] Content)[] entries) =>
        RootSelection(packageId, Framework, entries);

    static PackageRootRealization RootSelection(
        string packageId,
        string targetFramework,
        params (string Path, byte[] Content)[] entries) =>
        new(
            new InMemoryPackageContent(
                Archive(entries),
                fromCache: false,
                producerKey: "tests"),
            packageId,
            "1.0.0",
            targetFramework);

    static PackageRootBinding Binding(
        string packageId,
        IPackageContent content)
    {
        const string version = "1.0.0";
        const string producer = "tests";
        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, version),
            content,
            producer,
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(
            payload,
            Framework);
    }

    static byte[] IntegrationAssembly(
        string assemblyName,
        string typeName)
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

    sealed class CountingPackageContent(
        IReadOnlyList<string> paths)
        : IPackageContent, IPackageContentEntryManifest
    {
        public int EntryLengthRequests { get; private set; }
        public int EntryOpenRequests { get; private set; }
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
            EntryOpenRequests++;
            stream = Stream.Null;
            return true;
        }

        public IEnumerable<string> EnumerateEntries() => paths;

        public bool TryGetEntryLength(
            string relativePath,
            out long length)
        {
            EntryLengthRequests++;
            length = 0;
            return true;
        }

        public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths() =>
        [
            .. paths.Select(path => new PackageContentEntry(path, 0)),
        ];
    }

    sealed class TrackingPackageContent(
        params (string Path, byte[] Content)[] entries)
        : IPackageContent
    {
        readonly (string Path, byte[] Content)[] _entries = entries;

        public int EntryOpenRequests { get; private set; }
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
            foreach ((string path, byte[] content) in _entries)
            {
                if (!relativePath.Equals(path, StringComparison.Ordinal))
                    continue;

                EntryOpenRequests++;
                stream = new MemoryStream(content, writable: false);
                return true;
            }

            stream = null;
            return false;
        }

        public IEnumerable<string> EnumerateEntries() =>
            _entries.Select(entry => entry.Path);
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
