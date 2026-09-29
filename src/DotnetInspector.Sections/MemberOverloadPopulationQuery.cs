using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public sealed record MemberOverloadPopulationQueryPlan(
    PortableQueryIntent Intent,
    MemberOverloadAccessibilityFilter Accessibility,
    MemberOverloadReceiverFilter Receiver,
    bool IncludeHidden,
    MemberOverloadOrdering Ordering);

public enum MemberOverloadPopulationQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentNotSupported,
    TerminalMismatch,
    ResultContractMismatch,
}

public abstract record MemberOverloadPopulationQueryRequestResult
{
    private MemberOverloadPopulationQueryRequestResult()
    {
    }

    public sealed record Accepted(
        MemberOverloadPopulationQueryPlan Plan,
        QuerySpaceTerminalRequirement Terminal)
        : MemberOverloadPopulationQueryRequestResult;

    public sealed record Rejected(
        MemberOverloadPopulationQueryRequestRejectionKind Kind)
        : MemberOverloadPopulationQueryRequestResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : MemberOverloadPopulationQueryRequestResult;
}

public static class MemberOverloadPopulationQuery
{
    public const string OperationIdentity = "member-overload-population";
    public const string OperationRouteIdentity =
        "member-overload-population/default";
    public const string SubjectRole = "member-group";
    public const string ResultGrain = "exact-member";
    public const string RowSet = "member-overloads";
    public const string OperationProfileIdentity = "default";
    public const string VocabularyIdentity =
        "member-overload-population/v1";
    public const string RowScopeIdentity =
        "member-overload-population/rows";
    public const string QuerySpaceIdentity =
        "member-overload-population/query-space/v1";
    public const string RowVocabularyIdentity =
        "member-overload-population/rows/v1";
    public const string RowsResultContract =
        "member-overload-population/rows-result/v1";
    public const string CountResultContract =
        "member-overload-population/count/v1";
    public const string AccessibilityTermKey = "accessibility";
    public const string ReceiverTermKey = "receiver";
    public const string IncludeHiddenTermKey = "include-hidden";

    private const string AccessibilityTermIdentity =
        "member-overload-population.term.accessibility";
    private const string ReceiverTermIdentity =
        "member-overload-population.term.receiver";
    private const string IncludeHiddenTermIdentity =
        "member-overload-population.term.include-hidden";
    private static readonly Lazy<QuerySpaceBinding> s_querySpace =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => s_querySpace.Value;

