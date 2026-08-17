using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public enum StructuralCloneReviewRelevance
{
    Relevant,
    HardNegative,
    OrdinaryNegative,
    SemanticHazard,
}

public sealed record StructuralCloneCoreLibArtifact(
    [property: JsonRequired] string FileName,
    [property: JsonRequired] string Sha256,
    [property: JsonRequired] string ModuleVersionId);

public sealed record StructuralCloneCoreLibSource(
    [property: JsonRequired] string Repository,
    [property: JsonRequired] string Commit);

public sealed record StructuralCloneCoreLibLimits(
    [property: JsonRequired] int MaximumMethods,
    [property: JsonRequired] int MaximumResults,
    [property: JsonRequired] int MaximumBlocks);

public sealed record StructuralCloneCoreLibSourceLocation(
    [property: JsonRequired] string Path,
    [property: JsonRequired] int Line);

public sealed record StructuralCloneCoreLibMethod(
    [property: JsonRequired] string Token,
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Method,
    [property: JsonRequired] string Signature,
    [property: JsonRequired] StructuralCloneCoreLibSourceLocation Source);

public sealed record StructuralCloneCoreLibLabel(
    [property: JsonRequired] string Candidate,
    [property: JsonRequired] StructuralCloneReviewRelevance Relevance,
    [property: JsonRequired] StructuralCloneDisposition ExpectedDisposition,
    [property: JsonRequired] StructuralCloneRelation? ExpectedRelation,
    int? MaximumRank,
    [property: JsonRequired] ImmutableArray<string> ScoresAbove,
    [property: JsonRequired] string Rationale);

public sealed record StructuralCloneCoreLibQuery(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Seed,
    [property: JsonRequired] int ReviewedTopK,
    [property: JsonRequired] int MinimumPrecisionBasisPoints,
    [property: JsonRequired] int MinimumRecallBasisPoints,
    [property: JsonRequired] ImmutableArray<StructuralCloneCoreLibLabel> Labels);

public sealed record StructuralCloneCoreLibCorpusDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] StructuralCloneCoreLibArtifact Artifact,
    [property: JsonRequired] StructuralCloneCoreLibSource Source,
    [property: JsonRequired] StructuralCloneCoreLibLimits Limits,
    [property: JsonRequired] ImmutableArray<StructuralCloneCoreLibMethod> Methods,
    [property: JsonRequired] ImmutableArray<StructuralCloneCoreLibQuery> Queries);

public sealed record StructuralCloneCoreLibMethodResult(
    int Token,
    string Type,
    string Method,
    string? ReviewedSignature);

public sealed record StructuralCloneCoreLibScoreContrastResult(
    StructuralCloneCoreLibMethodResult Method,
    int? Rank,
    StructuralCloneSimilarityEvidence? Similarity);

public sealed record StructuralCloneCoreLibLabelResult(
    StructuralCloneCoreLibLabel Label,
    StructuralCloneCoreLibMethodResult Candidate,
    int? Rank,
    StructuralCloneSimilarityEvidence? Similarity,
    StructuralCloneDisposition ActualDisposition,
    StructuralCloneRelation? ActualRelation,
    ImmutableArray<StructuralCloneCoreLibScoreContrastResult> Contrasts,
    bool Passed);

public sealed record StructuralCloneCoreLibTopCandidate(
    int Rank,
    StructuralCloneCoreLibMethodResult Method,
    StructuralCloneSimilarityEvidence Similarity,
    StructuralCloneReviewRelevance? Relevance,
    StructuralCloneDisposition? ActualDisposition,
    StructuralCloneRelation? ActualRelation);

public sealed record StructuralCloneCoreLibQueryResult(
    string Id,
    StructuralCloneCoreLibMethodResult Seed,
    int ReviewedTopK,
    int MinimumPrecisionBasisPoints,
    int MinimumRecallBasisPoints,
    StructuralCloneRetrievalDisposition RetrievalDisposition,
    StructuralCloneRetrievalReceipt RetrievalReceipt,
    ImmutableArray<StructuralCloneRetrievalBlocker> RetrievalBlockers,
    ImmutableArray<StructuralCloneCoreLibTopCandidate> TopCandidates,
    ImmutableArray<StructuralCloneCoreLibLabelResult> Labels,
    int RelevantAtK,
    int RelevantLabels,
    int? PrecisionBasisPoints,
    int? RecallBasisPoints,
    int StructuralMatchesAtK,
    int SemanticHazardsAtK,
    int HardNegativesAtK,
    int OrdinaryNegativesAtK,
    bool TopKFullyReviewed,
    bool Passed);

