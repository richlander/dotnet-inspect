using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.ILDiff;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The Body provision seam (<c>docs/design/finding-census.md#provisions</c>):
/// a comparison whose paired methods are decided by lockstep over two IL
/// cursors reaches the same Type states, on both axes, as the walk, over the
/// fixture pair, the identity images, and a real image against itself, in the
/// whole and the stepped mode alike.
/// </summary>
public sealed class FindingDiffAnyBodyReaderTests
{
    [Fact]
    public void FixturePair_LockstepReachesTheWalksStates()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        AssertAgreement(File.ReadAllBytes(pair.OldAssemblyPath()), File.ReadAllBytes(pair.NewAssemblyPath()), minimumChanged: 10);
    }

    [Fact]
    public void RealImageAgainstItself_LockstepReachesTheWalksStates()
    {
        byte[] image = File.ReadAllBytes(typeof(FindingDiffAny).Assembly.Location);
        AssertAgreement(image, image, minimumChanged: 0);
    }

    [Theory]
    [MemberData(nameof(IlExistsAgreementTests.IdentityImages), MemberType = typeof(IlExistsAgreementTests))]
    public void IdentityImages_LockstepReachesTheWalksStates(string type, byte[] beforeImage, byte[] afterImage, FindingExistsDecision expected)
    {
        FindingDiffAnyResult lockstep = AssertAgreement(beforeImage, afterImage, minimumChanged: 0);
        FindingDiffAnyState body = Assert.Single(lockstep.Types, state => state.FullName == type).Body;
        Assert.Equal(expected.ToString(), body.ToString());
    }

    static FindingDiffAnyResult AssertAgreement(byte[] beforeImage, byte[] afterImage, int minimumChanged)
    {
        using var before = new PEReader(new MemoryStream(beforeImage, writable: false));
        using var after = new PEReader(new MemoryStream(afterImage, writable: false));
        FindingDiffAnyResult walk = FindingDiffAny.Compare(before, after);
        FindingDiffAnyResult lockstep = FindingDiffAny.Compare(before, after, Lockstep(before, after));
        Assert.Equal(walk.Types, lockstep.Types);
        Assert.Equal(walk.Receipt.BodiesCompared, lockstep.Receipt.BodiesCompared);
        Assert.True(lockstep.Types.Count(state => state.Body == FindingDiffAnyState.Changed) >= minimumChanged);

        var stepped = new FindingDiffAnyComparison(FindingDiffAnyAxes.ApiAndBody, Lockstep(before, after));
        while (!stepped.Step(before, after, TimeSpan.Zero))
        {
        }
        Assert.Equal(walk.Types, stepped.Result!.Types);
        return lockstep;
    }

    static FindingDiffAnyBodyReader Lockstep(PEReader before, PEReader after)
    {
        IlCensusSide beforeSide = IlCensusSide.Create(before.GetMetadataReader(), IlBodyDiffPolicy.Body);
        IlCensusSide afterSide = IlCensusSide.Create(after.GetMetadataReader(), IlBodyDiffPolicy.Body);
        return (beforePe, beforeMethod, afterPe, afterMethod) =>
            IlFindings.Exists(beforeSide, beforePe, beforeMethod, afterSide, afterPe, afterMethod).Decision;
    }
}
