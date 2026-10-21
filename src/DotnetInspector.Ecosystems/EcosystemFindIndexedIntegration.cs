using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using NuGetFetch;

namespace DotnetInspector.Ecosystems;

public sealed record EcosystemFindIndexedIntegrationSourceIdentity(
    EcosystemPackId Ecosystem,
    PackagePrefixDeclaration Prefix);

public sealed record EcosystemFindIndexedIntegrationRequest
{
    public EcosystemFindIndexedIntegrationRequest(
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        IReadOnlyList<EcosystemFindIndexedIntegrationSourceIdentity>
            demandedSources,
        FindQuestion question,
        int? maximumRows)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(demandedSources);
        ArgumentNullException.ThrowIfNull(question);
        if (maximumRows is < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRows));
        int? questionMaximum = question switch
        {
            TypeFindQuestion type => type.MaximumMatches,
            MemberFindQuestion member => member.MaximumMatches,
            _ => null,
        };
        if (maximumRows is int requested
            && questionMaximum is int maximum
            && requested > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumRows),
                "Remaining indexed row capacity cannot exceed the question's maximum.");
        }
        if (artifacts.Any(static artifact => artifact is null))
        {
            throw new ArgumentException(
                "Admitted Ecosystem artifacts cannot contain null.",
                nameof(artifacts));
        }
        if (demandedSources.Any(static source =>
                source is null
                || source.Ecosystem is null
                || source.Prefix is null))
        {
            throw new ArgumentException(
                "Demanded indexed sources cannot contain null.",
                nameof(demandedSources));
        }

        Artifacts = [.. artifacts];
        DemandedSources = [.. demandedSources];
        Question = question;
        MaximumRows = maximumRows;
        Validate();
    }

    public ImmutableArray<EcosystemFindIndexArtifact> Artifacts { get; }
    public ImmutableArray<EcosystemFindIndexedIntegrationSourceIdentity>
        DemandedSources
    { get; }
    public FindQuestion Question { get; }
    public int? MaximumRows { get; }

    private void Validate()
    {
        if (Artifacts
            .Select(static artifact => artifact.Ecosystem)
            .Distinct()
            .Count()
            != Artifacts.Length)
        {
            throw new ArgumentException(
                "Only one admitted artifact may name an Ecosystem.",
                nameof(Artifacts));
        }
        if (DemandedSources.Distinct().Count() != DemandedSources.Length)
        {
            throw new ArgumentException(
                "Demanded indexed source identities must be unique.",
                nameof(DemandedSources));
        }
        foreach (EcosystemFindIndexedIntegrationSourceIdentity demand
            in DemandedSources)
        {
            EcosystemFindIndexArtifact? artifact = Artifacts.SingleOrDefault(
                candidate => candidate.Ecosystem == demand.Ecosystem);
            if (artifact is null
                || !artifact.Prefixes.Contains(demand.Prefix))
            {
                throw new ArgumentException(
                    $"Demanded indexed source '{demand.Ecosystem}/"
                        + $"{demand.Prefix.Prefix}' is not present in the "
                        + "admitted artifacts.",
                    nameof(DemandedSources));
            }
        }
    }
}

public enum EcosystemFindIndexedIntegrationCompletion
{
    Exhausted,
    RowLimitReached,
    Canceled,
}

public sealed record EcosystemFindIndexedPublishedSymbolIdentity
{
    private EcosystemFindIndexedPublishedSymbolIdentity(
        ExactLibrarySourceCoordinate source,
        MetadataTypeDefinitionName? type,
        MemberAnchor? member)
    {
        Source = source;
        Type = type;
        Member = member;
    }

    public ExactLibrarySourceCoordinate Source { get; }
    public MetadataTypeDefinitionName? Type { get; }
    public MemberAnchor? Member { get; }

    internal static EcosystemFindIndexedPublishedSymbolIdentity ForType(
        ExactLibrarySourceCoordinate source,
        MetadataTypeDefinitionName type) =>
        new(source, type, null);

    internal static EcosystemFindIndexedPublishedSymbolIdentity ForMember(
        ExactLibrarySourceCoordinate source,
        MemberAnchor member) =>
        new(source, null, member);
}

