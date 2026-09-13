using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public sealed class CSharpDeclarationFormatterTests
{
    static readonly Guid ModuleId =
        Guid.Parse("3c3f6298-4f13-4df8-9d5f-77a323f1a4bf");

    [Fact]
    public void MemberSignatureSection_UsesCSharpFormatter()
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
    public void MemberSignatureSection_UsesModelAwareCallerContract()
    {
        ApiMember member = UpdatedMember(
            "Run",
            "method",
            new MemorySafetyMemberContractResult.Explicit(
                Evidence(0x06000001, MemorySafetyPointerEvidence.Absent)));
        ApiType type = UpdatedType(member);
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(
            view,
            type,
            new MemberOptions { OverloadIndex = 1 });

        MemberSignatureRow row = Assert.Single(view.SignatureRows!);
        Assert.Contains("unsafe", row.Signature, StringComparison.Ordinal);
        Assert.Null(row.Unavailable);
    }

    [Fact]
    public void MemberSignatureSection_UsesModelAwareExplicitLayoutField()
    {
        ApiMember member = UpdatedMember(
            "Value",
            "field",
            new MemorySafetyMemberContractResult.None(
                Evidence(0x04000001, MemorySafetyPointerEvidence.Absent)));
        member.DeclarationMetadataToken = 0x04000001;
        member.FieldLayout = new ApiFieldLayoutFacts(
            ModuleId,
            0x02000001,
            0x04000001,
            Offset: 4);
        ApiType type = UpdatedType(member);
        type.Layout = ApiTypeLayout.Explicit;
        type.LayoutDetails = new ApiTypeLayoutFacts(
            ModuleId,
            0x02000001,
            Size: 8,
            PackingSize: 4);
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(
            view,
            type,
            new MemberOptions { OverloadIndex = 1 });

        MemberSignatureRow row = Assert.Single(view.SignatureRows!);
        Assert.Contains("safe", row.Signature, StringComparison.Ordinal);
        Assert.Contains("FieldOffsetAttribute(4)", row.Signature, StringComparison.Ordinal);
        Assert.Null(row.Unavailable);
    }

    [Fact]
    public void MemberSignatureSection_ShowsModelAwareUnavailability()
    {
        ApiMember member = UpdatedMember(
            "Current",
            "property",
            new MemorySafetyMemberContractResult.None(
                Evidence(0x17000001, MemorySafetyPointerEvidence.Absent)));
        member.SignatureModel!.Accessors =
        [
            new ApiAccessor { Kind = "get" },
        ];
        ApiType type = UpdatedType(member);
        var view = new TypeView();

        ApiOutputFormatter.PopulateMemberSignature(
            view,
            type,
            new MemberOptions { OverloadIndex = 1 });

        MemberSignatureRow row = Assert.Single(view.SignatureRows!);
        Assert.Equal("Unavailable", row.Signature);
        Assert.Contains(
            "not supported",
            Assert.IsType<string>(row.Unavailable),
            StringComparison.OrdinalIgnoreCase);
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
    public void MemberSignatureSection_CanRenderParameterAttributesFromStructuredSignature()
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
                    Name = "Validate",
                    Kind = "method",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = "Validate",
                        Parameters =
                        [
                            new ApiParameter
                            {
                                Attributes = ["System.Diagnostics.CodeAnalysis.StringSyntax(\"Regex\")"],
                                Type = "string",
                                Name = "pattern"
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

        Assert.Contains("[System.Diagnostics.CodeAnalysis.StringSyntax(\"Regex\")] string pattern", view.SignatureRows![0].Signature);
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
    public void MemberSignatureSection_CanRenderEventFromStructuredSignature()
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
                    Name = "Changed",
                    Kind = "event",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.EventHandler",
                        MemberName = "Changed"
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

        Assert.Contains("event System.EventHandler", view.SignatureRows![0].Signature);
        Assert.Contains("Changed", view.SignatureRows[0].Signature);
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

    [Fact]
    public void ConstructorOverloadSnippet_UsesStructuredParameterDeclarations()
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
                                Attributes = ["System.Diagnostics.CodeAnalysis.NotNull"],
                                Type = "string",
                                Name = "event"
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

        Assert.Equal("new Widget([System.Diagnostics.CodeAnalysis.NotNull] string @event)", view.ConstructorOverloads![0].Signature.Content);
    }

    [Fact]
    public void ConstructorOverloadSnippet_EscapesKeywordGenericParameterTypes()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Widget",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "event" }],
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
                                Type = "System.Action<event>",
                                Name = "callback"
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

        Assert.Equal("new Widget(System.Action<@event> callback)", view.ConstructorOverloads![0].Signature.Content);
    }

    [Fact]
    public void ConstructorOverloadSnippet_IgnoresObsoleteAttributePrefix()
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
                    IsObsolete = true,
                    ObsoleteMessage = "Use Widget()",
                    Signature = "BROKEN",
                    SignatureModel = new ApiSignature
                    {
                        Parameters =
                        [
                            new ApiParameter
                            {
                                Type = "int",
                                Name = "count"
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

        Assert.Equal("new Widget(int count)", view.ConstructorOverloads![0].Signature.Content);
    }

    static ApiType UpdatedType(ApiMember member)
        => new()
        {
            Namespace = "Samples",
            Name = "UpdatedHost",
            Kind = "class",
            MetadataToken = 0x02000001,
            MemorySafety = new ApiModuleMemorySafetyFacts(
                ModuleId,
                new MemorySafetyRulesResult.Available(
                    MemorySafetyRulesState.Updated,
                    [])),
            Members = [member],
        };

    static ApiMember UpdatedMember(
        string name,
        string kind,
        MemorySafetyMemberContractResult contract)
        => new()
        {
            Name = name,
            Kind = kind,
            IsStatic = kind != "field",
            MetadataToken = kind == "method" ? 0x06000001 : null,
            MethodSemantics = kind == "method"
                ? ApiMethodSemanticsKind.None
                : null,
            ReturnType = "int",
            SignatureModel = new ApiSignature
            {
                MemberName = name,
                ReturnType = "int",
            },
            MemorySafety = new ApiMemberMemorySafetyFacts(
                ModuleId,
                contract,
                MemorySafetyPointerEvidence.Absent),
        };

    static MemorySafetyMemberContractEvidence Evidence(
        int token,
        MemorySafetyPointerEvidence pointer)
        => new(
            token,
            MemorySafetyRulesState.Updated,
            pointer,
            MemorySafetyFixedBufferEvidence.Absent,
            RequiresUnsafeAttributeEvidence.None,
            RequiresUnsafeAttributeEvidence.None,
            AssociatedMemberToken: null);
}
