using System.Diagnostics.CodeAnalysis;

using DotnetInspector.DocumentationHouse;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

/// <summary>One QuerySpace-resolved documentation operation.</summary>
public sealed class DocumentationQueryPlan
{
    internal DocumentationQueryPlan(
        QuerySpaceRequest request,
        PortableQueryIntent intent,
        DocumentationDemand demand)
    {
        Request = request;
        Intent = intent;
        Demand = demand;
    }

    public QuerySpaceRequest Request { get; }
    public PortableQueryIntent Intent { get; }
    public DocumentationDemand Demand { get; }
}

/// <summary>The result of resolving one documentation operation intent.</summary>
public abstract record DocumentationQueryPlanResult
{
    private DocumentationQueryPlanResult()
    {
    }

    public sealed record Accepted(DocumentationQueryPlan Plan)
        : DocumentationQueryPlanResult;

    public sealed record Rejected(PortableQueryFailure Failure)
        : DocumentationQueryPlanResult;
}

public enum DocumentationQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentNotSupported,
    TerminalMismatch,
    ResultContractMismatch,
}

/// <summary>
/// The result of validating and resolving one structural QuerySpace request.
/// </summary>
public abstract record DocumentationQueryRequestResult
{
    private DocumentationQueryRequestResult()
    {
    }

    public sealed record Accepted(DocumentationQueryPlan Plan)
        : DocumentationQueryRequestResult;

    public sealed record Rejected(
        DocumentationQueryRequestRejectionKind Kind)
        : DocumentationQueryRequestResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : DocumentationQueryRequestResult;
}

/// <summary>
/// QuerySpace registration and resolution for one DocumentationHouse
/// settlement.
/// </summary>
public static partial class DocumentationQuery
{
    public const string OperationIdentity = "documentation";
    public const string OperationRouteIdentity = "documentation/default";
    public const string OperationSubjectRole = "exact-documentation-subject";
    public const string OperationResultGrain = "documentation";
    public const string OperationProfileIdentity = "demand";
    public const string DemandTermKey = "demand";
    public const string CompiledXmlDemandValue = "compiled-xml";
    public const string AuthoredSourceDemandValue = "authored-source";
    public const string CombinedDemandValue =
        "compiled-xml-and-authored-source";
    public const string DocumentationRowSet = "documentation";
    public const string QuerySpaceIdentity = "documentation/query-space/v1";
    public const string DocumentationRowScopeIdentity =
        "documentation/rows/v1";
    public const string ResultContractIdentity =
        "documentation/query-result/v1";

    private const string DemandFamily = "documentation-demand";

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => QuerySpaceValue.Value;