public sealed record EcosystemFindIndexedSourceCoverage(
    FindSourceIdentity Source,
    int SymbolCount,
    ImmutableArray<int> EvaluatedSymbolOrdinals,
    ImmutableArray<int> CompletedPatternOrdinals)
{
    public bool IsExhausted { get; init; }
}

public sealed record EcosystemFindIndexedPrefixCoverage(
    EcosystemFindIndexedIntegrationSourceIdentity Source,
    ImmutableArray<EcosystemFindIndexedSourceCoverage> Libraries,
    ImmutableArray<EcosystemFindIndexGap> Gaps)
{
    public bool IsExhausted { get; init; }
}

public abstract class EcosystemFindIndexedIntegrationResult
{
    private protected EcosystemFindIndexedIntegrationResult(
        FindQuestion question,
        ImmutableArray<EcosystemFindIndexedPublishedSymbolIdentity>
            publishedSymbols,
        ImmutableArray<EcosystemFindIndexedPrefixCoverage> coverage,
        EcosystemFindIndexedIntegrationCompletion completion)
    {
        Question = question;
        PublishedSymbols = publishedSymbols;
        Coverage = coverage;
        Completion = completion;
    }

    public FindQuestion Question { get; }
    public ImmutableArray<EcosystemFindIndexedPublishedSymbolIdentity>
        PublishedSymbols
    { get; }
    public ImmutableArray<EcosystemFindIndexedPrefixCoverage> Coverage { get; }
    public EcosystemFindIndexedIntegrationCompletion Completion { get; }
    public bool ExhaustedEveryDemandedSource =>
        Completion is EcosystemFindIndexedIntegrationCompletion.Exhausted;

    public sealed class Type : EcosystemFindIndexedIntegrationResult
    {
        internal Type(
            TypeFindQuestion question,
            ImmutableArray<TypeFindSemanticMatch> matches,
            ImmutableArray<EcosystemFindIndexedPublishedSymbolIdentity>
                publishedSymbols,
            ImmutableArray<EcosystemFindIndexedPrefixCoverage> coverage,
            EcosystemFindIndexedIntegrationCompletion completion)
            : base(question, publishedSymbols, coverage, completion)
        {
            Matches = matches;
        }

        public ImmutableArray<TypeFindSemanticMatch> Matches { get; }
    }

    public sealed class Member : EcosystemFindIndexedIntegrationResult
    {
        internal Member(
            MemberFindQuestion question,
            ImmutableArray<MemberFindSemanticMatch> matches,
            int acceptedCount,
            ImmutableArray<EcosystemFindIndexedPublishedSymbolIdentity>
                publishedSymbols,
            ImmutableArray<EcosystemFindIndexedPrefixCoverage> coverage,
            EcosystemFindIndexedIntegrationCompletion completion)
            : base(question, publishedSymbols, coverage, completion)
        {
            Matches = matches;
            AcceptedCount = acceptedCount;
        }

        public ImmutableArray<MemberFindSemanticMatch> Matches { get; }
        public int AcceptedCount { get; }
    }
}

public static class EcosystemFindIndexedIntegration
{
    public static EcosystemFindIndexedIntegrationResult Evaluate(
        EcosystemFindIndexedIntegrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Question switch
        {
            TypeFindQuestion type => EvaluateType(
                request,
                type,
                cancellationToken),
            MemberFindQuestion member => EvaluateMember(
                request,
                member,
                cancellationToken),
            _ => throw new InvalidOperationException(
                "Unknown normalized Find question."),
        };
    }

