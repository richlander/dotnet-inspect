using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

/// <summary>
/// One validated structural request for the Library overview operation.
/// </summary>
public sealed class LibraryOverviewQueryPlan
{
    internal LibraryOverviewQueryPlan(QuerySpaceRequest request) =>
        Request = request;

    public QuerySpaceRequest Request { get; }

    public QuerySpaceTerminalRequirement Terminal => Request.Terminal;
}

/// <summary>The result of resolving one Library overview QuerySpace request.</summary>
public abstract record LibraryOverviewQueryPlanResult
{
    private LibraryOverviewQueryPlanResult()
    {
    }

    public sealed record Accepted(LibraryOverviewQueryPlan Plan)
        : LibraryOverviewQueryPlanResult;

    public sealed record Rejected(
        LibraryOverviewQueryRequestRejectionKind Kind)
        : LibraryOverviewQueryPlanResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : LibraryOverviewQueryPlanResult;
}

public enum LibraryOverviewQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentMismatch,
    TerminalMismatch,
    ResultContractMismatch,
}

/// <summary>
/// QuerySpace registration for one bounded Library overview Document.
/// </summary>
public static class LibraryOverviewQuery
{
    public const string OperationIdentity = "library-overview";
    public const string OperationRouteIdentity =
        "library-overview/default";
    public const string OperationSubjectRole = "exact-library";
    public const string OperationResultGrain = "library-overview";
    public const string OperationProfileIdentity = "overview";
    public const string OverviewRowSet = "library-overview";
    public const string QuerySpaceIdentity =
        "library-overview/query-space/v1";
    public const string OverviewRowScopeIdentity =
        "library-overview/rows/v1";
    public const string RowsResultContractIdentity =
        "library-overview/outcome/v1";
    public const string CountResultContractIdentity =
        "library-overview/count/v1";

    private static readonly Lazy<QuerySpaceBinding> s_querySpace =
        new(CreateQuerySpace);
    private static readonly SectionRowSchemaIdentity<
        LibraryOverviewDocument> s_schema =
            SectionRowSchemaIdentity<LibraryOverviewDocument>.Create();

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => s_querySpace.Value;

    public static QuerySpaceRowScopeBinding<LibraryOverviewDocument>
        OverviewRowScope { get; } =
            new(
                new QuerySpaceRowScopeDescriptor(
                    OverviewRowScopeIdentity,
                    RowsResultContractIdentity,
                    [OverviewRowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<LibraryOverviewDocument>.Create(
                    RowQueryVocabularyIdentity.Create(),
                    [],
                    []));

    public static LibraryOverviewQueryPlan CreatePlan(
        QuerySpaceTerminalRequirement terminal)
    {
        LibraryOverviewQueryPlanResult result =
            ResolveRequest(CreateRequest(terminal));
        return result is LibraryOverviewQueryPlanResult.Accepted accepted
            ? accepted.Plan
            : throw new InvalidOperationException(
                "The owner-issued Library overview request was rejected.");
    }

    public static QuerySpaceRequest CreateRequest(
        QuerySpaceTerminalRequirement terminal)
    {
        if (terminal is not QuerySpaceTerminalRequirement.Rows
            and not QuerySpaceTerminalRequirement.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "Library overview supports Rows and Count.");
        }

        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [OverviewRowSet],
            [
                new QuerySpaceRowIntentAssociation(
                    OverviewRowScopeIdentity,
                    PortableQueryIntent.Empty,
                    [OverviewRowSet]),
            ],
            terminal);
    }

    public static LibraryOverviewQueryPlanResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(
                request.QuerySpace,
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new LibraryOverviewQueryPlanResult.Rejected(
                LibraryOverviewQueryRequestRejectionKind
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !string.Equals(
                request.ParticipatingRowSets[0],
                OverviewRowSet,
                StringComparison.Ordinal))
        {
            return new LibraryOverviewQueryPlanResult.Rejected(
                LibraryOverviewQueryRequestRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 1
            || !string.Equals(
                request.RowIntents[0].Scope,
                OverviewRowScopeIdentity,
                StringComparison.Ordinal)
            || request.RowIntents[0].RowSets.Count != 1
            || !string.Equals(
                request.RowIntents[0].RowSets[0],
                OverviewRowSet,
                StringComparison.Ordinal)
            || request.RowIntents[0].Intent.Terms.Count != 0
            || request.RowIntents[0].Intent.Order.Count != 0
            || request.RowIntents[0].Intent.Stages.Count != 0
            || request.RowIntents[0].Intent.Bounds.Count != 0)
        {
            return new LibraryOverviewQueryPlanResult.Rejected(
                LibraryOverviewQueryRequestRejectionKind
                    .RowIntentMismatch);
        }
        if (request.Terminal is not QuerySpaceTerminalRequirement.Rows
            and not QuerySpaceTerminalRequirement.Count)
        {
            return new LibraryOverviewQueryPlanResult.Rejected(
                LibraryOverviewQueryRequestRejectionKind
                    .TerminalMismatch);
        }

        string expectedContract =
            request.Terminal == QuerySpaceTerminalRequirement.Rows
                ? RowsResultContractIdentity
                : CountResultContractIdentity;
        if (!string.Equals(
                request.ResultContract,
                expectedContract,
                StringComparison.Ordinal))
        {
            return new LibraryOverviewQueryPlanResult.Rejected(
                LibraryOverviewQueryRequestRejectionKind
                    .ResultContractMismatch);
        }

        PortableQueryResolution<OperationPlan> resolution =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        return resolution.IsResolved
            ? new LibraryOverviewQueryPlanResult.Accepted(
                new(request))
            : new LibraryOverviewQueryPlanResult.IntentRejected(
                resolution.Failure);
    }

    internal static SectionRowsOutcome<
        string,
        LibraryOverviewDocument> ApplyRows(
            LibraryOverviewQueryPlan plan,
            LibraryOverviewDocument document) =>
        QuerySpaceSectionRowExecutor.ApplyRows(
            Resolve(plan, document));

    internal static SectionCountOutcome<string, string> ApplyCount(
        LibraryOverviewQueryPlan plan,
        LibraryOverviewDocument document) =>
        QuerySpaceSectionRowExecutor.ApplyCount<
            LibraryOverviewDocument,
            string>(Resolve(plan, document));

    private static QuerySpaceSectionRowExecutionRequest<
        LibraryOverviewDocument> Resolve(
            LibraryOverviewQueryPlan plan,
            LibraryOverviewDocument document)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(document);
        var declaration = new SectionRowSetDeclaration<
            string,
            LibraryOverviewDocument,
            LibraryOverviewDocument>(
                OverviewRowSet,
                s_schema,
                [document],
                static (_, rows) => rows.Single());
        QuerySpaceSectionRowResolutionResult<
            LibraryOverviewDocument> resolution =
                QuerySpaceSectionRowResolver.Resolve(
                    QuerySpace,
                    plan.Request,
                    [declaration],
                    new SectionQuerySpaceRowScopeBinding<
                        LibraryOverviewDocument>(
                            OverviewRowScope,
                            s_schema));
        return resolution.IsSuccess
            ? resolution.Request!
            : throw new InvalidOperationException(
                "The validated Library overview QuerySpace plan could not "
                    + "resolve its declared Document row.");
    }

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [OverviewRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContractIdentity),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContractIdentity),
            ]);

    private sealed record OperationPredicate;

    private sealed record OperationPlan;

    private sealed class OperationVocabulary
        : PortableQueryVocabulary<OperationPredicate, OperationPlan>
    {
        public override string Identity =>
            "library-overview/v1";

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<OperationPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<OperationPredicate>?
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

        public override OperationPlan CreatePlan(
            PortableQueryResolvedIntent<OperationPredicate> resolved) =>
            new();
    }

    private static class OperationRegistration
    {
        private static readonly OperationVocabulary s_vocabulary =
            new();

        internal static readonly QueryOperationDefinition<
            OperationPredicate,
            OperationPlan> Definition =
                QueryOperationDefinition<
                    OperationPredicate,
                    OperationPlan>.Create(
                        OperationIdentity,
                        s_vocabulary,
                        [OperationSubjectRole],
                        [OperationResultGrain],
                        [OverviewRowSet],
                        [],
                        [],
                        [
                            new(
                                OperationProfileIdentity,
                                [],
                                []),
                        ]);

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            OperationPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    OperationPlan>.Create(
                        OperationRouteIdentity,
                        Definition,
                        OperationSubjectRole,
                        OperationResultGrain,
                        [OverviewRowSet],
                        OperationProfileIdentity,
                        [],
                        []);
    }
}
