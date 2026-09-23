using CSharpText;
using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse;

public sealed class DocumentationAuthoredSourceOperationBinding
{
    public DocumentationAuthoredSourceOperationBinding(
        DocumentationHouseRequestIdentity request,
        DocumentationHouseOperationPlanIdentity operationPlan,
        DocumentationHousePolicyGeneration policyGeneration,
        DocumentationSubjectReference subject,
        LibraryContentReference implementationContent,
        DocumentationImplementationSubjectReference?
            implementationSubject)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationPlan);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(implementationContent);
        if (!ReferenceEquals(
                implementationContent,
                subject.Library.ImplementationAssembly))
        {
            throw new ArgumentException(
                "The implementation content must be the subject Library's exact implementation assembly.",
                nameof(implementationContent));
        }
        if (implementationSubject is not null
            && (implementationSubject.CompiledXmlIdentity
                    != subject.CompiledXmlIdentity
                || implementationSubject.IsMember != subject.IsMember))
        {
            throw new ArgumentException(
                "The implementation subject must describe the API subject's exact compiler XML identity and subject kind.",
                nameof(implementationSubject));
        }

        Request = request;
        OperationPlan = operationPlan;
        PolicyGeneration = policyGeneration;
        Subject = subject;
        ImplementationContent = implementationContent;
        ImplementationSubject = implementationSubject;
    }

    public DocumentationHouseRequestIdentity Request { get; }
    public DocumentationHouseOperationPlanIdentity OperationPlan { get; }
    public DocumentationHousePolicyGeneration PolicyGeneration { get; }
    public DocumentationSubjectReference Subject { get; }
    public LibraryReference Library => Subject.Library;
    public LibraryContentReference ImplementationContent { get; }
    public DocumentationImplementationSubjectReference?
        ImplementationSubject { get; }
}

public sealed class DocumentationAuthoredSourceOperationLimits
{
    public DocumentationAuthoredSourceOperationLimits(
        int maximumSourceDocuments,
        int maximumSourceBytes,
        CSharpAuthoredDocumentationLimits documentation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumSourceDocuments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumSourceBytes);
        ArgumentNullException.ThrowIfNull(documentation);
        Validate(documentation);

        MaximumSourceDocuments = maximumSourceDocuments;
        MaximumSourceBytes = maximumSourceBytes;
        Documentation = documentation;
    }

    public int MaximumSourceDocuments { get; }
    public int MaximumSourceBytes { get; }
    public CSharpAuthoredDocumentationLimits Documentation { get; }

    private static void Validate(
        CSharpAuthoredDocumentationLimits limits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxSourceCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxLines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxDeclarations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxDocumentationCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxXmlDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxXmlNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxParameters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxExceptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxSamples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            limits.MaxRetainedTextCharacters);
    }
}

public sealed class DocumentationAuthoredSourceOperationInvocation
{
    public DocumentationAuthoredSourceOperationInvocation(
        DocumentationAuthoredSourceOperationBinding binding,
        DocumentationAuthoredSourceOperationLimits remainingLimits,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(remainingLimits);
        if (deadline == DateTimeOffset.MinValue
            || deadline == DateTimeOffset.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deadline),
                "Authored documentation requires a finite deadline.");
        }

        Binding = binding;
        RemainingLimits = remainingLimits;
        Deadline = deadline;
    }

    public DocumentationAuthoredSourceOperationBinding Binding { get; }
    public DocumentationAuthoredSourceOperationLimits RemainingLimits { get; }
    public DateTimeOffset Deadline { get; }
}

public interface IDocumentationAuthoredSourceOperation
{
    ValueTask<DocumentationAuthoredSourceOperationOutcome> InvokeAsync(
        DocumentationAuthoredSourceOperationInvocation invocation,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default);
}

public abstract class DocumentationAuthoredSourceEvidenceReference
{
    protected DocumentationAuthoredSourceEvidenceReference()
    {
    }
}

public sealed record DocumentationAuthoredSourceOperationEvidence(
    DocumentationSourceReference Source,
    DocumentationAuthoredSourceEvidenceReference AuthoredSource);

public sealed class DocumentationAuthoredSourceContribution
{
    public DocumentationAuthoredSourceContribution(
        DocumentationAuthoredSourceOperationBinding binding,
        DocumentationAuthoredSourceOperationEvidence evidence,
        CSharpAuthoredDocumentationOutcome documentation)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(documentation);
        if (documentation
            is not CSharpAuthoredDocumentationOutcome.Available
            and not CSharpAuthoredDocumentationOutcome.Absent)
        {
            throw new ArgumentException(
                "A contribution requires available or authoritatively absent documentation.",
                nameof(documentation));
        }

