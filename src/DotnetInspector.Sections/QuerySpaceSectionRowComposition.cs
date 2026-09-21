using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public abstract class SectionQuerySpaceRowScopeBinding
{
    private protected SectionQuerySpaceRowScopeBinding(
        QuerySpaceRowScopeBinding queryScope,
        SectionRowSchemaIdentity schema)
    {
        ArgumentNullException.ThrowIfNull(queryScope);
        ArgumentNullException.ThrowIfNull(schema);
        QueryScope = queryScope;
        Schema = schema;
    }

    public QuerySpaceRowScopeBinding QueryScope { get; }

    public SectionRowSchemaIdentity Schema { get; }

    internal abstract SectionQuerySpaceScopeResolution Resolve(
        QuerySpaceRowIntentAssociation association);
}

public sealed class SectionQuerySpaceRowScopeBinding<TRow> :
    SectionQuerySpaceRowScopeBinding
{
    public SectionQuerySpaceRowScopeBinding(
        QuerySpaceRowScopeBinding<TRow> queryScope,
        SectionRowSchemaIdentity<TRow> schema)
        : base(queryScope, schema)
    {
        TypedQueryScope = queryScope;
        TypedSchema = schema;
    }

    public QuerySpaceRowScopeBinding<TRow> TypedQueryScope { get; }

    public SectionRowSchemaIdentity<TRow> TypedSchema { get; }

    internal override SectionQuerySpaceScopeResolution Resolve(
        QuerySpaceRowIntentAssociation association)
    {
        RowQueryResolutionResult<TRow> resolution =
            TypedQueryScope.Resolve(association.Intent);
        if (!resolution.IsSuccess)
        {
            return SectionQuerySpaceScopeResolution.Failed(
                resolution.Failure!);
        }

        ResolvedRowQueryPlan<TRow> plan = resolution.Plan!;
        bool canApplyCount =
            RowQueryExecutor.TryApplyCount(
                0,
                plan,
                out _);
        var schemaBinding =
            new SectionRowSchemaBinding<string, TRow>(
                TypedSchema,
                sequences =>
                    RowsCohortExecutor.ApplyResolved(
                        sequences,
                        plan),
                canApplyCount
                    ? rowSets =>
                        RowsCohortExecutor.ApplyResolvedCount<
                            string,
                            TRow>(
                                rowSets,
                                plan)
                    : null);
        return SectionQuerySpaceScopeResolution.Success(
            schemaBinding,
            new ResolvedQuerySpaceRowAssociation<TRow>(
                association,
                TypedQueryScope,
                TypedSchema,
                plan));
    }
}

public abstract class ResolvedQuerySpaceRowAssociation
{
    private protected ResolvedQuerySpaceRowAssociation(
        QuerySpaceRowIntentAssociation association,
        QuerySpaceRowScopeBinding queryScope,
        SectionRowSchemaIdentity schema)
    {
        Association = association;
        QueryScope = queryScope;
        Schema = schema;
    }

    public QuerySpaceRowIntentAssociation Association { get; }

    public QuerySpaceRowScopeBinding QueryScope { get; }

    public SectionRowSchemaIdentity Schema { get; }
}

public sealed class ResolvedQuerySpaceRowAssociation<TRow> :
    ResolvedQuerySpaceRowAssociation
{
    internal ResolvedQuerySpaceRowAssociation(
        QuerySpaceRowIntentAssociation association,
        QuerySpaceRowScopeBinding<TRow> queryScope,
        SectionRowSchemaIdentity<TRow> schema,
        ResolvedRowQueryPlan<TRow> plan)
        : base(association, queryScope, schema)
    {
        TypedQueryScope = queryScope;
        TypedSchema = schema;
        Plan = plan;
    }

    public QuerySpaceRowScopeBinding<TRow> TypedQueryScope { get; }

    public SectionRowSchemaIdentity<TRow> TypedSchema { get; }

    public ResolvedRowQueryPlan<TRow> Plan { get; }
}

public sealed class QuerySpaceSectionRowResolutionFailure
{
    internal QuerySpaceSectionRowResolutionFailure(
        string scope,
        RowQueryFailure rowQueryFailure)
    {
        Scope = scope;
        RowQueryFailure = rowQueryFailure;
    }

    public string Scope { get; }

    public RowQueryFailure RowQueryFailure { get; }
}

public sealed class QuerySpaceSectionRowResolutionResult<TProjection>
{
    private QuerySpaceSectionRowResolutionResult(
        QuerySpaceSectionRowExecutionRequest<TProjection>? request,
        QuerySpaceSectionRowResolutionFailure? failure)
    {
        Request = request;
        Failure = failure;
    }

