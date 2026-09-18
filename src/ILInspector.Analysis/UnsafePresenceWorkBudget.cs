using System.Reflection.Metadata;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Bounds aggregate IL and same-image correspondence work visited by one
/// unsafe-presence query. Gated by
/// <c>UnsafeEvidencePresence_RejectsAssemblyIlAboveBudget</c> and
/// <c>UnsafeEvidencePresence_RejectsSameImageCorrespondenceAboveBudget</c>.
/// </summary>
internal sealed class UnsafePresenceWorkBudget
{
    internal const int MaxIlBytes = 4 * 1024 * 1024;
    internal const int MaxCorrespondenceBytes =
        ILInspector.Metadata.MetadataSafetyPolicy
            .MaxStructuralSignatureWorkChars;
    internal const int MaxCorrespondenceRows =
        ILInspector.Metadata.MetadataSafetyPolicy
            .MaxCorrespondenceMethodRows;

    long _ilBytes;
    long _correspondenceBytes;
    long _correspondenceRows;
    Dictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>?
        _localTypeDefinitions;

    internal void ReserveIlBytes(
        int bytes)
        => Reserve(
            ref _ilBytes,
            bytes,
            MaxIlBytes,
            "Unsafe evidence IL scanning exceeds the assembly budget.");

    internal void ReserveCorrespondenceBytes(
        int bytes)
        => Reserve(
            ref _correspondenceBytes,
            Math.Max(bytes, 1),
            MaxCorrespondenceBytes,
            "Unsafe evidence same-image correspondence exceeds the assembly budget.");

    internal void ThrowIfCorrespondenceByteBudgetExceeded()
    {
        if (Volatile.Read(
                ref _correspondenceBytes)
            < 0)
        {
            throw new BadImageFormatException(
                "Unsafe evidence same-image correspondence exceeds the assembly budget.");
        }
    }

    internal void ReserveCorrespondenceRow()
        => Reserve(
            ref _correspondenceRows,
            1,
            MaxCorrespondenceRows,
            "Unsafe evidence same-image correspondence exceeds the metadata-row budget.");

    internal Dictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle> GetOrCreateLocalTypeDefinitions(
            Func<Dictionary<
                MetadataTypeDefinitionName,
                TypeDefinitionHandle>> create)
        => _localTypeDefinitions ??= create();

    static void Reserve(
        ref long work,
        int bytes,
        int maximum,
        string failure)
    {
        while (true)
        {
            long current = Volatile.Read(
                ref work);
            if (current < 0
                || bytes > maximum - current)
            {
                Interlocked.Exchange(
                    ref work,
                    -1);
                throw new BadImageFormatException(
                    failure);
            }
            if (Interlocked.CompareExchange(
                    ref work,
                    current + bytes,
                    current)
                == current)
            {
                return;
            }
        }
    }
}
