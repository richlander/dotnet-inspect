using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;
using ILInspector.MetadataPrimitives;

namespace ILInspector.AnalysisHarness;

public sealed record StructuralCloneCrossAssemblyArtifact(
    [property: JsonRequired] string AssemblyName,
    [property: JsonRequired] string Type,
    [property: JsonRequired] string Project);

public sealed record StructuralCloneCrossAssemblyLimits(
    [property: JsonRequired] int MaximumMethods,
    [property: JsonRequired] int MaximumResults,
    [property: JsonRequired] int MaximumBlocks);

public sealed record StructuralCloneCrossAssemblyLabel(
    [property: JsonRequired] string Candidate,
    [property: JsonRequired] StructuralCloneReviewRelevance Relevance,
    [property: JsonRequired] int MaximumRank,
    [property: JsonRequired] ImmutableArray<string> ScoresAbove,
    [property: JsonRequired] string Rationale);

public sealed record StructuralCloneCrossAssemblyQuery(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Seed,
    [property: JsonRequired] int ReviewedTopK,
    [property: JsonRequired] int MinimumPrecisionBasisPoints,
    [property: JsonRequired] int MinimumRecallBasisPoints,
    [property: JsonRequired] ImmutableArray<StructuralCloneCrossAssemblyLabel>
        Labels);

public sealed record StructuralCloneCrossAssemblyCorpusDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] StructuralCloneCrossAssemblyArtifact Left,
    [property: JsonRequired] StructuralCloneCrossAssemblyArtifact Right,
    [property: JsonRequired] StructuralCloneCrossAssemblyLimits Limits,
    [property: JsonRequired] ImmutableArray<StructuralCloneCrossAssemblyQuery>
        Queries);

public sealed record StructuralCloneCrossAssemblyMethodResult(
    MetadataMethodAddress Address,
    string Type,
    string Method);

public sealed record StructuralCloneCrossAssemblyContrastResult(
    StructuralCloneCrossAssemblyMethodResult Method,
    int? Rank,
    StructuralCloneSimilarityEvidence? Similarity);

public sealed record StructuralCloneCrossAssemblyLabelResult(
    StructuralCloneCrossAssemblyLabel Label,
    StructuralCloneCrossAssemblyMethodResult Candidate,
    int? Rank,
    StructuralCloneSimilarityEvidence? Similarity,
    ImmutableArray<StructuralCloneCrossAssemblyContrastResult> Contrasts,
    bool Passed);

public sealed record StructuralCloneCrossAssemblyTopCandidate(
    int Rank,
    StructuralCloneCrossAssemblyMethodResult Method,
    StructuralCloneSimilarityEvidence Similarity,
    StructuralCloneReviewRelevance? Relevance);

public sealed record StructuralCloneCrossAssemblyQueryResult(
    string Id,
    StructuralCloneCrossAssemblyMethodResult Seed,
    int ReviewedTopK,
    int MinimumPrecisionBasisPoints,
    int MinimumRecallBasisPoints,
    StructuralCloneRetrievalDisposition RetrievalDisposition,
    StructuralCloneRetrievalReceipt RetrievalReceipt,
    ImmutableArray<StructuralCloneRetrievalBlocker> RetrievalBlockers,
    ImmutableArray<StructuralCloneCrossAssemblyTopCandidate> TopCandidates,
    ImmutableArray<StructuralCloneCrossAssemblyLabelResult> Labels,
    int RelevantAtK,
    int RelevantLabels,
    int? PrecisionBasisPoints,
    int? RecallBasisPoints,
    bool TopKFullyReviewed,
    bool Passed);

public sealed record StructuralCloneCrossAssemblyCorpusReport(
    string LeftAssembly,
    Guid LeftModuleVersionId,
    string RightAssembly,
    Guid RightModuleVersionId,
    int PassedQueries,
    int TotalQueries,
    int ReviewedCandidates,
    int RequestedReviewSlots,
    int RelevantAtK,
    int RelevantLabels,
    int? PrecisionBasisPoints,
    int? RecallBasisPoints,
    int SemanticHazardsAtK,
    int HardNegativesAtK,
    int KnownMisses,
    ImmutableArray<StructuralCloneCrossAssemblyQueryResult> Queries)
{
    public bool Success =>
        TotalQueries > 0
        && PassedQueries == TotalQueries;
}

