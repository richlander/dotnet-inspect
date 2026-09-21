using CSharpText;

namespace DotnetInspector.DocumentationHouse;

public enum DocumentationCompiledXmlAttemptKind
{
    Available,
    Absent,
    Unavailable,
    Ambiguous,
    Rejected,
    Failed,
    Incomplete,
}

public enum DocumentationCompiledXmlRejectionKind
{
    SubjectMismatch,
    LibraryMismatch,
    ApiContentMismatch,
    CompanionMismatch,
}

public sealed record DocumentationCompiledXmlRejection(
    CompiledXmlContribution Contribution,
    DocumentationCompiledXmlRejectionKind Kind);

public enum DocumentationCompiledXmlFailureKind
{
    MalformedOrUnreadableDocument,
    ContentAccessFailed,
}

public sealed record DocumentationCompiledXmlFailure(
    DocumentationCompiledXmlFailureKind Kind);

public enum DocumentationIncompleteBoundary
{
    Deadline,
    ContributionLimit,
    CompanionSelectionPartial,
    CompiledXmlByteLimit,
}

public abstract class DocumentationCompiledXmlAttempt
{
    private protected DocumentationCompiledXmlAttempt(
        DocumentationCompiledXmlAttemptKind kind,
        IReadOnlyList<CompiledXmlContribution> contributions)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(contributions);
        Kind = kind;
        Contributions = contributions;
    }

    public DocumentationCompiledXmlAttemptKind Kind { get; }
    public IReadOnlyList<CompiledXmlContribution> Contributions { get; }

    public sealed class Available : DocumentationCompiledXmlAttempt
    {
        internal Available(
            CompiledXmlContribution selected,
            XmlDocumentationEntry documentation,
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Available,
                contributions)
        {
            Selected = selected;
            Documentation = documentation;
        }

        public CompiledXmlContribution Selected { get; }
        public XmlDocumentationEntry Documentation { get; }
    }

    public sealed class Absent : DocumentationCompiledXmlAttempt
    {
        internal Absent(
            CompiledXmlContribution? selected,
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Absent,
                contributions)
        {
            Selected = selected;
        }

        public CompiledXmlContribution? Selected { get; }
    }

    public sealed class Unavailable : DocumentationCompiledXmlAttempt
    {
        internal Unavailable(
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Unavailable,
                contributions)
        {
        }
    }

    public sealed class Ambiguous : DocumentationCompiledXmlAttempt
    {
        internal Ambiguous(
            IReadOnlyList<CompiledXmlContribution> candidates,
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Ambiguous,
                contributions)
        {
            Candidates = candidates;
        }

        public IReadOnlyList<CompiledXmlContribution> Candidates { get; }
    }

    public sealed class Rejected : DocumentationCompiledXmlAttempt
    {
        internal Rejected(
            IReadOnlyList<DocumentationCompiledXmlRejection> rejections,
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Rejected,
                contributions)
        {
            Rejections = rejections;
        }

        public IReadOnlyList<DocumentationCompiledXmlRejection> Rejections
        {
            get;
        }
    }

    public sealed class Failed : DocumentationCompiledXmlAttempt
    {
        internal Failed(
            CompiledXmlContribution selected,
            DocumentationCompiledXmlFailure failure,
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Failed,
                contributions)
        {
            Selected = selected;
            Failure = failure;
        }

        public CompiledXmlContribution Selected { get; }
        public DocumentationCompiledXmlFailure Failure { get; }
    }

    public sealed class Incomplete : DocumentationCompiledXmlAttempt
    {
        internal Incomplete(
            DocumentationIncompleteBoundary boundary,
            CompiledXmlContribution? selected,
            IReadOnlyList<CompiledXmlContribution> contributions)
            : base(
                DocumentationCompiledXmlAttemptKind.Incomplete,
                contributions)
        {
            Boundary = boundary;
            Selected = selected;
        }

        public DocumentationIncompleteBoundary Boundary { get; }
        public CompiledXmlContribution? Selected { get; }
    }
}

public enum DocumentationHouseRejectionKind
{
    LibraryReferenceMismatch,
    ApiContentMismatch,
    LeaseReferenceMismatch,
    AuthoredSourceBindingMismatch,
}

public sealed record DocumentationHouseRejection(
    DocumentationHouseRejectionKind Kind);

public enum DocumentationHouseFailureStage
{
    CompiledXmlSnapshot,
}

public sealed record DocumentationHouseFailure(
    DocumentationHouseFailureStage Stage,
    DocumentationCompiledXmlFailureKind Kind,
    CompiledXmlContribution Selected);

public sealed record DocumentationHouseWorkCharge(
    int ContributionsObserved,
    long CompiledXmlBytesObserved,
    bool ParsedCompiledXml,
    DocumentationAuthoredSourceOperationWorkCharge? AuthoredSourceWork = null);

public enum DocumentationLibraryLeaseConsumer
{
    DocumentationHouse,
    Operation,
    SourceHouse,
}

