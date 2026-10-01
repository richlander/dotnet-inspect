using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal static class DirectCallIncidence
{
    internal static IReadOnlyDictionary<int, ImmutableArray<DirectCall>>
        ByEvidenceMethod(ImmutableArray<DirectCall> directCalls)
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