public static class StructuralCloneCrossAssemblyCorpus
{
    const int SupportedSchema = 1;

    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static StructuralCloneCrossAssemblyCorpusDocument Load(
        string json)
    {
        StructuralCloneCrossAssemblyCorpusDocument document =
            JsonSerializer.Deserialize<
                StructuralCloneCrossAssemblyCorpusDocument>(
                json,
                JsonOptions)
            ?? throw new InvalidDataException(
                "The cross-assembly clone corpus is empty.");
        Validate(document);
        return document;
    }

    public static StructuralCloneCrossAssemblyCorpusReport Run(
        string leftPath,
        string rightPath,
        StructuralCloneCrossAssemblyCorpusDocument corpus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leftPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightPath);
        ArgumentNullException.ThrowIfNull(corpus);
        Validate(corpus);

        string fullLeftPath = Path.GetFullPath(leftPath);
        string fullRightPath = Path.GetFullPath(rightPath);
        using var leftStream = File.OpenRead(fullLeftPath);
        using var rightStream = File.OpenRead(fullRightPath);
        using var leftImage = new PEReader(leftStream);
        using var rightImage = new PEReader(rightStream);
        if (!leftImage.HasMetadata || !rightImage.HasMetadata)
        {
            throw new BadImageFormatException(
                "Both cross-assembly inputs must contain managed metadata.");
        }
        MetadataReader leftReader = leftImage.GetMetadataReader();
        MetadataReader rightReader = rightImage.GetMetadataReader();
        Guid leftModuleVersionId = ModuleVersionId(leftReader);
        Guid rightModuleVersionId = ModuleVersionId(rightReader);
        if (leftModuleVersionId == rightModuleVersionId)
        {
            throw new InvalidDataException(
                "The cross-assembly corpus requires distinct module "
                    + "identities.");
        }

        VerifyAssembly(leftReader, corpus.Left);
        VerifyAssembly(rightReader, corpus.Right);
        TypeDefinitionHandle leftType = ResolveType(
            leftReader,
            corpus.Left.Type);
        TypeDefinitionHandle rightType = ResolveType(
            rightReader,
            corpus.Right.Type);
        IReadOnlyDictionary<string, MethodDefinitionHandle> leftMethods =
            ResolveMethods(leftReader, leftType);
        IReadOnlyDictionary<string, MethodDefinitionHandle> rightMethods =
            ResolveMethods(rightReader, rightType);
        ImmutableArray<MethodDefinitionHandle> rightPopulation =
        [
            .. rightReader
                .GetTypeDefinition(rightType)
                .GetMethods(),
        ];

