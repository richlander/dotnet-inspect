using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// One normalized Member pattern with its input ordinal and visible spelling.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MemberFindPattern.Direct), "direct")]
[JsonDerivedType(typeof(MemberFindPattern.Glob), "glob")]
public abstract record MemberFindPattern
{
    private protected MemberFindPattern(int ordinal, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Ordinal = ordinal;
        Text = text;
    }

    public int Ordinal { get; }
    public string Text { get; }

    public static MemberFindPattern Create(int ordinal, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return text.Contains('*') || text.Contains('?')
            ? new Glob(ordinal, text)
            : new Direct(ordinal, text);
    }

    public sealed record Direct : MemberFindPattern
    {
        public Direct(int ordinal, string text)
            : base(ordinal, text)
        {
        }
    }

    public sealed record Glob : MemberFindPattern
    {
        public Glob(int ordinal, string text)
            : base(ordinal, text)
        {
        }
    }
}

/// <summary>A normalized declaring-Type predicate for Member Find.</summary>
public sealed record MemberFindDeclaringTypeFilter
{
    [JsonConstructor]
    public MemberFindDeclaringTypeFilter(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
    }

    public string Text { get; }

    internal bool Matches(MetadataTypeDefinitionName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TypeMatcher.MatchesTypeFilter(
            name.ToMetadataFullName(),
            Text);
    }
}

/// <summary>
/// Accepted Member positions executed by the source evaluator.
/// </summary>
public sealed record MemberFindAcceptedRows
{
    [JsonConstructor]
    public MemberFindAcceptedRows(
        int start,
        int? end,
        bool materializeRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(start);
        if (end is int finiteEnd && finiteEnd < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                finiteEnd,
                "The accepted-row end must not precede its start.");
        }
        if (materializeRows && end is null)
        {
            throw new ArgumentException(
                "A row-materializing Member evaluation requires a finite end.",
                nameof(end));
        }

        Start = start;
        End = end;
        MaterializeRows = materializeRows;
    }

    public int Start { get; }
    public int? End { get; }
    public bool MaterializeRows { get; }
}

/// <summary>An ordered plural Member Find question.</summary>
public sealed record MemberFindQuestion : FindQuestion
{
    [JsonConstructor]
    public MemberFindQuestion(
        ImmutableArray<MemberFindPattern> patterns,
        FindVisibility visibility,
        MemberFindDeclaringTypeFilter? declaringTypeFilter,
        int? maximumMatches,
        MemberFindAcceptedRows? acceptedRows = null)
    {
        Validate(
            patterns,
            visibility,
            maximumMatches,
            acceptedRows);
        Patterns = patterns;
        Visibility = visibility;
        DeclaringTypeFilter = declaringTypeFilter;
        MaximumMatches = maximumMatches;
        AcceptedRows = acceptedRows;
    }

    public ImmutableArray<MemberFindPattern> Patterns { get; }
    public FindVisibility Visibility { get; }
    public MemberFindDeclaringTypeFilter? DeclaringTypeFilter { get; }
    public int? MaximumMatches { get; }
    public MemberFindAcceptedRows? AcceptedRows { get; }

    public static MemberFindQuestion Create(
        IEnumerable<string> patterns,
        FindVisibility visibility,
        string? declaringTypeFilter = null,
        int? maximumMatches = null,
        MemberFindAcceptedRows? acceptedRows = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return Create(
            patterns.Select(
                static (pattern, ordinal) =>
                    MemberFindPattern.Create(ordinal, pattern)),
            visibility,
            declaringTypeFilter is null
                ? null
                : new MemberFindDeclaringTypeFilter(
                    declaringTypeFilter),
            maximumMatches,
            acceptedRows);
    }

    public static MemberFindQuestion Create(
        IEnumerable<MemberFindPattern> patterns,
        FindVisibility visibility,
        MemberFindDeclaringTypeFilter? declaringTypeFilter = null,
        int? maximumMatches = null,
        MemberFindAcceptedRows? acceptedRows = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return new(
            [.. patterns],
            visibility,
            declaringTypeFilter,
            maximumMatches,
            acceptedRows);
    }

