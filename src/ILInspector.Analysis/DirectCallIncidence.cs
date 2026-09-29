using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>
/// Physical-method-token incidence over one detached direct-call population.
/// Groups keep source call order and are enumerated in first-appearance order.
/// </summary>
/// <remarks>
/// Library-body Analysis appends each method body's calls as one block
/// (<c>LibraryBodyAnalysisAccumulator.Build</c>), so a producer population
/// is contiguous per evidence method and grouping reduces to run-length
/// slicing: one dictionary insert per method and no per-call hashing.
/// Contiguity is observed, not typed: a population whose token reappears
/// after another token falls back to the general grouping loop, so the
/// answer is exact either way.
/// </remarks>
internal static class DirectCallIncidence
{
    internal static IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        ByEvidenceMethod(ImmutableArray<DirectCall> directCalls)
    {
        ReadOnlySpan<DirectCall> calls = directCalls.AsSpan();
        var incidence = new Dictionary<int, ImmutableArray<DirectCall>>();
        int start = 0;
        while (start < calls.Length)
        {
            int methodToken = calls[start].EvidenceMethod.MetadataToken;
            int end = start + 1;
            while (end < calls.Length
                && calls[end].EvidenceMethod.MetadataToken == methodToken)
            {
                end++;
            }

            ImmutableArray<DirectCall> group =
                start == 0 && end == calls.Length
                    ? directCalls
                    : ImmutableArray.Create(
                        directCalls,
                        start,
                        end - start);
            if (!incidence.TryAdd(methodToken, group))
                return Interleaved(directCalls);
            start = end;
        }

        return incidence;
    }

    static Dictionary<int, ImmutableArray<DirectCall>> Interleaved(
        ImmutableArray<DirectCall> directCalls)
    {
        var builders =
            new Dictionary<int, ImmutableArray<DirectCall>.Builder>();
        foreach (DirectCall call in directCalls)
        {
            int methodToken = call.EvidenceMethod.MetadataToken;
            if (!builders.TryGetValue(
                    methodToken,
                    out ImmutableArray<DirectCall>.Builder? builder))
            {
                builder = ImmutableArray.CreateBuilder<DirectCall>();
                builders.Add(methodToken, builder);
            }
            builder.Add(call);
        }

        var incidence =
            new Dictionary<int, ImmutableArray<DirectCall>>(
                builders.Count);
        foreach (var pair in builders)
            incidence.Add(pair.Key, pair.Value.ToImmutable());
        return incidence;
    }
}
