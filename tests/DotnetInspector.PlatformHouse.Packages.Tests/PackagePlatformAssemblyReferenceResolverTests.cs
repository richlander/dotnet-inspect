using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Packages;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using NuGetFetch;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformAssemblyReferenceResolverTests
{
    [Fact]
    public async Task
        CanonicalBindingContinuesToExactImplementation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity targetIdentity =
            PackagePlatformTestData.Identity(image);
        AssemblyReferenceIdentity sourceIdentity =
            targetIdentity with { Version = new Version(8, 0, 0, 0) };
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                sourceIdentity,
                cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        var completed = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult.Completed>(
                await PackagePlatformAssemblyReferenceImplementation
                    .ContinueAsync(
                        request,
                        binding,
                        PlatformHouseRequestIdentity.Create(
                            "package-binding-implementation"),
                        adapter,
                        "linux-x64",
                        (implementationRequest, remaining) =>
                            environment.IssueOperation(
                                implementationRequest.CancellationToken,
                                operationTimeout:
                                    remaining.MaxDuration)));

        var resolved = Assert.IsType<
            AssemblyBindingDecision.Resolved>(binding.Value);
        var demand = Assert.IsType<PlatformLibraryDemand.Assembly>(
            Assert.IsType<PlatformPopulationDemand.Library>(
                    Assert.IsType<PlatformHouseOperation.Realize>(
                            completed.ImplementationRequest.Operation)
                        .Population)
                .Value);
        Assert.Equal(targetIdentity, resolved.Candidate.Identity);
        Assert.Equal(targetIdentity, demand.Identity);
        PackageImplementationLibrary sourceLibrary =
            Assert.Single(completed.Source.Value.Libraries);
        Assert.Equal(targetIdentity, sourceLibrary.Identity);

        PlatformLibraryReference certificate =
            completed.Implementation.Library.Value;
        var implementationAssembly =
            certificate.Library.ImplementationAssembly;
        Assert.NotNull(implementationAssembly);
        Assert.NotNull(implementationAssembly!.AssemblyIdentity);
        Assert.Equal(
            targetIdentity,
            implementationAssembly.AssemblyIdentity!.Identity);
        Assert.Same(
            completed.Implementation.Library.Value,
            completed.Implementation.Library.Receipt.RealizedLibrary);
        Assert.Same(
            completed.Implementation.Library.Value.Library,
            completed.Implementation.Library.Owner.Reference);
        Assert.Equal(2, completed.ConsumedWork!.SourceOperations);
        Assert.Equal(2, completed.ConsumedWork.Assemblies);
        Assert.Equal(
            binding.Receipt.ConsumedWork.Bytes
                + completed.Implementation.Library.Receipt
                    .HouseReceipt.ConsumedWork.Bytes,
            completed.ConsumedWork.Bytes);

        await completed.Implementation.Library.Owner.DisposeAsync();
        await completed.Implementation.Artifacts.DisposeAsync();
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        CanonicalBindingUsesNamedRangedImplementationAcquisition()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        byte[] referenceArchive = PackagePlatformTestData.Archive(
            [
                PackagePlatformTestData.Entry(
                    "ref/net11.0/System.Text.Json.dll",
                    image),
            ]);
        byte[] implementationArchive =
            PackagePlatformTestData.Archive(
                PackagePlatformTestData.RuntimePackEntries(
                    "Microsoft.NETCore.App",
                    PackagePlatformTestData.RuntimeConfiguration(),
                    PackagePlatformTestData.DependencyManifest(
                        "System.Text.Json.dll"),
                    ("System.Text.Json.dll", image)));
        var feed = new PackagePlatformRangeFeed(
            (
                PackagePlatformTestEnvironment.RuntimePackageId,
                PackagePlatformTestEnvironment.Version,
                referenceArchive),
            (
                PackagePlatformTestEnvironment
                    .RuntimeImplementationPackageId,
                PackagePlatformTestEnvironment.Version,
                implementationArchive));
        PackageSourceAuthorization sources =
            PackageSourceAuthorization.Authorize(
                [PackageSource.NuGetOrg]);
        using IPackageSourceClient client =
            PackageSourceClientFactory.CreateGallery(
                sources.Authorities[0].Association,
                feed);
        await using PackageSourceSettlementLease root =
            PackageSourceSettlementService.IssueLease(_ => client);
        var store = new InMemoryPackageStore();
        var source = new PackagePlatformSource(
            new TestAuthorization(sources),
            new PackagePayloadAcquisitionPlan(
                (_, _) => store,
                rangedSizeCut: 0));
        var adapter = new PackagePlatformHouseAdapter(
            source,
            "package-binding-range");
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                PackagePlatformTestData.Identity(image) with
                {
                    Version = new Version(8, 0, 0, 0),
                },
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        root.IssueOperationLease(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        var completed = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult.Completed>(
                await PackagePlatformAssemblyReferenceImplementation
                    .ContinueAsync(
                        request,
                        binding,
                        PlatformHouseRequestIdentity.Create(
                            "package-binding-range-implementation"),
                        adapter,
                        "linux-x64",
                        (implementationRequest, remaining) =>
                            root.IssueOperationLease(
                                implementationRequest.CancellationToken,
                                operationTimeout:
                                    remaining.MaxDuration)));

        Assert.Equal(
            PackagePayloadOrigin.Ranged,
            Assert.Single(completed.Source.Value.Frameworks).Origin);
        Assert.True(feed.RangedRequests >= 1);
        Assert.Null(
            store.TryGetCached(
                PackagePlatformTestEnvironment
                    .RuntimeImplementationPackageId,
                PackagePlatformTestEnvironment.Version,
                null));
        await completed.Implementation.Library.Owner.DisposeAsync();
        await completed.Implementation.Artifacts.DisposeAsync();
    }

    [Fact]
    public async Task
        ExhaustedBindingWorkDoesNotIssueImplementationOperation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                PackagePlatformTestData.Identity(image) with
                {
                    Version = new Version(8, 0, 0, 0),
                },
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(
                            reference.Value,
                            request.Work.MaxDuration)));
        int issued = 0;

        var incomplete = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult
                .WorkIncomplete>(
                    await PackagePlatformAssemblyReferenceImplementation
                        .ContinueAsync(
                            request,
                            binding,
                            PlatformHouseRequestIdentity.Create(
                                "package-binding-work-incomplete"),
                            adapter,
                            "linux-x64",
                            (_, _) =>
                            {
                                issued++;
                                return environment.IssueOperation(
                                    cancellationToken,
                                    operationTimeout:
                                        TimeSpan.FromSeconds(1));
                            }));

        Assert.Equal(0, issued);
        Assert.Equal(
            TimeSpan.Zero,
            incomplete.ImplementationRequest.Work.MaxDuration);
        Assert.Same(
            binding.Receipt.ConsumedWork,
            incomplete.ConsumedWork);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        MissingBindingDoesNotIssueImplementationOperation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity sourceIdentity =
            PackagePlatformTestData.Identity(image) with
            {
                Version = new Version(8, 0, 0, 0),
                PublicKeyToken = "0000000000000000",
            };
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                sourceIdentity,
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseOutcome<AssemblyBindingDecision> binding =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                request,
                reference,
                Consumed(reference.Value));
        Assert.IsType<AssemblyBindingDecision.Missing>(
            Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Completed>(binding)
                .Value);
        int issued = 0;

        var notContinued = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult
                .NotContinued>(
                    await PackagePlatformAssemblyReferenceImplementation
                        .ContinueAsync(
                            request,
                            binding,
                            PlatformHouseRequestIdentity.Create(
                                "package-binding-not-continued"),
                            adapter,
                            "linux-x64",
                            (_, _) =>
                            {
                                issued++;
                                return environment.IssueOperation(
                                    cancellationToken,
                                    operationTimeout:
                                        request.Work.MaxDuration);
                            }));

        Assert.Same(binding, notContinued.Binding);
        Assert.Equal(0, issued);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        MissingNamesakeDoesNotIssueImplementationOperation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        byte[] missingImage =
            PackagePlatformTestData.Assembly(
                "Missing.Platform.Library",
                new Version(8, 0, 0, 0));
        AssemblyReferenceIdentity sourceIdentity =
            PackagePlatformTestData.Identity(missingImage);
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                sourceIdentity,
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseOutcome<AssemblyBindingDecision> binding =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                request,
                reference,
                TerminalConsumed(
                    PackageSourceTerminalCase.Unavailable));
        Assert.IsType<AssemblyBindingDecision.Missing>(
            Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Completed>(binding)
                .Value);
        int issued = 0;

        Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult
                .NotContinued>(
                    await PackagePlatformAssemblyReferenceImplementation
                        .ContinueAsync(
                            request,
                            binding,
                            PlatformHouseRequestIdentity.Create(
                                "package-binding-missing"),
                            adapter,
                            "linux-x64",
                            (_, _) =>
                            {
                                issued++;
                                return environment.IssueOperation(
                                    cancellationToken,
                                    operationTimeout:
                                        request.Work.MaxDuration);
                            }));

        Assert.Equal(0, issued);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        IdentityMismatchedImplementationRemainsTerminal()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] referenceImage = Image();
        byte[] implementationImage =
            PackagePlatformTestData.Assembly(
                "System.Text.Json",
                new Version(10, 0, 0, 0));
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(
                referenceImage,
                implementationImage);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                PackagePlatformTestData.Identity(referenceImage) with
                {
                    Version = new Version(8, 0, 0, 0),
                },
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        var terminal = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult
                .SourceTerminal>(
                    await PackagePlatformAssemblyReferenceImplementation
                        .ContinueAsync(
                            request,
                            binding,
                            PlatformHouseRequestIdentity.Create(
                                "package-binding-identity-mismatch"),
                            adapter,
                            "linux-x64",
                            (implementationRequest, remaining) =>
                                environment.IssueOperation(
                                    implementationRequest.CancellationToken,
                                    operationTimeout:
                                        remaining.MaxDuration)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.AssemblyIdentityMismatch,
            terminal.Source.Diagnostic.Kind);
        Assert.Null(terminal.ConsumedWork);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        MeasuredImplementationTerminalChargesInvocationAndElapsedTime()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            MissingImplementationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                PackagePlatformTestData.Identity(image) with
                {
                    Version = new Version(8, 0, 0, 0),
                },
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        var terminal = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult
                .SourceTerminal>(
                    await PackagePlatformAssemblyReferenceImplementation
                        .ContinueAsync(
                            request,
                            binding,
                            PlatformHouseRequestIdentity.Create(
                                "package-binding-missing-implementation"),
                            adapter,
                            "linux-x64",
                            (implementationRequest, remaining) =>
                                environment.IssueOperation(
                                    implementationRequest.CancellationToken,
                                    operationTimeout:
                                        remaining.MaxDuration)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.MemberUnavailable,
            terminal.Source.Diagnostic.Kind);
        Assert.NotNull(terminal.Source.SourceWork);
        Assert.NotNull(terminal.ConsumedWork);
        Assert.Equal(2, terminal.ConsumedWork!.SourceOperations);
        Assert.True(terminal.ConsumedWork.Elapsed > TimeSpan.Zero);
        Assert.Equal(
            binding.Receipt.ConsumedWork.Bytes
                + terminal.Source.SourceWork!.Bytes,
            terminal.ConsumedWork.Bytes);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        MaterializationDurationExhaustionTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                PackagePlatformTestData.Identity(image) with
                {
                    Version = new Version(8, 0, 0, 0),
                },
                cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        TimeSpan remaining = TimeSpan.FromSeconds(1);
        TimeSpan bindingElapsed =
            request.Work.MaxDuration - remaining;
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(
                            reference.Value,
                            bindingElapsed)));
        var timeProvider = new SequenceTimeProvider(
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(1_500));

        var terminal = Assert.IsType<
            PackagePlatformAssemblyReferenceImplementationResult
                .MaterializationTerminal>(
                    await ContinueWithTimeProviderAsync(
                        request,
                        binding,
                        PlatformHouseRequestIdentity.Create(
                            "package-binding-materialization-timeout"),
                        adapter,
                        "linux-x64",
                        (implementationRequest, remainingWork) =>
                            environment.IssueOperation(
                                implementationRequest.CancellationToken,
                                operationTimeout:
                                    remainingWork.MaxDuration),
                        timeProvider));

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryReference>.Incomplete>(
                    terminal.Materialization.TerminalRealization.Outcome);
        Assert.Equal(
            TimeSpan.FromMilliseconds(1_500),
            incomplete.Receipt.ConsumedWork.Elapsed);
        Assert.Equal(
            bindingElapsed + TimeSpan.FromMilliseconds(1_500),
            terminal.ConsumedWork!.Elapsed);
        Assert.Null(
            terminal.Materialization.TerminalRealization
                .Receipt.RealizedLibrary);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        CancellationBetweenBindingAndImplementationTransfersNoAuthority()
    {
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            ContinuationEnvironment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            ContinuationRequest(
                adapter,
                PackagePlatformTestData.Identity(image) with
                {
                    Version = new Version(8, 0, 0, 0),
                },
                cancellation.Token);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var binding = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        cancellation.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackagePlatformAssemblyReferenceImplementation
                    .ContinueAsync(
                        request,
                        binding,
                        PlatformHouseRequestIdentity.Create(
                            "package-binding-cancelled"),
                        adapter,
                        "linux-x64",
                        (implementationRequest, remaining) =>
                            environment.IssueOperation(
                                implementationRequest.CancellationToken,
                                operationTimeout:
                                    remaining.MaxDuration))
                    .AsTask());

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        AdapterProducesNamesakeBindingContributionAfterPackageSettlement()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity targetIdentity =
            PackagePlatformTestData.Identity(image);
        AssemblyReferenceIdentity sourceIdentity =
            targetIdentity with { Version = new Version(8, 0, 0, 0) };
        PlatformHouseRequest request =
            Request(adapter, sourceIdentity, cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        await environment.AssertSettledAsync();

        var contribution =
            Assert.IsType<PlatformSourceContribution.Realization>(
                reference.Contribution);
        var population =
            Assert.IsType<PlatformPopulationDemand.Library>(
                contribution.Population);
        var binding = Assert.IsType<
            PlatformLibraryDemand.AssemblyReferenceBinding>(
                population.Value);
        var sourceBinding = Assert.IsType<
            PackageReferencePopulationDemand.AssemblyReferenceBinding>(
                reference.Value.Population);
        PackageReferenceLibrary library =
            Assert.Single(reference.Value.Libraries);
        Assert.Equal(sourceIdentity, binding.Identity);
        Assert.Equal(sourceIdentity, sourceBinding.Identity);
        Assert.True(targetIdentity.IsEquivalentTo(library.Identity));
        Assert.Same(request.Snapshot, contribution.Request);
        Assert.Same(((PlatformTargetDemand.Exact)request.Target).Target,
            contribution.Target);
        Assert.Same(
            adapter.ReferenceRealization,
            contribution.Capability);
        Assert.Equal(
            PlatformSourceContributionCompleteness.Authoritative,
            contribution.RealizationCompleteness);
        Assert.Equal(
            image,
            await PackagePlatformTestData.ReadAllAsync(library));
    }

    [Fact]
    public async Task
        ResolveAsync_PreservesPackageProvenanceAndSettlesContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity targetIdentity =
            PackagePlatformTestData.Identity(image);
        AssemblyReferenceIdentity sourceIdentity =
            targetIdentity with { Version = new Version(8, 0, 0, 0) };
        PlatformHouseRequest request =
            Request(adapter, sourceIdentity, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        await environment.AssertSettledAsync();
        PlatformHouseConsumedWork consumed = Consumed(reference.Value);
        PlatformHouseCandidateIdentity candidate =
            PlatformHouseCandidateIdentity.Create("package-candidate");
        var attempt = Assert.IsType<
            PlatformAssemblyReferenceSourceAttempt.Succeeded>(
                PackagePlatformAssemblyReferenceResolver.PrepareAttempt(
                    request,
                    reference,
                    candidate));

        Assert.Same(candidate, attempt.Candidate);
        Assert.Same(reference.Contribution, attempt.Contribution);

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        consumed));

        var decision = Assert.IsType<AssemblyBindingDecision.Resolved>(
            completed.Value);
        Assert.Equal(targetIdentity, decision.Candidate.Identity);
        var platformProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                decision.Candidate.Registration
                    .ArtifactRegistration!
                    .Provenance);
        Assert.Same(
            reference.Contribution,
            platformProvenance.Contribution);
        var packageProvenance =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                platformProvenance.SourceProvenance);
        PackageReferenceLibrary library =
            Assert.Single(reference.Value.Libraries);
        Assert.Same(
            reference.Value.Generation,
            packageProvenance.SourceGeneration);
        Assert.Equal(
            reference.Contribution.Generation.Name,
            packageProvenance.SourceGeneration.Name);
        Assert.Same(
            reference.Value.Coordinate,
            packageProvenance.Coordinate);
        Assert.Equal(library.Path, packageProvenance.Path);
        Assert.Same(
            reference.Value.Candidate,
            packageProvenance.Candidate);
        Assert.Same(
            reference.Value.Authority,
            packageProvenance.Authority);
        Assert.Same(reference.Value.Source, packageProvenance.Source);
        Assert.Same(
            reference.Value.ContentGeneration,
            packageProvenance.ContentGeneration);
        Assert.Equal(reference.Value.Origin, packageProvenance.Origin);
        Assert.True(
            library.Identity.IsEquivalentTo(
                packageProvenance.Identity));
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
        ResolveAsync_IncompatibleNamesakeReportsNameOwnedNoMatch()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity sourceIdentity =
            PackagePlatformTestData.Identity(image) with
            {
                Version = new Version(8, 0, 0, 0),
                PublicKeyToken = "0000000000000000",
            };
        PlatformHouseRequest request =
            Request(adapter, sourceIdentity, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        await environment.AssertSettledAsync();

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        var missing = Assert.IsType<AssemblyBindingDecision.Missing>(
            completed.Value);

        Assert.Equal(
            AssemblyBindingMissDisposition.NameOwnedNoMatch,
            missing.Disposition);
        Assert.Equal(
            PlatformAssemblyReferenceCompletionKind.NameOwnedNoMatch,
            Assert.IsType<PlatformHouseCompletion.AssemblyReference>(
                    completed.Receipt.Completion)
                .Kind);
    }

    [Fact]
    public async Task
        ResolveAsync_AbsentNamesakeReportsNoNameOwner()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
                [
                    TestSourceBehavior.Create(
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        entries: []),
                ]);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            cancellationToken);
        var terminal = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        await environment.AssertSettledAsync();
        var unavailable =
            Assert.IsType<PlatformSourceContribution.Unavailable>(
                terminal.Contribution);

        var completed = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        terminal,
                        TerminalConsumed(
                            PackageSourceTerminalCase.Unavailable)));
        var missing = Assert.IsType<AssemblyBindingDecision.Missing>(
            completed.Value);

        Assert.Equal(
            PlatformSourceUnavailabilityKind.Absent,
            unavailable.Reason);
        Assert.Equal(
            AssemblyBindingMissDisposition.NoNameOwner,
            missing.Disposition);
        Assert.Equal(
            PlatformAssemblyReferenceCompletionKind.NoNameOwner,
            Assert.IsType<PlatformHouseCompletion.AssemblyReference>(
                    completed.Receipt.Completion)
                .Kind);
    }

    [Fact]
    public async Task
        ResolveAsync_UnavailablePackageRemainsUnavailable()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
                [
                    TestSourceBehavior.Create(
                        PackagePlatformTestEnvironment.RuntimePackageId),
                ]);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            cancellationToken);
        var terminal = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        await environment.AssertSettledAsync();
        var unavailable =
            Assert.IsType<PlatformSourceContribution.Unavailable>(
                terminal.Contribution);

        var outcome = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Unavailable>(
                await PackagePlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        terminal,
                        TerminalConsumed(
                            PackageSourceTerminalCase.Unavailable)));

        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.PackageUnavailable,
            terminal.Diagnostic.Kind);
        Assert.Equal(
            PlatformSourceUnavailabilityKind.Unavailable,
            unavailable.Reason);
        Assert.Equal(
            PlatformHouseSettlementKind.Unavailable,
            outcome.Receipt.SettlementKind);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsSuccessfulResultForDifferentRequest()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
        PlatformHouseRequest sourceRequest =
            Request(adapter, identity, cancellationToken);
        PlatformHouseRequest executionRequest =
            Request(adapter, identity, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));
        await environment.AssertSettledAsync();

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                executionRequest,
                reference,
                Consumed(reference.Value));

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

    [Theory]
    [InlineData(PackageSourceTerminalCase.Unavailable)]
    [InlineData(PackageSourceTerminalCase.Rejected)]
    [InlineData(PackageSourceTerminalCase.Incomplete)]
    [InlineData(PackageSourceTerminalCase.Failed)]
    public async Task
        ResolveAsync_ProjectsPackageSourceTerminalOutcomes(
            PackageSourceTerminalCase terminalCase)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            TerminalEnvironment(terminalCase);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            cancellationToken,
            maxSourceOperations:
                terminalCase == PackageSourceTerminalCase.Incomplete
                    ? 0
                    : 1);

        PackagePlatformHouseResult<PackageReferenceRealization> result =
            await adapter.RealizeReferenceAsync(
                request,
                environment.IssueOperation(
                    cancellationToken,
                    operationTimeout: request.Work.MaxDuration));
        var terminal = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded>(result);
        await environment.AssertSettledAsync();
        var attempt = Assert.IsType<
            PlatformAssemblyReferenceSourceAttempt.NotSucceeded>(
                PackagePlatformAssemblyReferenceResolver.PrepareAttempt(
                    terminal));
        int payloadRequests =
            environment.Clients.Sum(
                static client => client.PayloadRequests);

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                request,
                result,
                TerminalConsumed(terminalCase));

        Assert.Equal(
            ExpectedDiagnostic(terminalCase),
            terminal.Diagnostic.Kind);
        Assert.Same(terminal.Contribution, attempt.Contribution);
        Assert.Equal(
            terminalCase == PackageSourceTerminalCase.Rejected
                ? PlatformHouseRejectionKind.InvalidOwnerResult
                : null,
            attempt.RejectionKind);
        Assert.Equal(payloadRequests,
            environment.Clients.Sum(
                static client => client.PayloadRequests));
        PlatformSourceSettlement settlement =
            Assert.Single(outcome.Receipt.SourceSettlements);
        Assert.Same(terminal.Contribution, settlement.Contribution);
        Assert.Equal(
            PlatformSourceSettlementDisposition.OutcomeRelevant,
            settlement.Disposition);
        Assert.Equal(
            ExpectedSettlement(terminalCase),
            outcome.Receipt.SettlementKind);

        switch (terminalCase)
        {
            case PackageSourceTerminalCase.Unavailable:
                Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Unavailable>(outcome);
                break;
            case PackageSourceTerminalCase.Rejected:
                var rejected = Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Rejected>(outcome);
                Assert.Equal(
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    Assert.IsType<
                            PlatformHouseRejection.OwnerEvidence>(
                                rejected.Evidence.Rejection)
                        .Kind);
                break;
            case PackageSourceTerminalCase.Incomplete:
                Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Incomplete>(outcome);
                break;
            case PackageSourceTerminalCase.Failed:
                var failed = Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Failed>(outcome);
                Assert.Equal(
                    [PlatformHouseFailureKind.Source],
                    failed.Evidence.Failures);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(terminalCase));
        }
    }

    [Theory]
    [InlineData(PackageSourceTerminalCase.Unavailable)]
    [InlineData(PackageSourceTerminalCase.Incomplete)]
    public async Task
        ResolveAsync_RejectsForeignPackageSourceTerminal(
            PackageSourceTerminalCase terminalCase)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            TerminalEnvironment(terminalCase);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
        PlatformHouseRequest sourceRequest =
            Request(
                adapter,
                identity,
                cancellationToken,
                maxSourceOperations:
                    terminalCase == PackageSourceTerminalCase.Incomplete
                        ? 0
                        : 1);
        PlatformHouseRequest executionRequest =
            Request(adapter, identity, cancellationToken);
        PackagePlatformHouseResult<PackageReferenceRealization> result =
            await adapter.RealizeReferenceAsync(
                sourceRequest,
                environment.IssueOperation(
                    cancellationToken,
                    operationTimeout:
                        sourceRequest.Work.MaxDuration));
        var terminal = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded>(result);
        await environment.AssertSettledAsync();

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                executionRequest,
                result,
                TerminalConsumed(terminalCase));

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                outcome);
        Assert.Equal(
            PlatformHouseRejectionKind.InvalidOwnerResult,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Empty(rejected.Receipt.SourceSettlements);
        Assert.Equal(
            ExpectedDiagnostic(terminalCase),
            terminal.Diagnostic.Kind);
    }

    [Fact]
    public async Task
        ResolveAsync_BudgetExhaustionPrecedesForeignPackageSourceTerminal()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image = Image();
        await using PackagePlatformTestEnvironment environment =
            TerminalEnvironment(
                PackageSourceTerminalCase.Unavailable);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
        PlatformHouseRequest sourceRequest =
            Request(adapter, identity, cancellationToken);
        PlatformHouseRequest executionRequest =
            Request(
                adapter,
                identity,
                cancellationToken,
                maxSourceOperations: 0);
        PackagePlatformHouseResult<PackageReferenceRealization> result =
            await adapter.RealizeReferenceAsync(
                sourceRequest,
                environment.IssueOperation(
                    cancellationToken,
                    operationTimeout:
                        sourceRequest.Work.MaxDuration));
        var terminal = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded>(result);
        await environment.AssertSettledAsync();

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await PackagePlatformAssemblyReferenceResolver.ResolveAsync(
                executionRequest,
                result,
                TerminalConsumed(
                    PackageSourceTerminalCase.Unavailable));

        var incomplete = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete>(
                outcome);
        Assert.Empty(incomplete.Receipt.SourceSettlements);
        Assert.Equal(
            PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
            terminal.Diagnostic.Kind);
    }

    static byte[] Image() =>
        PackagePlatformTestData.Assembly(
            "System.Text.Json",
            new Version(11, 0, 0, 0));

    static PackagePlatformTestEnvironment Environment(byte[] image) =>
        PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.Create(
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    entries:
                    [
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/System.Text.Json.dll",
                            image),
                        PackagePlatformTestData.Entry(
                            "ref/net11.0/Unrelated.dll",
                            [0, 1, 2, 3]),
                    ]),
            ]);

    static PackagePlatformTestEnvironment ContinuationEnvironment(
        byte[] image) =>
        ContinuationEnvironment(image, image);

    static PackagePlatformTestEnvironment ContinuationEnvironment(
        byte[] referenceImage,
        byte[] implementationImage) =>
        PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        PackagePlatformTestEnvironment.Version,
                        [
                            PackagePlatformTestData.Entry(
                                "ref/net11.0/System.Text.Json.dll",
                                referenceImage),
                        ]),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        PackagePlatformTestData.RuntimePackEntries(
                            "Microsoft.NETCore.App",
                            PackagePlatformTestData.RuntimeConfiguration(),
                            PackagePlatformTestData.DependencyManifest(
                                "System.Text.Json.dll"),
                            ("System.Text.Json.dll",
                                implementationImage))))
            ]);

    static PackagePlatformTestEnvironment MissingImplementationEnvironment(
        byte[] referenceImage) =>
        PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        PackagePlatformTestEnvironment.Version,
                        [
                            PackagePlatformTestData.Entry(
                                "ref/net11.0/System.Text.Json.dll",
                                referenceImage),
                        ]),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        PackagePlatformTestData.RuntimePackEntries(
                            "Microsoft.NETCore.App",
                            PackagePlatformTestData.RuntimeConfiguration(),
                            PackagePlatformTestData.DependencyManifest())))
            ]);

    static PackagePlatformHouseAdapter Adapter(
        PackagePlatformTestEnvironment environment) =>
        new(environment.CreateSource(), "package-binding");

    static PlatformHouseRequest Request(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken,
        int maxSourceOperations = 1)
    {
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(
                PackagePlatformTestEnvironment.Version));
        var origin = new PlatformHouseRequestOrigin.Standalone(
            PlatformStandaloneOperationIdentity.Create(
                "package-binding-test"));
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("package-binding-plan"),
            PlatformSourcePolicyGeneration.Create(
                "package-binding-policy"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ReferenceRealization]),
            ]);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<AssemblyBindingRequest>(
                new AssemblyBindingRequest(
                    AssemblyBindingTarget.Reference(identity),
                    AssemblyBindingOrigin.Global(),
                    AssemblyResolutionScope.Platform),
                "package-binding-metadata-request");
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
                            "package-binding-route"),
                    PlatformViewDemand.Reference);
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "package-binding-request"),
            new PlatformTargetDemand.Exact(target),
            origin,
            operation,
            sources,
            new PlatformHouseWorkBudget(
                maxSourceOperations,
                maxTargetCandidates: 0,
                maxAssemblies: 1,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    static PlatformHouseRequest ContinuationRequest(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken)
    {
        PlatformHouseRequest request = Request(
            adapter,
            identity,
            cancellationToken,
            maxSourceOperations: 4);
        var sources = new PlatformSourcePlan(
            request.Sources.Identity,
            request.Sources.Generation,
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ReferenceRealization]),
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ImplementationRealization]),
            ]);
        return new PlatformHouseRequest(
            request.Identity,
            request.Target,
            request.Origin,
            request.Operation,
            sources,
            new PlatformHouseWorkBudget(
                request.Work.MaxSourceOperations,
                request.Work.MaxTargetCandidates,
                maxAssemblies: 4,
                request.Work.MaxXmlDocuments,
                request.Work.MaxPortablePdbs,
                request.Work.MaxSourceDocuments,
                maxBytes: 32 * 1024 * 1024,
                request.Work.MaxForwardingHops,
                request.Work.MaxDuration),
            cancellationToken);
    }

    static PlatformHouseConsumedWork Consumed(
        PackageReferenceRealization reference,
        TimeSpan? elapsed = null)
    {
        PackageReferenceLibrary library =
            Assert.Single(reference.Libraries);
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
            elapsed: elapsed ?? TimeSpan.Zero);
    }

    static PackagePlatformTestEnvironment TerminalEnvironment(
        PackageSourceTerminalCase terminalCase) =>
        terminalCase switch
        {
            PackageSourceTerminalCase.Unavailable =>
                PackagePlatformTestEnvironment.Create(
                    [
                        TestSourceBehavior.Create(
                            PackagePlatformTestEnvironment
                                .RuntimePackageId),
                    ],
                    deniedPackageIds:
                    [
                        PackagePlatformTestEnvironment.RuntimePackageId,
                    ]),
            PackageSourceTerminalCase.Rejected =>
                PackagePlatformTestEnvironment.Create(
                    [
                        TestSourceBehavior.Create(
                            PackagePlatformTestEnvironment.RuntimePackageId,
                            entries:
                            [
                                PackagePlatformTestData.Entry(
                                    "ref/net11.0/System.Text.Json.dll",
                                    [0, 1, 2, 3]),
                            ]),
                    ]),
            PackageSourceTerminalCase.Incomplete =>
                PackagePlatformTestEnvironment.Create(
                    [
                        TestSourceBehavior.Create(
                            PackagePlatformTestEnvironment
                                .RuntimePackageId),
                    ]),
            PackageSourceTerminalCase.Failed =>
                PackagePlatformTestEnvironment.Create(
                    [
                        TestSourceBehavior.Create(
                            PackagePlatformTestEnvironment.RuntimePackageId,
                            payloadFailure:
                                PackageSourceFailureKind.Transport),
                    ]),
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminalCase)),
        };

    static PlatformHouseConsumedWork TerminalConsumed(
        PackageSourceTerminalCase terminalCase) =>
        new(
            sourceOperations:
                terminalCase == PackageSourceTerminalCase.Incomplete
                    ? 0
                    : 1,
            targetCandidates: 0,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes:
                terminalCase == PackageSourceTerminalCase.Rejected
                    ? 4
                    : 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static ValueTask<
        PackagePlatformAssemblyReferenceImplementationResult>
        ContinueWithTimeProviderAsync(
            PlatformHouseRequest bindingRequest,
            PlatformHouseOutcome<AssemblyBindingDecision> binding,
            PlatformHouseRequestIdentity implementationRequestIdentity,
            PackagePlatformHouseAdapter adapter,
            string runtimeIdentifier,
            Func<
                PlatformHouseRequest,
                PlatformHouseWorkBudget,
                PackageSourceOperationLease> issueOperation,
            TimeProvider timeProvider)
    {
        // Keep the production clock seam internal while exercising its
        // deterministic deadline boundary.
        System.Reflection.MethodInfo method =
            typeof(PackagePlatformAssemblyReferenceImplementation)
                .GetMethods(
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.NonPublic)
                .Single(
                    static method =>
                        method.Name == "ContinueAsync"
                        && method.GetParameters().Length == 7);
        return (ValueTask<
            PackagePlatformAssemblyReferenceImplementationResult>)
                method.Invoke(
                    null,
                    [
                        bindingRequest,
                        binding,
                        implementationRequestIdentity,
                        adapter,
                        runtimeIdentifier,
                        issueOperation,
                        timeProvider,
                    ])!;
    }

    static PackagePlatformSourceDiagnosticKind ExpectedDiagnostic(
        PackageSourceTerminalCase terminalCase) =>
        terminalCase switch
        {
            PackageSourceTerminalCase.Unavailable =>
                PackagePlatformSourceDiagnosticKind.AuthorizationDenied,
            PackageSourceTerminalCase.Rejected =>
                PackagePlatformSourceDiagnosticKind.MalformedAssembly,
            PackageSourceTerminalCase.Incomplete =>
                PackagePlatformSourceDiagnosticKind.WorkLimitExceeded,
            PackageSourceTerminalCase.Failed =>
                PackagePlatformSourceDiagnosticKind.PackageUnavailable,
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminalCase)),
        };

    static PlatformHouseSettlementKind ExpectedSettlement(
        PackageSourceTerminalCase terminalCase) =>
        terminalCase switch
        {
            PackageSourceTerminalCase.Unavailable =>
                PlatformHouseSettlementKind.Unavailable,
            PackageSourceTerminalCase.Rejected =>
                PlatformHouseSettlementKind.Rejected,
            PackageSourceTerminalCase.Incomplete =>
                PlatformHouseSettlementKind.Incomplete,
            PackageSourceTerminalCase.Failed =>
                PlatformHouseSettlementKind.Failed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminalCase)),
        };

    public enum PackageSourceTerminalCase
    {
        Unavailable,
        Rejected,
        Incomplete,
        Failed,
    }

    sealed class SequenceTimeProvider(
        params TimeSpan[] timestamps) : TimeProvider
    {
        private int _index = -1;

        public override long TimestampFrequency =>
            TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            int index = Interlocked.Increment(ref _index);
            return timestamps[Math.Min(index, timestamps.Length - 1)]
                .Ticks;
        }
    }
}