        var results =
            ImmutableArray.CreateBuilder<
                StructuralCloneCrossAssemblyQueryResult>(
                corpus.Queries.Length);
        foreach (StructuralCloneCrossAssemblyQuery query in corpus.Queries)
        {
            MethodDefinitionHandle seed = RequiredMethod(
                leftMethods,
                query.Seed,
                corpus.Left.Type);
            MetadataMethodAddress seedAddress =
                new(leftModuleVersionId, seed);
            var labels =
                query.Labels.ToDictionary(
                    label => new MetadataMethodAddress(
                        rightModuleVersionId,
                        RequiredMethod(
                            rightMethods,
                            label.Candidate,
                            corpus.Right.Type)),
                    static label => label);
            StructuralCloneRetrievalResult retrieval =
                StructuralCloneAnalysis.RetrieveSimilar(
                    leftImage,
                    seed,
                    rightImage,
                    rightPopulation,
                    new StructuralCloneRetrievalLimits(
                        corpus.Limits.MaximumMethods,
                        corpus.Limits.MaximumResults,
                        new StructuralCloneComparisonLimits(
                            MaximumBlocks:
                                corpus.Limits.MaximumBlocks)));
            RequireAddress(
                retrieval.Seed.Method,
                seedAddress,
                $"query '{query.Id}' seed");
            foreach (StructuralCloneRetrievalCandidate candidate
                in retrieval.Candidates)
            {
                RequireAddress(
                    candidate.Method,
                    new MetadataMethodAddress(
                        rightModuleVersionId,
                        candidate.Method.Handle),
                    $"query '{query.Id}' candidate");
            }
            var ranked = retrieval.Candidates.ToDictionary(
                static candidate => candidate.Method);

            var labelResults =
                ImmutableArray.CreateBuilder<
                    StructuralCloneCrossAssemblyLabelResult>(
                    query.Labels.Length);
            foreach (StructuralCloneCrossAssemblyLabel label
                in query.Labels)
            {
                MethodDefinitionHandle candidateHandle = RequiredMethod(
                    rightMethods,
                    label.Candidate,
                    corpus.Right.Type);
                MetadataMethodAddress candidateAddress =
                    new(rightModuleVersionId, candidateHandle);
                ranked.TryGetValue(
                    candidateAddress,
                    out StructuralCloneRetrievalCandidate? candidate);
                var contrasts =
                    ImmutableArray.CreateBuilder<
                        StructuralCloneCrossAssemblyContrastResult>(
                        label.ScoresAbove.Length);
                bool contrastsPassed = true;
                foreach (string contrastName in label.ScoresAbove)
                {
                    MethodDefinitionHandle contrastHandle =
                        RequiredMethod(
                            rightMethods,
                            contrastName,
                            corpus.Right.Type);
                    MetadataMethodAddress contrastAddress =
                        new(rightModuleVersionId, contrastHandle);
                    ranked.TryGetValue(
                        contrastAddress,
                        out StructuralCloneRetrievalCandidate? contrast);
                    contrasts.Add(
                        new StructuralCloneCrossAssemblyContrastResult(
                            Project(
                                rightReader,
                                corpus.Right.Type,
                                contrast?.Method
                                    ?? contrastAddress),
                            contrast?.Rank,
                            contrast?.Similarity));
                    contrastsPassed =
                        contrastsPassed
                        && candidate is not null
                        && contrast is not null
                        && candidate.Similarity.Score
                            > contrast.Similarity.Score;
                }
                labelResults.Add(
                    new StructuralCloneCrossAssemblyLabelResult(
                        label,
                        Project(
                            rightReader,
                            corpus.Right.Type,
                            candidate?.Method
                                ?? candidateAddress),
                        candidate?.Rank,
                        candidate?.Similarity,
                        contrasts.ToImmutable(),
                        candidate is not null
                            && candidate.Rank <= label.MaximumRank
                            && contrastsPassed));
            }

            ImmutableArray<StructuralCloneRetrievalCandidate>
                actualTopCandidates =
            [
                .. retrieval.Candidates.Take(query.ReviewedTopK),
            ];
            bool returnedRowsFullyReviewed =
                actualTopCandidates.Length > 0
                && actualTopCandidates.All(candidate =>
                    labels.ContainsKey(candidate.Method));
            bool topKFullyReviewed =
                actualTopCandidates.Length == query.ReviewedTopK
                && returnedRowsFullyReviewed;
            ImmutableArray<StructuralCloneCrossAssemblyTopCandidate>
                topCandidates =
            [
                .. actualTopCandidates.Select(candidate =>
                    new StructuralCloneCrossAssemblyTopCandidate(
                        candidate.Rank,
                        Project(
                            rightReader,
                            corpus.Right.Type,
                            candidate.Method),
                        candidate.Similarity,
                        labels.TryGetValue(
                            candidate.Method,
                            out StructuralCloneCrossAssemblyLabel? label)
                                ? label.Relevance
                                : null)),
            ];
            int relevantAtK = topCandidates.Count(static candidate =>
                candidate.Relevance
                    == StructuralCloneReviewRelevance.Relevant);
            int relevantLabels = query.Labels.Count(static label =>
                label.Relevance
                    == StructuralCloneReviewRelevance.Relevant);
            int? precisionBasisPoints =
                returnedRowsFullyReviewed
                    ? BasisPoints(
                        relevantAtK,
                        topCandidates.Length)
                    : null;
            int? recallBasisPoints =
                relevantLabels == 0
                    || retrieval.Disposition
                        != StructuralCloneRetrievalDisposition.Completed
                    ? null
                    : BasisPoints(
                        query.Labels.Count(label =>
                            label.Relevance
                                == StructuralCloneReviewRelevance.Relevant
                            && ranked.TryGetValue(
                                new MetadataMethodAddress(
                                    rightModuleVersionId,
                                    RequiredMethod(
                                        rightMethods,
                                        label.Candidate,
                                        corpus.Right.Type)),
                                out StructuralCloneRetrievalCandidate?
                                    candidate)
                            && candidate.Rank <= query.ReviewedTopK),
                        relevantLabels);
            ImmutableArray<StructuralCloneCrossAssemblyLabelResult>
                queryLabels = labelResults.ToImmutable();
            bool passed =
                retrieval.Disposition
                    == StructuralCloneRetrievalDisposition.Completed
                && topKFullyReviewed
                && queryLabels.All(static label => label.Passed)
                && precisionBasisPoints is { } precision
                && precision >= query.MinimumPrecisionBasisPoints
                && (recallBasisPoints ?? 0)
                    >= query.MinimumRecallBasisPoints;
            results.Add(
                new StructuralCloneCrossAssemblyQueryResult(
                    query.Id,
                    Project(
                        leftReader,
                        corpus.Left.Type,
                        retrieval.Seed.Method),
                    query.ReviewedTopK,
                    query.MinimumPrecisionBasisPoints,
                    query.MinimumRecallBasisPoints,
                    retrieval.Disposition,
                    retrieval.Receipt,
                    retrieval.Blockers,
                    topCandidates,
                    queryLabels,
                    relevantAtK,
                    relevantLabels,
                    precisionBasisPoints,
                    recallBasisPoints,
                    topKFullyReviewed,
                    passed));
        }