/// <summary>Resource-free proof of the final Library lease consumer.</summary>
public sealed record DocumentationLibraryLeaseSettlement(
    DocumentationLibraryLeaseConsumer Consumer,
    DocumentationAuthoredLeaseSettlement? AuthoredSourceSettlement = null);

public sealed class DocumentationHouseReceiptIdentity
{
    internal DocumentationHouseReceiptIdentity()
    {
    }

    public override string ToString() =>
        nameof(DocumentationHouseReceiptIdentity);
}

/// <summary>Resource-free completed DocumentationHouse settlement.</summary>
public sealed class DocumentationHouseReceipt
{
    internal DocumentationHouseReceipt(
        DocumentationHouseRequestEvidence request,
        DocumentationCompiledXmlAttempt? compiledXmlAttempt,
        DocumentationAuthoredSourceAttempt? authoredSourceAttempt,
        DocumentationFieldSettlement fields,
        DocumentationHouseWorkCharge work,
        DocumentationLibraryLeaseSettlement leaseSettlement)
    {
        Identity = new DocumentationHouseReceiptIdentity();
        Request = request;
        CompiledXmlAttempt = compiledXmlAttempt;
        AuthoredSourceAttempt = authoredSourceAttempt;
        Fields = fields;
        Work = work;
        LeaseSettlement = leaseSettlement;
    }

    public DocumentationHouseReceiptIdentity Identity { get; }
    public DocumentationHouseRequestEvidence Request { get; }
    public DocumentationCompiledXmlAttempt? CompiledXmlAttempt { get; }
    public DocumentationAuthoredSourceAttempt? AuthoredSourceAttempt { get; }
    public DocumentationFieldSettlement Fields { get; }
    public DocumentationHouseWorkCharge Work { get; }
    public DocumentationLibraryLeaseSettlement LeaseSettlement { get; }
}

/// <summary>Closed result of one DocumentationHouse operation.</summary>
public abstract class DocumentationHouseOutcome
{
    private protected DocumentationHouseOutcome(
        DocumentationHouseRequest request,
        DocumentationHouseWorkCharge work,
        DocumentationLibraryLeaseSettlement leaseSettlement)
    {
        Request = new(request);
        Work = work;
        LeaseSettlement = leaseSettlement;
    }

    public DocumentationHouseRequestEvidence Request { get; }
    public DocumentationHouseWorkCharge Work { get; }
    public DocumentationLibraryLeaseSettlement LeaseSettlement { get; }

    public sealed class Completed : DocumentationHouseOutcome
    {
        internal Completed(
            DocumentationHouseRequest request,
            DocumentationCompiledXmlAttempt? compiledXmlAttempt,
            DocumentationAuthoredSourceAttempt? authoredSourceAttempt,
            DocumentationFieldSettlement fields,
            DocumentationHouseWorkCharge work,
            DocumentationLibraryLeaseSettlement leaseSettlement)
            : base(request, work, leaseSettlement)
        {
            CompiledXmlAttempt = compiledXmlAttempt;
            AuthoredSourceAttempt = authoredSourceAttempt;
            Fields = fields;
            Receipt = new(
                Request,
                compiledXmlAttempt,
                authoredSourceAttempt,
                fields,
                work,
                leaseSettlement);
        }

        public DocumentationCompiledXmlAttempt? CompiledXmlAttempt { get; }
        public DocumentationAuthoredSourceAttempt? AuthoredSourceAttempt
        {
            get;
        }
        public DocumentationFieldSettlement Fields { get; }
        public DocumentationHouseReceipt Receipt { get; }
    }

    public sealed class Rejected : DocumentationHouseOutcome
    {
        internal Rejected(
            DocumentationHouseRequest request,
            DocumentationHouseRejection rejection,
            DocumentationHouseWorkCharge work,
            DocumentationLibraryLeaseSettlement leaseSettlement)
            : base(request, work, leaseSettlement)
        {
            Rejection = rejection;
        }

        public DocumentationHouseRejection Rejection { get; }
    }

    public sealed class Failed : DocumentationHouseOutcome
    {
        internal Failed(
            DocumentationHouseRequest request,
            DocumentationHouseFailure failure,
            DocumentationHouseWorkCharge work,
            DocumentationLibraryLeaseSettlement leaseSettlement)
            : base(request, work, leaseSettlement)
        {
            Failure = failure;
        }

        public DocumentationHouseFailure Failure { get; }
    }

    public sealed class Incomplete : DocumentationHouseOutcome
    {
        internal Incomplete(
            DocumentationHouseRequest request,
            DocumentationIncompleteBoundary boundary,
            DocumentationHouseWorkCharge work,
            DocumentationLibraryLeaseSettlement leaseSettlement)
            : base(request, work, leaseSettlement)
        {
            Boundary = boundary;
        }

        public DocumentationIncompleteBoundary Boundary { get; }
    }
}
