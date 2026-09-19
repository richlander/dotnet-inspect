using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using DotnetInspector.PortableQueries;
using DotnetInspector.QueryOperations;
using DotnetInspector.RowSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record LibraryQueryTermDescriptor(
    string Key,
    string Label,
    string Summary,
    string ValueKind,
    string ExampleValue);

public sealed record LibraryQueryRegisteredTerm(
    LibraryQueryTermDescriptor Descriptor,
    ImmutableArray<PortableQueryOperator> Operators);

public sealed record LibraryQueryPlan(
    PortableQueryIntent Intent,
    ImmutableArray<string> RequiredReferences,
    int MaximumCandidates);

public abstract record LibraryQueryPlanResult
{
    private LibraryQueryPlanResult()
    {
    }

    public sealed record Accepted(LibraryQueryPlan Plan)
        : LibraryQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : LibraryQueryPlanResult;
}

public abstract record LibraryQueryPopulationOccurrence
{
    private LibraryQueryPopulationOccurrence(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        Ordinal = ordinal;
    }

    public int Ordinal { get; }

    public sealed record Available : LibraryQueryPopulationOccurrence
    {
        public Available(
            int ordinal,
            AssemblyContextParticipant participant)
            : base(ordinal)
        {
            ArgumentNullException.ThrowIfNull(participant);
            Participant = participant;
        }

        public AssemblyContextParticipant Participant { get; }
    }

    public sealed record Unavailable : LibraryQueryPopulationOccurrence
    {
        public Unavailable(
            int ordinal,
            string source,
            CandidateOpenFailure failure)
            : base(ordinal)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source);
            ArgumentNullException.ThrowIfNull(failure);
            Source = source;
            Failure = failure;
        }

        public string Source { get; }
        public CandidateOpenFailure Failure { get; }
    }
}

public sealed class LibraryQueryPopulation
{
    public LibraryQueryPopulation(
        AssemblyContextGroup? group,
        ImmutableArray<LibraryQueryPopulationOccurrence> occurrences)
    {
        if (occurrences.IsDefault)
            throw new ArgumentException("Occurrences must be initialized.", nameof(occurrences));

        var participants = new HashSet<AssemblyContextParticipant>(
            group?.Participants ?? [],
            ReferenceEqualityComparer.Instance);
        var seenOrdinals = new HashSet<int>();
        foreach (LibraryQueryPopulationOccurrence occurrence in occurrences)
        {
            if (!seenOrdinals.Add(occurrence.Ordinal))
            {
                throw new ArgumentException(
                    $"Occurrence ordinal {occurrence.Ordinal} is duplicated.",
                    nameof(occurrences));
            }

            if (occurrence is LibraryQueryPopulationOccurrence.Available available
                && !participants.Contains(available.Participant))
            {
                throw new ArgumentException(
                    "Every available occurrence requires and must belong to the supplied group.",
                    nameof(occurrences));
            }
        }

        Group = group;
        Occurrences =
        [
            .. occurrences.OrderBy(occurrence => occurrence.Ordinal),
        ];
    }

    public AssemblyContextGroup? Group { get; }
    public ImmutableArray<LibraryQueryPopulationOccurrence> Occurrences { get; }

    public static LibraryQueryPopulation FromGroup(AssemblyContextGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new(
            group,
            [
                .. group.Participants.Select((participant, ordinal) =>
                    (LibraryQueryPopulationOccurrence)new
                        LibraryQueryPopulationOccurrence.Available(
                            ordinal,
                            participant)),
            ]);
    }
}

public sealed record LibraryQueryMatch(
    int Occurrence,
    AssemblyReferenceIdentity Library,
    string? Source,
    string Provenance,
    ImmutableArray<string> Answers);

public sealed record LibraryQueryFailure(
    int Occurrence,
    string Source,
    CandidateOpenFailureKind Kind,
    string Message);

public enum LibraryQueryCompletionKind
{
    Complete,
    CandidateLimitReached,
    EvaluationFailures,
    CandidateLimitReachedWithEvaluationFailures,
}

public sealed record LibraryQuerySummary(
    int Population,
    int Evaluated,
    int Matches,
    int Failures,
    int CandidateLimit,
    LibraryQueryCompletionKind Completion)
{
    public bool IsExact => Completion == LibraryQueryCompletionKind.Complete;
}

public sealed record LibraryQueryDocument(
    ImmutableArray<LibraryQueryMatch> Results,
    ImmutableArray<LibraryQueryFailure> Failures,
    LibraryQuerySummary Summary);

public static class LibraryQuery
{
    public const int DefaultMaximumCandidates = 256;
    public const string OperationIdentity = "library-query";
    public const string OperationRouteIdentity = "library-query/default";
    public const string OperationSubjectRole = "explicit-library-population";
    public const string OperationResultGrain = "library";
    public const string OperationLibrariesRowSet = "libraries";
    public const string OperationProfileIdentity = "default";
    public const string ReferencesTermKey = "references";

