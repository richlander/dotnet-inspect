using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public sealed record StructuralCloneCorpusDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] ImmutableArray<StructuralCloneCorpusCase> Cases,
    [property: JsonRequired] StructuralCloneCorpusDiscovery Discovery,
    [property: JsonRequired] StructuralCloneCorpusRetrieval Retrieval);

public sealed record StructuralCloneCorpusDiscovery(
    [property: JsonRequired]
    ImmutableArray<StructuralCloneCorpusMethod> Population);

public sealed record StructuralCloneCorpusRetrieval(
    [property: JsonRequired]
    ImmutableArray<StructuralCloneCorpusRetrievalQuery> Queries);

public sealed record StructuralCloneCorpusRetrievalQuery(
    [property: JsonRequired] string Id,
    [property: JsonRequired] StructuralCloneCorpusMethod Seed,
    [property: JsonRequired]
    ImmutableArray<StructuralCloneCorpusRetrievalExpectation> Expectations);

public sealed record StructuralCloneCorpusRetrievalExpectation(
    [property: JsonRequired] StructuralCloneCorpusMethod Candidate,
    [property: JsonRequired] int MaximumRank,
    [property: JsonRequired]
    ImmutableArray<StructuralCloneCorpusMethod> ScoresAbove);

public sealed record StructuralCloneCorpusCase(
    [property: JsonRequired] string Id,
    [property: JsonRequired] StructuralCloneCorpusMethod Left,
    [property: JsonRequired] StructuralCloneCorpusMethod Right,
    [property: JsonRequired] StructuralCloneDisposition ExpectedDisposition,
    [property: JsonRequired] StructuralCloneRelation? ExpectedRelation,
    [property: JsonRequired] string Difficulty,
    [property: JsonRequired] string Intent,
    [property: JsonRequired] string Actionability,
    [property: JsonRequired] ImmutableArray<string> Tags,
    StructuralCloneCorpusEditSummary? ExpectedEdits);

public sealed record StructuralCloneCorpusEditSummary(
    int InsertedBlocks,
    int RemovedBlocks,
    int ChangedBlocks,
    int InsertedOperations,
    int RemovedOperations,
    int ChangedOperations,
    int InsertedEdges,
    int RemovedEdges,
    int ChangedEdges);

public sealed record StructuralCloneCorpusMethod(
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Method);

public sealed record StructuralCloneCorpusCaseResult(
    string Id,
    StructuralCloneCorpusMethod Left,
    StructuralCloneCorpusMethod Right,
    StructuralCloneDisposition ExpectedDisposition,
    StructuralCloneRelation? ExpectedRelation,
    StructuralCloneCorpusEditSummary? ExpectedEdits,
    StructuralCloneDisposition ActualDisposition,
    StructuralCloneRelation? ActualRelation,
    StructuralCloneCorrespondenceKind? Correspondence,
    ImmutableArray<StructuralCloneCorpusEditSummary> ActualEdits,
    ImmutableArray<StructuralCloneBlocker> Blockers,
    StructuralCloneVerificationReceipt Receipt,
    bool Passed);

public sealed record StructuralCloneCorpusCluster(
    ImmutableArray<StructuralCloneCorpusMethod> Members);

public sealed record StructuralCloneCorpusDiscoveryResult(
    StructuralCloneDiscoveryDisposition Disposition,
    ImmutableArray<StructuralCloneCorpusCluster> ExpectedClusters,
    ImmutableArray<StructuralCloneCorpusCluster> ActualClusters,
    ImmutableArray<StructuralCloneSuppressedBucket> SuppressedBuckets,
    ImmutableArray<StructuralCloneDiscoveryBlocker> Blockers,
    StructuralCloneDiscoveryReceipt Receipt,
    bool Passed);

public sealed record StructuralCloneCorpusRankedCandidate(
    StructuralCloneCorpusMethod Method,
    int Rank,
    StructuralCloneSimilarityEvidence Similarity);

