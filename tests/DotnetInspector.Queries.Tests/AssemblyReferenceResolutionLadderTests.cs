using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyReferenceResolutionLadderTests
{
    [Fact]
    public async Task ContextSelectionIsTerminal()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        var selection = new AssemblyBindingSelectionSnapshot(
            version,
            AssemblyBindingSelection.Found(assembly));
        var externalCalls = 0;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                selection)),
                    (_, _, _) =>
                    {
                        externalCalls++;
                        throw new InvalidOperationException(
                            "External routes must not be formed.");
                    }));

        var resolved = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Resolved>(outcome);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ReferencingContext,
            resolved.Rung);
        Assert.Same(assembly, resolved.Selected.Assembly);
        Assert.Same(version, resolved.Selection.Version);
        Assert.Null(resolved.Route);
        Assert.Equal(0, externalCalls);
        Assert.Single(resolved.Trace);
    }

    [Fact]
    public async Task ContextOwnedMissDoesNotAdvance()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        var externalCalls = 0;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameOwnedButNoMatch()))),
                    (_, _, _) =>
                    {
                        externalCalls++;
                        throw new InvalidOperationException(
                            "External routes must not be formed.");
                    }));

        var unbound = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Unbound>(outcome);
        Assert.Equal(
            AssemblyBindingMissDisposition.NameOwnedNoMatch,
            unbound.Disposition);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ReferencingContext,
            unbound.Rung);
        Assert.Equal(0, externalCalls);
    }

    [Fact]
    public async Task NoNameOwnerAdvancesToExactExternalRoute()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        TestExternalRoute? selectedRoute = null;
        AssemblyReferenceExternalRouteSet? completedRouteSet = null;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, _, _) =>
                    {
                        selectedRoute = new(
                            binding,
                            environment.Generation);
                        completedRouteSet =
                            new AssemblyReferenceExternalRouteSet(
                                binding,
                                environment.Generation,
                                advancement,
                                [selectedRoute],
                                (_, _) => ValueTask.FromResult<
                                    AssemblyReferenceExternalRouteOutcome>(
                                    new AssemblyReferenceExternalRouteOutcome
                                        .Completed(
                                            binding,
                                            environment.Generation,
                                            completedRouteSet!,
                                            new(
                                                version,
                                                AssemblyBindingSelection
                                                    .Found(assembly)),
                                            selectedRoute)));
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(completedRouteSet));
                    }));

        var resolved = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Resolved>(outcome);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ExternalSupplier,
            resolved.Rung);
        Assert.Same(selectedRoute, resolved.Route);
        Assert.Equal(2, resolved.Trace.Length);
        Assert.IsType<
            AssemblyReferenceResolutionContextOutcome.Selected>(
                resolved.Trace[0].Evidence);
        Assert.IsType<
            AssemblyReferenceExternalRouteOutcome.Completed>(
                resolved.Trace[1].Evidence);
    }

    [Fact]
    public async Task ExternalSelectionMayPublishSuccessorGeneration()
    {
        ResolutionEnvironment predecessor =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest initialRequest = ReferenceRequest(assembly);
        AssemblyReferenceResolutionGenerationReceipt successorGeneration =
            SuccessorGeneration(predecessor.Generation);
        var successorRequest = new AssemblyBindingRequest(
            initialRequest.Target,
            SuccessorOrigin(assembly),
            initialRequest.Scope);
        var version = new AssemblyBindingPolicyVersion();
        TestExternalRoute? selectedRoute = null;
        AssemblyReferenceExternalRouteSet? routeSet = null;
        var continuation =
            new AssemblyReferenceResolutionContinuationReceipt(
                initialRequest,
                predecessor.Generation,
                successorRequest,
                successorGeneration,
                ownerEvidence: new object());

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    predecessor,
                    initialRequest,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                initialRequest,
                                predecessor.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, _, _) =>
                    {
                        selectedRoute = new(
                            initialRequest,
                            predecessor.Generation);
                        routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                initialRequest,
                                predecessor.Generation,
                                advancement,
                                [selectedRoute],
                                (_, _) => ValueTask.FromResult<
                                    AssemblyReferenceExternalRouteOutcome>(
                                    new AssemblyReferenceExternalRouteOutcome
                                        .Completed(
                                            successorRequest,
                                            successorGeneration,
                                            routeSet!,
                                            new(
                                                version,
                                                AssemblyBindingSelection
                                                    .Found(assembly)),
                                            selectedRoute,
                                            continuation)));
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    }));

        var resolved = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Resolved>(outcome);
        Assert.Same(initialRequest, resolved.Request);
        Assert.Same(successorRequest, resolved.FinalRequest);
        Assert.Same(successorGeneration, resolved.Generation);
        var external = Assert.IsType<
            AssemblyReferenceExternalRouteOutcome.Completed>(
                resolved.Trace[1].Evidence);
        Assert.Same(continuation, external.Continuation);
    }

    [Fact]
    public async Task ExternalOutcomeMustMatchExactContinuationSuccessor()
    {
        ResolutionEnvironment predecessor =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest initialRequest = ReferenceRequest(assembly);
        AssemblyReferenceResolutionGenerationReceipt successorGeneration =
            SuccessorGeneration(predecessor.Generation);
        var correlatedSuccessor = new AssemblyBindingRequest(
            initialRequest.Target,
            SuccessorOrigin(assembly),
            initialRequest.Scope);
        var foreignSuccessor = new AssemblyBindingRequest(
            initialRequest.Target,
            SuccessorOrigin(assembly),
            initialRequest.Scope);
        var version = new AssemblyBindingPolicyVersion();
        AssemblyReferenceExternalRouteSet? routeSet = null;
        var continuation =
            new AssemblyReferenceResolutionContinuationReceipt(
                initialRequest,
                predecessor.Generation,
                correlatedSuccessor,
                successorGeneration,
                ownerEvidence: new object());

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    predecessor,
                    initialRequest,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                initialRequest,
                                predecessor.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, _, _) =>
                    {
                        routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                initialRequest,
                                predecessor.Generation,
                                advancement,
                                [],
                                (_, _) => ValueTask.FromResult<
                                    AssemblyReferenceExternalRouteOutcome>(
                                    new AssemblyReferenceExternalRouteOutcome
                                        .Completed(
                                            foreignSuccessor,
                                            successorGeneration,
                                            routeSet!,
                                            new(
                                                version,
                                                AssemblyBindingSelection
                                                    .NameNotOwned()),
                                            continuation: continuation)));
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    }));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Same(initialRequest, rejected.FinalRequest);
        Assert.Same(predecessor.Generation, rejected.Generation);
    }

    [Fact]
    public async Task SuccessorSelectionRequiresOriginalPolicyVersion()
    {
        ResolutionEnvironment predecessor =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest initialRequest = ReferenceRequest(assembly);
        AssemblyReferenceResolutionGenerationReceipt successorGeneration =
            SuccessorGeneration(predecessor.Generation);
        var successorRequest = new AssemblyBindingRequest(
            initialRequest.Target,
            SuccessorOrigin(assembly),
            initialRequest.Scope);
        var expectedVersion = new AssemblyBindingPolicyVersion();
        var foreignVersion = new AssemblyBindingPolicyVersion();
        AssemblyReferenceExternalRouteSet? routeSet = null;
        var continuation =
            new AssemblyReferenceResolutionContinuationReceipt(
                initialRequest,
                predecessor.Generation,
                successorRequest,
                successorGeneration,
                ownerEvidence: new object());

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    predecessor,
                    initialRequest,
                    expectedVersion,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                initialRequest,
                                predecessor.Generation,
                                new(
                                    expectedVersion,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, _, _) =>
                    {
                        routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                initialRequest,
                                predecessor.Generation,
                                advancement,
                                [],
                                (_, _) => ValueTask.FromResult<
                                    AssemblyReferenceExternalRouteOutcome>(
                                    new AssemblyReferenceExternalRouteOutcome
                                        .Completed(
                                            successorRequest,
                                            successorGeneration,
                                            routeSet!,
                                            new(
                                                foreignVersion,
                                                AssemblyBindingSelection
                                                    .NameNotOwned()),
                                            continuation: continuation)));
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    }));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Same(successorRequest, rejected.FinalRequest);
        Assert.Same(successorGeneration, rejected.Generation);
    }

    [Fact]
    public async Task ContinuationRequiresFreshCorrelatedWorkspaceEvidence()
    {
        ResolutionEnvironment predecessor =
            await ResolutionEnvironment.CreateAsync();
        ResolutionEnvironment foreign =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest initialRequest = ReferenceRequest(assembly);
        var successorRequest = new AssemblyBindingRequest(
            initialRequest.Target,
            SuccessorOrigin(assembly),
            initialRequest.Scope);

        Assert.Throws<ArgumentException>(
            () => new AssemblyReferenceResolutionContinuationReceipt(
                initialRequest,
                predecessor.Generation,
                successorRequest,
                predecessor.Generation,
                ownerEvidence: new object()));
        Assert.Throws<ArgumentException>(
            () => new AssemblyReferenceResolutionContinuationReceipt(
                initialRequest,
                predecessor.Generation,
                successorRequest,
                foreign.Generation,
                ownerEvidence: new object()));
        Assert.Throws<ArgumentException>(
            () => new AssemblyReferenceResolutionContinuationReceipt(
                initialRequest,
                predecessor.Generation,
                new(
                    initialRequest.Target,
                    initialRequest.Origin,
                    initialRequest.Scope),
                SuccessorGeneration(predecessor.Generation),
                ownerEvidence: new object()));
    }

    [Fact]
    public async Task NullContextOutcomeIsRejected()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    new AssemblyBindingPolicyVersion(),
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(null!),
                    (_, _, _) => throw new InvalidOperationException()));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Empty(rejected.Trace);
    }

    [Fact]
    public async Task NullExternalFormationIsRejected()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (_, _, _) => ValueTask.FromResult<
                        AssemblyReferenceExternalRouteSetFormationOutcome>(
                        null!)));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Single(rejected.Trace);
    }

    [Fact]
    public async Task NullExternalOutcomeIsRejected()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        AssemblyReferenceExternalRouteSet? routeSet = null;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, _, _) =>
                    {
                        routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                binding,
                                environment.Generation,
                                advancement,
                                [],
                                (_, _) => ValueTask.FromResult<
                                    AssemblyReferenceExternalRouteOutcome>(
                                    null!));
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    }));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Single(rejected.Trace);
    }

    [Fact]
    public async Task ExternalRouteSetRejectsDuplicateRouteInstance()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        AssemblyReferenceResolutionContextAdvancement? advancement = null;

        _ = await ExecuteAsync(
            Request(
                environment,
                binding,
                version,
                (_, _) => ValueTask.FromResult<
                    AssemblyReferenceResolutionContextOutcome>(
                    new AssemblyReferenceResolutionContextOutcome
                        .Selected(
                            binding,
                            environment.Generation,
                            new(
                                version,
                                AssemblyBindingSelection.NameNotOwned()))),
                (contextAdvancement, _, _) =>
                {
                    advancement = contextAdvancement;
                    return ValueTask.FromResult<
                        AssemblyReferenceExternalRouteSetFormationOutcome>(
                        new
                            AssemblyReferenceExternalRouteSetFormationOutcome
                            .Unavailable(new object()));
                }));
        var route = new TestExternalRoute(
            binding,
            environment.Generation);

        Assert.Throws<ArgumentException>(
            () => new AssemblyReferenceExternalRouteSet(
                binding,
                environment.Generation,
                advancement!,
                [route, route],
                (_, _) => throw new InvalidOperationException()));
    }

    [Fact]
    public async Task ForeignPolicyVersionIsRejectedWithoutExternalWork()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var expectedVersion = new AssemblyBindingPolicyVersion();
        var foreignVersion = new AssemblyBindingPolicyVersion();
        var externalCalls = 0;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    expectedVersion,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    foreignVersion,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (_, _, _) =>
                    {
                        externalCalls++;
                        throw new InvalidOperationException(
                            "External routes must not be formed.");
                    }));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ReferencingContext,
            rejected.Rung);
        Assert.Equal(0, externalCalls);
    }

    [Fact]
    public async Task ForeignExternalGenerationIsRejectedBeforeExecution()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolutionEnvironment foreign =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        var routeExecutions = 0;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, _, _) =>
                    {
                        var route = new TestExternalRoute(
                            binding,
                            foreign.Generation);
                        var routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                binding,
                                foreign.Generation,
                                advancement,
                                [route],
                                (_, _) =>
                                {
                                    routeExecutions++;
                                    throw new InvalidOperationException(
                                        "A foreign route set must not execute.");
                                });
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    }));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ExternalSupplier,
            rejected.Rung);
        Assert.Equal(0, routeExecutions);
    }

    [Fact]
    public async Task ExhaustedFormationCannotPublishSuccess()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        AssemblyReferenceExternalRouteSet? routeSet = null;
        var routeExecutions = 0;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .Selected(
                                binding,
                                environment.Generation,
                                new(
                                    version,
                                    AssemblyBindingSelection
                                        .NameNotOwned()))),
                    (advancement, work, _) =>
                    {
                        Assert.False(
                            work.TryCharge(
                                AssemblyReferenceResolutionWorkKind
                                    .PackageRouteOccurrence,
                                amount: 2,
                                out
                                    AssemblyReferenceResolutionWorkExhaustion?
                                    exhaustion));
                        Assert.NotNull(exhaustion);
                        routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                binding,
                                environment.Generation,
                                advancement,
                                [],
                                (_, _) =>
                                {
                                    routeExecutions++;
                                    return ValueTask.FromResult<
                                        AssemblyReferenceExternalRouteOutcome>(
                                        new
                                            AssemblyReferenceExternalRouteOutcome
                                            .Completed(
                                                binding,
                                                environment.Generation,
                                                routeSet!,
                                                new(
                                                    version,
                                                    AssemblyBindingSelection
                                                        .NameNotOwned())));
                                });
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    },
                    budget: Budget(
                        maxPackageRouteOccurrences: 1)));

        var incomplete = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Incomplete>(outcome);
        var exhaustion = Assert.IsType<
            AssemblyReferenceResolutionWorkExhaustion>(
                incomplete.Evidence);
        Assert.Equal(
            AssemblyReferenceResolutionWorkKind.PackageRouteOccurrence,
            exhaustion.Kind);
        Assert.Equal(0, routeExecutions);
        Assert.Single(incomplete.Trace);
        Assert.Same(exhaustion, incomplete.Work.Exhaustion);
    }

    [Fact]
    public async Task ExpiredDeadlinePerformsNoContextWork()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var version = new AssemblyBindingPolicyVersion();
        var contextCalls = 0;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) =>
                    {
                        contextCalls++;
                        throw new InvalidOperationException(
                            "An expired request must not evaluate its context.");
                    },
                    (_, _, _) => throw new InvalidOperationException(),
                    budget: Budget(
                        deadline: DateTimeOffset.UtcNow.AddMinutes(-1))));

        var incomplete = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Incomplete>(outcome);
        var exhaustion = Assert.IsType<
            AssemblyReferenceResolutionWorkExhaustion>(
                incomplete.Evidence);
        Assert.Equal(
            AssemblyReferenceResolutionWorkKind.Deadline,
            exhaustion.Kind);
        Assert.Equal(0, contextCalls);
        Assert.Empty(incomplete.Trace);
    }

    [Fact]
    public async Task IntrinsicNonParticipationCannotBecomeUnbound()
    {
        ResolutionEnvironment environment =
            await ResolutionEnvironment.CreateAsync();
        ResolvedAssemblyReference assembly = TestAssembly();
        var binding = new AssemblyBindingRequest(
            AssemblyBindingTarget.CoreLibrary(),
            AssemblyBindingOrigin.FromAssembly(assembly),
            AssemblyResolutionScope.Any);
        var version = new AssemblyBindingPolicyVersion();
        AssemblyReferenceExternalRouteSet? routeSet = null;

        AssemblyReferenceResolutionOutcome outcome =
            await ExecuteAsync(
                Request(
                    environment,
                    binding,
                    version,
                    (_, _) => ValueTask.FromResult<
                        AssemblyReferenceResolutionContextOutcome>(
                        new AssemblyReferenceResolutionContextOutcome
                            .NonParticipating(
                                binding,
                                environment.Generation,
                                environment.FocalScope,
                                ownerEvidence: new object())),
                    (advancement, _, _) =>
                    {
                        routeSet =
                            new AssemblyReferenceExternalRouteSet(
                                binding,
                                environment.Generation,
                                advancement,
                                [],
                                (_, _) => ValueTask.FromResult<
                                    AssemblyReferenceExternalRouteOutcome>(
                                    new AssemblyReferenceExternalRouteOutcome
                                        .Completed(
                                            binding,
                                            environment.Generation,
                                            routeSet!,
                                            new(
                                                version,
                                                AssemblyBindingSelection
                                                    .NameNotOwned()))));
                        return ValueTask.FromResult<
                            AssemblyReferenceExternalRouteSetFormationOutcome>(
                            new
                                AssemblyReferenceExternalRouteSetFormationOutcome
                                .Completed(routeSet));
                    }));

        var rejected = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Rejected>(outcome);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ExternalSupplier,
            rejected.Rung);
        Assert.Equal(2, rejected.Trace.Length);
    }

    static AssemblyReferenceResolutionRequest Request(
        ResolutionEnvironment environment,
        AssemblyBindingRequest binding,
        AssemblyBindingPolicyVersion version,
        Func<
            AssemblyReferenceResolutionWorkLedger,
            CancellationToken,
            ValueTask<AssemblyReferenceResolutionContextOutcome>>
            evaluateContext,
        Func<
            AssemblyReferenceResolutionContextAdvancement,
            AssemblyReferenceResolutionWorkLedger,
            CancellationToken,
            ValueTask<AssemblyReferenceExternalRouteSetFormationOutcome>>
            formExternalRoutes,
        AssemblyReferenceResolutionWorkBudget? budget = null)
    {
        var plan = new AssemblyReferenceResolutionRoutePlan(
            binding,
            environment.Generation,
            environment.FocalScope,
            version,
            evaluateContext,
            formExternalRoutes);
        return new(
            plan,
            new AssemblyReferenceResolutionWorkLedger(
                budget ?? Budget()));
    }

    static ValueTask<AssemblyReferenceResolutionOutcome> ExecuteAsync(
        AssemblyReferenceResolutionRequest request) =>
        AssemblyReferenceResolutionLadder.ExecuteAsync(
            request,
            TestContext.Current.CancellationToken);

    static AssemblyReferenceResolutionWorkBudget Budget(
        int maxPackageRouteOccurrences = 8,
        DateTimeOffset? deadline = null) =>
        new(
            maxPackageRouteOccurrences,
            maxPackageCandidateOperations: 8,
            maxSourceOperations: 8,
            maxAcquisitions: 8,
            maxRealizedAssemblies: 8,
            maxTransferBytes: 1024 * 1024,
            maxRetainedAssemblyBytes: 1024 * 1024,
            maxWorkspaceReplacements: 1,
            deadline ?? DateTimeOffset.UtcNow.AddMinutes(5));

    static ResolvedAssemblyReference TestAssembly() =>
        ResolvedAssemblyReference.CreateFromPath(
            typeof(AssemblyReferenceResolutionLadderTests)
                .Assembly.Location,
            AssemblyResolutionProvenance.Local(
                "assembly-reference resolution ladder test"));

    static AssemblyBindingRequest ReferenceRequest(
        ResolvedAssemblyReference assembly) =>
        new(
            AssemblyBindingTarget.Reference(assembly.Identity),
            AssemblyBindingOrigin.FromAssembly(assembly),
            AssemblyResolutionScope.Any);

    static AssemblyBindingOrigin SuccessorOrigin(
        ResolvedAssemblyReference assembly)
    {
        var selected =
            Assert.IsType<AssemblyBindingSelection.Selected>(
                AssemblyBindingSelection.Found(assembly));
        return AssemblyBindingOrigin.FromOccurrence(selected.Occurrence);
    }

    static AssemblyReferenceResolutionGenerationReceipt SuccessorGeneration(
        AssemblyReferenceResolutionGenerationReceipt predecessor) =>
        new(
            predecessor.Workspace,
            predecessor.ScopeRevision,
            new ArtifactRootCompositionGenerationIdentity());

    sealed class TestExternalRoute(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation)
        : AssemblyReferenceExternalRoute(request, generation);

    sealed record ResolutionEnvironment(
        AssemblyReferenceResolutionGenerationReceipt Generation,
        MemberCallGraphFocalScopeReceipt FocalScope)
    {
        internal static async Task<ResolutionEnvironment> CreateAsync()
        {
            await using var workspace = new InspectionWorkspace();
            var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync());
            var registrations =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot());
            MemberCallGraphFocalScopeReceipt focalScope =
                MemberCallGraphFocalScopeReceipt.CaptureEverything(
                    scope.Snapshot,
                    registrations.Revision);
            return new(
                AssemblyReferenceResolutionGenerationReceipt.Capture(
                    scope.Snapshot,
                    focalScope),
                focalScope);
        }
    }
}
