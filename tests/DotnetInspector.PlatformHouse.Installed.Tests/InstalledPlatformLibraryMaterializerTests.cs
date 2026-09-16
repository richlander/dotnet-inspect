using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Resources;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class InstalledPlatformLibraryMaterializerTests
{
    [Fact]
    public async Task
        InstalledSystemTextJson_TransfersLibraryAndArtifactAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        hive.CopyAssembly(
            hive.CreateImplementationFramework(implementationPath),
            implementationPath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity =
            ReadIdentity(referencePath);
        PlatformHouseRequest request = Request(
            adapter,
            identity,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);

        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(
            sourceOperations: 2,
            assemblies:
                reference.Value.Libraries.Count
                + implementation.Value.Libraries.Count,
            bytes:
                reference.Value.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Value.Libraries.Sum(
                    static library => library.ContentLength));

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request,
                        reference,
                        implementation,
                        consumed));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            library.ImplementationAssembly!.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.True(
            AssemblyReferenceIdentity.EquivalentComparer.Equals(
                identity,
                Assert.IsType<ManagedMetadataIdentity.Assembly>(
                        library.ApiAssembly.AssemblyIdentity)
                    .Identity));
        var referenceProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ApiAssembly.ArtifactReference.Provenance);
        Assert.Same(
            reference.Contribution,
            referenceProvenance.Contribution);
        var installedReference =
            Assert.IsType<InstalledReferenceArtifactProvenance>(
                referenceProvenance.SourceProvenance);
        Assert.Same(
            reference.Value.Generation,
            installedReference.SourceGeneration);
        Assert.Same(
            reference.Value.Coordinate,
            installedReference.Coordinate);
        Assert.True(
            AssemblyReferenceIdentity.EquivalentComparer.Equals(
                reference.Value.Libraries.Single().Identity,
                installedReference.Identity));
        var implementationProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ImplementationAssembly.ArtifactReference
                    .Provenance);
        Assert.Same(
            implementation.Contribution,
            implementationProvenance.Contribution);
        var installedImplementation =
            Assert.IsType<InstalledImplementationArtifactProvenance>(
                implementationProvenance.SourceProvenance);
        Assert.Same(
            implementation.Value.Generation,
            installedImplementation.SourceGeneration);
        Assert.Same(
            implementation.Value.Coordinate,
            installedImplementation.Coordinate);
        Assert.Equal(
            implementation.Value.Libraries.Single().ContentDigest,
            installedImplementation.ContentDigest);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        using LibraryOperationLease operation = Issued(
            completed.Library.Owner,
            library);
        Assert.Equal(
            ((byte)'M', (byte)'M'),
            operation.SnapshotPair(
                library.ApiAssembly,
                library.ImplementationAssembly,
                static (view, _) =>
                    (view.First.Content[0],
                        view.Second.Content[0]),
                cancellationToken));
        operation.Dispose();
        await completed.Library.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task InstalledReferenceOnly_ClosesOneApiRole()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: reference.Value.Libraries.Count,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.Null(
            completed.Library.Value.Reference
                .ImplementationAssembly);
        Assert.Single(
            completed.Library.Value.Reference.Contents);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task InstalledImplementationOnly_AssignsBothRoles()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string implementationPath =
            typeof(JsonSerializer).Assembly.Location;
        hive.CopyAssembly(
            hive.CreateImplementationFramework(implementationPath),
            implementationPath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(implementationPath),
            PlatformViewDemand.Implementation,
            cancellationToken);
        var implementation = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(request));

        var completed = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Completed>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeImplementationAsync(
                        request,
                        implementation,
                        Consumed(
                            sourceOperations: 1,
                            assemblies:
                                implementation.Value.Libraries.Count,
                            bytes: implementation.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.Same(
            library.ApiAssembly,
            library.ImplementationAssembly);
        Assert.Equal(2, library.ApiAssembly.Roles.Count);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task ForeignSuccessfulResult_IsRejectedBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity =
            ReadIdentity(referencePath);
        PlatformHouseRequest sourceRequest = Request(
            adapter,
            identity,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformHouseRequest materializationRequest = Request(
            adapter,
            identity,
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest));

        var terminal = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Terminal>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        materializationRequest,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
    }

    [Fact]
    public async Task MissingPriorSourceEvidence_ReturnsTerminalAfterCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellationToken,
            installedFirst: false);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));

        var terminal = Assert.IsType<
            InstalledPlatformLibraryMaterializationResult.Terminal>(
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
    }

    [Fact]
    public async Task CancellationPrecedesArtifactOwnership()
    {
        using var hive = new TestHive();
        string referencePath = FindReferenceAssembly(
            "System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            referencePath);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        using var cancellation = new CancellationTokenSource();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(referencePath),
            PlatformViewDemand.Reference,
            cancellation.Token);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await InstalledPlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(
                            sourceOperations: 1,
                            assemblies: 1,
                            bytes: reference.Value.Libraries.Sum(
                                static library =>
                                    library.ContentLength))));
    }

    [Fact]
    public void InstalledArtifactProvenance_IsResourceFree()
    {
        Type[] types =
        [
            typeof(InstalledReferenceArtifactProvenance),
            typeof(InstalledImplementationArtifactProvenance),
            typeof(InstalledPlatformLibraryMaterializationResult.Terminal),
        ];

        foreach (Type type in types)
        {
            Assert.Null(
                type.GetCustomAttribute<ResourceOwnershipAttribute>());
            Assert.All(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field =>
                {
                    Assert.False(
                        typeof(IDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(IAsyncDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Stream).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Delegate).IsAssignableFrom(
                            field.FieldType));
                });
        }
    }

    [Fact]
    public void SuccessfulSourcePairing_IsAdapterIssued()
    {
        Type[] succeededResults =
        [
            typeof(InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded),
            typeof(InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded),
        ];

        foreach (Type result in succeededResults)
        {
            Assert.Empty(
                result.GetConstructors(
                    BindingFlags.Instance
                    | BindingFlags.Public));
        }
    }

    static PlatformHouseRequest Request(
        InstalledPlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        PlatformViewDemand view,
        CancellationToken cancellationToken,
        bool installedFirst = true)
    {
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.Capabilities.ReferenceRealization,
                        installedFirst)));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.Capabilities.ImplementationRealization,
                        installedFirst)));
        }

        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("installed-library"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("installed-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "installed-policy"),
                selections),
            Work(),
            cancellationToken);
    }

    static IReadOnlyList<PlatformSourceCapabilityIdentity> Capabilities(
        PlatformSourceCapabilityIdentity installed,
        bool installedFirst) =>
        installedFirst
            ? [installed]
            :
            [
                PlatformSourceCapabilityIdentity.Create("prior"),
                installed,
            ];

    static PlatformHouseWorkBudget Work() =>
        new(
            maxSourceOperations: 2,
            maxTargetCandidates: 0,
            maxAssemblies: 8,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 64 * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromSeconds(30));

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations,
        int assemblies,
        long bytes) =>
        new(
            sourceOperations,
            targetCandidates: 0,
            assemblies,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));

    static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    static string FindReferenceAssembly(string fileName)
    {
        DirectoryInfo runtimeVersion =
            new FileInfo(typeof(object).Assembly.Location).Directory
            ?? throw new InvalidOperationException(
                "The runtime assembly location has no directory.");
        DirectoryInfo dotnetRoot =
            runtimeVersion.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException(
                "The runtime assembly location is outside a dotnet root.");
        string referenceRoot = Path.Combine(
            dotnetRoot.FullName,
            "packs",
            "Microsoft.NETCore.App.Ref");
        return Directory.EnumerateFiles(
                referenceRoot,
                fileName,
                SearchOption.AllDirectories)
            .Where(
                path => string.Equals(
                    new FileInfo(path).Directory?.Name,
                    "net11.0",
                    StringComparison.Ordinal))
            .OrderByDescending(
                static path => path,
                StringComparer.Ordinal)
            .First();
    }
}
