using System.Collections.Immutable;
using Analysis = ILInspector.Analysis;

namespace DotnetInspect.Cli.Tests;

/// <summary>
/// Locks the targeted-decode invariant: opening a <see cref="Analysis.LibraryBodyAnalysisExecution"/> with a
/// <c>bodyScope</c> restricted to one method decodes only that body, yet produces per-method facts
/// (direct calls, unsafe evidence, unsafety occurrences, allocation occurrences) identical to the
/// full whole-assembly build. This is what lets the member command render Calls / Unsafe Operations
/// / Allocation-Safety-Cost facts for one member without decoding every method in the assembly.
/// </summary>
public class TargetedDecodeTests
{
    static string SelfPath => typeof(Analysis.LibraryBodyAnalysisExecution).Assembly.Location;

    static string Facts<T>(IReadOnlyDictionary<int, ImmutableArray<T>> byToken, int token)
        => byToken.TryGetValue(token, out var v)
            ? string.Join(";", v.Select(x => x!.ToString()).OrderBy(s => s, StringComparer.Ordinal))
            : "";

    [Fact]
    [Trait("Speed", "Slow")]
    public void TargetedBuild_MatchesFullBuild_ForEveryPerMethodFactOfTheTarget()
    {
        var full = BodyAnalysisTestExecution.Open(SelfPath);
        var fullCalls = full.CallGraph.DirectCallsByCaller;
        var fullUnsafe = full.Safety.GetEvidenceByMember();
        var fullUnsafety = full.Safety.Occurrences;
        var fullAlloc = full.Allocations.Occurrences;

        // Sample methods that actually carry each kind of fact, plus a few plain ones.
        var sample = new HashSet<int>();
        if (fullCalls.Count > 0) sample.Add(fullCalls.OrderByDescending(kv => kv.Value.Length).First().Key);
        if (fullAlloc.Count > 0) sample.Add(fullAlloc.First().Key);
        if (fullUnsafe.Count > 0) sample.Add(fullUnsafe.First().Key);
        foreach (var m in full.CallGraph.Methods.Take(25))
            sample.Add(m.MetadataToken);

        Assert.NotEmpty(sample);
        foreach (var token in sample)
        {
            var targeted = BodyAnalysisTestExecution.Open(SelfPath, bodyScope: new HashSet<int> { token });
            Assert.Equal(Facts(fullCalls, token), Facts(targeted.CallGraph.DirectCallsByCaller, token));
            Assert.Equal(Facts(fullUnsafe, token), Facts(targeted.Safety.GetEvidenceByMember(), token));
            Assert.Equal(Facts(fullUnsafety, token), Facts(targeted.Safety.Occurrences, token));
            Assert.Equal(Facts(fullAlloc, token), Facts(targeted.Allocations.Occurrences, token));
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TargetedBuild_DecodesOnlyTheScopedMember()
    {
        var full = BodyAnalysisTestExecution.Open(SelfPath);
        var caller = full.CallGraph.DirectCallsByCaller.OrderByDescending(kv => kv.Value.Length).First().Key;

        var targeted = BodyAnalysisTestExecution.Open(SelfPath, bodyScope: new HashSet<int> { caller });
        var targetedCalls = targeted.CallGraph.DirectCallsByCaller;

        // Only the scoped member has decoded direct calls; other callers are not decoded.
        Assert.True(targetedCalls.ContainsKey(caller));
        Assert.Single(targetedCalls);
    }
}
