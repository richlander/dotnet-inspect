using System.Collections.Immutable;
using Inspector.Findings;

namespace DotnetInspector.Queries;

/// <summary>
/// The closed vocabulary of operations an analysis can take part in. An
/// operation kind enters it only with its first adopter.
/// </summary>
public enum AnalysisOperationKind
{
    Compare,
}

/// <summary>
/// One report surface of one operation participation: the ordered Finding
/// descriptors the analysis issues there and the one producer route the
/// operation dispatches for that surface.
/// </summary>
public sealed class AnalysisSurfaceParticipation
{
    public AnalysisSurfaceParticipation(
        AnalysisReportSurfaceKind surface,
        IEnumerable<FindingDescriptor> descriptors,
        AnalysisDeclarationId producerRoute)
    {
        if (!Enum.IsDefined(surface))
            throw new ArgumentOutOfRangeException(nameof(surface));
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(producerRoute);
        Descriptors = [.. descriptors];
        if (Descriptors.IsEmpty)
        {
            throw new ArgumentException(
                "A participating surface issues at least one Finding descriptor.",
                nameof(descriptors));
        }
        if (Descriptors.Any(descriptor => descriptor is null))
            throw new ArgumentException("The collection cannot contain null.", nameof(descriptors));
        if (Descriptors.Select(descriptor => descriptor.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != Descriptors.Length)
        {
            throw new ArgumentException(
                "Finding descriptors must be unique within one surface.",
                nameof(descriptors));
        }
        Surface = surface;
        ProducerRoute = producerRoute;
    }

    public AnalysisReportSurfaceKind Surface { get; }

    /// <summary>Finding descriptors in declaration order.</summary>
    public ImmutableArray<FindingDescriptor> Descriptors { get; }

    /// <summary>The producer route (not its delivery) dispatched at this surface.</summary>
    public AnalysisDeclarationId ProducerRoute { get; }
}

/// <summary>One owner-issued operation participation declaration.</summary>
public sealed class AnalysisOperationParticipation
{
    public AnalysisOperationParticipation(
        AnalysisOperationKind operation,
        IEnumerable<AnalysisSurfaceParticipation> surfaces)
    {
        if (!Enum.IsDefined(operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        ArgumentNullException.ThrowIfNull(surfaces);
        Surfaces = [.. surfaces];
        if (Surfaces.IsEmpty)
        {
            throw new ArgumentException(
                "A participation declares at least one report surface.",
                nameof(surfaces));
        }
        if (Surfaces.Any(surface => surface is null))
            throw new ArgumentException("The collection cannot contain null.", nameof(surfaces));
        if (Surfaces.Select(surface => surface.Surface).Distinct().Count()
            != Surfaces.Length)
        {
            throw new ArgumentException(
                "A report surface may be declared only once per participation.",
                nameof(surfaces));
        }
        Operation = operation;
    }

    public AnalysisOperationKind Operation { get; }

    public ImmutableArray<AnalysisSurfaceParticipation> Surfaces { get; }

    public AnalysisSurfaceParticipation? For(AnalysisReportSurfaceKind surface)
        => Surfaces.FirstOrDefault(candidate => candidate.Surface == surface);
}

/// <summary>
/// An operation's own selection context: its kind and its operation-owned
/// default analysis set, selected when a request omits the set.
/// </summary>
public sealed class AnalysisOperationDefinition
{
    public AnalysisOperationDefinition(
        AnalysisOperationKind kind,
        IEnumerable<string> defaultSet)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(defaultSet);
        DefaultSet = [.. defaultSet];
        if (DefaultSet.IsEmpty)
        {
            throw new ArgumentException(
                "An operation default set is non-empty.",
                nameof(defaultSet));
        }
        Kind = kind;
    }

    public AnalysisOperationKind Kind { get; }

    public ImmutableArray<string> DefaultSet { get; }
}

/// <summary>Why one analysis-set entry, or the set itself, was rejected.</summary>
public enum AnalysisSetRejectionReason
{
    /// <summary>The entry names no configured analysis descriptor.</summary>
    Unknown,

    /// <summary>The descriptor declares no participation for the operation.</summary>
    NotParticipating,

    /// <summary>The entry repeats an earlier entry.</summary>
    Duplicate,

    /// <summary>The entry is empty, or an explicit set has no entries.</summary>
    Empty,
}

/// <summary>
/// One typed analysis-set rejection. Exactly one of <see cref="SetReason"/>
/// and <see cref="RequestReason"/> is present; surface and target-role
/// mismatches reuse the existing request rejection reasons.
/// </summary>
public sealed class AnalysisSetEntryRejection
{
    internal AnalysisSetEntryRejection(
        int? position,
        string? requestedIdentity,
        AnalysisSetRejectionReason? setReason,
        AnalysisRequestRejectionReason? requestReason,
        AnalysisDescriptor? analysis,
        IEnumerable<AnalysisTargetRoleDescriptor>? targetRoles = null)
    {
        Position = position;
        RequestedIdentity = requestedIdentity;
        SetReason = setReason;
        RequestReason = requestReason;
        Analysis = analysis;
        TargetRoles = targetRoles is null ? [] : [.. targetRoles];
    }

    /// <summary>The zero-based entry position, or null for a whole-set rejection.</summary>
    public int? Position { get; }

    /// <summary>The exact requested text, never case-folded.</summary>
    public string? RequestedIdentity { get; }

    public AnalysisSetRejectionReason? SetReason { get; }

    public AnalysisRequestRejectionReason? RequestReason { get; }

    public AnalysisDescriptor? Analysis { get; }

    public ImmutableArray<AnalysisTargetRoleDescriptor> TargetRoles { get; }
}

/// <summary>The closed result of whole-set analysis validation.</summary>
public abstract class AnalysisSetValidationResult
{
    private AnalysisSetValidationResult()
    {
    }

    public sealed class Accepted : AnalysisSetValidationResult
    {
        internal Accepted(
            AnalysisOperationKind operation,
            AnalysisReportSurfaceKind surface,
            ImmutableArray<AnalysisDescriptor> analyses,
            bool isOperationDefault)
        {
            Operation = operation;
            Surface = surface;
            Analyses = analyses;
            IsOperationDefault = isOperationDefault;
        }

        public AnalysisOperationKind Operation { get; }

        public AnalysisReportSurfaceKind Surface { get; }

        /// <summary>The selected analyses, in selection order.</summary>
        public ImmutableArray<AnalysisDescriptor> Analyses { get; }

        /// <summary>True when the request omitted the set.</summary>
        public bool IsOperationDefault { get; }
    }

    public sealed class Rejected : AnalysisSetValidationResult
    {
        internal Rejected(ImmutableArray<AnalysisSetEntryRejection> rejections)
        {
            Rejections = rejections;
        }

        /// <summary>One rejection per offending entry, in entry order.</summary>
        public ImmutableArray<AnalysisSetEntryRejection> Rejections { get; }
    }
}

/// <summary>The analysis-identity grammar shared by every operation.</summary>
public static class AnalysisIdentity
{
    /// <summary>
    /// True when <paramref name="value"/> is one or more lowercase ASCII
    /// words joined by <c>-</c> and no word is <c>analysis</c>.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        foreach (string word in value.Split('-'))
        {
            if (word.Length == 0
                || word.Any(character => character is < 'a' or > 'z')
                || word == "analysis")
            {
                return false;
            }
        }
        return true;
    }
}
