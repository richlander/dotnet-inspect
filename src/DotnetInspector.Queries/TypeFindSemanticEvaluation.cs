using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Visibility evidence required by a semantic Find question.</summary>
public enum FindVisibility
{
    Public,
    All,
}

/// <summary>A normalized host-neutral semantic Find question.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeFindQuestion), "type")]
[JsonDerivedType(typeof(MemberFindQuestion), "member")]
public abstract record FindQuestion
{
    private protected FindQuestion()
    {
    }
}

/// <summary>
/// One normalized Type pattern with its input ordinal and visible spelling.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeFindPattern.Ordinary), "ordinary")]
[JsonDerivedType(typeof(TypeFindPattern.Glob), "glob")]
[JsonDerivedType(typeof(TypeFindPattern.Namespace), "namespace")]
public abstract record TypeFindPattern
{
    private protected TypeFindPattern(int ordinal, string text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Ordinal = ordinal;
        Text = text;
    }

    public int Ordinal { get; }
    public string Text { get; }

    public static TypeFindPattern Create(int ordinal, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return TypeMatcher.IsTypeGlobPattern(text)
            ? new Glob(ordinal, text)
            : new Ordinary(ordinal, text);
    }

    public static TypeFindPattern CreateNamespace(
        int ordinal,
        string text,
        string @namespace,
        MetadataNamespaceMatch match)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        if (match is not MetadataNamespaceMatch.Exact
            and not MetadataNamespaceMatch.ExactOrDescendant)
        {
            throw new ArgumentOutOfRangeException(
                nameof(match),
                match,
                "Type Find namespace matching supports exact or descendant matching.");
        }

        return new Namespace(ordinal, text, @namespace, match);
    }

    public static TypeFindPattern CreateWithExactNamespaceFallback(
        int ordinal,
        string text,
        string @namespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        return new Ordinary(
            ordinal,
            text,
            @namespace);
    }

    public sealed record Ordinary : TypeFindPattern
    {
        public Ordinary(
            int ordinal,
            string text,
            string? exactNamespaceFallback = null)
            : base(ordinal, text)
        {
            if (exactNamespaceFallback is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    exactNamespaceFallback);
            }
            ExactNamespaceFallback = exactNamespaceFallback;
        }

        public string? ExactNamespaceFallback { get; }
    }

    public sealed record Glob : TypeFindPattern
    {
        public Glob(int ordinal, string text)
            : base(ordinal, text)
        {
        }
    }

    public sealed record Namespace : TypeFindPattern
    {
        public Namespace(
            int ordinal,
            string text,
            string @namespace,
            MetadataNamespaceMatch match)
            : base(ordinal, text)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
            if (match is not MetadataNamespaceMatch.Exact
                and not MetadataNamespaceMatch.ExactOrDescendant)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(match),
                    match,
                    "Type Find namespace matching supports exact or descendant matching.");
            }
            NamespaceName = @namespace;
            Match = match;
        }

        public string NamespaceName { get; }
        public MetadataNamespaceMatch Match { get; }
    }
}

/// <summary>An ordered plural Type Find question.</summary>
public sealed record TypeFindQuestion : FindQuestion
{
    [JsonConstructor]
    public TypeFindQuestion(
        ImmutableArray<TypeFindPattern> patterns,
        FindVisibility visibility,
        int? maximumMatches)
    {
        Validate(patterns, visibility, maximumMatches);
        Patterns = patterns;
        Visibility = visibility;
        MaximumMatches = maximumMatches;
    }

    public ImmutableArray<TypeFindPattern> Patterns { get; }
    public FindVisibility Visibility { get; }
    public int? MaximumMatches { get; }

    public static TypeFindQuestion Create(
        IEnumerable<string> patterns,
        FindVisibility visibility,
        int? maximumMatches = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        return Create(
            patterns.Select(
                static (pattern, ordinal) =>
                    TypeFindPattern.Create(ordinal, pattern)),
            visibility,
            maximumMatches);
    }

    public static TypeFindQuestion Create(
        IEnumerable<TypeFindPattern> patterns,
        FindVisibility visibility,
        int? maximumMatches = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ImmutableArray<TypeFindPattern> normalized = [.. patterns];
        return new(normalized, visibility, maximumMatches);
    }