    private static EcosystemFindIndexedIntegrationResult.Type EvaluateType(
        EcosystemFindIndexedIntegrationRequest request,
        TypeFindQuestion question,
        CancellationToken cancellationToken)
    {
        EvaluationPlan plan = EvaluationPlan.Create(request);
        var matches =
            ImmutableArray.CreateBuilder<TypeFindSemanticMatch>();
        var published =
            ImmutableArray.CreateBuilder<
                EcosystemFindIndexedPublishedSymbolIdentity>();
        var publishedSet =
            new HashSet<EcosystemFindIndexedPublishedSymbolIdentity>();
        int? maximum =
            EffectiveMaximum(request.MaximumRows, question.MaximumMatches);
        EcosystemFindIndexedIntegrationCompletion completion =
            cancellationToken.IsCancellationRequested
                ? EcosystemFindIndexedIntegrationCompletion.Canceled
                : maximum == 0
                ? EcosystemFindIndexedIntegrationCompletion.RowLimitReached
                : EcosystemFindIndexedIntegrationCompletion.Exhausted;
        if (completion is not EcosystemFindIndexedIntegrationCompletion.Exhausted)
            goto Complete;

        foreach (TypeFindPattern pattern in question.Patterns)
        {
            foreach (SourceState state in plan.Sources)
            {
                if (maximum is int limit && matches.Count >= limit)
                {
                    completion =
                        EcosystemFindIndexedIntegrationCompletion
                            .RowLimitReached;
                    goto Complete;
                }

                ImmutableArray<TypeFindSemanticCandidate> candidates =
                [
                    .. state.Symbols
                        .Where(static symbol =>
                            symbol.Kind
                                is EcosystemFindIndexSymbolKind.Type)
                        .Select(symbol =>
                            new TypeFindSemanticCandidate(
                                symbol.Type!,
                                state.Coordinate.ModuleVersionId,
                                symbol.SymbolOrdinal,
                                AssemblyTypeDeclarationKind.Definition,
                                DefinitionKind: null,
                                IsDefinitionPublic: true,
                                symbol.TypeDiscoveryAttributes)),
                ];
                int? remaining = maximum is int finite
                    ? finite - matches.Count
                    : null;
                TypeFindCandidateEvaluation evaluation =
                    TypeFindCandidateEvaluator.Evaluate(
                        question,
                        pattern,
                        state.Source,
                        candidates,
                        remaining,
                        cancellationToken);
                state.Observe(
                    pattern.Ordinal,
                    evaluation.EvaluatedDeclarationOrders,
                    evaluation.Completion);
                foreach (TypeFindSemanticMatch match in evaluation.Matches)
                {
                    matches.Add(match);
                    EcosystemFindIndexedPublishedSymbolIdentity identity =
                        EcosystemFindIndexedPublishedSymbolIdentity.ForType(
                            match.Declaration.Source.Coordinate,
                            match.Declaration.Name);
                    if (publishedSet.Add(identity))
                        published.Add(identity);
                }

                if (evaluation.Completion
                    is FindCandidateEvaluationCompletion.Canceled)
                {
                    completion =
                        EcosystemFindIndexedIntegrationCompletion.Canceled;
                    goto Complete;
                }
                if (evaluation.Completion
                    is FindCandidateEvaluationCompletion.MatchLimitReached
                    || maximum is int cap && matches.Count >= cap)
                {
                    completion =
                        EcosystemFindIndexedIntegrationCompletion
                            .RowLimitReached;
                    goto Complete;
                }
            }
        }

    Complete:
        return new(
            question,
            matches.DrainToImmutable(),
            published.DrainToImmutable(),
            plan.FreezeCoverage(),
            completion);
    }

