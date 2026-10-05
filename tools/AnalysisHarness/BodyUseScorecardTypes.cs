using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using NLinq;

namespace ILInspector.AnalysisHarness;

public enum BodyUseScorecardColumn
{
    Direct,
    Linq,
    NLinq,
    Planner,
}

public enum BodyUseScorecardClosing
{
    Exists,
    Count,
    Rows,
}

public enum BodyUseScorecardDisposition
{
    Settled,
    Complete,
    Qualified,
    Partial,
}

public sealed record BodyUseScorecardAsset(
    string Name,
    string SourceName,
    ImmutableArray<byte> Image);

public sealed record BodyUseScorecardTerminalEvidence(
    BodyUseScorecardDisposition Disposition,
    AnalysisLibraryBodyUseCoverage Coverage,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

public abstract record BodyUseScorecardAnswer
{
    private protected BodyUseScorecardAnswer()
    {
    }

    public sealed record Exists(
        bool Value,
        BodyUseScorecardTerminalEvidence Evidence)
        : BodyUseScorecardAnswer;

    public sealed record Count(
        int Value,
        BodyUseScorecardTerminalEvidence Evidence)
        : BodyUseScorecardAnswer;

    public sealed record Rows(
        AnalysisLibraryBodyUseDisposition Disposition,
        ImmutableArray<AnalysisLibraryBodyUseType> Types,
        ImmutableArray<AnalysisLibraryBodyUseOccurrence> Occurrences,
        ImmutableArray<AnalysisLibraryBodyUsePhysicalEvidence> PhysicalEvidence,
        AnalysisLibraryBodyUseCoverage Coverage,
        ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics)
        : BodyUseScorecardAnswer;
}

public sealed record BodyUseScorecardRejection(
    AnalysisLibraryBodyUseRejectionKind Kind,
    string Detail)
{
    public override string ToString() => $"{Kind}: {Detail}";
}

public sealed record BodyUseScorecardExecution(
    BodyUseScorecardColumn Column,
    BodyUseScorecardClosing Closing,
    BodyUseScorecardAnswer? Answer,
    BodyUseScorecardRejection? Rejection)
{
    public bool Available => Answer is not null;
}

public sealed record BodyUseScorecardMismatch(
    string Asset,
    BodyUseScorecardClosing Closing,
    BodyUseScorecardColumn Column,
    string Answer,
    string OracleAnswer);

public sealed record BodyUseScorecardAnswerHash(
    string Asset,
    BodyUseScorecardClosing Closing,
    string Hash);

public sealed record BodyUseScorecardCheck(
    int Compared,
    IReadOnlyList<BodyUseScorecardMismatch> Mismatches,
    IReadOnlyList<BodyUseScorecardAnswerHash> AnswerHashes)
{
    public bool Agrees => Mismatches.Count == 0;
}

public sealed record BodyUseScorecardCell(
    int AssetIndex,
    string Asset,
    BodyUseScorecardClosing Closing,
    BodyUseScorecardColumn Column,
    IReadOnlyList<double> RoundMediansMicroseconds,
    IReadOnlyList<long> RoundMediansAllocatedBytes)
{
    public double MedianMicroseconds =>
        Median(RoundMediansMicroseconds);

    public long MedianAllocatedBytes =>
        Median(RoundMediansAllocatedBytes);

    static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = [.. values];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    static long Median(IReadOnlyList<long> values)
    {
        long[] sorted = [.. values];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
