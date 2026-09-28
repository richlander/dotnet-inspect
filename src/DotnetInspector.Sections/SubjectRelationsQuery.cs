using System.Diagnostics.CodeAnalysis;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using QuerySpace;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public sealed record SubjectRelationsQueryPlan(
    PortableQueryIntent Intent,
    SubjectRelationPopulationSelection Selection);

public abstract record SubjectRelationsQueryPlanResult
{
    private SubjectRelationsQueryPlanResult()
    {
    }

    public sealed record Accepted(SubjectRelationsQueryPlan Plan)
        : SubjectRelationsQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : SubjectRelationsQueryPlanResult;
}

/// <summary>
/// Executable QuerySpace registration for exact-subject relation routes.
/// </summary>
public static class SubjectRelationsQuery
{
    public const string OperationIdentity = "subject-relations";
    public const string PackageRouteIdentity =
        "subject-relations/package";
    public const string LibraryRouteIdentity =
        "subject-relations/library";
    public const string TypeRouteIdentity =
        "subject-relations/type";
    public const string MemberRouteIdentity =
        "subject-relations/member";
    public const string PackageSubjectRole = "exact-package";
    public const string LibrarySubjectRole = "exact-library";
    public const string TypeSubjectRole = "exact-type";
    public const string MemberSubjectRole = "exact-member";
    public const string ResultGrain = "logical-relation";
    public const string RelationsRowSet = "relations";
    public const string ProfileIdentity = "default";
    public const string VocabularyIdentity = "subject-relations/v1";
    public const string FormTermKey = "form";
    public const string RelationTermKey = "relation";
    public const string DirectionTermKey = "direction";
    public const string EvidenceTermKey = "evidence";
    public const string EcosystemTermKey = "ecosystem";
    public const string ConceptTermKey = "concept";

    private static readonly QueryVocabulary Vocabulary = new();

    public static IQueryOperationRoute Route(
        SubjectRelationsRouteKind kind) =>
        RouteCore(kind);

    public static SubjectRelationsQueryPlanResult ResolveIntent(
        SubjectRelationsRouteKind kind,
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<SubjectRelationsQueryPlan> resolution =
            RouteCore(kind).Resolve(intent, cancellationToken);
        return resolution.IsResolved
            ? new SubjectRelationsQueryPlanResult.Accepted(resolution.Plan)
            : new SubjectRelationsQueryPlanResult.Rejected(
                resolution.Failure);
    }

    private static QueryOperationRoute<
        QueryPredicate,
        SubjectRelationsQueryPlan> RouteCore(
        SubjectRelationsRouteKind kind) =>
        kind switch
        {
            SubjectRelationsRouteKind.Package =>
                OperationRegistration.PackageRoute,
            SubjectRelationsRouteKind.Library =>
                OperationRegistration.LibraryRoute,
            SubjectRelationsRouteKind.Type =>
                OperationRegistration.TypeRoute,
            SubjectRelationsRouteKind.Member =>
                OperationRegistration.MemberRoute,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private abstract record QueryPredicate
    {
        internal sealed record Form(SubjectRelationForm Value)
            : QueryPredicate;

        internal sealed record Relation(string Value)
            : QueryPredicate;

        internal sealed record Direction(
            SubjectRelationDirectionSelection Value)
            : QueryPredicate;

        internal sealed record Evidence(SubjectRelationEvidenceKind Value)
            : QueryPredicate;

        internal sealed record Ecosystem(
            WorkspaceEcosystemRegistrationId Value)
            : QueryPredicate;

        internal sealed record Concept(string Value)
            : QueryPredicate;
    }

    private sealed class KeyDeclaration(
        string key,
        Func<string, QueryPredicate?> bind)
        : PortableQueryKeyDeclaration<QueryPredicate>
    {
        public override string Key { get; } = key;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Combining;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<QueryPredicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            if (@operator != PortableQueryOperator.Equal
                || bind(value) is not { } predicate)
            {
                return PortableQueryBinding<QueryPredicate>.Rejected;
            }

            return PortableQueryBinding<QueryPredicate>.Bound(
                $"{Key}:{value}",
                predicate);
        }
    }

