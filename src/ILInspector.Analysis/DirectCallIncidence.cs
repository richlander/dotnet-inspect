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
/// slicing: one dictionary insert per method and no per-call hashing. The
/// result is a view over the one shared population; a group is copied into
/// its own array only when a consumer first reads it, so point lookups do
/// not pay for every other method's slice.
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

            // Analysis emits bodies in metadata order, so run tokens are
            // normally ascending and a binary search replaces hashing.
            bool ascending = true;
            for (int run = 1; run < runs && ascending; run++)
                ascending = tokens[run - 1] < tokens[run];
            Dictionary<int, int>? ordinals = null;
            if (!ascending)
            {
                ordinals = new Dictionary<int, int>(runs);
                for (int run = 0; run < runs; run++)
                {
                    if (!ordinals.TryAdd(tokens[run], run))
                        return Interleaved(directCalls);
                }
            }

            return new RunView(
                directCalls,
                tokens.AsSpan(0, runs),
                ordinals,
                ends.AsSpan(0, runs));
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

    /// <summary>
    /// Read-only token-to-run index over one contiguous population. Keys
    /// enumerate in run order, which is first-appearance order.
    /// </summary>
    sealed class RunView :
        IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
    {
        readonly ImmutableArray<DirectCall> _calls;
        readonly ImmutableArray<int> _tokens;
        readonly Dictionary<int, int>? _ordinals;
        readonly int[] _ends;
        readonly ImmutableArray<DirectCall>[] _groups;

        internal RunView(
            ImmutableArray<DirectCall> calls,
            ReadOnlySpan<int> tokens,
            Dictionary<int, int>? ordinals,
            ReadOnlySpan<int> ends)
        {
            _calls = calls;
            _tokens = [.. tokens];
            _ordinals = ordinals;
            _ends = ends.ToArray();
            _groups = new ImmutableArray<DirectCall>[_ends.Length];
        }

        public int Count => _ends.Length;

        public ImmutableArray<DirectCall> this[int key] =>
            TryGetValue(key, out ImmutableArray<DirectCall> value)
                ? value
                : throw new KeyNotFoundException(
                    $"No direct calls have evidence method {key:X8}.");

        public IEnumerable<int> Keys => _tokens;

        public IEnumerable<ImmutableArray<DirectCall>> Values
        {
            get
            {
                for (int run = 0; run < _ends.Length; run++)
                    yield return Group(run);
            }
        }

        public bool ContainsKey(int key) => Run(key) >= 0;

        public bool TryGetValue(
            int key,
            out ImmutableArray<DirectCall> value)
        {
            int run = Run(key);
            if (run < 0)
            {
                value = default;
                return false;
            }
            value = Group(run);
            return true;
        }

        public IEnumerator<KeyValuePair<int, ImmutableArray<DirectCall>>>
            GetEnumerator()
        {
            for (int run = 0; run < _tokens.Length; run++)
                yield return new(_tokens[run], Group(run));
        }

        int Run(int key)
        {
            if (_ordinals is null)
            {
                int low = 0;
                int high = _tokens.Length - 1;
                while (low <= high)
                {
                    int middle = low + ((high - low) >> 1);
                    int token = _tokens[middle];
                    if (token == key)
                        return middle;
                    if (token < key)
                        low = middle + 1;
                    else
                        high = middle - 1;
                }
                return -1;
            }
            return _ordinals.TryGetValue(key, out int ordinal)
                ? ordinal
                : -1;
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();

        ImmutableArray<DirectCall> Group(int run)
        {
            ImmutableArray<DirectCall> group = _groups[run];
            if (!group.IsDefault)
                return group;
            int start = run == 0 ? 0 : _ends[run - 1];
            int end = _ends[run];
            group = start == 0 && end == _calls.Length
                ? _calls
                : ImmutableArray.Create(_calls, start, end - start);
            _groups[run] = group;
            return group;
        }
    }
}
