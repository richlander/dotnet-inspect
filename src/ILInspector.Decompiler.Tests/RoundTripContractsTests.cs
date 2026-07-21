using DotnetInspector.RoundTripCompilation;
using ILInspector.CSharp;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Decompiler.Tests;

public sealed class RoundTripContractsTests
{
    static readonly MetadataMethodAddress Method = new(Guid.NewGuid(), System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1));
    static readonly MemberAnchor Anchor = new("M", "M:T.M()", "abc", "T", "M");

    [Fact]
    public void MethodReplacement_AcceptsBlockBody()
    {
        var replacement = RoundTripMethodReplacement.Create(Method, Anchor, new CSharpBlockBody("{ return; }"));

        Assert.Equal(Method, replacement.Method);
        Assert.Equal("{ return; }", replacement.Body.Source);
    }

    [Theory]
    [MemberData(nameof(UnsupportedBodies))]
    public void MethodReplacement_RejectsNonMethodBodyShapes(CSharpMemberBody body)
    {
        var error = Assert.Throws<ArgumentException>(() => RoundTripMethodReplacement.Create(Method, Anchor, body));

        Assert.Contains(body.GetType().Name, error.Message);
    }

    public static TheoryData<CSharpMemberBody> UnsupportedBodies => new()
    {
        new CSharpFieldInitializer("42"),
        new CSharpPropertyBody(
            new CSharpAccessorBody(CSharpAccessorBodyKind.Auto),
            new CSharpAccessorBody(CSharpAccessorBodyKind.Auto)),
        new CSharpEventBody(
            new CSharpAccessorBody(CSharpAccessorBodyKind.Block, "{ }"),
            new CSharpAccessorBody(CSharpAccessorBodyKind.Block, "{ }")),
    };

    [Fact]
    public void Request_RejectsReplacementOutsideTargetSet()
    {
        var other = Method with { Handle = System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(2) };
        var replacement = RoundTripMethodReplacement.Create(other, Anchor, new CSharpBlockBody("{ }"));

        Assert.Throws<ArgumentException>(() => RoundTripRequest.Create(
            new RoundTripArtifactIdentity("input.dll", "hash", "test"),
            new RoundTripModuleIdentity("input.dll", Method.ModuleVersionId),
            [new RoundTripTarget(Method, Anchor)],
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected,
            [replacement]));
    }
}