public sealed record StructuralCloneCoreLibCorpusReport(
    string Assembly,
    string Sha256,
    Guid ModuleVersionId,
    string SourceRepository,
    string SourceCommit,
    int PassedQueries,
    int TotalQueries,
    int ReviewedCandidates,
    int RelevantAtK,
    int RelevantLabels,
    int? PrecisionBasisPoints,
    int? RecallBasisPoints,
    int StructuralMatchesAtK,
    int SemanticHazardsAtK,
    int HardNegativesAtK,
    int OrdinaryNegativesAtK,
    ImmutableArray<StructuralCloneCoreLibQueryResult> Queries)
{
    public bool Success =>
        TotalQueries > 0
        && PassedQueries == TotalQueries;
}

public static class StructuralCloneCoreLibCorpus
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

    public static StructuralCloneCoreLibCorpusDocument Load(string json)
    {
        StructuralCloneCoreLibCorpusDocument document =
            JsonSerializer.Deserialize<
                StructuralCloneCoreLibCorpusDocument>(
                json,
                s_json)
            ?? throw new InvalidDataException(
                "The CoreLib structural clone corpus is empty.");
        Validate(document);
        return document;
    }

    public static StructuralCloneCoreLibCorpusReport Run(
        string assemblyPath,
        StructuralCloneCoreLibCorpusDocument corpus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(corpus);
        Validate(corpus);

        string fullPath = Path.GetFullPath(assemblyPath);
        string sha256;
        using (FileStream hashStream = File.OpenRead(fullPath))
        {
            sha256 = Convert.ToHexStringLower(
                SHA256.HashData(hashStream));
        }
        if (!StringComparer.Ordinal.Equals(
                sha256,
                corpus.Artifact.Sha256))
        {
            throw new InvalidDataException(
                $"CoreLib SHA-256 mismatch: expected "
                    + $"{corpus.Artifact.Sha256}, actual {sha256}.");
        }
        if (!StringComparer.Ordinal.Equals(
                Path.GetFileName(fullPath),
                corpus.Artifact.FileName))
        {
            throw new InvalidDataException(
                $"CoreLib file name mismatch: expected "
                    + $"'{corpus.Artifact.FileName}', actual "
                    + $"'{Path.GetFileName(fullPath)}'.");
        }

        using var stream = File.OpenRead(fullPath);
        using var image = new PEReader(stream);
        MetadataReader reader =
            StructuralCloneCensus.GetMetadataReader(image, fullPath);
        ModuleDefinition module = reader.GetModuleDefinition();
        Guid moduleVersionId = reader.GetGuid(module.Mvid);
        Guid expectedModuleVersionId =
            Guid.Parse(corpus.Artifact.ModuleVersionId);
        if (moduleVersionId != expectedModuleVersionId)
        {
            throw new InvalidDataException(
                $"CoreLib MVID mismatch: expected "
                    + $"{expectedModuleVersionId}, actual "
                    + $"{moduleVersionId}.");
        }

        var methods = new Dictionary<
            int,
            (StructuralCloneCoreLibMethod Declaration,
                MethodDefinitionHandle Handle)>();
        foreach (StructuralCloneCoreLibMethod declaration
            in corpus.Methods)
        {
            int token = ParseToken(declaration.Token);
            MethodDefinitionHandle handle = Resolve(
                reader,
                token,
                declaration);
            methods.Add(token, (declaration, handle));
        }

        ImmutableArray<MethodDefinitionHandle> population =
            ImmutableArray.CreateRange(reader.MethodDefinitions);
        StructuralCloneComparisonLimits comparisonLimits =
            new(MaximumBlocks: corpus.Limits.MaximumBlocks);
        ImmutableArray<StructuralCloneCoreLibQueryResult>.Builder
            queryResults =
                ImmutableArray.CreateBuilder<
                    StructuralCloneCoreLibQueryResult>(
                    corpus.Queries.Length);

        foreach (StructuralCloneCoreLibQuery query
            in corpus.Queries)
        {
            int seedToken = ParseToken(query.Seed);
            (StructuralCloneCoreLibMethod SeedDeclaration,
                MethodDefinitionHandle SeedHandle) = methods[seedToken];
            StructuralCloneRetrievalResult retrieval =
                StructuralCloneAnalysis.RetrieveSimilar(
                    image,
                    SeedHandle,
                    population,
                    new StructuralCloneRetrievalLimits(
                        corpus.Limits.MaximumMethods,
                        corpus.Limits.MaximumResults,
                        comparisonLimits));
            Dictionary<int, StructuralCloneRetrievalCandidate> ranked =
                retrieval.Candidates.ToDictionary(
                    static candidate =>
                        MetadataTokens.GetToken(
                            candidate.Method.Handle));
            Dictionary<int, StructuralCloneCoreLibLabel> labels =
                query.Labels.ToDictionary(
                    label => ParseToken(label.Candidate));
            ImmutableArray<StructuralCloneCoreLibLabelResult>.Builder
                labelResults =
                    ImmutableArray.CreateBuilder<
                        StructuralCloneCoreLibLabelResult>(
                        query.Labels.Length);
            var comparisons =
                new Dictionary<int, StructuralCloneComparison>();

            foreach (StructuralCloneCoreLibLabel label
                in query.Labels)
            {
                int candidateToken = ParseToken(label.Candidate);
                (StructuralCloneCoreLibMethod CandidateDeclaration,
                    MethodDefinitionHandle CandidateHandle) =
                        methods[candidateToken];
                StructuralCloneComparison comparison =
                    StructuralCloneAnalysis.Compare(
                        image,
                        SeedHandle,
                        CandidateHandle,
                        comparisonLimits);
                comparisons.Add(candidateToken, comparison);
                ranked.TryGetValue(
                    candidateToken,
                    out StructuralCloneRetrievalCandidate? actual);
                var contrasts =
                    ImmutableArray.CreateBuilder<
                        StructuralCloneCoreLibScoreContrastResult>(
                            label.ScoresAbove.Length);
                bool relationPassed =
                    comparison.Disposition
                        == label.ExpectedDisposition
                    && comparison.Relation
                        == label.ExpectedRelation;
                bool rankPassed =
                    label.MaximumRank is null
                    || actual is not null
                        && actual.Rank <= label.MaximumRank;
                bool contrastPassed =
                    actual is not null
                    || label.ScoresAbove.IsEmpty;
                foreach (string contrastTokenText
                    in label.ScoresAbove)
                {
                    int contrastToken =
                        ParseToken(contrastTokenText);
                    ranked.TryGetValue(
                        contrastToken,
                        out StructuralCloneRetrievalCandidate?
                            contrast);
                    contrasts.Add(
                        new StructuralCloneCoreLibScoreContrastResult(
                            Project(
                                methods[contrastToken].Declaration),
                            contrast?.Rank,
                            contrast?.Similarity));
                    contrastPassed =
                        contrastPassed
                        && actual is not null
                        && contrast is not null
                        && actual.Similarity.Score
                            > contrast.Similarity.Score;
                }
                labelResults.Add(
                    new StructuralCloneCoreLibLabelResult(
                        label,
                        Project(CandidateDeclaration),
                        actual?.Rank,
                        actual?.Similarity,
                        comparison.Disposition,
                        comparison.Relation,
                        contrasts.ToImmutable(),
                        relationPassed
                            && rankPassed
                            && contrastPassed));
            }

            ImmutableArray<StructuralCloneRetrievalCandidate>
                actualTopCandidates =
                [
                    .. retrieval.Candidates.Take(
                        query.ReviewedTopK),
                ];
            bool topKFullyReviewed =
                actualTopCandidates.Length
                    == query.ReviewedTopK
                && actualTopCandidates.All(candidate =>
                    labels.ContainsKey(
                        MetadataTokens.GetToken(
                            candidate.Method.Handle)));
            ImmutableArray<StructuralCloneCoreLibTopCandidate>.Builder
                topCandidates =
                    ImmutableArray.CreateBuilder<
                        StructuralCloneCoreLibTopCandidate>(
                        actualTopCandidates.Length);
            foreach (StructuralCloneRetrievalCandidate candidate
                in actualTopCandidates)
            {
                int token =
                    MetadataTokens.GetToken(
                        candidate.Method.Handle);
                labels.TryGetValue(
                    token,
                    out StructuralCloneCoreLibLabel? label);
                comparisons.TryGetValue(
                    token,
                    out StructuralCloneComparison? comparison);
                StructuralCloneCoreLibMethodResult method =
                    methods.TryGetValue(
                        token,
                        out var declared)
                    ? Project(declared.Declaration)
                    : Project(
                        reader,
                        candidate.Method.Handle);
                topCandidates.Add(
                    new StructuralCloneCoreLibTopCandidate(
                        candidate.Rank,
                        method,
                        candidate.Similarity,
                        label?.Relevance,
                        comparison?.Disposition,
                        comparison?.Relation));
            }

            ImmutableArray<StructuralCloneCoreLibTopCandidate>
                reviewedTopCandidates =
                    topCandidates.ToImmutable();
            int relevantAtK =
                reviewedTopCandidates.Count(static candidate =>
                    candidate.Relevance
                        == StructuralCloneReviewRelevance.Relevant);
            int relevantLabels =
                query.Labels.Count(static label =>
                    label.Relevance
                        == StructuralCloneReviewRelevance.Relevant);
            int recoveredRelevant =
                query.Labels.Count(label =>
                    label.Relevance
                        == StructuralCloneReviewRelevance.Relevant
                    && ranked.TryGetValue(
                        ParseToken(label.Candidate),
                        out StructuralCloneRetrievalCandidate?
                            candidate)
                    && candidate.Rank <= query.ReviewedTopK);
            int? precisionBasisPoints =
                topKFullyReviewed
                    ? BasisPoints(
                        relevantAtK,
                        query.ReviewedTopK)
                    : null;
            int? recallBasisPoints =
                relevantLabels == 0
                    || retrieval.Disposition
                        != StructuralCloneRetrievalDisposition
                            .Completed
                    ? null
                    : BasisPoints(
                        recoveredRelevant,
                        relevantLabels);
            int structuralMatchesAtK =
                reviewedTopCandidates.Count(candidate =>
                    candidate.ActualDisposition
                        == StructuralCloneDisposition.Completed
                    && candidate.ActualRelation
                        is StructuralCloneRelation.Exact
                            or StructuralCloneRelation.Near);
            int semanticHazardsAtK =
                reviewedTopCandidates.Count(static candidate =>
                    candidate.Relevance
                        == StructuralCloneReviewRelevance
                            .SemanticHazard);
            int hardNegativesAtK =
                reviewedTopCandidates.Count(static candidate =>
                    candidate.Relevance
                        == StructuralCloneReviewRelevance
                            .HardNegative);
            int ordinaryNegativesAtK =
                reviewedTopCandidates.Count(static candidate =>
                    candidate.Relevance
                        == StructuralCloneReviewRelevance
                            .OrdinaryNegative);
            ImmutableArray<StructuralCloneCoreLibLabelResult>
                queryLabelResults = labelResults.ToImmutable();
            bool passed =
                retrieval.Disposition
                    == StructuralCloneRetrievalDisposition.Completed
                && topKFullyReviewed
                && queryLabelResults.All(static label =>
                    label.Passed)
                && precisionBasisPoints is { } precision
                && precision
                    >= query.MinimumPrecisionBasisPoints
                && (recallBasisPoints ?? 0)
                    >= query.MinimumRecallBasisPoints;
            queryResults.Add(
                new StructuralCloneCoreLibQueryResult(
                    query.Id,
                    Project(SeedDeclaration),
                    query.ReviewedTopK,
                    query.MinimumPrecisionBasisPoints,
                    query.MinimumRecallBasisPoints,
                    retrieval.Disposition,
                    retrieval.Receipt,
                    retrieval.Blockers,
                    reviewedTopCandidates,
                    queryLabelResults,
                    relevantAtK,
                    relevantLabels,
                    precisionBasisPoints,
                    recallBasisPoints,
                    structuralMatchesAtK,
                    semanticHazardsAtK,
                    hardNegativesAtK,
                    ordinaryNegativesAtK,
                    topKFullyReviewed,
                    passed));
        }

        ImmutableArray<StructuralCloneCoreLibQueryResult> queries =
            queryResults.ToImmutable();
        int reviewedCandidates = queries.Sum(
            static query => query.ReviewedTopK);
        int aggregateRelevantAtK = queries.Sum(
            static query => query.RelevantAtK);
        int aggregateRelevantLabels = queries.Sum(
            static query => query.RelevantLabels);
        return new StructuralCloneCoreLibCorpusReport(
            fullPath,
            sha256,
            moduleVersionId,
            corpus.Source.Repository,
            corpus.Source.Commit,
            queries.Count(static query => query.Passed),
            queries.Length,
            reviewedCandidates,
            aggregateRelevantAtK,
            aggregateRelevantLabels,
            queries.All(static query =>
                query.PrecisionBasisPoints is not null)
                ? BasisPoints(
                    aggregateRelevantAtK,
                    reviewedCandidates)
                : null,
            aggregateRelevantLabels == 0
                || queries.Any(static query =>
                    query.RelevantLabels > 0
                    && query.RecallBasisPoints is null)
                ? null
                : BasisPoints(
                    queries.Sum(query =>
                        query.Labels.Count(label =>
                            label.Label.Relevance
                                == StructuralCloneReviewRelevance
                                    .Relevant
                            && label.Rank is { } rank
                            && rank <= query.ReviewedTopK)),
                    aggregateRelevantLabels),
            queries.Sum(static query =>
                query.StructuralMatchesAtK),
            queries.Sum(static query =>
                query.SemanticHazardsAtK),
            queries.Sum(static query =>
                query.HardNegativesAtK),
            queries.Sum(static query =>
                query.OrdinaryNegativesAtK),
            queries);
    }

    public static string ToJson(
        StructuralCloneCoreLibCorpusReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string Format(
        StructuralCloneCoreLibCorpusReport report)
    {
        StringBuilder output = new();
        output.Append("CoreLib structural clone corpus: ");
        output.Append(report.PassedQueries);
        output.Append('/');
        output.Append(report.TotalQueries);
        output.AppendLine(" queries passed");
        output.Append("  artifact: sha256=");
        output.Append(report.Sha256);
        output.Append(" mvid=");
        output.AppendLine(report.ModuleVersionId.ToString());
        output.Append("  source: ");
        output.Append(report.SourceRepository);
        output.Append('@');
        output.AppendLine(report.SourceCommit);
        output.Append("  reviewed top-k: relevant=");
        output.Append(report.RelevantAtK);
        output.Append('/');
        output.Append(report.ReviewedCandidates);
        output.Append(" precision=");
        AppendPercent(output, report.PrecisionBasisPoints);
        output.Append(" labeled-recall=");
        if (report.RecallBasisPoints is { } recall)
            AppendPercent(output, recall);
        else
            output.Append("n/a");
        output.Append(" structural=");
        output.Append(report.StructuralMatchesAtK);
        output.Append('/');
        output.Append(report.ReviewedCandidates);
        output.Append(" hazards=");
        output.Append(report.SemanticHazardsAtK);
        output.Append(" hard-negatives=");
        output.Append(report.HardNegativesAtK);
        output.Append(" ordinary-negatives=");
        output.AppendLine(
            report.OrdinaryNegativesAtK.ToString(
                CultureInfo.InvariantCulture));

        foreach (StructuralCloneCoreLibQueryResult query
            in report.Queries)
        {
            output.Append(query.Passed ? "PASS " : "FAIL ");
            output.Append(query.Id);
            output.Append(": seed ");
            output.Append(MethodDisplay(query.Seed));
            output.Append(" k=");
            output.Append(query.ReviewedTopK);
            output.Append(" precision=");
            AppendPercent(output, query.PrecisionBasisPoints);
            output.Append(" (min ");
            AppendPercent(
                output,
                query.MinimumPrecisionBasisPoints);
            output.Append(')');
            output.Append(" recall=");
            if (query.RecallBasisPoints is { } queryRecall)
                AppendPercent(output, queryRecall);
            else
                output.Append("n/a");
            output.Append(" (min ");
            AppendPercent(
                output,
                query.MinimumRecallBasisPoints);
            output.Append(')');
            output.Append(" disposition=");
            output.AppendLine(query.RetrievalDisposition.ToString());
            if (!query.TopKFullyReviewed)
            {
                output.AppendLine(
                    "  FAIL reviewed top-k is incomplete or "
                        + "contains an unlabeled candidate");
            }
            foreach (StructuralCloneCoreLibTopCandidate candidate
                in query.TopCandidates)
            {
                output.Append("  #");
                output.Append(candidate.Rank);
                output.Append(" score=");
                output.Append(candidate.Similarity.Score);
                output.Append(' ');
                output.Append(
                    candidate.Relevance?.ToString()
                        ?? "Unreviewed");
                output.Append(' ');
                output.Append(
                    candidate.ActualDisposition?.ToString()
                        ?? "-");
                output.Append('/');
                output.Append(candidate.ActualRelation?.ToString()
                    ?? "-");
                output.Append(' ');
                output.AppendLine(MethodDisplay(candidate.Method));
            }
            foreach (StructuralCloneCoreLibLabelResult label
                in query.Labels.Where(label =>
                    label.Label.Relevance
                        == StructuralCloneReviewRelevance.Relevant
                    && (label.Rank is null
                        || label.Rank > query.ReviewedTopK)))
            {
                output.Append("  MISS@");
                output.Append(query.ReviewedTopK);
                output.Append(" rank=");
                output.Append(label.Rank?.ToString(
                    CultureInfo.InvariantCulture) ?? "unranked");
                output.Append(' ');
                output.AppendLine(MethodDisplay(label.Candidate));
            }
            foreach (StructuralCloneCoreLibLabelResult label
                in query.Labels.Where(
                    static label => !label.Passed))
            {
                output.Append("  FAIL label ");
                output.Append(MethodDisplay(label.Candidate));
                output.Append(" expected=");
                output.Append(label.Label.ExpectedDisposition);
                output.Append('/');
                output.Append(
                    label.Label.ExpectedRelation?.ToString()
                        ?? "-");
                output.Append(" actual=");
                output.Append(label.ActualDisposition);
                output.Append('/');
                output.Append(
                    label.ActualRelation?.ToString() ?? "-");
                output.Append(" rank=");
                output.Append(
                    label.Rank?.ToString(
                        CultureInfo.InvariantCulture)
                    ?? "unranked");
                output.Append(" maximum-rank=");
                output.AppendLine(
                    label.Label.MaximumRank?.ToString(
                        CultureInfo.InvariantCulture)
                    ?? "-");
                foreach (
                    StructuralCloneCoreLibScoreContrastResult
                        contrast in label.Contrasts)
                {
                    output.Append("    contrast score=");
                    output.Append(
                        label.Similarity?.Score.ToString(
                            CultureInfo.InvariantCulture)
                        ?? "unranked");
                    output.Append(" must exceed score=");
                    output.Append(
                        contrast.Similarity?.Score.ToString(
                            CultureInfo.InvariantCulture)
                        ?? "unranked");
                    output.Append(' ');
                    output.AppendLine(
                        MethodDisplay(contrast.Method));
                }
            }
        }
        return output.ToString();
    }

    static void Validate(
        StructuralCloneCoreLibCorpusDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported CoreLib clone corpus schema "
                    + $"{document.SchemaVersion}; expected 1.");
        }
        if (document.Artifact is null
            || document.Source is null
            || document.Limits is null)
        {
            throw new InvalidDataException(
                "The CoreLib clone corpus requires artifact, source, "
                    + "and limit declarations.");
        }
        if (string.IsNullOrWhiteSpace(document.Artifact.FileName)
            || document.Artifact.Sha256 is null
            || document.Artifact.Sha256.Length != 64
            || !document.Artifact.Sha256.All(
                static character =>
                    character is >= '0' and <= '9'
                        or >= 'a' and <= 'f')
            || !Guid.TryParse(
                document.Artifact.ModuleVersionId,
                out _))
        {
            throw new InvalidDataException(
                "The CoreLib clone corpus has invalid artifact "
                    + "provenance.");
        }
        if (string.IsNullOrWhiteSpace(document.Source.Repository)
            || document.Source.Commit is null
            || document.Source.Commit.Length != 40
            || !document.Source.Commit.All(
                static character =>
                    character is >= '0' and <= '9'
                        or >= 'a' and <= 'f'))
        {
            throw new InvalidDataException(
                "The CoreLib clone corpus has invalid source "
                    + "provenance.");
        }
        if (document.Limits.MaximumMethods < 1
            || document.Limits.MaximumResults < 1
            || document.Limits.MaximumBlocks < 1)
        {
            throw new InvalidDataException(
                "The CoreLib clone corpus limits must be positive.");
        }
        if (document.Methods.IsDefaultOrEmpty
            || document.Queries.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The CoreLib clone corpus requires methods and queries.");
        }

        var methods = new Dictionary<int, StructuralCloneCoreLibMethod>();
        foreach (StructuralCloneCoreLibMethod method
            in document.Methods)
        {
            if (method is null)
            {
                throw new InvalidDataException(
                    "The CoreLib method catalog contains null.");
            }
            int token = ParseToken(method.Token);
            if (!methods.TryAdd(token, method)
                || string.IsNullOrWhiteSpace(method.Type)
                || string.IsNullOrWhiteSpace(method.Method)
                || string.IsNullOrWhiteSpace(method.Signature)
                || method.Source is null
                || string.IsNullOrWhiteSpace(method.Source.Path)
                || method.Source.Line < 1)
            {
                throw new InvalidDataException(
                    $"CoreLib method '{method.Token}' is incomplete "
                        + "or duplicated.");
            }
        }

        var referencedMethods = new HashSet<int>();
        var queryIds = new HashSet<string>(
            StringComparer.Ordinal);
        foreach (StructuralCloneCoreLibQuery query
            in document.Queries)
        {
            if (query is null)
            {
                throw new InvalidDataException(
                    "The CoreLib query catalog contains null.");
            }
            int seed = ParseToken(query.Seed);
            if (string.IsNullOrWhiteSpace(query.Id)
                || !queryIds.Add(query.Id)
                || !methods.ContainsKey(seed)
                || query.ReviewedTopK < 1
                || query.ReviewedTopK
                    > document.Limits.MaximumResults
                || query.MinimumPrecisionBasisPoints
                    is < 0 or > 10_000
                || query.MinimumRecallBasisPoints
                    is < 0 or > 10_000
                || query.Labels.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    $"CoreLib query '{query.Id}' is invalid.");
            }
            referencedMethods.Add(seed);
            var candidates = new HashSet<int>();
            foreach (StructuralCloneCoreLibLabel label
                in query.Labels)
            {
                if (label is null)
                {
                    throw new InvalidDataException(
                        $"CoreLib query '{query.Id}' contains a "
                            + "null label.");
                }
                int candidate = ParseToken(label.Candidate);
                if (candidate == seed
                    || !methods.ContainsKey(candidate)
                    || !candidates.Add(candidate)
                    || (label.ExpectedDisposition
                            == StructuralCloneDisposition.Completed)
                        ^ (label.ExpectedRelation is not null)
                    || label.MaximumRank is < 1
                    || label.MaximumRank
                        > document.Limits.MaximumResults
                    || label.ScoresAbove.IsDefault
                    || string.IsNullOrWhiteSpace(label.Rationale))
                {
                    throw new InvalidDataException(
                        $"CoreLib query '{query.Id}' has invalid "
                            + $"candidate '{label.Candidate}'.");
                }
                if (label.Relevance
                        == StructuralCloneReviewRelevance.Relevant
                    && label.MaximumRank is null)
                {
                    throw new InvalidDataException(
                        $"Relevant CoreLib candidate "
                            + $"'{label.Candidate}' requires a maximum "
                            + "rank.");
                }
                referencedMethods.Add(candidate);
            }
            foreach (StructuralCloneCoreLibLabel label
                in query.Labels)
            {
                int candidate = ParseToken(label.Candidate);
                var contrasts = new HashSet<int>();
                foreach (string contrastText
                    in label.ScoresAbove)
                {
                    int contrast = ParseToken(contrastText);
                    if (contrast == seed
                        || contrast == candidate
                        || !candidates.Contains(contrast)
                        || !contrasts.Add(contrast))
                    {
                        throw new InvalidDataException(
                            $"CoreLib query '{query.Id}' has invalid "
                                + $"score contrast '{contrastText}'.");
                    }
                }
            }
        }
        if (!referencedMethods.SetEquals(methods.Keys))
        {
            throw new InvalidDataException(
                "The CoreLib method catalog must equal all query "
                    + "seeds and candidates.");
        }
    }

    static MethodDefinitionHandle Resolve(
        MetadataReader reader,
        int token,
        StructuralCloneCoreLibMethod declaration)
    {
        int row = token & 0x00FFFFFF;
        if (row > reader.MethodDefinitions.Count)
        {
            throw new InvalidDataException(
                $"CoreLib method token {declaration.Token} is outside "
                    + "the MethodDef table.");
        }
        MethodDefinitionHandle handle =
            MetadataTokens.MethodDefinitionHandle(row);
        MethodDefinition method =
            reader.GetMethodDefinition(handle);
        string type = StructuralCloneCensus.TypeName(
            reader,
            method.GetDeclaringType());
        string name = reader.GetString(method.Name);
        if (!StringComparer.Ordinal.Equals(
                type,
                declaration.Type)
            || !StringComparer.Ordinal.Equals(
                name,
                declaration.Method))
        {
            throw new InvalidDataException(
                $"CoreLib method token {declaration.Token} resolved "
                    + $"to '{type}::{name}', expected "
                    + $"'{declaration.Type}::{declaration.Method}'.");
        }
        return handle;
    }

    static int ParseToken(string token)
    {
        if (token is null
            || token.Length != 10
            || !token.StartsWith("0x06", StringComparison.Ordinal)
            || !int.TryParse(
                token.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out int value)
            || (value & unchecked((int)0xFF000000))
                != 0x06000000
            || (value & 0x00FFFFFF) == 0)
        {
            throw new InvalidDataException(
                $"Invalid MethodDef token '{token}'.");
        }
        return value;
    }

    static StructuralCloneCoreLibMethodResult Project(
        StructuralCloneCoreLibMethod method)
        => new(
            ParseToken(method.Token),
            method.Type,
            method.Method,
            method.Signature);

    static StructuralCloneCoreLibMethodResult Project(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        MethodDefinition method =
            reader.GetMethodDefinition(handle);
        return new StructuralCloneCoreLibMethodResult(
            MetadataTokens.GetToken(handle),
            StructuralCloneCensus.TypeName(
                reader,
                method.GetDeclaringType()),
            reader.GetString(method.Name),
            ReviewedSignature: null);
    }

    static int BasisPoints(int numerator, int denominator)
        => denominator == 0
            ? 0
            : checked((int)(
                10_000L * numerator / denominator));

    static void AppendPercent(
        StringBuilder output,
        int? basisPoints)
    {
        if (basisPoints is not { } value)
        {
            output.Append("n/a");
            return;
        }
        output.Append(value / 100);
        output.Append('.');
        output.Append((value % 100).ToString(
            "00",
            CultureInfo.InvariantCulture));
        output.Append('%');
    }

    static string MethodDisplay(
        StructuralCloneCoreLibMethodResult method)
        => method.ReviewedSignature is { } signature
            ? $"0x{method.Token:X8} {method.Type}::{method.Method} "
                + $"[reviewed-signature: {signature}]"
            : $"0x{method.Token:X8} {method.Type}::{method.Method}";
}
