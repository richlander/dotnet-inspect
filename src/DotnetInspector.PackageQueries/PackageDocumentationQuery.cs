using CSharpText;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Packages;
using DotnetInspector.DocumentationHouse.Source;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

public sealed record PackageDocumentationQueryLimits
{
    public static PackageDocumentationQueryLimits Default { get; } = new();

    public PackageHouseLibraryMaterializationLimits Materialization { get; init; } =
        new();

    public ApiSurfaceExtractionBounds ApiSurface { get; init; } =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    public ApiSurfaceExtractionScope ApiSurfaceScope { get; init; } =
        ApiSurfaceExtractionScope.PublicWithNonPublicTypes;

    public DocumentationHouseLimits Documentation { get; init; } =
        new(
            maximumCompiledXmlContributions: 1,
            maximumCompiledXmlBytes: 8 * 1024 * 1024,
            XmlDocumentationReadLimits.Default);

    public CSharpAuthoredDocumentationLimits AuthoredDocumentation { get; init; } =
        CSharpAuthoredDocumentationLimits.Default;

    public TimeSpan DocumentationTimeout { get; init; } =
        TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Materialization);
        ArgumentNullException.ThrowIfNull(ApiSurface);
        ArgumentNullException.ThrowIfNull(Documentation);
        ArgumentNullException.ThrowIfNull(AuthoredDocumentation);
        if (!Enum.IsDefined(ApiSurfaceScope))
            throw new ArgumentOutOfRangeException(nameof(ApiSurfaceScope));
        if (DocumentationTimeout <= TimeSpan.Zero
            || DocumentationTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DocumentationTimeout),
                DocumentationTimeout,
                "The documentation timeout must be finite and positive.");
        }
    }
}

/// <summary>
/// Composes one exact PackageHouse Library handoff into combined compiled and
/// authored DocumentationHouse queries.
/// </summary>
public static class PackageDocumentationQuery
{
    public static async ValueTask<DocumentationQueryOutcome> ExecuteAsync(
        PackageHouseSettlement.Acquired settlement,
        PackageHouseLibraryHandoff.Compile handoff,
        string documentationId,
        DocumentationDemand demand,
        AssemblyContextSourceQueryContext? sourceContext = null,
        PackageDocumentationQueryLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentationId);
        IReadOnlyDictionary<string, DocumentationQueryOutcome> outcomes =
            await ExecuteManyAsync(
                    settlement,
                    handoff,
                    [documentationId],
                    demand,
                    sourceContext,
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        return outcomes[documentationId];
    }

    public static async ValueTask<
        IReadOnlyDictionary<string, DocumentationQueryOutcome>>
        ExecuteManyAsync(
            PackageHouseSettlement.Acquired settlement,
            PackageHouseLibraryHandoff.Compile handoff,
            IReadOnlyCollection<string> documentationIds,
            DocumentationDemand demand,
            AssemblyContextSourceQueryContext? sourceContext = null,
            PackageDocumentationQueryLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(documentationIds);
        if (demand is not DocumentationDemand.CompiledXml
            and not DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation)
        {
            throw new ArgumentOutOfRangeException(
                nameof(demand),
                demand,
                "Package documentation supports compiled XML or combined demand.");
        }
        bool requestsAuthored =
            demand == DocumentationDemand
                .CompiledXmlAndAuthoredSourceDocumentation;
        if (requestsAuthored)
            ArgumentNullException.ThrowIfNull(sourceContext);
        if (documentationIds.Count == 0)
            return new Dictionary<string, DocumentationQueryOutcome>();

        string[] requestedIds = [.. documentationIds];
        if (requestedIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Documentation IDs cannot be empty.",
                nameof(documentationIds));
        }
        if (requestedIds.Distinct(StringComparer.Ordinal).Count()
            != requestedIds.Length)
        {
            throw new ArgumentException(
                "Documentation IDs must be unique.",
                nameof(documentationIds));
        }