    public bool IsSuccess => Request is not null;

    public QuerySpaceSectionRowExecutionRequest<TProjection>? Request
    { get; }

    public ResolvedQuerySpaceRowAssociation? Association =>
        Request?.Association;

    public QuerySpaceSectionRowResolutionFailure? Failure { get; }

    internal static QuerySpaceSectionRowResolutionResult<TProjection>
        Success(
            QuerySpaceSectionRowExecutionRequest<TProjection> request) =>
        new(request, null);

    internal static QuerySpaceSectionRowResolutionResult<TProjection>
        Failed(
            QuerySpaceSectionRowResolutionFailure failure) =>
        new(null, failure);
}

public sealed class QuerySpaceSectionRowExecutionRequest<TProjection>
{
    internal QuerySpaceSectionRowExecutionRequest(
        QuerySpaceRequest structuralRequest,
        SectionRowExecutionRequest<string, TProjection> sectionRequest,
        ResolvedQuerySpaceRowAssociation association)
    {
        StructuralRequest = structuralRequest;
        SectionRequest = sectionRequest;
        Association = association;
    }

    public QuerySpaceRequest StructuralRequest { get; }

    public QuerySpaceTerminalRequirement Terminal =>
        StructuralRequest.Terminal;

    public ResolvedQuerySpaceRowAssociation Association { get; }

    internal SectionRowExecutionRequest<string, TProjection>
        SectionRequest
    { get; }
}

public sealed class QuerySpaceSectionSourceRowExecutionRequest<
    TProjection,
    TDisposition,
    TCompletionEvidence>
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    internal QuerySpaceSectionSourceRowExecutionRequest(
        QuerySpaceRequest structuralRequest,
        SectionSourceRowExecutionRequest<
            string,
            TProjection,
            TDisposition,
            TCompletionEvidence> sectionRequest,
        ResolvedQuerySpaceRowAssociation association)
    {
        StructuralRequest = structuralRequest;
        SectionRequest = sectionRequest;
        Association = association;
    }

    public QuerySpaceRequest StructuralRequest { get; }

    public QuerySpaceTerminalRequirement Terminal =>
        StructuralRequest.Terminal;

    public ResolvedQuerySpaceRowAssociation Association { get; }

    internal SectionSourceRowExecutionRequest<
        string,
        TProjection,
        TDisposition,
        TCompletionEvidence> SectionRequest
    { get; }
}

public sealed class QuerySpaceSectionSourceRowResolutionResult<
    TProjection,
    TDisposition,
    TCompletionEvidence>
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    private QuerySpaceSectionSourceRowResolutionResult(
        QuerySpaceSectionSourceRowExecutionRequest<
            TProjection,
            TDisposition,
            TCompletionEvidence>? request,
        QuerySpaceSectionRowResolutionFailure? failure)
    {
        Request = request;
        Failure = failure;
    }

    public bool IsSuccess => Request is not null;

    public QuerySpaceSectionSourceRowExecutionRequest<
        TProjection,
        TDisposition,
        TCompletionEvidence>? Request
    { get; }

    public ResolvedQuerySpaceRowAssociation? Association =>
        Request?.Association;

    public QuerySpaceSectionRowResolutionFailure? Failure { get; }

    internal static QuerySpaceSectionSourceRowResolutionResult<
        TProjection,
        TDisposition,
        TCompletionEvidence> Success(
            QuerySpaceSectionSourceRowExecutionRequest<
                TProjection,
                TDisposition,
                TCompletionEvidence> request) =>
        new(request, null);

    internal static QuerySpaceSectionSourceRowResolutionResult<
        TProjection,
        TDisposition,
        TCompletionEvidence> Failed(
            QuerySpaceSectionRowResolutionFailure failure) =>
        new(null, failure);
}

public static class QuerySpaceSectionRowExecutor
{
    public static SectionRowsOutcome<string, TProjection>
        ApplyRows<TProjection>(
            QuerySpaceSectionRowExecutionRequest<TProjection> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTerminal(
            request.Terminal,
            QuerySpaceTerminalRequirement.Rows);
        return SectionRowExecutor.ApplyRows(
            request.SectionRequest);
    }