    public static QuerySpaceRowScopeBinding<DocumentationQueryOutcome>
        DocumentationRowScope { get; } =
            new(
                new QuerySpaceRowScopeDescriptor(
                    DocumentationRowScopeIdentity,
                    ResultContractIdentity,
                    [DocumentationRowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<DocumentationQueryOutcome>.Create(
                    RowQueryVocabularyIdentity.Create(),
                    [],
                    []));

    public static PortableQueryIntent CreateIntent(
        DocumentationDemand demand)
    {
        if (!Enum.IsDefined(demand))
            throw new ArgumentOutOfRangeException(nameof(demand));

        return PortableQueryIntent.Create(
            [
                new(
                    DemandTermKey,
                    PortableQueryOperator.Equal,
                    Value(demand)),
            ],
            [],
            [],
            []);
    }

    public static QuerySpaceRequest CreateRequest(
        DocumentationDemand demand) =>
        QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            CreateIntent(demand),
            [DocumentationRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);

    public static DocumentationQueryPlanResult ResolveIntent(
        PortableQueryIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        PortableQueryResolution<DocumentationDemand> resolution =
            OperationRegistration.Route.Resolve(
                intent,
                cancellationToken);
        if (!resolution.IsResolved)
            return new DocumentationQueryPlanResult.Rejected(
                resolution.Failure);

        QuerySpaceRequest request = QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            CreateIntent(resolution.Plan),
            [DocumentationRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
        return new DocumentationQueryPlanResult.Accepted(
            new(request, request.Operation, resolution.Plan));
    }

    public static DocumentationQueryRequestResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(
                request.QuerySpace,
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new DocumentationQueryRequestResult.Rejected(
                DocumentationQueryRequestRejectionKind.QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !string.Equals(
                request.ParticipatingRowSets[0],
                DocumentationRowSet,
                StringComparison.Ordinal))
        {
            return new DocumentationQueryRequestResult.Rejected(
                DocumentationQueryRequestRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 0)
        {
            return new DocumentationQueryRequestResult.Rejected(
                DocumentationQueryRequestRejectionKind
                    .RowIntentNotSupported);
        }
        if (request.Terminal != QuerySpaceTerminalRequirement.Rows)
        {
            return new DocumentationQueryRequestResult.Rejected(
                DocumentationQueryRequestRejectionKind.TerminalMismatch);
        }
        if (!string.Equals(
                request.ResultContract,
                ResultContractIdentity,
                StringComparison.Ordinal))
        {
            return new DocumentationQueryRequestResult.Rejected(
                DocumentationQueryRequestRejectionKind
                    .ResultContractMismatch);
        }

        PortableQueryResolution<DocumentationDemand> resolution =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        return resolution.IsResolved
            ? new DocumentationQueryRequestResult.Accepted(
                new(
                    request,
                    CreateIntent(resolution.Plan),
                    resolution.Plan))
            : new DocumentationQueryRequestResult.IntentRejected(
                resolution.Failure);
    }

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [DocumentationRowScope],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    ResultContractIdentity),
            ]);

    private static string Value(DocumentationDemand demand) =>
        demand switch
        {
            DocumentationDemand.CompiledXml =>
                CompiledXmlDemandValue,
            DocumentationDemand.AuthoredSourceDocumentation =>
                AuthoredSourceDemandValue,
            DocumentationDemand.CompiledXmlAndAuthoredSourceDocumentation =>
                CombinedDemandValue,
            _ => throw new InvalidOperationException(
                "Unknown documentation demand."),
        };

    private sealed record DocumentationDemandPredicate(
        DocumentationDemand Demand);

    private sealed class DocumentationDemandDeclaration
        : PortableQueryKeyDeclaration<DocumentationDemandPredicate>
    {
        public override string Key => DemandTermKey;

        public override string? Family => DemandFamily;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Exclusive;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<DocumentationDemandPredicate>
            Bind(
                PortableQueryOperator @operator,
                string value)
        {
            if (@operator != PortableQueryOperator.Equal
                || !TryParse(value, out DocumentationDemand demand))
            {
                return PortableQueryBinding<
                    DocumentationDemandPredicate>.Rejected;
            }

            return PortableQueryBinding<
                DocumentationDemandPredicate>.Bound(
                    $"demand:{value}",
                    new(demand));
        }
    }

    private sealed class DocumentationQueryVocabulary
        : PortableQueryVocabulary<
            DocumentationDemandPredicate,
            DocumentationDemand>
    {
        internal const string VocabularyIdentity = "documentation/v1";

        private static readonly DocumentationDemandDeclaration
            DemandDeclaration = new();

        public override string Identity => VocabularyIdentity;

        public override IReadOnlyList<string> RequiredTermFamilies =>
            [DemandFamily];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<
                DocumentationDemandPredicate>? declaration)
        {
            bool found = string.Equals(
                key,
                DemandTermKey,
                StringComparison.Ordinal);
            declaration = found ? DemandDeclaration : null;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<
                DocumentationDemandPredicate>? declaration)
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

        public override DocumentationDemand CreatePlan(
            PortableQueryResolvedIntent<
                DocumentationDemandPredicate> resolved) =>
            resolved.Terms.Single().Predicate.Demand;
    }

    private static bool TryParse(
        string value,
        out DocumentationDemand demand)
    {
        demand = value switch
        {
            CompiledXmlDemandValue => DocumentationDemand.CompiledXml,
            AuthoredSourceDemandValue =>
                DocumentationDemand.AuthoredSourceDocumentation,
            CombinedDemandValue =>
                DocumentationDemand
                    .CompiledXmlAndAuthoredSourceDocumentation,
            _ => default,
        };
        return value is CompiledXmlDemandValue
            or AuthoredSourceDemandValue
            or CombinedDemandValue;
    }

    private static class OperationRegistration
    {
        private static readonly DocumentationQueryVocabulary Vocabulary =
            new();

        internal static readonly QueryOperationDefinition<
            DocumentationDemandPredicate,
            DocumentationDemand> Definition =
                CreateDefinition();

        internal static readonly QueryOperationRoute<
            DocumentationDemandPredicate,
            DocumentationDemand> Route =
                QueryOperationRoute<
                    DocumentationDemandPredicate,
                    DocumentationDemand>.Create(
                    OperationRouteIdentity,
                    Definition,
                    OperationSubjectRole,
                    OperationResultGrain,
                    [DocumentationRowSet],
                    OperationProfileIdentity,
                    [],
                    []);

        private static QueryOperationDefinition<
            DocumentationDemandPredicate,
            DocumentationDemand> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var demand = new QueryOperationTermBinding(
                "documentation.term.demand",
                DemandTermKey,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new QueryOperationTermDescription(
                    "documentation demand",
                    "closed documentation-channel demand",
                    [
                        CompiledXmlDemandValue,
                        AuthoredSourceDemandValue,
                        CombinedDemandValue,
                    ],
                    "Selects the DocumentationHouse channels to settle."),
                []);

            return QueryOperationDefinition<
                DocumentationDemandPredicate,
                DocumentationDemand>.Create(
                    OperationIdentity,
                    Vocabulary,
                    [OperationSubjectRole],
                    [OperationResultGrain],
                    [DocumentationRowSet],
                    [demand],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [demand.Identity],
                            []),
                    ]);
        }
    }
}
