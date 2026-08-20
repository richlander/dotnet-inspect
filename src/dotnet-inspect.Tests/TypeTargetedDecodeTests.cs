using System.Collections.Immutable;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the type-targeted-decode invariant: opening a <see cref="Analysis.LibraryBodyIndex"/> with a
/// <c>bodyTypeScope</c> predicate restricted to one declaring type analyzes only evidence bodies
/// belonging to that type or to source methods of that type, yet produces per-evidence-method facts
/// (direct calls, unsafe evidence, unsafety occurrences, allocation occurrences) identical to the
/// full whole-assembly build for every method of that type.
/// This is what lets the type command render Unsafe Members / Called Types / Allocation-Safety-Cost
/// facts for one type — including its private/compiler-generated methods — without decoding every
/// method in the assembly. Unlike a token <c>bodyScope</c> (member command), a declaring-type
/// predicate is required because type sections scan ALL of a type's methods, not just its public API.
/// </summary>
public class TypeTargetedDecodeTests
{
    static string SelfPath => typeof(Analysis.LibraryBodyIndex).Assembly.Location;

    static string Facts<T>(IReadOnlyDictionary<int, ImmutableArray<T>> byToken, int token)
        => byToken.TryGetValue(token, out var v)
            ? string.Join(";", v.Select(x => x!.ToString()).OrderBy(s => s, StringComparer.Ordinal))
            : "";

    static Analysis.TypeRef LargestType(Analysis.LibraryBodyIndex full)
        => full.Methods
            .GroupBy(m => m.DeclaringType)
            .OrderByDescending(g => g.Count())
            .First().Key;

    static string CallFacts(
        Analysis.LibraryBodyIndex index,
        int evidenceToken) =>
        string.Join(
            ";",
            index.DirectCalls
                .Where(call =>
                    call.EvidenceMethod.MetadataToken
                        == evidenceToken)
                .Select(call => call.ToString())
                .OrderBy(
                    value => value,
                    StringComparer.Ordinal));

    [Fact]
    public void TypeTargetedBuild_MatchesFullBuild_ForEveryMethodOfTheType()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var fullUnsafe = full.GetUnsafeEvidenceByMember();
        var fullUnsafety = full.GetUnsafetyOccurrences();
        var fullAlloc = full.GetAllocationOccurrences();

        var target = LargestType(full);
        var tokens = full.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToArray();
        Assert.NotEmpty(tokens);

        var targeted = Analysis.LibraryBodyIndex.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));
        var tUnsafe = targeted.GetUnsafeEvidenceByMember();
        var tUnsafety = targeted.GetUnsafetyOccurrences();
        var tAlloc = targeted.GetAllocationOccurrences();

        Assert.Contains(
            targeted.DirectCalls,
            call => call.EvidenceMethod.DeclaringType.Equals(
                    target)
                && !call.Caller.DeclaringType.Equals(target));
        foreach (var token in tokens)
        {
            Assert.Equal(
                CallFacts(full, token),
                CallFacts(targeted, token));
            Assert.Equal(Facts(fullUnsafe, token), Facts(tUnsafe, token));
            Assert.Equal(Facts(fullUnsafety, token), Facts(tUnsafety, token));
            Assert.Equal(Facts(fullAlloc, token), Facts(tAlloc, token));
        }
    }

    [Fact]
    public void TypeTargetedBuild_DecodesOnlyTheScopedType()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var target = LargestType(full);
        var inScope = full.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToHashSet();

        var targeted = Analysis.LibraryBodyIndex.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));

        // Every direct-call evidence body belongs to the scoped type; declared callers may
        // belong to source-owner types outside that physical scope.
        foreach (var call in targeted.DirectCalls)
            Assert.Contains(
                call.EvidenceMethod.MetadataToken,
                inScope);

        // At least one method of the target type actually decoded (the type has bodies with calls).
        Assert.NotEmpty(targeted.DirectCalls);
    }
}