    public static SectionCountOutcome<string, TEvidence>
        ApplyCount<TProjection, TEvidence>(
            QuerySpaceSectionRowExecutionRequest<TProjection> request)
        where TEvidence : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTerminal(
            request.Terminal,
            QuerySpaceTerminalRequirement.Count);
        return SectionRowExecutor.ApplyCount<
            string,
            TProjection,
            TEvidence>(request.SectionRequest);
    }

    public static SectionSourceRowsOutcome<
        string,
        TProjection,
        TDisposition,
        TCompletionEvidence> ApplyRows<
            TProjection,
            TDisposition,
            TCompletionEvidence>(
                QuerySpaceSectionSourceRowExecutionRequest<
                    TProjection,
                    TDisposition,
                    TCompletionEvidence> request)
        where TDisposition : notnull
        where TCompletionEvidence : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTerminal(
            request.Terminal,
            QuerySpaceTerminalRequirement.Rows);
        return SectionSourceRowExecutor.ApplyRows(
            request.SectionRequest);
    }

    public static SectionCountOutcome<
        string,
        SectionRowSourceEvidence<
            TDisposition,
            TCompletionEvidence>> ApplyCount<
                TProjection,
                TDisposition,
                TCompletionEvidence>(
                    QuerySpaceSectionSourceRowExecutionRequest<
                        TProjection,
                        TDisposition,
                        TCompletionEvidence> request)
        where TDisposition : notnull
        where TCompletionEvidence : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTerminal(
            request.Terminal,
            QuerySpaceTerminalRequirement.Count);
        return SectionSourceRowExecutor.ApplyCount(
            request.SectionRequest);
    }

    private static void RequireTerminal(
        QuerySpaceTerminalRequirement actual,
        QuerySpaceTerminalRequirement required)
    {
        if (actual != required)
        {
            throw new InvalidOperationException(
                $"A QuerySpace '{actual}' request cannot execute the "
                + $"'{required}' terminal.");
        }
    }
}

public static class QuerySpaceSectionRowResolver
{
    public static QuerySpaceSectionSourceRowResolutionResult<
        TProjection,
        TDisposition,
        TCompletionEvidence> Resolve<
            TProjection,
            TDisposition,
            TCompletionEvidence>(
                QuerySpaceBinding querySpace,
                QuerySpaceRequest request,
                IReadOnlyList<
                    SectionRowSetDeclaration<
                        string,
                        TProjection>> rowSets,
                IReadOnlyList<
                    SectionRowSourceState<
                        string,
                        TDisposition,
                        TCompletionEvidence>> sources,
                SectionQuerySpaceRowScopeBinding scopeBinding)
        where TDisposition : notnull
        where TCompletionEvidence : notnull
    {
        QuerySpaceSectionRowResolutionResult<TProjection>
            completeSource =
                Resolve(
                    querySpace,
                    request,
                    rowSets,
                    scopeBinding);
        if (!completeSource.IsSuccess)
        {
            return QuerySpaceSectionSourceRowResolutionResult<
                TProjection,
                TDisposition,
                TCompletionEvidence>.Failed(
                    completeSource.Failure!);
        }

        QuerySpaceSectionRowExecutionRequest<TProjection>
            completeRequest =
                completeSource.Request!;
        SectionSourceRowExecutionRequest<
            string,
            TProjection,
            TDisposition,
            TCompletionEvidence> sectionRequest =
                SectionSourceRowExecutionRequest<
                    string,
                    TProjection,
                    TDisposition,
                    TCompletionEvidence>.Create(
                        completeRequest.SectionRequest.RowSets,
                        completeRequest.SectionRequest.Association,
                        sources);
        return QuerySpaceSectionSourceRowResolutionResult<
            TProjection,
            TDisposition,
            TCompletionEvidence>.Success(
                new(
                    completeRequest.StructuralRequest,
                    sectionRequest,
                    completeRequest.Association));
    }

