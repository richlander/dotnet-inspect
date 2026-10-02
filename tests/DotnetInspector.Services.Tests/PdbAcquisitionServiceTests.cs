using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Services.Tests;

public class PdbAcquisitionServiceTests
{
    [Fact]
    public async Task SelectedPackageDescriptor_OverridesCallerPackageFallback()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Package(
                "Supplier.Symbols",
                "2.0.0",
                "net10.0",
                rid: null));
        using var source = SourceLinkService.Open(assembly);
        var handler = new SymbolPackageHandler(
            BuildSnupkg(
                source.Context.PdbId!.PdbFileName,
                pdbBytes));
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            fallbackPackageName: "Root.Symbols",
            fallbackPackageVersion: "1.0.0");

        Assert.True(source.HasPdb);
        Uri request = Assert.Single(
            handler.RequestUris,
            static uri => uri.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "supplier.symbols.2.0.0.snupkg",
            request.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "root.symbols",
            request.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("project")]
    [InlineData("designated")]
    public async Task SelectedLocalOrProjectDescriptor_UsesCallerPackageFallback(
        string provenanceKind)
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            provenanceKind switch
            {
                "local" =>
                    AssemblyResolutionProvenance.Local("test"),
                "project" =>
                    AssemblyResolutionProvenance.Project(
                        "test.csproj",
                        "net10.0",
                        rid: null),
                "designated" =>
                    AssemblyResolutionProvenance.Designated("test"),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(provenanceKind)),
            });
        using var source = SourceLinkService.Open(assembly);
        var handler = new SymbolPackageHandler(
            BuildSnupkg(
                source.Context.PdbId!.PdbFileName,
                pdbBytes));
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            fallbackPackageName: "Root.Symbols",
            fallbackPackageVersion: "1.0.0");

        Assert.True(source.HasPdb);
        Uri request = Assert.Single(
            handler.RequestUris,
            static uri => uri.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "root.symbols.1.0.0.snupkg",
            request.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectedPlatformDescriptor_IgnoresCallerPackageFallback()
    {
        var (assembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source = SourceLinkService.Open(assembly);
        var handler = new SymbolPackageHandler([]);
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken,
            fallbackPackageName: "Root.Symbols",
            fallbackPackageVersion: "1.0.0");

        Assert.NotEmpty(handler.RequestUris);
        Assert.DoesNotContain(
            handler.RequestUris,
            static uri => uri.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            handler.RequestUris,
            static uri => uri.AbsolutePath.Contains(
                "root.symbols",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PathlessParticipant_AcquiresMatchingPdbThroughInMemoryStore()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(
            File.Exists(pdbPath),
            $"Expected test PDB at {pdbPath}");

        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        AssemblyReferenceIdentity identity =
            ReadIdentity(assemblyBytes);
        var assembly =
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        Assert.True(source.Context.NeedsPdb);

        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        var handler = new SymbolPackageHandler(snupkg);
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            new InMemoryPdbStore(),
            new UniformPackageSourceAuthorization(
                [NuGetFetch.PackageSource.NuGetOrg]),
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken);

        Assert.True(source.HasPdb);
        Assert.Null(source.Context.PortablePdbPath);
        Assert.NotEmpty(
            source.Context.EnumeratePdbDocuments());
        Assert.Single(handler.RequestUris);
        Assert.EndsWith(
            ".snupkg",
            handler.RequestUris[0].AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescriptorAcquisition_RequiresExplicitHostCapabilities()
    {
        var overload =
            Assert.Single(
                typeof(PdbAcquisitionService).GetMethods(),
                method =>
                {
                    var parameters = method.GetParameters();
                    return parameters.Length > 1
                        && parameters[1].ParameterType
                            == typeof(ResolvedAssemblyReference)
                        && parameters.Any(
                            parameter => parameter.ParameterType
                                == typeof(IPdbStore));
                });
        var parameters = overload.GetParameters();

        Assert.False(
            Assert.Single(
                parameters,
                parameter => parameter.ParameterType
                    == typeof(IPdbStore))
                .IsOptional);
        Assert.False(
            Assert.Single(
                parameters,
                parameter => parameter.ParameterType
                    == typeof(IPackageSourceAuthorization))
                .IsOptional);
    }

    [Fact]
    public async Task PathlessParticipant_DesktopOverloadDoesNotAcquire()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        var handler = new SymbolPackageHandler(snupkg);
        using var client = new HttpClient(handler);

        await PdbAcquisitionService.AcquireAsync(
            source.Context,
            assembly,
            client,
            log: null,
            cancellationToken:
                TestContext.Current.CancellationToken);

        Assert.False(source.HasPdb);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task PathlessParticipant_StoreReadFailureIsVisible()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        using var client =
            new HttpClient(
                new SymbolPackageHandler(snupkg));
        var evidence =
            new PortablePdbAcquisitionEvidenceCollector();

        PdbStoreAcquisitionException exception =
            await Assert.ThrowsAsync<PdbStoreAcquisitionException>(
            () => PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                new FailingStoredReadPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken,
                evidence: evidence));
        Assert.Equal(
            PortablePdbStoreFailureKind.ReadFailed,
            exception.StoreFailure);
        PortablePdbAcquisitionEvidenceDocument document =
            evidence.ToDocument();
        Assert.Equal(
            PortablePdbExternalAcquisitionOutcome.Failed,
            document.Outcome);
        Assert.Equal(
            PortablePdbStoreFailureKind.ReadFailed,
            document.StoreFailure);
        Assert.False(document.FromCache);
        Assert.Equal(
            "nuget.org",
            document.SymbolServer);
        Assert.NotEmpty(document.NetworkAttempts);
    }

    [Fact]
    public async Task PathlessParticipant_ProviderFailureIsVisible()
    {
        var (assembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Package(
                "Example.Symbols",
                "1.0.0",
                "net10.0",
                rid: null));
        using var source = SourceLinkService.Open(assembly);
        using var client =
            new HttpClient(
                new SymbolPackageHandler(
                    new byte[65]));
        var evidence =
            new PortablePdbAcquisitionEvidenceCollector();

        PortablePdbAcquisitionResult? result =
            await PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken,
                limits:
                    new SymbolAcquisitionLimits(
                        maxSymbolPackageBytes: 64,
                        maxPortablePdbBytes: 64,
                        maxSymbolPackageEntries: 8),
                evidence: evidence);

        var unavailable =
            Assert.IsType<
                PortablePdbAcquisitionResult.Unavailable>(
                    result);
        Assert.Equal(
            PortablePdbAcquisitionFailureKind
                .ExternalProviderFailed,
            unavailable.AcquisitionFailure);
        PortablePdbAcquisitionEvidenceDocument document =
            evidence.ToDocument();
        Assert.Equal(
            PortablePdbExternalAcquisitionOutcome.Failed,
            document.Outcome);
        Assert.Contains(
            document.NetworkAttempts,
            attempt =>
                attempt.Outcome
                == PortablePdbNetworkAttemptOutcome.TooLarge);
    }

    [Fact]
    public async Task PathlessParticipant_StoreWriteFailureIsVisible()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        using var client =
            new HttpClient(
                new SymbolPackageHandler(snupkg));

        PdbStoreAcquisitionException exception =
            await Assert.ThrowsAsync<PdbStoreAcquisitionException>(
            () => PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                new FailingStoredWritePdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            PortablePdbStoreFailureKind.PublicationNotRetained,
            exception.StoreFailure);
    }

    [Fact]
    public async Task PathlessParticipant_LocalPathFailurePrecedesOwnedStreamOpen()
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        var assembly =
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                AssemblyResolutionProvenance.Package(
                    "Example.Symbols",
                    "1.0.0",
                    "net10.0",
                    rid: null));
        using var source = SourceLinkService.Open(assembly);
        byte[] snupkg =
            BuildSnupkg(
                Path.GetFileName(pdbPath),
                File.ReadAllBytes(pdbPath));
        using var client =
            new HttpClient(
                new SymbolPackageHandler(snupkg));
        var store = new ThrowingLocalPathPdbStore();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => PdbAcquisitionService.AcquireAsync(
                source.Context,
                assembly,
                client,
                store,
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]),
                log: null,
                cancellationToken:
                    TestContext.Current.CancellationToken));

        Assert.Equal(1, store.OpenCount);
        Assert.True(Assert.Single(store.OpenedStreams).IsDisposed);
    }

    [Fact]
    public async Task PlatformSettlement_AcquiresRepeatablePathlessContent()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        var handler = new PlatformSymbolHandler(pdbBytes);
        using var client = new HttpClient(handler);
        var request =
            new PortablePdbSettlementRequest(
                source.Context,
                assembly,
                client,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg]));

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                request,
                TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(
                    result);
        Assert.Equal(
            PortablePdbSettlementSource.MicrosoftSymbolServer,
            acquired.Source);
        Assert.Equal(
            PortablePdbPositiveStoreDisposition.Published,
            acquired.PositiveStore);
        Assert.True(acquired.NetworkOccurred);
        Assert.Collection(
            acquired.Receipts,
            receipt =>
            {
                Assert.Equal(
                    PortablePdbSettlementCandidate.Embedded,
                    receipt.Candidate);
                Assert.Equal(
                    PortablePdbSettlementAttemptOutcome.Unavailable,
                    receipt.Outcome);
            },
            receipt =>
            {
                Assert.Equal(
                    PortablePdbSettlementCandidate.PositiveStore,
                    receipt.Candidate);
                Assert.Equal(
                    PortablePdbSettlementAttemptOutcome.Unavailable,
                    receipt.Outcome);
            },
            receipt =>
            {
                Assert.Equal(
                    PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer,
                    receipt.Candidate);
                Assert.Equal(
                    PortablePdbSettlementAttemptOutcome.Acquired,
                    receipt.Outcome);
            });

        await using Stream first =
            await acquired.Content.OpenReadAsync(
                TestContext.Current.CancellationToken);
        await using Stream second =
            await acquired.Content.OpenReadAsync(
                TestContext.Current.CancellationToken);
        Assert.Equal(pdbBytes, ReadAllBytes(first));
        Assert.Equal(pdbBytes, ReadAllBytes(second));

        await acquired.LoadIntoAsync(
            source.Context,
            TestContext.Current.CancellationToken);
        Assert.True(source.HasPdb);
        Assert.Null(source.Context.PortablePdbPath);
    }

    [Fact]
    public async Task PlatformSettlement_WarmStoreDoesNotRequestNetwork()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        var store = new RecordingPdbStore();
        var firstHandler =
            new PlatformSymbolHandler(pdbBytes);
        using (var firstClient = new HttpClient(firstHandler))
        using (var firstSource =
               SourceLinkService.OpenEmbeddedPdbOnly(assembly))
        {
            PortablePdbSettlementResult first =
                await PortablePdbSettlement.SettleAsync(
                    new PortablePdbSettlementRequest(
                        firstSource.Context,
                        assembly,
                        firstClient,
                        store,
                        new UniformPackageSourceAuthorization(
                            [NuGetFetch.PackageSource.NuGetOrg])),
                    TestContext.Current.CancellationToken);
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(first);
            Assert.NotEmpty(firstHandler.RequestUris);
        }

        var warmHandler =
            new PlatformSymbolHandler(
                throwOnRequest: true);
        using var warmClient = new HttpClient(warmHandler);
        using var warmSource =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);

        PortablePdbSettlementResult warm =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    warmSource.Context,
                    assembly,
                    warmClient,
                    store,
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(warm);
        Assert.Equal(
            PortablePdbSettlementSource.PositiveStore,
            acquired.Source);
        Assert.Equal(
            PortablePdbPositiveStoreDisposition.Reused,
            acquired.PositiveStore);
        Assert.False(acquired.NetworkOccurred);
        Assert.Empty(warmHandler.RequestUris);
        CodeViewInfo identity =
            Assert.IsType<CodeViewInfo>(
                warmSource.Context.PdbId);
        string expectedKey =
            "portable/"
            + identity.Guid.ToString("N").ToUpperInvariant()
            + identity.Stamp.ToString("X8")
            + ".pdb";
        Assert.NotEmpty(store.Keys);
        Assert.All(
            store.Keys,
            key => Assert.Equal(expectedKey, key));
    }

    [Fact]
    public async Task PlatformSettlement_ReplacesRejectedStoreContent()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        CodeViewInfo identity =
            Assert.IsType<CodeViewInfo>(
                source.Context.PdbId);
        string key =
            "portable/"
            + identity.Guid.ToString("N").ToUpperInvariant()
            + identity.Stamp.ToString("X8")
            + ".pdb";
        var store = new RecordingPdbStore();
        await store.PutAsync(
            key,
            new MemoryStream(
                BuildPortablePdb(
                    identity.Guid,
                    identity.Stamp + 1),
                writable: false),
            TestContext.Current.CancellationToken);
        var handler = new PlatformSymbolHandler(pdbBytes);
        using var client = new HttpClient(handler);

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    store,
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(result);
        Assert.Equal(
            PortablePdbSettlementSource.MicrosoftSymbolServer,
            acquired.Source);
        Assert.Contains(
            acquired.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate.PositiveStore
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome.Failed
                && receipt.StoreFailure
                    == PortablePdbStoreFailureKind.InvalidCachedContent);
        Assert.Contains(
            acquired.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome.Acquired);
    }

    [Fact]
    public async Task PlatformSettlement_RejectsMatchingGuidWithDifferentStamp()
    {
        var (assembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        CodeViewInfo identity =
            Assert.IsType<CodeViewInfo>(
                source.Context.PdbId);
        byte[] mismatched =
            BuildPortablePdb(
                identity.Guid,
                identity.Stamp + 1);
        using var client =
            new HttpClient(
                new PlatformSymbolHandler(mismatched));

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                PortablePdbSettlementResult.Failed>(
                    result);
        Assert.Equal(
            PortablePdbSettlementFailureKind
                .ExternalProviderFailed,
            failed.Failure);
        Assert.Contains(
            failed.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome.Rejected);
    }

    [Fact]
    public async Task PlatformSettlement_StoreFailureIsVisible()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        using var client =
            new HttpClient(
                new PlatformSymbolHandler(pdbBytes));

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    new FailingStoredWritePdbStore(),
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                PortablePdbSettlementResult.Failed>(
                    result);
        Assert.Equal(
            PortablePdbSettlementFailureKind.PositiveStoreFailed,
            failed.Failure);
        Assert.Equal(
            PortablePdbStoreFailureKind.PublicationNotRetained,
            failed.StoreFailure);
        Assert.Contains(
            failed.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome.Failed);
    }

    [Fact]
    public async Task PlatformSettlement_ProviderLimitIsIncomplete()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        using var client =
            new HttpClient(
                new PlatformSymbolHandler(pdbBytes));

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg]))
                {
                    Limits =
                        new SymbolAcquisitionLimits(
                            maxSymbolPackageBytes: 64,
                            maxPortablePdbBytes: 64,
                            maxSymbolPackageEntries: 8),
                },
                TestContext.Current.CancellationToken);

        var incomplete =
            Assert.IsType<
                PortablePdbSettlementResult.Incomplete>(
                    result);
        Assert.Contains(
            incomplete.Receipts,
            receipt =>
                receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .MicrosoftSymbolServer
                && receipt.Outcome
                    == PortablePdbSettlementAttemptOutcome.Incomplete);
    }

    [Fact]
    public async Task PlatformSettlement_CancellationIsTyped()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        var handler = new PlatformSymbolHandler(pdbBytes);
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                cancellation.Token);

        Assert.IsType<
            PortablePdbSettlementResult.Canceled>(result);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task PlatformSettlement_DeadlineIsIncomplete()
    {
        var (assembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        using var client =
            new HttpClient(
                new DelayedPlatformSymbolHandler());

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg]))
                {
                    Timeout = TimeSpan.FromMilliseconds(25),
                },
                TestContext.Current.CancellationToken);

        Assert.IsType<
            PortablePdbSettlementResult.Incomplete>(result);
    }

    [Fact]
    public async Task PackageSettlement_DoesNotUseNamePrefixAsProviderAuthority()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Package(
                "System.Example",
                "1.0.0",
                "net10.0",
                rid: null));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        var handler = new PlatformSymbolHandler(pdbBytes);
        using var client = new HttpClient(handler);

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    new InMemoryPdbStore(),
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<
                PortablePdbSettlementResult.Failed>(result);
        Assert.Equal(
            PortablePdbSettlementFailureKind.UnsupportedProvenance,
            failed.Failure);
        PortablePdbSettlementReceipt skipped =
            Assert.Single(
                failed.Receipts,
                receipt =>
                    receipt.Candidate
                    == PortablePdbSettlementCandidate
                        .ExternalProviders);
        Assert.Equal(
            PortablePdbSettlementAttemptOutcome.Skipped,
            skipped.Outcome);
        Assert.False(skipped.Authorized);
        Assert.Equal(
            PortablePdbSettlementSkipReason.UnsupportedProvenance,
            skipped.SkipReason);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task PackageSettlement_CanReuseExactPositiveStoreContent()
    {
        var (assembly, pdbBytes) = CreateTestAssembly(
            AssemblyResolutionProvenance.Package(
                "System.Example",
                "1.0.0",
                "net10.0",
                rid: null));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        CodeViewInfo identity =
            Assert.IsType<CodeViewInfo>(
                source.Context.PdbId);
        string key =
            "portable/"
            + identity.Guid.ToString("N").ToUpperInvariant()
            + identity.Stamp.ToString("X8")
            + ".pdb";
        var store = new RecordingPdbStore();
        await store.PutAsync(
            key,
            new MemoryStream(pdbBytes, writable: false),
            TestContext.Current.CancellationToken);
        var handler =
            new PlatformSymbolHandler(
                throwOnRequest: true);
        using var client = new HttpClient(handler);

        PortablePdbSettlementResult result =
            await PortablePdbSettlement.SettleAsync(
                new PortablePdbSettlementRequest(
                    source.Context,
                    assembly,
                    client,
                    store,
                    new UniformPackageSourceAuthorization(
                        [NuGetFetch.PackageSource.NuGetOrg])),
                TestContext.Current.CancellationToken);

        var acquired =
            Assert.IsType<
                PortablePdbSettlementResult.Acquired>(result);
        Assert.Equal(
            PortablePdbSettlementSource.PositiveStore,
            acquired.Source);
        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public void SettlementRequest_RequiresExactAssemblyBinding()
    {
        var (assembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        var (otherAssembly, _) = CreateTestAssembly(
            AssemblyResolutionProvenance.Platform(
                "runtime",
                "10.0.0",
                "test"));
        using var source =
            SourceLinkService.OpenEmbeddedPdbOnly(assembly);
        using var client =
            new HttpClient(
                new PlatformSymbolHandler([]));

        Assert.Throws<ArgumentException>(
            () => new PortablePdbSettlementRequest(
                source.Context,
                otherAssembly,
                client,
                new InMemoryPdbStore(),
                new UniformPackageSourceAuthorization(
                    [NuGetFetch.PackageSource.NuGetOrg])));
    }

    private static AssemblyReferenceIdentity ReadIdentity(
        byte[] assemblyBytes)
    {
        using var stream =
            new MemoryStream(
                assemblyBytes,
                writable: false);
        using var reader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] BuildPortablePdb(
        Guid id,
        uint stamp)
    {
        var metadata = new MetadataBuilder();
        var contentId = new BlobContentId(id, stamp);
        var rowCounts =
            ImmutableArray.CreateRange(
                new int[MetadataTokens.TableCount]);
        var pdbBuilder = new PortablePdbBuilder(
            metadata,
            rowCounts,
            entryPoint: default,
            idProvider: _ => contentId);
        var blob = new BlobBuilder();
        pdbBuilder.Serialize(blob);
        return blob.ToArray();
    }

    private static (
        ResolvedAssemblyReference Assembly,
        byte[] PdbBytes)
        CreateTestAssembly(
            AssemblyResolutionProvenance provenance)
    {
        string assemblyPath =
            typeof(PdbAcquisitionServiceTests).Assembly.Location;
        string pdbPath =
            Path.ChangeExtension(assemblyPath, ".pdb");
        Assert.True(
            File.Exists(pdbPath),
            $"Expected test PDB at {pdbPath}");
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        return (
            ResolvedAssemblyReference.Create(
                ReadIdentity(assemblyBytes),
                path: null,
                () => new MemoryStream(
                    assemblyBytes,
                    writable: false),
                provenance),
            File.ReadAllBytes(pdbPath));
    }

    private static byte[] BuildSnupkg(
        string pdbFileName,
        byte[] pdbBytes)
    {
        using var buffer = new MemoryStream();
        using (var archive =
               new ZipArchive(
                   buffer,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            ZipArchiveEntry entry =
                archive.CreateEntry(
                    $"lib/net10.0/{pdbFileName}");
            using Stream stream = entry.Open();
            stream.Write(pdbBytes);
        }

        return buffer.ToArray();
    }

    private sealed class SymbolPackageHandler(
        byte[] snupkg) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath.EndsWith(
                    ".snupkg",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(snupkg),
                    });
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class PlatformSymbolHandler : HttpMessageHandler
    {
        private readonly byte[] _pdbBytes;
        private readonly bool _throwOnRequest;

        internal PlatformSymbolHandler(
            byte[]? pdbBytes = null,
            bool throwOnRequest = false)
        {
            _pdbBytes = pdbBytes ?? [];
            _throwOnRequest = throwOnRequest;
        }

        internal List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            if (_throwOnRequest)
            {
                throw new InvalidOperationException(
                    "Warm settlement performed network work.");
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_pdbBytes),
                });
        }
    }

    private sealed class DelayedPlatformSymbolHandler
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            return new HttpResponseMessage(
                HttpStatusCode.NotFound);
        }
    }

    private sealed class FailingStoredReadPdbStore : IPdbStore
    {
        private byte[]? _content;
        private int _storedOpenCount;

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            if (_content is null)
                return ValueTask.FromResult<Stream?>(null);

            Stream stream =
                Interlocked.Increment(ref _storedOpenCount) == 1
                    ? new MemoryStream(_content, writable: false)
                    : new FailingReadStream(_content);
            return ValueTask.FromResult<Stream?>(stream);
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(
                buffer,
                cancellationToken);
            _content = buffer.ToArray();
        }

        public string? TryGetLocalPath(string key)
            => null;
    }

    private sealed class RecordingPdbStore : IPdbStore
    {
        private readonly Dictionary<string, byte[]> _entries =
            new(StringComparer.Ordinal);

        internal List<string> Keys { get; } = [];

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(key);
            return ValueTask.FromResult(
                _entries.TryGetValue(key, out byte[]? content)
                    ? (Stream)new MemoryStream(
                        content,
                        writable: false)
                    : null);
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(
                buffer,
                cancellationToken);
            _entries[key] = buffer.ToArray();
        }

        public string? TryGetLocalPath(string key)
        {
            Keys.Add(key);
            return null;
        }
    }

    private sealed class FailingStoredWritePdbStore : IPdbStore
    {
        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Stream?>(null);

        public ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException(
                new IOException("Injected store write failure."));

        public string? TryGetLocalPath(string key)
            => null;
    }

    private sealed class ThrowingLocalPathPdbStore : IPdbStore
    {
        byte[]? _content;
        int _openCount;

        internal int OpenCount =>
            Volatile.Read(ref _openCount);
        internal List<TrackingMemoryStream> OpenedStreams { get; } =
            [];

        public ValueTask<Stream?> TryOpenAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_content is null)
                return ValueTask.FromResult<Stream?>(null);

            Interlocked.Increment(ref _openCount);
            var stream =
                new TrackingMemoryStream(_content);
            OpenedStreams.Add(stream);
            return ValueTask.FromResult<Stream?>(stream);
        }

        public async ValueTask PutAsync(
            string key,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(
                buffer,
                cancellationToken);
            _content = buffer.ToArray();
        }

        public string? TryGetLocalPath(string key) =>
            throw new HttpRequestException(
                "Injected local-path store failure.");
    }

    private sealed class TrackingMemoryStream(
        byte[] content) : MemoryStream(
        content,
        writable: false)
    {
        internal bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FailingReadStream(
        byte[] content) : MemoryStream(
            content,
            writable: false)
    {
        public override int Read(
            byte[] buffer,
            int offset,
            int count)
            => throw new IOException(
                "Injected store read failure.");

        public override int Read(Span<byte> buffer)
            => throw new IOException(
                "Injected store read failure.");
    }
}
