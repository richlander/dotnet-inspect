using DotnetInspector.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// A property or event has no MethodDef of its own, so source locations are resolved through its
/// accessors. Every accessor must be offered in preference order — a preferred accessor can carry
/// no sequence points while a later one resolves — rather than only the first (issue #3278).
/// </summary>
public class MemberSourceTokenTests
{
    [Fact]
    public void Property_OffersBothAccessors_GetterPreferred()
    {
        var property = new ApiMember
        {
            Name = "Mixed",
            Kind = "property",
            GetterToken = 0x0600_0001,
            SetterToken = 0x0600_0002
        };

        Assert.Equal(
            [(0x0600_0001, 0), (0x0600_0002, 1)],
            MemberSourceLocationCollector.SourceTokens(property).ToArray());
    }

    [Fact]
    public void Property_SetterOnly_IsStillOffered()
    {
        var property = new ApiMember
        {
            Name = "WriteOnly",
            Kind = "property",
            SetterToken = 0x0600_0003
        };

        Assert.Equal(
            [(0x0600_0003, 0)],
            MemberSourceLocationCollector.SourceTokens(property).ToArray());
    }

    [Fact]
    public void Event_OffersBothAccessors_AdderPreferred()
    {
        var member = new ApiMember
        {
            Name = "Changed",
            Kind = "event",
            AdderToken = 0x0600_0004,
            RemoverToken = 0x0600_0005
        };

        Assert.Equal(
            [(0x0600_0004, 0), (0x0600_0005, 1)],
            MemberSourceLocationCollector.SourceTokens(member).ToArray());
    }

    [Fact]
    public void Method_OffersItsOwnToken_Only()
    {
        var method = new ApiMember
        {
            Name = "Compute",
            Kind = "method",
            MetadataToken = 0x0600_0006,
            GetterToken = 0x0600_0007
        };

        Assert.Equal(
            [(0x0600_0006, 0)],
            MemberSourceLocationCollector.SourceTokens(method).ToArray());
    }

    [Fact]
    public void Property_WithNoAccessorTokens_OffersNothing()
    {
        var property = new ApiMember { Name = "Bare", Kind = "property" };

        Assert.Empty(MemberSourceLocationCollector.SourceTokens(property));
    }
}