        Binding = binding;
        Evidence = evidence;
        Documentation = documentation;
    }

    public DocumentationAuthoredSourceOperationBinding Binding { get; }
    public DocumentationAuthoredSourceOperationEvidence Evidence { get; }
    public CSharpAuthoredDocumentationOutcome Documentation { get; }
}

public enum DocumentationAuthoredSourceOperationOutcomeKind
{
    Produced,
    Unavailable,
    Rejected,
    Failed,
    Incomplete,
}

public enum DocumentationAuthoredUnavailableKind
{
    SourceUnavailable,
    DeclarationNotFound,
    DeclarationAmbiguous,
    DeclarationUncertain,
}

public enum DocumentationAuthoredRejectionKind
{
    AlreadyInvoked,
    BindingMismatch,
    LeaseReferenceMismatch,
    SourceRejected,
    SourceEvidenceMismatch,
}

public enum DocumentationAuthoredFailureKind
{
    SourceFailed,
    MalformedDocumentation,
}

public enum DocumentationAuthoredIncompleteBoundary
{
    ImplementationSurface,
    Deadline,
    SourceDocuments,
    SourceBytes,
    SourceCharacters,
    SourceHouse,
    Documentation,
}

public sealed record DocumentationAuthoredSourceOperationObservation
{
    public const int MaximumDetailCharacters = 4_096;

    public DocumentationAuthoredSourceOperationObservation(
        string code,
        string? detail = null)
    {
        Code = DocumentationHouseContractName.Validate(code);
        DetailWasTruncated =
            detail is { Length: > MaximumDetailCharacters };
        Detail = detail is null
            ? null
            : detail[..Math.Min(
                detail.Length,
                MaximumDetailCharacters)];
    }

    public string Code { get; }
    public string? Detail { get; }
    public bool DetailWasTruncated { get; }
}

public sealed record DocumentationAuthoredSourceOperationWorkCharge(
    long SourceBytesObserved,
    long SourceTextCharactersObserved,
    int SourceDocumentsObserved,
    CSharpAuthoredDocumentationWork? DocumentationWork);

public enum DocumentationAuthoredLeaseConsumer
{
    Operation,
    SourceHouse,
}

public sealed record DocumentationAuthoredLeaseSettlement(
    DocumentationAuthoredLeaseConsumer Consumer);

public sealed class DocumentationAuthoredSourceOperationReceiptIdentity
{
    internal DocumentationAuthoredSourceOperationReceiptIdentity()
    {
    }

    public override string ToString() =>
        nameof(DocumentationAuthoredSourceOperationReceiptIdentity);
}

public sealed class DocumentationAuthoredSourceOperationReceipt
{
    internal DocumentationAuthoredSourceOperationReceipt(
        DocumentationAuthoredSourceOperationInvocation invocation,
        DocumentationAuthoredSourceOperationWorkCharge work,
        DocumentationAuthoredLeaseSettlement leaseSettlement,
        DocumentationAuthoredSourceOperationEvidence? evidence)
    {
        Identity = new DocumentationAuthoredSourceOperationReceiptIdentity();
        Invocation = invocation;
        Work = work;
        LeaseSettlement = leaseSettlement;
        Evidence = evidence;
    }

    public DocumentationAuthoredSourceOperationReceiptIdentity Identity { get; }
    public DocumentationAuthoredSourceOperationInvocation Invocation { get; }
    public DocumentationAuthoredSourceOperationWorkCharge Work { get; }
    public DocumentationAuthoredLeaseSettlement LeaseSettlement { get; }
    public DocumentationAuthoredSourceOperationEvidence? Evidence { get; }
}

public abstract class DocumentationAuthoredSourceOperationOutcome
{
    private protected DocumentationAuthoredSourceOperationOutcome(
        DocumentationAuthoredSourceOperationOutcomeKind kind,
        DocumentationAuthoredSourceOperationInvocation invocation,
        DocumentationAuthoredSourceOperationWorkCharge work,
        DocumentationAuthoredLeaseSettlement leaseSettlement,
        DocumentationAuthoredSourceOperationEvidence? evidence)
    {
        Kind = kind;
        Invocation = invocation;
        Work = work;
        LeaseSettlement = leaseSettlement;
        Evidence = evidence;
        Receipt = new(
            invocation,
            work,
            leaseSettlement,
            evidence);
    }

