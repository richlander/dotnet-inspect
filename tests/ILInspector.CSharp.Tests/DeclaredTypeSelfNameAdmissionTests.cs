using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class DeclaredTypeSelfNameAdmissionTests
{
    [Fact]
    public void OrdinaryExactNamesConsumeCSharpTextAdmission()
    {
        AssertAdmitted(["Widget"], [0], [], "Widget");
        AssertAdmitted(["class"], [0], [], "@class");
        AssertAdmitted(["extension`1"], [1], [Parameter("T")], "@extension");
        AssertAdmitted(["\u03A9"], [0], [], "\u03A9");
        AssertAdmitted(["Outer", "Inner`1"], [0, 1], [Parameter("T")], "Inner");

        AssertIdentifierRefusal(
            ["A+B"],
            [0],
            [],
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier);
        AssertIdentifierRefusal(
            ["A\u200C"],
            [0],
            [],
            CSharpTypeDeclarationIdentifierRefusalReason.IdentityNotPreserved);
        AssertIdentifierRefusal(
            ["\U00010400"],
            [0],
            [],
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier);
        AssertIdentifierRefusal(
            ["Widget`0"],
            [0],
            [],
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier);

        AssertArityMismatch(["Outer", "Inner"], [0], []);
        AssertArityMismatch(["Outer", "Inner"], [0, -1], []);
        AssertArityMismatch(["Widget"], [1], [Parameter("T")]);
        AssertArityMismatch(["Widget"], [0], [Parameter("T")]);
        AssertArityMismatch(["Widget`2"], [1], [Parameter("T")]);
        AssertArityMismatch(["Widget`1"], [1], []);
        AssertArityMismatch(["Widget`1"], [1], [Parameter("T"), Parameter("U")]);
        AssertArityMismatch(["Outer", "Inner`1"], [0, 1], []);
        AssertArityMismatch(
            ["Outer", "Inner`1"],
            [0, 1],
            [Parameter("T"), Parameter("U")]);
    }

    static void AssertAdmitted(
        string[] segments,
        int[] introducedCounts,
        TypeParameter[] parameters,
        string expectedIdentifier)
    {
        var admitted =
            Assert.IsType<CSharpDeclaredTypeSelfNameAdmission.Admitted>(
                Admit(segments, introducedCounts, parameters));

        Assert.Equal(segments, admitted.Identity.Segments);
        Assert.Equal(expectedIdentifier, admitted.Identifier);
    }

    static void AssertIdentifierRefusal(
        string[] segments,
        int[] introducedCounts,
        TypeParameter[] parameters,
        CSharpTypeDeclarationIdentifierRefusalReason expectedReason)
    {
        var unrepresentable =
            Assert.IsType<CSharpDeclaredTypeSelfNameAdmission.Unrepresentable>(
                Admit(segments, introducedCounts, parameters));
        var reason =
            Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.IdentifierNotAdmitted>(
                unrepresentable.Failure.Reason);

        Assert.Equal(segments, unrepresentable.Failure.Identity.Segments);
        Assert.Equal(expectedReason, reason.Reason);
    }

    static void AssertArityMismatch(
        string[] segments,
        int[] introducedCounts,
        TypeParameter[] parameters)
    {
        var unrepresentable =
            Assert.IsType<CSharpDeclaredTypeSelfNameAdmission.Unrepresentable>(
                Admit(segments, introducedCounts, parameters));

        Assert.Equal(segments, unrepresentable.Failure.Identity.Segments);
        Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch>(
            unrepresentable.Failure.Reason);
    }

    static CSharpDeclaredTypeSelfNameAdmission Admit(
        string[] segments,
        int[] introducedCounts,
        TypeParameter[] parameters)
        => CSharpDeclaredTypeSelfName.Admit(
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("N", [.. segments])).Name,
            introducedCounts,
            parameters);

    static TypeParameter Parameter(string name) => new() { Name = name };
}
