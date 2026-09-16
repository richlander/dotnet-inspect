namespace ILInspector.Analysis;

/// <summary>
/// Bounds aggregate IL bytes visited by one unsafe-presence query. Gated by
/// <c>UnsafeEvidencePresence_RejectsAssemblyIlAboveBudget</c>.
/// </summary>
internal sealed class UnsafePresenceWorkBudget
{
    internal const int MaxIlBytes = 4 * 1024 * 1024;

    long _ilBytes;

    internal void ReserveIlBytes(
        int bytes)
    {
        while (true)
        {
            long current = Volatile.Read(
                ref _ilBytes);
            if (current < 0
                || bytes > MaxIlBytes - current)
            {
                Interlocked.Exchange(
                    ref _ilBytes,
                    -1);
                throw new BadImageFormatException(
                    "Unsafe evidence IL scanning exceeds the assembly budget.");
            }
            if (Interlocked.CompareExchange(
                    ref _ilBytes,
                    current + bytes,
                    current)
                == current)
            {
                return;
            }
        }
    }
}