public sealed record StructuralCloneCorpusRetrievalExpectationResult(
    StructuralCloneCorpusRetrievalExpectation Expectation,
    StructuralCloneCorpusRankedCandidate? Actual,
    ImmutableArray<StructuralCloneCorpusRankedCandidate> Contrasts,
    bool Passed);

public sealed record StructuralCloneCorpusRetrievalQueryResult(
    string Id,
    StructuralCloneCorpusMethod Seed,
    StructuralCloneRetrievalDisposition Disposition,
    ImmutableArray<StructuralCloneCorpusRetrievalExpectationResult>
        Expectations,
    ImmutableArray<StructuralCloneRetrievalBlocker> Blockers,
    StructuralCloneRetrievalReceipt Receipt,
    bool Passed);

public sealed record StructuralCloneCorpusRetrievalResult(
    ImmutableArray<StructuralCloneCorpusRetrievalQueryResult> Queries,
    bool Passed);

public sealed record StructuralCloneCorpusReport(
    string Assembly,
    int Total,
    int Passed,
    ImmutableArray<StructuralCloneCorpusCaseResult> Cases,
    StructuralCloneCorpusDiscoveryResult Discovery,
    StructuralCloneCorpusRetrievalResult Retrieval)
{
    public bool Success =>
        Total > 0
        && Passed == Total
        && Discovery.Passed
        && Retrieval.Passed;
}

