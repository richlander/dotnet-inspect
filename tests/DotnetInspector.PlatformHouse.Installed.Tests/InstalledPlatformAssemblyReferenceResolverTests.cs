using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

public sealed class InstalledPlatformAssemblyReferenceResolverTests
{
    [Fact]
    public async Task
        ResolveAsync_PreservesInstalledProvenanceAndSettlesContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string source = FindReferenceAssembly("System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            source);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity = ReadIdentity(source);
        PlatformHouseRequest request =
            Request(adapter, identity, cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(request));
        PlatformHouseConsumedWork consumed = Consumed(reference);

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await InstalledPlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        consumed));

        var decision = Assert.IsType<AssemblyBindingDecision.Resolved>(
            completed.Value);
        var platformProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                decision.Candidate.Registration
                    .ArtifactRegistration!
                    .Provenance);
        Assert.Same(
            reference.Contribution,
            platformProvenance.Contribution);
        var installedProvenance =
            Assert.IsType<InstalledReferenceArtifactProvenance>(
                platformProvenance.SourceProvenance);
        InstalledReferenceLibrary library =
            Assert.Single(reference.Value.Libraries);
        Assert.Same(
            reference.Value.Generation,
            installedProvenance.SourceGeneration);
        Assert.Equal(
            reference.Contribution.Generation.Name,
            installedProvenance.SourceGeneration.Name);
        Assert.Same(
            reference.Value.Coordinate,
            installedProvenance.Coordinate);
        Assert.Equal(library.FileName, installedProvenance.FileName);
        Assert.True(
            library.Identity.IsEquivalentTo(
                installedProvenance.Identity));
        Assert.Same(
            reference.Contribution,
            Assert.Single(completed.Receipt.SourceSettlements)
                .Contribution);
        Assert.Equal(
            PlatformHouseSettlementKind.Completed,
            completed.Receipt.SettlementKind);
        Assert.Equal(consumed, completed.Receipt.ConsumedWork);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsSuccessfulResultForDifferentRequest()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string source = FindReferenceAssembly("System.Text.Json.dll");
        hive.CopyAssembly(
            hive.CreateReferencePack(),
            source);
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity = ReadIdentity(source);
        PlatformHouseRequest sourceRequest =
            Request(adapter, identity, cancellationToken);
        PlatformHouseRequest executionRequest =
            Request(adapter, identity, cancellationToken);
        var reference = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(sourceRequest));

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await InstalledPlatformAssemblyReferenceResolver.ResolveAsync(
                executionRequest,
                reference,
                Consumed(reference));

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                outcome);
        Assert.Equal(
            PlatformHouseRejectionKind.InvalidRequest,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Empty(rejected.Receipt.SourceSettlements);
    }

    static PlatformHouseRequest Request(
        InstalledPlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken)
    {
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        var origin = new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create(
                "installed-binding-test"));
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("installed-binding-plan"),
            PlatformSourcePolicyGeneration.Create(
                "installed-binding-policy"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.Capabilities.ReferenceRealization]),
            ]);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Platform),
                "installed-binding-metadata-request");
        var route = new PlatformAssemblyReferenceRoute(
            metadataRequest.Identity,
            target,
            origin,
            sources.Identity,
            sources.Generation);
        var operation =
            new PlatformHouseOperation.ResolveAssemblyReference
                .WithPrerequisites<PlatformAssemblyReferenceRoute>(
                    metadataRequest,
                    new PlatformRoutePrerequisitesEvidence<
                        PlatformAssemblyReferenceRoute>(
                            route,
                            "installed-binding-route"),
                    PlatformViewDemand.Reference);
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "installed-binding-request"),
            new PlatformTargetDemand.Exact(target),
            origin,
            operation,
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 0,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 64 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    static PlatformHouseConsumedWork Consumed(
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.Succeeded reference)
    {
        InstalledReferenceLibrary library =
            Assert.Single(reference.Value.Libraries);
        return new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: 1,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: library.ContentLength,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);
    }

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using FileStream stream = File.OpenRead(path);
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
