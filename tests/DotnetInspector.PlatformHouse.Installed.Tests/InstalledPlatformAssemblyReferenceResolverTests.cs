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
        PlatformHouseCandidateIdentity candidate =
            PlatformHouseCandidateIdentity.Create("installed-candidate");
        var attempt = Assert.IsType<
            PlatformAssemblyReferenceSourceAttempt.Succeeded>(
                InstalledPlatformAssemblyReferenceResolver.PrepareAttempt(
                    request,
                    reference,
                    candidate));

        Assert.Same(candidate, attempt.Candidate);
        Assert.Same(reference.Contribution, attempt.Contribution);

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

    [Theory]
    [InlineData(InstalledSourceTerminalCase.Unavailable)]
    [InlineData(InstalledSourceTerminalCase.Rejected)]
    [InlineData(InstalledSourceTerminalCase.Incomplete)]
    [InlineData(InstalledSourceTerminalCase.Failed)]
    public async Task
        ResolveAsync_ProjectsInstalledSourceTerminalOutcomes(
            InstalledSourceTerminalCase terminalCase)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        string source = FindReferenceAssembly("System.Text.Json.dll");
        if (terminalCase == InstalledSourceTerminalCase.Rejected)
        {
            File.WriteAllBytes(
                Path.Combine(
                    hive.CreateReferencePack(),
                    "System.Text.Json.dll"),
                [0, 1, 2, 3]);
        }
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(source),
            cancellationToken,
            maxSourceOperations:
                terminalCase == InstalledSourceTerminalCase.Incomplete
                    ? 0
                    : 1);
        InstalledPlatformHouseResult<InstalledReferenceRealization>
            result = terminalCase == InstalledSourceTerminalCase.Failed
                ? FailedResult(adapter, request)
                : await adapter.RealizeReferenceAsync(request);
        var terminal = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded>(result);
        var attempt = Assert.IsType<
            PlatformAssemblyReferenceSourceAttempt.NotSucceeded>(
                InstalledPlatformAssemblyReferenceResolver.PrepareAttempt(
                    terminal));

        PlatformHouseOutcome<AssemblyBindingDecision> outcome =
            await InstalledPlatformAssemblyReferenceResolver.ResolveAsync(
                request,
                result,
                TerminalConsumed(terminalCase));

        Assert.Equal(
            ExpectedDiagnostic(terminalCase),
            terminal.Diagnostic.Kind);
        Assert.Same(terminal.Contribution, attempt.Contribution);
        Assert.Equal(
            terminalCase == InstalledSourceTerminalCase.Rejected
                ? PlatformHouseRejectionKind.InvalidOwnerResult
                : null,
            attempt.RejectionKind);
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
            case InstalledSourceTerminalCase.Unavailable:
                Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Unavailable>(outcome);
                break;
            case InstalledSourceTerminalCase.Rejected:
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
            case InstalledSourceTerminalCase.Incomplete:
                Assert.IsType<
                    PlatformHouseOutcome<
                        AssemblyBindingDecision>.Incomplete>(outcome);
                break;
            case InstalledSourceTerminalCase.Failed:
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
    [InlineData(
        InstalledPlatformSourceDiagnosticKind.InvalidRequest,
        PlatformHouseRejectionKind.InvalidRequest)]
    [InlineData(
        InstalledPlatformSourceDiagnosticKind.InvalidCoordinate,
        PlatformHouseRejectionKind.InvalidTargetCorrespondence)]
    [InlineData(
        InstalledPlatformSourceDiagnosticKind.InvalidLayout,
        PlatformHouseRejectionKind.InvalidOwnerResult)]
    public async Task ResolveAsync_ClassifiesInstalledRejection(
        InstalledPlatformSourceDiagnosticKind diagnosticKind,
        PlatformHouseRejectionKind rejectionKind)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        PlatformHouseRequest request = Request(
            adapter,
            ReadIdentity(
                FindReferenceAssembly("System.Text.Json.dll")),
            cancellationToken);
        var exact = Assert.IsType<PlatformTargetDemand.Exact>(
            request.Target);
        var terminal = new InstalledPlatformHouseResult<
            InstalledReferenceRealization>.NotSucceeded(
                new InstalledPlatformSourceDiagnostic(
                    diagnosticKind,
                    "Installed rejection classification test."),
                new PlatformSourceContribution.Rejected(
                    PlatformSourceFacet.Reference,
                    adapter.Capabilities.ReferenceRealization,
                    request.Snapshot,
                    PlatformSourceGeneration.Create(
                        "installed-rejection-classification"),
                    exact.Target));

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await InstalledPlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        request,
                        terminal,
                        TerminalConsumed(
                            InstalledSourceTerminalCase.Rejected)));

        Assert.Equal(
            rejectionKind,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Same(
            terminal.Contribution,
            Assert.Single(rejected.Receipt.SourceSettlements)
                .Contribution);
        Assert.Equal(diagnosticKind, terminal.Diagnostic.Kind);
    }

    [Fact]
    public async Task
        ResolveAsync_RejectsForeignInstalledSourceTerminal()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        using var hive = new TestHive();
        InstalledPlatformHouseAdapter adapter = hive.CreateAdapter();
        AssemblyReferenceIdentity identity = ReadIdentity(
            FindReferenceAssembly("System.Text.Json.dll"));
        PlatformHouseRequest sourceRequest =
            Request(adapter, identity, cancellationToken);
        PlatformHouseRequest executionRequest =
            Request(adapter, identity, cancellationToken);
        InstalledPlatformHouseResult<InstalledReferenceRealization>
            result = await adapter.RealizeReferenceAsync(sourceRequest);
        var terminal = Assert.IsType<
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded>(result);

        var rejected = Assert.IsType<
            PlatformHouseOutcome<AssemblyBindingDecision>.Rejected>(
                await InstalledPlatformAssemblyReferenceResolver
                    .ResolveAsync(
                        executionRequest,
                        result,
                        TerminalConsumed(
                            InstalledSourceTerminalCase.Unavailable)));

        Assert.Equal(
            PlatformHouseRejectionKind.InvalidOwnerResult,
            Assert.IsType<PlatformHouseRejection.OwnerEvidence>(
                    rejected.Evidence.Rejection)
                .Kind);
        Assert.Empty(rejected.Receipt.SourceSettlements);
        Assert.Equal(
            InstalledPlatformSourceDiagnosticKind.InvalidLayout,
            terminal.Diagnostic.Kind);
    }

    static PlatformHouseRequest Request(
        InstalledPlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken,
        int maxSourceOperations = 1)
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
                maxSourceOperations,
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

    static InstalledPlatformHouseResult<
        InstalledReferenceRealization>.NotSucceeded FailedResult(
            InstalledPlatformHouseAdapter adapter,
            PlatformHouseRequest request)
    {
        var exact = Assert.IsType<PlatformTargetDemand.Exact>(
            request.Target);
        return new(
            new InstalledPlatformSourceDiagnostic(
                InstalledPlatformSourceDiagnosticKind.IoFailure,
                "Installed source failure test."),
            new PlatformSourceContribution.Failed(
                PlatformSourceFacet.Reference,
                adapter.Capabilities.ReferenceRealization,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    "installed-source-failure"),
                exact.Target));
    }

    static PlatformHouseConsumedWork TerminalConsumed(
        InstalledSourceTerminalCase terminalCase) =>
        new(
            sourceOperations:
                terminalCase == InstalledSourceTerminalCase.Incomplete
                    ? 0
                    : 1,
            targetCandidates: 0,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes:
                terminalCase == InstalledSourceTerminalCase.Rejected
                    ? 4
                    : 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static InstalledPlatformSourceDiagnosticKind ExpectedDiagnostic(
        InstalledSourceTerminalCase terminalCase) =>
        terminalCase switch
        {
            InstalledSourceTerminalCase.Unavailable =>
                InstalledPlatformSourceDiagnosticKind.InvalidLayout,
            InstalledSourceTerminalCase.Rejected =>
                InstalledPlatformSourceDiagnosticKind.MalformedAssembly,
            InstalledSourceTerminalCase.Incomplete =>
                InstalledPlatformSourceDiagnosticKind.WorkLimitExceeded,
            InstalledSourceTerminalCase.Failed =>
                InstalledPlatformSourceDiagnosticKind.IoFailure,
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminalCase)),
        };

    static PlatformHouseSettlementKind ExpectedSettlement(
        InstalledSourceTerminalCase terminalCase) =>
        terminalCase switch
        {
            InstalledSourceTerminalCase.Unavailable =>
                PlatformHouseSettlementKind.Unavailable,
            InstalledSourceTerminalCase.Rejected =>
                PlatformHouseSettlementKind.Rejected,
            InstalledSourceTerminalCase.Incomplete =>
                PlatformHouseSettlementKind.Incomplete,
            InstalledSourceTerminalCase.Failed =>
                PlatformHouseSettlementKind.Failed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminalCase)),
        };

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

    public enum InstalledSourceTerminalCase
    {
        Unavailable,
        Rejected,
        Incomplete,
        Failed,
    }
}
