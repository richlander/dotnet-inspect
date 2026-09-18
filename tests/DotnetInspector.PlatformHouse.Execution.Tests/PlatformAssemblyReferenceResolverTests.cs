using System.Reflection;
using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.PlatformHouse.Tests;

public sealed class PlatformAssemblyReferenceResolverTests
{
    [Fact]
    public async Task
        ResolveAsync_DetachesDecisionAndRetiresAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        ResolvedAssemblyReference sourceAssembly = Descriptor(image);
        bool sourceAvailable = true;
        int sourceOpens = 0;
        var input = Input(
            sourceAssembly.Identity,
            image,
            () => sourceAvailable,
            () => sourceOpens++,
            cancellationToken);

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                input.Request,
                input.Item,
                input.Consumed);

        Assert.True(
            outcome
                is PlatformHouseOutcome<AssemblyBindingDecision>.Completed,
            outcome
                is PlatformHouseOutcome<AssemblyBindingDecision>.Failed failed
                ? string.Join(", ", failed.Evidence.Failures)
                : outcome.GetType().FullName);
        var completed =
            (PlatformHouseOutcome<AssemblyBindingDecision>.Completed)
                outcome;
        var decision = Assert.IsType<AssemblyBindingDecision.Resolved>(
            completed.Value);
        sourceAvailable = false;

        Assert.Equal(sourceAssembly.Identity, decision.Candidate.Identity);
        Assert.NotEqual(Guid.Empty, decision.Candidate.ModuleVersionId);
        Assert.Same(
            input.Contribution,
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                    decision.Candidate.Registration
                        .ArtifactRegistration!
                        .Provenance)
                .Contribution);
        Assert.Same(
            input.Contribution,
            Assert.Single(completed.Receipt.SourceSettlements)
                .Contribution);
        Assert.Equal(
            PlatformHouseSettlementKind.Completed,
            completed.Receipt.SettlementKind);
        Assert.Equal(
            PlatformAssemblyReferenceCompletionKind.Resolved,
            Assert.IsType<PlatformHouseCompletion.AssemblyReference>(
                    completed.Receipt.Completion)
                .Kind);
        Assert.True(sourceOpens > 0);
        Assert.Throws<IOException>(() => input.OpenSource());

        Assert.Equal(sourceAssembly.Identity, decision.Request.Target
            is AssemblyBindingTarget.AssemblyReference target
                ? target.Identity
                : null);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsForeignAndNonGlobalInputsWithoutAuthorityTransfer()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        ResolvedAssemblyReference sourceAssembly = Descriptor(image);
        int sourceOpens = 0;
        var input = Input(
            sourceAssembly.Identity,
            image,
            static () => true,
            () => sourceOpens++,
            cancellationToken);
        ResolvedAssemblyReference origin = Descriptor(image);
        PlatformHouseRequest nonGlobal = Request(
            sourceAssembly.Identity,
            input.Capability,
            AssemblyBindingOrigin.FromAssembly(origin),
            cancellationToken,
            image.LongLength);
        var foreignContribution =
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                input.Capability,
                nonGlobal.Snapshot,
                PlatformSourceGeneration.Create(
                    "foreign-generation"),
                ((PlatformTargetDemand.Exact)nonGlobal.Target).Target,
                PlatformSourceCoordinateIdentity.Create(
                    "foreign-coordinate"),
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(
                        sourceAssembly.Identity)),
                PlatformSourceContributionCompleteness.Authoritative);
        var foreignItem = new PlatformLibraryArtifactMaterializationItem(
            foreignContribution,
            new TestProvenance(),
            sourceAssembly.Identity,
            image.LongLength,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceOpens++;
                return new MemoryStream(image, writable: false);
            });

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                nonGlobal,
                foreignItem,
                input.Consumed);

        Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                outcome);
        Assert.Equal(0, sourceOpens);
    }

    [Fact]
    public async Task PublicResultClosure_IsResourceFree()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        ResolvedAssemblyReference sourceAssembly = Descriptor(image);
        var input = Input(
            sourceAssembly.Identity,
            image,
            static () => true,
            static () => { },
            cancellationToken);
        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                input.Request,
                input.Item,
                input.Consumed);
        Type root = outcome.GetType();

        var visited = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.TryPop(out Type? type))
        {
            type = Normalize(type);
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || type == typeof(Version))
            {
                continue;
            }

            Assert.False(typeof(IDisposable).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(Stream).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(Delegate).IsAssignableFrom(type), type.FullName);
            Assert.NotEqual(typeof(ResolvedAssemblyReference), type);
            Assert.NotEqual(typeof(TypeResolutionContext), type);
            Assert.NotEqual(typeof(TypeResolutionCatalog), type);

            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }
            foreach (Type argument in type.GetGenericArguments())
                pending.Push(argument);
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Push(property.PropertyType);
            }
        }
    }

    [Fact]
    public async Task ResolveAsync_ReportsMetadataIdentityFailure()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] expectedImage = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        byte[] differentImage = File.ReadAllBytes(
            typeof(System.Text.Json.JsonSerializer).Assembly.Location);
        ResolvedAssemblyReference expected = Descriptor(expectedImage);
        var input = Input(
            expected.Identity,
            differentImage,
            static () => true,
            static () => { },
            cancellationToken);

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                input.Request,
                input.Item,
                input.Consumed);

        var failed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Failed>(
                outcome);
        Assert.Contains(
            PlatformHouseFailureKind.Metadata,
            failed.Evidence.Failures);
        Assert.False(failed.Evidence.CancellationObserved);
    }

    [Fact]
    public async Task ResolveAsync_ObservesCancellationAfterCleanup()
    {
        using var cancellation = CancellationTokenSource
            .CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        ResolvedAssemblyReference sourceAssembly = Descriptor(image);
        var input = Input(
            sourceAssembly.Identity,
            image,
            static () => true,
            cancellation.Cancel,
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    input.Request,
                    input.Item,
                    input.Consumed));
    }

    static (
        PlatformHouseRequest Request,
        PlatformLibraryArtifactMaterializationItem Item,
        PlatformSourceContribution.Realization Contribution,
        PlatformSourceCapabilityIdentity Capability,
        PlatformHouseConsumedWork Consumed,
        Func<Stream> OpenSource) Input(
            AssemblyReferenceIdentity identity,
            byte[] image,
            Func<bool> sourceAvailable,
            Action observedOpen,
            CancellationToken cancellationToken)
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create(
                "reference-source");
        PlatformHouseRequest request = Request(
            identity,
            capability,
            AssemblyBindingOrigin.Global(),
            cancellationToken,
            image.LongLength);
        var contribution =
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    "reference-generation"),
                ((PlatformTargetDemand.Exact)request.Target).Target,
                PlatformSourceCoordinateIdentity.Create(
                    "reference-coordinate"),
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformSourceContributionCompleteness.Authoritative);
        var item = new PlatformLibraryArtifactMaterializationItem(
            contribution,
            new TestProvenance(),
            identity,
            image.LongLength,
            token =>
            {
                token.ThrowIfCancellationRequested();
                return OpenSource();
            });
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: 1,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: image.LongLength,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);
        return (
            request,
            item,
            contribution,
            capability,
            consumed,
            OpenSource);

        Stream OpenSource()
        {
            observedOpen();
            return sourceAvailable()
                ? new MemoryStream(image, writable: false)
                : throw new IOException(
                    "The original Platform source is retired.");
        }
    }

    static PlatformHouseRequest Request(
        AssemblyReferenceIdentity identity,
        PlatformSourceCapabilityIdentity capability,
        AssemblyBindingOrigin origin,
        CancellationToken cancellationToken,
        long maxBytes)
    {
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        var requestOrigin = new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create(
                "assembly-reference-standalone"));
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create(
                "assembly-reference-sources"),
            PlatformSourcePolicyGeneration.Create(
                "assembly-reference-source-generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [capability]),
            ]);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    origin,
                    AssemblyResolutionScope.Platform),
                "metadata-request");
        var route = new PlatformAssemblyReferenceRoute(
            metadataRequest.Identity,
            target,
            requestOrigin,
            sources.Identity,
            sources.Generation);
        var operation = new PlatformHouseOperation.ResolveAssemblyReference
            .WithPrerequisites<PlatformAssemblyReferenceRoute>(
                metadataRequest,
                new PlatformRoutePrerequisitesEvidence<
                    PlatformAssemblyReferenceRoute>(
                        route,
                        "platform-route"),
                PlatformViewDemand.Reference);

        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "assembly-reference-request"),
            new PlatformTargetDemand.Exact(target),
            requestOrigin,
            operation,
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations: 1,
                maxTargetCandidates: 1,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    static ResolvedAssemblyReference Descriptor(byte[] image) =>
        ResolvedAssemblyReference.CreateFromStreamIfManaged(
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Designated(
                "PlatformHouse test reference"))
        ?? throw new Xunit.Sdk.XunitException(
            "The real framework image must be a managed assembly.");

    static Type Normalize(Type type) =>
        type.IsByRef || type.IsPointer
            ? type.GetElementType()!
            : type;

    sealed record TestProvenance : IArtifactProvenance;
}
