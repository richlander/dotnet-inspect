using System.Collections.Immutable;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

internal static class DirectCallEvidence
{
    internal static ImmutableArray<DirectCall> Physicalize(
        ImmutableArray<DirectCall> calls)
        => calls.All(call => call.Caller == call.EvidenceMethod)
            ? calls
            :
            [
                .. calls.Select(static call =>
                    call.Caller == call.EvidenceMethod
                        ? call
                        : call with
                        {
                            Caller = call.EvidenceMethod,
                        }),
            ];
}
