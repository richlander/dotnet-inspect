using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Research;

[Flags]
public enum ResearchChangeMechanism
{
    None = 0,
    Api = 1,
    BodySignals = 2,
    IlBody = 4,
    CSharp = 8,
    ReturnToSender = 16,
    AllAvailable = Api | BodySignals | IlBody | CSharp | ReturnToSender,
}

public enum ResearchSubjectKind
{
    Type,
    Member,
}

public enum ResearchChangeKind
{
    Added,
    Removed,
    Changed,
}

public enum ResearchChangeCategory
{
    Unknown,
    Signature,
    Attribute,
    BodySignal,
    IlBody,
    CSharp,
    RoundTrip,
}

public sealed record ResearchSubjectKey
{
    public ResearchSubjectKey(
        ResearchSubjectKind kind,
        string id,
        string display,
        string? typeName = null,
        string? memberName = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(display);

        Kind = kind;
        Id = id;
        Display = display;
        TypeName = typeName;
        MemberName = memberName;
    }

    public ResearchSubjectKind Kind { get; }
    public string Id { get; }
    public string Display { get; }
    public string? TypeName { get; }
    public string? MemberName { get; }
}

/// <summary>
/// One Research-level change projected from a two-version mechanism. This is not a
/// <see cref="PairFinding{T}"/>: mechanisms that do not yet expose old/new Finding censuses cannot
/// honestly manufacture Finding atoms. The native producer payload remains attached for typed
/// presentation and future producer migration. Common-field-only status and failure rows are valid;
/// any populated native payload must belong to <see cref="Mechanism"/>.
/// </summary>
public sealed record ResearchChange
{
    public ResearchChange(
        ResearchSubjectKey subject,
        ResearchChangeMechanism mechanism,
        FindingDescriptor descriptor,
        ResearchChangeKind kind,
        string? oldValue = null,
        string? newValue = null,
        string? delta = null,
        int? oldIlOffset = null,
        int? newIlOffset = null,
        string? detail = null,
        ResearchChangeCategory category = ResearchChangeCategory.Unknown,
        string? signal = null,
        string? shape = null,
        int? magnitude = null,
        int directionScore = 0,
        bool subjectInBoth = true,
        bool inLoop = false,
        ApiChange? apiChange = null,
        IlDiffRow? ilRow = null,
        IlDiffFailureRow? ilFailureRow = null,
        BodySignalDiffRow? bodySignalRow = null,
        CSharpDiffRow? cSharpRow = null,
        CSharpDiffFailureRow? cSharpFailureRow = null,
        ImmutableArray<IlDiffDisplayRow> ilDisplayRows = default,
        IlDiffDisplayFailureRow? ilDisplayFailureRow = null,
        IlMemberDiffResult? ilMemberDiff = null,
        ImmutableArray<CSharpDiffDisplayRow> cSharpDisplayRows = default,
        CSharpDiffDisplayFailureRow? cSharpDisplayFailureRow = null,
        FindingComparison<AllocationOccurrence>? allocationComparison = null)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        int mechanismValue = (int)mechanism;
        if (mechanism == ResearchChangeMechanism.None
            || (mechanism & ~ResearchChangeMechanism.AllAvailable) != 0
            || (mechanismValue & (mechanismValue - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mechanism),
                "A Research change must have exactly one known mechanism.");
        }
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        ArgumentNullException.ThrowIfNull(descriptor);

        ValidatePayloadMechanism(
            apiChange is not null,
            mechanism,
            ResearchChangeMechanism.Api,
            nameof(apiChange));
        ValidatePayloadMechanism(
            ilRow is not null,
            mechanism,
            ResearchChangeMechanism.IlBody,
            nameof(ilRow));
        ValidatePayloadMechanism(
            ilFailureRow is not null,
            mechanism,
            ResearchChangeMechanism.IlBody,
            nameof(ilFailureRow));
        ValidatePayloadMechanism(
            bodySignalRow is not null,
            mechanism,
            ResearchChangeMechanism.BodySignals,
            nameof(bodySignalRow));
        ValidatePayloadMechanism(
            cSharpRow is not null,
            mechanism,
            ResearchChangeMechanism.CSharp,
            nameof(cSharpRow));
        ValidatePayloadMechanism(
            cSharpFailureRow is not null,
            mechanism,
            ResearchChangeMechanism.CSharp,
            nameof(cSharpFailureRow));
        ValidatePayloadMechanism(
            !ilDisplayRows.IsDefaultOrEmpty,
            mechanism,
            ResearchChangeMechanism.IlBody,
            nameof(ilDisplayRows));
        ValidatePayloadMechanism(
            ilDisplayFailureRow is not null,
            mechanism,
            ResearchChangeMechanism.IlBody,
            nameof(ilDisplayFailureRow));
        ValidatePayloadMechanism(
            ilMemberDiff is not null,
            mechanism,
            ResearchChangeMechanism.IlBody,
            nameof(ilMemberDiff));
        ValidatePayloadMechanism(
            !cSharpDisplayRows.IsDefaultOrEmpty,
            mechanism,
            ResearchChangeMechanism.CSharp,
            nameof(cSharpDisplayRows));
        ValidatePayloadMechanism(
            cSharpDisplayFailureRow is not null,
            mechanism,
            ResearchChangeMechanism.CSharp,
            nameof(cSharpDisplayFailureRow));
        ValidatePayloadMechanism(
            allocationComparison is not null,
            mechanism,
            ResearchChangeMechanism.BodySignals,
            nameof(allocationComparison));