    private static EcosystemFindIndexedIntegrationResult.Member EvaluateMember(
        EcosystemFindIndexedIntegrationRequest request,
        MemberFindQuestion question,
        CancellationToken cancellationToken)
    {
        EvaluationPlan plan = EvaluationPlan.Create(request);
        var matches =
            ImmutableArray.CreateBuilder<MemberFindSemanticMatch>();
        var published =
            ImmutableArray.CreateBuilder<
                EcosystemFindIndexedPublishedSymbolIdentity>();
        var publishedSet =
            new HashSet<EcosystemFindIndexedPublishedSymbolIdentity>();
        int? maximum = question.AcceptedRows is null
            ? EffectiveMaximum(request.MaximumRows, question.MaximumMatches)
            : request.MaximumRows;
        int acceptedCount = 0;
        EcosystemFindIndexedIntegrationCompletion completion =
            cancellationToken.IsCancellationRequested
                ? EcosystemFindIndexedIntegrationCompletion.Canceled
                : maximum == 0
                ? EcosystemFindIndexedIntegrationCompletion.RowLimitReached
                : EcosystemFindIndexedIntegrationCompletion.Exhausted;
        if (completion is not EcosystemFindIndexedIntegrationCompletion.Exhausted)
            goto Complete;

        foreach (MemberFindPattern pattern in question.Patterns)
        {
            foreach (SourceState state in plan.Sources)
            {
                if (maximum is int limit && matches.Count >= limit)
                {
                    completion =
                        EcosystemFindIndexedIntegrationCompletion
                            .RowLimitReached;
                    goto Complete;
                }

                ImmutableArray<MemberFindSemanticCandidate> candidates =
                [
                    .. state.Symbols
                        .Where(static symbol =>
                            symbol.Kind
                                is EcosystemFindIndexSymbolKind.Member)
                        .Select(symbol =>
                            new MemberFindSemanticCandidate(
                                symbol.Member!.DeclaringType,
                                symbol.Member.Member,
                                symbol.SymbolOrdinal,
                                MemberOrder: 0,
                                symbol.Member.Member.MemberName,
                                symbol.Member.DeclaringType
                                    .ToMetadataFullName(),
                                string.IsNullOrEmpty(
                                    symbol.Member.DeclaringType.Namespace)
                                    ? null
                                    : symbol.Member.DeclaringType.Namespace,
                                symbol.Member.DisplayKind,
                                symbol.Member.DisplaySignature,
                                ReturnType: null,
                                symbol.Member
                                    .DeclaringTypeDiscoveryAttributes,
                                symbol.Member
                                    .IsEditorBrowsableNever)),
                ];
                if (question.AcceptedRows is { } acceptedRows)
                {
                    foreach (MemberFindSemanticCandidate candidate
                        in candidates)
                    {
                        if (acceptedRows.MaterializeRows
                            && maximum is int materializedLimit
                            && matches.Count >= materializedLimit)
                        {
                            completion =
                                EcosystemFindIndexedIntegrationCompletion
                                    .RowLimitReached;
                            goto Complete;
                        }
                        MemberFindCandidateEvaluation candidateEvaluation =
                            MemberFindCandidateEvaluator.Evaluate(
                                question,
                                pattern,
                                state.Source,
                                [candidate],
                                maximumMatches: null,
                                cancellationToken);
                        state.Observe(
                            pattern.Ordinal,
                            candidateEvaluation.EvaluatedDeclarationOrders,
                            candidateEvaluation.Completion
                                is FindCandidateEvaluationCompletion.Canceled
                                    ? FindCandidateEvaluationCompletion.Canceled
                                    : FindCandidateEvaluationCompletion
                                        .MatchLimitReached);
                        if (candidateEvaluation.Completion
                            is FindCandidateEvaluationCompletion.Canceled)
                        {
                            completion =
                                EcosystemFindIndexedIntegrationCompletion
                                    .Canceled;
                            goto Complete;
                        }
                        if (candidateEvaluation.Matches.IsEmpty)
                            continue;

                        acceptedCount++;
                        if (acceptedCount >= acceptedRows.Start
                            && acceptedRows.MaterializeRows)
                        {
                            AddMatch(candidateEvaluation.Matches[0]);
                        }
                        if (acceptedRows.End is int acceptedEnd
                            && acceptedCount >= acceptedEnd)
                        {
                            completion =
                                EcosystemFindIndexedIntegrationCompletion
                                    .RowLimitReached;
                            goto Complete;
                        }
                    }
                    state.Observe(
                        pattern.Ordinal,
                        [],
                        FindCandidateEvaluationCompletion.Exhausted);
                    continue;
                }

                int? remaining = maximum is int finite
                    ? finite - matches.Count
                    : null;
                MemberFindCandidateEvaluation evaluation =
                    MemberFindCandidateEvaluator.Evaluate(
                        question,
                        pattern,
                        state.Source,
                        candidates,
                        remaining,
                        cancellationToken);
                state.Observe(
                    pattern.Ordinal,
                    evaluation.EvaluatedDeclarationOrders,
                    evaluation.Completion);
                foreach (MemberFindSemanticMatch match in evaluation.Matches)
                    AddMatch(match);

                if (evaluation.Completion
                    is FindCandidateEvaluationCompletion.Canceled)
                {
                    completion =
                        EcosystemFindIndexedIntegrationCompletion.Canceled;
                    goto Complete;
                }
                if (evaluation.Completion
                    is FindCandidateEvaluationCompletion.MatchLimitReached
                    || maximum is int cap && matches.Count >= cap)
                {
                    completion =
                        EcosystemFindIndexedIntegrationCompletion
                            .RowLimitReached;
                    goto Complete;
                }
            }
        }

    Complete:
        int totalAccepted = question.AcceptedRows is null
            ? matches.Count
            : acceptedCount;
        return new(
            question,
            matches.DrainToImmutable(),
            totalAccepted,
            published.DrainToImmutable(),
            plan.FreezeCoverage(),
            completion);

        void AddMatch(MemberFindSemanticMatch match)
        {
            matches.Add(match);
            EcosystemFindIndexedPublishedSymbolIdentity identity =
                EcosystemFindIndexedPublishedSymbolIdentity.ForMember(
                    match.Declaration.Source.Coordinate,
                    match.Declaration.Member);
            if (publishedSet.Add(identity))
                published.Add(identity);
        }
    }