    private static void Validate(
        ImmutableArray<TypeFindPattern> patterns,
        FindVisibility visibility,
        int? maximumMatches)
    {
        if (!Enum.IsDefined(visibility))
            throw new ArgumentOutOfRangeException(nameof(visibility));
        if (maximumMatches is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMatches),
                "A Type Find match limit must be positive.");
        }
        if (patterns.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A Type Find question requires at least one pattern.",
                nameof(patterns));
        }

        for (int index = 0; index < patterns.Length; index++)
        {
            TypeFindPattern pattern =
                patterns[index]
                ?? throw new ArgumentException(
                    "A Type Find pattern cannot be null.",
                    nameof(patterns));
            if (pattern.Ordinal != index)
            {
                throw new ArgumentException(
                    "Type Find pattern ordinals must be contiguous and ordered.",
                    nameof(patterns));
            }
        }
    }
}

/// <summary>The strongest established classification for one Type candidate.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TypeFindSemanticMatchKind>))]
public enum TypeFindSemanticMatchKind
{
    Exact,
    Direct,
    Glob,
    Namespace,
    Prefix,
    Substring,
    Partial,
}

/// <summary>Population-relative settlement for one input pattern.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FindPatternSettlementKind>))]
public enum FindPatternSettlementKind
{
    Matched,
    NoMatch,
    Inconclusive,
    NotEvaluated,
}

/// <summary>Whether semantic match production exhausted the question.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FindMatchCompletion>))]
public enum FindMatchCompletion
{
    Exhausted,
    MatchLimitReached,
}

/// <summary>
/// One exact source occurrence inside an ordered semantic Find population.
/// </summary>
public sealed record FindSourceIdentity
{
    public FindSourceIdentity(
        ExactLibrarySourceCoordinate coordinate,
        AssemblyResolutionProvenance selection,
        int contextOrder,
        int memberOrder,
        AssemblyReferenceIdentity assemblyIdentity)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(assemblyIdentity);
        ArgumentOutOfRangeException.ThrowIfNegative(contextOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(memberOrder);
        Coordinate = coordinate;
        Selection = selection;
        ContextOrder = contextOrder;
        MemberOrder = memberOrder;
        AssemblyIdentity = assemblyIdentity;
    }

    public ExactLibrarySourceCoordinate Coordinate { get; }
    public AssemblyResolutionProvenance Selection { get; }
    public int ContextOrder { get; }
    public int MemberOrder { get; }
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
}

/// <summary>Exact source-backed identity of one Type declaration.</summary>
public sealed record TypeFindDeclarationAssociation
{
    public TypeFindDeclarationAssociation(
        FindSourceIdentity source,
        MetadataTypeDefinitionName name,
        Guid moduleVersionId,
        int declarationOrder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(declarationOrder);
        Source = source;
        Name = name;
        ModuleVersionId = moduleVersionId;
        DeclarationOrder = declarationOrder;
    }

    public FindSourceIdentity Source { get; }
    public MetadataTypeDefinitionName Name { get; }
    public Guid ModuleVersionId { get; }
    public int DeclarationOrder { get; }
}

/// <summary>One independently truthful semantic Type match.</summary>
public sealed record TypeFindSemanticMatch(
    TypeFindPattern Pattern,
    string EffectivePattern,
    TypeFindSemanticMatchKind Match,
    double Similarity,
    TypeFindDeclarationAssociation Declaration,
    AssemblyTypeDeclarationKind DeclarationKind,
    AssemblyTypeDefinitionKind? DefinitionKind,
    TypeDeclarationDiscoveryAttributes? DiscoveryAttributes)
{
    public string TypeName =>
        Declaration.Name.Segments.Length == 1
            ? Declaration.Name.Segments[0]
            : string.Join(".", Declaration.Name.Segments);

    public string Namespace => Declaration.Name.Namespace;

    public string FullName => Declaration.Name.ToMetadataFullName();
}

/// <summary>Unsupported Metadata declaration evidence attributed to a source.</summary>
public sealed record TypeFindUnsupportedDeclaration(
    MetadataTypeDefinitionName Name,
    AssemblyTypeDeclarationKind Kind);

/// <summary>Evaluation coverage established for one exact Type source.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TypeFindSourceCoverageKind>))]
public enum TypeFindSourceCoverageKind
{
    Complete,
    UnsupportedDeclarations,
    InventoryRejected,
    AccessRejected,
    Unavailable,
    NotEvaluated,
}

