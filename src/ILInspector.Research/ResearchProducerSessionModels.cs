using System.Collections.Immutable;

using ILInspector.Decompiler;
using ILInspector.Instructions;

namespace ILInspector.Research;

/// <summary>The closed set of Research-local implementation producers.</summary>
public enum ResearchProducerKind
{
    CSharp,
    IlBody,
}

/// <summary>The Research-owned declaration of local producer membership.</summary>
public static class ResearchProducerCatalog
{
    /// <summary>Every local producer kind, in normative execution order.</summary>
    public static ImmutableArray<ResearchProducerKind> Kinds { get; } =
    [
        ResearchProducerKind.CSharp,
        ResearchProducerKind.IlBody,
    ];
}

/// <summary>One request to run selected local producers over a target resolution.</summary>
public sealed class ResearchProducerSessionRequest
{
    readonly object _identity = new();

    public ResearchProducerSessionRequest(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution,
        IEnumerable<ResearchProducerKind> producers)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(producers);
        Population = population;
        Resolution = resolution;
        Producers = [.. producers];
        WorkBases =
        [
            .. resolution.Correspondences.Select(
                static outcome => new ResearchProducerWorkBasis.Correspondence(outcome)),
        ];
    }

    public ResearchProducerSessionRequest(
        ResearchAdmittedPopulation population,
        ResearchDesignatedPair pair,
        IEnumerable<ResearchProducerKind> producers)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(producers);
        Population = population;
        Resolution = pair.Resolution;
        Producers = [.. producers];
        WorkBases = [new ResearchProducerWorkBasis.DesignatedPair(pair)];
    }

    public ResearchAdmittedPopulation Population { get; }

    public ResearchTargetResolution Resolution { get; }

    public ImmutableArray<ResearchProducerKind> Producers { get; }

    internal object Identity => _identity;

    internal ImmutableArray<ResearchProducerWorkBasis> WorkBases { get; }
}

/// <summary>Why a producer session request was rejected before execution.</summary>
public enum ResearchProducerRejectionKind
{
    UnsupportedProfile,
    ForeignResolution,
    InvalidIdentityClosure,
    EmptyProducerSelection,
    DuplicateProducerKind,
    UnknownProducerKind,
}

/// <summary>One bounded pre-execution rejection.</summary>
public sealed class ResearchProducerRejection
{
    internal ResearchProducerRejection(
        ResearchProducerRejectionKind kind,
        string summary)
    {
        Kind = kind;
        Summary = summary;
    }

    public ResearchProducerRejectionKind Kind { get; }

    public string Summary { get; }
}

/// <summary>An opaque Research identity for one local producer session.</summary>
public sealed class ResearchProducerSessionId
{
    readonly object _request;

    internal ResearchProducerSessionId(
        ResearchComparisonOperationId operation,
        object request)
    {
        Operation = operation;
        _request = request;
    }

    public ResearchComparisonOperationId Operation { get; }

    internal bool BelongsTo(object request) => ReferenceEquals(_request, request);
}

/// <summary>An opaque Research identity for one producer work item.</summary>
public sealed class ResearchProducerWorkItemId
{
    internal ResearchProducerWorkItemId(ResearchProducerSessionId session)
        => Session = session;

    public ResearchProducerSessionId Session { get; }

    public ResearchComparisonOperationId Operation => Session.Operation;
}

/// <summary>The exact owner-issued evidence that authorizes a local comparison.</summary>
public abstract class ResearchProducerWorkBasis
{
    private protected ResearchProducerWorkBasis()
    {
    }

    public sealed class Correspondence : ResearchProducerWorkBasis
    {
        internal Correspondence(ResearchTargetCorrespondenceOutcome outcome)
            => Outcome = outcome;

        public ResearchTargetCorrespondenceOutcome Outcome { get; }
    }

    public sealed class DesignatedPair : ResearchProducerWorkBasis
    {
        internal DesignatedPair(ResearchDesignatedPair pair) => Pair = pair;

        public ResearchDesignatedPair Pair { get; }
    }
}

/// <summary>One exact comparison-and-producer unit of sequential work.</summary>
public sealed class ResearchProducerWorkItem
{
    internal ResearchProducerWorkItem(
        ResearchProducerWorkItemId id,
        ResearchProducerWorkBasis basis,
        ResearchProducerKind producer)
    {
        Id = id;
        Basis = basis;
        Producer = producer;
    }

    public ResearchProducerWorkItemId Id { get; }

    public ResearchProducerWorkBasis Basis { get; }

    public ResearchProducerKind Producer { get; }
}

/// <summary>Why Research could not invoke a producer for one exact item.</summary>
public enum ResearchProducerUnavailableKind
{
    CorrespondenceUnavailable,
    InputUnreadable,
    AssemblyIdentityMismatch,
    ModuleIdentityMismatch,
    EndpointAddressUnavailable,
}

/// <summary>Bounded Research-local unavailability for one work item.</summary>
public sealed class ResearchProducerUnavailable
{
    internal ResearchProducerUnavailable(
        ResearchProducerUnavailableKind kind,
        ResearchComparisonInputId? input,
        string summary)
    {
        Kind = kind;
        Input = input;
        Summary = summary;
    }

    public ResearchProducerUnavailableKind Kind { get; }

    public ResearchComparisonInputId? Input { get; }

    public string Summary { get; }
}

/// <summary>Why an executing producer session failed after admission.</summary>
public enum ResearchProducerDiagnosticKind
{
    ProducerException,
    ProducerContractViolation,
    ResearchExecutionFailed,
    CleanupFailed,
    CompletionValidationFailed,
}