        Mechanism = mechanism;
        Descriptor = descriptor;
        Kind = kind;
        OldValue = oldValue;
        NewValue = newValue;
        Delta = delta;
        OldIlOffset = oldIlOffset;
        NewIlOffset = newIlOffset;
        Detail = detail;
        Category = category;
        Signal = signal;
        Shape = shape;
        Magnitude = magnitude;
        DirectionScore = directionScore;
        SubjectInBoth = subjectInBoth;
        InLoop = inLoop;
        ApiChange = apiChange;
        IlRow = ilRow;
        IlFailureRow = ilFailureRow;
        BodySignalRow = bodySignalRow;
        CSharpRow = cSharpRow;
        CSharpFailureRow = cSharpFailureRow;
        IlDisplayRows = ilDisplayRows.IsDefault ? [] : ilDisplayRows;
        IlDisplayFailureRow = ilDisplayFailureRow;
        IlMemberDiff = ilMemberDiff;
        CSharpDisplayRows = cSharpDisplayRows.IsDefault ? [] : cSharpDisplayRows;
        CSharpDisplayFailureRow = cSharpDisplayFailureRow;
        AllocationComparison = allocationComparison;
    }

    static void ValidatePayloadMechanism(
        bool populated,
        ResearchChangeMechanism actual,
        ResearchChangeMechanism expected,
        string parameterName)
    {
        if (populated && actual != expected)
        {
            throw new ArgumentException(
                $"{parameterName} is valid only for the {expected} mechanism.",
                parameterName);
        }
    }

    public ResearchSubjectKey Subject { get; }
    public ResearchChangeMechanism Mechanism { get; }
    public FindingDescriptor Descriptor { get; }
    public ResearchChangeKind Kind { get; }
    public string? OldValue { get; }
    public string? NewValue { get; }
    public string? Delta { get; }
    public int? OldIlOffset { get; }
    public int? NewIlOffset { get; }
    public string? Detail { get; }
    public ResearchChangeCategory Category { get; }
    public string? Signal { get; }
    public string? Shape { get; }
    public int? Magnitude { get; }
    public int DirectionScore { get; }
    public bool SubjectInBoth { get; }
    public bool InLoop { get; }
    public ApiChange? ApiChange { get; }
    public IlDiffRow? IlRow { get; }
    public IlDiffFailureRow? IlFailureRow { get; }
    public BodySignalDiffRow? BodySignalRow { get; }
    public CSharpDiffRow? CSharpRow { get; }
    public CSharpDiffFailureRow? CSharpFailureRow { get; }
    public ImmutableArray<IlDiffDisplayRow> IlDisplayRows { get; }
    public IlDiffDisplayFailureRow? IlDisplayFailureRow { get; }
    public IlMemberDiffResult? IlMemberDiff { get; }
    public ImmutableArray<CSharpDiffDisplayRow> CSharpDisplayRows { get; }
    public CSharpDiffDisplayFailureRow? CSharpDisplayFailureRow { get; }
    public FindingComparison<AllocationOccurrence>? AllocationComparison { get; }
}

public sealed record ResearchSubjectChanges
{
    public ResearchSubjectChanges(
        ResearchSubjectKey subject,
        ImmutableArray<ResearchChange> changes)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        if (changes.IsDefault)
            throw new ArgumentException("Changes must be initialized.", nameof(changes));
        if (changes.Any(change => change is null))
            throw new ArgumentException("Changes cannot contain null entries.", nameof(changes));

        Changes = changes;
    }

    public ResearchSubjectKey Subject { get; }
    public ImmutableArray<ResearchChange> Changes { get; }

    public bool ApiChanged
        => Changes.Any(change => change.Mechanism == ResearchChangeMechanism.Api);

    public bool ApiSignatureChanged
        => Changes.Any(change =>
            change.Mechanism == ResearchChangeMechanism.Api
            && change.Category == ResearchChangeCategory.Signature);

    public bool ApiAttributeChanged
        => Changes.Any(change =>
            change.Mechanism == ResearchChangeMechanism.Api
            && change.Category == ResearchChangeCategory.Attribute);

    public bool ImplementationChanged
        => Changes.Any(change =>
            change.Mechanism is ResearchChangeMechanism.BodySignals
                or ResearchChangeMechanism.IlBody
                or ResearchChangeMechanism.CSharp);

    public bool HasMechanism(ResearchChangeMechanism mechanism)
        => Changes.Any(change => change.Mechanism == mechanism);

    public bool HasChange(string descriptorId)
        => Changes.Any(change =>
            string.Equals(change.Descriptor.Id, descriptorId, StringComparison.Ordinal));

    public bool HasChangePrefix(string descriptorIdPrefix)
        => Changes.Any(change =>
            change.Descriptor.Id.StartsWith(descriptorIdPrefix, StringComparison.Ordinal));

    public bool HasChangeCategory(ResearchChangeCategory category)
        => Changes.Any(change => change.Category == category);
}