    private sealed class QueryVocabulary
        : PortableQueryVocabulary<
            QueryPredicate,
            SubjectRelationsQueryPlan>
    {
        private readonly IReadOnlyDictionary<string, KeyDeclaration> _keys =
            new KeyDeclaration[]
            {
                new(FormTermKey, BindForm),
                new(RelationTermKey, BindRelation),
                new(DirectionTermKey, BindDirection),
                new(EvidenceTermKey, BindEvidence),
                new(EcosystemTermKey, BindEcosystem),
                new(ConceptTermKey, BindConcept),
            }.ToDictionary(key => key.Key, StringComparer.Ordinal);

        public override string Identity => VocabularyIdentity;

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<QueryPredicate>? declaration)
        {
            bool found = _keys.TryGetValue(
                key,
                out KeyDeclaration? value);
            declaration = value;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<QueryPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            false;

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
            PortableQueryResolvedTerm<QueryPredicate> first,
            PortableQueryResolvedTerm<QueryPredicate> second) =>
            first.Predicate.GetType() != second.Predicate.GetType()
            || first.Predicate == second.Predicate;

        public override SubjectRelationsQueryPlan CreatePlan(
            PortableQueryResolvedIntent<QueryPredicate> resolved)
        {
            SubjectRelationForm? form = null;
            string? relation = null;
            SubjectRelationDirectionSelection direction =
                SubjectRelationDirectionSelection.Both;
            SubjectRelationEvidenceKind? evidence = null;
            WorkspaceEcosystemRegistrationId? ecosystem = null;
            string? concept = null;

            foreach (PortableQueryResolvedTerm<QueryPredicate> term
                in resolved.Terms)
            {
                switch (term.Predicate)
                {
                    case QueryPredicate.Form selected:
                        form = selected.Value;
                        break;
                    case QueryPredicate.Relation selected:
                        relation = selected.Value;
                        break;
                    case QueryPredicate.Direction selected:
                        direction = selected.Value;
                        break;
                    case QueryPredicate.Evidence selected:
                        evidence = selected.Value;
                        break;
                    case QueryPredicate.Ecosystem selected:
                        ecosystem = selected.Value;
                        break;
                    case QueryPredicate.Concept selected:
                        concept = selected.Value;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown Subject Relations query predicate.");
                }
            }

            return new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [.. resolved.Bounds],
                    [.. resolved.Stages],
                    []),
                new(
                    form,
                    relation,
                    direction,
                    evidence,
                    ecosystem: ecosystem,
                    concept: concept));
        }
    }

    private static QueryPredicate? BindForm(string value) =>
        value switch
        {
            "interface" =>
                new QueryPredicate.Form(SubjectRelationForm.Interface),
            "base-type" =>
                new QueryPredicate.Form(SubjectRelationForm.BaseType),
            "extension" =>
                new QueryPredicate.Form(SubjectRelationForm.Extension),
            "signature" =>
                new QueryPredicate.Form(SubjectRelationForm.Signature),
            "exception" =>
                new QueryPredicate.Form(SubjectRelationForm.Exception),
            "invocation" =>
                new QueryPredicate.Form(SubjectRelationForm.Invocation),
            "object-creation" =>
                new QueryPredicate.Form(SubjectRelationForm.ObjectCreation),
            "assembly-reference" =>
                new QueryPredicate.Form(SubjectRelationForm.AssemblyReference),
            "package-dependency" =>
                new QueryPredicate.Form(SubjectRelationForm.PackageDependency),
            "pattern" =>
                new QueryPredicate.Form(SubjectRelationForm.Pattern),
            _ => null,
        };

    private static QueryPredicate? BindRelation(string value) =>
        IsStableIdentifier(value)
            ? new QueryPredicate.Relation(value)
            : null;

    private static QueryPredicate? BindDirection(string value) =>
        value switch
        {
            "incoming" =>
                new QueryPredicate.Direction(
                    SubjectRelationDirectionSelection.Incoming),
            "outgoing" =>
                new QueryPredicate.Direction(
                    SubjectRelationDirectionSelection.Outgoing),
            "both" =>
                new QueryPredicate.Direction(
                    SubjectRelationDirectionSelection.Both),
            _ => null,
        };

    private static QueryPredicate? BindEvidence(string value) =>
        value switch
        {
            "declaration" =>
                new QueryPredicate.Evidence(
                    SubjectRelationEvidenceKind.Declaration),
            "static-il" =>
                new QueryPredicate.Evidence(
                    SubjectRelationEvidenceKind.StaticIlObservation),
            "pattern-candidate" =>
                new QueryPredicate.Evidence(
                    SubjectRelationEvidenceKind.BoundedPatternCandidate),
            "opportunity" =>
                new QueryPredicate.Evidence(
                    SubjectRelationEvidenceKind.InferredOpportunity),
            _ => null,
        };

    private static QueryPredicate? BindEcosystem(string value) =>
        WorkspaceEcosystemRegistrationId.TryCreate(
            value,
            out WorkspaceEcosystemRegistrationId? ecosystem)
                ? new QueryPredicate.Ecosystem(ecosystem)
                : null;

    private static QueryPredicate? BindConcept(string value) =>
        IntegrationConceptCatalog.Concepts.Any(
            concept => string.Equals(
                concept.Id.Value,
                value,
                StringComparison.Ordinal))
            ? new QueryPredicate.Concept(value)
            : null;

    private static bool IsStableIdentifier(string value) =>
        value.Length is > 0 and <= 160
        && value.AsSpan().Trim().Length == value.Length
        && InertString.IsPermitted(TextPolicy.Field, value);

    private static class OperationRegistration
    {
        private static readonly string[] SubjectRoles =
        [
            PackageSubjectRole,
            LibrarySubjectRole,
            TypeSubjectRole,
            MemberSubjectRole,
        ];

        internal static readonly QueryOperationDefinition<
            QueryPredicate,
            SubjectRelationsQueryPlan> Definition =
            CreateDefinition();

        internal static readonly QueryOperationRoute<
            QueryPredicate,
            SubjectRelationsQueryPlan> PackageRoute =
            CreateRoute(PackageRouteIdentity, PackageSubjectRole);

        internal static readonly QueryOperationRoute<
            QueryPredicate,
            SubjectRelationsQueryPlan> LibraryRoute =
            CreateRoute(LibraryRouteIdentity, LibrarySubjectRole);

        internal static readonly QueryOperationRoute<
            QueryPredicate,
            SubjectRelationsQueryPlan> TypeRoute =
            CreateRoute(TypeRouteIdentity, TypeSubjectRole);

        internal static readonly QueryOperationRoute<
            QueryPredicate,
            SubjectRelationsQueryPlan> MemberRoute =
            CreateRoute(MemberRouteIdentity, MemberSubjectRole);

        private static QueryOperationRoute<
            QueryPredicate,
            SubjectRelationsQueryPlan> CreateRoute(
            string identity,
            string subjectRole) =>
            QueryOperationRoute<
                QueryPredicate,
                SubjectRelationsQueryPlan>.Create(
                    identity,
                    Definition,
                    subjectRole,
                    ResultGrain,
                    [RelationsRowSet],
                    ProfileIdentity,
                    [],
                    []);

        private static QueryOperationDefinition<
            QueryPredicate,
            SubjectRelationsQueryPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                SubjectRoles,
                [ResultGrain],
                [RelationsRowSet]);
            QueryOperationTermBinding[] terms =
            [
                Term(
                    FormTermKey,
                    "relation form",
                    "relation form",
                    [
                        "interface",
                        "base-type",
                        "extension",
                        "signature",
                        "exception",
                        "invocation",
                        "object-creation",
                        "assembly-reference",
                        "package-dependency",
                        "pattern",
                    ],
                    "Selects how the relation is expressed.",
                    applicability),
                Term(
                    RelationTermKey,
                    "relation",
                    "producer-issued relation id",
                    [],
                    "Selects one producer-owned relationship identity.",
                    applicability),
                Term(
                    DirectionTermKey,
                    "direction",
                    "focus-relative direction",
                    ["incoming", "outgoing", "both"],
                    "Selects relation incidence relative to the exact subject.",
                    applicability),
                Term(
                    EvidenceTermKey,
                    "evidence",
                    "evidence kind",
                    [
                        "declaration",
                        "static-il",
                        "pattern-candidate",
                        "opportunity",
                    ],
                    "Selects how a producer established the relation.",
                    applicability),
                Term(
                    EcosystemTermKey,
                    "ecosystem",
                    "ecosystem id",
                    [],
                    "Selects an intrinsic Integration ecosystem association.",
                    applicability),
                Term(
                    ConceptTermKey,
                    "concept",
                    "Integration concept id",
                    [
                        .. IntegrationConceptCatalog.Concepts.Select(
                            concept => concept.Id.Value),
                    ],
                    "Selects an intrinsic Integration concept association.",
                    applicability),
            ];

            return QueryOperationDefinition<
                QueryPredicate,
                SubjectRelationsQueryPlan>.Create(
                    OperationIdentity,
                    Vocabulary,
                    SubjectRoles,
                    [ResultGrain],
                    [RelationsRowSet],
                    terms,
                    [],
                    [
                        new(
                            ProfileIdentity,
                            [.. terms.Select(term => term.Identity)],
                            []),
                    ]);
        }

        private static QueryOperationTermBinding Term(
            string key,
            string label,
            string valueKind,
            IReadOnlyList<string> values,
            string summary,
            QueryOperationApplicability applicability) =>
            new(
                $"subject-relations.term.{key}",
                key,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new(label, valueKind, values, summary),
                []);
    }
}
