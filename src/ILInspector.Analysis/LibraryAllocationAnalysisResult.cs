using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Detached allocation occurrences produced by one library-body Analysis
/// execution.
/// </summary>
public sealed class LibraryAllocationAnalysisResult
{
    internal LibraryAllocationAnalysisResult(
        LibraryBodyAnalysisReceipt receipt,
        AllocationAnalysisResult allocations)
    {
        Receipt = receipt;
        Occurrences = allocations.Occurrences;
    }

    /// <summary>
    /// Common identity, coverage, and diagnostics for the producing execution.
    /// </summary>
    public LibraryBodyAnalysisReceipt Receipt { get; }

    /// <summary>Whether allocation production participated.</summary>
    public bool WasRequested =>
        Receipt.Features.HasFlag(
            LibraryBodyAnalysisFeatures.Allocations);

    /// <summary>
    /// Allocation occurrences grouped by the physical MethodDef body that owns
    /// their IL coordinates.
    /// </summary>
    public IReadOnlyDictionary<
        int,
        ImmutableArray<AllocationOccurrence>> Occurrences { get; }
}
