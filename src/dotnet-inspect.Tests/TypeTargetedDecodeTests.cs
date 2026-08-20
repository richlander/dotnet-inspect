using System.Collections.Immutable;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the type-targeted-decode invariant: opening a <see cref="Analysis.LibraryBodyIndex"/> with a
/// <c>bodyTypeScope</c> predicate restricted to one declaring type decodes only that type's method
/// bodies, yet produces per-method facts (direct calls, unsafe evidence, unsafety occurrences,
/// allocation occurrences) identical to the full whole-assembly build for every method of that type.
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

    [Fact]
    public void TypeTargetedBuild_MatchesFullBuild_ForEveryMethodOfTheType()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var fullCalls = full.GetDirectCallsByEvidenceMethod();
        var fullUnsafe = full.GetUnsafeEvidenceByMember();
        var fullUnsafety = full.GetUnsafetyOccurrences();
        var fullAlloc = full.GetAllocationOccurrences();

        var target = LargestType(full);
        var tokens = full.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToArray();
        Assert.NotEmpty(tokens);

        var targeted = Analysis.LibraryBodyIndex.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));
        var tCalls = targeted.GetDirectCallsByEvidenceMethod();
        var tUnsafe = targeted.GetUnsafeEvidenceByMember();
        var tUnsafety = targeted.GetUnsafetyOccurrences();
        var tAlloc = targeted.GetAllocationOccurrences();

        foreach (var token in tokens)
        {
            Assert.Equal(Facts(fullCalls, token), Facts(tCalls, token));
            Assert.Equal(Facts(fullUnsafe, token), Facts(tUnsafe, token));
            Assert.Equal(Facts(fullUnsafety, token), Facts(tUnsafety, token));
            Assert.Equal(Facts(fullAlloc, token), Facts(tAlloc, token));
        }
    }

    [Fact]
    public void TypeTargetedBuild_PreservesDeclaredCallerForScopedLiftedBody()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        Analysis.DirectCall attributed = full.DirectCalls.First(
            call => call.Caller != call.EvidenceMethod);
        Analysis.TypeRef evidenceType =
            attributed.EvidenceMethod.DeclaringType;

        var targeted = Analysis.LibraryBodyIndex.Open(
            SelfPath,
            bodyTypeScope: type => type.Equals(evidenceType));

        Assert.Contains(
            attributed,
            targeted.DirectCalls);
    }

    [Fact]
    public void TypeTargetedBuild_DecodesOnlyTheScopedType()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var target = LargestType(full);
        var inScope = full.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToHashSet();

        var targeted = Analysis.LibraryBodyIndex.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));

        // Every decoded call-site body belongs to the scoped type; its declared
        // caller may be a source owner outside that generated physical type.
        foreach (var evidenceMethod
            in targeted.GetDirectCallsByEvidenceMethod().Keys)
        {
            Assert.Contains(evidenceMethod, inScope);
        }

        // At least one method of the target type actually decoded (the type has bodies with calls).
        Assert.NotEmpty(targeted.GetDirectCallsByEvidenceMethod());
    }
}
