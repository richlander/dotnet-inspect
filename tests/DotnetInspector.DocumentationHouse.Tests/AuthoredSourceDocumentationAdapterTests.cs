using CSharpText;
using DotnetInspector.DocumentationHouse.Source;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.SourceHouse;
using DotnetInspector.SourceHouse.BuildAttestation;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed partial class CompiledXmlDocumentationHouseTests
{
    private static readonly Lazy<SourceHouseBuildAttestation>
        s_authoredBuildAttestation = new(BuildAuthoredAttestation);

    [Fact]
    public async Task
        RealMemberTextSlicerMethod_ProducesDetachedAuthoredDocumentation()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability = new CountingAttestationCapability(attestation);
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray());
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);

        Assert.Equal(0, capability.SourceReads);
        Assert.Equal(0, capability.AttestationReads);
        LibraryOperationLease operation = library.IssueOperation();
        DocumentationAuthoredSourceOperationOutcome.Produced produced =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Produced>(
                await authoredOperation.InvokeAsync(
                    scenario.Invocation,
                    operation,
                    TestContext.Current.CancellationToken));

        var available = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                produced.Contribution.Documentation);
        Assert.Contains(
            "Locates the declaration",
            available.Documentation.Summary,
            StringComparison.Ordinal);
        Assert.Equal(DeclarationKind.Method, available.Declaration.Kind);
        Assert.NotEmpty(available.DocumentationSpans);
        Assert.Same(
            scenario.Binding,
            produced.Contribution.Binding);
        Assert.Same(
            produced.Contribution.Evidence,
            produced.Receipt.Evidence);
        Assert.Equal(
            DocumentationAuthoredLeaseConsumer.SourceHouse,
            produced.LeaseSettlement.Consumer);
        Assert.True(produced.Work.SourceBytesObserved > 0);
        Assert.True(
            produced.Work.SourceTextCharactersObserved > 0);
        Assert.Equal(1, produced.Work.AttestationContributionsObserved);
        Assert.NotNull(produced.Work.DocumentationWork);
        Assert.Equal(1, capability.SourceReads);
        Assert.Equal(1, capability.AttestationReads);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);

        LibraryOperationLease repeatedOperation =
            library.IssueOperation();
        DocumentationAuthoredSourceOperationOutcome.Rejected repeated =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Rejected>(
                await authoredOperation.InvokeAsync(
                    scenario.Invocation,
                    repeatedOperation,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            DocumentationAuthoredRejectionKind.AlreadyInvoked,
            repeated.Rejection);
        Assert.Equal(
            DocumentationAuthoredLeaseConsumer.Operation,
            repeated.LeaseSettlement.Consumer);
        Assert.Equal(1, capability.SourceReads);
        Assert.Equal(1, capability.AttestationReads);
        AssertOperationSettled(
            repeatedOperation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task ForeignBinding_RejectsBeforeSourceWorkAndSettlesLease()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability = new CountingAttestationCapability(attestation);
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray());
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);
        var foreignBinding = new DocumentationAuthoredSourceOperationBinding(
            DocumentationHouseRequestIdentity.Create(
                "foreign-documentation-request"),
            scenario.Binding.OperationPlan,
            scenario.Binding.PolicyGeneration,
            scenario.Binding.Subject,
            scenario.Binding.ImplementationContent);
        var foreignInvocation =
            new DocumentationAuthoredSourceOperationInvocation(
                foreignBinding,
                scenario.Invocation.RemainingLimits,
                scenario.Invocation.Deadline);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationAuthoredSourceOperationOutcome.Rejected rejected =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Rejected>(
                await authoredOperation.InvokeAsync(
                    foreignInvocation,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationAuthoredRejectionKind.BindingMismatch,
            rejected.Rejection);
        Assert.Equal(
            DocumentationAuthoredLeaseConsumer.Operation,
            rejected.LeaseSettlement.Consumer);
        Assert.Equal(0, capability.SourceReads);
        Assert.Equal(0, capability.AttestationReads);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task SourceByteBudget_StopsBeforeSourceWorkAndSettlesLease()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability = new CountingAttestationCapability(attestation);
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray());
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);
        var exhausted = new DocumentationAuthoredSourceOperationInvocation(
            scenario.Binding,
            new(
                scenario.Invocation.RemainingLimits
                    .MaximumSourceDocuments,
                maximumSourceBytes: 1,
                scenario.Invocation.RemainingLimits.Documentation),
            scenario.Invocation.Deadline);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationAuthoredSourceOperationOutcome.Incomplete incomplete =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Incomplete>(
                await authoredOperation.InvokeAsync(
                    exhausted,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationAuthoredIncompleteBoundary.SourceBytes,
            incomplete.Boundary);
        Assert.Equal(
            DocumentationAuthoredLeaseConsumer.Operation,
            incomplete.LeaseSettlement.Consumer);
        Assert.Equal(0, capability.SourceReads);
        Assert.Equal(0, capability.AttestationReads);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task SourceWithoutPhysicalIdentity_DoesNotReachCSharpText()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability =
            new IdentitylessAttestationCapability(attestation);
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray());
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationAuthoredSourceOperationOutcome.Unavailable unavailable =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Unavailable>(
                await authoredOperation.InvokeAsync(
                    scenario.Invocation,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationAuthoredUnavailableKind
                .PhysicalDeclarationUnavailable,
            unavailable.UnavailableKind);
        Assert.Equal(
            "SourceResultHasNoPhysicalInputIdentity",
            unavailable.Observation?.Code);
        Assert.Null(unavailable.Evidence);
        Assert.Null(unavailable.Documentation);
        Assert.Equal(
            DocumentationAuthoredLeaseConsumer.SourceHouse,
            unavailable.LeaseSettlement.Consumer);
        Assert.Equal(1, capability.SourceReads);
        Assert.Equal(0, capability.AttestationReads);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task CancellationBeforeTransfer_SettlesLeaseWithoutSourceWork()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability = new CountingAttestationCapability(attestation);
        await using LibraryFixture library =
            await LibraryFixture.CreateSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray());
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);
        LibraryOperationLease operation = library.IssueOperation();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await authoredOperation.InvokeAsync(
                    scenario.Invocation,
                    operation,
                    cancellation.Token));

        Assert.Equal(0, capability.SourceReads);
        Assert.Equal(0, capability.AttestationReads);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task ApiAssemblyBinding_IsRejectedForDistinctImplementation()
    {
        SourceHouseBuildAttestation attestation =
            s_authoredBuildAttestation.Value;
        var capability = new CountingAttestationCapability(attestation);
        await using LibraryFixture library =
            await LibraryFixture.CreateDistinctSourceAsync(
                attestation.PeImage.ToArray(),
                attestation.PortablePdbImage.ToArray());
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);

        Assert.NotSame(
            library.Reference.ApiAssembly,
            library.Reference.ImplementationAssembly);
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new DocumentationAuthoredSourceOperationBinding(
                DocumentationHouseRequestIdentity.Create(
                    "api-content-documentation-request"),
                scenario.Binding.OperationPlan,
                scenario.Binding.PolicyGeneration,
                scenario.Binding.Subject,
                library.Reference.ApiAssembly));

        Assert.Equal("implementationContent", exception.ParamName);
        Assert.Equal(0, capability.SourceReads);
        Assert.Equal(0, capability.AttestationReads);
    }

    private static SourceHouseBuildAttestation BuildAuthoredAttestation()
    {
        CSharpBuildAttestationOutcome outcome =
            CSharpBuildAttestor.EmitAndAttest(
                new(
                    "DocumentationHouseAuthoredOperationFixture",
                    AuthoredBuildSources(),
                    TrustedPlatformAssemblyPaths(),
                    SourceHouseCapabilityIdentity.Create(
                        "documentation-house-build-attestor"),
                    SourceHouseAttestationIssuerIdentity.Create(
                        "dotnet-inspect-build"),
                    SourceHouseAttestationProfileIdentity.Create(
                        "direct-csharp-emit-v1"),
                    SourceHouseAttestationGeneration.Create(
                        "documentation-house-source-operation")));
        if (outcome is CSharpBuildAttestationOutcome.Failed failed)
        {
            Assert.Fail(
                string.Join(
                    Environment.NewLine,
                    failed.Diagnostics));
        }

        return Assert.IsType<
                CSharpBuildAttestationOutcome.Available>(outcome)
            .Attestation;
    }

    private static CSharpBuildSource[] AuthoredBuildSources()
    {
        string directory = Path.Combine(
            RepositoryRoot(),
            "src",
            "CSharpText.MemberSlicing");
        return
        [
            .. Directory.EnumerateFiles(
                    directory,
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(path =>
                    new CSharpBuildSource(
                        path,
                        File.ReadAllBytes(path))),
        ];
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    private static string[] TrustedPlatformAssemblyPaths()
    {
        string runtimeDirectory =
            Path.GetDirectoryName(typeof(object).Assembly.Location)
            ?? throw new InvalidOperationException(
                "The runtime assembly has no directory.");
        return
        [
            .. Directory.EnumerateFiles(
                runtimeDirectory,
                "*.dll",
                SearchOption.TopDirectoryOnly),
            typeof(CSharpSourceText).Assembly.Location,
        ];
    }

    private sealed record AuthoredScenario(
        DocumentationAuthoredSourceOperationBinding Binding,
        SourceHouseAuthoredRequest SourceRequest,
        DocumentationAuthoredSourceOperationInvocation Invocation)
    {
        internal static AuthoredScenario Create(
            LibraryFixture library,
            ISourceHousePhysicalDeclarationCapability capability)
        {
            LibraryApiSurfaceCorrespondence correspondence =
                library.ApiSurfaceCorrespondence;
            ApiType type = Assert.Single(
                correspondence.Surface.Types,
                candidate =>
                    candidate.FullName
                        == "CSharpText.MemberSlicing.MemberTextSlicer");
            ApiMember member = Assert.Single(
                type.Members,
                candidate =>
                    candidate.Name == "ExtractMemberText"
                    && candidate.MetadataToken is not null);
            DocumentationSubjectReference subject =
                DocumentationSubjectReference.ForMember(
                    correspondence,
                    type,
                    member);
            DateTimeOffset deadline =
                DateTimeOffset.UtcNow.AddMinutes(1);
            var sourceRequest = new SourceHouseAuthoredRequest(
                SourceHouseRequestIdentity.Create(
                    "documentation-authored-source"),
                library.Reference,
                library.Reference.ImplementationAssembly!,
                new SourceHouseTarget.MemberTarget(
                    type.DefinitionName!,
                    ApiMemberIdentity.GetMemberAnchor(type, member),
                    member.MetadataToken!.Value,
                    SourceHouseMemberSourceForm.DocumentParts),
                new SourceHouseOperationPlan(
                    SourceHouseOperationPlanIdentity.Create(
                        "documentation-authored-plan"),
                    SourceHousePolicyGeneration.Create(
                        "documentation-authored-policy"),
                    SourceLimits(),
                    deadline,
                    [capability]));
            var binding = new DocumentationAuthoredSourceOperationBinding(
                DocumentationHouseRequestIdentity.Create(
                    "documentation-request"),
                DocumentationHouseOperationPlanIdentity.Create(
                    "documentation-plan"),
                DocumentationHousePolicyGeneration.Create(
                    "documentation-policy"),
                subject,
                library.Reference.ImplementationAssembly!);
            var invocation =
                new DocumentationAuthoredSourceOperationInvocation(
                    binding,
                    new(
                        maximumSourceDocuments: 1_000,
                        maximumSourceBytes: 1024 * 1024,
                        CSharpAuthoredDocumentationLimits.Default),
                    deadline);
            return new(binding, sourceRequest, invocation);
        }
    }

    private static SourceHouseLimits SourceLimits() =>
        new(
            maximumAssemblyBytes: 16 * 1024 * 1024,
            maximumPortablePdbBytes: 16 * 1024 * 1024,
            targetBounds: s_apiSurfaceBounds,
            sourceLinkReadLimits: new(
                maxEmbeddedPdbBytes: 16 * 1024 * 1024,
                maxMapBytes: 4 * 1024 * 1024,
                maxMappings: 10_000),
            maximumDocuments: 1_000,
            maximumTargetMappings: 1_000,
            maximumCandidateAttempts: 10,
            maximumSourceBytes: 1024 * 1024,
            maximumSourceTextCharacters: 1024 * 1024);

    private sealed class CountingAttestationCapability(
        SourceHouseBuildAttestation inner)
        : ISourceHousePhysicalDeclarationCapability
    {
        public int SourceReads { get; private set; }
        public int AttestationReads { get; private set; }
        public SourceHouseCapabilityIdentity Identity => inner.Identity;
        public SourceHouseCapabilityCategory Category => inner.Category;
        public SourceHouseAttestationIssuerIdentity Issuer => inner.Issuer;
        public SourceHouseAttestationProfileIdentity Profile => inner.Profile;
        public SourceHouseAttestationGeneration Generation =>
            inner.Generation;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            SourceReads++;
            return inner.ReadAsync(
                candidate,
                maximumBytes,
                cancellationToken);
        }

        public ValueTask<SourceHouseAttestationCapabilityOutcome>
            ReadAttestationsAsync(
                SourceHousePhysicalDeclarationRequest request,
                int maximumContributions,
                CancellationToken cancellationToken)
        {
            AttestationReads++;
            return inner.ReadAttestationsAsync(
                request,
                maximumContributions,
                cancellationToken);
        }
    }

    private sealed class IdentitylessAttestationCapability(
        SourceHouseBuildAttestation inner)
        : ISourceHousePhysicalDeclarationCapability
    {
        public int SourceReads { get; private set; }
        public int AttestationReads { get; private set; }
        public SourceHouseCapabilityIdentity Identity => inner.Identity;
        public SourceHouseCapabilityCategory Category => inner.Category;
        public SourceHouseAttestationIssuerIdentity Issuer => inner.Issuer;
        public SourceHouseAttestationProfileIdentity Profile => inner.Profile;
        public SourceHouseAttestationGeneration Generation =>
            inner.Generation;

        public async ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            SourceReads++;
            SourceHouseCapabilityOutcome outcome =
                await inner.ReadAsync(
                    candidate,
                    maximumBytes,
                    cancellationToken);
            if (outcome is not SourceHouseCapabilityOutcome.Available
                available)
            {
                return outcome;
            }

            return new SourceHouseCapabilityOutcome.Available(
                available.Bytes.AsSpan(),
                observation: available.Observation);
        }

        public ValueTask<SourceHouseAttestationCapabilityOutcome>
            ReadAttestationsAsync(
                SourceHousePhysicalDeclarationRequest request,
                int maximumContributions,
                CancellationToken cancellationToken)
        {
            AttestationReads++;
            return inner.ReadAttestationsAsync(
                request,
                maximumContributions,
                cancellationToken);
        }
    }
}