/// <summary>Detached coverage and failure evidence for one exact source.</summary>
public sealed record TypeFindSourceCoverage(
    FindSourceIdentity Source,
    TypeFindSourceCoverageKind Kind,
    string? Detail,
    ImmutableArray<TypeFindUnsupportedDeclaration> UnsupportedDeclarations)
{
    public bool IsComplete => Kind is TypeFindSourceCoverageKind.Complete;
}

/// <summary>
/// Population-owner coverage that could not be attributed to an exact source.
/// </summary>
public sealed record FindPopulationGap(
    int ContextOrder,
    int? MemberOrder,
    string Kind,
    string Detail);

/// <summary>One immutable question-specific source map outcome.</summary>
public abstract class FindSourceEvaluation
{
    private protected FindSourceEvaluation(FindQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        Question = question;
    }

    public FindQuestion Question { get; }
}

/// <summary>One Type source outcome: available, rejected, or failed.</summary>
public abstract class TypeFindSourceEvaluation
    : FindSourceEvaluation
{
    private protected TypeFindSourceEvaluation(
        TypeFindQuestion question,
        FindSourceIdentity source,
        TypeFindSourceCoverage coverage)
        : base(question)
    {
        Source = source;
        Coverage = coverage;
    }

    public new TypeFindQuestion Question =>
        (TypeFindQuestion)base.Question;
    public FindSourceIdentity Source { get; }
    public TypeFindSourceCoverage Coverage { get; }
    public abstract ImmutableArray<TypeFindSemanticMatch> Matches { get; }
    public bool IsComplete => Coverage.IsComplete;

    public sealed class Available : TypeFindSourceEvaluation
    {
        internal Available(
            TypeFindQuestion question,
            FindSourceIdentity source,
            ImmutableArray<TypeFindSemanticMatch> matches,
            TypeFindSourceCoverage coverage)
            : base(question, source, coverage)
        {
            Matches = matches;
        }

        public override ImmutableArray<TypeFindSemanticMatch> Matches
        {
            get;
        }
    }

    public sealed class Rejected : TypeFindSourceEvaluation
    {
        internal Rejected(
            TypeFindQuestion question,
            FindSourceIdentity source,
            TypeFindSourceCoverage coverage)
            : base(question, source, coverage)
        {
        }

        public override ImmutableArray<TypeFindSemanticMatch> Matches => [];
    }

    public sealed class Failed : TypeFindSourceEvaluation
    {
        internal Failed(
            TypeFindQuestion question,
            FindSourceIdentity source,
            TypeFindSourceCoverage coverage)
            : base(question, source, coverage)
        {
        }

        public override ImmutableArray<TypeFindSemanticMatch> Matches => [];
    }
}

/// <summary>
/// An ordered array of source outcomes plus its owner's coverage statement.
/// </summary>
public sealed class TypeFindSemanticPopulation
{
    private TypeFindSemanticPopulation(
        TypeFindQuestion question,
        ImmutableArray<TypeFindSourceEvaluation> sources,
        ImmutableArray<FindPopulationGap> gaps,
        bool ownerReportsComplete)
    {
        Question = question;
        Sources = sources;
        Gaps = gaps;
        IsComplete =
            ownerReportsComplete
            && gaps.IsEmpty
            && sources.All(static source => source.IsComplete);
    }

    public TypeFindQuestion Question { get; }
    public ImmutableArray<TypeFindSourceEvaluation> Sources { get; }
    public ImmutableArray<FindPopulationGap> Gaps { get; }
    public bool IsComplete { get; }

    public static TypeFindSemanticPopulation Create(
        TypeFindQuestion question,
        IEnumerable<TypeFindSourceEvaluation> sources,
        IEnumerable<FindPopulationGap>? gaps = null,
        bool ownerReportsComplete = true)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(sources);
        ImmutableArray<TypeFindSourceEvaluation> sourceArray = [.. sources];
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
                "A Type Find population cannot repeat a source identity.",
                nameof(sources));
        }

        return new(
            question,
            sourceArray,
            gaps is null ? [] : [.. gaps],
            ownerReportsComplete);
    }
}

