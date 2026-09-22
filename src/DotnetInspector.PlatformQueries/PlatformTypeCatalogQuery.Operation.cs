using System.Diagnostics.CodeAnalysis;
using CSharpText;
using DotnetInspector.PlatformHouse;
using ILInspector.Metadata;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.PlatformQueries;

/// <summary>One QuerySpace-resolved Platform type catalog query.</summary>
public sealed class PlatformTypeCatalogQueryPlan
{
    internal PlatformTypeCatalogQueryPlan(
        QuerySpaceRequest request,
        PortableQueryIntent intent,
        string pattern,
        string normalizedPattern,
        bool hasExplicitGenericNotation)
    {
        Request = request;
        Intent = intent;
        Pattern = pattern;
        NormalizedPattern = normalizedPattern;
        HasExplicitGenericNotation = hasExplicitGenericNotation;
    }

    public QuerySpaceRequest Request { get; }
    public PortableQueryIntent Intent { get; }
    public string Pattern { get; }
    public string NormalizedPattern { get; }
    public bool HasExplicitGenericNotation { get; }
}

/// <summary>The result of lowering one user pattern through QuerySpace.</summary>
public abstract record PlatformTypeCatalogQueryPlanResult
{
    private PlatformTypeCatalogQueryPlanResult()
    {
    }

    public sealed record Accepted(PlatformTypeCatalogQueryPlan Plan)
        : PlatformTypeCatalogQueryPlanResult;

    public sealed record Rejected(PlatformTypeCatalogQueryRejectionKind Kind)
        : PlatformTypeCatalogQueryPlanResult;
}

public enum PlatformTypeCatalogQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentNotSupported,
    TerminalMismatch,
    ResultContractMismatch,
}

/// <summary>
/// The result of validating and resolving one Platform QuerySpace request.
/// </summary>
public abstract record PlatformTypeCatalogQueryRequestResult
{
    private PlatformTypeCatalogQueryRequestResult()
    {
    }

    public sealed record Accepted(PlatformTypeCatalogQueryPlan Plan)
        : PlatformTypeCatalogQueryRequestResult;

    public sealed record Rejected(
        PlatformTypeCatalogQueryRequestRejectionKind Kind)
        : PlatformTypeCatalogQueryRequestResult;

    public sealed record IntentRejected(PortableQueryFailure Failure)
        : PlatformTypeCatalogQueryRequestResult;
}

public static partial class PlatformTypeCatalogQuery
{
    public const string OperationIdentity = "platform-type-catalog-query";
    public const string OperationRouteIdentity =
        "platform-type-catalog-query/default";
    public const string OperationSubjectRole =
        "exact-platform-type-catalog";
    public const string OperationResultGrain =
        "platform-type-declaration";
    public const string OperationProfileIdentity = "lookup";
    public const string OperationVocabularyIdentity =
        "platform-type-catalog-query/v1";
    public const string PatternTermKey = "type-pattern";
    public const string DeclarationsRowSet = "platform-type-declarations";
    public const string QuerySpaceIdentity =
        "platform-type-catalog/query-space/v1";
    public const string DeclarationRowScopeIdentity =
        "platform-type-catalog/declarations/v1";
    public const string DeclarationRowVocabularyIdentity =
        "platform-type-catalog/declaration-rows/v1";
    public const string ResultContractIdentity =
        "platform-type-catalog-query/outcome/v1";

    private const string PatternFamily = "platform-type-pattern";

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => QuerySpaceValue.Value;

    public static QuerySpaceRowScopeBinding<PlatformTypeCatalogEntry>
        DeclarationRowScope { get; } =
            new(
                new QuerySpaceRowScopeDescriptor(
                    DeclarationRowScopeIdentity,
                    DeclarationRowVocabularyIdentity,
                    [DeclarationsRowSet],
                    [],
                    [],
                    []),
                RowQueryVocabulary<PlatformTypeCatalogEntry>.Create(
                    RowQueryVocabularyIdentity.Create(),
                    [],
                    []));

