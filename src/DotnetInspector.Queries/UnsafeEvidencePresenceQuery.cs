using System.Collections.Immutable;
using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>Typed result of probing whether an assembly contains any unsafe evidence.</summary>
public abstract record UnsafeEvidencePresenceResult
{
    private UnsafeEvidencePresenceResult()
    {
    }

    /// <summary>The probe completed and reports whether at least one finding exists.</summary>
    public sealed record Available(bool HasEvidence) : UnsafeEvidencePresenceResult;

    /// <summary>The probe failed while reading the retained assembly image.</summary>
    public sealed record Failed(Exception Error) : UnsafeEvidencePresenceResult;
}

/// <summary>
/// Stops at the first unsafe finding without materializing the complete unsafe-evidence census.
/// </summary>
public static class UnsafeEvidencePresenceQuery
{
    public static InspectionQuery<UnsafeEvidencePresenceResult> Definition { get; } =
        new("Unsafe evidence presence", InspectionCost.NetworkFree);

    public static UnsafeEvidencePresenceResult Execute(
        string path,
        ImmutableArray<byte> image)
    {
        try
        {
            return new UnsafeEvidencePresenceResult.Available(
                LibraryBodyIndex.HasUnsafeEvidence(path, image));
        }
        catch (Exception ex)
        {
            return new UnsafeEvidencePresenceResult.Failed(ex);
        }
    }
}