/// <summary>Typed settlement evidence for one normalized Type pattern.</summary>
public sealed record TypeFindPatternSettlement(
    TypeFindPattern Pattern,
    FindPatternSettlementKind Kind);

/// <summary>
/// Portable completed Type Find content with separate coverage and match completion.
/// </summary>
public sealed class TypeFindBlock
{
    [JsonConstructor]
    public TypeFindBlock(
        TypeFindQuestion question,
        ImmutableArray<TypeFindSemanticMatch> matches,
        ImmutableArray<TypeFindPatternSettlement> settlements,
        ImmutableArray<TypeFindSourceCoverage> sourceCoverage,
        ImmutableArray<FindPopulationGap> populationGaps,
        bool isSourceCoverageComplete,
        FindMatchCompletion matchCompletion)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (matches.IsDefault
            || settlements.IsDefault
            || sourceCoverage.IsDefault
            || populationGaps.IsDefault)
        {
            throw new ArgumentException(
                "Type Find block arrays must be initialized.");
        }
        Question = question;
        Matches = matches;
        Settlements = settlements;
        SourceCoverage = sourceCoverage;
        PopulationGaps = populationGaps;
        IsSourceCoverageComplete = isSourceCoverageComplete;
        MatchCompletion = matchCompletion;
    }

    public TypeFindQuestion Question { get; }
    public ImmutableArray<TypeFindSemanticMatch> Matches { get; }
    public ImmutableArray<TypeFindPatternSettlement> Settlements { get; }
    public ImmutableArray<TypeFindSourceCoverage> SourceCoverage { get; }
    public ImmutableArray<FindPopulationGap> PopulationGaps { get; }
    public bool IsSourceCoverageComplete { get; }
    public FindMatchCompletion MatchCompletion { get; }
}

/// <summary>Maps exact locator census facts into semantic Type source outcomes.</summary>
public static class TypeFindSourceEvaluator
{
    public static TypeFindSemanticPopulation EvaluateLocatorCensus(
        TypeFindQuestion question,
        TypeDeclarationLocatorResult result)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(result);
        if (result is TypeDeclarationLocatorResult.Rejected rejected)
        {
            string detail = rejected.PopulationFailure is { } failure
                ? failure.ToString()
                : rejected.Kind.ToString();
            return TypeFindSemanticPopulation.Create(
                question,
                [],
                [
                    new(
                        -1,
                        null,
                        $"Locator{rejected.Kind}",
                        detail),
                ],
                ownerReportsComplete: false);
        }

        var evaluated = (TypeDeclarationLocatorResult.Evaluated)result;
        if (evaluated.Answers.Length != 1
            || evaluated.Answers[0].Request
                is not TypeDeclarationLocatorRequest.Pattern
                {
                    Text: "*",
                })
        {
            throw new ArgumentException(
                "Type Find source evaluation requires one all-declarations locator census.",
                nameof(result));
        }

        TypeDeclarationLocatorAnswer census = evaluated.Answers[0];
        Dictionary<
            (int ContextOrder, int MemberOrder),
            List<TypeDeclarationLocatorCandidate>> candidatesByMember =
                [];
        foreach (TypeDeclarationLocatorCandidate candidate
            in census.Candidates)
        {
            var key =
                (
                    candidate.Observation.Occurrence.ContextOrder,
                    candidate.Observation.Occurrence.MemberOrder);
            if (!candidatesByMember.TryGetValue(
                    key,
                    out List<TypeDeclarationLocatorCandidate>? candidates))
            {
                candidates = [];
                candidatesByMember.Add(key, candidates);
            }
            candidates.Add(candidate);
        }

        var sources =
            ImmutableArray.CreateBuilder<TypeFindSourceEvaluation>();
        var gaps = ImmutableArray.CreateBuilder<FindPopulationGap>();
        foreach (WorkspaceDeclarationContextReceipt context
            in evaluated.Population.Contexts)
        {
            foreach (WorkspaceDeclarationFailure failure
                in context.Failures)
            {
                gaps.Add(
                    new(
                        context.Order,
                        null,
                        failure.GetType().Name,
                        failure.ToString()));
            }
        }

