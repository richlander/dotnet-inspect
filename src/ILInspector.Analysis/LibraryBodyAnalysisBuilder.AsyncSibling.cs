using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed partial class LibraryBodyAnalysisBuilder
{
    ImmutableArray<OptimizationOpportunity> CollectAsyncSiblingOpportunities(
        MethodBodyAnalysisContext context,
        ImmutableArray<DirectCall>.Builder calls,
        MethodIdentity asyncSource)
    {
        var opportunities =
            ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        DirectCall[] candidateCalls = calls
            .Where(call => call.Kind is
                CallKind.Call or CallKind.CallVirtual)
            .ToArray();
        var calledMethods =
            new Dictionary<string, List<MemberRef>>(
                StringComparer.Ordinal);
        foreach (DirectCall call in candidateCalls)
        {
            if (!calledMethods.TryGetValue(
                    call.Callee.Name,
                    out List<MemberRef>? named))
            {
                named = [];
                calledMethods.Add(
                    call.Callee.Name,
                    named);
            }
            named.Add(call.Callee);
        }
        foreach (DirectCall call in candidateCalls)
        {
            MemberRef? sibling =
                _asyncSiblingCandidateResolver.FindAsyncSibling(
                    call,
                    asyncSource);
            if (sibling is null
                || LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingMethodMatchesSource(
                    sibling,
                    asyncSource)
                || calledMethods.TryGetValue(
                    sibling.Name,
                    out List<MemberRef>? named)
                    && named.Any(called =>
                        LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingMethodsMatch(
                            called,
                            sibling)))
            {
                continue;
            }

            opportunities.Add(new OptimizationOpportunity(
                asyncSource,
                "sync-call-in-async",
                $"{LibraryBodyAsyncSiblingSignatureMatcher.FormatMember(
                    call.Callee)} is called from an async method; "
                    + $"{LibraryBodyAsyncSiblingSignatureMatcher.FormatMember(
                        sibling)} is available",
                $"Prefer {LibraryBodyAsyncSiblingSignatureMatcher.FormatMember(
                    sibling)} with await or await foreach "
                    + "when its behavior matches the synchronous call.",
                "medium",
                call.InLoop,
                call.ILOffset,
                "Name and signature shape establish the sibling relationship; "
                    + "confirm ordering, exception, cancellation, and enumeration semantics.")
            {
                EvidenceMethodToken = context.Method.MetadataToken,
            });
        }
        return opportunities.ToImmutable();
    }
}
