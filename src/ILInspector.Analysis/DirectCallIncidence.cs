using System.Buffers;
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
        if (calls.IsEmpty)
            return new Dictionary<int, ImmutableArray<DirectCall>>();

        // Record run tokens and ends first so the dictionary is built once
        // at its exact size instead of rehashing as groups arrive.
        int[] tokens = ArrayPool<int>.Shared.Rent(calls.Length);
        int[] ends = ArrayPool<int>.Shared.Rent(calls.Length);
        try
        {
            int runs = 0;
            int current = calls[0].EvidenceMethod.MetadataToken;
            for (int i = 1; i < calls.Length; i++)
            {
                int methodToken = calls[i].EvidenceMethod.MetadataToken;
                if (methodToken == current)
                    continue;
                tokens[runs] = current;
                ends[runs++] = i;
                current = methodToken;
            }
            tokens[runs] = current;
            ends[runs++] = calls.Length;

            var incidence =
                new Dictionary<int, ImmutableArray<DirectCall>>(runs);
            if (runs == 1)
            {
                incidence.Add(tokens[0], directCalls);
                return incidence;
            }

            int start = 0;
            for (int run = 0; run < runs; run++)
            {
                int end = ends[run];
                if (!incidence.TryAdd(
                        tokens[run],
                        ImmutableArray.Create(
                            directCalls,
                            start,
                            end - start)))
                {
                    return Interleaved(directCalls);
                }
                start = end;
            }

            return incidence;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(tokens);
            ArrayPool<int>.Shared.Return(ends);
        }
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
