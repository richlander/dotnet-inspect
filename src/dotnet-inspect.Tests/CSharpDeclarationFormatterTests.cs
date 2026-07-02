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

    [Fact]
    public void MemberSignatureSection_CanRenderMethodFromStructuredSignature()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "StructuredHost",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "GetValues",
                    Kind = "method",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Collections.Generic.Dictionary<string, System.DateTime>",
                        MemberName = "GetValues",
                        Parameters =
                        [
                            new ApiParameter
                            {
                                Type = "System.Collections.Generic.List<System.Guid>",
                                Name = "ids"
                            }
                        ]
                    }
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

        Assert.Contains("public System.Collections.Generic.Dictionary", view.SignatureRows![0].Signature);
        Assert.Contains("GetValues", view.SignatureRows[0].Signature);
        Assert.Contains("System.Collections.Generic.List", view.SignatureRows[0].Signature);
        Assert.DoesNotContain("BROKEN", view.SignatureRows[0].Signature);
    }

    [Fact]
    public void MemberSignatureSection_CanRenderGenericMethodFromStructuredSignature()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "StructuredHost",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Map",
                    Kind = "method",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "TResult",
                        MemberName = "Map<TSource, TResult>",
                        TypeParameters =
                        [
                            new TypeParameter { Name = "TSource", Constraints = ["unmanaged"] },
                            new TypeParameter { Name = "TResult", Constraints = ["System.IComparable<TResult>", "new()"] }
                        ],
                        Parameters =
                        [
                            new ApiParameter { Type = "TSource", Name = "source" }
                        ]
                    }
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

        Assert.Contains("Map", view.SignatureRows![0].Signature);
        Assert.Contains("TSource", view.SignatureRows[0].Signature);
        Assert.Contains("TResult", view.SignatureRows[0].Signature);
        Assert.Contains("IComparable", view.SignatureRows[0].Signature);
        Assert.DoesNotContain("BROKEN", view.SignatureRows[0].Signature);
    }

    [Fact]
    public void MemberSignatureSection_CanRenderPropertyFromStructuredSignature()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "StructuredHost",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Current",
                    Kind = "property",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Collections.Generic.List<System.Guid>",
                        MemberName = "Current",
                        Accessors =
                        [
                            new ApiAccessor { Kind = "get" },
                            new ApiAccessor { Kind = "set", Accessibility = "private" }
                        ]
                    }
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

        Assert.Contains("public System.Collections.Generic.List", view.SignatureRows![0].Signature);
        Assert.Contains("Current", view.SignatureRows[0].Signature);
        Assert.Contains("private set", view.SignatureRows[0].Signature);
        Assert.DoesNotContain("BROKEN", view.SignatureRows[0].Signature);
    }

    [Fact]
    public void ConstructorOverloadSnippet_UsesStructuredDefaultValueText()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        Parameters =
                        [
                            new ApiParameter
                            {
                                Type = "int",
                                Name = "count",
                                HasDefault = true,
                                DefaultValueText = "42"
                            }
                        ]
                    }
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
            new TypeOptions());

        ApiOutputFormatter.PopulateConstructorOverloads(view, type, new TypeOptions());

        Assert.Equal("new Widget(int count = 42)", view.ConstructorOverloads![0].Signature.Content);
    }
}
