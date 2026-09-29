using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal static class DirectCallIncidence
{
    internal static IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        ByEvidenceMethod(ImmutableArray<DirectCall> directCalls) =>
        directCalls
            .GroupBy(static call =>
                call.EvidenceMethod.MetadataToken)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());
}
