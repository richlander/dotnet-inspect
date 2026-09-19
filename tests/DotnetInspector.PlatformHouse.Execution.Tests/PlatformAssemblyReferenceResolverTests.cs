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
        ResolveAsync_RejectsNonGlobalInputBeforeSourceAccess()
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
    public async Task
        ResolveAsync_RejectsMismatchedRouteBeforeSourceAccess()
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
            cancellationToken,
            mismatchRoute: true);

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                input.Request,
                input.Item,
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

    [Fact]
    public async Task
        ResolveAsync_CleanupFailureRemainsPrimaryOverCancellation()
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
            cancellation.Token,
            openStream: static content =>
                new ThrowingDisposeStream(content));

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                input.Request,
                input.Item,
                input.Consumed);

        var failed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Failed>(
                outcome);
        Assert.Contains(
            PlatformHouseFailureKind.ArtifactPublication,
            failed.Evidence.Failures);
        Assert.True(failed.Evidence.CancellationObserved);
    }

    [Fact]
    public async Task
        ResolveAsync_FallbackUsesPlanOrderAndRetainsPriorAbsence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var unavailable = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Unavailable);
        int selectedOpens = 0;
        var selected = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => selectedOpens++);

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [selected, unavailable],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 1,
                        bytes: image.LongLength)));

        Assert.IsType<AssemblyBindingDecision.Resolved>(completed.Value);
        Assert.True(selectedOpens > 0);
        Assert.Collection(
            completed.Receipt.SourceSettlements,
            settlement =>
            {
                Assert.Same(
                    unavailable.Contribution,
                    settlement.Contribution);
                Assert.Equal(
                    PlatformSourceSettlementDisposition.OutcomeRelevant,
                    settlement.Disposition);
            },
            settlement =>
            {
                Assert.Same(
                    selected.Contribution,
                    settlement.Contribution);
                Assert.Equal(
                    PlatformSourceSettlementDisposition.Selected,
                    settlement.Disposition);
            });
    }

    [Fact]
    public async Task
        ResolveAsync_PrecedenceFailureStopsBeforeLaterSuccess()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Precedence,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var failed = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Failed);
        int shadowedOpens = 0;
        var shadowed = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => shadowedOpens++);

        var outcome = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Failed>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [shadowed, failed],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 1,
                        bytes: image.LongLength)));

        Assert.Equal(
            [PlatformHouseFailureKind.Source],
            outcome.Evidence.Failures);
        Assert.Equal(0, shadowedOpens);
        Assert.Equal(
            PlatformSourceSettlementDisposition.Shadowed,
            outcome.Receipt.SourceSettlements[1].Disposition);
    }

    [Fact]
    public async Task
        ResolveAsync_FallbackSupersedesPriorFailure()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var failed = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Failed);
        var selected = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate");

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [failed, selected],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 1,
                        bytes: image.LongLength)));

        Assert.Equal(
            PlatformSourceSettlementDisposition.OutcomeRelevant,
            completed.Receipt.SourceSettlements[0].Disposition);
        Assert.Equal(
            PlatformSourceSettlementDisposition.Selected,
            completed.Receipt.SourceSettlements[1].Disposition);
    }

    [Theory]
    [InlineData(PlatformSourceContributionKind.Rejected)]
    [InlineData(PlatformSourceContributionKind.Incomplete)]
    public async Task
        ResolveAsync_FallbackTerminalPreventsLaterSelection(
            PlatformSourceContributionKind terminalKind)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var terminal = TerminalAttempt(request, first, terminalKind);
        int laterOpens = 0;
        var later = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => laterOpens++);

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                request,
                [later, terminal],
                Consumed(
                    sourceOperations: 2,
                    assemblies: 1,
                    bytes: image.LongLength));

        if (terminalKind == PlatformSourceContributionKind.Rejected)
        {
            Assert.IsType<
                PlatformHouseOutcome<
                    AssemblyBindingDecision>.Rejected>(outcome);
        }
        else
        {
            Assert.IsType<
                PlatformHouseOutcome<
                    AssemblyBindingDecision>.Incomplete>(outcome);
        }
        Assert.Equal(0, laterOpens);
        Assert.Equal(
            PlatformSourceSettlementDisposition.Shadowed,
            outcome.Receipt.SourceSettlements[1].Disposition);
    }

    [Fact]
    public async Task
        ResolveAsync_AggregationSelectsAfterAuthoritativePeerAbsence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var selected = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "installed-candidate");
        var absent = TerminalAttempt(
            request,
            second,
            PlatformSourceContributionKind.Unavailable,
            PlatformSourceUnavailabilityKind.Absent);

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [absent, selected],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 1,
                        bytes: image.LongLength)));

        Assert.Equal(
            PlatformSourceSettlementDisposition.Selected,
            completed.Receipt.SourceSettlements[0].Disposition);
        Assert.Equal(
            PlatformSourceSettlementDisposition.OutcomeRelevant,
            completed.Receipt.SourceSettlements[1].Disposition);
    }

    [Fact]
    public async Task
        ResolveAsync_AggregationAmbiguityOpensNoSourceContent()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 2,
            maxBytes: image.LongLength * 2);
        int opens = 0;
        var firstSuccess = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "installed-candidate",
            () => opens++);
        var secondSuccess = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var ambiguous = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Ambiguous>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [secondSuccess, firstSuccess],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 2,
                        bytes: image.LongLength * 2)));

        Assert.Equal(0, opens);
        Assert.Equal(2, ambiguous.Evidence.Candidates.Count);
        Assert.All(
            ambiguous.Receipt.SourceSettlements,
            settlement => Assert.Equal(
                PlatformSourceSettlementDisposition.OutcomeRelevant,
                settlement.Disposition));
    }

    [Theory]
    [InlineData(
        PlatformSourceContributionKind.Incomplete,
        PlatformHouseSettlementKind.Incomplete)]
    [InlineData(
        PlatformSourceContributionKind.Failed,
        PlatformHouseSettlementKind.Failed)]
    [InlineData(
        PlatformSourceContributionKind.Rejected,
        PlatformHouseSettlementKind.Rejected)]
    [InlineData(
        PlatformSourceContributionKind.Unavailable,
        PlatformHouseSettlementKind.Unavailable)]
    public async Task
        ResolveAsync_AggregationRetainsPeerTerminalOutcome(
            PlatformSourceContributionKind terminalKind,
            PlatformHouseSettlementKind expected)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        int opens = 0;
        var success = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "installed-candidate",
            () => opens++);
        var terminal = TerminalAttempt(
            request,
            second,
            terminalKind,
            PlatformSourceUnavailabilityKind.Unavailable);

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                request,
                [success, terminal],
                Consumed(
                    sourceOperations: 2,
                    assemblies: 1,
                    bytes: image.LongLength));

        Assert.Equal(expected, outcome.Receipt.SettlementKind);
        Assert.Equal(0, opens);
        Assert.All(
            outcome.Receipt.SourceSettlements,
            settlement => Assert.Equal(
                PlatformSourceSettlementDisposition.OutcomeRelevant,
                settlement.Disposition));
    }

    [Theory]
    [InlineData(PlatformSourceContributionKind.Incomplete)]
    [InlineData(PlatformSourceContributionKind.Rejected)]
    public async Task
        ResolveAsync_AggregationFailurePrecedesPeerTerminal(
            PlatformSourceContributionKind peerKind)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 0,
            maxBytes: 0);
        var failed = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Failed);
        var peer = TerminalAttempt(request, second, peerKind);

        var outcome = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Failed>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [peer, failed],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 0,
                        bytes: 0)));

        Assert.Equal(
            [PlatformHouseFailureKind.Source],
            outcome.Evidence.Failures);
        Assert.All(
            outcome.Receipt.SourceSettlements,
            settlement => Assert.Equal(
                PlatformSourceSettlementDisposition.OutcomeRelevant,
                settlement.Disposition));
    }

    [Fact]
    public async Task
        ResolveAsync_AggregationFailurePrecedesMissingCapability()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 1,
            maxAssemblies: 0,
            maxBytes: 0);
        var failed = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Failed);

        var outcome = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Failed>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [failed],
                    Consumed(
                        sourceOperations: 1,
                        assemblies: 0,
                        bytes: 0)));

        Assert.Same(
            failed.Contribution,
            Assert.Single(outcome.Receipt.SourceSettlements)
                .Contribution);
    }

    [Fact]
    public async Task
        ResolveAsync_MissingCapabilityIsIncompleteWithoutOpeningSuccess()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        int opens = 0;
        var success = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [success],
                    Consumed(
                        sourceOperations: 1,
                        assemblies: 1,
                        bytes: image.LongLength)));

        PlatformSourceSettlement retained =
            Assert.Single(incomplete.Receipt.SourceSettlements);
        Assert.Same(success.Contribution, retained.Contribution);
        Assert.Equal(
            PlatformSourceSettlementDisposition.Shadowed,
            retained.Disposition);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsDuplicateCapabilityAttemptsWithoutOpening()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 2,
            maxBytes: image.LongLength * 2);
        int opens = 0;
        var firstAttempt = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "first-candidate",
            () => opens++);
        var duplicate = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "duplicate-candidate",
            () => opens++);

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [duplicate, firstAttempt],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 2,
                        bytes: image.LongLength * 2)));

        Assert.Empty(rejected.Receipt.SourceSettlements);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsDuplicateCandidateIdentityWithoutOpening()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 2,
            maxBytes: image.LongLength * 2);
        PlatformHouseCandidateIdentity duplicate =
            PlatformHouseCandidateIdentity.Create("duplicate-candidate");
        int opens = 0;
        var firstAttempt = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "ignored-first",
            () => opens++,
            duplicate);
        var secondAttempt = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "ignored-second",
            () => opens++,
            duplicate);

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [firstAttempt, secondAttempt],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 2,
                        bytes: image.LongLength * 2)));

        Assert.Empty(rejected.Receipt.SourceSettlements);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsUnderreportedTerminalSourceWork()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Precedence,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var failed = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Failed);
        int opens = 0;
        var success = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [failed, success],
                    Consumed(
                        sourceOperations: 1,
                        assemblies: 1,
                        bytes: image.LongLength)));

        Assert.Equal(
            PlatformHouseRejectionKind.InvalidBudget,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Empty(rejected.Receipt.SourceSettlements);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ResolveAsync_InvalidRequestDoesNotEnumerateAttempts()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformHouseRequest request = Request(
            identity,
            [capability],
            PlatformSourceSelectionMode.Precedence,
            cancellationToken,
            maxSourceOperations: 1,
            maxAssemblies: 1,
            maxBytes: image.LongLength,
            mismatchRoute: true);
        bool enumerated = false;

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    Attempts(),
                    Consumed(
                        sourceOperations: 0,
                        assemblies: 0,
                        bytes: 0)));

        Assert.Equal(
            PlatformHouseRejectionKind.InvalidRequest,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.False(enumerated);

        IEnumerable<PlatformAssemblyReferenceSourceAttempt> Attempts()
        {
            enumerated = true;
            yield break;
        }
    }

    [Fact]
    public async Task
        ResolveAsync_ExhaustedInvalidRequestIsIncompleteWithoutEnumeration()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformHouseRequest request = Request(
            identity,
            [capability],
            PlatformSourceSelectionMode.Precedence,
            cancellationToken,
            maxSourceOperations: 1,
            maxAssemblies: 1,
            maxBytes: image.LongLength,
            mismatchRoute: true);
        bool enumerated = false;

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    Attempts(),
                    Consumed(
                        sourceOperations: 0,
                        assemblies: 0,
                        bytes: 0,
                        elapsed: TimeSpan.FromMinutes(1))));

        Assert.Empty(incomplete.Receipt.SourceSettlements);
        Assert.False(enumerated);

        IEnumerable<PlatformAssemblyReferenceSourceAttempt> Attempts()
        {
            enumerated = true;
            yield break;
        }
    }

    [Fact]
    public async Task
        ResolveAsync_ExhaustedUnderreportedWorkIsIncompleteWithoutSettlement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Aggregation,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 2,
            maxBytes: image.LongLength * 2);
        int opens = 0;
        var firstAttempt = SuccessfulAttempt(
            request,
            first,
            identity,
            image,
            "installed-candidate",
            () => opens++);
        var secondAttempt = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [firstAttempt, secondAttempt],
                    Consumed(
                        sourceOperations: 1,
                        assemblies: 2,
                        bytes: image.LongLength * 2,
                        elapsed: TimeSpan.FromMinutes(1))));

        Assert.Empty(incomplete.Receipt.SourceSettlements);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task
        ResolveAsync_ExhaustedUnderreportedFailureIsIncompleteWithoutSettlement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Precedence,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var failed = TerminalAttempt(
            request,
            first,
            PlatformSourceContributionKind.Failed);
        int opens = 0;
        var success = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [failed, success],
                    Consumed(
                        sourceOperations: 1,
                        assemblies: 1,
                        bytes: image.LongLength,
                        elapsed: TimeSpan.FromMinutes(1))));

        Assert.Empty(incomplete.Receipt.SourceSettlements);
        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task ResolveAsync_RejectsForeignAttemptBeforeSourceAccess()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        PlatformHouseRequest foreignRequest = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var unavailable = TerminalAttempt(
            foreignRequest,
            first,
            PlatformSourceContributionKind.Unavailable);
        int opens = 0;
        var success = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [success, unavailable],
                    Consumed(
                        sourceOperations: 2,
                        assemblies: 1,
                        bytes: image.LongLength)));

        Assert.Equal(0, opens);
        Assert.Empty(rejected.Receipt.SourceSettlements);
    }

    [Fact]
    public async Task ResolveAsync_BudgetExhaustionPrecedesForeignAttempt()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = File.ReadAllBytes(
            typeof(Enumerable).Assembly.Location);
        AssemblyReferenceIdentity identity = Descriptor(image).Identity;
        PlatformSourceCapabilityIdentity first =
            PlatformSourceCapabilityIdentity.Create("installed");
        PlatformSourceCapabilityIdentity second =
            PlatformSourceCapabilityIdentity.Create("package-backed");
        PlatformHouseRequest request = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        PlatformHouseRequest foreignRequest = Request(
            identity,
            [first, second],
            PlatformSourceSelectionMode.Fallback,
            cancellationToken,
            maxSourceOperations: 2,
            maxAssemblies: 1,
            maxBytes: image.LongLength);
        var unavailable = TerminalAttempt(
            foreignRequest,
            first,
            PlatformSourceContributionKind.Unavailable);
        int opens = 0;
        var success = SuccessfulAttempt(
            request,
            second,
            identity,
            image,
            "package-candidate",
            () => opens++);

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete>(
                await PlatformHouseAssemblyReferenceResolver.ResolveAsync(
                    request,
                    [success, unavailable],
                    Consumed(
                        sourceOperations: 3,
                        assemblies: 2,
                        bytes: image.LongLength * 2)));

        Assert.Equal(0, opens);
        Assert.Empty(incomplete.Receipt.SourceSettlements);
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
            CancellationToken cancellationToken,
            bool mismatchRoute = false,
            Func<byte[], Stream>? openStream = null)
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create(
                "reference-source");
        PlatformHouseRequest request = Request(
            identity,
            capability,
            AssemblyBindingOrigin.Global(),
            cancellationToken,
            image.LongLength,
            mismatchRoute);
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
                ? openStream?.Invoke(image)
                    ?? new MemoryStream(image, writable: false)
                : throw new IOException(
                    "The original Platform source is retired.");
        }
    }

    static PlatformHouseRequest Request(
        AssemblyReferenceIdentity identity,
        PlatformSourceCapabilityIdentity capability,
        AssemblyBindingOrigin origin,
        CancellationToken cancellationToken,
        long maxBytes,
        bool mismatchRoute = false)
        => Request(
            identity,
            [capability],
            PlatformSourceSelectionMode.Precedence,
            cancellationToken,
            maxSourceOperations: 1,
            maxAssemblies: 1,
            maxBytes,
            origin,
            mismatchRoute);

    static PlatformHouseRequest Request(
        AssemblyReferenceIdentity identity,
        IReadOnlyList<PlatformSourceCapabilityIdentity> capabilities,
        PlatformSourceSelectionMode mode,
        CancellationToken cancellationToken,
        int maxSourceOperations,
        int maxAssemblies,
        long maxBytes,
        AssemblyBindingOrigin? origin = null,
        bool mismatchRoute = false)
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
                    mode,
                    capabilities),
            ]);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    origin ?? AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Platform),
                "metadata-request");
        var route = new PlatformAssemblyReferenceRoute(
            metadataRequest.Identity,
            target,
            requestOrigin,
            sources.Identity,
            mismatchRoute
                ? PlatformSourcePolicyGeneration.Create(
                    "mismatched-source-generation")
                : sources.Generation);
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
                maxSourceOperations,
                maxTargetCandidates: 1,
                maxAssemblies,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    static PlatformAssemblyReferenceSourceAttempt.Succeeded
        SuccessfulAttempt(
            PlatformHouseRequest request,
            PlatformSourceCapabilityIdentity capability,
            AssemblyReferenceIdentity identity,
            byte[] image,
            string candidateName,
            Action? observedOpen = null,
            PlatformHouseCandidateIdentity? candidate = null)
    {
        var contribution =
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Reference,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    $"{capability.Name}-generation"),
                ((PlatformTargetDemand.Exact)request.Target).Target,
                PlatformSourceCoordinateIdentity.Create(
                    $"{capability.Name}-coordinate"),
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
                observedOpen?.Invoke();
                return new MemoryStream(image, writable: false);
            });
        return new(
            candidate ?? PlatformHouseCandidateIdentity.Create(candidateName),
            item);
    }

    static PlatformAssemblyReferenceSourceAttempt.NotSucceeded
        TerminalAttempt(
            PlatformHouseRequest request,
            PlatformSourceCapabilityIdentity capability,
            PlatformSourceContributionKind kind,
            PlatformSourceUnavailabilityKind unavailability =
                PlatformSourceUnavailabilityKind.Unavailable)
    {
        PlatformFamilyTarget target =
            ((PlatformTargetDemand.Exact)request.Target).Target;
        PlatformSourceGeneration generation =
            PlatformSourceGeneration.Create(
                $"{capability.Name}-terminal-generation");
        PlatformSourceContribution contribution = kind switch
        {
            PlatformSourceContributionKind.Unavailable =>
                new PlatformSourceContribution.Unavailable(
                    PlatformSourceFacet.Reference,
                    capability,
                    request.Snapshot,
                    generation,
                    target,
                    unavailability),
            PlatformSourceContributionKind.Rejected =>
                new PlatformSourceContribution.Rejected(
                    PlatformSourceFacet.Reference,
                    capability,
                    request.Snapshot,
                    generation,
                    target),
            PlatformSourceContributionKind.Incomplete =>
                new PlatformSourceContribution.Incomplete(
                    PlatformSourceFacet.Reference,
                    capability,
                    request.Snapshot,
                    generation,
                    target),
            PlatformSourceContributionKind.Failed =>
                new PlatformSourceContribution.Failed(
                    PlatformSourceFacet.Reference,
                    capability,
                    request.Snapshot,
                    generation,
                    target),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new(
            contribution,
            kind == PlatformSourceContributionKind.Rejected
                ? PlatformHouseRejectionKind.InvalidOwnerResult
                : null);
    }

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations,
        int assemblies,
        long bytes,
        TimeSpan? elapsed = null) =>
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
            elapsed: elapsed ?? TimeSpan.Zero);

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

    sealed class ThrowingDisposeStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        protected override void Dispose(bool disposing) =>
            throw new IOException("The source stream could not be closed.");
    }
}