    private static readonly LibraryQueryTermDescriptor ReferencesTerm =
        new(
            ReferencesTermKey,
            "references assembly",
            "Matches a direct AssemblyRef simple name on each Library occurrence.",
            "assembly simple name",
            "System.Text.Json");

    private static readonly IReadOnlyDictionary<
        string,
        LibraryQueryTermDescriptor> TermsByKey =
            new Dictionary<string, LibraryQueryTermDescriptor>(
                StringComparer.Ordinal)
            {
                [ReferencesTerm.Key] = ReferencesTerm,
            };

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static ImmutableArray<LibraryQueryRegisteredTerm> RegisteredTerms =>
        OperationRegistration.RegisteredTerms;

    public static PortableQueryIntent CreateIntent(
        IEnumerable<string> requiredReferences)
    {
        ArgumentNullException.ThrowIfNull(requiredReferences);
        return PortableQueryIntent.Create(
            [
                .. requiredReferences
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(reference =>
                    new PortableQueryTerm(
                        ReferencesTermKey,
                        PortableQueryOperator.Equal,
                        reference)),
            ],
            [],
            [],
            []);
    }

    public static LibraryQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        int maximumCandidates = DefaultMaximumCandidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (maximumCandidates <= 0
            || maximumCandidates > DefaultMaximumCandidates)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCandidates),
                maximumCandidates,
                $"Library Query admits 1-{DefaultMaximumCandidates} candidates.");
        }

        intent = CollapseReferenceCaseVariants(intent);
        PortableQueryResolution<LibraryQueryPlan> resolution =
            OperationRegistration.Route.Resolve(intent, cancellationToken);
        return resolution.IsResolved
            ? new LibraryQueryPlanResult.Accepted(
                resolution.Plan with
                {
                    MaximumCandidates = maximumCandidates,
                })
            : new LibraryQueryPlanResult.Rejected(resolution.Failure);
    }

    private static PortableQueryIntent CollapseReferenceCaseVariants(
        PortableQueryIntent intent)
    {
        var seenReferences = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        PortableQueryTerm[] terms =
        [
            .. intent.Terms.Where(term =>
                !term.Key.Equals(
                    ReferencesTermKey,
                    StringComparison.Ordinal)
                || term.Operator != PortableQueryOperator.Equal
                || seenReferences.Add(term.Value)),
        ];
        return terms.Length == intent.Terms.Count
            ? intent
            : PortableQueryIntent.Create(
                terms,
                intent.Bounds,
                intent.Stages,
                intent.Order);
    }

    public static LibraryQueryDocument Execute(
        LibraryQueryPopulation population,
        LibraryQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(plan);

        var results = ImmutableArray.CreateBuilder<LibraryQueryMatch>();
        var failures = ImmutableArray.CreateBuilder<LibraryQueryFailure>();
        int evaluated = 0;
        foreach (LibraryQueryPopulationOccurrence occurrence
            in population.Occurrences.Take(plan.MaximumCandidates))
        {
            evaluated++;
            switch (occurrence)
            {
                case LibraryQueryPopulationOccurrence.Unavailable unavailable:
                    failures.Add(
                        new(
                            unavailable.Ordinal,
                            unavailable.Source,
                            unavailable.Failure.Kind,
                            unavailable.Failure.Detail));
                    break;

                case LibraryQueryPopulationOccurrence.Available available:
                    EvaluateAvailable(
                        population.Group
                            ?? throw new InvalidOperationException(
                                "An available Library Query occurrence requires a group."),
                        available,
                        plan,
                        results,
                        failures);
                    break;

                default:
                    throw new InvalidOperationException(
                        "Unknown Library Query population occurrence.");
            }
        }

        bool limitReached =
            population.Occurrences.Length > plan.MaximumCandidates;
        LibraryQueryCompletionKind completion =
            (limitReached, failures.Count > 0) switch
            {
                (false, false) => LibraryQueryCompletionKind.Complete,
                (true, false) =>
                    LibraryQueryCompletionKind.CandidateLimitReached,
                (false, true) =>
                    LibraryQueryCompletionKind.EvaluationFailures,
                (true, true) =>
                    LibraryQueryCompletionKind
                        .CandidateLimitReachedWithEvaluationFailures,
            };
        return new(
            results.ToImmutable(),
            failures.ToImmutable(),
            new(
                population.Occurrences.Length,
                evaluated,
                results.Count,
                failures.Count,
                plan.MaximumCandidates,
                completion));
    }

    private static void EvaluateAvailable(
        AssemblyContextGroup group,
        LibraryQueryPopulationOccurrence.Available occurrence,
        LibraryQueryPlan plan,
        ImmutableArray<LibraryQueryMatch>.Builder results,
        ImmutableArray<LibraryQueryFailure>.Builder failures)
    {
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>> entry =
            AssemblyContextReferencesQuery.ExecuteParticipant(
                group,
                occurrence.Participant);
        switch (entry)
        {
            case AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Available available:
                if (plan.RequiredReferences.All(required =>
                    available.Value.Any(reference =>
                        reference.Name.Equals(
                            required,
                            StringComparison.OrdinalIgnoreCase))))
                {
                    ResolvedAssemblyReference assembly =
                        occurrence.Participant.Assembly;
                    results.Add(
                        new(
                            occurrence.Ordinal,
                            assembly.Identity,
                            assembly.Path ?? assembly.AssetFileName,
                            ProvenanceKind(assembly.Provenance),
                            plan.RequiredReferences));
                }
                break;

            case AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected:
                failures.Add(
                    new(
                        occurrence.Ordinal,
                        Source(occurrence.Participant.Assembly),
                        rejected.Failure.Kind,
                        rejected.Failure.Detail));
                break;

            case AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Failed failed:
                failures.Add(
                    new(
                        occurrence.Ordinal,
                        Source(occurrence.Participant.Assembly),
                        CandidateOpenFailureKind.InvalidImage,
                        failed.Error.Message));
                break;

            default:
                throw new InvalidOperationException(
                    "Unknown assembly-reference query result.");
        }
    }

    private static string Source(ResolvedAssemblyReference assembly) =>
        assembly.Path
        ?? assembly.AssetFileName
        ?? assembly.Identity.Name;

    private static string ProvenanceKind(
        AssemblyResolutionProvenance provenance) =>
        provenance switch
        {
            AssemblyResolutionProvenance.PackageAsset => "package",
            AssemblyResolutionProvenance.PlatformAsset => "platform",
            AssemblyResolutionProvenance.ProjectAsset => "project",
            AssemblyResolutionProvenance.LocalAsset => "local",
            AssemblyResolutionProvenance.EmbeddedAsset => "embedded",
            AssemblyResolutionProvenance.DesignatedAsset => "designated",
            _ => throw new InvalidOperationException(
                "Unknown assembly resolution provenance."),
        };

    private sealed record LibraryQueryPredicate(string Reference);

    private sealed class ReferencesKeyDeclaration
        : PortableQueryKeyDeclaration<LibraryQueryPredicate>
    {
        public override string Key => ReferencesTermKey;
        public override string? Family => ReferencesTermKey;
        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Combining;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<LibraryQueryPredicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            if (@operator != PortableQueryOperator.Equal
                || string.IsNullOrWhiteSpace(value)
                || value.AsSpan().Trim().Length != value.Length
                || value.Contains(',')
                || value.Contains('/')
                || value.Contains('\\')
                || !RealizedMemberCoordinate.IsAssemblySimpleName(value))
            {
                return PortableQueryBinding<LibraryQueryPredicate>.Rejected;
            }

            return PortableQueryBinding<LibraryQueryPredicate>.Bound(
                $"references:{value.ToUpperInvariant()}",
                new(value));
        }
    }

    private sealed class LibraryQueryVocabulary
        : PortableQueryVocabulary<LibraryQueryPredicate, LibraryQueryPlan>
    {
        private static readonly ReferencesKeyDeclaration References = new();

        public override string Identity => "library-query/v1";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<LibraryQueryPredicate>? declaration)
        {
            bool found = key.Equals(
                ReferencesTermKey,
                StringComparison.Ordinal);
            declaration = found ? References : null;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                LibraryQueryPredicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override LibraryQueryPlan CreatePlan(
            PortableQueryResolvedIntent<LibraryQueryPredicate> resolved) =>
            new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [],
                    [],
                    []),
                [
                    .. resolved.Terms.Select(term =>
                        term.Predicate.Reference),
                ],
                DefaultMaximumCandidates);
    }

    private static class OperationRegistration
    {
        private static readonly LibraryQueryVocabulary Vocabulary = new();

        internal static readonly QueryOperationDefinition<
            LibraryQueryPredicate,
            LibraryQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            LibraryQueryPredicate,
            LibraryQueryPlan> Route =
                QueryOperationRoute<
                    LibraryQueryPredicate,
                    LibraryQueryPlan>.Create(
                    OperationRouteIdentity,
                    Definition,
                    OperationSubjectRole,
                    OperationResultGrain,
                    [OperationLibrariesRowSet],
                    OperationProfileIdentity,
                    [],
                    []);

        internal static readonly ImmutableArray<
            LibraryQueryRegisteredTerm> RegisteredTerms =
        [
            .. Route.Capabilities.Terms.Select(capability =>
                new LibraryQueryRegisteredTerm(
                    TermsByKey[capability.Binding.Key],
                    [.. capability.Operators])),
        ];

        private static QueryOperationDefinition<
            LibraryQueryPredicate,
            LibraryQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var term = new QueryOperationTermBinding(
                "library-query.term.references",
                ReferencesTermKey,
                QueryOperationTermRole.SubjectQualification,
                applicability,
                new QueryOperationTermDescription(
                    ReferencesTerm.Label,
                    ReferencesTerm.ValueKind,
                    [],
                    ReferencesTerm.Summary),
                []);

            return QueryOperationDefinition<
                LibraryQueryPredicate,
                LibraryQueryPlan>.Create(
                    OperationIdentity,
                    Vocabulary,
                    [OperationSubjectRole],
                    [OperationResultGrain],
                    [OperationLibrariesRowSet],
                    [term],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [term.Identity],
                            []),
                    ]);
        }
    }
}
