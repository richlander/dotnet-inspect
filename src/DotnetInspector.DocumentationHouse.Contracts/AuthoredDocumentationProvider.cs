using CSharpText;
using DotnetInspector.Libraries;

namespace DotnetInspector.DocumentationHouse;

public sealed class DocumentationAuthoredProviderBinding
{
    public DocumentationAuthoredProviderBinding(
        DocumentationHouseRequestIdentity request,
        DocumentationHouseOperationPlanIdentity operationPlan,
        DocumentationHousePolicyGeneration policyGeneration,
        DocumentationSubjectReference subject,
        LibraryContentReference implementationContent)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(operationPlan);
        ArgumentNullException.ThrowIfNull(policyGeneration);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(implementationContent);
        if (!ReferenceEquals(
                implementationContent.Library,
                subject.Library))
        {
            throw new ArgumentException(
                "The implementation content must belong to the subject Library.",
                nameof(implementationContent));
        }
        if (!implementationContent.HasRole(
                LibraryContentRole.ApiAssembly)
            && !implementationContent.HasRole(
                LibraryContentRole.ImplementationAssembly))
        {
            throw new ArgumentException(
                "Authored documentation requires API or implementation assembly content.",
                nameof(implementationContent));
        }

        Request = request;
        OperationPlan = operationPlan;
        PolicyGeneration = policyGeneration;
        Subject = subject;
        ImplementationContent = implementationContent;
    }

    public DocumentationHouseRequestIdentity Request { get; }
    public DocumentationHouseOperationPlanIdentity OperationPlan { get; }
    public DocumentationHousePolicyGeneration PolicyGeneration { get; }
    public DocumentationSubjectReference Subject { get; }
    public LibraryReference Library => Subject.Library;
    public LibraryContentReference ImplementationContent { get; }
}