    public DocumentationAuthoredSourceOperationOutcomeKind Kind { get; }
    public DocumentationAuthoredSourceOperationInvocation Invocation { get; }
    public DocumentationAuthoredSourceOperationWorkCharge Work { get; }
    public DocumentationAuthoredLeaseSettlement LeaseSettlement { get; }
    public DocumentationAuthoredSourceOperationEvidence? Evidence { get; }
    public DocumentationAuthoredSourceOperationReceipt Receipt { get; }

    public sealed class Produced : DocumentationAuthoredSourceOperationOutcome
    {
        public Produced(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationAuthoredSourceContribution contribution,
            DocumentationAuthoredSourceOperationWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement)
            : base(
                DocumentationAuthoredSourceOperationOutcomeKind.Produced,
                invocation,
                work,
                leaseSettlement,
                contribution.Evidence)
        {
            Contribution = contribution;
        }

        public DocumentationAuthoredSourceContribution Contribution { get; }
    }

    public sealed class Unavailable : DocumentationAuthoredSourceOperationOutcome
    {
        public Unavailable(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationAuthoredUnavailableKind unavailable,
            DocumentationAuthoredSourceOperationWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredSourceOperationObservation? observation = null,
            DocumentationAuthoredSourceOperationEvidence? evidence = null,
            CSharpAuthoredDocumentationOutcome? documentation = null)
            : base(
                DocumentationAuthoredSourceOperationOutcomeKind.Unavailable,
                invocation,
                work,
                leaseSettlement,
                evidence)
        {
            UnavailableKind = unavailable;
            Observation = observation;
            Documentation = documentation;
        }

        public DocumentationAuthoredUnavailableKind UnavailableKind { get; }
        public DocumentationAuthoredSourceOperationObservation? Observation { get; }
        public CSharpAuthoredDocumentationOutcome? Documentation { get; }
    }

    public sealed class Rejected : DocumentationAuthoredSourceOperationOutcome
    {
        public Rejected(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationAuthoredRejectionKind rejection,
            DocumentationAuthoredSourceOperationWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredSourceOperationObservation? observation = null)
            : base(
                DocumentationAuthoredSourceOperationOutcomeKind.Rejected,
                invocation,
                work,
                leaseSettlement,
                evidence: null)
        {
            Rejection = rejection;
            Observation = observation;
        }

        public DocumentationAuthoredRejectionKind Rejection { get; }
        public DocumentationAuthoredSourceOperationObservation? Observation { get; }
    }

    public sealed class Failed : DocumentationAuthoredSourceOperationOutcome
    {
        public Failed(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationAuthoredFailureKind failure,
            DocumentationAuthoredSourceOperationWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredSourceOperationObservation? observation = null,
            DocumentationAuthoredSourceOperationEvidence? evidence = null,
            CSharpAuthoredDocumentationOutcome? documentation = null)
            : base(
                DocumentationAuthoredSourceOperationOutcomeKind.Failed,
                invocation,
                work,
                leaseSettlement,
                evidence)
        {
            Failure = failure;
            Observation = observation;
            Documentation = documentation;
        }

        public DocumentationAuthoredFailureKind Failure { get; }
        public DocumentationAuthoredSourceOperationObservation? Observation { get; }
        public CSharpAuthoredDocumentationOutcome? Documentation { get; }
    }

    public sealed class Incomplete : DocumentationAuthoredSourceOperationOutcome
    {
        public Incomplete(
            DocumentationAuthoredSourceOperationInvocation invocation,
            DocumentationAuthoredIncompleteBoundary boundary,
            DocumentationAuthoredSourceOperationWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredSourceOperationObservation? observation = null,
            DocumentationAuthoredSourceOperationEvidence? evidence = null,
            CSharpAuthoredDocumentationOutcome? documentation = null)
            : base(
                DocumentationAuthoredSourceOperationOutcomeKind.Incomplete,
                invocation,
                work,
                leaseSettlement,
                evidence)
        {
            Boundary = boundary;
            Observation = observation;
            Documentation = documentation;
        }

        public DocumentationAuthoredIncompleteBoundary Boundary { get; }
        public DocumentationAuthoredSourceOperationObservation? Observation { get; }
        public CSharpAuthoredDocumentationOutcome? Documentation { get; }
    }
}
