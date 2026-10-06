namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One completed pass occurrence in an opt-in observed run.
/// </summary>
public sealed record PassExecutionReceipt(int Ordinal, string PassName, bool Changed);

/// <summary>Aggregations over ordered pass execution receipts.</summary>
public static class PassExecutionReceipts
{
    /// <summary>
    /// Returns each pass name whose observed projection changed in at least one
    /// occurrence.
    /// </summary>
    public static IReadOnlyCollection<string> ChangedPasses(
        IReadOnlyList<PassExecutionReceipt> receipts)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var receipt in receipts)
            if (receipt.Changed)
                changed.Add(receipt.PassName);
        return changed;
    }
}
