using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

public sealed record StructuralCloneWorksheetCandidate(
    StructuralCloneCensusMethod Method,
    int Rank,
    StructuralCloneSimilarityEvidence Similarity);

public sealed record StructuralCloneWorksheetReport(
    string Assembly,
    StructuralCloneCensusMethod Seed,
    int MaximumMethods,
    int MaximumResults,
    StructuralCloneRetrievalDisposition Disposition,
    ImmutableArray<StructuralCloneRetrievalBlocker> Blockers,
    StructuralCloneRetrievalReceipt Receipt,
    ImmutableArray<StructuralCloneWorksheetCandidate> Candidates,
    long RetrievalElapsedMilliseconds)
{
    public bool Success =>
        Disposition == StructuralCloneRetrievalDisposition.Completed;
}

public static class StructuralCloneWorksheet
{
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false),
        },
    };

    public static StructuralCloneWorksheetReport Run(
        string assemblyPath,
        string seedSelector,
        int maximumMethods = 50_000,
        int maximumResults = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedSelector);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMethods, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumResults, 1);

        string fullPath = Path.GetFullPath(assemblyPath);
        using var stream = File.OpenRead(fullPath);
        using var image = new PEReader(stream);
        MetadataReader reader =
            StructuralCloneCensus.GetMetadataReader(image, fullPath);
        ImmutableArray<MethodDefinitionHandle> population =
            ImmutableArray.CreateRange(reader.MethodDefinitions);
        MethodDefinitionHandle seed =
            StructuralCloneCensus.ResolveSeed(reader, seedSelector);

        Stopwatch stopwatch = Stopwatch.StartNew();
        StructuralCloneRetrievalResult retrieval =
            StructuralCloneAnalysis.RetrieveSimilar(
                image,
                seed,
                population,
                new StructuralCloneRetrievalLimits(
                    maximumMethods,
                    maximumResults));
        stopwatch.Stop();

        StructuralCloneCensusMethod Project(MethodDefinitionHandle handle)
        {
            MethodDefinition definition = reader.GetMethodDefinition(handle);
            return new StructuralCloneCensusMethod(
                MetadataTokens.GetToken(handle),
                StructuralCloneCensus.TypeName(
                    reader,
                    definition.GetDeclaringType()),
                reader.GetString(definition.Name));
        }

        return new StructuralCloneWorksheetReport(
            fullPath,
            Project(seed),
            maximumMethods,
            maximumResults,
            retrieval.Disposition,
            retrieval.Blockers,
            retrieval.Receipt,
            [
                .. retrieval.Candidates.Select(candidate =>
                    new StructuralCloneWorksheetCandidate(
                        Project(candidate.Method.Handle),
                        candidate.Rank,
                        candidate.Similarity)),
            ],
            stopwatch.ElapsedMilliseconds);
    }

    public static string ToJson(StructuralCloneWorksheetReport report)
        => JsonSerializer.Serialize(report, s_json);

    public static string Format(
        StructuralCloneWorksheetReport report,
        int top = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        StringBuilder output = new();
        output.Append("FUZZY CLONE WORKSHEET: ");
        output.Append(report.Disposition);
        output.Append(' ');
        output.AppendLine(Path.GetFileName(report.Assembly));
        output.Append("  seed: ");
        output.AppendLine(MethodDisplay(report.Seed));
        output.Append("  limits: methods=");
        output.Append(report.MaximumMethods);
        output.Append(" results=");
        output.AppendLine(
            report.MaximumResults.ToString(CultureInfo.InvariantCulture));
        output.Append("  methods: input=");
        output.Append(report.Receipt.InputMethods);
        output.Append(" processed=");
        output.Append(report.Receipt.ProcessedMethods);
        output.Append(" eligible=");
        output.Append(report.Receipt.EligibleMethods);
        output.Append(" unsupported=");
        output.Append(report.Receipt.UnsupportedMethods);
        output.Append(" limited=");
        output.Append(report.Receipt.LimitReachedMethods);
        output.Append(" failed=");
        output.AppendLine(
            report.Receipt.FailedMethods.ToString(
                CultureInfo.InvariantCulture));
        output.Append("  ranking: ranked=");
        output.Append(report.Receipt.RankedCandidates);
        output.Append(" returned=");
        output.Append(report.Receipt.ReturnedCandidates);
        output.Append(" suppressed=");
        output.Append(report.Receipt.SuppressedCandidates);
        output.Append(" body-productions=");
        output.Append(report.Receipt.BodyProductions);
        output.Append(" elapsed-ms=");
        output.AppendLine(
            report.RetrievalElapsedMilliseconds.ToString(
                CultureInfo.InvariantCulture));

        foreach (StructuralCloneRetrievalBlocker blocker in report.Blockers)
        {
            output.Append("  blocker: ");
            output.Append(blocker.Kind);
            output.Append(": ");
            output.AppendLine(blocker.Detail);
        }

        foreach (StructuralCloneWorksheetCandidate candidate
            in report.Candidates.Take(top))
        {
            StructuralCloneSimilarityEvidence similarity =
                candidate.Similarity;
            output.Append("  #");
            output.Append(candidate.Rank);
            output.Append(" score=");
            output.Append(similarity.Score);
            output.Append(" operation=");
            output.Append(similarity.OperationScore);
            output.Append(" position=");
            output.Append(similarity.PositionScore);
            output.Append(" block=");
            output.Append(similarity.BlockScore);
            output.Append(" edge=");
            output.Append(similarity.EdgeScore);
            output.Append(" local=");
            output.AppendLine(
                similarity.LocalScore.ToString(
                    CultureInfo.InvariantCulture));
            output.Append("    ");
            output.AppendLine(MethodDisplay(candidate.Method));
        }

        return output.ToString();
    }

    static string MethodDisplay(StructuralCloneCensusMethod method)
        => $"0x{method.Token:X8} {method.Type}::{method.Name}";
}