        foreach (TypeDeclarationLocatorMemberOutcome outcome
            in evaluated.Members)
        {
            WorkspaceDeclarationMember member = outcome.Member;
            int contextOrder = member.Occurrence.ContextOrder;
            int memberOrder = member.Occurrence.MemberOrder;
            if (member.Coordinate is null)
            {
                gaps.Add(
                    new(
                        contextOrder,
                        memberOrder,
                        nameof(
                            WorkspaceDeclarationCoordinateStatus
                                .CoordinateUnavailable),
                        $"No exact source coordinate was available for "
                        + $"'{member.AssemblyIdentity.Name}'."));
                continue;
            }

            var source =
                new FindSourceIdentity(
                    member.Coordinate,
                    member.Selection,
                    contextOrder,
                    memberOrder,
                    member.AssemblyIdentity);
            if (outcome is TypeDeclarationLocatorMemberOutcome.Searched
                searched)
            {
                candidatesByMember.TryGetValue(
                    (contextOrder, memberOrder),
                    out List<TypeDeclarationLocatorCandidate>? candidates);
                ImmutableArray<TypeFindSemanticMatch> matches =
                    EvaluateAvailableSource(
                        question,
                        source,
                        candidates ?? []);
                ImmutableArray<TypeFindUnsupportedDeclaration> unsupported =
                [
                    .. searched.UnsupportedDeclarations.Select(
                        static declaration =>
                            new TypeFindUnsupportedDeclaration(
                                declaration.Name,
                                declaration.Kind)),
                ];
                var coverage =
                    new TypeFindSourceCoverage(
                        source,
                        unsupported.IsEmpty
                            ? TypeFindSourceCoverageKind.Complete
                            : TypeFindSourceCoverageKind
                                .UnsupportedDeclarations,
                        unsupported.IsEmpty
                            ? null
                            : $"{unsupported.Length} unsupported "
                                + "declaration form(s) were encountered.",
                        unsupported);
                sources.Add(
                    new TypeFindSourceEvaluation.Available(
                        question,
                        source,
                        matches,
                        coverage));
                continue;
            }

            (
                TypeFindSourceCoverageKind coverageKind,
                string detail) = outcome switch
            {
                TypeDeclarationLocatorMemberOutcome.InventoryRejected
                    inventoryRejected =>
                    (
                        TypeFindSourceCoverageKind.InventoryRejected,
                        inventoryRejected.Failure.Detail),
                TypeDeclarationLocatorMemberOutcome.AccessRejected
                    accessRejected =>
                    (
                        TypeFindSourceCoverageKind.AccessRejected,
                        accessRejected.Failure.Detail),
                TypeDeclarationLocatorMemberOutcome.Unavailable unavailable =>
                    (
                        TypeFindSourceCoverageKind.Unavailable,
                        unavailable.Failure.ToString()),
                TypeDeclarationLocatorMemberOutcome.NotEvaluated
                    notEvaluated =>
                    (
                        TypeFindSourceCoverageKind.NotEvaluated,
                        notEvaluated.Bound?.ToString()
                            ?? "The declaration inventory was not evaluated."),
                TypeDeclarationLocatorMemberOutcome.CoordinateUnavailable =>
                    throw new InvalidOperationException(
                        "A coordinate-unavailable outcome retained a coordinate."),
                _ => throw new InvalidOperationException(
                    "Unknown Type declaration locator member outcome."),
            };
            TypeFindSourceCoverage incompleteCoverage =
                new(
                    source,
                    coverageKind,
                    detail,
                    []);
            sources.Add(
                coverageKind is TypeFindSourceCoverageKind.Unavailable
                    ? new TypeFindSourceEvaluation.Failed(
                        question,
                        source,
                        incompleteCoverage)
                    : new TypeFindSourceEvaluation.Rejected(
                        question,
                        source,
                        incompleteCoverage));
        }

