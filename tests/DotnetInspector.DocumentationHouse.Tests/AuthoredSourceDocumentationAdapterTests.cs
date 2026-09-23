using CSharpText;
using CSharpText.MemberSlicing;
using DotnetInspector.DocumentationHouse.Source;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.DocumentationHouse.Tests;

public sealed partial class CompiledXmlDocumentationHouseTests
{
    [Fact]
    public async Task
        RealMemberTextSlicerMethod_ProducesDetachedAuthoredDocumentation()
    {
        var capability = new CountingSourceCapability(SourceBytes());
        await using LibraryFixture library =
            await CreateSourceLibraryAsync();
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                scenario.Binding,
                scenario.SourceRequest);

        Assert.Equal(0, capability.SourceReads);
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
        Assert.NotNull(produced.Work.DocumentationWork);
        Assert.Equal(1, capability.SourceReads);
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
        AssertOperationSettled(
            repeatedOperation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task ForeignBinding_RejectsBeforeSourceWorkAndSettlesLease()
    {
        var capability = new CountingSourceCapability(SourceBytes());
        await using LibraryFixture library =
            await CreateSourceLibraryAsync();
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
            scenario.Binding.ImplementationContent,
            scenario.Binding.ImplementationSubject);
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
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task SourceByteBudget_StopsBeforeSourceWorkAndSettlesLease()
    {
        var capability = new CountingSourceCapability(SourceBytes());
        await using LibraryFixture library =
            await CreateSourceLibraryAsync();
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
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task
        AccessorImplementationSubject_IsUnavailableBeforeSourceWork()
    {
        var capability = new CountingSourceCapability(SourceBytes());
        await using LibraryFixture library =
            await CreateSourceLibraryAsync();
        AuthoredScenario scenario =
            AuthoredScenario.Create(library, capability);
        DocumentationImplementationSubjectReference implementation =
            Assert.IsType<DocumentationImplementationSubjectReference>(
                scenario.Binding.ImplementationSubject);
        var accessorImplementation =
            new DocumentationImplementationSubjectReference(
                implementation.TypeIdentity,
                implementation.MemberIdentity,
                implementation.MetadataToken,
                ApiMethodSemanticsKind.PropertyGetter,
                implementation.CompiledXmlIdentity);
        var binding = new DocumentationAuthoredSourceOperationBinding(
            scenario.Binding.Request,
            scenario.Binding.OperationPlan,
            scenario.Binding.PolicyGeneration,
            scenario.Binding.Subject,
            scenario.Binding.ImplementationContent,
            accessorImplementation);
        var invocation =
            new DocumentationAuthoredSourceOperationInvocation(
                binding,
                scenario.Invocation.RemainingLimits,
                scenario.Invocation.Deadline);
        IDocumentationAuthoredSourceOperation authoredOperation =
            SourceHouseDocumentationHouseAdapter.CreateOperation(
                binding,
                scenario.SourceRequest);
        LibraryOperationLease operation = library.IssueOperation();

        DocumentationAuthoredSourceOperationOutcome.Unavailable unavailable =
            Assert.IsType<
                DocumentationAuthoredSourceOperationOutcome.Unavailable>(
                await authoredOperation.InvokeAsync(
                    invocation,
                    operation,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            DocumentationAuthoredUnavailableKind.DeclarationNotFound,
            unavailable.UnavailableKind);
        Assert.Equal(
            "AccessorUnavailable",
            unavailable.Observation?.Code);
        Assert.Equal(0, capability.SourceReads);
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task CancellationBeforeTransfer_SettlesLeaseWithoutSourceWork()
    {
        var capability = new CountingSourceCapability(SourceBytes());
        await using LibraryFixture library =
            await CreateSourceLibraryAsync();
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
        AssertOperationSettled(
            operation,
            library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task ApiAssemblyBinding_IsRejectedForDistinctImplementation()
    {
        byte[] assembly = File.ReadAllBytes(AssemblyPath());
        byte[] pdb = File.ReadAllBytes(PdbPath());
        var capability = new CountingSourceCapability(SourceBytes());
        await using LibraryFixture library =
            await LibraryFixture.CreateDistinctSourceAsync(
                assembly,
                pdb);
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
                library.Reference.ApiAssembly,
                scenario.Binding.ImplementationSubject));

        Assert.Equal("implementationContent", exception.ParamName);
        Assert.Equal(0, capability.SourceReads);
    }

    private static Task<LibraryFixture> CreateSourceLibraryAsync() =>
        LibraryFixture.CreateSourceAsync(
            File.ReadAllBytes(AssemblyPath()),
            File.ReadAllBytes(PdbPath()));

    private static string AssemblyPath() =>
        typeof(MemberTextSlicer).Assembly.Location;

    private static string PdbPath() =>
        Path.ChangeExtension(AssemblyPath(), ".pdb");

    private static byte[] SourceBytes() =>
        File.ReadAllBytes(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "CSharpText.MemberSlicing",
                "MemberTextSlicer.cs"));

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

    private sealed record AuthoredScenario(
        DocumentationAuthoredSourceOperationBinding Binding,
        SourceHouseAuthoredRequest SourceRequest,
        DocumentationAuthoredSourceOperationInvocation Invocation)
    {
        internal static AuthoredScenario Create(
            LibraryFixture library,
            ISourceHouseSourceCapability capability)
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
                library.Reference.ImplementationAssembly!,
                DocumentationImplementationSubjectReference
                    .FromApiSubject(subject));
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

    private sealed class CountingSourceCapability(byte[] source)
        : ISourceHouseSourceCapability
    {
        public int SourceReads { get; private set; }
        public SourceHouseCapabilityIdentity Identity { get; } =
            SourceHouseCapabilityIdentity.Create(
                "documentation-house-source");
        public SourceHouseCapabilityCategory Category =>
            SourceHouseCapabilityCategory.Repository;

        public ValueTask<SourceHouseCapabilityOutcome> ReadAsync(
            SourceHouseSourceCandidate candidate,
            int maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceReads++;
            return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                new SourceHouseCapabilityOutcome.Available(source));
        }
    }
}