public static class StructuralCloneCorpus
{
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false),
        },
    };

    static readonly ImmutableHashSet<string> s_difficulties =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "banal",
            "challenging");
    static readonly ImmutableHashSet<string> s_intents =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "authored-duplicate",
            "authored-near",
            "authored-hard-negative",
            "control-flow-contrast",
            "semantic-hazard",
            "unsupported-boundary");
    static readonly ImmutableHashSet<string> s_actionabilities =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "actionable",
            "diagnostic",
            "none");

    public static StructuralCloneCorpusDocument Load(string json)
    {
        StructuralCloneCorpusDocument document =
            JsonSerializer.Deserialize<StructuralCloneCorpusDocument>(
                json,
                s_json)
            ?? throw new InvalidDataException(
                "The structural clone relationship ledger is empty.");
        Validate(document);
        return document;
    }

    public static StructuralCloneCorpusReport Run(
        string assemblyPath,
        StructuralCloneCorpusDocument corpus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(corpus);
        Validate(corpus);

        using PEReader image =
            new(File.OpenRead(Path.GetFullPath(assemblyPath)));
        MetadataReader reader;
        try
        {
            if (!image.HasMetadata)
            {
                throw new InvalidDataException(
                    $"The clone corpus target is not a managed assembly: "
                        + assemblyPath);
            }
            reader = image.GetMetadataReader();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
        {
            throw new InvalidDataException(
                $"The clone corpus target has invalid managed metadata: "
                    + assemblyPath,
                ex);
        }
        ImmutableArray<StructuralCloneCorpusCaseResult>.Builder results =
            ImmutableArray.CreateBuilder<StructuralCloneCorpusCaseResult>(
                corpus.Cases.Length);
        foreach (StructuralCloneCorpusCase item in corpus.Cases)
        {
            MethodDefinitionHandle left = Resolve(reader, item.Left);
            MethodDefinitionHandle right = Resolve(reader, item.Right);
            StructuralCloneComparison comparison =
                StructuralCloneAnalysis.Compare(image, left, right);
            bool passed =
                comparison.Disposition == item.ExpectedDisposition
                && comparison.Relation == item.ExpectedRelation;
            ImmutableArray<StructuralCloneCorpusEditSummary> actualEdits =
                Summaries(comparison);
            if (item.ExpectedEdits is { } expectedEdits)
            {
                passed = passed
                    && !actualEdits.IsEmpty
                    && actualEdits.All(edit => edit == expectedEdits);
            }
            results.Add(
                new StructuralCloneCorpusCaseResult(
                    item.Id,
                    item.Left,
                    item.Right,
                    item.ExpectedDisposition,
                    item.ExpectedRelation,
                    item.ExpectedEdits,
                    comparison.Disposition,
                    comparison.Relation,
                    comparison.Correspondence?.Kind
                        ?? comparison.Alignment?.Kind,
                    actualEdits,
                    comparison.Blockers,
                    comparison.Receipt,
                    passed));
        }

        var methodsByHandle =
            new Dictionary<MethodDefinitionHandle, StructuralCloneCorpusMethod>();
        ImmutableArray<MethodDefinitionHandle>.Builder population =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>(
                corpus.Discovery.Population.Length);
        foreach (StructuralCloneCorpusMethod method
            in corpus.Discovery.Population)
        {
            MethodDefinitionHandle handle = Resolve(reader, method);
            methodsByHandle.Add(handle, method);
            population.Add(handle);
        }
        StructuralCloneDiscoveryResult discovery =
            StructuralCloneAnalysis.Discover(
                image,
                population.ToImmutable());
        ImmutableArray<StructuralCloneCorpusCluster> expectedClusters =
            ExpectedClusters(corpus);
        ImmutableArray<StructuralCloneCorpusCluster> actualClusters =
        [
            .. discovery.Clusters
                .Select(cluster =>
                    new StructuralCloneCorpusCluster(
                        [
                            .. cluster.Members
                                .Select(member =>
                                    methodsByHandle[member.Handle])
                                .OrderBy(MethodKey, StringComparer.Ordinal),
                        ]))
                .OrderBy(ClusterKey, StringComparer.Ordinal),
        ];
        bool discoveryPassed =
            discovery.Disposition
                == StructuralCloneDiscoveryDisposition.Completed
            && discovery.SuppressedBuckets.IsEmpty
            && expectedClusters
                .Select(ClusterKey)
                .SequenceEqual(
                    actualClusters.Select(ClusterKey),
                    StringComparer.Ordinal);

        ImmutableArray<StructuralCloneCorpusRetrievalQueryResult>.Builder
            retrievalResults =
                ImmutableArray.CreateBuilder<
                    StructuralCloneCorpusRetrievalQueryResult>(
                    corpus.Retrieval.Queries.Length);
        foreach (StructuralCloneCorpusRetrievalQuery query
            in corpus.Retrieval.Queries)
        {
            MethodDefinitionHandle seed = Resolve(reader, query.Seed);
            StructuralCloneRetrievalResult retrieval =
                StructuralCloneAnalysis.RetrieveSimilar(
                    image,
                    seed,
                    population.ToImmutable(),
                    new StructuralCloneRetrievalLimits(
                        MaximumMethods: population.Count,
                        MaximumResults: population.Count));
            var rankedByMethod =
                new Dictionary<string, StructuralCloneCorpusRankedCandidate>(
                    StringComparer.Ordinal);
            foreach (StructuralCloneRetrievalCandidate candidate
                in retrieval.Candidates)
            {
                StructuralCloneCorpusMethod method =
                    methodsByHandle[candidate.Method.Handle];
                rankedByMethod.Add(
                    MethodKey(method),
                    new StructuralCloneCorpusRankedCandidate(
                        method,
                        candidate.Rank,
                        candidate.Similarity));
            }

            ImmutableArray<
                StructuralCloneCorpusRetrievalExpectationResult>.Builder
                    expectations =
                        ImmutableArray.CreateBuilder<
                            StructuralCloneCorpusRetrievalExpectationResult>(
                            query.Expectations.Length);
            foreach (StructuralCloneCorpusRetrievalExpectation expectation
                in query.Expectations)
            {
                rankedByMethod.TryGetValue(
                    MethodKey(expectation.Candidate),
                    out StructuralCloneCorpusRankedCandidate? actual);
                ImmutableArray<StructuralCloneCorpusRankedCandidate> contrasts =
                [
                    .. expectation.ScoresAbove
                        .Select(method =>
                            rankedByMethod.GetValueOrDefault(
                                MethodKey(method)))
                        .Where(static candidate => candidate is not null)
                        .Select(static candidate => candidate!),
                ];
                bool passed =
                    retrieval.Disposition
                        == StructuralCloneRetrievalDisposition.Completed
                    && actual is not null
                    && actual.Rank <= expectation.MaximumRank
                    && contrasts.Length == expectation.ScoresAbove.Length
                    && contrasts.All(contrast =>
                        actual.Similarity.Score
                            > contrast.Similarity.Score);
                expectations.Add(
                    new StructuralCloneCorpusRetrievalExpectationResult(
                        expectation,
                        actual,
                        contrasts,
                        passed));
            }
            ImmutableArray<
                StructuralCloneCorpusRetrievalExpectationResult>
                    queryExpectations = expectations.ToImmutable();
            retrievalResults.Add(
                new StructuralCloneCorpusRetrievalQueryResult(
                    query.Id,
                    query.Seed,
                    retrieval.Disposition,
                    queryExpectations,
                    retrieval.Blockers,
                    retrieval.Receipt,
                    retrieval.Disposition
                        == StructuralCloneRetrievalDisposition.Completed
                    && queryExpectations.All(static item => item.Passed)));
        }

        ImmutableArray<StructuralCloneCorpusCaseResult> cases =
            results.ToImmutable();
        ImmutableArray<StructuralCloneCorpusRetrievalQueryResult>
            retrievalQueries = retrievalResults.ToImmutable();
        return new StructuralCloneCorpusReport(
            Path.GetFullPath(assemblyPath),
            cases.Length,
            cases.Count(static item => item.Passed),
            cases,
            new StructuralCloneCorpusDiscoveryResult(
                discovery.Disposition,
                expectedClusters,
                actualClusters,
                discovery.SuppressedBuckets,
                discovery.Blockers,
                discovery.Receipt,
                discoveryPassed),
            new StructuralCloneCorpusRetrievalResult(
                retrievalQueries,
                retrievalQueries.All(static query => query.Passed)));
    }

    public static string ToJson(StructuralCloneCorpusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string Format(StructuralCloneCorpusReport report)
    {
        StringBuilder output = new();
        output.AppendLine(
            $"Structural clone relationship corpus: {report.Passed}/{report.Total} passed");
        foreach (StructuralCloneCorpusCaseResult item in report.Cases)
        {
            output.Append(item.Passed ? "PASS " : "FAIL ");
            output.Append(item.Id);
            output.Append(": expected ");
            AppendOutcome(
                output,
                item.ExpectedDisposition,
                item.ExpectedRelation);
            output.Append(", actual ");
            AppendOutcome(
                output,
                item.ActualDisposition,
                item.ActualRelation);
            if (item.Correspondence is { } correspondence)
            {
                output.Append(" (");
                output.Append(correspondence);
                output.Append(
                    item.ActualRelation == StructuralCloneRelation.Near
                        ? " alignment)"
                        : " correspondence)");
            }
            if (!item.ActualEdits.IsEmpty)
            {
                output.Append(": edits ");
                output.Append(string.Join(
                    " | ",
                    item.ActualEdits.Select(FormatEdits)));
            }
            output.AppendLine();
        }
        output.Append(report.Discovery.Passed ? "PASS " : "FAIL ");
        output.Append("closed-world exact discovery: expected ");
        output.Append(report.Discovery.ExpectedClusters.Length);
        output.Append(" clusters, actual ");
        output.Append(report.Discovery.ActualClusters.Length);
        output.Append(" clusters, disposition ");
        output.AppendLine(report.Discovery.Disposition.ToString());
        foreach (StructuralCloneCorpusCluster cluster
            in report.Discovery.ActualClusters)
        {
            output.Append("  ");
            output.AppendLine(string.Join(
                " = ",
                cluster.Members.Select(static member =>
                    $"{member.Type}::{member.Method}")));
        }
        foreach (StructuralCloneCorpusRetrievalQueryResult query
            in report.Retrieval.Queries)
        {
            output.Append(query.Passed ? "PASS " : "FAIL ");
            output.Append("fuzzy retrieval ");
            output.Append(query.Id);
            output.Append(": seed ");
            output.Append(query.Seed.Type);
            output.Append("::");
            output.AppendLine(query.Seed.Method);
            foreach (StructuralCloneCorpusRetrievalExpectationResult expectation
                in query.Expectations)
            {
                output.Append(expectation.Passed ? "  PASS " : "  FAIL ");
                output.Append(expectation.Expectation.Candidate.Type);
                output.Append("::");
                output.Append(expectation.Expectation.Candidate.Method);
                output.Append(" rank=");
                output.Append(
                    expectation.Actual?.Rank.ToString()
                    ?? "<missing>");
                output.Append(" score=");
                output.AppendLine(
                    expectation.Actual?.Similarity.Score.ToString()
                    ?? "<missing>");
            }
        }
        return output.ToString();
    }

    static void Validate(StructuralCloneCorpusDocument document)
    {
        if (document.SchemaVersion != 4)
        {
            throw new InvalidDataException(
                $"Unsupported structural clone corpus schema {document.SchemaVersion}; expected 4.");
        }
        if (document.Cases.IsDefaultOrEmpty)
            throw new InvalidDataException(
                "The structural clone relationship ledger has no cases.");
        if (document.Discovery is null
            || document.Discovery.Population.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The structural clone relationship ledger has no closed-world discovery population.");
        }
        if (document.Retrieval is null
            || document.Retrieval.Queries.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The structural clone relationship ledger has no retrieval queries.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> caseMethods = new(StringComparer.Ordinal);
        foreach (StructuralCloneCorpusCase item in document.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new InvalidDataException(
                    $"Corpus case ids must be non-empty and unique: '{item.Id}'.");
            ValidateMethod(item.Id, "left", item.Left);
            ValidateMethod(item.Id, "right", item.Right);
            caseMethods.Add(MethodKey(item.Left));
            caseMethods.Add(MethodKey(item.Right));
            if (item.ExpectedDisposition == StructuralCloneDisposition.Completed
                ^ item.ExpectedRelation is not null)
            {
                throw new InvalidDataException(
                    $"Corpus case '{item.Id}' must separate disposition from relation: completed requires a relation and other dispositions forbid one.");
            }
            if ((item.ExpectedRelation == StructuralCloneRelation.Near)
                != (item.ExpectedEdits is not null))
            {
                throw new InvalidDataException(
                    $"Corpus case '{item.Id}' must declare expectedEdits "
                        + "exactly when its relation is Near.");
            }
            if (item.ExpectedEdits is { } edits
                && new[]
                {
                    edits.InsertedBlocks,
                    edits.RemovedBlocks,
                    edits.ChangedBlocks,
                    edits.InsertedOperations,
                    edits.RemovedOperations,
                    edits.ChangedOperations,
                    edits.InsertedEdges,
                    edits.RemovedEdges,
                    edits.ChangedEdges,
                }.Any(static count => count < 0))
            {
                throw new InvalidDataException(
                    $"Corpus case '{item.Id}' expectedEdits counts "
                        + "must be non-negative.");
            }
            if (!s_difficulties.Contains(item.Difficulty))
                throw InvalidAxis(item.Id, "difficulty", item.Difficulty);
            if (!s_intents.Contains(item.Intent))
                throw InvalidAxis(item.Id, "intent", item.Intent);
            if (!s_actionabilities.Contains(item.Actionability))
                throw InvalidAxis(
                    item.Id,
                    "actionability",
                    item.Actionability);
            if (item.Tags.IsDefault)
                throw new InvalidDataException(
                    $"Corpus case '{item.Id}' must declare a tags array.");
        }

        HashSet<string> population = new(StringComparer.Ordinal);
        foreach (StructuralCloneCorpusMethod method
            in document.Discovery.Population)
        {
            ValidateMethod("discovery", "population", method);
            if (!population.Add(MethodKey(method)))
            {
                throw new InvalidDataException(
                    $"Discovery population method '{method.Type}::{method.Method}' is duplicated.");
            }
        }
        if (!caseMethods.SetEquals(population))
        {
            throw new InvalidDataException(
                "The closed-world discovery population must equal the distinct methods declared by relationship cases.");
        }

        HashSet<string> queryIds = new(StringComparer.Ordinal);
        foreach (StructuralCloneCorpusRetrievalQuery query
            in document.Retrieval.Queries)
        {
            if (string.IsNullOrWhiteSpace(query.Id)
                || !queryIds.Add(query.Id))
            {
                throw new InvalidDataException(
                    $"Retrieval query ids must be non-empty and unique: "
                        + $"'{query.Id}'.");
            }
            ValidateMethod(query.Id, "seed", query.Seed);
            string seed = MethodKey(query.Seed);
            if (!population.Contains(seed))
            {
                throw new InvalidDataException(
                    $"Retrieval query '{query.Id}' seed is outside the "
                        + "discovery population.");
            }
            if (query.Expectations.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    $"Retrieval query '{query.Id}' has no expectations.");
            }
            HashSet<string> expected = new(StringComparer.Ordinal);
            foreach (StructuralCloneCorpusRetrievalExpectation expectation
                in query.Expectations)
            {
                ValidateMethod(
                    query.Id,
                    "candidate",
                    expectation.Candidate);
                string candidate = MethodKey(expectation.Candidate);
                if (candidate == seed
                    || !population.Contains(candidate)
                    || !expected.Add(candidate))
                {
                    throw new InvalidDataException(
                        $"Retrieval query '{query.Id}' has an invalid or "
                            + $"duplicate candidate '{candidate}'.");
                }
                if (expectation.MaximumRank < 1)
                {
                    throw new InvalidDataException(
                        $"Retrieval query '{query.Id}' maximum rank must be "
                            + "positive.");
                }
                if (expectation.ScoresAbove.IsDefault)
                {
                    throw new InvalidDataException(
                        $"Retrieval query '{query.Id}' scoresAbove must be "
                            + "initialized.");
                }
                HashSet<string> contrasts = new(StringComparer.Ordinal);
                foreach (StructuralCloneCorpusMethod contrast
                    in expectation.ScoresAbove)
                {
                    ValidateMethod(query.Id, "contrast", contrast);
                    string key = MethodKey(contrast);
                    if (key == candidate
                        || key == seed
                        || !population.Contains(key)
                        || !contrasts.Add(key))
                    {
                        throw new InvalidDataException(
                            $"Retrieval query '{query.Id}' has an invalid "
                                + $"contrast '{key}'.");
                    }
                }
            }
        }
    }

    static ImmutableArray<StructuralCloneCorpusCluster> ExpectedClusters(
        StructuralCloneCorpusDocument corpus)
    {
        Dictionary<string, string> parent =
            corpus.Discovery.Population.ToDictionary(
                MethodKey,
                MethodKey,
                StringComparer.Ordinal);

        foreach (StructuralCloneCorpusCase item in corpus.Cases)
        {
            if (item.ExpectedDisposition
                    == StructuralCloneDisposition.Completed
                && item.ExpectedRelation == StructuralCloneRelation.Exact)
            {
                Union(
                    parent,
                    MethodKey(item.Left),
                    MethodKey(item.Right));
            }

        }

        return
        [
            .. corpus.Discovery.Population
                .GroupBy(
                    method => Find(parent, MethodKey(method)),
                    StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(group =>
                    new StructuralCloneCorpusCluster(
                        [
                            .. group.OrderBy(
                                MethodKey,
                                StringComparer.Ordinal),
                        ]))
                .OrderBy(ClusterKey, StringComparer.Ordinal),
        ];
    }

    static ImmutableArray<StructuralCloneCorpusEditSummary> Summaries(
        StructuralCloneComparison comparison)
        => comparison.Alignment is null
            ? []
            :
            [
                .. comparison.Alignment.Alternatives.Select(
                    static alternative => new StructuralCloneCorpusEditSummary(
                        alternative.Blocks.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Inserted),
                        alternative.Blocks.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Removed),
                        alternative.Blocks.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Changed),
                        alternative.Operations.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Inserted),
                        alternative.Operations.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Removed),
                        alternative.Operations.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Changed),
                        alternative.Edges.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Inserted),
                        alternative.Edges.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Removed),
                        alternative.Edges.Count(static edit =>
                            edit.Kind == StructuralCloneEditKind.Changed))),
            ];

    static void Union(
        Dictionary<string, string> parent,
        string left,
        string right)
    {
        string leftRoot = Find(parent, left);
        string rightRoot = Find(parent, right);
        if (!StringComparer.Ordinal.Equals(leftRoot, rightRoot))
            parent[rightRoot] = leftRoot;
    }

    static string Find(
        Dictionary<string, string> parent,
        string member)
    {
        string root = member;
        while (!StringComparer.Ordinal.Equals(root, parent[root]))
            root = parent[root];
        while (!StringComparer.Ordinal.Equals(member, root))
        {
            string next = parent[member];
            parent[member] = root;
            member = next;
        }
        return root;
    }

    static string ClusterKey(StructuralCloneCorpusCluster cluster)
        => string.Join("\n", cluster.Members.Select(MethodKey));

    static string MethodKey(StructuralCloneCorpusMethod method)
        => $"{method.Type}\0{method.Method}";

    static string FormatEdits(StructuralCloneCorpusEditSummary edits)
        => $"blocks +{edits.InsertedBlocks}/-{edits.RemovedBlocks}/~{edits.ChangedBlocks}, "
            + $"operations +{edits.InsertedOperations}/-{edits.RemovedOperations}/~{edits.ChangedOperations}, "
            + $"edges +{edits.InsertedEdges}/-{edits.RemovedEdges}/~{edits.ChangedEdges}";

    static MethodDefinitionHandle Resolve(
        MetadataReader reader,
        StructuralCloneCorpusMethod method)
    {
        TypeDefinitionHandle typeHandle = default;
        foreach (TypeDefinitionHandle candidate in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(candidate);
            string candidateName =
                string.IsNullOrEmpty(reader.GetString(type.Namespace))
                    ? reader.GetString(type.Name)
                    : $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
            if (StringComparer.Ordinal.Equals(candidateName, method.Type))
            {
                if (!typeHandle.IsNil)
                    throw Ambiguous(method, "type");
                typeHandle = candidate;
            }
        }
        if (typeHandle.IsNil)
            throw Missing(method, "type");

        MethodDefinitionHandle result = default;
        foreach (MethodDefinitionHandle candidate
            in reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            if (!reader.StringComparer.Equals(
                reader.GetMethodDefinition(candidate).Name,
                method.Method))
            {
                continue;
            }
            if (!result.IsNil)
                throw Ambiguous(method, "method");
            result = candidate;
        }
        return !result.IsNil ? result : throw Missing(method, "method");
    }

    static void ValidateMethod(
        string id,
        string side,
        StructuralCloneCorpusMethod method)
    {
        if (method is null
            || string.IsNullOrWhiteSpace(method.Type)
            || string.IsNullOrWhiteSpace(method.Method))
        {
            throw new InvalidDataException(
                $"Corpus case '{id}' has an incomplete {side} method identity.");
        }
    }

    static InvalidDataException InvalidAxis(
        string id,
        string axis,
        string value)
        => new(
            $"Corpus case '{id}' has invalid {axis} value '{value}'.");

    static InvalidDataException Missing(
        StructuralCloneCorpusMethod method,
        string part)
        => new(
            $"Could not resolve {part} for '{method.Type}::{method.Method}'.");

    static InvalidDataException Ambiguous(
        StructuralCloneCorpusMethod method,
        string part)
        => new(
            $"Ambiguous {part} for '{method.Type}::{method.Method}'.");

    static void AppendOutcome(
        StringBuilder output,
        StructuralCloneDisposition disposition,
        StructuralCloneRelation? relation)
    {
        output.Append(disposition);
        if (relation is { } value)
        {
            output.Append('/');
            output.Append(value);
        }
    }
}
