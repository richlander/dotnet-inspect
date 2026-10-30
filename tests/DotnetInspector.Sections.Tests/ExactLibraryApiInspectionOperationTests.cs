using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class ExactLibraryApiInspectionOperationTests
{
    const string PackageId = "exact-library-api.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task ExecuteAsync_ReturnsExactDetachedLibrarySurface()
    {
        var store = await CachedStoreAsync();
        var request = new ExactLibraryApiInspectionRequest(
            PackageId,
            Version,
            Framework,
            "DotnetInspector.Sections.dll");
        ExactLibraryApiInspectionExecution execution =
            await ExecuteColdAsync(
                request,
                store);

        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;
        ExactLibraryApiInspectionResult result = envelope.Content;
        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Available,
            result.Outcome);
        Assert.Equal(
            "DotnetInspector.Sections.dll",
            result.Asset?.AssemblyName);
        Assert.Equal(
            ExactLibraryApiAssetKind.Reference,
            result.Asset?.Kind);
        Assert.Equal(
            NuGetCache.GetSourceKey(SourceUrl),
            result.Source?.Producer);
        Assert.Equal(
            typeof(ExactLibraryApiInspectionOperation).Module.ModuleVersionId,
            result.Assembly?.ModuleVersionId);
        Assert.Equal(
            typeof(ExactLibraryApiInspectionOperation).Assembly.GetName().Name,
            result.Assembly?.Identity.Name);
        Assert.Equal(
            typeof(ExactLibraryApiInspectionOperation).Assembly.GetName().Version,
            result.Assembly?.Identity.Version);
        Assert.NotNull(execution.Surface);
        Assert.Equal(
            execution.Surface.PublicTypeCount,
            result.Inventory?.TypeKinds.Sum(facet => facet.Count));
        Assert.NotEmpty(result.Inventory?.Namespaces ?? []);
        Assert.IsType<InspectionShare.Available>(envelope.Share);
        AssertDetachedContract();
    }

    [Fact]
    public async Task ExactAssetIdAndColdSelectionProduceEquivalentResults()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextLoadOptions options = LoadOptions(client, store);
        WorkspaceContextInput input = Input();
        PackageRootBinding root =
            Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    input,
                    options,
                    TestContext.Current.CancellationToken))
            .Root;
        Assert.NotNull(root.Root.AssetSelection);
        PackageCompileAsset asset = Assert.Single(
            root.Root.AssetSelection.Assets,
            candidate => candidate.AssemblyName
                == "DotnetInspector.Sections.dll");
        var coldRequest = new ExactLibraryApiInspectionRequest(
            PackageId,
            Version,
            Framework,
            asset.AssemblyName);
        var exactRequest = new ExactLibraryApiInspectionRequest(
            PackageId,
            Version,
            Framework,
            asset.Id,
            ExactLibraryApiSelectionKind.AssetId);

        ExactLibraryApiInspectionExecution cold =
            await ExecuteColdAsync(
                coldRequest,
                store);
        await using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            await workspace.RealizePackageAssemblyContextRolesAsync(
                root,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        ExactLibraryApiInspectionExecution retained =
            ExactLibraryApiInspectionOperation.Execute(
                root,
                realization,
                exactRequest,
                ExactLibraryApiInspectionOperation.DefaultLimits);

        Assert.Equal(
            JsonSerializer.Serialize(cold.Surface),
            JsonSerializer.Serialize(retained.Surface));
        Assert.Equal(
            cold.Inspection.Content.Assembly,
            retained.Inspection.Content.Assembly);
        Assert.Equal(
            cold.Inspection.Content.Source,
            retained.Inspection.Content.Source);
        Assert.Equal(
            cold.Inspection.Content.Asset,
            retained.Inspection.Content.Asset);
        Assert.Equal(
            JsonSerializer.Serialize(cold.Inspection.Content.Inventory),
            JsonSerializer.Serialize(retained.Inspection.Content.Inventory));
        Assert.Equal(
            cold.Inspection.Diagnostics,
            retained.Inspection.Diagnostics);
    }

    [Fact]
    public async Task RetainedToolEntryReturnsExactDetachedLibrarySurface()
    {
        const string selectedFramework = "net8.0";
        const string asset =
            "tools/net8.0/any/DotnetInspector.Sections.dll";
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            Archive(
                (asset,
                    await File.ReadAllBytesAsync(
                        typeof(ExactLibraryApiInspectionOperation)
                            .Assembly
                            .Location,
                        TestContext.Current.CancellationToken))));
        using var client = new HttpClient(new FailingHandler());
        PackageRootBinding root =
            Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    Input(),
                    LoadOptions(client, store),
                    TestContext.Current.CancellationToken))
            .Root;
        Assert.Equal(
            PackageCompileAssetSelectionStatus.NoCompileAssets,
            Assert.IsType<PackageCompileAssetSelection>(root.Root.AssetSelection).Status);

        ExactLibraryApiInspectionExecution execution =
            await ExactLibraryApiInspectionOperation.ExecuteToolEntryAsync(
                PackageInspectionInput.CreateFromBinding(root),
                new(
                    PackageId,
                    Version,
                    Framework,
                    asset,
                    ExactLibraryApiSelectionKind.AssetId),
                selectedFramework,
                new()
                {
                    MaxAssembliesPerRole = 1,
                    MaxAggregateRetainedImageBytes = 64 * 1024 * 1024,
                    MaxAssemblyEntryBytes = 64 * 1024 * 1024,
                    RequireDeclaredEntryLengths = true,
                },
                ExactLibraryApiInspectionOperation.DefaultLimits,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Available,
            execution.Inspection.Content.Outcome);
        Assert.Equal(asset, execution.Inspection.Content.Asset?.Id);
        Assert.Equal(
            Framework,
            execution.Inspection.Content.RequestedTargetFramework);
        Assert.Equal(
            Framework,
            execution.Inspection.Content.Source?.Framework);
        Assert.Equal(
            selectedFramework,
            execution.Inspection.Content.Asset?.TargetFramework);
        Assert.Equal(
            ExactLibraryApiAssetKind.Tool,
            execution.Inspection.Content.Asset?.Kind);
        Assert.Equal(
            typeof(ExactLibraryApiInspectionOperation).Module.ModuleVersionId,
            execution.Inspection.Content.Assembly?.ModuleVersionId);
        Assert.True(
            execution.Inspection.Content.Inventory?.PublicTypeCount > 0);
        Assert.IsType<InspectionShare.Available>(
            execution.Inspection.Share);
    }

    [Fact]
    public async Task
        PackageNamespaceDiscoveryReturnsDetachedExactLibraryDeclarations()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = Input();
        PackageRootBinding root =
            Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    input,
                    LoadOptions(client, store),
                    TestContext.Current.CancellationToken))
            .Root;
        InspectionEnvelope<PackageNamespaceDiscoveryOutcome> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using PackageAssemblyContextRealization realization =
                await workspace.RealizePackageAssemblyContextRolesAsync(
                    root,
                    cancellationToken:
                        TestContext.Current.CancellationToken);
            inspection =
                await PackageNamespaceDiscoveryInspection.ExecuteAsync(
                    realization,
                    new(
                        "DotnetInspector.Queries.Definitions",
                        new(
                            maxTypes: 500_000,
                            maxMembers: 0,
                            maxInspectionFailures: 10_000,
                            maxTypeForwarders: 100_000,
                            maxMetadataRows: int.MaxValue,
                            maxRetainedTextCharacters: int.MaxValue),
                        new(
                            maxCapturedImageBytes:
                                512L * 1024 * 1024,
                            maxRetainedArtifactBytes:
                                512L * 1024 * 1024)),
                    TestContext.Current.CancellationToken);
        }

        Assert.True(inspection.Content.IsComplete);
        Assert.Empty(inspection.Diagnostics);
        PackageNamespaceDiscoveryHit hit =
            Assert.Single(inspection.Content.Hits);
        Assert.Equal(PackageId, hit.PackageId);
        Assert.Equal(Version, hit.PackageVersion);
        Assert.Equal("DotnetInspector.Queries", hit.Library);
        Assert.Equal(
            "DotnetInspector.Queries.Definitions",
            hit.Namespace);
        Assert.Contains(
            hit.Declarations,
            static declaration =>
                declaration.Identity.ToMetadataFullName()
                    == "DotnetInspector.Queries.Definitions.WorkspaceSharePacket");
        Assert.All(
            hit.Declarations,
            static declaration =>
                Assert.Equal(
                    "DotnetInspector.Queries.Definitions",
                    declaration.Namespace.ToString()));
        AssertDetachedContract();
    }

    [Fact]
    public async Task MissingLibraryRetainsTypedFailureAndShare()
    {
        var store = await CachedStoreAsync();

        ExactLibraryApiInspectionExecution execution =
            await ExecuteColdAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Missing.dll"),
                store);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.NotFound,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code == "exact-library-api.not-found");
        Assert.IsType<InspectionShare.Available>(envelope.Share);
    }

    [Fact]
    public async Task AmbiguousLibraryRetainsTypedFailureAndShare()
    {
        byte[] library = await File.ReadAllBytesAsync(
            typeof(ExactLibraryApiInspectionOperation).Assembly.Location,
            TestContext.Current.CancellationToken);
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            Archive(
                ($"ref/{Framework}/a/DotnetInspector.Sections.dll", library),
                ($"ref/{Framework}/b/DotnetInspector.Sections.dll", library)));

        ExactLibraryApiInspectionExecution execution =
            await ExecuteColdAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "DotnetInspector.Sections.dll"),
                store);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Ambiguous,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code == "exact-library-api.ambiguous");
        Assert.IsType<InspectionShare.Available>(envelope.Share);
    }

    [Fact]
    public async Task CatalogProjectionDoesNotDemandConstraintDependencies()
    {
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            Archive(
                ($"ref/{Framework}/Constraint.dll",
                    BuildModuleConstraintAssembly())));

        ExactLibraryApiInspectionExecution execution =
            await ExecuteColdAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Constraint.dll"),
                store);

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Available,
            execution.Inspection.Content.Outcome);
        Assert.True(execution.Inspection.Content.IsComplete);
        Assert.Empty(execution.Inspection.Content.Failures);
        Assert.Empty(execution.Inspection.Diagnostics);
    }

    [Fact]
    public async Task ProjectionBoundIsVisibleAndNotSuccessShaped()
    {
        var store = await CachedStoreAsync();

        ExactLibraryApiInspectionExecution execution =
            await ExecuteColdAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "DotnetInspector.Sections.dll"),
                store,
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 1,
                    maxTypes: 1,
                    maxMembers: 1,
                    maxInspectionFailures: 10,
                    maxTypeForwarders: 10,
                    maxMetadataRows: 10_000));
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(execution.Surface);
        Assert.NotNull(envelope.Content.Truncation);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "exact-library-api.projection-truncated");
    }

    [Fact]
    public async Task MalformedLibraryIsVisibleAsParticipantFailure()
    {
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            Archive(
                ($"ref/{Framework}/Malformed.dll",
                    [0x4d, 0x5a, 0, 1])));

        ExactLibraryApiInspectionExecution execution =
            await ExecuteColdAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Malformed.dll"),
                store);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(execution.Surface);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "exact-library-api.participant-unavailable");
    }

    static WorkspaceContextLoadOptions LoadOptions(
        HttpClient client,
        IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

    static async Task<ExactLibraryApiInspectionExecution> ExecuteColdAsync(
        ExactLibraryApiInspectionRequest request,
        IPackageStore store,
        ApiSurfaceProjectionLimits? limits = null)
    {
        var authorization =
            new UniformPackageSourceAuthorization([Source]);
        ConfiguredPackageAuthority authority = Assert.Single(
            authorization.AuthorizeSourcesFor(PackageId).Authorities);
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                new FailingHandler());
        await using PackageSourceSettlementLease source =
            PackageSourceSettlementService.IssueLease(_ => client);
        var house = new PackageHouse(
            authorization,
            PackagePayloadAcquisitionPlan.ForContentQueries(
                (_, _) => store));
        using PackageSourceOperationLease operation =
            source.IssueOperationLease(
                TestContext.Current.CancellationToken);
        return limits is null
            ? await ExactLibraryApiInspectionOperation.ExecuteAsync(
                    request,
                    house,
                    operation,
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(false)
            : await ExactLibraryApiInspectionOperation.ExecuteAsync(
                    request,
                    house,
                    operation,
                    limits,
                    TestContext.Current.CancellationToken)
                .ConfigureAwait(false);
    }

    static WorkspaceContextInput Input() =>
        new()
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    PackageId,
                    Version,
                    Framework),
            ],
        };

    static async Task<IPackageStore> CachedStoreAsync()
    {
        var store = new InMemoryPackageStore();
        byte[] package = Archive(
            ($"ref/{Framework}/DotnetInspector.Sections.dll",
                await File.ReadAllBytesAsync(
                    typeof(ExactLibraryApiInspectionOperation)
                        .Assembly
                        .Location,
                    TestContext.Current.CancellationToken)),
            ($"ref/{Framework}/DotnetInspector.Queries.dll",
                await File.ReadAllBytesAsync(
                    typeof(ExactLibraryApiInspectionQuery)
                        .Assembly
                        .Location,
                    TestContext.Current.CancellationToken)));
        await CommitAsync(store, package);
        return store;
    }

    static async Task CommitAsync(
        IPackageStore store,
        byte[] package)
    {
        foreach (string producerKey in FixtureProducerKeys())
        {
            await store.CommitAsync(
                PackageId,
                Version,
                producerKey,
                new MemoryStream(package),
                TestContext.Current.CancellationToken);
        }
    }

    static string[] FixtureProducerKeys()
    {
        using IPackageSourceClient client =
            PackageSourceClientFactory.Create(
                Source,
                PackageSourceAssociation.Create(),
                new FailingHandler());
        return
        [
            NuGetCache.GetSourceKey(SourceUrl),
            client.Source.Producer.Key,
        ];
    }

    static byte[] BuildModuleConstraintAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Constraint.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Constraint"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        ModuleReferenceHandle module =
            metadata.AddModuleReference(
                metadata.GetOrAddString("Other.netmodule"));
        TypeReferenceHandle constraint =
            metadata.AddTypeReference(
                module,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Constraint"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle holder =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Holder`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        GenericParameterHandle parameter =
            metadata.AddGenericParameter(
                holder,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
        metadata.AddGenericParameterConstraint(parameter, constraint);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static void AssertDetachedContract()
    {
        Type[] forbidden =
        [
            typeof(InspectionWorkspace),
            typeof(PackageRootBinding),
            typeof(PackageRootRealization),
            typeof(PackageAssemblyContextRealization),
            typeof(WorkspaceRealizationOperationLease),
            typeof(Stream),
            typeof(Delegate),
        ];
        Type[] contract =
        [
            typeof(InspectionEnvelope<ExactLibraryApiInspectionResult>),
            typeof(ExactLibraryApiInspectionExecution),
            typeof(ExactLibraryApiQueryExecution),
            typeof(ExactLibraryApiInspectionResult),
            typeof(ExactLibraryApiSourceCoordinate),
            typeof(ExactLibraryApiAsset),
            typeof(ExactLibraryApiAssemblyReferenceIdentity),
            typeof(ExactLibraryApiAssemblyIdentity),
            typeof(ExactLibraryApiInventory),
            typeof(ExactLibraryApiInspectionFailure),
            typeof(PackageNamespaceDiscoveryRequest),
            typeof(PackageNamespaceDiscoveryHit),
            typeof(PackageNamespaceDiscoveryOutcome),
        ];
        foreach (Type type in contract)
        {
            foreach (System.Reflection.PropertyInfo property
                     in type.GetProperties())
            {
                Assert.DoesNotContain(
                    forbidden,
                    candidate =>
                        candidate.IsAssignableFrom(property.PropertyType));
            }
        }

        Assert.Equal(
            typeof(ExactLibraryApiAssemblyReferenceIdentity),
            typeof(ExactLibraryApiAssemblyIdentity)
                .GetProperty(nameof(ExactLibraryApiAssemblyIdentity.Identity))!
                .PropertyType);
        Assert.Equal(
            typeof(ExactLibraryApiAssemblyReferenceIdentity?),
            typeof(ExactLibraryApiInspectionFailure)
                .GetProperty(nameof(ExactLibraryApiInspectionFailure.SubjectAssembly))!
                .PropertyType);
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected request to {request.RequestUri}.");
    }
}
