using System.Diagnostics.CodeAnalysis;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public sealed record LibraryAddressPopulationQueryPlan(
    ResolvedRowQueryPlan<LibraryAddressPopulationRow> Rows);

public enum LibraryAddressPopulationQueryRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentMismatch,
    TerminalMismatch,
    ResultContractMismatch,
}

public abstract record LibraryAddressPopulationQueryResolution
{
    private LibraryAddressPopulationQueryResolution()
    {
    }

    public sealed record Accepted(
        LibraryAddressPopulationQueryPlan Plan,
        QuerySpaceTerminalRequirement Terminal)
        : LibraryAddressPopulationQueryResolution;

    public sealed record Rejected(
        LibraryAddressPopulationQueryRejectionKind Kind)
        : LibraryAddressPopulationQueryResolution;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : LibraryAddressPopulationQueryResolution;

    public sealed record RowIntentRejected(RowQueryFailure Failure)
        : LibraryAddressPopulationQueryResolution;
}

public static class LibraryAddressPopulationQuery
{
    public const string OperationIdentity =
        "library-address-population";
    public const string OperationRouteIdentity =
        "library-address-population/default";
    public const string OperationVocabularyIdentity =
        "library-address-population/operation/v1";
    public const string SubjectRole = "library-address-population";
    public const string ResultGrain = "population-record";
    public const string RowSet = "address-population-records";
    public const string OperationProfileIdentity = "default";
    public const string QuerySpaceIdentity =
        "library-address-population/query-space/v1";
    public const string RowScopeIdentity =
        "library-address-population/rows/v1";
    public const string RowVocabularyIdentity =
        "library-address-population/row-vocabulary/v1";
    public const string RowsResultContract =
        "library-address-population/rows-result/v1";
    public const string CountResultContract =
        "library-address-population/count-result/v1";
    public const string ExistsResultContract =
        "library-address-population/exists-result/v1";

    private static readonly QueryOperationDefinition<
        Predicate,
        OperationPlan> s_operation =
            QueryOperationDefinition<Predicate, OperationPlan>.Create(
                OperationIdentity,
                new Vocabulary(),
                [SubjectRole],
                [ResultGrain],
                [RowSet],
                [],
                [],
                [
                    new(
                        OperationProfileIdentity,
                        [],
                        []),
                ]);

    private static readonly QueryOperationRoute<
        Predicate,
        OperationPlan> s_route =
            QueryOperationRoute<Predicate, OperationPlan>.Create(
                OperationRouteIdentity,
                s_operation,
                SubjectRole,
                ResultGrain,
                [RowSet],
                OperationProfileIdentity,
                [],
                []);

    public static QuerySpaceRowScopeBinding<LibraryAddressPopulationRow>
        RowsScope { get; } =
            new(
                new QuerySpaceRowScopeDescriptor(
                    RowScopeIdentity,
                    RowVocabularyIdentity,
                    [RowSet],
                    [],
                    [],
                    [
                        RowSelectionStageKind.Head,
                        RowSelectionStageKind.Tail,
                        RowSelectionStageKind.Window,
                    ]),
                RowQueryVocabulary<LibraryAddressPopulationRow>.Create(
                    RowQueryVocabularyIdentity.Create(),
                    [],
                    []));

    public static QuerySpaceBinding QuerySpace { get; } =
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            s_route,
            [RowsScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
                QuerySpaceTerminalRequirement.Exists,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContract),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContract),
                new(
                    QuerySpaceTerminalRequirement.Exists,
                    ExistsResultContract),
            ]);

    public static QuerySpaceRequest CreateRequest(
        RowSelectionIntent<string> rows,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rows);
        PortableQueryIntent rowIntent =
            PortableQueryIntent.Create(
                [],
                [],
                PortableQueryRowSelection.ToStages(rows),
                []);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            PortableQueryIntent.Empty,
            [RowSet],
            [
                new(
                    RowScopeIdentity,
                    rowIntent,
                    [RowSet]),
            ],
            terminal);
    }

    public static LibraryAddressPopulationQueryResolution ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.QuerySpace.Equals(
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new LibraryAddressPopulationQueryResolution.Rejected(
                LibraryAddressPopulationQueryRejectionKind
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets is not [RowSet])
        {
            return new LibraryAddressPopulationQueryResolution.Rejected(
                LibraryAddressPopulationQueryRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents is not [var association]
            || !association.Scope.Equals(
                RowScopeIdentity,
                StringComparison.Ordinal)
            || association.RowSets is not [RowSet])
        {
            return new LibraryAddressPopulationQueryResolution.Rejected(
                LibraryAddressPopulationQueryRejectionKind
                    .RowIntentMismatch);
        }
        if (request.Terminal is not (
                QuerySpaceTerminalRequirement.Rows
                or QuerySpaceTerminalRequirement.Count
                or QuerySpaceTerminalRequirement.Exists))
        {
            return new LibraryAddressPopulationQueryResolution.Rejected(
                LibraryAddressPopulationQueryRejectionKind
                    .TerminalMismatch);
        }
        string expectedContract =
            request.Terminal switch
            {
                QuerySpaceTerminalRequirement.Rows =>
                    RowsResultContract,
                QuerySpaceTerminalRequirement.Count =>
                    CountResultContract,
                QuerySpaceTerminalRequirement.Exists =>
                    ExistsResultContract,
                _ => throw new InvalidOperationException(
                    "Unknown Library Address population terminal."),
            };
        if (!string.Equals(
                request.ResultContract,
                expectedContract,
                StringComparison.Ordinal))
        {
            return new LibraryAddressPopulationQueryResolution.Rejected(
                LibraryAddressPopulationQueryRejectionKind
                    .ResultContractMismatch);
        }

        PortableQueryResolution<OperationPlan> operation =
            s_route.Resolve(
                request.Operation,
                cancellationToken);
        if (!operation.IsResolved)
        {
            return new LibraryAddressPopulationQueryResolution.IntentRejected(
                operation.Failure);
        }
        RowQueryResolutionResult<LibraryAddressPopulationRow> rows =
            RowsScope.Resolve(association.Intent);
        return rows.IsSuccess
            ? new LibraryAddressPopulationQueryResolution.Accepted(
                new(rows.Plan!),
                request.Terminal)
            : new LibraryAddressPopulationQueryResolution.RowIntentRejected(
                rows.Failure!);
    }

    private readonly record struct Predicate;

    private sealed record OperationPlan(PortableQueryIntent Intent);

    private sealed class Vocabulary
        : PortableQueryVocabulary<Predicate, OperationPlan>
    {
        public override string Identity => OperationVocabularyIdentity;

        public override IReadOnlyList<string> RequiredDimensions => [];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<Predicate>? declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<Predicate>? declaration)
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
            PortableQueryResolvedTerm<Predicate> second) => true;

        public override OperationPlan CreatePlan(
            PortableQueryResolvedIntent<Predicate> resolved) =>
            new(
                PortableQueryIntent.Create(
                    [.. resolved.Terms.Select(term => term.Term)],
                    [.. resolved.Bounds],
                    [.. resolved.Stages],
                    []));
    }
}
