using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public sealed record TypeMemberGroupPopulationQueryPlan(
    PortableQueryIntent Intent,
    TypeMemberGroupSpelling Spelling,
    TypeMemberGroupAccessibilityFilter Accessibility,
    TypeMemberGroupReceiverFilter Receiver,
    bool IncludeHidden,
    TypeMemberGroupOrdering Ordering);

public enum TypeMemberGroupPopulationQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentNotSupported,
    TerminalMismatch,
    ResultContractMismatch,
}

public abstract record TypeMemberGroupPopulationQueryRequestResult
{
    private TypeMemberGroupPopulationQueryRequestResult()
    {
    }

    public sealed record Accepted(
        TypeMemberGroupPopulationQueryPlan Plan,
        QuerySpaceTerminalRequirement Terminal)
        : TypeMemberGroupPopulationQueryRequestResult;

    public sealed record Rejected(
        TypeMemberGroupPopulationQueryRequestRejectionKind Kind)
        : TypeMemberGroupPopulationQueryRequestResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : TypeMemberGroupPopulationQueryRequestResult;
}

public static class TypeMemberGroupPopulationQuery
{
    public const string OperationIdentity =
        "type-member-group-population";
    public const string OperationRouteIdentity =
        "type-member-group-population/default";
    public const string SubjectRole = "exact-type";
    public const string ResultGrain = "member-group";
    public const string RowSet = "declared-member-groups";
    public const string OperationProfileIdentity = "default";
    public const string VocabularyIdentity =
        "type-member-group-population/v1";
    public const string RowScopeIdentity =
        "type-member-group-population/rows";
    public const string QuerySpaceIdentity =
        "type-member-group-population/query-space/v1";
    public const string RowVocabularyIdentity =
        "type-member-group-population/rows/v1";
    public const string RowsResultContract =
        "type-member-group-population/rows-result/v1";
    public const string CountResultContract =
        "type-member-group-population/count-result/v1";
    public const string SpellingTermKey = "spelling";
    public const string AccessibilityTermKey = "accessibility";
    public const string ReceiverTermKey = "receiver";
    public const string IncludeHiddenTermKey = "include-hidden";

    private const string SpellingTermIdentity =
        "type-member-group-population.term.spelling";
    private const string AccessibilityTermIdentity =
        "type-member-group-population.term.accessibility";
    private const string ReceiverTermIdentity =
        "type-member-group-population.term.receiver";
    private const string IncludeHiddenTermIdentity =
        "type-member-group-population.term.include-hidden";
    private static readonly Lazy<QuerySpaceBinding> s_querySpace =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => s_querySpace.Value;

