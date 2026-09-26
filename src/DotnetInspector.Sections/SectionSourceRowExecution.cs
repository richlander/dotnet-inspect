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
            TProjection>? rowsResidualRequest,
        SectionRowExecutionRequest<
            TIdentity,
            TProjection>? countResidualRequest)
    {
        RowSets = rowSets;
        Sources = sources;
        RowsResidualRequest = rowsResidualRequest;
        CountResidualRequest = countResidualRequest;
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
        TProjection>? RowsResidualRequest
    { get; }

    internal SectionRowExecutionRequest<
        TIdentity,
        TProjection>? CountResidualRequest
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
        ArgumentNullException.ThrowIfNull(rowSets);
        ArgumentNullException.ThrowIfNull(association);
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count != rowSets.Count)
        {
            throw new ArgumentException(
                "A source-aware section-row request requires one source "
                + "state for every participating row set.",
                nameof(sources));
        }

        var resolvedRowSets =
            new SectionRowSetDeclaration<
                TIdentity,
                TProjection>[rowSets.Count];
        var sourceCopy =
            new SectionRowSourceState<
                TIdentity,
                TDisposition,
                TCompletionEvidence>[sources.Count];
        var exactCountIdentities = new HashSet<TIdentity>();
        var usableIdentities = new HashSet<TIdentity>();
        var countResidualIdentities = new HashSet<TIdentity>();
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
                rowSet =
                    rowSets[index]
                    ?? throw new ArgumentNullException(
                        nameof(rowSets),
                        $"Declared row set {index + 1} is null.");
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
            if (source.ExactCount is int exactCount)
            {
                exactCountIdentities.Add(source.Identity);
                resolvedRowSets[index] =
                    rowSet.ResolveExactCountSnapshot(exactCount);
            }
            else
            {
                resolvedRowSets[index] = rowSet;
            }
            if (source.RowsAreUsable)
            {
                usableIdentities.Add(source.Identity);
            }
            if (source.CountIsSufficient
                && source.ExactCount is null)
            {
                countResidualIdentities.Add(source.Identity);
            }
        }

        SectionRowExecutionRequest<TIdentity, TProjection>
            validated =
                SectionRowExecutionRequest<
                    TIdentity,
                    TProjection>.Create(
                        resolvedRowSets,
                        association,
                        exactCountIdentities);
        SectionRowExecutionRequest<
            TIdentity,
            TProjection>? rowsResidualRequest =
                validated.CreateSubset(usableIdentities);
        SectionRowExecutionRequest<
            TIdentity,
            TProjection>? countResidualRequest =
                validated.CreateSubset(countResidualIdentities);

        return new(
            validated.RowSets,
            SectionContractSnapshot.Own(sourceCopy),
            rowsResidualRequest,
            countResidualRequest);
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
        if (request.RowsResidualRequest is not null)
        {
            SectionRowsOutcome<TIdentity, TProjection> selected =
                SectionRowExecutor.ApplyRows(
                    request.RowsResidualRequest);
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

        var countsByIdentity =
            new Dictionary<TIdentity, int>();
        foreach (SectionRowSourceState<
            TIdentity,
            TDisposition,
            TCompletionEvidence> source in request.Sources)
        {
            if (source.ExactCount is int exactCount)
            {
                countsByIdentity.Add(
                    source.Identity,
                    exactCount);
            }
        }

        if (request.CountResidualRequest is not null)
        {
            SectionCountOutcome<
                TIdentity,
                SectionRowSourceEvidence<
                    TDisposition,
                    TCompletionEvidence>> residual =
                        SectionRowExecutor.ApplyCount<
                            TIdentity,
                            TProjection,
                            SectionRowSourceEvidence<
                                TDisposition,
                                TCompletionEvidence>>(
                                    request.CountResidualRequest);
            if (residual is SectionCountOutcome<
                    TIdentity,
                    SectionRowSourceEvidence<
                        TDisposition,
                        TCompletionEvidence>>.Semantic semantic)
            {
                return semantic;
            }
            if (residual is not SectionCountOutcome<
                    TIdentity,
                    SectionRowSourceEvidence<
                        TDisposition,
                        TCompletionEvidence>>.Completed completed)
            {
                throw new InvalidOperationException(
                    "Residual section-row Count returned a source outcome.");
            }

            foreach (SectionCountEntry<TIdentity> count
                in completed.Counts)
            {
                if (!countsByIdentity.TryAdd(
                        count.Identity,
                        count.Value))
                {
                    throw new InvalidOperationException(
                        "Source-aware Count produced a row set more than "
                        + "once.");
                }
            }
        }

        var counts =
            new SectionCountEntry<TIdentity>[request.Sources.Count];
        for (int index = 0;
             index < request.Sources.Count;
             index++)
        {
            TIdentity identity =
                request.Sources[index].Identity;
            if (!countsByIdentity.TryGetValue(
                    identity,
                    out int count))
            {
                throw new InvalidOperationException(
                    "Source-aware Count omitted a participating row set.");
            }
            counts[index] = new(identity, count);
        }

        return new SectionCountOutcome<
            TIdentity,
            SectionRowSourceEvidence<
                TDisposition,
                TCompletionEvidence>>.Completed(counts);
    }
}
