using CSharpText;
using DotnetInspector.Libraries;
using DotnetInspector.SourceHouse;

namespace DotnetInspector.DocumentationHouse.Source;

/// <summary>
/// Adapts one pre-authorized SourceHouse authored request into the
/// source-neutral DocumentationHouse provider contract.
/// </summary>
public sealed class SourceHouseDocumentationProvider
    : IDocumentationAuthoredSourceProvider
{
    private static readonly DocumentationAuthoredProviderWorkCharge
        s_emptyWork = new(
            SourceBytesObserved: 0,
            SourceTextCharactersObserved: 0,
            SourceDocumentsObserved: 0,
            AttestationContributionsObserved: 0,
            DocumentationWork: null);

    private readonly DocumentationAuthoredProviderBinding _binding;
    private readonly SourceHouseAuthoredRequest _request;
    private int _invoked;

    public SourceHouseDocumentationProvider(
        DocumentationAuthoredProviderBinding binding,
        SourceHouseAuthoredRequest request)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);
        if (!MatchesBinding(binding, request))
        {
            throw new ArgumentException(
                "The SourceHouse request does not match the documentation binding.",
                nameof(request));
        }

        _binding = binding;
        _request = request;
    }

    public async ValueTask<DocumentationAuthoredProviderOutcome> InvokeAsync(
        DocumentationAuthoredProviderInvocation invocation,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(operationLease);

        if (Interlocked.Exchange(ref _invoked, 1) != 0)
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredProviderOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.AlreadyInvoked,
                    s_emptyWork,
                    ProviderSettlement()));
        }

        if (!ReferenceEquals(invocation.Binding, _binding))
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredProviderOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.BindingMismatch,
                    s_emptyWork,
                    ProviderSettlement()));
        }
        if (!ReferenceEquals(
                operationLease.Reference,
                _binding.Library))
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredProviderOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind
                        .LeaseReferenceMismatch,
                    s_emptyWork,
                    ProviderSettlement()));
        }

        cancellationToken.ThrowIfCancellationRequestedAfter(
            operationLease.Dispose);

        DocumentationAuthoredIncompleteBoundary? exhausted =
            ExhaustedBoundary(invocation);
        if (exhausted is { } boundary)
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredProviderOutcome.Incomplete(
                    invocation,
                    boundary,
                    s_emptyWork,
                    ProviderSettlement()));
        }

        SourceHouseOutcome source =
            await SourceHouse.SourceHouse.ExecuteAuthoredAsync(
                    _request,
                    operationLease,
                    cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        DocumentationAuthoredProviderWorkCharge sourceWork =
            Work(source.Work, documentation: null);
        DocumentationAuthoredLeaseSettlement sourceSettlement =
            SourceHouseSettlement();
        if (!ValidSourceReceipt(source))
        {
            return new DocumentationAuthoredProviderOutcome.Rejected(
                invocation,
                DocumentationAuthoredRejectionKind.SourceEvidenceMismatch,
                sourceWork,
                sourceSettlement);
        }
        if (DateTimeOffset.UtcNow >= invocation.Deadline)
        {
            return new DocumentationAuthoredProviderOutcome.Incomplete(
                invocation,
                DocumentationAuthoredIncompleteBoundary.Deadline,
                sourceWork,
                sourceSettlement);
        }

        return source switch
        {
            SourceHouseOutcome.Unavailable =>
                new DocumentationAuthoredProviderOutcome.Unavailable(
                    invocation,
                    DocumentationAuthoredUnavailableKind.SourceUnavailable,
                    sourceWork,
                    sourceSettlement),
            SourceHouseOutcome.Rejected rejected =>
                new DocumentationAuthoredProviderOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.SourceRejected,
                    sourceWork,
                    sourceSettlement,
                    new(rejected.Rejection.Kind.ToString())),
            SourceHouseOutcome.Failed failed =>
                new DocumentationAuthoredProviderOutcome.Failed(
                    invocation,
                    DocumentationAuthoredFailureKind.SourceFailed,
                    sourceWork,
                    sourceSettlement,
                    new(
                        failed.Failure.Code,
                        failed.Failure.Detail)),
            SourceHouseOutcome.Incomplete incomplete =>
                new DocumentationAuthoredProviderOutcome.Incomplete(
                    invocation,
                    DocumentationAuthoredIncompleteBoundary.SourceHouse,
                    sourceWork,
                    sourceSettlement,
                    new(incomplete.Boundary.ToString())),
            SourceHouseOutcome.Available available =>
                CompleteAvailable(
                    invocation,
                    available,
                    sourceWork,
                    sourceSettlement,
                    cancellationToken),
            _ => throw new InvalidOperationException(
                "Unknown SourceHouse outcome."),
        };
    }

    private DocumentationAuthoredProviderOutcome CompleteAvailable(
        DocumentationAuthoredProviderInvocation invocation,
        SourceHouseOutcome.Available available,
        DocumentationAuthoredProviderWorkCharge sourceWork,
        DocumentationAuthoredLeaseSettlement settlement,
        CancellationToken cancellationToken)
    {
        SourceHousePhysicalDeclarationOutcome physical =
            available.PhysicalDeclaration;
        if (physical
            is SourceHousePhysicalDeclarationOutcome.Unavailable unavailable)
        {
            return new DocumentationAuthoredProviderOutcome.Unavailable(
                invocation,
                DocumentationAuthoredUnavailableKind
                    .PhysicalDeclarationUnavailable,
                sourceWork,
                settlement,
                Observation(unavailable.Observation));
        }
        if (physical
            is SourceHousePhysicalDeclarationOutcome.Conflict conflict)
        {
            return new DocumentationAuthoredProviderOutcome.Unavailable(
                invocation,
                DocumentationAuthoredUnavailableKind
                    .PhysicalDeclarationConflict,
                sourceWork,
                settlement,
                Observation(conflict.Observation));
        }
        if (physical
            is SourceHousePhysicalDeclarationOutcome.Rejected rejected)
        {
            return new DocumentationAuthoredProviderOutcome.Rejected(
                invocation,
                DocumentationAuthoredRejectionKind
                    .PhysicalDeclarationRejected,
                sourceWork,
                settlement,
                Observation(rejected.Observation));
        }
        if (physical
            is SourceHousePhysicalDeclarationOutcome.Failed failed)
        {
            return new DocumentationAuthoredProviderOutcome.Failed(
                invocation,
                DocumentationAuthoredFailureKind
                    .PhysicalDeclarationFailed,
                sourceWork,
                settlement,
                Observation(failed.Observation));
        }
        if (physical
            is SourceHousePhysicalDeclarationOutcome.Incomplete incomplete)
        {
            return new DocumentationAuthoredProviderOutcome.Incomplete(
                invocation,
                DocumentationAuthoredIncompleteBoundary
                    .PhysicalDeclaration,
                sourceWork,
                settlement,
                Observation(incomplete.Observation));
        }

        var exact =
            (SourceHousePhysicalDeclarationOutcome.Exact)physical;
        if (!TryGetExactDocument(
                available,
                exact,
                out string? document))
        {
            return new DocumentationAuthoredProviderOutcome.Rejected(
                invocation,
                DocumentationAuthoredRejectionKind.SourceEvidenceMismatch,
                sourceWork,
                settlement);
        }

        var evidence = new DocumentationAuthoredProviderEvidence(
            DocumentationSourceReference.Create(
                DocumentationSourceKind.SourceHouse,
                SourceReferenceName()),
            new SourceEvidenceReference(),
            new PhysicalDeclarationReference());
        CSharpAuthoredDocumentationOutcome documentation =
            CSharpAuthoredDocumentation.Read(
                new(
                    document,
                    new(
                        exact.Span.Start,
                        exact.Span.Length),
                    limits:
                        invocation.RemainingLimits.Documentation));
        DocumentationAuthoredProviderWorkCharge work =
            Work(
                available.Work,
                documentation.Work);
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= invocation.Deadline)
        {
            return new DocumentationAuthoredProviderOutcome.Incomplete(
                invocation,
                DocumentationAuthoredIncompleteBoundary.Deadline,
                work,
                settlement,
                evidence: evidence,
                documentation: documentation);
        }
        return documentation switch
        {
            CSharpAuthoredDocumentationOutcome.Available
                or CSharpAuthoredDocumentationOutcome.Absent =>
                new DocumentationAuthoredProviderOutcome.Produced(
                    invocation,
                    new(
                        _binding,
                        evidence,
                        documentation),
                    work,
                    settlement),
            CSharpAuthoredDocumentationOutcome.NoDeclaration =>
                Unavailable(
                    DocumentationAuthoredUnavailableKind
                        .DeclarationNotFound),
            CSharpAuthoredDocumentationOutcome.Ambiguous =>
                Unavailable(
                    DocumentationAuthoredUnavailableKind
                        .DeclarationAmbiguous),
            CSharpAuthoredDocumentationOutcome.Uncertain =>
                Unavailable(
                    DocumentationAuthoredUnavailableKind
                        .DeclarationUncertain),
            CSharpAuthoredDocumentationOutcome.Malformed =>
                new DocumentationAuthoredProviderOutcome.Failed(
                    invocation,
                    DocumentationAuthoredFailureKind
                        .MalformedDocumentation,
                    work,
                    settlement,
                    evidence: evidence,
                    documentation: documentation),
            CSharpAuthoredDocumentationOutcome.Incomplete =>
                new DocumentationAuthoredProviderOutcome.Incomplete(
                    invocation,
                    DocumentationAuthoredIncompleteBoundary.Documentation,
                    work,
                    settlement,
                    evidence: evidence,
                    documentation: documentation),
            _ => throw new InvalidOperationException(
                "Unknown CSharpText authored-documentation outcome."),
        };

        DocumentationAuthoredProviderOutcome Unavailable(
            DocumentationAuthoredUnavailableKind kind) =>
            new DocumentationAuthoredProviderOutcome.Unavailable(
                invocation,
                kind,
                work,
                settlement,
                evidence: evidence,
                documentation: documentation);
    }

    private bool TryGetExactDocument(
        SourceHouseOutcome.Available available,
        SourceHousePhysicalDeclarationOutcome.Exact exact,
        out string document)
    {
        document = "";
        SourceHouseAuthoredAttempt.Available source = available.Source;
        SourceHousePhysicalSourceEvidence? physicalSource =
            source.PhysicalSource;
        SourceHousePhysicalDeclarationReceipt receipt =
            exact.Receipt;
        if (physicalSource is null
            || !ReferenceEquals(receipt.Source, physicalSource)
            || !ReferenceEquals(receipt.Target, available.PhysicalTarget)
            || !ReferenceEquals(
                physicalSource.Result,
                source.ResultIdentity)
            || available.PhysicalTarget.XmlDocumentationIdentity
                is not { } xmlIdentity
            || !string.Equals(
                xmlIdentity.Value,
                _binding.Subject.CompiledXmlIdentity.Value,
                StringComparison.Ordinal))
        {
            return false;
        }

        document = _request.Target switch
        {
            SourceHouseTarget.MemberTarget =>
                source.MemberDocument?.Text ?? "",
            SourceHouseTarget.TypeTarget => source.Text,
            _ => "",
        };
        if (document.Length != physicalSource.RawUtf16Length)
            return false;
        try
        {
            return exact.Span.End <= document.Length;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private bool ValidSourceReceipt(SourceHouseOutcome outcome)
    {
        SourceHouseRequestEvidence evidence = outcome.Receipt.Request;
        return ReferenceEquals(
                outcome.Receipt.LeaseSettlement,
                outcome.LeaseSettlement)
            && outcome.LeaseSettlement.Consumer
                == SourceHouseLibraryLeaseConsumer.SourceHouse
            && ReferenceEquals(evidence.Identity, _request.Identity)
            && ReferenceEquals(evidence.Library, _request.Library)
            && ReferenceEquals(
                evidence.SelectedAssembly,
                _request.SelectedAssembly)
            && ReferenceEquals(evidence.Target, _request.Target)
            && ReferenceEquals(
                evidence.OperationPlan,
                _request.Plan.Identity)
            && ReferenceEquals(
                evidence.PolicyGeneration,
                _request.Plan.PolicyGeneration);
    }

    private DocumentationAuthoredIncompleteBoundary? ExhaustedBoundary(
        DocumentationAuthoredProviderInvocation invocation)
    {
        SourceHouseLimits source = _request.Plan.Limits;
        DocumentationAuthoredProviderLimits remaining =
            invocation.RemainingLimits;
        if (DateTimeOffset.UtcNow >= invocation.Deadline
            || _request.Plan.Deadline > invocation.Deadline)
        {
            return DocumentationAuthoredIncompleteBoundary.Deadline;
        }
        if (source.MaximumDocuments
            > remaining.MaximumSourceDocuments)
        {
            return DocumentationAuthoredIncompleteBoundary.SourceDocuments;
        }
        if (source.MaximumSourceBytes > remaining.MaximumSourceBytes)
            return DocumentationAuthoredIncompleteBoundary.SourceBytes;
        if (source.MaximumSourceTextCharacters
            > remaining.Documentation.MaxSourceCharacters)
        {
            return DocumentationAuthoredIncompleteBoundary.SourceCharacters;
        }

        return null;
    }

    private static bool MatchesBinding(
        DocumentationAuthoredProviderBinding binding,
        SourceHouseAuthoredRequest request)
    {
        if (!ReferenceEquals(request.Library, binding.Library)
            || !ReferenceEquals(
                request.SelectedAssembly,
                binding.ImplementationContent)
            || !Equals(
                request.Target.Type,
                binding.Subject.TypeIdentity))
        {
            return false;
        }

        return request.Target switch
        {
            SourceHouseTarget.TypeTarget =>
                !binding.Subject.IsMember,
            SourceHouseTarget.MemberTarget member =>
                binding.Subject.MemberIdentity is { } identity
                && Equals(member.Member, identity)
                && member.SourceForm
                    == SourceHouseMemberSourceForm.DocumentParts,
            _ => false,
        };
    }

    private string SourceReferenceName()
    {
        string name = $"source-house:{_request.Identity.Name}";
        return name.Length <= 256 ? name : "source-house";
    }

    private static DocumentationAuthoredProviderObservation? Observation(
        SourceHouseCapabilityObservation? observation) =>
        observation is null
            ? null
            : new(
                observation.Code,
                observation.Detail);

    private static DocumentationAuthoredProviderWorkCharge Work(
        SourceHouseWorkCharge source,
        CSharpAuthoredDocumentationWork? documentation) =>
        new(
            source.SourceBytesObserved,
            source.SourceTextCharactersObserved,
            source.DocumentsObserved,
            source.AttestationContributionsObserved,
            documentation);

    private static DocumentationAuthoredLeaseSettlement ProviderSettlement() =>
        new(DocumentationAuthoredLeaseConsumer.Provider);

    private static DocumentationAuthoredLeaseSettlement SourceHouseSettlement() =>
        new(DocumentationAuthoredLeaseConsumer.SourceHouse);

    private static T CompleteLocally<T>(
        LibraryOperationLease operationLease,
        T outcome)
    {
        operationLease.Dispose();
        return outcome;
    }

    private sealed class SourceEvidenceReference
        : DocumentationAuthoredSourceEvidenceReference;

    private sealed class PhysicalDeclarationReference
        : DocumentationPhysicalDeclarationEvidenceReference;
}

internal static class CancellationTokenExtensions
{
    internal static void ThrowIfCancellationRequestedAfter(
        this CancellationToken cancellationToken,
        Action settle)
    {
        if (!cancellationToken.IsCancellationRequested)
            return;

        settle();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