    private static void Validate(
        ImmutableArray<MemberFindPattern> patterns,
        FindVisibility visibility,
        int? maximumMatches,
        MemberFindAcceptedRows? acceptedRows)
    {
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));
        if (maximumMatches is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMatches),
                "A Member Find match limit must be positive.");
        }
        if (acceptedRows is not null
            && maximumMatches is not null
            && acceptedRows.End != maximumMatches)
        {
            throw new ArgumentException(
                "A bounded accepted-row request must share the question's "
                    + "maximum match position.",
                nameof(acceptedRows));
        }
        if (patterns.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A Member Find question requires at least one pattern.",
                nameof(patterns));
        }

        for (int index = 0; index < patterns.Length; index++)
        {
            MemberFindPattern pattern =
                patterns[index]
                ?? throw new ArgumentException(
                    "A Member Find pattern cannot be null.",
                    nameof(patterns));
            if (pattern.Ordinal != index)
            {
                throw new ArgumentException(
                    "Member Find pattern ordinals must be contiguous and ordered.",
                    nameof(patterns));
            }
        }
    }
}

/// <summary>The established classification for one Member match.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MemberFindSemanticMatchKind>))]
public enum MemberFindSemanticMatchKind
{
    Direct,
    Glob,
}

/// <summary>Exact source-backed identity of one Member declaration.</summary>
public sealed record MemberFindDeclarationAssociation
{
    public MemberFindDeclarationAssociation(
        FindSourceIdentity source,
        MetadataTypeDefinitionName declaringType,
        MemberAnchor member,
        int declarationOrder,
        int memberOrder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentOutOfRangeException.ThrowIfNegative(declarationOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(memberOrder);
        Source = source;
        DeclaringType = declaringType;
        Member = member;
        DeclarationOrder = declarationOrder;
        MemberOrder = memberOrder;
    }

    public FindSourceIdentity Source { get; }
    public MetadataTypeDefinitionName DeclaringType { get; }
    public MemberAnchor Member { get; }
    public int DeclarationOrder { get; }
    public int MemberOrder { get; }
}

/// <summary>One independently truthful semantic Member match.</summary>
public sealed record MemberFindSemanticMatch(
    MemberFindPattern Pattern,
    MemberFindSemanticMatchKind Match,
    MemberFindDeclarationAssociation Declaration,
    string MemberName,
    string DeclaringType,
    string? DeclaringNamespace,
    string Kind,
    string? Signature,
    string? ReturnType);

/// <summary>Evaluation coverage established for one exact Member source.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MemberFindSourceCoverageKind>))]
public enum MemberFindSourceCoverageKind
{
    Complete,
    InspectionFailures,
    MatchLimitReached,
    AccessRejected,
    Unavailable,
}

/// <summary>Detached coverage and failure evidence for one exact source.</summary>
public sealed record MemberFindSourceCoverage(
    FindSourceIdentity Source,
    MemberFindSourceCoverageKind Kind,
    string? Detail,
    ImmutableArray<ApiSurfaceInspectionFailure> InspectionFailures,
    ImmutableArray<int> CompletedPatternOrdinals)
{
    public ImmutableArray<int> MatchedPatternOrdinals { get; init; } = [];

    public bool IsComplete => Kind is MemberFindSourceCoverageKind.Complete;

    public bool IsPatternComplete(int ordinal) =>
        InspectionFailures.IsEmpty
        && CompletedPatternOrdinals.Contains(ordinal);
}

/// <summary>One Member source outcome: available, rejected, or failed.</summary>
public abstract class MemberFindSourceEvaluation
    : FindSourceEvaluation
{
    private protected MemberFindSourceEvaluation(
        MemberFindQuestion question,
        FindSourceIdentity source,
        MemberFindSourceCoverage coverage)
        : base(question)
    {
        Source = source;
        Coverage = coverage;
    }

    public new MemberFindQuestion Question =>
        (MemberFindQuestion)base.Question;
    public FindSourceIdentity Source { get; }
    public MemberFindSourceCoverage Coverage { get; }
    public abstract ImmutableArray<MemberFindSemanticMatch> Matches { get; }
    public abstract int AcceptedCount { get; }
    public bool IsComplete => Coverage.IsComplete;

    public sealed class Available : MemberFindSourceEvaluation
    {
        internal Available(
            MemberFindQuestion question,
            FindSourceIdentity source,
            ImmutableArray<MemberFindSemanticMatch> matches,
            int acceptedCount,
            MemberFindSourceCoverage coverage)
            : base(question, source, coverage)
        {
            Matches = matches;
            AcceptedCount = acceptedCount;
        }

        public override ImmutableArray<MemberFindSemanticMatch> Matches
        {
            get;
        }

        public override int AcceptedCount { get; }
    }

    public sealed class Rejected : MemberFindSourceEvaluation
    {
        internal Rejected(
            MemberFindQuestion question,
            FindSourceIdentity source,
            MemberFindSourceCoverage coverage)
            : base(question, source, coverage)
        {
        }

        public override ImmutableArray<MemberFindSemanticMatch> Matches => [];
        public override int AcceptedCount => 0;
    }

    public sealed class Failed : MemberFindSourceEvaluation
    {
        internal Failed(
            MemberFindQuestion question,
            FindSourceIdentity source,
            MemberFindSourceCoverage coverage)
            : base(question, source, coverage)
        {
        }

        public override ImmutableArray<MemberFindSemanticMatch> Matches => [];
        public override int AcceptedCount => 0;
    }
}

/// <summary>
/// An ordered array of Member source outcomes plus its owner's coverage
/// statement.
/// </summary>
public sealed class MemberFindSemanticPopulation
{
    private MemberFindSemanticPopulation(
        MemberFindQuestion question,
        ImmutableArray<MemberFindSourceEvaluation> sources,
        ImmutableArray<FindPopulationGap> gaps,
        bool ownerReportsComplete)
    {
        Question = question;
        Sources = sources;
        Gaps = gaps;
        OwnerReportsComplete = ownerReportsComplete;
        IsComplete =
            ownerReportsComplete
            && gaps.IsEmpty
            && sources.All(static source => source.IsComplete);
    }

