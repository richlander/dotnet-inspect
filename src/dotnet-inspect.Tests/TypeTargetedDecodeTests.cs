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

    static Analysis.TypeRef ClosureType(
        Analysis.LibraryBodyIndex full) =>
        full.Methods
            .Select(method => method.DeclaringType)
            .Distinct()
            .Single(type =>
                type.ToQualifiedDisplayString().EndsWith(
                    "StructuralCloneComparisonDocumentJsonContext.<>c",
                    StringComparison.Ordinal));

    static Analysis.TypeRef SourceOwnerType(
        Analysis.LibraryBodyIndex full)
    {
        string closureName =
            ClosureType(full).ToQualifiedDisplayString();
        string ownerName =
            closureName[..closureName.LastIndexOf(
                ".<>c",
                StringComparison.Ordinal)];
        return full.Methods
            .Select(method => method.DeclaringType)
            .Distinct()
            .Single(type =>
                type.ToQualifiedDisplayString() == ownerName);
    }

    static string CallFacts(
        Analysis.LibraryBodyIndex index,
        Analysis.TypeRef target) =>
        string.Join(
            ";",
            index.DirectCalls
                .Where(call =>
                    call.EvidenceMethod.DeclaringType.Equals(
                        target)
                    || call.Caller.DeclaringType.Equals(target))
                .Select(call => call.ToString())
                .OrderBy(
                    value => value,
                    StringComparer.Ordinal));

    [Fact]
    [Trait("Speed", "Slow")]
    public void TypeTargetedBuild_MatchesFullBuild_ForEveryMethodOfTheType()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var fullUnsafe = full.GetUnsafeEvidenceByMember();
        var fullUnsafety = full.GetUnsafetyOccurrences();
        var fullAlloc = full.GetAllocationOccurrences();

        var target = ClosureType(full);
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
            Assert.Equal(Facts(fullUnsafe, token), Facts(tUnsafe, token));
            Assert.Equal(Facts(fullUnsafety, token), Facts(tUnsafety, token));
            Assert.Equal(Facts(fullAlloc, token), Facts(tAlloc, token));
        }
        Assert.Equal(
            CallFacts(full, target),
            string.Join(
                ";",
                targeted.DirectCalls
                    .Select(call => call.ToString())
                    .OrderBy(
                        value => value,
                        StringComparer.Ordinal)));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void
        TypeTargetedBuild_AnalyzesOnlyPhysicalOrSourceOwnedBodies()
    {
        var full = Analysis.LibraryBodyIndex.Open(SelfPath);
        var target = SourceOwnerType(full);
        var inScope = full.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToHashSet();

        var targeted = Analysis.LibraryBodyIndex.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));

        // Every analyzed call belongs to the selected physical type or to a source method
        // of that type.
        foreach (var call in targeted.DirectCalls)
        {
            Assert.True(
                inScope.Contains(
                    call.EvidenceMethod.MetadataToken)
                || call.Caller.DeclaringType.Equals(target));
        }

        Assert.Contains(
            targeted.DirectCalls,
            call => !inScope.Contains(
                    call.EvidenceMethod.MetadataToken)
                && call.Caller.DeclaringType.Equals(target));
        Assert.Equal(
            CallFacts(full, target),
            string.Join(
                ";",
                targeted.DirectCalls
                    .Select(call => call.ToString())
                    .OrderBy(
                        value => value,
                        StringComparer.Ordinal)));
    }
}
