using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpDeclarationWriterTests
{
    [Fact]
    public void TypeDeclaration_PreservesRecordModifiers()
    {
        var abstractType = new ApiType
        {
            Namespace = "Samples",
            Name = "Shape",
            Kind = "record",
            IsAbstract = true,
        };
        var sealedType = new ApiType
        {
            Namespace = "Samples",
            Name = "ClosedShape",
            Kind = "record",
            IsSealed = true
        };

        var abstractDeclaration = CSharpDeclarationWriter.RenderTypeDeclaration(abstractType);
        var sealedDeclaration = CSharpDeclarationWriter.RenderTypeDeclaration(sealedType);

        Assert.Equal("public abstract record Shape", abstractDeclaration);
        Assert.Equal("public sealed record ClosedShape", sealedDeclaration);
    }

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
    public void FinalizerMember_RendersDestructorSyntaxWithoutModifiers()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Handle", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            // Roslyn emits the finalizer with an explicit .override MethodImpl, so
            // it surfaces as an explicit-interface-implementation kind; IsFinalizer
            // is the metadata fact that drives the ~Type() spelling.
            Kind = "explicit-interface-implementation",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, finalizer);

        Assert.Equal("~Handle()", declaration);
    }

    [Fact]
    public void UnsafeFinalizerMember_KeepsUnsafeModifierOnly()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Handle", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "explicit-interface-implementation",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
            IsUnsafe = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, finalizer);

        Assert.Equal("unsafe ~Handle()", declaration);
    }

    [Fact]
    public void FinalizerMember_OnGenericType_DropsTypeArity()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Box`1", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "explicit-interface-implementation",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, finalizer);

        Assert.Equal("~Box()", declaration);
    }

    [Fact]
    public void FinalizerMember_OnTypeNestedInGeneric_UsesInnermostSegment()
    {
        // Regression guard (adversarial review): the destructor spelling must
        // isolate the innermost nested-type segment before stripping generic
        // arity. A type nested in a generic outer carries a dotted metadata name
        // like "Outer`1.Nested"; stripping the backtick first yields "~Outer()".
        var type = new ApiType { Namespace = "Samples", Name = "Outer`1.Nested", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "finalizer",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, finalizer);

        Assert.Equal("~Nested()", declaration);
    }

    [Fact]
    public void ConstructorMember_OnTypeNestedInGeneric_UsesInnermostSegment()
    {
        // Same root cause as the finalizer case: FormatConstructorTypeName must
        // isolate the innermost segment so a constructor on a type nested in a
        // generic outer spells the nested type, not the outer.
        var type = new ApiType { Namespace = "Samples", Name = "Outer`1.Nested", Kind = "class" };
        var ctor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Signature = "void .ctor()",
            Accessibility = "public",
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, ctor);

        Assert.Equal("public Nested()", declaration);
    }

    [Fact]
    public void FinalizerMember_WithSuppressFinalizerSpelling_KeepsLiteralFinalize()
    {
        // Issue #3157 (fidelity hardening): the '~Type()' spelling assumes the
        // recompiled destructor re-emits the mandatory 'base.Finalize()'. When the
        // decompiled body did NOT recover the canonical destructor scaffold, the
        // full-body path suppresses the destructor spelling so recompiling keeps
        // the observed body instead of silently re-injecting the base call.
        var type = new ApiType { Namespace = "Samples", Name = "Handle", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "explicit-interface-implementation",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type, finalizer, new CSharpDeclarationOptions { SuppressFinalizerSpelling = true });

        Assert.Equal("void Finalize()", declaration);
        Assert.DoesNotContain("~Handle", declaration);
    }

    [Fact]
    public void FinalizerMember_NeverGetsAsyncModifier_EvenWhenForced()
    {
        // Defensive guard (adversarial review of #3168): a finalizer must never
        // acquire the 'async' modifier. Even under ForceAsync and an async-eligible
        // Kind (explicit-interface-implementation), 'async ~Handle()' is not legal
        // C#. The '!member.IsFinalizer' clause on the async gate locks this shut
        // independently of the Kind classification, so a future Kind change cannot
        // re-open it.
        var type = new ApiType { Namespace = "Samples", Name = "Handle", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "explicit-interface-implementation",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type, finalizer, new CSharpDeclarationOptions { ForceAsync = true });

        Assert.Equal("~Handle()", declaration);
        Assert.DoesNotContain("async", declaration);
    }

    [Fact]
    public void FinalizerMember_WithFinalizerKindAndSuppression_StaysModifierFree()
    {
        // Regression guard (adversarial review): with the dedicated
        // Kind = "finalizer" (#3186), a suppressed finalizer must NOT fall through
        // to the normal modifier path and pick up 'public virtual', which would
        // render 'public virtual void Finalize()' — a new virtual slot (CS0465)
        // instead of the object-finalizer override. The suppressed fallback stays
        // modifier-free, matching the explicit-interface-kind case above.
        var type = new ApiType { Namespace = "Samples", Name = "Handle", Kind = "class" };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "finalizer",
            Signature = "void Finalize()",
            IsVirtual = true,
            IsFinalizer = true,
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
            type, finalizer, new CSharpDeclarationOptions { SuppressFinalizerSpelling = true });

        Assert.Equal("void Finalize()", declaration);
        Assert.DoesNotContain("public", declaration);
        Assert.DoesNotContain("virtual", declaration);
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
    public void MethodDeclaration_CanRenderStructuredParameterAttributes()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Validate",
            Kind = "method",
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
            using System.Diagnostics.CodeAnalysis;

            public void Validate([StringSyntax("Regex")] string pattern);
            """,
            rendered.Source);
    }

    [Fact]
    public void MethodDeclaration_DoesNotShortenTypeNamesInsideParameterAttributeStringLiterals()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Validate",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = "Validate",
                Parameters =
                [
                    new ApiParameter
                    {
                        Attributes = ["System.Diagnostics.CodeAnalysis.StringSyntax(\"System.String\")"],
                        Type = "string",
                        Name = "pattern"
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
            using System.Diagnostics.CodeAnalysis;

            public void Validate([StringSyntax("System.String")] string pattern);
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
    public void IndexerDeclaration_CanRenderStructuredParameterAttributes()
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
                    new ApiParameter
                    {
                        Attributes = ["System.Diagnostics.CodeAnalysis.StringSyntax(\"Uri\")"],
                        Type = "string",
                        Name = "key"
                    }
                ],
                Accessors =
                [
                    new ApiAccessor { Kind = "get" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("public string this[[System.Diagnostics.CodeAnalysis.StringSyntax(\"Uri\")] string key] { get; }", declaration);
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
    public void ExplicitEventDeclaration_RendersFromStructuredAccessorShape()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Some.@event.IEvents.Changed",
            Kind = "explicit-interface-implementation",
            IsStatic = true,
            IsUnsafe = true,
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Some.event.IEvents.Changed",
                Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal(
            "static unsafe event System.EventHandler Some.@event.IEvents.Changed",
            declaration);
    }

    [Fact]
    public void ExplicitEventDeclaration_RequiresTypedAccessorShape()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Values", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Some.IEvents.Changed",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Some.IEvents.Changed"
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.DoesNotContain("Some.IEvents.Changed", declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("event ", declaration, StringComparison.Ordinal);
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
    public void ConstructorDeclaration_WithUnmodeledDefaultAndUnrelatedAttributeKeepsCompatibilitySignature()
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
                        Attributes = ["System.ComponentModel.Description(\"Test\")"],
                        HasDefault = true
                    }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Contains("DateTimeConstant(42L)", declaration);
    }

    [Fact]
    public void ConstructorDeclaration_WithRepresentedMetadataDefaultPreservesAdditionalAttributes()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Widget", Kind = "class" };
        var member = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Signature = "compatibility signature must not be used",
            SignatureModel = new ApiSignature
            {
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "System.DateTime",
                        Name = "when",
                        Attributes =
                        [
                            "System.Runtime.InteropServices.Optional",
                            "System.Runtime.CompilerServices.DateTimeConstant(42L)",
                            "Marker"
                        ],
                        HasDefault = true
                    }
                ]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal(
            "public Widget([System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(42L), Marker] System.DateTime when)",
            declaration);
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
    public void ExplicitInterfaceProperty_Static_RetainsStaticModifier()
    {
        // #2875: static-abstract interface members implemented explicitly must keep `static`
        // while still omitting the access modifier.
        var type = new ApiType { Namespace = "Samples", Name = "Counter", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Samples.ICounter.Count",
            Kind = "explicit-interface-implementation",
            IsStatic = true,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Samples.ICounter.Count",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };

        var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(type, member);

        Assert.Equal("static int Samples.ICounter.Count { get; }", declaration);
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

    [Theory]
    // A C# tuple type is parenthesized, so a parameter-list scan that takes the first
    // '(' mistakes a tuple-typed return for a parameter list and escapes each element's
    // trailing token. Unnamed elements end in the type keyword, so `(int, string)`
    // became `(@int, @string)` — an identifier that does not bind (CS0246). Named
    // elements hid the bug, because their trailing token is the element name.
    [InlineData("(int, string) Pair(int a)", "public (int, string) Pair(int a)")]
    [InlineData("(int Sum, int Product) Pair(int a)", "public (int Sum, int Product) Pair(int a)")]
    [InlineData(
        "(int, int, int, int, int, int, int, int) Rest(int a)",
        "public (int, int, int, int, int, int, int, int) Rest(int a)")]
    [InlineData("((int, int), int) Nested(int a)", "public ((int, int), int) Nested(int a)")]
    [InlineData(
        "T Echo<T>(T a) where T : System.IComparable<(int, int)>",
        "public T Echo<T>(T a) where T : System.IComparable<(int, int)>")]
    // Parameter escaping itself must keep working, including when the keyword-named
    // parameter is itself tuple-typed and when the member is an operator.
    [InlineData("void M(int event)", "public void M(int @event)")]
    [InlineData("void M((int, int) event)", "public void M((int, int) @event)")]
    [InlineData("Samples.Op operator +(Samples.Op class, Samples.Op b)", "public Samples.Op operator +(Samples.Op @class, Samples.Op b)")]
    public void MemberDeclaration_EscapesParameterNamesWithoutManglingTupleTypes(string signature, string expected)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Tuples", Kind = "class" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        Assert.Equal(expected, CSharpDeclarationWriter.RenderMemberDeclaration(type, member));
    }

    /// <summary>
    /// Pins how <c>EscapeParameterLists</c> resolves a <c>(</c> preceded by whitespace,
    /// which is the one genuinely ambiguous position.
    ///
    /// A tuple return follows the modifier/return-type run, so the token before it is a
    /// C# keyword (<c>public static (int, int) Pair(…)</c>). A parameter list follows the
    /// member name (<c>void M (int a)</c>), which is never a bare keyword — a
    /// keyword-named member is escaped to <c>@name</c> before this runs.
    ///
    /// Deciding from the previous character alone is not sufficient in either direction:
    /// rejecting all whitespace drops the escape on <c>void M (int event)</c>, while
    /// accepting any preceding identifier character mangles <c>static (int, int)</c>,
    /// whose previous non-whitespace character is the <c>c</c> of <c>static</c> (#3489).
    /// </summary>
    [Fact]
    public void MemberDeclaration_ClassifiesParenGroupsByTrailingContext()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Tuples", Kind = "class" };

        // Ends the declaration -> parameter list, so the keyword parameter escapes.
        Assert.Equal(
            "public void M (int @event)",
            CSharpDeclarationWriter.RenderMemberDeclaration(
                type, new ApiMember { Name = "M", Kind = "method", Signature = "void M (int event)" }));

        // Followed by the member name -> tuple return, left alone, while the real
        // parameter list is still escaped. Both halves must hold at once.
        Assert.Equal(
            "public static (int, int) Pair(int @event)",
            CSharpDeclarationWriter.RenderMemberDeclaration(
                type, new ApiMember { Name = "Pair", Kind = "method", Signature = "static (int, int) Pair(int event)" }));

        // A generic member's '>' precedes the space, and an escaped member name is not a
        // keyword: neither is decidable from the preceding token, both are from trailing
        // context.
        Assert.Equal(
            "public void M<T> (T @event)",
            CSharpDeclarationWriter.RenderMemberDeclaration(
                type, new ApiMember { Name = "M", Kind = "method", Signature = "void M<T> (T event)" }));

        Assert.Equal(
            "public void @event (int @class)",
            CSharpDeclarationWriter.RenderMemberDeclaration(
                type, new ApiMember { Name = "event", Kind = "method", Signature = "void event (int class)" }));

        // A modifier run that is not itself a reserved keyword must not turn the tuple
        // return into a parameter list.
        Assert.Equal(
            "public partial (int, int) M(int @event)",
            CSharpDeclarationWriter.RenderMemberDeclaration(
                type, new ApiMember { Name = "M", Kind = "method", Signature = "partial (int, int) M(int event)" }));
    }

    [Theory]
    // A conversion operator returning a tuple: the tuple's paren comes first, so a
    // first-paren scan bails out and leaves the raw op_Implicit spelling.
    [InlineData("op_Implicit", "(int a, int b) op_Implicit(Samples.Tuples value)",
        "public static implicit operator (int a, int b)(Samples.Tuples value)")]
    [InlineData("op_Explicit", "(int, int) op_Explicit(Samples.Tuples value)",
        "public static explicit operator (int, int)(Samples.Tuples value)")]
    // Non-tuple returns must keep rendering exactly as before.
    [InlineData("op_Implicit", "int op_Implicit(Samples.Tuples value)",
        "public static implicit operator int(Samples.Tuples value)")]
    [InlineData("op_Addition", "Samples.Tuples op_Addition(Samples.Tuples left, Samples.Tuples right)",
        "public static Samples.Tuples operator +(Samples.Tuples left, Samples.Tuples right)")]
    // A parameter or return type may itself be named op_*: the member occurrence is the
    // one followed by the parameter list, not the last textual one.
    [InlineData("op_Implicit", "Samples.Converter op_Implicit(op_Implicit value)",
        "public static implicit operator Samples.Converter(op_Implicit value)")]
    [InlineData("op_Implicit", "op_Implicit op_Implicit(Samples.Tuples value)",
        "public static implicit operator op_Implicit(Samples.Tuples value)")]
    [InlineData("op_Addition", "op_Addition op_Addition(op_Addition left, op_Addition right)",
        "public static op_Addition operator +(op_Addition left, op_Addition right)")]
    public void MemberDeclaration_FormatsOperatorsWithTupleReturns(string name, string signature, string expected)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Tuples", Kind = "class" };
        var member = new ApiMember { Name = name, Kind = "method", Signature = signature, IsStatic = true };

        Assert.Equal(expected, CSharpDeclarationWriter.RenderMemberDeclaration(type, member));
    }

    [Theory]
    // MAI-Code round-3 finding: a ')' inside a string default terminated the parameter
    // list early, so the trailing-context rule saw leftover text and declined to escape.
    [InlineData("M", "void M(int event = \")\")", "public void M(int @event = \")\")")]
    [InlineData("M", "void M(int event = \"(\")", "public void M(int @event = \"(\")")]
    [InlineData("M", "void M(char c = ')', int event = 0)", "public void M(char c = ')', int @event = 0)")]
    // A ',' inside a string default must not split one parameter into two.
    [InlineData("M", "void M(string s = \",\", int event = 0)", "public void M(string s = \",\", int @event = 0)")]
    // An escaped quote inside the literal must not end it early.
    [InlineData("M", "void M(string s = \"\\\")\", int event = 0)", "public void M(string s = \"\\\")\", int @event = 0)")]
    // A tuple return must still be left alone when a literal is present.
    [InlineData("Pair", "(int, int) Pair(string s = \")\", int event = 0)",
        "public (int, int) Pair(string s = \")\", int @event = 0)")]
    public void MemberDeclaration_TreatsPunctuationInsideLiteralsAsText(
        string name, string signature, string expected)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Tuples", Kind = "class" };

        Assert.Equal(
            expected,
            CSharpDeclarationWriter.RenderMemberDeclaration(
                type, new ApiMember { Name = name, Kind = "method", Signature = signature }));
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

    [Fact]
    public void TypeDeclaration_EscapesKeywordConstraintTypeNamesButNotConstraintKeywords()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "KeywordConstraint`1",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "T", Constraints = ["class", "TestNS.class", "new()"] },
            ],
        };

        var declaration = CSharpDeclarationWriter.RenderTypeDeclaration(type);

        Assert.Equal("public class KeywordConstraint<T> where T : class, TestNS.@class, new()", declaration);
    }

    [Fact]
    public void TypeDeclaration_StructuredConstraintsDisambiguateKeywordFromTypeNamedLikeKeyword()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "GlobalKeywordType`1",
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter
                {
                    Name = "T",
                    Constraints = ["struct", "struct"],
                    StructuredConstraints =
                    [
                        new TypeParameterConstraint("struct", IsTypeName: false),
                        new TypeParameterConstraint("struct", IsTypeName: true),
                    ],
                },
            ],
        };

        var declaration = CSharpDeclarationWriter.RenderTypeDeclaration(type);

        Assert.Equal("public class GlobalKeywordType<T> where T : struct, @struct", declaration);
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
