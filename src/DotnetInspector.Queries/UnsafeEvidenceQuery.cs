using System.Collections.Immutable;
using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>Typed result of reading unsafe declaration and body evidence.</summary>
public abstract record UnsafeEvidenceResult
{
    private UnsafeEvidenceResult()
    {
    }

    /// <summary>The complete unsafe-evidence census and any per-method diagnostics.</summary>
    public sealed record Available(
        ImmutableArray<UnsafeEvidence> Evidence,
        ImmutableArray<AnalysisDiagnostic> Diagnostics) : UnsafeEvidenceResult;

    /// <summary>The image contains no managed metadata and therefore has no method bodies.</summary>
    public sealed record NoMetadata : UnsafeEvidenceResult;

    /// <summary>The query failed while acquiring or reading whole-assembly analysis.</summary>
    public sealed record Failed(Exception Error) : UnsafeEvidenceResult;
}

/// <summary>Reads unsafe evidence from an already-acquired whole-assembly body index.</summary>
public static class UnsafeEvidenceQuery
{
    public static InspectionQuery<UnsafeEvidenceResult> Definition { get; } =
        new("Unsafe evidence", InspectionCost.Unbounded);

    public static UnsafeEvidenceResult Execute(LibraryBodyIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);

        try
        {
            return new UnsafeEvidenceResult.Available(
                index.UnsafeEvidence,
                index.Diagnostics);
        }
        catch (Exception ex)
        {
            return new UnsafeEvidenceResult.Failed(ex);
        }
    }
}