    private static int? EffectiveMaximum(int? requested, int? question) =>
        (requested, question) switch
        {
            (int left, int right) => Math.Min(left, right),
            (int left, null) => left,
            (null, int right) => right,
            _ => null,
        };

    private sealed class EvaluationPlan
    {
        private EvaluationPlan(
            ImmutableArray<PrefixState> prefixes,
            ImmutableArray<SourceState> sources)
        {
            Prefixes = prefixes;
            Sources = sources;
        }

        public ImmutableArray<PrefixState> Prefixes { get; }
        public ImmutableArray<SourceState> Sources { get; }

        public static EvaluationPlan Create(
            EcosystemFindIndexedIntegrationRequest request)
        {
            var prefixes = ImmutableArray.CreateBuilder<PrefixState>();
            var sources = ImmutableArray.CreateBuilder<SourceState>();
            var demands = request.DemandedSources.ToHashSet();
            for (int artifactOrdinal = 0;
                 artifactOrdinal < request.Artifacts.Length;
                 artifactOrdinal++)
            {
                EcosystemFindIndexArtifact artifact =
                    request.Artifacts[artifactOrdinal];
                for (int prefixOrdinal = 0;
                     prefixOrdinal < artifact.Prefixes.Length;
                     prefixOrdinal++)
                {
                    var identity =
                        new EcosystemFindIndexedIntegrationSourceIdentity(
                            artifact.Ecosystem,
                            artifact.Prefixes[prefixOrdinal]);
                    if (!demands.Contains(identity))
                        continue;

                    var prefix = new PrefixState(
                        identity,
                        [
                            .. artifact.Gaps.Where(gap =>
                                gap.PrefixOrdinal == prefixOrdinal),
                        ]);
                    prefixes.Add(prefix);
                    int sourceOrdinal = 0;
                    foreach ((
                        EcosystemFindIndexSourceCoordinate coordinate,
                        int sourceIndex) in artifact.Sources
                            .Select(static (coordinate, index) =>
                                (coordinate, index))
                            .Where(item =>
                                item.coordinate.PrefixOrdinal
                                    == prefixOrdinal))
                    {
                        ImmutableArray<EcosystemFindIndexSymbol> symbols =
                        [
                            .. artifact.Symbols.Where(symbol =>
                                symbol.SourceIndex == sourceIndex
                                && IsRelevant(
                                    request.Question,
                                    symbol.Kind)),
                        ];
                        var state = new SourceState(
                            prefix,
                            coordinate,
                            CreateSource(
                                coordinate,
                                artifactOrdinal,
                                sourceOrdinal++),
                            symbols,
                            request.Question switch
                            {
                                TypeFindQuestion type =>
                                    type.Patterns.Length,
                                MemberFindQuestion member =>
                                    member.Patterns.Length,
                                _ => 0,
                            });
                        prefix.Sources.Add(state);
                        sources.Add(state);
                    }
                }
            }
            return new(prefixes.DrainToImmutable(), sources.DrainToImmutable());
        }