    public MemberFindQuestion Question { get; }
    public ImmutableArray<MemberFindSourceEvaluation> Sources { get; }
    public ImmutableArray<FindPopulationGap> Gaps { get; }
    public bool OwnerReportsComplete { get; }
    public bool IsComplete { get; }

    public static MemberFindSemanticPopulation Create(
        MemberFindQuestion question,
        IEnumerable<MemberFindSourceEvaluation> sources,
        IEnumerable<FindPopulationGap>? gaps = null,
        bool ownerReportsComplete = true)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(sources);
        ImmutableArray<MemberFindSourceEvaluation> sourceArray = [.. sources];
        if (sourceArray.Any(
                source => !ReferenceEquals(source.Question, question)))
        {
            throw new ArgumentException(
                "Every source evaluation must retain the exact population question.",
                nameof(sources));
        }

        if (sourceArray
            .Select(static source => source.Source)
            .Distinct()
            .Count()
            != sourceArray.Length)
        {
            throw new ArgumentException(
                "A Member Find population cannot repeat a source identity.",
                nameof(sources));
        }

        return new(
            question,
            sourceArray,
            gaps is null ? [] : [.. gaps],
            ownerReportsComplete);
    }
}

/// <summary>Typed settlement evidence for one normalized Member pattern.</summary>
public sealed record MemberFindPatternSettlement(
    MemberFindPattern Pattern,
    FindPatternSettlementKind Kind);

