namespace DotnetInspector.Sections;

public sealed class SectionSourceRowExecutionRequest<
    TIdentity,
    TProjection,
    TDisposition,
    TCompletionEvidence>
    where TIdentity : notnull
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    private SectionSourceRowExecutionRequest(
        IReadOnlyList<
            SectionRowSetDeclaration<TIdentity, TProjection>> rowSets,
        IReadOnlyList<
            SectionRowSourceState<
                TIdentity,
                TDisposition,
                TCompletionEvidence>> sources,
        SectionRowExecutionRequest<
            TIdentity,
            TProjection>? residualRequest)
    {
        RowSets = rowSets;
        Sources = sources;
        ResidualRequest = residualRequest;
    }

    public IReadOnlyList<
        SectionRowSetDeclaration<TIdentity, TProjection>> RowSets
    { get; }

    public IReadOnlyList<
        SectionRowSourceState<
            TIdentity,
            TDisposition,
            TCompletionEvidence>> Sources
    { get; }

    internal SectionRowExecutionRequest<
        TIdentity,
        TProjection>? ResidualRequest
    { get; }

    public static SectionSourceRowExecutionRequest<
        TIdentity,
        TProjection,
        TDisposition,
        TCompletionEvidence> Create(
            IReadOnlyList<
                SectionRowSetDeclaration<
                    TIdentity,
                    TProjection>> rowSets,
            SectionRowIntentAssociation<TIdentity> association,
            IReadOnlyList<
                SectionRowSourceState<
                    TIdentity,
                    TDisposition,
                    TCompletionEvidence>> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        SectionRowExecutionRequest<TIdentity, TProjection>
            validated =
                SectionRowExecutionRequest<
                    TIdentity,
                    TProjection>.Create(
                        rowSets,
                        association);
        if (sources.Count != validated.RowSets.Count)
        {
            throw new ArgumentException(
                "A source-aware section-row request requires one source "
                + "state for every participating row set.",
                nameof(sources));
        }

        var sourceCopy =
            new SectionRowSourceState<
                TIdentity,
                TDisposition,
                TCompletionEvidence>[sources.Count];
        var usableIdentities = new HashSet<TIdentity>();
        for (int index = 0; index < sources.Count; index++)
        {
            SectionRowSourceState<
                TIdentity,
                TDisposition,
                TCompletionEvidence> source =
                    sources[index]
                    ?? throw new ArgumentNullException(
                        nameof(sources),
                        $"Source state {index + 1} is null.");
            SectionRowSetDeclaration<TIdentity, TProjection>
                rowSet = validated.RowSets[index];
            if (!EqualityComparer<TIdentity>.Default.Equals(
                    source.Identity,
                    rowSet.Identity))
            {
                throw new ArgumentException(
                    $"Source state {index + 1} does not match its declared "
                    + "row-set identity.",
                    nameof(sources));
            }

            sourceCopy[index] = source;
            if (source.RowsAreUsable)
            {
                usableIdentities.Add(source.Identity);
            }
        }

        SectionRowExecutionRequest<
            TIdentity,
            TProjection>? residualRequest =
                validated.CreateSubset(usableIdentities);

        return new(
            validated.RowSets,
            SectionContractSnapshot.Own(sourceCopy),
            residualRequest);
    }
}

public static class SectionSourceRowExecutor
{
    public static SectionSourceRowsOutcome<
        TIdentity,
        TProjection,
        TDisposition,
        TCompletionEvidence> ApplyRows<
            TIdentity,
            TProjection,
            TDisposition,
            TCompletionEvidence>(
                SectionSourceRowExecutionRequest<
                    TIdentity,
                    TProjection,
                    TDisposition,
                    TCompletionEvidence> request)
        where TIdentity : notnull
        where TDisposition : notnull
        where TCompletionEvidence : notnull
    {
        ArgumentNullException.ThrowIfNull(request);

        var selectedByIdentity =
            new Dictionary<
                TIdentity,
                SectionRowSetResult<TIdentity, TProjection>>();
        if (request.ResidualRequest is not null)
        {
            SectionRowsOutcome<TIdentity, TProjection> selected =
                SectionRowExecutor.ApplyRows(
                    request.ResidualRequest);
            if (!selected.IsSuccess)
            {
                return SectionSourceRowsOutcome<
                    TIdentity,
                    TProjection,
                    TDisposition,
                    TCompletionEvidence>.Failed(
                        selected.Failure!);
            }

            foreach (SectionRowSetResult<TIdentity, TProjection>
                rowSet in selected.RowSets)
            {
                selectedByIdentity.Add(
                    rowSet.Identity,
                    rowSet);
            }
        }

        var outcomes =
            new SectionSourceRowSetResult<
                TIdentity,
                TProjection,
                TDisposition,
                TCompletionEvidence>[request.Sources.Count];
        for (int index = 0; index < request.Sources.Count; index++)
        {
            SectionRowSourceState<
                TIdentity,
                TDisposition,
                TCompletionEvidence> source =
                    request.Sources[index];
            selectedByIdentity.TryGetValue(
                source.Identity,
                out SectionRowSetResult<
                    TIdentity,
                    TProjection>? selectedRows);
            if (source.RowsAreUsable != (selectedRows is not null))
            {
                throw new InvalidOperationException(
                    "Section-row residual execution did not match the "
                    + "source usability contract.");
            }
            outcomes[index] =
                new(source, selectedRows);
        }

        return SectionSourceRowsOutcome<
            TIdentity,
            TProjection,
            TDisposition,
            TCompletionEvidence>.Success(outcomes);
    }

    public static SectionCountOutcome<
        TIdentity,
        SectionRowSourceEvidence<
            TDisposition,
            TCompletionEvidence>> ApplyCount<
                TIdentity,
                TProjection,
                TDisposition,
                TCompletionEvidence>(
                    SectionSourceRowExecutionRequest<
                        TIdentity,
                        TProjection,
                        TDisposition,
                        TCompletionEvidence> request)
        where TIdentity : notnull
        where TDisposition : notnull
        where TCompletionEvidence : notnull
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Sources.Any(
                static source => !source.CountIsSufficient))
        {
            var sources =
                new SectionCountSourceEvidence<
                    TIdentity,
                    SectionRowSourceEvidence<
                        TDisposition,
                        TCompletionEvidence>>[request.Sources.Count];
            for (int index = 0;
                 index < request.Sources.Count;
                 index++)
            {
                SectionRowSourceState<
                    TIdentity,
                    TDisposition,
                    TCompletionEvidence> source =
                        request.Sources[index];
                sources[index] =
                    new(
                        source.Identity,
                        source.Evidence);
            }
            return new SectionCountOutcome<
                TIdentity,
                SectionRowSourceEvidence<
                    TDisposition,
                    TCompletionEvidence>>.SourceForCount(
                        sources);
        }

        if (request.ResidualRequest is null)
        {
            throw new InvalidOperationException(
                "A Count-sufficient source-aware request has no residual "
                + "row execution.");
        }

        return SectionRowExecutor.ApplyCount<
            TIdentity,
            TProjection,
            SectionRowSourceEvidence<
                TDisposition,
                TCompletionEvidence>>(
                    request.ResidualRequest);
    }
}
