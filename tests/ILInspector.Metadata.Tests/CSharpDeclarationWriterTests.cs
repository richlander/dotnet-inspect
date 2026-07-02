using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class CSharpDeclarationWriterTests
{
    [Fact]
    public void QualifiedMemberDeclaration_KeepsFullyQualifiedTypeNames()
    {
        var type = CreateSampleType();
        var member = type.Members[0];

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal(
            "public System.Collections.Generic.Dictionary<string, System.DateTime> GetValues(System.Collections.Generic.List<System.Guid> ids)",
            declaration);
    }

    [Fact]
    public void ShortWithUsingsMemberUnit_GeneratesImportsAndShortensTypes()
    {
        var type = CreateSampleType();
        var member = type.Members[0];

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;
            using System.Collections.Generic;

            public Dictionary<string, DateTime> GetValues(List<Guid> ids);
            """,
            rendered.Source);
        Assert.Equal(["System", "System.Collections.Generic"], rendered.Usings);
        Assert.Empty(rendered.Diagnostics);
    }

    [Fact]
    public void ContextualShort_UsesCallerSuppliedNamespaceContext()
    {
        var type = CreateSampleType();
        var member = type.Members[0];

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ContextualShort,
                Usings = ["System", "System.Collections.Generic"]
            });

        Assert.Equal("public Dictionary<string, DateTime> GetValues(List<Guid> ids)", declaration);
    }

    [Fact]
    public void ContextualShort_LeavesTypesQualifiedWhenImportsAreMissing()
    {
        var type = CreateSampleType();
        var member = type.Members[0];

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ContextualShort,
                Usings = ["System"]
            });

        Assert.Equal(
            "public System.Collections.Generic.Dictionary<string, DateTime> GetValues(System.Collections.Generic.List<Guid> ids)",
            declaration);
    }

    [Fact]
    public void MethodDeclaration_CanRenderFromStructuredSignatureModel()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "GetValues",
            Kind = "method",
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
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;
            using System.Collections.Generic;

            public Dictionary<string, DateTime> GetValues(List<Guid> ids);
            """,
            rendered.Source);
    }

    [Fact]
    public void GenericMethodDeclaration_CanRenderFromStructuredSignatureModel()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Map",
            Kind = "method",
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
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;

            public TResult Map<TSource, TResult>(TSource source) where TSource : unmanaged where TResult : IComparable<TResult>, new();
            """,
            rendered.Source);
    }

    [Fact]
    public void GenericMethodDeclaration_WithoutGenericParameterFactsKeepsCompatibilitySignature()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Echo",
            Kind = "method",
            Signature = "T Echo<T>(T value)",
            SignatureModel = new ApiSignature
            {
                ReturnType = "T",
                MemberName = "Echo<T>",
                Parameters =
                [
                    new ApiParameter { Type = "T", Name = "value" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public T Echo<T>(T value)", declaration);
    }

    [Fact]
    public void PropertyDeclaration_CanRenderFromStructuredSignatureModel()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Current",
            Kind = "property",
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
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;
            using System.Collections.Generic;

            public List<Guid> Current { get; private set; }
            """,
            rendered.Source);
    }

    [Fact]
    public void RequiredPropertyDeclaration_UsesStructuredRequiredFact()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Name",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "Name",
                IsRequired = true,
                Accessors =
                [
                    new ApiAccessor { Kind = "get" },
                    new ApiAccessor { Kind = "set" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public required string Name { get; set; }", declaration);
    }

    [Fact]
    public void IndexerDeclaration_CanRenderFromStructuredSignatureModel()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Item",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "this[]",
                Parameters =
                [
                    new ApiParameter { Type = "int", Name = "index" }
                ],
                Accessors =
                [
                    new ApiAccessor { Kind = "get" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public string this[int index] { get; }", declaration);
    }

    [Fact]
    public void PropertyDeclaration_EscapesStructuredKeywordMemberName()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "event",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "event",
                Accessors =
                [
                    new ApiAccessor { Kind = "get" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public string @event { get; }", declaration);
    }

    [Fact]
    public void IndexerDeclaration_EscapesStructuredKeywordParameterName()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Item",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "this[]",
                Parameters =
                [
                    new ApiParameter { Type = "int", Name = "event" }
                ],
                Accessors =
                [
                    new ApiAccessor { Kind = "get" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public string this[int @event] { get; }", declaration);
    }

    [Fact]
    public void ExplicitPropertyDeclaration_KeepsCompatibilitySignature()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "ITest.Prop",
            Kind = "property",
            Signature = "string ITest.Prop { get; }",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "ITest.Prop",
                Accessors =
                [
                    new ApiAccessor { Kind = "get" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public string ITest.Prop { get; }", declaration);
    }

    [Fact]
    public void EventDeclaration_CanRenderFromStructuredSignatureModel()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Changed",
            Kind = "event",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Changed"
            }
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;

            public event EventHandler Changed;
            """,
            rendered.Source);
    }

    [Fact]
    public void EventDeclaration_EscapesStructuredKeywordMemberName()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "event",
            Kind = "event",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "event"
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public event System.EventHandler @event", declaration);
    }

    [Fact]
    public void ExplicitEventDeclaration_KeepsCompatibilitySignature()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "ITest.Changed",
            Kind = "event",
            Signature = "System.EventHandler ITest.Changed",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "ITest.Changed"
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public event System.EventHandler ITest.Changed", declaration);
    }

    [Fact]
    public void ShortWithUsings_KeepsCollidingSimpleNamesQualified()
    {
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "CollisionHost",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Choose",
                    Kind = "method",
                    Signature = "Contoso.Models.Widget Choose(Fabrikam.Models.Widget other, System.DateTime when)"
                }
            ]
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            type.Members[0],
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;

            public Contoso.Models.Widget Choose(Fabrikam.Models.Widget other, DateTime when);
            """,
            rendered.Source);
        Assert.Contains("Type name 'Widget' is ambiguous", rendered.Diagnostics[0]);
    }

    [Fact]
    public void ShortWithUsings_ShortensEnumDefaultTypePrefix()
    {
        var type = new ApiType
        {
            Namespace = "Sample",
            Name = "EnumDefaultHost",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Pick",
                    Kind = "method",
                    Signature = "DotnetInspector.Tests.SampleColor Pick(DotnetInspector.Tests.SampleColor color = DotnetInspector.Tests.SampleColor.Green)"
                }
            ]
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            type.Members[0],
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using DotnetInspector.Tests;

            public SampleColor Pick(SampleColor color = SampleColor.Green);
            """,
            rendered.Source);
    }

    [Fact]
    public void TypeUnit_ComposesNamespaceTypeAndMemberDeclarations()
    {
        var type = CreateSampleType();

        var rendered = CSharpDeclarationWriter.RenderTypeUnit(
            type,
            type.Members,
            new CSharpDeclarationOptions
            {
                ContainingNamespace = type.Namespace,
                NamespaceMode = CSharpNamespaceMode.FileScoped,
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings
            });

        Assert.Equal(
            """
            using System;
            using System.Collections.Generic;

            namespace Samples;

            public class Values
            {
                public Dictionary<string, DateTime> GetValues(List<Guid> ids);
            }
            """,
            rendered.Source);
    }

    [Fact]
    public void DeclarationOptions_InsertAsyncUnsafeAndCanSuppressObsoleteAttribute()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            Signature = "System.Threading.Tasks.Task Run()",
            IsStatic = true,
            IsObsolete = true,
            ObsoleteMessage = "Use RunAsync."
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            new CSharpDeclarationOptions
            {
                ForceAsync = true,
                ForceUnsafe = true,
                IncludeObsoleteAttribute = false
            });

        Assert.Equal("public static unsafe async System.Threading.Tasks.Task Run()", declaration);
    }

    [Fact]
    public void StaticConstructorDeclaration_OmitsAccessibility()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = ".cctor",
            Kind = "constructor",
            Signature = "void .cctor()",
            IsStatic = true
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("static Worker()", declaration);
    }

    [Fact]
    public void ConstructorDeclaration_CanRenderFromStructuredSignatureModel()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var member = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature
            {
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "System.Collections.Generic.List<System.Guid>",
                        Name = "items"
                    }
                ]
            }
        };

        var rendered = CSharpDeclarationWriter.RenderMemberUnit(
            type,
            member,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ShortWithUsings,
                TerminateMemberDeclaration = true
            });

        Assert.Equal(
            """
            using System;
            using System.Collections.Generic;

            public Widget(List<Guid> items);
            """,
            rendered.Source);
    }

    [Fact]
    public void ConstructorDeclaration_WithDefaultParameterUsesStructuredDefaultText()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var member = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
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
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public Widget(int count = 42)", declaration);
    }

    [Fact]
    public void ConstructorDeclaration_WithUnmodeledDefaultKeepsCompatibilitySignature()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var member = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Signature = "void .ctor([System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(42L)] System.DateTime when)",
            SignatureModel = new ApiSignature
            {
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "System.DateTime",
                        Name = "when",
                        HasDefault = true
                    }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Contains("DateTimeConstant(42L)", declaration);
    }

    [Fact]
    public void AbbreviatedMemberDeclaration_PreservesParameterModifiers()
    {
        var type = new ApiType { Namespace = "Samples", Name = "RefKinds", Kind = "class" };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "void M(ref int value, out string text, in long source, params byte[] bytes)"
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type,
            member,
            new CSharpDeclarationOptions { AbbreviateSignature = true });

        Assert.Equal("public void M(ref int, out string, in long, params byte[])", declaration);
    }

    [Fact]
    public void ExplicitInterfaceImplementation_WithUnsafeSignature_RetainsUnsafeModifier()
    {
        var type = new ApiType { Namespace = "Samples", Name = "UnsafeImpl", Kind = "class" };
        var member = new ApiMember
        {
            Name = "IFoo.Bar",
            Kind = "explicit-interface-implementation",
            Signature = "void IFoo.Bar(int* p)",
            IsUnsafe = true
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("unsafe void IFoo.Bar(int* p)", declaration);
    }

    [Fact]
    public void ExplicitInterfaceImplementation_EscapesDottedKeywordSegments()
    {
        var type = new ApiType { Namespace = "Samples", Name = "KeywordImpl", Kind = "class" };
        var member = new ApiMember
        {
            Name = "event.class",
            Kind = "explicit-interface-implementation",
            Signature = "void event.class()"
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("void @event.@class()", declaration);
    }

    [Fact]
    public void TypeDeclaration_EscapesKeywordTypeParametersInInterfaces()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "KeywordGeneric`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "object" }],
            Interfaces = ["System.Collections.Generic.IEnumerable<object>"]
        };

        var declaration = CSharpDeclarationWriter.RenderTypeDeclaration(type);

        Assert.Equal("public class KeywordGeneric<@object> : System.Collections.Generic.IEnumerable<@object>", declaration);
    }

    static ApiType CreateSampleType()
        => new()
        {
            Namespace = "Samples",
            Name = "Values",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "GetValues",
                    Kind = "method",
                    Signature = "System.Collections.Generic.Dictionary<string, System.DateTime> GetValues(System.Collections.Generic.List<System.Guid> ids)"
                }
            ]
        };
}