    public static QuerySpaceSectionRowResolutionResult<TProjection>
        Resolve<TProjection>(
            QuerySpaceBinding querySpace,
            QuerySpaceRequest request,
            IReadOnlyList<
                SectionRowSetDeclaration<string, TProjection>> rowSets,
            SectionQuerySpaceRowScopeBinding scopeBinding)
    {
        ArgumentNullException.ThrowIfNull(querySpace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rowSets);
        ArgumentNullException.ThrowIfNull(scopeBinding);

        if (!string.Equals(
                querySpace.Descriptor.Identity,
                request.QuerySpace,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The QuerySpace request belongs to a different query space.",
                nameof(request));
        }

        QuerySpaceRequest validatedRequest =
            QuerySpaceRequest.Create(
                querySpace.Descriptor,
                request.Operation,
                request.ParticipatingRowSets,
                request.RowIntents,
                request.Terminal);
        if (validatedRequest.RowIntents.Count != 1)
        {
            throw new ArgumentException(
                "The complete-source QuerySpace section-row composition "
                + "requires exactly one explicit row-intent association.",
                nameof(request));
        }

        QuerySpaceRowIntentAssociation association =
            validatedRequest.RowIntents[0];
        if (!querySpace.TryGetRowScope(
                association.Scope,
                out QuerySpaceRowScopeBinding? registeredScope)
            || !ReferenceEquals(
                registeredScope,
                scopeBinding.QueryScope))
        {
            throw new ArgumentException(
                $"Row-query scope '{association.Scope}' has no matching "
                + "section execution binding.",
                nameof(scopeBinding));
        }

        SectionRowSetDeclaration<string, TProjection>[]
            participating =
                OrderParticipatingRowSets(
                    validatedRequest,
                    rowSets);
        foreach (SectionRowSetDeclaration<string, TProjection>
            rowSet in participating)
        {
            if (!ReferenceEquals(
                    rowSet.Schema,
                    scopeBinding.Schema))
            {
                throw new ArgumentException(
                    $"Participating row set '{rowSet.Identity}' uses a "
                    + "different schema from its QuerySpace scope binding.",
                    nameof(rowSets));
            }
        }

        SectionQuerySpaceScopeResolution resolution =
            scopeBinding.Resolve(association);
        if (resolution.Failure is not null)
        {
            return QuerySpaceSectionRowResolutionResult<TProjection>.Failed(
                new QuerySpaceSectionRowResolutionFailure(
                    association.Scope,
                    resolution.Failure));
        }

        bool countOnly =
            validatedRequest.Terminal
                is QuerySpaceTerminalRequirement.Count
            && resolution.SchemaBinding!
                .CanExecuteCountWithoutRows;
        for (int index = 0; index < participating.Length; index++)
        {
            participating[index] =
                participating[index].ResolveSnapshot(countOnly);
        }
        var sectionAssociation =
            new SectionRowIntentAssociation<string>(
                association.RowSets,
                [resolution.SchemaBinding!]);
        SectionRowExecutionRequest<string, TProjection>
            executionRequest =
                SectionRowExecutionRequest<string, TProjection>.Create(
                    participating,
                    sectionAssociation);
        return QuerySpaceSectionRowResolutionResult<TProjection>.Success(
            new QuerySpaceSectionRowExecutionRequest<TProjection>(
                validatedRequest,
                executionRequest,
                resolution.Association!));
    }

    private static SectionRowSetDeclaration<string, TProjection>[]
        OrderParticipatingRowSets<TProjection>(
            QuerySpaceRequest request,
            IReadOnlyList<
                SectionRowSetDeclaration<string, TProjection>> rowSets)
    {
        var byIdentity =
            new Dictionary<
                string,
                SectionRowSetDeclaration<string, TProjection>>(
                    StringComparer.Ordinal);
        foreach (SectionRowSetDeclaration<string, TProjection>
            rowSet in rowSets)
        {
            ArgumentNullException.ThrowIfNull(rowSet);
            if (!byIdentity.TryAdd(
                    rowSet.Identity,
                    rowSet))
            {
                throw new ArgumentException(
                    $"Section row set '{rowSet.Identity}' is duplicated.",
                    nameof(rowSets));
            }
        }

        var participating =
            new SectionRowSetDeclaration<
                string,
                TProjection>[request.ParticipatingRowSets.Count];
        for (int index = 0;
             index < request.ParticipatingRowSets.Count;
             index++)
        {
            string identity =
                request.ParticipatingRowSets[index];
            if (!byIdentity.TryGetValue(
                    identity,
                    out SectionRowSetDeclaration<
                        string,
                        TProjection>? rowSet))
            {
                throw new ArgumentException(
                    $"Participating row set '{identity}' has no section "
                    + "declaration.",
                    nameof(rowSets));
            }
            participating[index] = rowSet;
        }

        if (byIdentity.Count != participating.Length)
        {
            throw new ArgumentException(
                "Section row declarations include a nonparticipating row set.",
                nameof(rowSets));
        }

        return participating;
    }
}

internal sealed class SectionQuerySpaceScopeResolution
{
    private SectionQuerySpaceScopeResolution(
        SectionRowSchemaBinding<string>? schemaBinding,
        ResolvedQuerySpaceRowAssociation? association,
        RowQueryFailure? failure)
    {
        SchemaBinding = schemaBinding;
        Association = association;
        Failure = failure;
    }

    public SectionRowSchemaBinding<string>? SchemaBinding { get; }

    public ResolvedQuerySpaceRowAssociation? Association { get; }

    public RowQueryFailure? Failure { get; }

    public static SectionQuerySpaceScopeResolution Success(
        SectionRowSchemaBinding<string> schemaBinding,
        ResolvedQuerySpaceRowAssociation association) =>
        new(schemaBinding, association, null);

    public static SectionQuerySpaceScopeResolution Failed(
        RowQueryFailure failure) =>
        new(null, null, failure);
}