        limits ??= PackageDocumentationQueryLimits.Default;
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseLibraryMaterializationOutcome materialization =
            await PackageHouseLibraryMaterializer.MaterializeAsync(
                    settlement,
                    handoff,
                    requestsAuthored
                        ? PackageHouseLibraryOptionalArtifacts
                            .ImplementationPortablePdb
                        : PackageHouseLibraryOptionalArtifacts.None,
                    limits.Materialization,
                    cancellationToken)
                .ConfigureAwait(false);
        if (materialization
            is PackageHouseLibraryMaterializationOutcome.Terminal terminal)
        {
            throw new InvalidOperationException(
                "The selected package Library could not be materialized: "
                    + string.Join(", ", terminal.Evidence.Failures));
        }

        var completed =
            (PackageHouseLibraryMaterializationOutcome.Completed)
                materialization;
        await using var artifacts = completed.Artifacts;
        await using var owner = completed.Owner;

        IReadOnlyDictionary<string, DocumentationSubjectReference> subjects =
            CompiledDocumentationSubjectResolver.Resolve(
                completed.Receipt.Library,
                completed.Owner,
                requestedIds,
                limits.ApiSurfaceScope,
                limits.ApiSurface,
                cancellationToken);
        DocumentationQueryPlan query = ResolveQuery(
            demand,
            cancellationToken);
        var outcomes =
            new Dictionary<string, DocumentationQueryOutcome>(
                requestedIds.Length,
                StringComparer.Ordinal);
        var requests =
            new List<DocumentationHouseRequest>(
                requestedIds.Length);
        var operations =
            new List<LibraryOperationLease>(
                requestedIds.Length);
        bool houseOwnsOperations = false;
        try
        {
            foreach (string documentationId in requestedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DocumentationSubjectReference subject =
                    subjects[documentationId];
                DocumentationHouseRequestIdentity requestIdentity =
                    DocumentationHouseRequestIdentity.Create(
                        "package-documentation");
                DocumentationHouseOperationPlanIdentity planIdentity =
                    DocumentationHouseOperationPlanIdentity.Create(
                        "package-documentation");
                DocumentationHousePolicyGeneration policyGeneration =
                    DocumentationHousePolicyGeneration.Create(
                        "package-documentation-v1");
                DateTimeOffset deadline =
                    DateTimeOffset.UtcNow.Add(
                        limits.DocumentationTimeout);
                DocumentationAuthoredSourceChannelPlan? authored = null;
                if (requestsAuthored)
                {
                    DocumentationImplementationSubjectResolution
                        implementationSubject =
                        DocumentationImplementationSubjectResolver.Resolve(
                            completed.Receipt.Library,
                            completed.Owner,
                            subject,
                            limits.ApiSurfaceScope,
                            limits.ApiSurface,
                            cancellationToken);
                    authored = CreateAuthoredSourcePlan(
                        requestIdentity,
                        planIdentity,
                        policyGeneration,
                        subject,
                        implementationSubject,
                        completed.Receipt.Library,
                        sourceContext!,
                        completed.Receipt
                            .ImplementationPortablePdbOmission,
                        limits.AuthoredDocumentation,
                        deadline);
                }
                var operationPlan =
                    new DocumentationHouseOperationPlan(
                        planIdentity,
                        policyGeneration,
                        limits.Documentation,
                        deadline,
                        [
                            PackageDocumentationHouseAdapter
                                .CreateCompiledXmlContribution(
                                    completed.Receipt,
                                    subject),
                        ],
                        authored);
                requests.Add(
                    new DocumentationHouseRequest(
                        requestIdentity,
                        subject,
                        query.Demand,
                        operationPlan));
                operations.Add(IssueOperation(completed));
            }

            houseOwnsOperations = true;
            IReadOnlyList<DocumentationHouseOutcome> results =
                await DocumentationHouse.DocumentationHouse
                    .ExecuteManyAsync(
                        requests,
                        operations,
                        cancellationToken)
                    .ConfigureAwait(false);
            for (int index = 0; index < requestedIds.Length; index++)
            {
                outcomes.Add(
                    requestedIds[index],
                    DocumentationQuery.Content(results[index]));
            }
        }
        finally
        {
            if (!houseOwnsOperations)
            {
                foreach (LibraryOperationLease operation in operations)
                    operation.Dispose();
            }
        }

