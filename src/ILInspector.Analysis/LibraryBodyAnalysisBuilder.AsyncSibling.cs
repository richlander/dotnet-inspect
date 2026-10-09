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
        foreach ((DirectCall call, MemberRef sibling)
            in _asyncSiblingAnalyzer.FindMatches(calls, asyncSource))
        {
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
                AsyncSibling = new(call, sibling),
                EvidenceMethodToken = context.Method.MetadataToken,
            });
        }
        return opportunities.ToImmutable();
    }
}
