using System.Linq;
using DotnetInspector.Inspectors;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Tests;

/// <summary>
/// Tests for <see cref="MethodBodyInspectionSession"/>: the method-body composition layer that
/// projects method/coordinate-scoped semantic facts over one shared analysis index. See
/// <c>docs/design/method-body-inspection.md</c>.
/// </summary>
public class MethodBodyInspectionSessionTests
{
    static string SelfPath => typeof(MethodBodyInspectionSession).Assembly.Location;

    static int AllocatingToken(Analysis.LibraryBodyIndex index)
        => index.GetAllocationOccurrences().First(kv => kv.Value.Length > 0).Key;

    [Fact]
    public void AllocationFacts_MatchDirectProjection_ForAllocatingMethod()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var token = AllocatingToken(index);

        var expected = Analysis.SemanticFactProjection.AllocationFacts(index.GetAllocationOccurrences(), token);
        var actual = MethodBodyInspectionSession.Open(SelfPath).AllocationFacts(token);

        Assert.NotEmpty(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AllocationFacts_UnknownToken_ReturnsEmpty()
        => Assert.Empty(MethodBodyInspectionSession.Open(SelfPath).AllocationFacts(methodToken: 0));

    [Fact]
    public void AllocationFacts_CoordinateScoped_FiltersToOffset()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var pair = index.GetAllocationOccurrences().First(kv => kv.Value.Length > 0);
        var offset = pair.Value[0].ILOffset;

        var atOffset = MethodBodyInspectionSession.Open(SelfPath).AllocationFacts(pair.Key, offset);

        Assert.NotEmpty(atOffset);
        Assert.All(atOffset, fact => Assert.Equal(offset, fact.ILOffset));
    }

    [Fact]
    public void OneSession_ProducesAllThreeFactKinds_OverSharedIndex()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var token = AllocatingToken(index);
        var session = MethodBodyInspectionSession.Open(SelfPath);

        Assert.NotEmpty(session.AllocationFacts(token)); // token chosen for allocations
        _ = session.SafetyFacts(token).ToList();          // must not throw
        _ = session.CostFacts(token).ToList();            // must not throw
    }

    static int CallingToken(Analysis.LibraryBodyIndex index)
        => index.GetDirectCallsByCaller().First(kv => kv.Value.Length > 0).Key;

    [Fact]
    public void CallTree_MatchesDirectBuild_ForCallingMethod()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var token = CallingToken(index);

        var expected = index.BuildCallTree(token);
        var actual = MethodBodyInspectionSession.Open(SelfPath).CallTree(token);

        Assert.NotEmpty(actual.Children);
        Assert.Equal(expected.Children.Count(), actual.Children.Count());
    }

    [Fact]
    public void CallerTree_NoScope_ReturnsRoot()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var token = CallingToken(index);

        Assert.NotNull(MethodBodyInspectionSession.Open(SelfPath).CallerTree(token));
    }

    [Fact]
    public void DirectCalls_MatchesCallerFilter_ForCallingMethod()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var token = CallingToken(index);

        var expected = index.DirectCalls.Where(call => call.Caller.MetadataToken == token).ToList();
        var actual = MethodBodyInspectionSession.Open(SelfPath).DirectCalls(token);

        Assert.NotEmpty(actual);
        Assert.Equal(expected.Count, actual.Length);
        Assert.Equal(
            expected.Select(call => (call.Caller.MetadataToken, call.ILOffset)).OrderBy(x => x.ILOffset),
            actual.Select(call => (call.Caller.MetadataToken, call.ILOffset)).OrderBy(x => x.ILOffset));
    }

    [Fact]
    public void DirectCalls_UnknownToken_ReturnsEmpty()
        => Assert.Empty(MethodBodyInspectionSession.Open(SelfPath).DirectCalls(methodToken: 0));

    [Fact]
    public void UnsafeEvidence_MatchesMemberFilter()
    {
        var index = Analysis.LibraryBodyIndex.Open(SelfPath);
        var grouped = index.GetUnsafeEvidenceByMember();
        if (grouped.Count == 0)
            return; // no unsafe evidence in this assembly; nothing to assert

        var token = grouped.First(kv => kv.Value.Length > 0).Key;
        var expected = index.UnsafeEvidence.Where(e => e.Member.MetadataToken == token).ToList();
        var actual = MethodBodyInspectionSession.Open(SelfPath).UnsafeEvidence(token);

        Assert.NotEmpty(actual);
        Assert.Equal(expected.Count, actual.Length);
    }

    [Fact]
    public void UnsafeEvidence_UnknownToken_ReturnsEmpty()
        => Assert.Empty(MethodBodyInspectionSession.Open(SelfPath).UnsafeEvidence(methodToken: 0));
}
