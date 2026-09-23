namespace DotnetInspector.DocumentationHouse;

public enum DocumentationAuthoredSourceAttemptKind
{
    Available,
    Absent,
    Unavailable,
    Ambiguous,
    Rejected,
    Failed,
    Incomplete,
}

public enum DocumentationAuthoredSourceUnavailableKind
{
    OperationUnavailable,
    SourceUnavailable,
    DeclarationNotFound,
}

public enum DocumentationAuthoredSourceAmbiguityKind
{
    DeclarationAmbiguous,
}

public enum DocumentationAuthoredSourceAttemptRejectionKind
{
    OperationRejected,
    OperationEvidenceMismatch,
}

public abstract class DocumentationAuthoredSourceAttempt
{
    private protected DocumentationAuthoredSourceAttempt(
        DocumentationAuthoredSourceAttemptKind kind,
        DocumentationAuthoredSourceOperationOutcome? operationOutcome)
    {
        Kind = kind;
        OperationOutcome = operationOutcome;
    }

    public DocumentationAuthoredSourceAttemptKind Kind { get; }
    public DocumentationAuthoredSourceOperationOutcome? OperationOutcome
    {
        get;
    }

    public sealed class Available : DocumentationAuthoredSourceAttempt
    {
        internal Available(
            DocumentationAuthoredSourceOperationOutcome.Produced outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Available,
                outcome) =>
            Contribution = outcome.Contribution;

        public DocumentationAuthoredSourceContribution Contribution { get; }
    }

    public sealed class Absent : DocumentationAuthoredSourceAttempt
    {
        internal Absent(
            DocumentationAuthoredSourceOperationOutcome.Produced outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Absent,
                outcome) =>
            Contribution = outcome.Contribution;

        public DocumentationAuthoredSourceContribution Contribution { get; }
    }

    public sealed class Unavailable : DocumentationAuthoredSourceAttempt
    {
        internal Unavailable(
            DocumentationAuthoredSourceUnavailableKind unavailable,
            DocumentationAuthoredSourceOperationOutcome? outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Unavailable,
                outcome) =>
            UnavailableKind = unavailable;

        public DocumentationAuthoredSourceUnavailableKind UnavailableKind
        {
            get;
        }
    }

    public sealed class Ambiguous : DocumentationAuthoredSourceAttempt
    {
        internal Ambiguous(
            DocumentationAuthoredSourceAmbiguityKind ambiguity,
            DocumentationAuthoredSourceOperationOutcome outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Ambiguous,
                outcome) =>
            Ambiguity = ambiguity;

        public DocumentationAuthoredSourceAmbiguityKind Ambiguity { get; }
    }

    public sealed class Rejected : DocumentationAuthoredSourceAttempt
    {
        internal Rejected(
            DocumentationAuthoredSourceAttemptRejectionKind rejection,
            DocumentationAuthoredSourceOperationOutcome outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Rejected,
                outcome) =>
            Rejection = rejection;

        public DocumentationAuthoredSourceAttemptRejectionKind Rejection
        {
            get;
        }
    }

    public sealed class Failed : DocumentationAuthoredSourceAttempt
    {
        internal Failed(
            DocumentationAuthoredSourceOperationOutcome.Failed outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Failed,
                outcome)
        {
        }
    }

    public sealed class Incomplete : DocumentationAuthoredSourceAttempt
    {
        internal Incomplete(
            DocumentationAuthoredSourceOperationOutcome outcome)
            : base(
                DocumentationAuthoredSourceAttemptKind.Incomplete,
                outcome)
        {
        }
    }
}