        return TypeFindSemanticPopulation.Create(
            question,
            sources
                .OrderBy(static source => source.Source.ContextOrder)
                .ThenBy(static source => source.Source.MemberOrder),
            gaps,
            census.IsComplete);
    }

    private static ImmutableArray<TypeFindSemanticMatch>
        EvaluateAvailableSource(
            TypeFindQuestion question,
            FindSourceIdentity source,
            IReadOnlyList<TypeDeclarationLocatorCandidate> candidates)
    {
        var matches =
            ImmutableArray.CreateBuilder<TypeFindSemanticMatch>();
        foreach (TypeFindPattern pattern in question.Patterns)
        {
            foreach (TypeDeclarationLocatorCandidate candidate
                in candidates.OrderBy(
                    static candidate => candidate.DeclarationOrder))
            {
                if (!IsVisible(candidate, question.Visibility))
                    continue;
                TypeFindSemanticMatch? match =
                    Classify(pattern, source, candidate);
                if (match is not null)
                    matches.Add(match);
            }
        }
        return matches.DrainToImmutable();
    }

    private static bool IsVisible(
        TypeDeclarationLocatorCandidate candidate,
        FindVisibility visibility)
    {
        if (candidate.Kind is not AssemblyTypeDeclarationKind.Definition
            || TypeFilters.IsCompilerGenerated(
                candidate.Name.Segments[^1]))
        {
            return false;
        }

        if (visibility is FindVisibility.All)
            return true;

        return candidate.IsDefinitionPublic is true
            && candidate.DiscoveryAttributes
                is not { IsEditorBrowsableNever: true }
                and not { IsObsolete: true };
    }

    private static TypeFindSemanticMatch? Classify(
        TypeFindPattern pattern,
        FindSourceIdentity source,
        TypeDeclarationLocatorCandidate candidate)
    {
        string typeName =
            candidate.Name.Segments.Length == 1
                ? candidate.Name.Segments[0]
                : string.Join(".", candidate.Name.Segments);
        string fullName = candidate.Name.ToMetadataFullName();
        TypeFindSemanticMatchKind match;
        double similarity = 1.0;
        string effectivePattern = pattern.Text;

        if (pattern is TypeFindPattern.Namespace namespacePattern)
        {
            if (!candidate.Name.IsInNamespace(
                    namespacePattern.NamespaceName,
                    namespacePattern.Match))
            {
                return null;
            }
            match = TypeFindSemanticMatchKind.Namespace;
        }
        else if (pattern is not TypeFindPattern.Glob
            && (string.Equals(
                    typeName,
                    pattern.Text,
                    StringComparison.Ordinal)
                || string.Equals(
                    fullName,
                    pattern.Text,
                    StringComparison.Ordinal)))
        {
            match = TypeFindSemanticMatchKind.Exact;
        }
        else if (TypeMatcher.MatchesTypeFilter(
                fullName,
                pattern.Text))
        {
            match = pattern is TypeFindPattern.Glob
                ? TypeFindSemanticMatchKind.Glob
                : TypeFindSemanticMatchKind.Direct;
        }
        else if (pattern is TypeFindPattern.Ordinary
            {
                ExactNamespaceFallback: { } @namespace,
            }
            && candidate.Name.IsInNamespace(
                @namespace,
                MetadataNamespaceMatch.Exact))
        {
            match = TypeFindSemanticMatchKind.Namespace;
        }
        else
        {
            TypeNameMatchTier? tier =
                TypeNameMatchRanking.Classify(
                    fullName,
                    pattern.Text);
            if (tier is TypeNameMatchTier.Prefix)
            {
                match = TypeFindSemanticMatchKind.Prefix;
                if (LooksLikeNamespacePrefix(pattern.Text))
                    effectivePattern = $"{pattern.Text}*";
            }
            else if (tier is TypeNameMatchTier.Substring)
            {
                match = TypeFindSemanticMatchKind.Substring;
            }
            else if (IsSimilarityEligible(pattern.Text)
                && TypeMatcher.NameSimilarity(
                    fullName,
                    pattern.Text) is >= 0.5 and var score)
            {
                match = TypeFindSemanticMatchKind.Partial;
                similarity = score;
            }
            else
            {
                return null;
            }
        }

        return new(
            pattern,
            effectivePattern,
            match,
            similarity,
            new(
                source,
                candidate.Name,
                candidate.ModuleVersionId,
                candidate.DeclarationOrder),
            candidate.Kind,
            candidate.DefinitionKind,
            candidate.DiscoveryAttributes);
    }

    private static bool LooksLikeNamespacePrefix(string pattern) =>
        pattern.Contains('.')
        && !pattern.Contains('<')
        && !pattern.Contains('`');

    private static bool IsSimilarityEligible(string pattern) =>
        !pattern.Contains('*')
        && !pattern.Contains('?');
}