        return outcomes;
    }

    private static DocumentationAuthoredSourceChannelPlan?
        CreateAuthoredSourcePlan(
            DocumentationHouseRequestIdentity requestIdentity,
            DocumentationHouseOperationPlanIdentity planIdentity,
            DocumentationHousePolicyGeneration policyGeneration,
            DocumentationSubjectReference subject,
            DocumentationImplementationSubjectResolution
                implementationResolution,
            LibraryReference library,
            AssemblyContextSourceQueryContext sourceContext,
            PackageHouseLibraryOptionalArtifactOmissionKind?
                portablePdbOmission,
            CSharpAuthoredDocumentationLimits documentationLimits,
            DateTimeOffset deadline)
    {
        if (library.ImplementationAssembly
            is not { } implementation)
        {
            return null;
        }

        SourceHouseLimits sourceLimits = DocumentationSourceLimits(
            sourceContext.MemberSourceLimits,
            documentationLimits);
        DocumentationImplementationSubjectReference?
            implementationSubject =
                (implementationResolution
                    as DocumentationImplementationSubjectResolution
                        .Resolved)
                    ?.Subject;
        var binding = new DocumentationAuthoredSourceOperationBinding(
            requestIdentity,
            planIdentity,
            policyGeneration,
            subject,
            implementation,
            implementationSubject);
        IDocumentationAuthoredSourceOperation operation =
            implementationSubject is not null
                ? portablePdbOmission is { } omission
                    ? new OmittedPortablePdbOperation(
                        binding,
                        omission)
                    : SourceHouseDocumentationHouseAdapter.CreateOperation(
                        binding,
                        new SourceHouseAuthoredRequest(
                            SourceHouseRequestIdentity.Create(
                                "package-documentation"),
                            library,
                            implementation,
                            new SourceHouseTarget.MemberTarget(
                                implementationSubject.TypeIdentity,
                                implementationSubject.MemberIdentity!,
                                implementationSubject.MetadataToken!.Value,
                                SourceHouseMemberSourceForm.DocumentParts),
                            new SourceHouseOperationPlan(
                                SourceHouseOperationPlanIdentity.Create(
                                    "package-documentation"),
                                SourceHousePolicyGeneration.Create(
                                    "package-documentation-v1"),
                                sourceLimits,
                                deadline,
                                AssemblyContextSourceCapabilities.Create(
                                    sourceContext))))
                : DocumentationImplementationSubjectResolver
                    .CreateTerminalOperation(
                        binding,
                        implementationResolution);
        return new(
            binding,
            new(
                sourceLimits.MaximumDocuments,
                sourceLimits.MaximumSourceBytes,
                documentationLimits),
            operation);
    }

    private static SourceHouseLimits DocumentationSourceLimits(
        SourceHouseLimits source,
        CSharpAuthoredDocumentationLimits documentation) =>
        new(
            source.MaximumAssemblyBytes,
            source.MaximumPortablePdbBytes,
            source.TargetBounds,
            source.SourceLinkReadLimits,
            source.MaximumDocuments,
            source.MaximumTargetMappings,
            source.MaximumCandidateAttempts,
            Math.Min(
                source.MaximumSourceBytes,
                documentation.MaxSourceCharacters),
            Math.Min(
                source.MaximumSourceTextCharacters,
                documentation.MaxSourceCharacters));

    private static DocumentationQueryPlan ResolveQuery(
        DocumentationDemand demand,
        CancellationToken cancellationToken) =>
        DocumentationQuery.ResolveRequest(
            DocumentationQuery.CreateRequest(demand),
            cancellationToken) switch
        {
            DocumentationQueryRequestResult.Accepted accepted =>
                accepted.Plan,
            DocumentationQueryRequestResult.Rejected rejected =>
                throw new InvalidOperationException(
                    "The package documentation QuerySpace request was "
                        + $"rejected ({rejected.Kind})."),
            DocumentationQueryRequestResult.IntentRejected rejected =>
                throw new InvalidOperationException(
                    "The package documentation demand was rejected "
                        + $"({rejected.Failure.GetType().Name})."),
            _ => throw new InvalidOperationException(
                "Unknown documentation QuerySpace result."),
        };

    private sealed class OmittedPortablePdbOperation(
        DocumentationAuthoredSourceOperationBinding binding,
        PackageHouseLibraryOptionalArtifactOmissionKind omission)
        : IDocumentationAuthoredSourceOperation
    {
        private static readonly DocumentationAuthoredSourceOperationWorkCharge
            s_emptyWork = new(0, 0, 0, DocumentationWork: null);

        private int _invoked;

        public ValueTask<DocumentationAuthoredSourceOperationOutcome>
            InvokeAsync(
                DocumentationAuthoredSourceOperationInvocation invocation,
                LibraryOperationLease operationLease,
                CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(invocation);
            ArgumentNullException.ThrowIfNull(operationLease);

            DocumentationAuthoredSourceOperationOutcome outcome;
            if (Interlocked.Exchange(ref _invoked, 1) != 0)
            {
                outcome =
                    new DocumentationAuthoredSourceOperationOutcome
                        .Rejected(
                            invocation,
                            DocumentationAuthoredRejectionKind
                                .AlreadyInvoked,
                            s_emptyWork,
                            OperationSettlement());
            }
            else if (!ReferenceEquals(invocation.Binding, binding))
            {
                outcome =
                    new DocumentationAuthoredSourceOperationOutcome
                        .Rejected(
                            invocation,
                            DocumentationAuthoredRejectionKind
                                .BindingMismatch,
                            s_emptyWork,
                            OperationSettlement());
            }
            else if (!ReferenceEquals(
                    operationLease.Reference,
                    binding.Library))
            {
                outcome =
                    new DocumentationAuthoredSourceOperationOutcome
                        .Rejected(
                            invocation,
                            DocumentationAuthoredRejectionKind
                                .LeaseReferenceMismatch,
                            s_emptyWork,
                            OperationSettlement());
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    operationLease.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                outcome =
                    new DocumentationAuthoredSourceOperationOutcome
                        .Incomplete(
                            invocation,
                            DocumentationAuthoredIncompleteBoundary
                                .SourceBytes,
                            s_emptyWork,
                            OperationSettlement(),
                            new(
                                omission.ToString(),
                                omission
                                    == PackageHouseLibraryOptionalArtifactOmissionKind
                                        .Unreadable
                                    ? "The package implementation PDB could "
                                        + "not be read."
                                    : "The package implementation PDB "
                                        + "exceeded the authorized "
                                        + "materialization budget."));
            }

            operationLease.Dispose();
            return ValueTask.FromResult(outcome);
        }

        private static DocumentationAuthoredLeaseSettlement
            OperationSettlement() =>
            new(DocumentationAuthoredLeaseConsumer.Operation);
    }

    private static LibraryOperationLease IssueOperation(
        PackageHouseLibraryMaterializationOutcome.Completed materialized) =>
        materialized.Owner.IssueOperationLease(
            materialized.Receipt.Library) switch
        {
            LibraryOperationLeaseIssueOutcome.Issued issued =>
                issued.Lease,
            LibraryOperationLeaseIssueOutcome outcome =>
                throw new InvalidOperationException(
                    "The selected package Library rejected documentation "
                        + $"access ({outcome.GetType().Name})."),
        };
}