    public static QuerySpaceRequest CreateRequest(
        MemberOverloadAccessibilityFilter accessibility,
        MemberOverloadReceiverFilter receiver,
        bool includeHidden,
        MemberOverloadOrdering ordering,
        QuerySpaceTerminalRequirement terminal)
    {
        if (!Enum.IsDefined(ordering))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ordering),
                ordering,
                "Unknown exact-Member ordering.");
        }

        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            CreateRequestIntent(
                accessibility,
                receiver,
                includeHidden),
            [RowSet],
            [],
            terminal);
    }

    public static MemberOverloadPopulationQueryRequestResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.QuerySpace.Equals(
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new MemberOverloadPopulationQueryRequestResult
                .Rejected(
                    MemberOverloadPopulationQueryRequestRejectionKind
                        .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !request.ParticipatingRowSets[0].Equals(
                RowSet,
                StringComparison.Ordinal))
        {
            return new MemberOverloadPopulationQueryRequestResult
                .Rejected(
                    MemberOverloadPopulationQueryRequestRejectionKind
                        .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 0)
        {
            return new MemberOverloadPopulationQueryRequestResult
                .Rejected(
                    MemberOverloadPopulationQueryRequestRejectionKind
                        .RowIntentNotSupported);
        }
        if (request.Terminal
            is not QuerySpaceTerminalRequirement.Rows
                and not QuerySpaceTerminalRequirement.Count)
        {
            return new MemberOverloadPopulationQueryRequestResult
                .Rejected(
                    MemberOverloadPopulationQueryRequestRejectionKind
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
            return new MemberOverloadPopulationQueryRequestResult
                .Rejected(
                    MemberOverloadPopulationQueryRequestRejectionKind
                        .ResultContractMismatch);
        }

        PortableQueryResolution<MemberOverloadPopulationQueryPlan>
            resolution =
                OperationRegistration.Route.Resolve(
                    request.Operation,
                    cancellationToken);
        return resolution.IsResolved
            ? new MemberOverloadPopulationQueryRequestResult.Accepted(
                resolution.Plan,
                request.Terminal)
            : new MemberOverloadPopulationQueryRequestResult.IntentRejected(
                resolution.Failure);
    }

    internal static MemberOverloadPopulationExecutionPlan Resolve(
        MemberOverloadPopulationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool includesCount = request.Count is not null;
        bool includesRows = request.Rows is not null;
        if (!includesCount && !includesRows)
        {
            throw new InvalidOperationException(
                "An exact-Member population requires a terminal.");
        }

        MemberOverloadPopulationQueryPlan resolved =
            ResolveOwnerIntent(
                CreateRequestIntent(
                    request.Accessibility,
                    request.Receiver,
                    request.IncludeHidden));

        return new(
            resolved.Accessibility,
            resolved.Receiver,
            resolved.IncludeHidden,
            resolved.Ordering,
            includesCount,
            includesRows);
    }

    private static MemberOverloadPopulationQueryPlan ResolveOwnerIntent(
        PortableQueryIntent intent)
    {
        PortableQueryResolution<MemberOverloadPopulationQueryPlan>
            resolution =
                OperationRegistration.Route.Resolve(intent);
        return resolution.IsResolved
            ? resolution.Plan
            : throw new InvalidOperationException(
                "An owner-issued exact-Member intent did not resolve.");
    }

    private static PortableQueryTerm Term(string key, string value) =>
        new(key, PortableQueryOperator.Equal, value);

    private static PortableQueryIntent CreateRequestIntent(
        MemberOverloadAccessibilityFilter accessibility,
        MemberOverloadReceiverFilter receiver,
        bool includeHidden)
    {
        if (accessibility is MemberOverloadAccessibilityFilter.Public
            && receiver is MemberOverloadReceiverFilter.All
            && !includeHidden)
        {
            return PortableQueryIntent.Empty;
        }

        var terms = new List<PortableQueryTerm>(3);
        if (accessibility is not MemberOverloadAccessibilityFilter.Public)
        {
            terms.Add(
                Term(
                    AccessibilityTermKey,
                    AccessibilityText(accessibility)));
        }
        if (receiver is not MemberOverloadReceiverFilter.All)
        {
            terms.Add(
                Term(
                    ReceiverTermKey,
                    ReceiverText(receiver)));
        }
        if (includeHidden)
        {
            terms.Add(
                Term(
                    IncludeHiddenTermKey,
                    "true"));
        }

        return terms.Count == 0
            ? PortableQueryIntent.Empty
            : PortableQueryIntent.Create(terms, [], [], []);
    }

    private static PortableQueryIntent CreateCanonicalIntent(
        MemberOverloadAccessibilityFilter accessibility,
        MemberOverloadReceiverFilter receiver,
        bool includeHidden) =>
        PortableQueryIntent.Create(
            [
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

    private static string AccessibilityText(
        MemberOverloadAccessibilityFilter accessibility) =>
        accessibility switch
        {
            MemberOverloadAccessibilityFilter.Public => "public",
            MemberOverloadAccessibilityFilter.Protected => "protected",
            MemberOverloadAccessibilityFilter.Internal => "internal",
            MemberOverloadAccessibilityFilter.Private => "private",
            MemberOverloadAccessibilityFilter.All => "all",
            _ => throw new ArgumentOutOfRangeException(
                nameof(accessibility)),
        };

    private static string ReceiverText(
        MemberOverloadReceiverFilter receiver) =>
        receiver switch
        {
            MemberOverloadReceiverFilter.All => "all",
            MemberOverloadReceiverFilter.This => "this",
            MemberOverloadReceiverFilter.Static => "static",
            MemberOverloadReceiverFilter.Extension => "extension",
            _ => throw new ArgumentOutOfRangeException(nameof(receiver)),
        };

    private sealed record Predicate(
        string Key,
        string Value);

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
            MemberOverloadPopulationQueryPlan>
    {
        private static readonly IReadOnlyDictionary<
            string,
            KeyDeclaration> s_keys =
                new[]
                {
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

        public override MemberOverloadPopulationQueryPlan CreatePlan(
            PortableQueryResolvedIntent<Predicate> resolved)
        {
            MemberOverloadAccessibilityFilter accessibility =
                MemberOverloadAccessibilityFilter.Public;
            MemberOverloadReceiverFilter receiver =
                MemberOverloadReceiverFilter.All;
            bool includeHidden = false;
            foreach (PortableQueryResolvedTerm<Predicate> term
                in resolved.Terms)
            {
                switch (term.Predicate.Key)
                {
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
                    accessibility,
                    receiver,
                    includeHidden),
                accessibility,
                receiver,
                includeHidden,
                MemberOverloadOrdering.Metadata);
        }
    }

    private static QuerySpaceBinding CreateQuerySpace()
    {
        var rowScope =
            new QuerySpaceRowScopeBinding<MemberOverloadShape>(
                new(
                    RowScopeIdentity,
                    RowVocabularyIdentity,
                    [RowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<MemberOverloadShape>.Create(
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

    private static Predicate? BindAccessibility(string value) =>
        value is "public" or "protected" or "internal" or "private"
            or "all"
            ? new(AccessibilityTermKey, value)
            : null;

    private static Predicate? BindReceiver(string value) =>
        value is "all" or "this" or "static" or "extension"
            ? new(ReceiverTermKey, value)
            : null;

    private static Predicate? BindIncludeHidden(string value) =>
        bool.TryParse(value, out bool parsed)
            ? new(IncludeHiddenTermKey, parsed.ToString())
            : null;

    private static MemberOverloadAccessibilityFilter ParseAccessibility(
        string value) =>
        value switch
        {
            "public" => MemberOverloadAccessibilityFilter.Public,
            "protected" => MemberOverloadAccessibilityFilter.Protected,
            "internal" => MemberOverloadAccessibilityFilter.Internal,
            "private" => MemberOverloadAccessibilityFilter.Private,
            "all" => MemberOverloadAccessibilityFilter.All,
            _ => throw new InvalidOperationException(
                "Exact-Member QuerySpace resolved an unknown accessibility."),
        };

    private static MemberOverloadReceiverFilter ParseReceiver(
        string value) =>
        value switch
        {
            "all" => MemberOverloadReceiverFilter.All,
            "this" => MemberOverloadReceiverFilter.This,
            "static" => MemberOverloadReceiverFilter.Static,
            "extension" => MemberOverloadReceiverFilter.Extension,
            _ => throw new InvalidOperationException(
                "Exact-Member QuerySpace resolved an unknown receiver."),
        };

    private static class OperationRegistration
    {
        private static readonly Vocabulary s_vocabulary = new();

        internal static readonly QueryOperationDefinition<
            Predicate,
            MemberOverloadPopulationQueryPlan> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            Predicate,
            MemberOverloadPopulationQueryPlan> Route =
                QueryOperationRoute<
                    Predicate,
                    MemberOverloadPopulationQueryPlan>.Create(
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
            MemberOverloadPopulationQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [SubjectRole],
                [ResultGrain],
                [RowSet]);
            QueryOperationTermBinding[] terms =
            [
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
                        "Selects exact Members by declared accessibility."),
                    []),
                new(
                    ReceiverTermIdentity,
                    ReceiverTermKey,
                    QueryOperationTermRole.SubjectQualification,
                    applicability,
                    new(
                        "Receiver",
                        "receiver kind",
                        ["all", "this", "static", "extension"],
                        "Selects exact Members by this, static, or extension receiver kind."),
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
                        "Includes hidden exact Members when true."),
                    []),
            ];
            return QueryOperationDefinition<
                Predicate,
                MemberOverloadPopulationQueryPlan>.Create(
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
                            [AccessibilityTermIdentity,
                                ReceiverTermIdentity,
                                IncludeHiddenTermIdentity],
                            []),
                    ]);
        }
    }
}

internal sealed record MemberOverloadPopulationExecutionPlan(
    MemberOverloadAccessibilityFilter Accessibility,
    MemberOverloadReceiverFilter Receiver,
    bool IncludeHidden,
    MemberOverloadOrdering Ordering,
    bool IncludesCount,
    bool IncludesRows);
