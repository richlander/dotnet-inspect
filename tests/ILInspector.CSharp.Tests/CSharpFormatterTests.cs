using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpFormatterTests
{
    [Fact]
    public void FormatsStructuredMemberDeclaration()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Container`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }]
        };
        var member = new ApiMember
        {
            Name = "Map",
            Kind = "method",
            Signature = "compatibility text",
            SignatureModel = new ApiSignature
            {
                ReturnType = "TResult",
                MemberName = "Map<TResult>",
                TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                Parameters = [new ApiParameter { Type = "T", Name = "value" }]
            }
        };

        var declaration = new CSharpFormatter().FormatMember(type, member);

        Assert.Equal(
            "public TResult Map<TResult>(T value) where TResult : class",
            declaration);
    }

    [Fact]
    public void PositiveDeclaredArityWithoutParameters_DoesNotAliasPlainType()
    {
        var exact = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Foo`1"])).Name;
        var malformed = new ApiType
        {
            Namespace = "N",
            Name = "Foo`1",
            Kind = "class",
            DefinitionName = exact,
            IntroducedTypeParameterCounts = [0],
        };

        string malformedName = CSharpFormatter.FormatTypeName(malformed);

        Assert.Equal("Foo`1", malformedName);
        Assert.NotEqual(
            CSharpFormatter.FormatTypeName(
                new ApiType
                {
                    Namespace = "N",
                    Name = "Foo",
                    Kind = "class",
                }),
            malformedName);
        Assert.Equal(
            "Foo`1",
            CSharpFormatter.FormatDeclarationLeafMetadataName(malformed));
        Assert.Equal(
            "Foo",
            CSharpFormatter.FormatDeclarationLeafMetadataName(
                new ApiType
                {
                    Namespace = "N",
                    Name = "Foo`1",
                    Kind = "class",
                    DefinitionName = exact,
                    IntroducedTypeParameterCounts = [1],
                }));
    }

    [Fact]
    public void NestedPerSegmentArityMismatch_DoesNotAliasValidType()
    {
        var exact = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ["Outer`1", "Inner`1"])).Name;
        var malformed = new ApiType
        {
            Namespace = "N",
            Name = "Outer`1.Inner`1",
            Kind = "class",
            DefinitionName = exact,
            IntroducedTypeParameterCounts = [2, 0],
            TypeParameters =
            [
                new TypeParameter { Name = "A" },
                new TypeParameter { Name = "B" },
            ],
        };

        Assert.Equal(
            "Outer`1.Inner`1",
            CSharpFormatter.FormatTypeName(malformed));
    }

    [Fact]
    public void ZeroParameterOuterArityMismatch_DoesNotAliasPlainType()
    {
        var exact = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "N",
                ["Outer`1", "Inner"])).Name;
        var malformed = new ApiType
        {
            Namespace = "N",
            Name = "Outer`1.Inner",
            Kind = "class",
            DefinitionName = exact,
            IntroducedTypeParameterCounts = [0, 0],
        };

        Assert.Equal(
            "Outer`1.Inner",
            CSharpFormatter.FormatTypeName(malformed));
    }

    [Fact]
    public void MissingDeclaredArity_UsesExactParameterOwnership()
    {
        MetadataTypeDefinitionName exact =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "N",
                    ["Outer", "Inner`1"]))
            .Name;
        var full = new ApiType
        {
            Namespace = "N",
            Name = "Outer.Inner`1",
            Kind = "class",
            DefinitionName = exact,
            IntroducedTypeParameterCounts = [1, 1],
            TypeParameters =
            [
                new TypeParameter { Name = "T" },
                new TypeParameter { Name = "U" },
            ],
        };
        var leaf = new ApiType
        {
            Namespace = "N",
            Name = "Inner`1",
            Kind = "class",
            DefinitionName = exact,
            IntroducedTypeParameterCounts = [1, 1],
            TypeParameters =
            [
                new TypeParameter { Name = "U" },
            ],
        };

        Assert.Equal(
            "Outer<T>.Inner<U>",
            CSharpFormatter.FormatTypeName(full));
        Assert.Equal(
            "Inner<U>",
            CSharpFormatter.FormatTypeName(leaf));
    }

    [Fact]
    public void FormatsContextualTypeUnit()
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
                    Name = "Create",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Samples.Widget",
                        MemberName = "Create"
                    }
                }
            ]
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = "Samples",
            TerminateMemberDeclaration = true
        });

        var declaration = formatter.FormatTypeUnit(type, type.Members);

        Assert.Equal(
            """
            public class Widget
            {
                public Widget Create();
            }
            """,
            declaration.Text);
        Assert.Empty(declaration.Usings);
        Assert.Empty(declaration.Diagnostics);
    }

    [Fact]
    public void FormatsPrimaryConstructorParametersInTypeUnit()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class"
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings
        });

        var declaration = formatter.FormatTypeUnit(
            type,
            members: null,
            primaryConstructorParameters:
            [
                new ApiParameter
                {
                    Type = "System.String",
                    Name = "message",
                    Attributes =
                    [
                        "Attributes.Other.Marker(typeof(External.Value))"
                    ]
                }
            ]);

        Assert.Contains(
            "public class Worker([Marker(typeof(External.Value))] String message)",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.Contains("Attributes.Other", declaration.Usings);
    }

    [Fact]
    public void PrimaryConstructorAttributeSuffixShadowKeepsQualifiedName()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class"
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            AdditionalRootShadowingNames = ["MarkerAttribute"]
        });

        var declaration = formatter.FormatTypeUnit(
            type,
            members: null,
            primaryConstructorParameters:
            [
                new ApiParameter
                {
                    Type = "int",
                    Name = "value",
                    Attributes = ["External.Marker"]
                }
            ]);

        Assert.Contains(
            "public class Worker([External.Marker] int value)",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("External", declaration.Usings);
    }

    [Theory]
    [InlineData(CSharpTypeNamePolicy.Qualified, "public System.Threading.Tasks.Task Run()", false)]
    [InlineData(CSharpTypeNamePolicy.ShortWithUsings, "public Task Run()", true)]
    [InlineData(CSharpTypeNamePolicy.ContextualShort, "public Task Run()", false)]
    public void TypeNamePolicyAppliesToIndividualMemberDeclarations(
        CSharpTypeNamePolicy policy,
        string expectedDeclaration,
        bool expectsGeneratedUsing)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Threading.Tasks.Task",
                MemberName = "Run"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = policy,
            Usings = policy == CSharpTypeNamePolicy.ContextualShort
                ? ["System.Threading.Tasks"]
                : []
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(expectedDeclaration, declaration.Text, StringComparison.Ordinal);
        Assert.Equal(expectsGeneratedUsing, declaration.Usings.Contains("System.Threading.Tasks"));
    }

    [Fact]
    public void FormatsBareMemberTypesWithNamespaceSet()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "FieldWriter",
            Kind = "class"
        };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature
            {
                Parameters =
                [
                    new ApiParameter { Type = "System.IO.TextWriter", Name = "writer" },
                    new ApiParameter { Type = "Markout.Formatting.IFieldFormatter", Name = "formatter" },
                    new ApiParameter
                    {
                        Type = "Markout.MarkoutWriterOptions?",
                        Name = "options",
                        HasDefault = true,
                        DefaultValueText = "null"
                    }
                ]
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings
        });

        var declaration = formatter.FormatMemberUnit(type, constructor);

        Assert.Equal(
            """
            using Markout;
            using Markout.Formatting;
            using System.IO;

            public FieldWriter(TextWriter writer, IFieldFormatter formatter, MarkoutWriterOptions? options = null)
            """,
            declaration.Text);
        Assert.Equal(
            ["Markout", "Markout.Formatting", "System.IO"],
            declaration.Usings);
        Assert.DoesNotContain(
            declaration.Usings,
            value => value.StartsWith("using ", StringComparison.Ordinal));
    }

    [Fact]
    public void ShortWithUsingsDerivesAlongsideCallerImports()
    {
        var type = new ApiType { Namespace = "Samples", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "CreateTimer",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Windows.Forms.Timer",
                MemberName = "CreateTimer"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            Usings = ["System.Threading"]
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public Timer CreateTimer()",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.Equal(["System.Windows.Forms"], declaration.Usings);
    }

    [Fact]
    public void UnqualifiedTypePreventsCollidingImport()
    {
        var type = new ApiType { Namespace = "App", Name = "Client", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Get",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Lib.Node",
                MemberName = "Get",
                Parameters = [new ApiParameter { Type = "Node", Name = "ambient" }]
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public Lib.Node Get(Node ambient)",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.Empty(declaration.Usings);
    }

    [Theory]
    [InlineData(CSharpTypeNamePolicy.ShortWithUsings)]
    [InlineData(CSharpTypeNamePolicy.ContextualShort)]
    public void DeclaredTypeNameKeepsCrossNamespaceReferenceQualified(
        CSharpTypeNamePolicy policy)
    {
        var type = new ApiType { Namespace = "Samples", Name = "Task", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Threading.Tasks.Task",
                MemberName = "Run"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = policy,
            Usings = policy == CSharpTypeNamePolicy.ContextualShort
                ? ["System.Threading.Tasks"]
                : []
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public System.Threading.Tasks.Task Run()",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.Empty(declaration.Usings);
    }

    [Fact]
    public void NamespaceSegmentKeepsSameNamedReferenceQualified()
    {
        var type = new ApiType { Namespace = "Samples.Models", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Get",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "External.Models",
                MemberName = "Get"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            ContainingNamespace = type.Namespace
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public External.Models Get()",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.Empty(declaration.Usings);
    }

    [Fact]
    public void KnownNamespaceRootKeepsSameNamedReferenceQualified()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "GetWidget",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Alpha.Beta.Widget",
                        MemberName = "GetWidget"
                    }
                },
                new ApiMember
                {
                    Name = "GetAlpha",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Zeta.Alpha",
                        MemberName = "GetAlpha"
                    }
                }
            ]
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            ContainingNamespace = type.Namespace
        });

        var declaration = formatter.FormatTypeUnit(type, type.Members);

        Assert.Contains("public Widget GetWidget();", declaration.Text, StringComparison.Ordinal);
        Assert.Contains("public Zeta.Alpha GetAlpha();", declaration.Text, StringComparison.Ordinal);
        Assert.Equal(["Alpha.Beta"], declaration.Usings);
    }

    [Fact]
    public void EnclosingNamespaceChildKeepsSameNamedReferenceQualified()
    {
        var type = new ApiType
        {
            Namespace = "Alpha.Beta",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "GetThing",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Alpha.Gamma.Thing",
                        MemberName = "GetThing"
                    }
                },
                new ApiMember
                {
                    Name = "GetGamma",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Other.Gamma",
                        MemberName = "GetGamma"
                    }
                }
            ]
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            ContainingNamespace = type.Namespace
        });

        var declaration = formatter.FormatTypeUnit(type, type.Members);

        Assert.Contains("public Thing GetThing();", declaration.Text, StringComparison.Ordinal);
        Assert.Contains("public Other.Gamma GetGamma();", declaration.Text, StringComparison.Ordinal);
        Assert.Equal(["Alpha.Gamma"], declaration.Usings);
    }

    [Fact]
    public void UnrelatedNamespaceChildDoesNotShadowSameNamedReference()
    {
        var type = new ApiType
        {
            Namespace = "Alpha.Beta",
            Name = "Worker",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "GetThing",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Zeta.Delta.Thing",
                        MemberName = "GetThing"
                    }
                },
                new ApiMember
                {
                    Name = "GetDelta",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "Other.Delta",
                        MemberName = "GetDelta"
                    }
                }
            ]
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            ContainingNamespace = type.Namespace
        });

        var declaration = formatter.FormatTypeUnit(type, type.Members);

        Assert.Contains("public Thing GetThing();", declaration.Text, StringComparison.Ordinal);
        Assert.Contains("public Delta GetDelta();", declaration.Text, StringComparison.Ordinal);
        Assert.Equal(
            ["Other", "Zeta.Delta"],
            declaration.Usings.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ContainingNamespaceChildShadowedRootUsesGlobalAlias()
    {
        var type = new ApiType { Namespace = "Alpha.System", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "GetUri",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Uri",
                MemberName = "GetUri"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
            ContainingNamespace = type.Namespace
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public global::System.Uri GetUri()",
            declaration.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContainingNamespaceRootDoesNotRequireGlobalAlias()
    {
        var type = new ApiType { Namespace = "System.Example", Name = "Worker", Kind = "class" };
        var member = new ApiMember
        {
            Name = "GetUri",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Uri",
                MemberName = "GetUri"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
            ContainingNamespace = type.Namespace
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains("public System.Uri GetUri()", declaration.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("global::System.Uri", declaration.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyUsesGlobalAliasWhenNamespaceRootIsShadowed()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "System" }]
        };
        var member = new ApiMember
        {
            Name = "Run",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Threading.Tasks.Task",
                MemberName = "Run"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.Qualified
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public global::System.Threading.Tasks.Task Run()",
            declaration.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyEscapesShadowedKeywordNamespaceRoot()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "event" }]
        };
        var member = new ApiMember
        {
            Name = "GetWidget",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "event.Models.Widget",
                MemberName = "GetWidget"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.Qualified
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public global::@event.Models.Widget GetWidget()",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("@global::", declaration.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyEscapesKeywordRootInsideExistingGlobalAlias()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class"
        };
        var member = new ApiMember
        {
            Name = "GetWidget",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "global::event.Models.Widget",
                MemberName = "GetWidget"
            }
        };

        var declaration = new CSharpFormatter().FormatMemberUnit(type, member);

        Assert.Contains(
            "public global::@event.Models.Widget GetWidget()",
            declaration.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("global::global::", declaration.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpTypeNamePolicy.Qualified, "public @event.Models.Widget GetWidget()", false)]
    [InlineData(CSharpTypeNamePolicy.ShortWithUsings, "public Widget GetWidget()", true)]
    [InlineData(CSharpTypeNamePolicy.ContextualShort, "public Widget GetWidget()", false)]
    public void KeywordNamespaceRootMatchesTypeNamePlan(
        CSharpTypeNamePolicy policy,
        string expectedDeclaration,
        bool importNamespace)
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class"
        };
        var member = new ApiMember
        {
            Name = "GetWidget",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "event.Models.Widget",
                MemberName = "GetWidget"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = policy,
            Usings = policy == CSharpTypeNamePolicy.ContextualShort
                ? ["event.Models"]
                : []
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(expectedDeclaration, declaration.Text, StringComparison.Ordinal);
        if (importNamespace)
            Assert.Contains("using @event.Models;", declaration.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyEscapesKeywordTypeWithShadowedRoot()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "Alpha" }]
        };
        var member = new ApiMember
        {
            Name = "GetEvent",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.event",
                MemberName = "GetEvent"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.Qualified
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains(
            "public global::Alpha.@event GetEvent()",
            declaration.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ShortPolicyEscapesKeywordType()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Worker",
            Kind = "class"
        };
        var member = new ApiMember
        {
            Name = "GetEvent",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.event",
                MemberName = "GetEvent"
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings
        });

        var declaration = formatter.FormatMemberUnit(type, member);

        Assert.Contains("Alpha", declaration.Usings);
        Assert.Contains("public @event GetEvent()", declaration.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatsParameterListsWithAttributesDefaultsAndEscapedKeywords()
    {
        var parameters = new ApiParameter[]
        {
            new()
            {
                Attributes = ["System.Runtime.InteropServices.Optional"],
                Type = "System.event.MyClass",
                Name = "event",
                HasDefault = true,
                DefaultValueText = "default"
            },
            new() { Type = "class", Name = "value" },
            new() { Type = "System.Collections.Generic.List<class>", Name = "items" },
            new() { Type = "delegate", Name = "delegateValue" },
            new() { Type = "readonly", Name = "readonlyValue" },
            new() { Type = "scoped", Name = "scopedValue" },
            new() { Type = "delegate*<ref int, void>", Name = "callback" }
        };

        Assert.Equal(
            "([System.Runtime.InteropServices.Optional] System.@event.MyClass @event = default, @class value, System.Collections.Generic.List<@class> items, @delegate delegateValue, @readonly readonlyValue, @scoped scopedValue, delegate*<ref int, void> callback)",
            CSharpFormatter.FormatParameterList(parameters));
    }

    [Fact]
    public void CanOmitParameterAndReturnAttributesFromMemberUnits()
    {
        var type = new ApiType { Name = "Widget", Kind = "class" };
        var member = new ApiMember
        {
            Name = "Create",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                MemberName = "Create",
                ReturnType = "Widget",
                ReturnAttributes = ["System.Diagnostics.CodeAnalysis.NotNull"],
                Parameters =
                [
                    new ApiParameter
                    {
                        Attributes = ["System.Runtime.InteropServices.Optional"],
                        Type = "int",
                        Name = "value"
                    }
                ]
            }
        };

        var withAttributes = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
        }).FormatMemberUnit(type, member);
        var withoutAttributes = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
            IncludeSignatureAttributes = false,
        }).FormatMemberUnit(type, member);

        Assert.Contains("[return: System.Diagnostics.CodeAnalysis.NotNull]", withAttributes.Text);
        Assert.Contains("[Optional]", withAttributes.Text);
        Assert.DoesNotContain("CodeAnalysis.NotNull", withoutAttributes.Text);
        Assert.DoesNotContain("Optional", withoutAttributes.Text);
        Assert.Contains("System.Runtime.InteropServices", withAttributes.Usings);
        Assert.DoesNotContain("System.Runtime.InteropServices", withoutAttributes.Usings);
    }

    [Fact]
    public void CanOmitSignatureAttributesFromDelegates()
    {
        var type = new ApiType
        {
            Name = "Callback",
            Kind = "delegate",
            Accessibility = "public"
        };
        var invoke = new ApiMember
        {
            Name = "Invoke",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.Widget",
                ReturnAttributes = ["Gamma.Result"],
                Parameters =
                [
                    new ApiParameter
                    {
                        Attributes = ["Beta.Widget"],
                        Type = "Alpha.Widget",
                        Name = "value"
                    }
                ]
            }
        };

        var declaration = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            Usings = ["Alpha"],
            IncludeSignatureAttributes = false
        }).FormatDelegate(type, invoke);

        Assert.Equal("public delegate Widget Callback(Widget value);", declaration);
    }

    [Fact]
    public void DelegateAttributeSuffixCollisionRemainsQualified()
    {
        var type = new ApiType
        {
            Name = "Callback",
            Kind = "delegate",
            Accessibility = "public"
        };
        var invoke = new ApiMember
        {
            Name = "Invoke",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Other.MarkerAttribute",
                Parameters =
                [
                    new ApiParameter
                    {
                        Attributes = ["External.Marker"],
                        Type = "int",
                        Name = "value"
                    }
                ]
            }
        };

        var declaration = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            Usings = ["External", "Other"]
        }).FormatDelegate(type, invoke);

        Assert.Equal(
            "public delegate MarkerAttribute Callback([External.Marker] int value);",
            declaration);
    }

    [Fact]
    public void SignatureSuppressionDeclinesUnstructuredCompatibilityText()
    {
        var type = new ApiType { Name = "Widget", Kind = "class" };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Signature =
                "void .ctor([External.Marker(typeof(Gamma.Widget))] Alpha.Widget value)"
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            IncludeSignatureAttributes = false
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => formatter.FormatMember(type, constructor));

        Assert.Contains(
            "signature attributes cannot be suppressed safely",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureSuppressionAllowsAttributeFreeModeledCompatibilityText()
    {
        var type = new ApiType { Name = "Widget", Kind = "class" };
        var method = new ApiMember
        {
            Name = "Map",
            Kind = "method",
            Signature = "void Map(T value)",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = "Map",
                TypeParameters = [new TypeParameter { Name = "T" }],
                Parameters = [new ApiParameter { Type = "T", Name = "value" }]
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            IncludeSignatureAttributes = false
        });

        var declaration = formatter.FormatMember(type, method, ["T"]);

        Assert.Equal("public void Map<T>(T value)", declaration);
    }

    [Fact]
    public void SignatureSuppressionDeclinesStructuredMetadataOnlyDefault()
    {
        var type = new ApiType { Name = "Widget", Kind = "class" };
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Signature =
                "void .ctor([System.Runtime.InteropServices.Optional, "
                + "System.Runtime.CompilerServices.DateTimeConstant(42L)] "
                + "System.DateTime when)",
            SignatureModel = new ApiSignature
            {
                MemberName = ".ctor",
                ReturnType = "void",
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "System.DateTime",
                        Name = "when",
                        HasDefault = true,
                        Attributes =
                        [
                            "System.Runtime.InteropServices.Optional",
                            "System.Runtime.CompilerServices.DateTimeConstant(42L)"
                        ]
                    }
                ]
            }
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            IncludeSignatureAttributes = false
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => formatter.FormatMember(type, constructor));

        Assert.Contains(
            "signature attributes cannot be suppressed safely",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OmittedPrimaryConstructorAttributesDoNotAffectTypeNames()
    {
        var type = new ApiType
        {
            Name = "Container",
            Kind = "class",
            Accessibility = "public"
        };
        var parameter = new ApiParameter
        {
            Attributes = ["Beta.Widget"],
            Type = "Alpha.Widget",
            Name = "value"
        };

        var declaration = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            Usings = ["Alpha"],
            IncludeSignatureAttributes = false
        }).FormatTypeDeclaration(type, [parameter]);

        Assert.Equal("public class Container(Widget value)", declaration);
    }

    [Theory]
    [InlineData("await")]
    [InlineData("file")]
    [InlineData("init")]
    [InlineData("record")]
    [InlineData("required")]
    [InlineData("scoped")]
    public void EscapesConservativeContextualKeywordSet(string identifier)
        => Assert.Equal($"@{identifier}", CSharpFormatter.EscapeIdentifier(identifier));

    [Fact]
    public void FormatsDelegateWithStructuredAccessibility()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Callback",
            Kind = "delegate",
            Accessibility = "private",
            Members =
            [
                new ApiMember
                {
                    Name = "Invoke",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "void",
                        MemberName = "Invoke"
                    }
                }
            ]
        };

        Assert.Equal(
            "private delegate void Callback();",
            new CSharpFormatter().FormatDelegate(type, type.Members.Single()));
    }

    [Fact]
    public void FormatsDelegateWithContainedHostileName()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Call\nback",
            Kind = "delegate",
            Accessibility = "private"
        };
        var invoke = new ApiMember
        {
            Name = "Invoke",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = "Invoke"
            }
        };

        Assert.Equal(
            "private delegate void Call_back();",
            new CSharpFormatter().FormatDelegate(type, invoke));
    }

    [Fact]
    public void FormatsNestedTypeNameWithoutFoldingItsNestingSeparator()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Outer.Inner",
            Kind = "class"
        };

        Assert.Equal(
            "public class Outer.Inner",
            new CSharpFormatter().FormatTypeDeclaration(type));
    }

    [Fact]
    public void KnownIdentifierEscapingIsIdempotent()
    {
        Assert.Equal(
            "System.Action<@event>",
            CSharpFormatter.EscapeKnownIdentifiers("System.Action<@event>", ["event"]));
        Assert.Equal(
            "System.Action<@event>",
            CSharpFormatter.EscapeKnownIdentifiers("System.Action<event>", ["event"]));
    }

    [Fact]
    public void FormatTypeParameterConstraints_UsesStructuredKindToDisambiguateKeywordFromTypeName()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["struct", "struct"],
            StructuredConstraints =
            [
                new TypeParameterConstraint("struct", IsTypeName: false),
                new TypeParameterConstraint("struct", IsTypeName: true),
            ],
        };

        Assert.Equal(
            "struct, @struct",
            CSharpFormatter.FormatTypeParameterConstraints(typeParameter, ["T"]));
    }

    [Fact]
    public void FormatTypeParameterConstraints_FallsBackToHeuristicWithoutStructuredKind()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["class", "TestNS.class"],
        };

        Assert.Equal(
            "class, TestNS.@class",
            CSharpFormatter.FormatTypeParameterConstraints(typeParameter, ["T"]));
    }

    [Fact]
    public void RejectsUndefinedPolicies()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CSharpFormatter(new CSharpFormatOptions
            {
                TypeNamePolicy = (CSharpTypeNamePolicy)42
            }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CSharpFormatter(new CSharpFormatOptions
            {
                NamespacePolicy = (CSharpNamespacePolicy)42
            }));
    }

    [Theory]
    // Primitive/void aliases stay bare when they name the primitive.
    [InlineData("int", "int")]
    [InlineData("string", "string")]
    [InlineData("void", "void")]
    [InlineData("int[]", "int[]")]
    [InlineData("int*", "int*")]
    [InlineData("System.Int32", "System.Int32")]
    [InlineData("System.Collections.Generic.List<int>", "System.Collections.Generic.List<int>")]
    // A type literally named after a primitive keyword (a dotted-name segment) is escaped.
    [InlineData("N.int", "N.@int")]
    [InlineData("int.MaxValue", "int.MaxValue")]
    // Reserved keywords used as identifiers are escaped, including inside generic args.
    [InlineData("class", "@class")]
    [InlineData("await", "@await")]
    [InlineData("record", "@record")]
    [InlineData("List<await>", "List<@await>")]
    [InlineData("MyType<class>", "MyType<@class>")]
    [InlineData("Foo.await", "Foo.@await")]
    [InlineData("await.Foo", "@await.Foo")]
    [InlineData("N.readonly", "N.@readonly")]
    // Parameter/type modifiers stay bare in a leading modifier run.
    [InlineData("ref int", "ref int")]
    [InlineData("ref readonly int", "ref readonly int")]
    [InlineData("scoped ref int", "scoped ref int")]
    [InlineData("in long", "in long")]
    [InlineData("out string", "out string")]
    [InlineData("params byte[]", "params byte[]")]
    // Function-pointer syntax stays bare, and reserved args inside are still escaped.
    [InlineData("delegate*<int, void>", "delegate*<int, void>")]
    [InlineData("delegate* unmanaged<int>", "delegate* unmanaged<int>")]
    [InlineData("delegate*<ref int, void>", "delegate*<ref int, void>")]
    [InlineData("delegate*<await, void>", "delegate*<@await, void>")]
    // Pointers to types literally named like a keyword must be escaped, not read as
    // type syntax: "ref*"/"in*" are pointers to a type named ref/in, and a bare
    // "delegate*" (not a function-pointer head) is a pointer to a type named delegate.
    [InlineData("ref*", "@ref*")]
    [InlineData("in*", "@in*")]
    [InlineData("readonly*", "@readonly*")]
    [InlineData("delegate*", "@delegate*")]
    [InlineData("delegate*[]", "@delegate*[]")]
    // Whitespace before terminating punctuation is not a modifier/calling-convention
    // boundary: the keyword names a type and must be escaped.
    [InlineData("ref *", "@ref *")]
    [InlineData("Tuple<readonly >", "Tuple<@readonly >")]
    [InlineData("(delegate* , int)", "(@delegate* , int)")]
    [InlineData("Tuple<delegate* >", "Tuple<@delegate* >")]
    [InlineData("delegate* managed<int, void>", "delegate* managed<int, void>")]
    // Whitespace between "delegate*" and '<' is still a function-pointer head.
    [InlineData("delegate* <int, void>", "delegate* <int, void>")]
    // A qualified "delegate" segment is a type name, never a function-pointer head.
    [InlineData("N.delegate*<int, void>", "N.@delegate*<int, void>")]
    [InlineData("N.delegate", "N.@delegate")]
    // Already-escaped identifiers are left untouched (idempotent).
    [InlineData("@int", "@int")]
    [InlineData("N.@int", "N.@int")]
    public void EscapeTypeKeywords_EscapesIdentifiersButNotTypeSyntax(string input, string expected)
        => Assert.Equal(expected, CSharpFormatter.EscapeTypeKeywords(input));

    [Theory]
    // Every CLR primitive full name aliases to its C# keyword, including the native
    // ints (nint/nuint) and decimal, matching the product decompiler's spelling.
    [InlineData("System.Boolean", "bool")]
    [InlineData("System.Byte", "byte")]
    [InlineData("System.SByte", "sbyte")]
    [InlineData("System.Char", "char")]
    [InlineData("System.Decimal", "decimal")]
    [InlineData("System.Double", "double")]
    [InlineData("System.Single", "float")]
    [InlineData("System.Int16", "short")]
    [InlineData("System.UInt16", "ushort")]
    [InlineData("System.Int32", "int")]
    [InlineData("System.UInt32", "uint")]
    [InlineData("System.Int64", "long")]
    [InlineData("System.UInt64", "ulong")]
    [InlineData("System.IntPtr", "nint")]
    [InlineData("System.UIntPtr", "nuint")]
    [InlineData("System.Object", "object")]
    [InlineData("System.String", "string")]
    [InlineData("System.Void", "void")]
    // Primitives nested in generics, arrays, pointers, and by-ref forms are aliased.
    [InlineData("System.Collections.Generic.List<System.Int32>", "System.Collections.Generic.List<int>")]
    [InlineData("System.Collections.Generic.Dictionary<System.String,System.Boolean>", "System.Collections.Generic.Dictionary<string,bool>")]
    [InlineData("System.Int32[]", "int[]")]
    [InlineData("System.Int32[,]", "int[,]")]
    [InlineData("System.Int32&", "int&")]
    [InlineData("System.Int32*", "int*")]
    [InlineData("System.Nullable<System.Int32>[]", "System.Nullable<int>[]")]
    // A longer name that merely contains a primitive as a substring is left alone.
    [InlineData("System.Int32Enum", "System.Int32Enum")]
    [InlineData("A.System.Int32", "A.System.Int32")]
    [InlineData("System.Int32.MaxValue", "System.Int32.MaxValue")]
    [InlineData("System.Collections.Generic.List<System.Guid>", "System.Collections.Generic.List<System.Guid>")]
    // An explicitly-escaped identifier (leading '@') is not a primitive reference.
    [InlineData("@System.Int32", "@System.Int32")]
    [InlineData("List<@System.Int32>", "List<@System.Int32>")]
    // Non-System text and already-keyword spellings pass through unchanged.
    [InlineData("int", "int")]
    [InlineData("MyNamespace.MyType", "MyNamespace.MyType")]
    [InlineData("", "")]
    public void AliasPrimitiveTypeNames_RewritesClrPrimitivesToKeywords(string input, string expected)
        => Assert.Equal(expected, CSharpFormatter.AliasPrimitiveTypeNames(input));

    [Theory]
    [InlineData("this(x)", CSharpConstructorInitializerKind.This, "x")]
    [InlineData("base(a, b)", CSharpConstructorInitializerKind.Base, "a, b")]
    // A leading ": " (the emitted form) is accepted and stripped.
    [InlineData(": this(1)", CSharpConstructorInitializerKind.This, "1")]
    [InlineData(": base()", CSharpConstructorInitializerKind.Base, null)]
    // Nested calls are carried verbatim as a single argument (no top-level split).
    [InlineData("base(Wrap(a, b), c)", CSharpConstructorInitializerKind.Base, "Wrap(a, b), c")]
    public void ParseConstructorInitializer_ParsesThisAndBaseChains(
        string chain,
        CSharpConstructorInitializerKind expectedKind,
        string? expectedArgument)
    {
        var initializer = CSharpFormatter.ParseConstructorInitializer(chain);
        Assert.NotNull(initializer);
        Assert.Equal(expectedKind, initializer!.Kind);
        Assert.Equal(
            expectedArgument is null ? [] : new[] { expectedArgument },
            initializer.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomeMethod(x)")]
    [InlineData("this")]
    public void ParseConstructorInitializer_ReturnsNullForNonChains(string? chain)
        => Assert.Null(CSharpFormatter.ParseConstructorInitializer(chain));

    [Fact]
    public void ParseConstructorInitializer_RoundTripsFormatConstructorInitializer()
    {
        var initializer = new CSharpConstructorInitializer(
            CSharpConstructorInitializerKind.This,
            ["a, b"]);
        string formatted = CSharpFormatter.FormatConstructorInitializer(initializer);
        var parsed = CSharpFormatter.ParseConstructorInitializer(formatted);
        Assert.NotNull(parsed);
        Assert.Equal(initializer.Kind, parsed!.Kind);
        Assert.Equal(initializer.Arguments, parsed.Arguments);
    }

    [Theory]
    [InlineData("int*", true)]
    [InlineData("delegate*<int, void>", true)]
    [InlineData("stackalloc int[4]", true)]
    [InlineData("int", false)]
    [InlineData("System.Collections.Generic.List<int>", false)]
    [InlineData("a + b", false)]
    public void RequiresUnsafeModifier_DetectsPointerAndStackalloc(string csharp, bool expected)
        => Assert.Equal(expected, CSharpFormatter.RequiresUnsafeModifier(csharp));

    [Theory]
    [InlineData("int*", true)]
    [InlineData("delegate*<int, void>", true)]
    [InlineData("System.Int32*", true)]
    [InlineData("int[*]*", true)]
    [InlineData("delegate*<int[*], void>", true)]
    [InlineData("int", false)]
    [InlineData("int[*]", false)]
    [InlineData("int[][*]", false)]
    [InlineData("ref int[*]", false)]
    [InlineData("(int[*], int[])", false)]
    [InlineData("System.Collections.Generic.List<int[*]>", false)]
    [InlineData("System.Collections.Generic.List<int>", false)]
    [InlineData("stackalloc", false)]
    [InlineData("@stackalloc", false)]
    [InlineData("N.stackalloc", false)]
    public void TypeRequiresUnsafeModifier_MatchesPointersButNotStackallocIdentifiers(string typeDisplayName, bool expected)
        => Assert.Equal(expected, CSharpFormatter.TypeRequiresUnsafeModifier(typeDisplayName));

    [Theory]
    [InlineData("List`1", "List")]
    [InlineData("Dictionary`2", "Dictionary")]
    [InlineData("Widget", "Widget")]
    [InlineData("", "")]
    // #4217: only the canonical `N is an arity suffix. A literal backtick suffix
    // keeps its identity instead of collapsing onto the unsuffixed name, and each
    // nested segment is stripped independently rather than truncating the rest.
    [InlineData("Widget`Literal", "Widget`Literal")]
    [InlineData("Widget`1Extra", "Widget`1Extra")]
    [InlineData("Widget`0", "Widget`0")]
    [InlineData("Widget`01", "Widget`01")]
    [InlineData("Widget`99999999999", "Widget`99999999999")]
    [InlineData("Outer`1.Inner`2", "Outer.Inner")]
    [InlineData("Outer`Literal.Inner`1", "Outer`Literal.Inner")]
    public void StripArity_RemovesOnlyCanonicalGenericAritySuffixes(string name, string expected)
        => Assert.Equal(expected, CSharpFormatter.StripArity(name));

    /// <summary>
    /// The identity collision that motivated #4217: two distinct metadata names
    /// must not produce the same C# spelling candidate.
    /// </summary>
    [Fact]
    public void StripArity_DoesNotCollapseDistinctMetadataNames()
        => Assert.NotEqual(
            CSharpFormatter.StripArity("Widget"),
            CSharpFormatter.StripArity("Widget`Literal"));

    /// <summary>
    /// The input is an <c>ApiType.Name</c>-shaped chain: nesting is spelled
    /// <c>.</c>, a <c>+</c> is name text, and a namespace is never included. A
    /// namespace passed in here would have its text rewritten, which is why
    /// callers keep it beside the chain.
    /// </summary>
    [Theory]
    [InlineData("Weird+Name`1", "Weird+Name")]
    [InlineData("Weird`1+Name", "Weird`1+Name")]
    [InlineData("Outer`1.Inner`2", "Outer.Inner")]
    public void StripArity_ParsesTheDottedTypeNameChainOnly(string name, string expected)
        => Assert.Equal(expected, CSharpFormatter.StripArity(name));

    [Fact]
    public void FormatTypeName_DoesNotInventStructureForLiteralDots()
    {
        var legacy = new ApiType { Name = "A`1.B" };
        var exactName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "",
                ImmutableArray.Create("A`1.B"))).Name;
        var exact = new ApiType
        {
            Name = "A`1.B",
            DefinitionName = exactName
        };

        Assert.Equal("A`1.B", CSharpFormatter.FormatTypeName(legacy));
        Assert.Equal(@"A`1\.B", CSharpFormatter.FormatTypeName(exact));
        Assert.NotEqual(
            CSharpFormatter.FormatTypeName(legacy),
            CSharpFormatter.FormatTypeName(new ApiType { Name = "A.B" }));
    }

    [Fact]
    public void FormatTypeName_DistributesExactNestedGenericParameters()
    {
        MetadataTypeDefinitionName exactName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "",
                    ["Outer`1", "Inner`1"]))
                .Name;
        var type = new ApiType
        {
            Name = "Outer`1.Inner`1",
            DefinitionName = exactName,
            TypeParameters =
            [
                new TypeParameter { Name = "T" },
                new TypeParameter { Name = "U" },
            ],
        };

        Assert.Equal(
            "Outer<T>.Inner<U>",
            CSharpFormatter.FormatTypeName(type));
    }

    [Fact]
    public void FormatTypeName_UsesOwnedLeafForNestedTypeShell()
    {
        MetadataTypeDefinitionName exactName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "",
                    ["Outer`1", "Inner`1"]))
                .Name;
        var type = new ApiType
        {
            Name = "Inner`1",
            DefinitionName = exactName,
            IntroducedTypeParameterCounts = [1, 1],
            TypeParameters =
            [
                new TypeParameter { Name = "U" },
            ],
        };

        Assert.Equal(
            "Inner<U>",
            CSharpFormatter.FormatTypeName(type));
    }

    [Fact]
    public void FormatTypeName_UsesMetadataLeafForNormalizedGeneratedShell()
    {
        const string leaf = "<State>d__1";
        MetadataTypeDefinitionName exactName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "",
                    ["Outer", leaf]))
                .Name;
        var type = new ApiType
        {
            Name =
                CSharpFormatter.NormalizeGeneratedMetadataTypeName(leaf),
            MetadataName = leaf,
            DefinitionName = exactName,
            IntroducedTypeParameterCounts = [0, 0],
        };

        Assert.Equal(
            CSharpFormatter.NormalizeGeneratedMetadataTypeName(leaf),
            CSharpFormatter.FormatTypeName(type));
        Assert.Equal(
            CSharpFormatter.NormalizeGeneratedMetadataTypeName(leaf),
            CSharpFormatter.FormatDeclarationLeafMetadataName(type));

        const string malformedLeaf = "<State>d__1`2";
        var malformed = new ApiType
        {
            Name =
                CSharpFormatter.NormalizeGeneratedMetadataTypeName(
                    malformedLeaf),
            MetadataName = malformedLeaf,
            DefinitionName = Assert
                .IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "",
                        ["Outer", malformedLeaf]))
                .Name,
            IntroducedTypeParameterCounts = [0, 1],
        };
        Assert.Equal(
            malformedLeaf,
            CSharpFormatter.FormatDeclarationLeafMetadataName(
                malformed));
        Assert.Equal(
            malformedLeaf,
            CSharpFormatter.FormatTypeName(malformed));

        const string delegateLeaf = "<>A{00000000}`2";
        var generatedDelegate = new ApiType
        {
            Name =
                CSharpFormatter.NormalizeGeneratedMetadataTypeName(
                    delegateLeaf),
            MetadataName = delegateLeaf,
            DefinitionName = Assert
                .IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "",
                        [delegateLeaf]))
                .Name,
            IntroducedTypeParameterCounts = [2],
            TypeParameters =
            [
                new TypeParameter { Name = "T1" },
                new TypeParameter { Name = "T2" },
            ],
        };
        Assert.Equal(
            "___A_00000000_<T1, T2>",
            CSharpFormatter.FormatTypeName(generatedDelegate));
    }

    [Fact]
    public void FormatTypeName_DistinguishesLiteralDotFromNesting()
    {
        static ApiType Exact(params string[] segments)
            => new()
            {
                Name = string.Join('.', segments),
                DefinitionName =
                    Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                        MetadataTypeDefinitionName.Create(
                            "",
                            [.. segments]))
                    .Name,
            };

        Assert.Equal(
            @"A\.B",
            CSharpFormatter.FormatTypeName(Exact("A.B")));
        Assert.Equal(
            "A.B",
            CSharpFormatter.FormatTypeName(Exact("A", "B")));
    }

    /// <summary>
    /// Delegate rendering used to truncate the name at the first backtick, which
    /// dropped every following nested component and spelled a distinct type
    /// (#4217). The declaring chain and a non-arity backtick both survive.
    /// </summary>
    [Fact]
    public void FormatDelegate_KeepsNestedComponentsAndNonArityBackticks()
    {
        static ApiMember Invoke() => new()
        {
            Name = "Invoke",
            Kind = "method",
            SignatureModel = new ApiSignature { ReturnType = "void", Parameters = [] }
        };

        var nested = new ApiType
        {
            Name = "Outer`1.Callback",
            DefinitionName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "",
                    ImmutableArray.Create("Outer`1", "Callback"))).Name,
            Kind = "delegate",
            Accessibility = "public"
        };
        var literal = new ApiType
        {
            Name = "Callback`Literal",
            Kind = "delegate",
            Accessibility = "public"
        };
        var formatter = new CSharpFormatter(new CSharpFormatOptions());

        Assert.Equal(
            "public delegate void Outer.Callback();",
            formatter.FormatDelegate(nested, Invoke()));
        Assert.Contains(
            "Callback`Literal",
            formatter.FormatDelegate(literal, Invoke()),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.Int32", "int")]
    [InlineData("A.B.delegate*", "A.B.@delegate*")]
    [InlineData("ref readonly", "ref @readonly")]
    public void CleanTypeDisplay_NormalizesToCSharpSpelling(string type, string expected)
        => Assert.Equal(expected, CSharpFormatter.CleanTypeDisplay(type));

    [Theory]
    [InlineData("int modreq(System.Runtime.CompilerServices.IsVolatile)", "int System.Runtime.CompilerServices.IsVolatile")]
    [InlineData("System.Int32 modopt(Mod)", "int Mod")]
    public void CleanTypeDisplay_StripsCustomModifierWrappers(string type, string expected)
        => Assert.Equal(expected, CSharpFormatter.CleanTypeDisplay(type));

    [Theory]
    [InlineData("(int, string)", "(int, string)")]
    [InlineData("System.ValueTuple<(int, string), object>", "System.ValueTuple<(int, string), object>")]
    public void CleanTypeDisplay_PreservesUnrelatedParentheses(string type, string expected)
        => Assert.Equal(expected, CSharpFormatter.CleanTypeDisplay(type));

    [Fact]
    public void CleanTypeDisplay_CollapsesUnspeakableGenericParameterToObject()
        => Assert.Equal("object", CSharpFormatter.CleanTypeDisplay("!0"));
}