        public ImmutableArray<EcosystemFindIndexedPrefixCoverage>
            FreezeCoverage() =>
        [
            .. Prefixes.Select(static prefix => prefix.Freeze()),
        ];

        private static FindSourceIdentity CreateSource(
            EcosystemFindIndexSourceCoordinate coordinate,
            int contextOrder,
            int memberOrder)
        {
            var libraryIdentity =
                new ManagedMetadataIdentity.Assembly(coordinate.Assembly);
            var exact = new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    coordinate.Package.PackageId,
                    coordinate.Package.Version!),
                libraryIdentity);
            AssemblyResolutionProvenance provenance =
                AssemblyResolutionProvenance.Package(
                    coordinate.Package.PackageId,
                    coordinate.Package.Version!,
                    coordinate.TargetFramework,
                    rid: null,
                    coordinate.Asset.AssetPath);
            return new(
                exact,
                provenance,
                new FindPackageSourceRequest(
                    coordinate.TargetFramework,
                    requestedRuntimeIdentifier: null),
                contextOrder,
                memberOrder,
                coordinate.Assembly);
        }

        private static bool IsRelevant(
            FindQuestion question,
            EcosystemFindIndexSymbolKind kind) =>
            question switch
            {
                TypeFindQuestion =>
                    kind is EcosystemFindIndexSymbolKind.Type,
                MemberFindQuestion =>
                    kind is EcosystemFindIndexSymbolKind.Member,
                _ => false,
            };
    }

    private sealed class PrefixState
    {
        public PrefixState(
            EcosystemFindIndexedIntegrationSourceIdentity identity,
            ImmutableArray<EcosystemFindIndexGap> gaps)
        {
            Identity = identity;
            Gaps = gaps;
        }

        public EcosystemFindIndexedIntegrationSourceIdentity Identity { get; }
        public ImmutableArray<EcosystemFindIndexGap> Gaps { get; }
        public List<SourceState> Sources { get; } = [];

        public EcosystemFindIndexedPrefixCoverage Freeze() =>
            new(
                Identity,
                [.. Sources.Select(static source => source.Freeze())],
                Gaps)
            {
                IsExhausted = Sources.All(static source => source.IsExhausted),
            };
    }

    private sealed class SourceState
    {
        private readonly HashSet<int> _evaluatedSymbols = [];
        private readonly HashSet<int> _completedPatterns = [];
        private readonly int _patternCount;

        public SourceState(
            PrefixState prefix,
            EcosystemFindIndexSourceCoordinate coordinate,
            FindSourceIdentity source,
            ImmutableArray<EcosystemFindIndexSymbol> symbols,
            int patternCount)
        {
            Prefix = prefix;
            Coordinate = coordinate;
            Source = source;
            Symbols = symbols;
            _patternCount = patternCount;
        }

        public PrefixState Prefix { get; }
        public EcosystemFindIndexSourceCoordinate Coordinate { get; }
        public FindSourceIdentity Source { get; }
        public ImmutableArray<EcosystemFindIndexSymbol> Symbols { get; }
        public bool IsExhausted =>
            _completedPatterns.Count == _patternCount;

        public void Observe(
            int patternOrdinal,
            ImmutableArray<int> evaluatedSymbols,
            FindCandidateEvaluationCompletion completion)
        {
            foreach (int symbol in evaluatedSymbols)
                _evaluatedSymbols.Add(symbol);
            if (completion is FindCandidateEvaluationCompletion.Exhausted)
                _completedPatterns.Add(patternOrdinal);
        }

        public EcosystemFindIndexedSourceCoverage Freeze() =>
            new(
                Source,
                Symbols.Length,
                [.. _evaluatedSymbols.Order()],
                [.. _completedPatterns.Order()])
            {
                IsExhausted = IsExhausted,
            };
    }
}