    public static QuerySpaceRequest CreateRequest(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            CreateIntent(pattern),
            [DeclarationsRowSet],
            [],
            QuerySpaceTerminalRequirement.Rows);
    }

    public static PlatformTypeCatalogQueryPlanResult ResolvePattern(
        string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetRejectionKind(
                pattern,
                out PlatformTypeCatalogQueryRejectionKind kind))
        {
            return new PlatformTypeCatalogQueryPlanResult.Rejected(kind);
        }

        PlatformTypeCatalogQueryRequestResult resolution =
            ResolveRequest(CreateRequest(pattern), cancellationToken);
        return resolution switch
        {
            PlatformTypeCatalogQueryRequestResult.Accepted accepted =>
                new PlatformTypeCatalogQueryPlanResult.Accepted(
                    accepted.Plan),
            PlatformTypeCatalogQueryRequestResult.IntentRejected =>
                throw new InvalidOperationException(
                    "An owner-validated Platform type pattern did not "
                    + "resolve through its QuerySpace operation."),
            PlatformTypeCatalogQueryRequestResult.Rejected =>
                throw new InvalidOperationException(
                    "An owner-issued Platform QuerySpace request did not "
                    + "match its binding."),
            _ => throw new InvalidOperationException(
                "Unknown Platform QuerySpace request result."),
        };
    }

    public static PlatformTypeCatalogQueryRequestResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                request.QuerySpace,
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return new PlatformTypeCatalogQueryRequestResult.Rejected(
                PlatformTypeCatalogQueryRequestRejectionKind
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !string.Equals(
                request.ParticipatingRowSets[0],
                DeclarationsRowSet,
                StringComparison.Ordinal))
        {
            return new PlatformTypeCatalogQueryRequestResult.Rejected(
                PlatformTypeCatalogQueryRequestRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 0)
        {
            return new PlatformTypeCatalogQueryRequestResult.Rejected(
                PlatformTypeCatalogQueryRequestRejectionKind
                    .RowIntentNotSupported);
        }
        if (request.Terminal != QuerySpaceTerminalRequirement.Rows)
        {
            return new PlatformTypeCatalogQueryRequestResult.Rejected(
                PlatformTypeCatalogQueryRequestRejectionKind.TerminalMismatch);
        }
        if (!string.Equals(
                request.ResultContract,
                ResultContractIdentity,
                StringComparison.Ordinal))
        {
            return new PlatformTypeCatalogQueryRequestResult.Rejected(
                PlatformTypeCatalogQueryRequestRejectionKind
                    .ResultContractMismatch);
        }

        PortableQueryResolution<PatternPlan> resolution =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        return resolution.IsResolved
            ? new PlatformTypeCatalogQueryRequestResult.Accepted(
                new(
                    request,
                    CreateIntent(resolution.Plan.Pattern),
                    resolution.Plan.Pattern,
                    resolution.Plan.NormalizedPattern,
                    resolution.Plan.HasExplicitGenericNotation))
            : new PlatformTypeCatalogQueryRequestResult.IntentRejected(
                resolution.Failure);
    }

    private static PortableQueryIntent CreateIntent(string pattern) =>
        PortableQueryIntent.Create(
            [
                new(
                    PatternTermKey,
                    PortableQueryOperator.Equal,
                    pattern),
            ],
            [],
            [],
            []);

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [DeclarationRowScope],
            [QuerySpaceTerminalRequirement.Rows],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    ResultContractIdentity),
            ]);

    private static bool TryGetRejectionKind(
        string pattern,
        out PlatformTypeCatalogQueryRejectionKind kind)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            kind = PlatformTypeCatalogQueryRejectionKind.EmptyPattern;
            return true;
        }
        if (pattern.Length > MetadataSafetyPolicy.MaxTypeNameCharacters)
        {
            kind = PlatformTypeCatalogQueryRejectionKind.PatternTooLong;
            return true;
        }

        kind = default;
        return false;
    }

    private sealed record PatternPredicate(
        string Pattern,
        string NormalizedPattern,
        bool HasExplicitGenericNotation);

    private sealed record PatternPlan(
        string Pattern,
        string NormalizedPattern,
        bool HasExplicitGenericNotation);

    private sealed class PatternDeclaration
        : PortableQueryKeyDeclaration<PatternPredicate>
    {
        public override string Key => PatternTermKey;

        public override string? Family => PatternFamily;

        public override PortableQueryFamilyKind FamilyKind =>
            PortableQueryFamilyKind.Exclusive;

        public override bool AdmitsOperator(
            PortableQueryOperator @operator) =>
            @operator == PortableQueryOperator.Equal;

        public override PortableQueryBinding<PatternPredicate> Bind(
            PortableQueryOperator @operator,
            string value)
        {
            if (@operator != PortableQueryOperator.Equal
                || TryGetRejectionKind(value, out _))
            {
                return PortableQueryBinding<PatternPredicate>.Rejected;
            }

            string normalized =
                FqnParser.NormalizeTypeName(value.Trim()).Replace('+', '.');
            return PortableQueryBinding<PatternPredicate>.Bound(
                $"{PatternTermKey}:{normalized}",
                new(
                    value,
                    normalized,
                    TypeMatcher.HasExplicitGenericNotation(value)));
        }
    }

    private sealed class PatternVocabulary
        : PortableQueryVocabulary<PatternPredicate, PatternPlan>
    {
        private static readonly PatternDeclaration Pattern = new();

        public override string Identity =>
            OperationVocabularyIdentity;

        public override IReadOnlyList<string> RequiredTermFamilies =>
            [PatternFamily];

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<PatternPredicate>? declaration)
        {
            bool found = string.Equals(
                key,
                PatternTermKey,
                StringComparison.Ordinal);
            declaration = found ? Pattern : null;
            return found;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<PatternPredicate>?
                declaration)
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

        public override PatternPlan CreatePlan(
            PortableQueryResolvedIntent<PatternPredicate> resolved)
        {
            PatternPredicate pattern =
                resolved.Terms.Single().Predicate;
            return new(
                pattern.Pattern,
                pattern.NormalizedPattern,
                pattern.HasExplicitGenericNotation);
        }
    }

    private static class OperationRegistration
    {
        private static readonly PatternVocabulary Vocabulary = new();

        internal static readonly QueryOperationDefinition<
            PatternPredicate,
            PatternPlan> Definition = CreateDefinition();

        internal static readonly QueryOperationRoute<
            PatternPredicate,
            PatternPlan> Route =
                QueryOperationRoute<PatternPredicate, PatternPlan>.Create(
                    OperationRouteIdentity,
                    Definition,
                    OperationSubjectRole,
                    OperationResultGrain,
                    [DeclarationsRowSet],
                    OperationProfileIdentity,
                    [],
                    []);

        private static QueryOperationDefinition<
            PatternPredicate,
            PatternPlan> CreateDefinition()
        {
            var applicability = new QueryOperationApplicability(
                [OperationSubjectRole],
                [OperationResultGrain],
                []);
            var pattern = new QueryOperationTermBinding(
                "platform-type-catalog-query.term.pattern",
                PatternTermKey,
                QueryOperationTermRole.OperationSelector,
                applicability,
                new QueryOperationTermDescription(
                    "type pattern",
                    "C# or metadata type name",
                    [],
                    "Resolves one declaration from the exact Platform catalog."),
                []);

            return QueryOperationDefinition<
                PatternPredicate,
                PatternPlan>.Create(
                    OperationIdentity,
                    Vocabulary,
                    [OperationSubjectRole],
                    [OperationResultGrain],
                    [DeclarationsRowSet],
                    [pattern],
                    [],
                    [
                        new(
                            OperationProfileIdentity,
                            [pattern.Identity],
                            []),
                    ]);
        }
    }
}