        ImmutableArray<StructuralCloneCrossAssemblyQueryResult> queries =
            results.ToImmutable();
        int reviewedCandidates = queries.Sum(static query =>
            query.TopCandidates.Length);
        int requestedReviewSlots = queries.Sum(static query =>
            query.ReviewedTopK);
        int aggregateRelevantAtK = queries.Sum(static query =>
            query.RelevantAtK);
        int aggregateRelevantLabels = queries.Sum(static query =>
            query.RelevantLabels);
        bool aggregateRowsFullyReviewed =
            reviewedCandidates > 0
            && queries.All(query =>
                query.TopCandidates.All(static candidate =>
                    candidate.Relevance is not null));
        return new StructuralCloneCrossAssemblyCorpusReport(
            fullLeftPath,
            leftModuleVersionId,
            fullRightPath,
            rightModuleVersionId,
            queries.Count(static query => query.Passed),
            queries.Length,
            reviewedCandidates,
            requestedReviewSlots,
            aggregateRelevantAtK,
            aggregateRelevantLabels,
            aggregateRowsFullyReviewed
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
                    aggregateRelevantAtK,
                    aggregateRelevantLabels),
            queries.Sum(query => query.TopCandidates.Count(
                static candidate =>
                    candidate.Relevance
                        == StructuralCloneReviewRelevance.SemanticHazard)),
            queries.Sum(query => query.TopCandidates.Count(
                static candidate =>
                    candidate.Relevance
                        == StructuralCloneReviewRelevance.HardNegative)),
            queries.Sum(query => query.Labels.Count(label =>
                label.Label.Relevance
                    == StructuralCloneReviewRelevance.Relevant
                && (label.Rank is not { } rank
                    || rank > query.ReviewedTopK))),
            queries);
    }

    public static string Format(
        StructuralCloneCrossAssemblyCorpusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var text = new StringBuilder();
        text.Append("Cross-assembly clone corpus: ")
            .Append(report.PassedQueries)
            .Append('/')
            .Append(report.TotalQueries)
            .Append(" queries; rows=")
            .Append(report.ReviewedCandidates)
            .Append('/')
            .Append(report.RequestedReviewSlots)
            .Append(" relevant=")
            .Append(report.RelevantAtK)
            .Append('/')
            .Append(report.ReviewedCandidates)
            .Append(" precision=")
            .Append(Percentage(report.PrecisionBasisPoints))
            .Append(" labeled-recall=")
            .Append(Percentage(report.RecallBasisPoints))
            .Append(" hazards=")
            .Append(report.SemanticHazardsAtK)
            .Append(" hard-negatives=")
            .Append(report.HardNegativesAtK)
            .Append(" known-misses=")
            .Append(report.KnownMisses)
            .AppendLine();
        text.Append("left-mvid=")
            .Append(report.LeftModuleVersionId)
            .Append(" right-mvid=")
            .Append(report.RightModuleVersionId)
            .AppendLine();
        foreach (StructuralCloneCrossAssemblyQueryResult query
            in report.Queries)
        {
            text.Append(query.Passed ? "PASS " : "FAIL ")
                .Append(query.Id)
                .Append(" seed=")
                .Append(query.Seed.Method)
                .Append(" disposition=")
                .Append(query.RetrievalDisposition)
                .Append(" rows=")
                .Append(query.TopCandidates.Length)
                .Append('/')
                .Append(query.ReviewedTopK)
                .Append(" relevant=")
                .Append(query.RelevantAtK)
                .Append('/')
                .Append(query.TopCandidates.Length)
                .Append(" precision=")
                .Append(Percentage(query.PrecisionBasisPoints))
                .Append(" recall=")
                .Append(Percentage(query.RecallBasisPoints))
                .AppendLine();
            foreach (StructuralCloneCrossAssemblyTopCandidate candidate
                in query.TopCandidates)
            {
                text.Append("  #")
                    .Append(candidate.Rank)
                    .Append(" score=")
                    .Append(candidate.Similarity.Score)
                    .Append(' ')
                    .Append(candidate.Method.Method)
                    .Append(" [")
                    .Append(candidate.Relevance?.ToString() ?? "Unreviewed")
                    .AppendLine("]");
            }
            if (!query.TopKFullyReviewed)
            {
                text.AppendLine(
                    "  FAIL reviewed top-k is incomplete or contains "
                        + "unreviewed rows");
            }
            foreach (StructuralCloneCrossAssemblyLabelResult label
                in query.Labels.Where(static label => !label.Passed))
            {
                text.Append("  FAIL label ")
                    .Append(label.Candidate.Method)
                    .Append(" rank=")
                    .Append(label.Rank?.ToString(
                        CultureInfo.InvariantCulture) ?? "unranked")
                    .Append(" maximum=")
                    .Append(label.Label.MaximumRank)
                    .AppendLine();
                foreach (StructuralCloneCrossAssemblyContrastResult contrast
                    in label.Contrasts.Where(contrast =>
                        label.Similarity is null
                        || contrast.Similarity is null
                        || label.Similarity.Score
                            <= contrast.Similarity.Score))
                {
                    text.Append("    contrast ")
                        .Append(contrast.Method.Method)
                        .Append(" score=")
                        .Append(
                            contrast.Similarity?.Score.ToString(
                                CultureInfo.InvariantCulture)
                            ?? "unranked")
                        .AppendLine();
                }
            }
            foreach (StructuralCloneRetrievalBlocker blocker
                in query.RetrievalBlockers)
            {
                text.Append("  retrieval blocker ")
                    .Append(blocker.Kind)
                    .Append(": ")
                    .AppendLine(blocker.Detail);
            }
        }
        return text.ToString();
    }

    public static string ToJson(
        StructuralCloneCrossAssemblyCorpusReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    static void Validate(
        StructuralCloneCrossAssemblyCorpusDocument document)
    {
        if (document.SchemaVersion != SupportedSchema
            || document.Left is null
            || document.Right is null
            || document.Limits is null
            || document.Queries.IsDefaultOrEmpty)
        {
            throw new InvalidDataException(
                "The cross-assembly clone corpus is incomplete.");
        }
        ValidateArtifact(document.Left, "left");
        ValidateArtifact(document.Right, "right");
        if (document.Limits.MaximumMethods < 1
            || document.Limits.MaximumResults < 1
            || document.Limits.MaximumBlocks < 1)
        {
            throw new InvalidDataException(
                "Cross-assembly clone limits must be positive.");
        }

        var queryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (StructuralCloneCrossAssemblyQuery query
            in document.Queries)
        {
            if (query is null
                || string.IsNullOrWhiteSpace(query.Id)
                || !queryIds.Add(query.Id)
                || string.IsNullOrWhiteSpace(query.Seed)
                || query.ReviewedTopK < 1
                || query.ReviewedTopK
                    > document.Limits.MaximumResults
                || query.MinimumPrecisionBasisPoints is < 0 or > 10_000
                || query.MinimumRecallBasisPoints is < 0 or > 10_000
                || query.Labels.IsDefaultOrEmpty)
            {
                throw new InvalidDataException(
                    $"Cross-assembly query '{query?.Id}' is invalid.");
            }
            var candidates = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (StructuralCloneCrossAssemblyLabel label
                in query.Labels)
            {
                if (label is null
                    || string.IsNullOrWhiteSpace(label.Candidate)
                    || !candidates.Add(label.Candidate)
                    || label.MaximumRank < 1
                    || label.MaximumRank
                        > document.Limits.MaximumResults
                    || label.ScoresAbove.IsDefault
                    || string.IsNullOrWhiteSpace(label.Rationale))
                {
                    throw new InvalidDataException(
                        $"Cross-assembly query '{query.Id}' has an "
                            + "invalid label.");
                }
            }
            foreach (StructuralCloneCrossAssemblyLabel label
                in query.Labels)
            {
                var contrasts = new HashSet<string>(
                    StringComparer.Ordinal);
                foreach (string contrast in label.ScoresAbove)
                {
                    if (string.IsNullOrWhiteSpace(contrast)
                        || contrast == label.Candidate
                        || !candidates.Contains(contrast)
                        || !contrasts.Add(contrast))
                    {
                        throw new InvalidDataException(
                            $"Cross-assembly query '{query.Id}' has an "
                                + $"invalid contrast '{contrast}'.");
                    }
                }
            }
        }
    }

    static void ValidateArtifact(
        StructuralCloneCrossAssemblyArtifact artifact,
        string side)
    {
        if (string.IsNullOrWhiteSpace(artifact.AssemblyName)
            || string.IsNullOrWhiteSpace(artifact.Type)
            || string.IsNullOrWhiteSpace(artifact.Project))
        {
            throw new InvalidDataException(
                $"The {side} cross-assembly artifact is incomplete.");
        }
    }

    static void VerifyAssembly(
        MetadataReader reader,
        StructuralCloneCrossAssemblyArtifact artifact)
    {
        if (!reader.IsAssembly)
        {
            throw new InvalidDataException(
                $"'{artifact.Project}' is not an assembly.");
        }
        string actual = reader.GetString(
            reader.GetAssemblyDefinition().Name);
        if (!StringComparer.Ordinal.Equals(
                actual,
                artifact.AssemblyName))
        {
            throw new InvalidDataException(
                $"Assembly '{artifact.Project}' is '{actual}', expected "
                    + $"'{artifact.AssemblyName}'.");
        }
    }

    static TypeDefinitionHandle ResolveType(
        MetadataReader reader,
        string fullName)
    {
        int separator = fullName.LastIndexOf('.');
        string @namespace =
            separator < 0 ? "" : fullName[..separator];
        string name =
            separator < 0 ? fullName : fullName[(separator + 1)..];
        TypeDefinitionHandle match = default;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if (!StringComparer.Ordinal.Equals(
                    reader.GetString(type.Namespace),
                    @namespace)
                || !StringComparer.Ordinal.Equals(
                    reader.GetString(type.Name),
                    name))
            {
                continue;
            }
            if (!match.IsNil)
            {
                throw new InvalidDataException(
                    $"Type '{fullName}' is duplicated.");
            }
            match = handle;
        }
        return !match.IsNil
            ? match
            : throw new InvalidDataException(
                $"Type '{fullName}' was not found.");
    }

    static IReadOnlyDictionary<string, MethodDefinitionHandle> ResolveMethods(
        MetadataReader reader,
        TypeDefinitionHandle type)
    {
        var methods = new Dictionary<string, MethodDefinitionHandle>(
            StringComparer.Ordinal);
        foreach (MethodDefinitionHandle handle
            in reader.GetTypeDefinition(type).GetMethods())
        {
            string name = reader.GetString(
                reader.GetMethodDefinition(handle).Name);
            if (!methods.TryAdd(name, handle))
            {
                throw new InvalidDataException(
                    $"Method '{name}' is overloaded; the schema requires "
                        + "an unambiguous fixture method name.");
            }
        }
        return methods;
    }

    static MethodDefinitionHandle RequiredMethod(
        IReadOnlyDictionary<string, MethodDefinitionHandle> methods,
        string name,
        string type)
        => methods.TryGetValue(name, out MethodDefinitionHandle method)
            ? method
            : throw new InvalidDataException(
                $"Method '{type}::{name}' was not found.");

    static StructuralCloneCrossAssemblyMethodResult Project(
        MetadataReader reader,
        string type,
        MetadataMethodAddress address)
        => new(
            address,
            type,
            reader.GetString(
                reader.GetMethodDefinition(address.Handle).Name));

    static void RequireAddress(
        MetadataMethodAddress actual,
        MetadataMethodAddress expected,
        string subject)
    {
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"The product returned {actual} for {subject}; "
                    + $"expected {expected}.");
        }
    }

    static Guid ModuleVersionId(MetadataReader reader)
        => reader.GetGuid(reader.GetModuleDefinition().Mvid);

    static int BasisPoints(int numerator, int denominator)
        => denominator == 0
            ? 0
            : checked((int)(10_000L * numerator / denominator));

    static string Percentage(int? basisPoints)
        => basisPoints is { } value
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{value / 100}.{value % 100:00}%")
            : "n/a";

    static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }
}