public sealed class DocumentationAuthoredProviderLimits
{
    public DocumentationAuthoredProviderLimits(
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

public sealed class DocumentationAuthoredProviderInvocation
{
    public DocumentationAuthoredProviderInvocation(
        DocumentationAuthoredProviderBinding binding,
        DocumentationAuthoredProviderLimits remainingLimits,
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

    public DocumentationAuthoredProviderBinding Binding { get; }
    public DocumentationAuthoredProviderLimits RemainingLimits { get; }
    public DateTimeOffset Deadline { get; }
}

public interface IDocumentationAuthoredSourceProvider
{
    ValueTask<DocumentationAuthoredProviderOutcome> InvokeAsync(
        DocumentationAuthoredProviderInvocation invocation,
        LibraryOperationLease operationLease,
        CancellationToken cancellationToken = default);
}

public abstract class DocumentationAuthoredSourceEvidenceReference
{
    protected DocumentationAuthoredSourceEvidenceReference()
    {
    }
}

public abstract class DocumentationPhysicalDeclarationEvidenceReference
{
    protected DocumentationPhysicalDeclarationEvidenceReference()
    {
    }
}

public sealed record DocumentationAuthoredProviderEvidence(
    DocumentationSourceReference Source,
    DocumentationAuthoredSourceEvidenceReference AuthoredSource,
    DocumentationPhysicalDeclarationEvidenceReference PhysicalDeclaration);

public sealed class DocumentationAuthoredSourceContribution
{
    public DocumentationAuthoredSourceContribution(
        DocumentationAuthoredProviderBinding binding,
        DocumentationAuthoredProviderEvidence evidence,
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

    public DocumentationAuthoredProviderBinding Binding { get; }
    public DocumentationAuthoredProviderEvidence Evidence { get; }
    public CSharpAuthoredDocumentationOutcome Documentation { get; }
}

public enum DocumentationAuthoredProviderOutcomeKind
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
    PhysicalDeclarationUnavailable,
    PhysicalDeclarationConflict,
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
    PhysicalDeclarationRejected,
}

public enum DocumentationAuthoredFailureKind
{
    SourceFailed,
    PhysicalDeclarationFailed,
    MalformedDocumentation,
}

public enum DocumentationAuthoredIncompleteBoundary
{
    Deadline,
    SourceDocuments,
    SourceBytes,
    SourceCharacters,
    SourceHouse,
    PhysicalDeclaration,
    Documentation,
}

public sealed record DocumentationAuthoredProviderObservation
{
    public const int MaximumDetailCharacters = 4_096;

    public DocumentationAuthoredProviderObservation(
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

public sealed record DocumentationAuthoredProviderWorkCharge(
    long SourceBytesObserved,
    long SourceTextCharactersObserved,
    int SourceDocumentsObserved,
    int AttestationContributionsObserved,
    CSharpAuthoredDocumentationWork? DocumentationWork);

public enum DocumentationAuthoredLeaseConsumer
{
    Provider,
    SourceHouse,
}

public sealed record DocumentationAuthoredLeaseSettlement(
    DocumentationAuthoredLeaseConsumer Consumer);

public sealed class DocumentationAuthoredProviderReceiptIdentity
{
    internal DocumentationAuthoredProviderReceiptIdentity()
    {
    }

    public override string ToString() =>
        nameof(DocumentationAuthoredProviderReceiptIdentity);
}

public sealed class DocumentationAuthoredProviderReceipt
{
    internal DocumentationAuthoredProviderReceipt(
        DocumentationAuthoredProviderInvocation invocation,
        DocumentationAuthoredProviderWorkCharge work,
        DocumentationAuthoredLeaseSettlement leaseSettlement,
        DocumentationAuthoredProviderEvidence? evidence)
    {
        Identity = new DocumentationAuthoredProviderReceiptIdentity();
        Invocation = invocation;
        Work = work;
        LeaseSettlement = leaseSettlement;
        Evidence = evidence;
    }

    public DocumentationAuthoredProviderReceiptIdentity Identity { get; }
    public DocumentationAuthoredProviderInvocation Invocation { get; }
    public DocumentationAuthoredProviderWorkCharge Work { get; }
    public DocumentationAuthoredLeaseSettlement LeaseSettlement { get; }
    public DocumentationAuthoredProviderEvidence? Evidence { get; }
}

public abstract class DocumentationAuthoredProviderOutcome
{
    private protected DocumentationAuthoredProviderOutcome(
        DocumentationAuthoredProviderOutcomeKind kind,
        DocumentationAuthoredProviderInvocation invocation,
        DocumentationAuthoredProviderWorkCharge work,
        DocumentationAuthoredLeaseSettlement leaseSettlement,
        DocumentationAuthoredProviderEvidence? evidence)
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

    public DocumentationAuthoredProviderOutcomeKind Kind { get; }
    public DocumentationAuthoredProviderInvocation Invocation { get; }
    public DocumentationAuthoredProviderWorkCharge Work { get; }
    public DocumentationAuthoredLeaseSettlement LeaseSettlement { get; }
    public DocumentationAuthoredProviderEvidence? Evidence { get; }
    public DocumentationAuthoredProviderReceipt Receipt { get; }

    public sealed class Produced : DocumentationAuthoredProviderOutcome
    {
        public Produced(
            DocumentationAuthoredProviderInvocation invocation,
            DocumentationAuthoredSourceContribution contribution,
            DocumentationAuthoredProviderWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement)
            : base(
                DocumentationAuthoredProviderOutcomeKind.Produced,
                invocation,
                work,
                leaseSettlement,
                contribution.Evidence)
        {
            Contribution = contribution;
        }

        public DocumentationAuthoredSourceContribution Contribution { get; }
    }

    public sealed class Unavailable : DocumentationAuthoredProviderOutcome
    {
        public Unavailable(
            DocumentationAuthoredProviderInvocation invocation,
            DocumentationAuthoredUnavailableKind unavailable,
            DocumentationAuthoredProviderWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredProviderObservation? observation = null,
            DocumentationAuthoredProviderEvidence? evidence = null,
            CSharpAuthoredDocumentationOutcome? documentation = null)
            : base(
                DocumentationAuthoredProviderOutcomeKind.Unavailable,
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
        public DocumentationAuthoredProviderObservation? Observation { get; }
        public CSharpAuthoredDocumentationOutcome? Documentation { get; }
    }

    public sealed class Rejected : DocumentationAuthoredProviderOutcome
    {
        public Rejected(
            DocumentationAuthoredProviderInvocation invocation,
            DocumentationAuthoredRejectionKind rejection,
            DocumentationAuthoredProviderWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredProviderObservation? observation = null)
            : base(
                DocumentationAuthoredProviderOutcomeKind.Rejected,
                invocation,
                work,
                leaseSettlement,
                evidence: null)
        {
            Rejection = rejection;
            Observation = observation;
        }

        public DocumentationAuthoredRejectionKind Rejection { get; }
        public DocumentationAuthoredProviderObservation? Observation { get; }
    }

    public sealed class Failed : DocumentationAuthoredProviderOutcome
    {
        public Failed(
            DocumentationAuthoredProviderInvocation invocation,
            DocumentationAuthoredFailureKind failure,
            DocumentationAuthoredProviderWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredProviderObservation? observation = null,
            DocumentationAuthoredProviderEvidence? evidence = null,
            CSharpAuthoredDocumentationOutcome? documentation = null)
            : base(
                DocumentationAuthoredProviderOutcomeKind.Failed,
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
        public DocumentationAuthoredProviderObservation? Observation { get; }
        public CSharpAuthoredDocumentationOutcome? Documentation { get; }
    }

    public sealed class Incomplete : DocumentationAuthoredProviderOutcome
    {
        public Incomplete(
            DocumentationAuthoredProviderInvocation invocation,
            DocumentationAuthoredIncompleteBoundary boundary,
            DocumentationAuthoredProviderWorkCharge work,
            DocumentationAuthoredLeaseSettlement leaseSettlement,
            DocumentationAuthoredProviderObservation? observation = null,
            DocumentationAuthoredProviderEvidence? evidence = null,
            CSharpAuthoredDocumentationOutcome? documentation = null)
            : base(
                DocumentationAuthoredProviderOutcomeKind.Incomplete,
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
        public DocumentationAuthoredProviderObservation? Observation { get; }
        public CSharpAuthoredDocumentationOutcome? Documentation { get; }
    }
}