/// <summary>
/// Portable completed Member Find content with separate coverage and match
/// completion.
/// </summary>
public sealed class MemberFindBlock
{
    [JsonConstructor]
    public MemberFindBlock(
        MemberFindQuestion question,
        ImmutableArray<MemberFindSemanticMatch> matches,
        ImmutableArray<MemberFindPatternSettlement> settlements,
        ImmutableArray<MemberFindSourceCoverage> sourceCoverage,
        ImmutableArray<FindPopulationGap> populationGaps,
        bool isSourceCoverageComplete,
        FindMatchCompletion matchCompletion,
        int acceptedCount = 0)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (matches.IsDefault
            || settlements.IsDefault
            || sourceCoverage.IsDefault
            || populationGaps.IsDefault)
        {
            throw new ArgumentException(
                "Member Find block arrays must be initialized.");
        }
        Question = question;
        Matches = matches;
        Settlements = settlements;
        SourceCoverage = sourceCoverage;
        PopulationGaps = populationGaps;
        IsSourceCoverageComplete = isSourceCoverageComplete;
        MatchCompletion = matchCompletion;
        AcceptedCount = acceptedCount;
    }

    public MemberFindQuestion Question { get; }
    public ImmutableArray<MemberFindSemanticMatch> Matches { get; }
    public ImmutableArray<MemberFindPatternSettlement> Settlements { get; }
    public ImmutableArray<MemberFindSourceCoverage> SourceCoverage { get; }
    public ImmutableArray<FindPopulationGap> PopulationGaps { get; }
    public bool IsSourceCoverageComplete { get; }
    public FindMatchCompletion MatchCompletion { get; }
    public int AcceptedCount { get; }
}

/// <summary>
/// Evaluates exact assembly-context sources under one normalized Member
/// question.
/// </summary>
public static class MemberFindSourceEvaluator
{
    public static MemberFindSemanticPopulation EvaluateAssemblyContext(
        MemberFindQuestion question,
        AssemblyContextGroup group,
        Func<
            AssemblyContextSubject,
            int,
            FindSourceIdentity> sourceFor)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(sourceFor);

        EvaluationState[] states =
        [
            .. group.Participants.Select(
                (participant, memberOrder) =>
                {
                    var subject =
                        new AssemblyContextSubject(
                            participant.Assembly);
                    FindSourceIdentity source =
                        sourceFor(subject, memberOrder);
                    ValidateSource(subject, source);
                    return new EvaluationState(
                        participant,
                        subject,
                        source);
                }),
        ];

        int? remaining = question.MaximumMatches;
        if (question.AcceptedRows is { } acceptedRows)
        {
            int acceptedCount = 0;
            foreach (MemberFindPattern pattern in question.Patterns)
            {
                foreach (EvaluationState state in states)
                {
                    if (acceptedRows.End is int participantEnd
                        && acceptedCount >= participantEnd)
                    {
                        break;
                    }
                    if (state.IsTerminal)
                        continue;

                    int localStart =
                        Math.Max(
                            1,
                            acceptedRows.Start
                                - acceptedCount);
                    int? localEnd =
                        acceptedRows.End is int finiteEnd
                            ? finiteEnd - acceptedCount
                            : null;
                    int added = EvaluateSelectivePattern(
                        question,
                        group,
                        state,
                        pattern,
                        new(
                            localStart,
                            localEnd,
                            acceptedRows.MaterializeRows));
                    acceptedCount =
                        checked(acceptedCount + added);
                }
                if (acceptedRows.End is int patternEnd
                    && acceptedCount >= patternEnd)
                {
                    break;
                }
            }
            remaining =
                acceptedRows.End is int terminalEnd
                    ? Math.Max(0, terminalEnd - acceptedCount)
                    : null;
        }
        else if (remaining is null)
        {
            foreach (EvaluationState state in states)
            {
                EvaluatePatterns(
                    question,
                    group,
                    state,
                    question.Patterns,
                    limit: null);
            }
        }
        else
        {
            foreach (MemberFindPattern pattern in question.Patterns)
            {
                foreach (EvaluationState state in states)
                {
                    if (remaining == 0)
                        break;
                    if (state.IsTerminal)
                        continue;

                    int added = EvaluatePatterns(
                        question,
                        group,
                        state,
                        [pattern],
                        remaining);
                    remaining -= added;
                }
                if (remaining == 0)
                    break;
            }
        }

