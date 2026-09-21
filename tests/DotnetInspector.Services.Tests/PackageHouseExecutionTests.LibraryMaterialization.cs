using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using DotnetInspector.Libraries;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    private const string MaterializedPackageId = "system.text.json";
    private const string MaterializedApiPath =
        "ref/net10.0/System.Text.Json.dll";
    private const string MaterializedImplementationPath =
        "runtimes/linux-x64/lib/net10.0/System.Text.Json.dll";
    private const string MaterializedLibraryPath =
        "lib/net10.0/System.Text.Json.dll";
    private const string MaterializedDocumentationPath =
        "ref/net10.0/System.Text.Json.xml";
    private const string MaterializedPortablePdbPath =
        "runtimes/linux-x64/lib/net10.0/System.Text.Json.pdb";

    [Fact]
    public async Task CompileHandoffMaterializesOwnedLibraryWithImplementationAndDocumentation()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        byte[] documentation = ReadRealAsset("System.Text.Json.xml");
        byte[] portablePdb = "portable-pdb"u8.ToArray();
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedApiPath, assembly),
            (MaterializedImplementationPath, assembly),
            (MaterializedDocumentationPath, documentation),
            (MaterializedPortablePdbPath, portablePdb));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        PackageHouseLibraryMaterializationOutcome.Completed completed =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Same(settlement.Result, completed.Receipt.PackageResult);
        Assert.Same(handoff, completed.Receipt.Handoff);
        LibraryReference library = completed.Receipt.Library;
        Assert.Same(library, completed.Owner.Reference);
        Assert.Equal(4, library.Contents.Count);
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            library.ImplementationAssembly!.HasRole(
                LibraryContentRole.ImplementationAssembly));
        LibraryContentReference documentationCompanion =
            Assert.Single(
                library.Contents,
                contentReference =>
                    contentReference.HasRole(
                        LibraryContentRole
                            .CompiledXmlDocumentation));
        Assert.Same(
            library.ApiAssembly,
            documentationCompanion.AssociatedAssembly);
        LibraryContentReference portablePdbCompanion =
            Assert.Single(
                library.Contents,
                contentReference =>
                    contentReference.HasRole(
                        LibraryContentRole.PortablePdb));
        Assert.Same(
            library.ImplementationAssembly,
            portablePdbCompanion.AssociatedAssembly);
        var source = Assert.IsType<
            ExactLibrarySourceCoordinate.Package>(
                library.SourceCoordinate);
        Assert.Equal(handoff.Coordinate, source.PackageCoordinate);

        PackageHouseLibraryArtifactProvenance apiProvenance =
            Assert.IsType<PackageHouseLibraryArtifactProvenance>(
                library.ApiAssembly.Provenance);
        Assert.Same(handoff, apiProvenance.Handoff);
        Assert.Same(handoff.Asset, apiProvenance.AssociatedAsset);
        Assert.Equal(MaterializedApiPath, apiProvenance.PackagePath);
        Assert.Equal(
            PackageHouseLibraryArtifactRole.ApiAssembly,
            apiProvenance.Roles);
        PackageHouseLibraryArtifactProvenance implementationProvenance =
            Assert.IsType<PackageHouseLibraryArtifactProvenance>(
                library.ImplementationAssembly.Provenance);
        Assert.Same(
            handoff.ImplementationAsset,
            implementationProvenance.AssociatedAsset);
        Assert.Equal(
            MaterializedImplementationPath,
            implementationProvenance.PackagePath);
        PackageHouseLibraryArtifactProvenance documentationProvenance =
            Assert.IsType<PackageHouseLibraryArtifactProvenance>(
                documentationCompanion.Provenance);
        Assert.Same(
            handoff.Asset,
            documentationProvenance.AssociatedAsset);
        Assert.Equal(
            MaterializedDocumentationPath,
            documentationProvenance.PackagePath);
        PackageHouseLibraryArtifactProvenance portablePdbProvenance =
            Assert.IsType<PackageHouseLibraryArtifactProvenance>(
                portablePdbCompanion.Provenance);
        Assert.Same(
            handoff.ImplementationAsset,
            portablePdbProvenance.AssociatedAsset);
        Assert.Equal(
            MaterializedPortablePdbPath,
            portablePdbProvenance.PackagePath);
        Assert.Equal(
            PackageHouseLibraryArtifactRole
                .ImplementationPortablePdb,
            portablePdbProvenance.Roles);

        await completed.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        Assert.Equal(
            LibraryContentOwnerState.Released,
            completed.Owner.State);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task OneLibraryAssetServesBothAssemblyRolesWithOneChild()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly),
            (
                "lib/net10.0/System.Text.Json.xml",
                ReadRealAsset("System.Text.Json.xml")));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        PackageHouseLibraryMaterializationOutcome.Completed completed =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        LibraryReference library = completed.Receipt.Library;
        Assert.Equal(2, library.Contents.Count);
        Assert.Same(
            library.ApiAssembly,
            library.ImplementationAssembly);
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ImplementationAssembly));
        PackageHouseLibraryArtifactProvenance provenance =
            Assert.IsType<PackageHouseLibraryArtifactProvenance>(
                library.ApiAssembly.Provenance);
        Assert.Equal(
            PackageHouseLibraryArtifactRole.ApiAssembly
                | PackageHouseLibraryArtifactRole
                    .ImplementationAssembly,
            provenance.Roles);

        await completed.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MissingCompiledXmlProducesAssemblyOnlyLibrary()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedApiPath, assembly),
            (MaterializedImplementationPath, assembly));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        PackageHouseLibraryMaterializationOutcome.Completed completed =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(2, completed.Receipt.Library.Contents.Count);
        Assert.Empty(
            completed.Receipt.Library.CompanionCorrespondences);
        await completed.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ReferenceOnlyHandoffDoesNotInventImplementationContent()
    {
        InMemoryPackageContent content = CreatePackageContent(
            (
                MaterializedApiPath,
                ReadRealAsset("System.Text.Json.dll")));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        Assert.Null(handoff.ImplementationAsset);

        PackageHouseLibraryMaterializationOutcome.Completed completed =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Single(completed.Receipt.Library.Contents);
        Assert.Null(
            completed.Receipt.Library.ImplementationAssembly);
        Assert.False(
            completed.Receipt.Library.ApiAssembly.HasRole(
                LibraryContentRole.ImplementationAssembly));
        await completed.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task OverBudgetCompiledXmlIsNotClassifiedAsAbsent()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        byte[] sourceDocumentation =
            ReadRealAsset("System.Text.Json.xml");
        byte[] documentation =
            GC.AllocateUninitializedArray<byte>(
                assembly.Length + 1);
        sourceDocumentation.CopyTo(documentation, 0);
        documentation.AsSpan(
                sourceDocumentation.Length)
            .Fill((byte)' ');
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedLibraryPath, assembly),
            (
                "lib/net10.0/System.Text.Json.xml",
                documentation));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        var limits =
            new PackageHouseLibraryMaterializationLimits
            {
                MaxContentBytes = documentation.LongLength - 1,
                MaxRetainedBytes =
                    assembly.LongLength
                    + documentation.LongLength,
            };
        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    limits,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .ContentByteLimit,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ByteIdenticalSettlementCannotUseAnotherSettlementHandoff()
    {
        byte[] archive = CreatePackageArchive(
            (
                MaterializedLibraryPath,
                ReadRealAsset("System.Text.Json.dll")));
        var firstContent = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        var secondContent = new InMemoryPackageContent(
            archive,
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);
        await using HouseEnvironment firstEnvironment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        await using HouseEnvironment secondEnvironment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired firstSettlement, _) =
            await ExecuteMaterializationInputAsync(
                firstEnvironment,
                firstContent);
        (_, PackageHouseLibraryHandoff.Compile secondHandoff) =
            await ExecuteMaterializationInputAsync(
                secondEnvironment,
                secondContent);

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    firstSettlement,
                    secondHandoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .InvalidSettlement,
            ],
            terminal.Evidence.Failures);
        await firstEnvironment.AssertRootSettledAsync();
        await secondEnvironment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task AggregateContentLimitIsVisibleBeforePublication()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        InMemoryPackageContent content = CreatePackageContent(
            (MaterializedApiPath, assembly),
            (MaterializedImplementationPath, assembly));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        var limits =
            new PackageHouseLibraryMaterializationLimits
            {
                MaxContentBytes = assembly.LongLength,
                MaxRetainedBytes =
                    (2 * assembly.LongLength) - 1,
            };

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    limits,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .RetainedByteLimit,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimeDiscoveredContentLimitPreservesTypedFailure()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        var content = new ForwardOnlyPackageContent(
            CreatePackageContent(
                (MaterializedLibraryPath, assembly)));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        var limits =
            new PackageHouseLibraryMaterializationLimits
            {
                MaxContentBytes = assembly.LongLength - 1,
                MaxRetainedBytes = assembly.LongLength,
            };

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    limits,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .ContentByteLimit,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task RuntimeDiscoveredAggregateLimitPreservesTypedFailure()
    {
        byte[] assembly = ReadRealAsset("System.Text.Json.dll");
        var content = new ForwardOnlyPackageContent(
            CreatePackageContent(
                (MaterializedApiPath, assembly),
                (MaterializedImplementationPath, assembly)));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        var limits =
            new PackageHouseLibraryMaterializationLimits
            {
                MaxContentBytes = assembly.LongLength,
                MaxRetainedBytes =
                    (2 * assembly.LongLength) - 1,
            };

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    limits,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .RetainedByteLimit,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MalformedSelectedAssemblyFailsMetadataProjection()
    {
        InMemoryPackageContent content = CreatePackageContent(
            (
                MaterializedLibraryPath,
                "not a managed assembly"u8.ToArray()));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .MetadataProjection,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task MissingSelectedAssemblyIsVisibleAfterSettlement()
    {
        var content = new HideablePackageContent(
            CreatePackageContent(
                (
                    MaterializedLibraryPath,
                    ReadRealAsset("System.Text.Json.dll"))));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        content.HideEntries = true;

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .MissingPackageEntry,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task DifferentImplementationIdentityIsRejected()
    {
        InMemoryPackageContent content = CreatePackageContent(
            (
                MaterializedApiPath,
                ReadRealAsset("System.Text.Json.dll")),
            (
                MaterializedImplementationPath,
                File.ReadAllBytes(typeof(PackageHouse).Assembly.Location)));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        PackageHouseLibraryMaterializationOutcome.Terminal terminal =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Terminal>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));

        Assert.Equal(
            [
                PackageHouseLibraryMaterializationFailureKind
                    .AssemblyIdentityMismatch,
            ],
            terminal.Evidence.Failures);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task CancellationBeforeOwnerAcceptanceReleasesOpenedContent()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new CancelAfterOpenPackageContent(
            CreatePackageContent(
                (
                    MaterializedLibraryPath,
                    ReadRealAsset("System.Text.Json.dll"))),
            cancellation);
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken: cancellation.Token));

        Assert.Equal(1, content.OpenedStreamCount);
        Assert.Equal(1, content.DisposedStreamCount);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public async Task ArtifactRetirementWaitsForLibraryOwnerRetirement()
    {
        InMemoryPackageContent content = CreatePackageContent(
            (
                MaterializedLibraryPath,
                ReadRealAsset("System.Text.Json.dll")));
        await using HouseEnvironment environment =
            HouseEnvironment.CreateNuGetOrg(
                MaterializedPackageId,
                new SourceBehavior([Version]));
        (PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff) =
            await ExecuteMaterializationInputAsync(
                environment,
                content);
        PackageHouseLibraryMaterializationOutcome.Completed completed =
            Assert.IsType<
                PackageHouseLibraryMaterializationOutcome.Completed>(
                await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        LibraryOperationLease operation =
            Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                completed.Owner.IssueOperationLease(
                    completed.Receipt.Library))
            .Lease;

        Task ownerRetirement =
            completed.Owner.DisposeAsync().AsTask();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(ownerRetirement.IsCompleted);
        Assert.False(artifactRetirement.IsCompleted);

        operation.Dispose();
        await ownerRetirement;
        await artifactRetirement;
        Assert.Equal(
            LibraryContentOwnerState.Released,
            completed.Owner.State);
        await environment.AssertRootSettledAsync();
    }

    [Fact]
    public void MaterializationEvidenceContractsAreResourceFree()
    {
        Type[] types =
        [
            typeof(PackageHouseLibraryArtifactProvenance),
            typeof(PackageHouseLibraryMaterializationReceipt),
            typeof(PackageHouseLibraryMaterializationFailure),
            typeof(PackageHouseLibraryMaterializationOutcome.Terminal),
        ];

        foreach (Type type in types)
        {
            Assert.Null(
                type.GetCustomAttribute<ResourceOwnershipAttribute>());
            Assert.False(typeof(IDisposable).IsAssignableFrom(type));
            Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
            Assert.DoesNotContain(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field =>
                    typeof(IDisposable).IsAssignableFrom(
                        field.FieldType)
                    || typeof(IAsyncDisposable).IsAssignableFrom(
                        field.FieldType)
                    || typeof(Stream).IsAssignableFrom(
                        field.FieldType)
                    || typeof(Delegate).IsAssignableFrom(
                        field.FieldType));
        }
    }

    private static async Task<(
        PackageHouseSettlement.Acquired Settlement,
        PackageHouseLibraryHandoff.Compile Handoff)>
        ExecuteMaterializationInputAsync(
        HouseEnvironment environment,
        IPackageContent content)
    {
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    MaterializedPackageId,
                    Version)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(
                "net10.0",
                "linux-x64"),
            PackageHouseAssetSelectionKind.Compile,
            PackageHouseLibraryHandoffMode.SelectedLibraries);
        var store = new FixedPackageContentStore(content);
        PackageHouseSettlement.Acquired settlement =
            Assert.IsType<PackageHouseSettlement.Acquired>(
                await environment.CreateHouse((_, _) => store)
                    .ExecuteAsync(
                        request,
                        environment.IssueOperation(
                            request,
                            TestContext.Current.CancellationToken)));
        PackageHouseRealizationReceipt.Compile realization =
            Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                settlement.Result.Evidence.Realization);
        PackageHouseLibraryHandoff.Compile handoff =
            Assert.IsType<PackageHouseLibraryHandoff.Compile>(
                Assert.Single(realization.LibraryHandoffs));
        return (settlement, handoff);
    }

    private static InMemoryPackageContent CreatePackageContent(
        params (string Path, byte[] Content)[] entries) =>
        new(
            CreatePackageArchive(entries),
            fromCache: true,
            PackageProducerIdentity.NuGetOrg.Key);

    private static byte[] CreatePackageArchive(
        params (string Path, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    path,
                    CompressionLevel.NoCompression);
                using Stream destination = entry.Open();
                destination.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ReadRealAsset(string fileName) =>
        File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "PackageHouse",
                fileName));

    private sealed class FixedPackageContentStore(
        IPackageContent content) : IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            packageName.Equals(
                MaterializedPackageId,
                StringComparison.OrdinalIgnoreCase)
            && version.Equals(
                Version,
                StringComparison.OrdinalIgnoreCase)
            && allowedSourceKeys?.Contains(
                content.ProducerKey,
                StringComparer.Ordinal) is true
                ? content
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The fixed package content fixture must not download.");
    }

    private sealed class CancelAfterOpenPackageContent(
        InMemoryPackageContent inner,
        CancellationTokenSource cancellation)
        : IPackageContent, IPackageContentEntryManifest
    {
        public int OpenedStreamCount { get; private set; }
        public int DisposedStreamCount { get; private set; }
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            TryOpenEntry(
                relativePath,
                long.MaxValue,
                out stream);

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (!inner.TryOpenEntry(
                    relativePath,
                    maxExpandedBytes,
                    out Stream? opened))
            {
                stream = null;
                return false;
            }

            using (opened)
            {
                using var bytes = new MemoryStream();
                opened.CopyTo(bytes);
                stream = new DisposalTrackingMemoryStream(
                    bytes.ToArray(),
                    () => DisposedStreamCount++);
            }
            OpenedStreamCount++;
            cancellation.Cancel();
            return true;
        }

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();

        public bool TryGetEntryLength(
            string relativePath,
            out long length) =>
            inner.TryGetEntryLength(relativePath, out length);

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            inner.EnumerateEntriesWithLengths();
    }

    private sealed class HideablePackageContent(
        InMemoryPackageContent inner)
        : IPackageContent, IPackageContentEntryManifest
    {
        public bool HideEntries { get; set; }
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (HideEntries)
            {
                stream = null;
                return false;
            }

            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            if (HideEntries)
            {
                stream = null;
                return false;
            }

            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            HideEntries
                ? []
                : inner.EnumerateEntries();

        public bool TryGetEntryLength(
            string relativePath,
            out long length)
        {
            if (HideEntries)
            {
                length = 0;
                return false;
            }

            return inner.TryGetEntryLength(relativePath, out length);
        }

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            HideEntries
                ? []
                : inner.EnumerateEntriesWithLengths();
    }

    private sealed class ForwardOnlyPackageContent(
        InMemoryPackageContent inner) : IPackageContent
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
                inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
                [NotNullWhen(true)] out Stream? stream) =>
                inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
                string relativePath,
                [NotNullWhen(true)] out Stream? stream)
        {
            if (!inner.TryOpenEntry(
                    relativePath,
                    out Stream? opened))
            {
                stream = null;
                return false;
            }

            stream = new ForwardOnlyReadStream(opened);
            return true;
        }

        public IEnumerable<string> EnumerateEntries() =>
                inner.EnumerateEntries();
    }

    private sealed class ForwardOnlyReadStream(Stream inner)
        : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length =>
                throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(
                byte[] buffer,
                int offset,
                int count) =>
                inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
                inner.Read(buffer);

        public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default) =>
                inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(
                long offset,
                SeekOrigin origin) =>
                throw new NotSupportedException();

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
                inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class DisposalTrackingMemoryStream(
        byte[] content,
        Action onDispose) : MemoryStream(content, writable: false)
    {
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                onDispose();
            }
            base.Dispose(disposing);
        }
    }
}
