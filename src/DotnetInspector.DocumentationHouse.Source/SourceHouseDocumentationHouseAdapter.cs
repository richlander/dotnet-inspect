using CSharpText;
using DotnetInspector.Libraries;
using DotnetInspector.SourceHouse;

namespace DotnetInspector.DocumentationHouse.Source;

/// <summary>
/// Adapts one pre-authorized SourceHouse authored request into the
/// source-neutral DocumentationHouse operation contract.
/// </summary>
public static class SourceHouseDocumentationHouseAdapter
{
    /// <summary>
    /// Creates one cold, single-use operation bound to the exact request.
    /// </summary>
    public static IDocumentationAuthoredSourceOperation CreateOperation(
        DocumentationAuthoredSourceOperationBinding binding,
        SourceHouseAuthoredRequest request) =>
        new SourceHouseDocumentationOperation(binding, request);
}

internal sealed class SourceHouseDocumentationOperation
    : IDocumentationAuthoredSourceOperation
{
    private static readonly DocumentationAuthoredSourceOperationWorkCharge
        s_emptyWork = new(
            SourceBytesObserved: 0,
            SourceTextCharactersObserved: 0,
            SourceDocumentsObserved: 0,
            AttestationContributionsObserved: 0,
            DocumentationWork: null);

    private readonly DocumentationAuthoredSourceOperationBinding _binding;
    private readonly SourceHouseAuthoredRequest _request;
    private int _invoked;

    internal SourceHouseDocumentationOperation(
        DocumentationAuthoredSourceOperationBinding binding,
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

    public async ValueTask<DocumentationAuthoredSourceOperationOutcome> InvokeAsync(
        DocumentationAuthoredSourceOperationInvocation invocation,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(operationLease);

        if (Interlocked.Exchange(ref _invoked, 1) != 0)
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredSourceOperationOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.AlreadyInvoked,
                    s_emptyWork,
                    OperationSettlement()));
        }

        if (!ReferenceEquals(invocation.Binding, _binding))
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredSourceOperationOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.BindingMismatch,
                    s_emptyWork,
                    OperationSettlement()));
        }
        if (!ReferenceEquals(
                operationLease.Reference,
                _binding.Library))
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredSourceOperationOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind
                        .LeaseReferenceMismatch,
                    s_emptyWork,
                    OperationSettlement()));
        }

        cancellationToken.ThrowIfCancellationRequestedAfter(
            operationLease.Dispose);

        DocumentationAuthoredIncompleteBoundary? exhausted =
            ExhaustedBoundary(invocation);
        if (exhausted is { } boundary)
        {
            return CompleteLocally(
                operationLease,
                new DocumentationAuthoredSourceOperationOutcome.Incomplete(
                    invocation,
                    boundary,
                    s_emptyWork,
                    OperationSettlement()));
        }

        SourceHouseOutcome source =
            await SourceHouse.SourceHouse.ExecuteAuthoredAsync(
                    _request,
                    operationLease,
                    cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        DocumentationAuthoredSourceOperationWorkCharge sourceWork =
            Work(source.Work, documentation: null);
        DocumentationAuthoredLeaseSettlement sourceSettlement =
            SourceHouseSettlement();
        if (!ValidSourceReceipt(source))
        {
            return new DocumentationAuthoredSourceOperationOutcome.Rejected(
                invocation,
                DocumentationAuthoredRejectionKind.SourceEvidenceMismatch,
                sourceWork,
                sourceSettlement);
        }
        if (DateTimeOffset.UtcNow >= invocation.Deadline)
        {
            return new DocumentationAuthoredSourceOperationOutcome.Incomplete(
                invocation,
                DocumentationAuthoredIncompleteBoundary.Deadline,
                sourceWork,
                sourceSettlement);
        }

        return source switch
        {
            SourceHouseOutcome.Unavailable =>
                new DocumentationAuthoredSourceOperationOutcome.Unavailable(
                    invocation,
                    DocumentationAuthoredUnavailableKind.SourceUnavailable,
                    sourceWork,
                    sourceSettlement),
            SourceHouseOutcome.Rejected rejected =>
                new DocumentationAuthoredSourceOperationOutcome.Rejected(
                    invocation,
                    DocumentationAuthoredRejectionKind.SourceRejected,
                    sourceWork,
                    sourceSettlement,
                    new(rejected.Rejection.Kind.ToString())),
            SourceHouseOutcome.Failed failed =>
                new DocumentationAuthoredSourceOperationOutcome.Failed(
                    invocation,
                    DocumentationAuthoredFailureKind.SourceFailed,
                    sourceWork,
                    sourceSettlement,
                    new(
                        failed.Failure.Code,
                        failed.Failure.Detail)),
            SourceHouseOutcome.Incomplete incomplete =>
                new DocumentationAuthoredSourceOperationOutcome.Incomplete(
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

    private DocumentationAuthoredSourceOperationOutcome CompleteAvailable(
        DocumentationAuthoredSourceOperationInvocation invocation,
        SourceHouseOutcome.Available available,
        DocumentationAuthoredSourceOperationWorkCharge sourceWork,
        DocumentationAuthoredLeaseSettlement settlement,
        CancellationToken cancellationToken)
    {
        SourceHousePhysicalDeclarationOutcome physical =
            available.PhysicalDeclaration;
        if (physical
            is SourceHousePhysicalDeclarationOutcome.Unavailable unavailable)
        {
            return new DocumentationAuthoredSourceOperationOutcome.Unavailable(
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
            return new DocumentationAuthoredSourceOperationOutcome.Unavailable(
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
            return new DocumentationAuthoredSourceOperationOutcome.Rejected(
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
            return new DocumentationAuthoredSourceOperationOutcome.Failed(
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
            return new DocumentationAuthoredSourceOperationOutcome.Incomplete(
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
            return new DocumentationAuthoredSourceOperationOutcome.Rejected(
                invocation,
                DocumentationAuthoredRejectionKind.SourceEvidenceMismatch,
                sourceWork,
                settlement);
        }

        var evidence = new DocumentationAuthoredSourceOperationEvidence(
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
        DocumentationAuthoredSourceOperationWorkCharge work =
            Work(
                available.Work,
                documentation.Work);
        cancellationToken.ThrowIfCancellationRequested();
        if (DateTimeOffset.UtcNow >= invocation.Deadline)
        {
            return new DocumentationAuthoredSourceOperationOutcome.Incomplete(
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
                new DocumentationAuthoredSourceOperationOutcome.Produced(
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
                new DocumentationAuthoredSourceOperationOutcome.Failed(
                    invocation,
                    DocumentationAuthoredFailureKind
                        .MalformedDocumentation,
                    work,
                    settlement,
                    evidence: evidence,
                    documentation: documentation),
            CSharpAuthoredDocumentationOutcome.Incomplete =>
                new DocumentationAuthoredSourceOperationOutcome.Incomplete(
                    invocation,
                    DocumentationAuthoredIncompleteBoundary.Documentation,
                    work,
                    settlement,
                    evidence: evidence,
                    documentation: documentation),
            _ => throw new InvalidOperationException(
                "Unknown CSharpText authored-documentation outcome."),
        };

        DocumentationAuthoredSourceOperationOutcome Unavailable(
            DocumentationAuthoredUnavailableKind kind) =>
            new DocumentationAuthoredSourceOperationOutcome.Unavailable(
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
        DocumentationAuthoredSourceOperationInvocation invocation)
    {
        SourceHouseLimits source = _request.Plan.Limits;
        DocumentationAuthoredSourceOperationLimits remaining =
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
        DocumentationAuthoredSourceOperationBinding binding,
        SourceHouseAuthoredRequest request)
    {
        DocumentationImplementationSubjectReference? implementation =
            binding.ImplementationSubject;
        if (!ReferenceEquals(request.Library, binding.Library)
            || !ReferenceEquals(
                request.SelectedAssembly,
                binding.ImplementationContent)
            || implementation is null
            || !Equals(
                request.Target.Type,
                implementation.TypeIdentity))
        {
            return false;
        }

        return request.Target switch
        {
            SourceHouseTarget.TypeTarget =>
                !implementation.IsMember,
            SourceHouseTarget.MemberTarget member =>
                implementation.MemberIdentity is { } identity
                && Equals(member.Member, identity)
                && member.MetadataToken
                    == implementation.MetadataToken
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

    private static DocumentationAuthoredSourceOperationObservation? Observation(
        SourceHouseCapabilityObservation? observation) =>
        observation is null
            ? null
            : new(
                observation.Code,
                observation.Detail);

    private static DocumentationAuthoredSourceOperationWorkCharge Work(
        SourceHouseWorkCharge source,
        CSharpAuthoredDocumentationWork? documentation) =>
        new(
            source.SourceBytesObserved,
            source.SourceTextCharactersObserved,
            source.DocumentsObserved,
            source.AttestationContributionsObserved,
            documentation);

    private static DocumentationAuthoredLeaseSettlement OperationSettlement() =>
        new(DocumentationAuthoredLeaseConsumer.Operation);

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