/// <summary>One bounded Research-owned producer-session failure.</summary>
public sealed class ResearchProducerDiagnostic
{
    internal ResearchProducerDiagnostic(
        ResearchProducerDiagnosticKind kind,
        ResearchProducerKind? producer = null)
    {
        Kind = kind;
        Producer = producer;
        Summary = kind switch
        {
            ResearchProducerDiagnosticKind.ProducerException =>
                "A local producer escaped with an exception.",
            ResearchProducerDiagnosticKind.ProducerContractViolation =>
                "A local producer result violated its endpoint contract.",
            ResearchProducerDiagnosticKind.ResearchExecutionFailed =>
                "Research could not finish the local producer session.",
            ResearchProducerDiagnosticKind.CleanupFailed =>
                "One or more Research-owned input stages could not be closed.",
            ResearchProducerDiagnosticKind.CompletionValidationFailed =>
                "The Research producer completion failed final validation.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public ResearchProducerDiagnosticKind Kind { get; }

    public ResearchProducerKind? Producer { get; }

    public string Summary { get; }
}

/// <summary>The closed outcome of one cataloged work item.</summary>
public abstract class ResearchProducerWorkOutcome
{
    private protected ResearchProducerWorkOutcome()
    {
    }

    public sealed class ProducedCSharp : ResearchProducerWorkOutcome
    {
        internal ProducedCSharp(CSharpMemberEndpointComparison result)
            => Result = result;

        public CSharpMemberEndpointComparison Result { get; }
    }

    public sealed class ProducedIlBody : ResearchProducerWorkOutcome
    {
        internal ProducedIlBody(IlMemberEndpointComparison result)
            => Result = result;

        public IlMemberEndpointComparison Result { get; }
    }

    public sealed class Unavailable : ResearchProducerWorkOutcome
    {
        internal Unavailable(ResearchProducerUnavailable reason)
            => Reason = reason;

        public ResearchProducerUnavailable Reason { get; }
    }

    public sealed class Failed : ResearchProducerWorkOutcome
    {
        internal Failed(ResearchProducerDiagnostic diagnostic)
            => Diagnostic = diagnostic;

        public ResearchProducerDiagnostic Diagnostic { get; }
    }
}

/// <summary>One work item associated with its exact terminal outcome.</summary>
public sealed class ResearchProducerWorkResult
{
    internal ResearchProducerWorkResult(
        ResearchProducerWorkItem item,
        ResearchProducerWorkOutcome outcome)
    {
        Item = item;
        Outcome = outcome;
    }

    public ResearchProducerWorkItem Item { get; }

    public ResearchProducerWorkOutcome Outcome { get; }
}

/// <summary>The cleanup outcome for one completely acquired input stage.</summary>
public abstract class ResearchProducerCleanupOutcome
{
    private protected ResearchProducerCleanupOutcome(
        ResearchComparisonInputId input)
        => Input = input;

    public ResearchComparisonInputId Input { get; }

    public sealed class Succeeded : ResearchProducerCleanupOutcome
    {
        internal Succeeded(ResearchComparisonInputId input)
            : base(input)
        {
        }
    }

    public sealed class Failed : ResearchProducerCleanupOutcome
    {
        internal Failed(
            ResearchComparisonInputId input,
            ResearchProducerDiagnostic diagnostic)
            : base(input)
            => Diagnostic = diagnostic;

        public ResearchProducerDiagnostic Diagnostic { get; }
    }
}

/// <summary>
/// One atomically published Research-local producer completion.
/// </summary>
public sealed class ResearchProducerCompletion
{
    internal ResearchProducerCompletion(
        ResearchComparisonOperationId operation,
        ResearchProducerSessionId session,
        ImmutableArray<ResearchProducerWorkItem> workItems,
        ImmutableArray<ResearchProducerWorkResult> results,
        ImmutableArray<ResearchProducerCleanupOutcome> cleanup)
    {
        Operation = operation;
        Session = session;
        WorkItems = workItems;
        Results = results;
        Cleanup = cleanup;
    }

    public ResearchComparisonOperationId Operation { get; }

    public ResearchProducerSessionId Session { get; }

    public ImmutableArray<ResearchProducerWorkItem> WorkItems { get; }

    public ImmutableArray<ResearchProducerWorkResult> Results { get; }

    public ImmutableArray<ResearchProducerCleanupOutcome> Cleanup { get; }
}

/// <summary>The closed terminal result of one producer-session request.</summary>
public abstract class ResearchProducerSessionOutcome
{
    private protected ResearchProducerSessionOutcome()
    {
    }

    public sealed class Rejected : ResearchProducerSessionOutcome
    {
        internal Rejected(ResearchProducerRejection rejection)
            => Rejection = rejection;

        public ResearchProducerRejection Rejection { get; }
    }

    public sealed class Completed : ResearchProducerSessionOutcome
    {
        internal Completed(ResearchProducerCompletion completion)
            => Completion = completion;

        public ResearchProducerCompletion Completion { get; }
    }

    public sealed class Failed : ResearchProducerSessionOutcome
    {
        internal Failed(
            ResearchProducerDiagnostic diagnostic,
            ImmutableArray<ResearchProducerCleanupOutcome> cleanup)
        {
            Diagnostic = diagnostic;
            Cleanup = cleanup;
        }

        public ResearchProducerDiagnostic Diagnostic { get; }

        public ImmutableArray<ResearchProducerCleanupOutcome> Cleanup { get; }
    }

    public sealed class Cancelled : ResearchProducerSessionOutcome
    {
        internal Cancelled(
            ImmutableArray<ResearchProducerCleanupOutcome> cleanup)
            => Cleanup = cleanup;

        public ImmutableArray<ResearchProducerCleanupOutcome> Cleanup { get; }
    }
}
