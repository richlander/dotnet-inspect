using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.ILDiff;
using Inspector.Findings;

using static ILInspector.Metadata.Tests.FindingDiffAnyIdentityTests;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Exists agreement gate of <c>docs/design/finding-census.md#gates</c>:
/// Exists on the IL kind per method equals Finding Diff Any's per-method Body
/// verdict, over the Finding Diff Any fixture pair, the identity images, and
/// a real image against itself, so the walk and lockstep decide one equality.
/// </summary>
public sealed class IlExistsAgreementTests
{
    [Fact]
    public void FixturePair_ExistsPerMethodEqualsTheWalksBodyVerdict()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        List<(FindingDiffAny.BodyVerdict Walk, FindingExistsResult Exists)> rows = Agree(before, after);
        Assert.True(rows.Count > 100, $"{rows.Count} method pairs");
        Assert.Contains(rows, row => row.Walk.Body == FindingDiffAnyState.Changed);
        Assert.Contains(rows, row => row.Walk.Body == FindingDiffAnyState.Unchanged);
    }

    [Fact]
    public void RealImageAgainstItself_EveryMethodIsUnchanged()
    {
        using var before = new PEReader(File.OpenRead(typeof(FindingDiffAny).Assembly.Location));
        using var after = new PEReader(File.OpenRead(typeof(FindingDiffAny).Assembly.Location));
        List<(FindingDiffAny.BodyVerdict Walk, FindingExistsResult Exists)> rows = Agree(before, after);
        Assert.True(rows.Count > 1_000, $"{rows.Count} method pairs");
        Assert.All(rows, row =>
        {
            Assert.Equal(FindingExistsDecision.Unchanged, row.Exists.Decision);
            // An unchanged pair reads every atom once on each side.
            Assert.Equal(row.Exists.OldAtoms, row.Exists.NewAtoms);
        });
    }

    public static TheoryData<string, byte[], byte[], FindingExistsDecision> IdentityImages()
    {
        byte[] returnTwo = [0x18, 0x26, 0x2A];
        byte[] callFirst = [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
        byte[] callSecond = [0x28, 0x02, 0x00, 0x00, 0x06, 0x2A];
        byte[] invalid = [0x24, 0x2A];
        byte[] invalidAfterOne = [0x17, 0x24, 0x2A];
        byte[] invalidAfterTwo = [0x18, 0x24, 0x2A];
        byte[] branchBack = [0x00, 0x2B, 0xFD, 0x2A]; // nop; br.s to the nop; ret
        byte[] branchSelf = [0x00, 0x2B, 0xFE, 0x2A]; // nop; br.s to itself; ret
        byte[] branchBackThenInvalid = [0x00, 0x2B, 0xFD, 0x24];
        byte[] branchSelfThenInvalid = [0x00, 0x2B, 0xFE, 0x24];
        TypeSpec compilerControlled = new("N", "C", null, ReturnOne, ["M", "M"], PrivateScope: true);
        TypeSpec callee = new("N", "C", null, ReturnOne);
        // Two rows of one long name: within the Type name budget (namespace, one
        // delimiter per segment, name) they are duplicates both provisions refuse
        // to name; one character over, the walk's unit is malformed instead and
        // neither refuses, so the two methods compare by their shared key.
        TypeSpec longWithin = new("N", new string('L', MetadataSafetyPolicy.MaxTypeNameCharacters - 2), null, ReturnOne);
        TypeSpec longOver = new("N", new string('L', MetadataSafetyPolicy.MaxTypeNameCharacters - 1), null, ReturnOne);
        return new TheoryData<string, byte[], byte[], FindingExistsDecision>
        {
            { "N.D", Image([compilerControlled, new("N", "D", null, callFirst)]), Image([compilerControlled, new("N", "D", null, callSecond)]), FindingExistsDecision.Indeterminate },
            { "N.D", Image([callee, callee, new("N", "D", null, callFirst)]), Image([callee, callee, new("N", "D", null, callSecond)]), FindingExistsDecision.Indeterminate },
            { "N.D", Image([longWithin, longWithin, new("N", "D", null, callFirst)]), Image([longWithin, longWithin, new("N", "D", null, callSecond)]), FindingExistsDecision.Indeterminate },
            { "N.D", Image([longOver, longOver, new("N", "D", null, callFirst)]), Image([longOver, longOver, new("N", "D", null, callSecond)]), FindingExistsDecision.Unchanged },
            { "N.C", Image([new("N", "C", null, invalid)]), Image([new("N", "C", null, invalid)]), FindingExistsDecision.Indeterminate },
            { "N.C", Image([new("N", "C", null, invalidAfterOne)]), Image([new("N", "C", null, invalidAfterTwo)]), FindingExistsDecision.Changed },
            { "N.C", Image([new("N", "C", null, ReturnOne)]), Image([new("N", "C", null, returnTwo)]), FindingExistsDecision.Changed },
            { "N.C", Image([new("N", "C", null, ReturnOne)]), Image([new("N", "C", null, ReturnOne)]), FindingExistsDecision.Unchanged },
            // A retargeted branch is decided once both streams complete; before a fault it is undecided on both provisions.
            { "N.C", Image([new("N", "C", null, branchBack)]), Image([new("N", "C", null, branchSelf)]), FindingExistsDecision.Changed },
            { "N.C", Image([new("N", "C", null, branchBackThenInvalid)]), Image([new("N", "C", null, branchSelfThenInvalid)]), FindingExistsDecision.Indeterminate },
        };
    }

    [Theory]
    [MemberData(nameof(IdentityImages))]
    public void IdentityImages_ExistsAgreesWithTheWalk_AndDecidesAsTheGateStates(
        string type,
        byte[] beforeImage,
        byte[] afterImage,
        FindingExistsDecision expected)
    {
        using var before = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var after = new PEReader(new MemoryStream(afterImage, writable: false));
        List<(FindingDiffAny.BodyVerdict Walk, FindingExistsResult Exists)> rows = Agree(before, after);
        (FindingDiffAny.BodyVerdict walk, FindingExistsResult exists) = Assert.Single(rows, row => row.Walk.Type == type);
        Assert.Equal(expected, exists.Decision);
        Assert.Equal(expected, Decision(walk.Body));
    }

    // Every method pair the walk pairs, decided by the walk and by lockstep over the two sides, agreeing.
    static List<(FindingDiffAny.BodyVerdict Walk, FindingExistsResult Exists)> Agree(PEReader before, PEReader after)
    {
        IlCensusSide oldSide = IlCensusSide.Create(before.GetMetadataReader(), IlBodyDiffPolicy.Body);
        IlCensusSide newSide = IlCensusSide.Create(after.GetMetadataReader(), IlBodyDiffPolicy.Body);
        var rows = new List<(FindingDiffAny.BodyVerdict, FindingExistsResult)>();
        foreach (FindingDiffAny.BodyVerdict verdict in FindingDiffAny.BodyVerdicts(before, after))
        {
            FindingExistsResult exists = IlFindings.Exists(oldSide, before, verdict.Before, newSide, after, verdict.After);
            Assert.True(
                Decision(verdict.Body) == exists.Decision,
                $"{verdict.Type} {verdict.Method}: walk {verdict.Body}, lockstep {exists.Decision} after {exists.OldAtoms}/{exists.NewAtoms} atoms");
            rows.Add((verdict, exists));
        }
        return rows;
    }

    static FindingExistsDecision Decision(FindingDiffAnyState state) => state switch
    {
        FindingDiffAnyState.Unchanged => FindingExistsDecision.Unchanged,
        FindingDiffAnyState.Changed => FindingExistsDecision.Changed,
        FindingDiffAnyState.Indeterminate => FindingExistsDecision.Indeterminate,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A paired method's Body verdict is decided."),
    };
}
