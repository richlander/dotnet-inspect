using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>One synchronous call and the async sibling the same declaring type offers.</summary>
internal readonly record struct AsyncSiblingMatch(
    DirectCall Call,
    MemberRef Sibling);

/// <summary>
/// Finds synchronous calls in an async method whose declaring type also offers
/// a <c>FooAsync</c> sibling. It owns the sibling method index and the dispatch,
/// accessibility, and candidate analysis, composed from the reader and the
/// caller-supplied type-definition lookups, so a producer and the legacy
/// builder share one implementation.
/// </summary>
internal sealed class AsyncSiblingOpportunityAnalyzer
{
    readonly LibraryBodyAsyncSiblingCandidateResolver _candidateResolver;

    internal AsyncSiblingOpportunityAnalyzer(
        MetadataReader reader,
        AssemblyReferenceIdentity assemblyIdentity,
        Func<
            AssemblyReferenceIdentity,
            AssemblyResolutionScope,
            MetadataTypeDefinitionName,
            (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?>
            resolveExternalTypeDefinition,
        Func<
            IReadOnlyDictionary<
                MetadataTypeDefinitionName,
                TypeDefinitionHandle>> localTypeDefinitions,
        Func<MetadataReader, MethodDefinition, bool> hasGenericConstraints,
        Action<MetadataReader, MethodDefinitionHandle>? methodScanned = null)
    {
        var methodIndex =
            new LibraryBodyAsyncSiblingMethodIndex(methodScanned);
        var dispatchAnalyzer =
            new LibraryBodyAsyncSiblingDispatchAnalyzer(
                reader,
                resolveExternalTypeDefinition,
                methodIndex,
                hasGenericConstraints);
        var accessibilityAnalyzer =
            new LibraryBodyAsyncSiblingAccessibilityAnalyzer(
                reader,
                assemblyIdentity,
                dispatchAnalyzer);
        _candidateResolver =
            new LibraryBodyAsyncSiblingCandidateResolver(
                reader,
                resolveExternalTypeDefinition,
                localTypeDefinitions,
                methodIndex,
                dispatchAnalyzer,
                accessibilityAnalyzer,
                hasGenericConstraints);
    }

    /// <summary>
    /// Returns each <c>call</c>/<c>callvirt</c> in <paramref name="calls"/> that
    /// has a distinct async sibling the body does not already call.
    /// </summary>
    internal ImmutableArray<AsyncSiblingMatch> FindMatches(
        IEnumerable<DirectCall> calls,
        MethodIdentity asyncSource) =>
        FindMatches(calls, asyncSource, out _);

    /// <summary>
    /// As <see cref="FindMatches(IEnumerable{DirectCall}, MethodIdentity)"/>,
    /// also returning the calls whose declaring type could not be resolved.
    /// </summary>
    internal ImmutableArray<AsyncSiblingMatch> FindMatches(
        IEnumerable<DirectCall> calls,
        MethodIdentity asyncSource,
        out ImmutableArray<DirectCall> unresolvedCalls)
    {
        var unresolved = ImmutableArray.CreateBuilder<DirectCall>();
        var matches = ImmutableArray.CreateBuilder<AsyncSiblingMatch>();
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
                _candidateResolver.FindAsyncSibling(
                    call,
                    asyncSource,
                    out bool skippedOnUnresolved);
            if (sibling is null
                && (skippedOnUnresolved
                    || _candidateResolver.IsDeclaringTypeUnresolved(call)))
            {
                unresolved.Add(call);
            }

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

            matches.Add(new(call, sibling));
        }
        unresolvedCalls = unresolved.ToImmutable();
        return matches.ToImmutable();
    }
}