/// <summary>Reduces ordered Type source outcomes into one portable block.</summary>
public static class FindSemanticReducer
{
    public static TypeFindBlock ReduceType(
        TypeFindQuestion question,
        TypeFindSemanticPopulation population)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(population);
        if (!ReferenceEquals(question, population.Question))
        {
            throw new ArgumentException(
                "The Type Find population must retain the exact reduction question.",
                nameof(population));
        }

        var matches =
            ImmutableArray.CreateBuilder<TypeFindSemanticMatch>();
        var settlements =
            ImmutableArray.CreateBuilder<TypeFindPatternSettlement>();
        bool limitReached = false;
        foreach (TypeFindPattern pattern in question.Patterns)
        {
            if (limitReached)
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.NotEvaluated));
                continue;
            }

            int before = matches.Count;
            foreach (TypeFindSourceEvaluation source
                in population.Sources)
            {
                foreach (TypeFindSemanticMatch match
                    in source.Matches.Where(
                        match =>
                            match.Pattern.Ordinal == pattern.Ordinal))
                {
                    if (question.MaximumMatches is int maximum
                        && matches.Count >= maximum)
                    {
                        limitReached = true;
                        break;
                    }
                    matches.Add(match);
                }
                if (limitReached)
                    break;
            }

            if (matches.Count > before)
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.Matched));
            }
            else if (population.IsComplete)
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.NoMatch));
            }
            else
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.Inconclusive));
            }

            if (question.MaximumMatches is int limit
                && matches.Count >= limit)
            {
                limitReached = true;
            }
        }

        return new(
            question,
            matches.DrainToImmutable(),
            settlements.DrainToImmutable(),
            [.. population.Sources.Select(static source => source.Coverage)],
            population.Gaps,
            population.IsComplete,
            limitReached
                ? FindMatchCompletion.MatchLimitReached
                : FindMatchCompletion.Exhausted);
    }

    public static MemberFindBlock ReduceMember(
        MemberFindQuestion question,
        MemberFindSemanticPopulation population)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(population);
        if (!ReferenceEquals(question, population.Question))
        {
            throw new ArgumentException(
                "The Member Find population must retain the exact reduction question.",
                nameof(population));
        }

        var matches =
            ImmutableArray.CreateBuilder<MemberFindSemanticMatch>();
        var settlements =
            ImmutableArray.CreateBuilder<MemberFindPatternSettlement>();
        bool limitReached = false;
        foreach (MemberFindPattern pattern in question.Patterns)
        {
            if (limitReached)
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.NotEvaluated));
                continue;
            }

            int before = matches.Count;
            foreach (MemberFindSourceEvaluation source
                in population.Sources)
            {
                foreach (MemberFindSemanticMatch match
                    in source.Matches.Where(
                        match =>
                            match.Pattern.Ordinal == pattern.Ordinal))
                {
                    if (question.MaximumMatches is int maximum
                        && matches.Count >= maximum)
                    {
                        limitReached = true;
                        break;
                    }
                    matches.Add(match);
                }
                if (limitReached)
                    break;
            }

            if (matches.Count > before)
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.Matched));
            }
            else if (IsMemberPatternComplete(
                         pattern,
                         population))
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.NoMatch));
            }
            else
            {
                settlements.Add(
                    new(
                        pattern,
                        FindPatternSettlementKind.Inconclusive));
            }

            if (question.MaximumMatches is int limit
                && matches.Count >= limit)
            {
                limitReached = true;
            }
        }

        return new(
            question,
            matches.DrainToImmutable(),
            settlements.DrainToImmutable(),
            [.. population.Sources.Select(static source => source.Coverage)],
            population.Gaps,
            population.IsComplete,
            limitReached
                ? FindMatchCompletion.MatchLimitReached
                : FindMatchCompletion.Exhausted);
    }

    private static bool IsMemberPatternComplete(
        MemberFindPattern pattern,
        MemberFindSemanticPopulation population) =>
        population.Gaps.IsEmpty
        && (
            population.OwnerReportsComplete
            || population.Sources.Any(
                static source =>
                    source.Coverage.Kind
                    is MemberFindSourceCoverageKind
                        .MatchLimitReached))
        && population.Sources.All(
            source =>
                source is MemberFindSourceEvaluation.Available
                && source.Coverage.IsPatternComplete(
                    pattern.Ordinal));
}
