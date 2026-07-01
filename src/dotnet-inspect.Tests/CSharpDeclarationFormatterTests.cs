using DotnetInspector.Options;
using DotnetInspector.Output;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

public sealed class CSharpDeclarationFormatterTests
{
    [Fact]
    public void MemberSignatureSection_UsesSharedCSharpDeclarationWriter()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "KeywordHost",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "class",
                    Kind = "method",
                    Signature = "int class(int object)"
                }
            ]
        };
        var view = ApiOutputFormatter.BuildTypeView(
            type,
            foundIn: "Test.dll",
            packageName: null,
            packageVersion: null,
            apiSource: "local",
            selectedTfm: null,
            new MemberOptions { OverloadIndex = 1 });

        ApiOutputFormatter.PopulateMemberSignature(view, type, new MemberOptions { OverloadIndex = 1 });

        Assert.Contains("public int @class(int @object)", view.SignatureRows![0].Signature);
    }
}