public sealed record AllocationFindingComparison(
    ResearchSubjectKey Subject,
    FindingComparison<AllocationOccurrence> Comparison);

public sealed record CallSiteFindingComparison(
    ResearchSubjectKey Subject,
    FindingComparison<DirectCall> Comparison);

/// <summary>
/// Outcome of one Research diff operation. Changes are stored once; subject grouping is a
/// projection so flat and grouped consumers cannot observe divergent state.
/// </summary>
public sealed record ResearchComparison
{
    public ResearchComparison(
        ImmutableArray<ResearchChange> changes,
        ApiDiff? apiDiff = null,
        ApiFindingComparison? apiComparison = null)
        : this(changes, apiDiff, apiComparison, [], [])
    {
    }

    public ResearchComparison(
        ImmutableArray<ResearchChange> changes,
        ApiDiff? apiDiff,
        ApiFindingComparison? apiComparison,
        ImmutableArray<AllocationFindingComparison> allocationComparisons)
        : this(changes, apiDiff, apiComparison, allocationComparisons, [])
    {
    }

    public ResearchComparison(
        ImmutableArray<ResearchChange> changes,
        ApiDiff? apiDiff,
        ApiFindingComparison? apiComparison,
        ImmutableArray<AllocationFindingComparison> allocationComparisons,
        ImmutableArray<CallSiteFindingComparison> callSiteComparisons)
    {
        if (changes.IsDefault)
            throw new ArgumentException("Changes must be initialized.", nameof(changes));
        if (changes.Any(change => change is null))
            throw new ArgumentException("Changes cannot contain null entries.", nameof(changes));
        if (!allocationComparisons.IsDefault
            && allocationComparisons.Any(comparison => comparison is null))
        {
            throw new ArgumentException(
                "Allocation comparisons cannot contain null entries.",
                nameof(allocationComparisons));
        }
        if (!callSiteComparisons.IsDefault
            && callSiteComparisons.Any(comparison => comparison is null))
        {
            throw new ArgumentException(
                "Call-site comparisons cannot contain null entries.",
                nameof(callSiteComparisons));
        }
        if (apiDiff is not null
            && apiComparison is not null
            && !ReferenceEquals(apiDiff, apiComparison.ApiDiff))
        {
            throw new ArgumentException(
                "The API diff must be the diff carried by the API Finding comparison.",
                nameof(apiDiff));
        }
        Changes = changes;
        ApiComparison = apiComparison;
        ApiDiff = apiComparison?.ApiDiff ?? apiDiff;
        AllocationComparisons = allocationComparisons.IsDefault ? [] : allocationComparisons;
        CallSiteComparisons = callSiteComparisons.IsDefault ? [] : callSiteComparisons;
    }

    public ImmutableArray<ResearchChange> Changes { get; }
    public ApiDiff? ApiDiff { get; }
    public ApiFindingComparison? ApiComparison { get; }
    public ImmutableArray<AllocationFindingComparison> AllocationComparisons { get; }
    public ImmutableArray<CallSiteFindingComparison> CallSiteComparisons { get; }
    public bool IsEmpty => Changes.IsEmpty;

    public IReadOnlyList<ResearchSubjectChanges> BySubject()
        => [.. Changes
            .GroupBy(change => change.Subject, ResearchSubjectKeyIdentityComparer.Instance)
            .OrderBy(group => group.Key.Kind)
            .ThenBy(group => group.Key.Id, StringComparer.Ordinal)
            .Select(group => new ResearchSubjectChanges(
                group.Key,
                [.. group
                    .OrderBy(change => change.Mechanism)
                    .ThenBy(change => change.Descriptor.Id, StringComparer.Ordinal)
                    .ThenBy(change => change.OldIlOffset)
                    .ThenBy(change => change.NewIlOffset)]))];

    public IReadOnlyList<ResearchSubjectChanges> MembersWhere(
        Func<ResearchSubjectChanges, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return [.. BySubject().Where(subject =>
            subject.Subject.Kind == ResearchSubjectKind.Member
            && predicate(subject))];
    }

    sealed class ResearchSubjectKeyIdentityComparer : IEqualityComparer<ResearchSubjectKey>
    {
        public static ResearchSubjectKeyIdentityComparer Instance { get; } = new();

        public bool Equals(ResearchSubjectKey? x, ResearchSubjectKey? y)
            => ReferenceEquals(x, y)
               || (x is not null
                   && y is not null
                   && x.Kind == y.Kind
                   && string.Equals(x.Id, y.Id, StringComparison.Ordinal));

        public int GetHashCode(ResearchSubjectKey obj)
            => HashCode.Combine(obj.Kind, StringComparer.Ordinal.GetHashCode(obj.Id));
    }
}
