using System.Collections.Immutable;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Locks the type-targeted-decode invariant: opening a <see cref="Analysis.LibraryBodyAnalysisExecution"/> with a
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
    static string SelfPath => typeof(Analysis.LibraryBodyAnalysisExecution).Assembly.Location;

    static string Facts<T>(IReadOnlyDictionary<int, ImmutableArray<T>> byToken, int token)
        => byToken.TryGetValue(token, out var v)
            ? string.Join(";", v.Select(x => x!.ToString()).OrderBy(s => s, StringComparer.Ordinal))
            : "";

    static Analysis.TypeRef ClosureType(
        Analysis.LibraryBodyAnalysisExecution full) =>
        full.CallGraph.Methods
            .Select(method => method.DeclaringType)
            .Distinct()
            .Single(type =>
                type.ToQualifiedDisplayString().EndsWith(
                    "StructuralCloneComparisonDocumentJsonContext.<>c",
                    StringComparison.Ordinal));

    static Analysis.TypeRef SourceOwnerType(
        Analysis.LibraryBodyAnalysisExecution full)
    {
        string closureName =
            ClosureType(full).ToQualifiedDisplayString();
        string ownerName =
            closureName[..closureName.LastIndexOf(
                ".<>c",
                StringComparison.Ordinal)];
        return full.CallGraph.Methods
            .Select(method => method.DeclaringType)
            .Distinct()
            .Single(type =>
                type.ToQualifiedDisplayString() == ownerName);
    }

    static string CallFacts(
        Analysis.LibraryBodyAnalysisExecution index,
        Analysis.TypeRef target) =>
        string.Join(
            ";",
            index.CallGraph.DirectCalls
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
        var full = BodyAnalysisTestExecution.Open(SelfPath);
        var fullUnsafe = full.Safety.GetEvidenceByMember();
        var fullUnsafety = full.Safety.Occurrences;
        var fullAlloc = full.Allocations.Occurrences;

        var target = ClosureType(full);
        var tokens = full.CallGraph.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToArray();
        Assert.NotEmpty(tokens);

        var targeted = BodyAnalysisTestExecution.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));
        var tUnsafe = targeted.Safety.GetEvidenceByMember();
        var tUnsafety = targeted.Safety.Occurrences;
        var tAlloc = targeted.Allocations.Occurrences;

        Assert.Contains(
            targeted.CallGraph.DirectCalls,
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
                targeted.CallGraph.DirectCalls
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
        var full = BodyAnalysisTestExecution.Open(SelfPath);
        var target = SourceOwnerType(full);
        var inScope = full.CallGraph.Methods.Where(m => m.DeclaringType.Equals(target)).Select(m => m.MetadataToken).ToHashSet();

        var targeted = BodyAnalysisTestExecution.Open(SelfPath, bodyTypeScope: tr => tr.Equals(target));

        // Every analyzed call belongs to the selected physical type or to a source method
        // of that type.
        foreach (var call in targeted.CallGraph.DirectCalls)
        {
            Assert.True(
                inScope.Contains(
                    call.EvidenceMethod.MetadataToken)
                || call.Caller.DeclaringType.Equals(target));
        }

        Assert.Contains(
            targeted.CallGraph.DirectCalls,
            call => !inScope.Contains(
                    call.EvidenceMethod.MetadataToken)
                && call.Caller.DeclaringType.Equals(target));
        Assert.Equal(
            CallFacts(full, target),
            string.Join(
                ";",
                targeted.CallGraph.DirectCalls
                    .Select(call => call.ToString())
                    .OrderBy(
                        value => value,
                        StringComparer.Ordinal)));
    }
}