        bool limitReached = remaining == 0;
        var sources =
            ImmutableArray.CreateBuilder<MemberFindSourceEvaluation>();
        var gaps = ImmutableArray.CreateBuilder<FindPopulationGap>();
        foreach (EvaluationState state in states)
        {
            if (!state.WasAttempted)
            {
                gaps.Add(
                    new(
                        state.Source.ContextOrder,
                        state.Source.MemberOrder,
                        nameof(
                            MemberFindSourceCoverageKind
                                .MatchLimitReached),
                        "The source was not evaluated after the Member match limit was reached."));
                continue;
            }

            sources.Add(
                state.ToEvaluation(
                    question,
                    limitReached));
        }

        return MemberFindSemanticPopulation.Create(
            question,
            sources,
            gaps,
            ownerReportsComplete: true);
    }

    private static int EvaluateSelectivePattern(
        MemberFindQuestion question,
        AssemblyContextGroup group,
        EvaluationState state,
        MemberFindPattern pattern,
        MemberSearchWindow window)
    {
        AssemblyContextEntry<MemberSearchWindowResult> entry =
            AssemblyContextQueryExecutor.ExecuteParticipant(
                group,
                state.Participant,
                session => session.SearchMembers(
                    state.Subject.Identity.Name,
                    [pattern.Text],
                    question.Visibility is FindVisibility.All,
                    window,
                    question.DeclaringTypeFilter is { } filter
                        ? filter.Matches
                        : null));
        state.WasAttempted = true;

        switch (entry)
        {
            case AssemblyContextEntry<
                MemberSearchWindowResult>.Rejected rejected:
                state.Rejection = rejected.Failure;
                return 0;
            case AssemblyContextEntry<
                MemberSearchWindowResult>.Failed failed:
                state.Failure = failed.Error;
                return 0;
            case AssemblyContextEntry<
                MemberSearchWindowResult>.Available available:
                MemberSearchWindowResult result =
                    available.Value;
                state.AddInspectionFailures(
                    result.InspectionFailures);
                ImmutableArray<MemberFindSemanticMatch> matches =
                    ConvertMatches(
                        question,
                        state.Source,
                        [pattern],
                        result.Results);
                state.AddMatches(matches);
                state.AddAcceptedMatches(
                    pattern,
                    result.AcceptedCount);
                if (result.EndReached)
                {
                    state.HitLimit = true;
                }
                else
                {
                    state.AddCompletedPatterns([pattern]);
                }
                return result.AcceptedCount;
            default:
                throw new InvalidOperationException(
                    "Unknown assembly-context Member outcome.");
        }
    }

    private static int EvaluatePatterns(
        MemberFindQuestion question,
        AssemblyContextGroup group,
        EvaluationState state,
        ImmutableArray<MemberFindPattern> patterns,
        int? limit)
    {
        IReadOnlyList<string> producerPatterns =
        [
            .. patterns.Select(static pattern => pattern.Text),
        ];
        int? producerLimit =
            question.DeclaringTypeFilter is null
                ? limit
                : null;
        AssemblyContextEntry<AssemblyMemberMatches> entry =
            AssemblyContextQueryExecutor.ExecuteParticipant(
                group,
                state.Participant,
                session => Inspect(
                    state.Subject.Identity.Name,
                    session,
                    producerPatterns,
                    question.Visibility,
                    producerLimit));
        state.WasAttempted = true;

        switch (entry)
        {
            case AssemblyContextEntry<
                AssemblyMemberMatches>.Rejected rejected:
                state.Rejection = rejected.Failure;
                return 0;
            case AssemblyContextEntry<
                AssemblyMemberMatches>.Failed failed:
                state.Failure = failed.Error;
                return 0;
            case AssemblyContextEntry<
                AssemblyMemberMatches>.Available available:
                state.AddInspectionFailures(
                    available.Value.InspectionFailures);
                ImmutableArray<MemberFindSemanticMatch> matches =
                    ConvertMatches(
                        question,
                        state.Source,
                        patterns,
                        available.Value.Members);
                bool reachedLimit =
                    limit is int maximum
                    && matches.Length >= maximum;
                if (reachedLimit)
                {
                    state.HitLimit = true;
                    matches =
                        [.. matches.Take(limit!.Value)];
                }
                else
                {
                    state.AddCompletedPatterns(patterns);
                }
                state.AddMatches(matches);
                return matches.Length;
            default:
                throw new InvalidOperationException(
                    "Unknown assembly-context Member outcome.");
        }
    }

    private static AssemblyMemberMatches Inspect(
        string assemblyName,
        AssemblyInspectionSession session,
        IReadOnlyList<string> patterns,
        FindVisibility visibility,
        int? limit)
    {
        ApiSurface surface =
            session.CompatibilityApiSurface(
                visibility is FindVisibility.All);
        return new(
            MemberSearch.Search(
                    surface,
                    assemblyName,
                    patterns,
                    limit)
                .ToImmutableArray(),
            surface.InspectionFailures.ToImmutableArray());
    }

    private static ImmutableArray<MemberFindSemanticMatch> ConvertMatches(
        MemberFindQuestion question,
        FindSourceIdentity source,
        ImmutableArray<MemberFindPattern> evaluatedPatterns,
        ImmutableArray<MemberSearchResult> rows)
    {
        var matches =
            ImmutableArray.CreateBuilder<MemberFindSemanticMatch>();
        foreach (MemberSearchResult row in rows)
        {
            if ((uint)row.PatternOrdinal
                >= (uint)evaluatedPatterns.Length)
            {
                throw new InvalidOperationException(
                    "Member search returned a pattern ordinal outside the evaluated question.");
            }
            MemberFindPattern pattern =
                evaluatedPatterns[row.PatternOrdinal];
            if (!string.Equals(
                    row.Pattern,
                    pattern.Text,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Member search did not retain the evaluated pattern spelling.");
            }
            if (question.DeclaringTypeFilter is { } typeFilter
                && !typeFilter.Matches(row.DeclaringTypeName))
            {
                continue;
            }

            matches.Add(
                new(
                    pattern,
                    pattern is MemberFindPattern.Glob
                        ? MemberFindSemanticMatchKind.Glob
                        : MemberFindSemanticMatchKind.Direct,
                    new(
                        source,
                        row.DeclaringTypeName,
                        row.Anchor,
                        row.DeclarationOrder,
                        row.MemberOrder),
                    row.MemberName,
                    row.DeclaringType,
                    row.DeclaringNamespace,
                    row.Kind,
                    row.Signature,
                    row.ReturnType));
        }

        return
        [
            .. matches
                .OrderBy(static match => match.Pattern.Ordinal)
                .ThenBy(
                    static match =>
                        match.Declaration.DeclarationOrder)
                .ThenBy(
                    static match =>
                        match.Declaration.MemberOrder),
        ];
    }

    private static void ValidateSource(
        AssemblyContextSubject subject,
        FindSourceIdentity source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                subject.Identity,
                source.AssemblyIdentity)
            || !AssemblyReferenceIdentity.EquivalentComparer.Equals(
                subject.Identity,
                source.Coordinate.LibraryIdentity.Identity))
        {
            throw new ArgumentException(
                "A Member source identity must name the exact assembly-context participant.",
                nameof(source));
        }
        if (source.Selection != subject.Provenance)
        {
            throw new ArgumentException(
                "A Member source identity must retain the participant's exact selection evidence.",
                nameof(source));
        }
    }

    private sealed class EvaluationState
    {
        readonly List<MemberFindSemanticMatch> _matches = [];
        readonly List<ApiSurfaceInspectionFailure> _inspectionFailures = [];
        readonly HashSet<int> _completedPatternOrdinals = [];
        readonly HashSet<int> _matchedPatternOrdinals = [];
        int _acceptedCount;

        internal EvaluationState(
            AssemblyContextParticipant participant,
            AssemblyContextSubject subject,
            FindSourceIdentity source)
        {
            Participant = participant;
            Subject = subject;
            Source = source;
        }

        internal AssemblyContextParticipant Participant { get; }
        internal AssemblyContextSubject Subject { get; }
        internal FindSourceIdentity Source { get; }
        internal bool WasAttempted { get; set; }
        internal bool HitLimit { get; set; }
        internal CandidateOpenFailure? Rejection { get; set; }
        internal Exception? Failure { get; set; }
        internal bool IsTerminal =>
            Rejection is not null || Failure is not null;

        internal void AddMatches(
            ImmutableArray<MemberFindSemanticMatch> matches) =>
            _matches.AddRange(matches);

        internal void AddAcceptedMatches(
            MemberFindPattern pattern,
            int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            _acceptedCount = checked(_acceptedCount + count);
            if (count > 0)
                _matchedPatternOrdinals.Add(pattern.Ordinal);
        }

        internal void AddCompletedPatterns(
            ImmutableArray<MemberFindPattern> patterns)
        {
            foreach (MemberFindPattern pattern in patterns)
                _completedPatternOrdinals.Add(pattern.Ordinal);
        }

        internal void AddInspectionFailures(
            ImmutableArray<ApiSurfaceInspectionFailure> failures)
        {
            foreach (ApiSurfaceInspectionFailure failure in failures)
            {
                if (!_inspectionFailures.Contains(failure))
                    _inspectionFailures.Add(failure);
            }
        }

        internal MemberFindSourceEvaluation ToEvaluation(
            MemberFindQuestion question,
            bool populationLimitReached)
        {
            if (Rejection is { } rejection)
            {
                var rejectedCoverage = new MemberFindSourceCoverage(
                    Source,
                    MemberFindSourceCoverageKind.AccessRejected,
                    rejection.Detail,
                    [],
                    []);
                return new MemberFindSourceEvaluation.Rejected(
                    question,
                    Source,
                    rejectedCoverage);
            }
            if (Failure is { } failure)
            {
                var failedCoverage = new MemberFindSourceCoverage(
                    Source,
                    MemberFindSourceCoverageKind.Unavailable,
                    failure.Message,
                    [],
                    []);
                return new MemberFindSourceEvaluation.Failed(
                    question,
                    Source,
                    failedCoverage);
            }

            ImmutableArray<ApiSurfaceInspectionFailure> failures =
                [.. _inspectionFailures];
            MemberFindSourceCoverageKind kind =
                HitLimit
                || populationLimitReached
                    && _completedPatternOrdinals.Count
                        < question.Patterns.Length
                    ? MemberFindSourceCoverageKind.MatchLimitReached
                    : failures.IsEmpty
                        ? MemberFindSourceCoverageKind.Complete
                        : MemberFindSourceCoverageKind.InspectionFailures;
            string? detail = kind switch
            {
                MemberFindSourceCoverageKind.MatchLimitReached =>
                    "Member matching stopped at the question limit.",
                MemberFindSourceCoverageKind.InspectionFailures =>
                    $"{failures.Length} metadata row(s) were rejected.",
                _ => null,
            };
            var coverage = new MemberFindSourceCoverage(
                Source,
                kind,
                detail,
                failures,
                [.. _completedPatternOrdinals.Order()])
            {
                MatchedPatternOrdinals =
                    [.. _matchedPatternOrdinals.Order()],
            };
            return new MemberFindSourceEvaluation.Available(
                question,
                Source,
                [
                    .. _matches
                        .OrderBy(
                            static match =>
                                match.Pattern.Ordinal)
                        .ThenBy(
                            static match =>
                                match.Declaration.DeclarationOrder)
                        .ThenBy(
                            static match =>
                                match.Declaration.MemberOrder),
                ],
                _acceptedCount == 0
                    ? _matches.Count
                    : _acceptedCount,
                coverage);
        }
    }
}