    public static QuerySpaceRequest CreateRequest(
        TypeMemberGroupSpelling spelling,
        TypeMemberGroupAccessibilityFilter accessibility,
        TypeMemberGroupReceiverFilter receiver,
        bool includeHidden,
        TypeMemberGroupOrdering ordering,
        QuerySpaceTerminalRequirement terminal)
    {
        if (!Enum.IsDefined(ordering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordering),
                ordering,
                "Unknown Type Member-group ordering.");
        }

        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            CreateRequestIntent(
                spelling,
                accessibility,
                receiver,
                includeHidden),
            [RowSet],
            [],
            terminal);
    }

    public static TypeMemberGroupPopulationQueryRequestResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.QuerySpace.Equals(
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new TypeMemberGroupPopulationQueryRequestResult
                .Rejected(
                    TypeMemberGroupPopulationQueryRequestRejectionKind
                        .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !request.ParticipatingRowSets[0].Equals(
                RowSet,
                StringComparison.Ordinal))
        {
            return new TypeMemberGroupPopulationQueryRequestResult
                .Rejected(
                    TypeMemberGroupPopulationQueryRequestRejectionKind
                        .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 0)
        {
            return new TypeMemberGroupPopulationQueryRequestResult
                .Rejected(
                    TypeMemberGroupPopulationQueryRequestRejectionKind
                        .RowIntentNotSupported);
        }
        if (request.Terminal
            is not QuerySpaceTerminalRequirement.Rows
                and not QuerySpaceTerminalRequirement.Count)
        {
            return new TypeMemberGroupPopulationQueryRequestResult
                .Rejected(
                    TypeMemberGroupPopulationQueryRequestRejectionKind
                        .TerminalMismatch);
        }
        string expectedResultContract =
            request.Terminal is QuerySpaceTerminalRequirement.Rows
                ? RowsResultContract
                : CountResultContract;
        if (!string.Equals(
                request.ResultContract,
                expectedResultContract,
                StringComparison.Ordinal))
        {
            return new TypeMemberGroupPopulationQueryRequestResult
                .Rejected(
                    TypeMemberGroupPopulationQueryRequestRejectionKind
                        .ResultContractMismatch);
        }

        PortableQueryResolution<TypeMemberGroupPopulationQueryPlan>
            resolution =
                OperationRegistration.Route.Resolve(
                    request.Operation,
                    cancellationToken);
        return resolution.IsResolved
            ? new TypeMemberGroupPopulationQueryRequestResult.Accepted(
                resolution.Plan,
                request.Terminal)
            : new TypeMemberGroupPopulationQueryRequestResult.IntentRejected(
                resolution.Failure);
    }

    internal static TypeMemberGroupPopulationExecutionPlan Resolve(
        TypeMemberGroupPopulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TypeMemberGroupPopulationQueryPlan resolved =
            ResolveOwnerIntent(
                CreateRequestIntent(
                    request.Spelling,
                    request.Accessibility,
                    request.Receiver,
                    request.IncludeHidden));
        QuerySpaceTerminalRequirement? terminal =
            request.Count is not null
                ? QuerySpaceTerminalRequirement.Count
                : request.Rows is not null
                    ? QuerySpaceTerminalRequirement.Rows
                    : null;

        return new(
            resolved.Spelling,
            resolved.Accessibility,
            resolved.Receiver,
            resolved.IncludeHidden,
            resolved.Ordering,
            terminal,
            request.Rows?.IncludeExactMemberCount ?? false,
            request.Composition is not null,
            request.SelectorCounts is not null);
    }

    private static TypeMemberGroupPopulationQueryPlan ResolveOwnerIntent(
        PortableQueryIntent intent)
    {
        PortableQueryResolution<TypeMemberGroupPopulationQueryPlan>
            resolution =
                OperationRegistration.Route.Resolve(intent);
        return resolution.IsResolved
            ? resolution.Plan
            : throw new InvalidOperationException(
                "An owner-issued Type Member-group intent did not resolve.");
    }

    private static PortableQueryTerm Term(string key, string value) =>
        new(key, PortableQueryOperator.Equal, value);

    private static PortableQueryIntent CreateRequestIntent(
        TypeMemberGroupSpelling spelling,
        TypeMemberGroupAccessibilityFilter accessibility,
        TypeMemberGroupReceiverFilter receiver,
        bool includeHidden)
    {
        if (spelling is TypeMemberGroupSpelling.CSharp
            && accessibility
                is TypeMemberGroupAccessibilityFilter.Public
            && receiver is TypeMemberGroupReceiverFilter.All
            && !includeHidden)
        {
            return PortableQueryIntent.Empty;
        }

        var terms = new List<PortableQueryTerm>(4);
        if (spelling is not TypeMemberGroupSpelling.CSharp)
            terms.Add(Term(SpellingTermKey, SpellingText(spelling)));
        if (accessibility
            is not TypeMemberGroupAccessibilityFilter.Public)
        {
            terms.Add(
                Term(
                    AccessibilityTermKey,
                    AccessibilityText(accessibility)));
        }
        if (receiver is not TypeMemberGroupReceiverFilter.All)
            terms.Add(Term(ReceiverTermKey, ReceiverText(receiver)));
        if (includeHidden)
            terms.Add(Term(IncludeHiddenTermKey, "true"));

        return terms.Count == 0
            ? PortableQueryIntent.Empty
            : PortableQueryIntent.Create(terms, [], [], []);
    }

    private static PortableQueryIntent CreateCanonicalIntent(
        TypeMemberGroupSpelling spelling,
        TypeMemberGroupAccessibilityFilter accessibility,
        TypeMemberGroupReceiverFilter receiver,
        bool includeHidden) =>
        PortableQueryIntent.Create(
            [
                Term(SpellingTermKey, SpellingText(spelling)),
                Term(
                    AccessibilityTermKey,
                    AccessibilityText(accessibility)),
                Term(ReceiverTermKey, ReceiverText(receiver)),
                Term(
                    IncludeHiddenTermKey,
                    includeHidden ? "true" : "false"),
            ],
            [],
            [],
            []);

    private static string SpellingText(
        TypeMemberGroupSpelling spelling) =>
        spelling switch
        {
            TypeMemberGroupSpelling.CSharp => "csharp",
            TypeMemberGroupSpelling.Metadata => "metadata",
            _ => throw new ArgumentOutOfRangeException(nameof(spelling)),
        };

    private static string AccessibilityText(
        TypeMemberGroupAccessibilityFilter accessibility) =>
        accessibility switch
        {
            TypeMemberGroupAccessibilityFilter.Public => "public",
            TypeMemberGroupAccessibilityFilter.Protected => "protected",
            TypeMemberGroupAccessibilityFilter.Internal => "internal",
            TypeMemberGroupAccessibilityFilter.Private => "private",
            TypeMemberGroupAccessibilityFilter.All => "all",
            _ => throw new ArgumentOutOfRangeException(
                nameof(accessibility)),
        };

    private static string ReceiverText(
        TypeMemberGroupReceiverFilter receiver) =>
        receiver switch
        {
            TypeMemberGroupReceiverFilter.All => "all",
            TypeMemberGroupReceiverFilter.This => "this",
            TypeMemberGroupReceiverFilter.Static => "static",
            TypeMemberGroupReceiverFilter.Extension => "extension",
            TypeMemberGroupReceiverFilter.NonExtension =>
                "non-extension",
            _ => throw new ArgumentOutOfRangeException(nameof(receiver)),
        };

    private sealed record Predicate(string Key, string Value);

    private sealed class KeyDeclaration(
        string key,
        Func<string, Predicate?> bind)
        : PortableQueryKeyDeclaration<Predicate>
    {
        public override string Key => key;

        public override string? Family => key;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Exclusive;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<Predicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            Predicate? predicate = bind(value);
            return @operator == PortableQueryOperator.Equal
                && predicate is not null
                    ? PortableQueryBinding<Predicate>.Bound(
                        $"{key}:{predicate.Value}",
                        predicate)
                    : PortableQueryBinding<Predicate>.Rejected;
        }
    }

    private sealed class Vocabulary
        : PortableQueryVocabulary<
            Predicate,
            TypeMemberGroupPopulationQueryPlan>
    {
        private static readonly IReadOnlyDictionary<
            string,
            KeyDeclaration> s_keys =
                new[]
                {
                    new KeyDeclaration(
                        SpellingTermKey,
                        BindSpelling),
                    new KeyDeclaration(
                        AccessibilityTermKey,
                        BindAccessibility),
                    new KeyDeclaration(
                        ReceiverTermKey,
                        BindReceiver),
                    new KeyDeclaration(
                        IncludeHiddenTermKey,
                        BindIncludeHidden),
                }.ToDictionary(
                    declaration => declaration.Key,
                    StringComparer.Ordinal);

        public override string Identity => VocabularyIdentity;

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<Predicate>? declaration)
        {
            bool found = s_keys.TryGetValue(
                key,
                out KeyDeclaration? value);
            declaration = value;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<Predicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) => false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override bool CollapsesDuplicateBindings => true;

        public override bool AreTermsCompatible(
            PortableQueryResolvedTerm<Predicate> first,
            PortableQueryResolvedTerm<Predicate> second) =>
            !first.Predicate.Key.Equals(
                second.Predicate.Key,
                StringComparison.Ordinal)
            || first.Predicate == second.Predicate;

        public override TypeMemberGroupPopulationQueryPlan CreatePlan(
            PortableQueryResolvedIntent<Predicate> resolved)
        {
            TypeMemberGroupSpelling spelling =
                TypeMemberGroupSpelling.CSharp;
            TypeMemberGroupAccessibilityFilter accessibility =
                TypeMemberGroupAccessibilityFilter.Public;
            TypeMemberGroupReceiverFilter receiver =
                TypeMemberGroupReceiverFilter.All;
            bool includeHidden = false;
            foreach (PortableQueryResolvedTerm<Predicate> term
                in resolved.Terms)
            {
                switch (term.Predicate.Key)
                {
                    case SpellingTermKey:
                        spelling =
                            ParseSpelling(term.Predicate.Value);
                        break;
                    case AccessibilityTermKey:
                        accessibility =
                            ParseAccessibility(term.Predicate.Value);
                        break;
                    case ReceiverTermKey:
                        receiver =
                            ParseReceiver(term.Predicate.Value);
                        break;
                    case IncludeHiddenTermKey:
                        includeHidden =
                            bool.Parse(term.Predicate.Value);
                        break;
                }
            }

            return new(
                CreateCanonicalIntent(
                    spelling,
                    accessibility,
                    receiver,
                    includeHidden),
                spelling,
                accessibility,
                receiver,
                includeHidden,
                TypeMemberGroupOrdering.Metadata);
        }
    }

    private static QuerySpaceBinding CreateQuerySpace()
    {
        var rowScope =
            new QuerySpaceRowScopeBinding<TypeMemberGroupShape>(
                new(
                    RowScopeIdentity,
                    RowVocabularyIdentity,
                    [RowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<TypeMemberGroupShape>.Create(
                    RowQueryVocabularyIdentity.Create(),
                    [],
                    []));
        return QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRegistration.Route,
            [rowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: true,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
            ]);
    }

    private static Predicate? BindSpelling(string value) =>
        value is "csharp" or "metadata"
            ? new(SpellingTermKey, value)
            : null;

    private static Predicate? BindAccessibility(string value) =>
        value is "public" or "protected" or "internal" or "private"
            or "all"
            ? new(AccessibilityTermKey, value)
            : null;

    private static Predicate? BindReceiver(string value) =>
        value is "all" or "this" or "static" or "extension"
            or "non-extension"
            ? new(ReceiverTermKey, value)
            : null;

    private static Predicate? BindIncludeHidden(string value) =>
        bool.TryParse(value, out bool parsed)
            ? new(IncludeHiddenTermKey, parsed.ToString())
            : null;

    private static TypeMemberGroupSpelling ParseSpelling(
        string value) =>
        value switch
        {
            "csharp" => TypeMemberGroupSpelling.CSharp,
            "metadata" => TypeMemberGroupSpelling.Metadata,
            _ => throw new InvalidOperationException(
                "Type Member-group QuerySpace resolved an unknown spelling."),
        };

    private static TypeMemberGroupAccessibilityFilter ParseAccessibility(
        string value) =>
        value switch
        {
            "public" => TypeMemberGroupAccessibilityFilter.Public,
            "protected" =>
                TypeMemberGroupAccessibilityFilter.Protected,
            "internal" => TypeMemberGroupAccessibilityFilter.Internal,
            "private" => TypeMemberGroupAccessibilityFilter.Private,
            "all" => TypeMemberGroupAccessibilityFilter.All,
            _ => throw new InvalidOperationException(
                "Type Member-group QuerySpace resolved an unknown accessibility."),
        };

    private static TypeMemberGroupReceiverFilter ParseReceiver(
        string value) =>
        value switch
        {
            "all" => TypeMemberGroupReceiverFilter.All,
            "this" => TypeMemberGroupReceiverFilter.This,
            "static" => TypeMemberGroupReceiverFilter.Static,
            "extension" => TypeMemberGroupReceiverFilter.Extension,
            "non-extension" =>
                TypeMemberGroupReceiverFilter.NonExtension,
            _ => throw new InvalidOperationException(
                "Type Member-group QuerySpace resolved an unknown receiver."),
        };

    private static class OperationRegistration
    {
        private static readonly Vocabulary s_vocabulary = new();

        internal static readonly QueryOperationDefinition<
            Predicate,
            TypeMemberGroupPopulationQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            Predicate,
            TypeMemberGroupPopulationQueryPlan> Route =
                QueryOperationRoute<
                    Predicate,
                    TypeMemberGroupPopulationQueryPlan>.Create(
                    OperationRouteIdentity,
                    Definition,
                    SubjectRole,
                    ResultGrain,
                    [RowSet],
                    OperationProfileIdentity,
                    [],
                    []);

        private static QueryOperationDefinition<
            Predicate,
            TypeMemberGroupPopulationQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [SubjectRole],
                [ResultGrain],
                [RowSet]);
            QueryOperationTermBinding[] terms =
            [
                new(
                    SpellingTermIdentity,
                    SpellingTermKey,
                    QueryOperationTermRole.SubjectQualification,
                    applicability,
                    new(
                        "Spelling",
                        "declaration spelling",
                        ["csharp", "metadata"],
                        "Selects C# declaration composition or physical metadata records."),
                    []),
                new(
                    AccessibilityTermIdentity,
                    AccessibilityTermKey,
                    QueryOperationTermRole.SubjectQualification,
                    applicability,
                    new(
                        "Accessibility",
                        "accessibility",
                        ["public", "protected", "internal",
                            "private", "all"],
                        "Selects exact declarations by effective accessibility."),
                    []),
                new(
                    ReceiverTermIdentity,
                    ReceiverTermKey,
                    QueryOperationTermRole.SubjectQualification,
                    applicability,
                    new(
                        "Receiver",
                        "receiver kind",
                        ["all", "this", "static", "extension",
                            "non-extension"],
                        "Selects exact declarations by receiver form before grouping."),
                    []),
                new(
                    IncludeHiddenTermIdentity,
                    IncludeHiddenTermKey,
                    QueryOperationTermRole.SubjectQualification,
                    applicability,
                    new(
                        "Include hidden",
                        "boolean",
                        ["false", "true"],
                        "Includes hidden declarations when true."),
                    []),
            ];
            return QueryOperationDefinition<
                Predicate,
                TypeMemberGroupPopulationQueryPlan>.Create(
                    OperationIdentity,
                    s_vocabulary,
                    [SubjectRole],
                    [ResultGrain],
                    [RowSet],
                    terms,
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [SpellingTermIdentity,
                                AccessibilityTermIdentity,
                                ReceiverTermIdentity,
                                IncludeHiddenTermIdentity],
                            []),
                    ]);
        }
    }
}

internal sealed record TypeMemberGroupPopulationExecutionPlan(
    TypeMemberGroupSpelling Spelling,
    TypeMemberGroupAccessibilityFilter Accessibility,
    TypeMemberGroupReceiverFilter Receiver,
    bool IncludeHidden,
    TypeMemberGroupOrdering Ordering,
    QuerySpaceTerminalRequirement? Terminal,
    bool IncludesExactMemberCount,
    bool IncludesComposition,
    bool IncludesSelectorCounts);
