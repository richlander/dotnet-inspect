using System.Collections.Generic;
using System.Linq;

public class CandidateLookupTests
{
    [Fact]
    public void FindNearestByTokenOffset_ReturnsNearestPrecedingAllocationInSameModule()
    {
        var before = Candidate(id: 1, methodToken: 0x06000001, ilOffset: 0x0010);
        var nearest = Candidate(id: 2, methodToken: 0x06000001, ilOffset: 0x0020);
        var after = Candidate(id: 3, methodToken: 0x06000001, ilOffset: 0x0030);
        var otherModule = Candidate(id: 4, methodToken: 0x06000001, ilOffset: 0x0028, libraryPath: "/tmp/Other.dll");
        var lookup = CandidateLookup.Create([before, nearest, after, otherModule]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000001,
            0x0025,
            "/tmp/Fixture.dll",
            "Fixture",
            "Fixture.Type::M");

        var match = Assert.Single(matches);
        Assert.Same(nearest, match);
    }

    [Fact]
    public void FindNearestByTokenOffset_ReturnsAllRowsAtNearestOffsetForAmbiguityGating()
    {
        var first = Candidate(id: 1, methodToken: 0x06000002, ilOffset: 0x0010, kind: "Closure");
        var second = Candidate(id: 2, methodToken: 0x06000002, ilOffset: 0x0010, kind: "Delegate");
        var lookup = CandidateLookup.Create([first, second]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000002,
            0x0015,
            "/tmp/Fixture.dll",
            "Fixture",
            "Fixture.Type::M");

        Assert.Equal([first.Id, second.Id], matches.Select(static candidate => candidate.Id).Order());
    }

    [Fact]
    public void FindNearestByTokenOffset_FallsBackToMethodNameWhenModuleIsUnavailable()
    {
        var target = Candidate(id: 1, methodToken: 0x06000003, ilOffset: 0x0040);
        var unrelated = Candidate(id: 2, methodToken: 0x06000003, ilOffset: 0x0050, method: "Other.Type.M()");
        var lookup = CandidateLookup.Create([target, unrelated]);

        var matches = lookup.FindNearestByTokenOffset(
            0x06000003,
            0x0060,
            modulePath: null,
            moduleName: null,
            methodName: "Fixture.Type::M");

        var match = Assert.Single(matches);
        Assert.Same(target, match);
    }

    static AllocationCandidate Candidate(
        int id,
        int methodToken,
        int ilOffset,
        string libraryPath = "/tmp/Fixture.dll",
        string method = "Fixture.Type.M()",
        string kind = "Object")
    {
        string methodKey = method[..method.IndexOf('(')];
        int lastDot = methodKey.LastIndexOf('.');
        string stackKey = lastDot < 0 ? methodKey : $"{methodKey[..lastDot]}::{methodKey[(lastDot + 1)..]}";
        return new(
            id,
            "library",
            libraryPath,
            "Fixture",
            null,
            methodToken,
            ilOffset,
            method,
            methodKey,
            stackKey,
            kind,
            "Fixture.Allocated",
            null,
            false,
            "Always",
            "Escapes",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "Capture");
    }
}
