using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed partial class CSharpTypePrinterTests
{

    [Fact]
    public void DerivedUsingShortensCrossNamespaceReference()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Equal(["System.Threading.Tasks"], result.Usings);
        Assert.Contains("public Task Run();", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void DerivationUsesOnlySelectedMembers()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var selected = CreateMethod("Open");
        selected.SignatureModel!.ReturnType = "System.IO.Stream";
        var omitted = CreateMethod("CreateTimer");
        omitted.SignatureModel!.ReturnType = "System.Windows.Forms.Timer";
        type.Members.Add(selected);
        type.Members.Add(omitted);

        var result = _printer.Print(new CSharpTypePrintRequest(type, members: [selected]));

        Assert.Equal(["System.IO"], result.Usings);
        Assert.Contains("public Stream Open();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredNamespaceShortensPrimaryConstructorParameter()
    {
        var type = CreateEmptyType("Samples", "Worker");

        var result = _printer.Print(
            new CSharpTypePrintRequest(
                type,
                primaryConstructorParameters:
                [
                    new ApiParameter { Type = "System.IO.TextWriter", Name = "writer" }
                ]),
            new CSharpTypePrintOptions { Usings = ["System.IO"] });

        Assert.Equal(["System.IO"], result.Usings);
        Assert.Contains("public class Worker(TextWriter writer)", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryConstructorNestedTypeDoesNotInventNamespace()
    {
        var type = CreateEmptyType("Samples", "Worker");

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            primaryConstructorParameters:
            [
                new ApiParameter
                {
                    Type = "System.Environment.SpecialFolder",
                    Name = "folder"
                }
            ]));

        Assert.Empty(result.Usings);
        Assert.Contains(
            "public class Worker(System.Environment.SpecialFolder folder)",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryConstructorTypeStaysQualifiedWithDistinctConfiguredUsing()
    {
        var type = CreateEmptyType("Samples", "Worker");

        var result = _printer.Print(
            new CSharpTypePrintRequest(
                type,
                primaryConstructorParameters:
                [
                    new ApiParameter { Type = "Lib.Exception", Name = "exception" }
                ]),
            new CSharpTypePrintOptions { Usings = ["System"] });

        Assert.Equal(["System"], result.Usings);
        Assert.Contains(
            "public class Worker(Lib.Exception exception)",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullMemberUsesBareTypesBackedByNamespaceSet()
    {
        var type = CreateEmptyType("Samples", "FieldWriter");
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
        type.Members.Add(constructor);

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    constructor,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody(
                        """
                        this.writer = writer;
                        this.formatter = formatter;
                        _options = options ?? new MarkoutWriterOptions();
                        """))
            ]));

        Assert.Equal(
            ["Markout", "Markout.Formatting", "System.IO"],
            result.Usings);
        Assert.Contains(
            "public FieldWriter(TextWriter writer, IFieldFormatter formatter, MarkoutWriterOptions? options = null)",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.StartsWith(
            "using Markout;\nusing Markout.Formatting;\nusing System.IO;\n",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyKeepsReferencesQualified()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Empty(result.Usings);
        Assert.Contains(
            "public System.Threading.Tasks.Task Run();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContextualShortUsesCallerNamespaceContextWithoutDerivingImports()
    {
        var type = CreateEmptyType("Samples", "Worker");
        type.Members.Add(new ApiMember
        {
            Name = "Run",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Threading.Tasks.Task",
                MemberName = "Run",
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "System.Threading.CancellationToken",
                        Name = "cancellationToken"
                    }
                ]
            }
        });

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                Usings = ["System.Threading.Tasks"]
            });

        Assert.Equal(["System.Threading.Tasks"], result.Usings);
        Assert.Contains(
            "public Task Run(System.Threading.CancellationToken cancellationToken);",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Threading;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortWithUsingsReturnsFieldWriterImportsAsRawNamespaces()
    {
        var type = CreateEmptyType("Markout.Writers", "FieldWriter");
        type.Members.Add(new ApiMember
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
                        Type = "Markout.Options.MarkoutWriterOptions?",
                        Name = "options",
                        HasDefault = true,
                        DefaultValueText = "null"
                    }
                ]
            }
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public FieldWriter(TextWriter writer, IFieldFormatter formatter, MarkoutWriterOptions? options = null)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Equal(
            ["Markout.Formatting", "Markout.Options", "System.IO"],
            result.Usings.Order(StringComparer.Ordinal));
        Assert.All(result.Usings, ns => Assert.DoesNotContain("using ", ns, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUndefinedTypeNamePolicy()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Worker")),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = (CSharpTypeNamePolicy)42
            }));

        Assert.Contains("type-name policy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CollidingSimpleNamesAcrossNamespacesStayQualified()
    {
        var type = CreateEmptyType("Samples", "Consumer");
        type.Members.Add(new ApiMember
        {
            Name = "Convert",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.Widget",
                MemberName = "Convert",
                Parameters = [new ApiParameter { Type = "Beta.Widget", Name = "value" }]
            }
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using Alpha;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Beta;", result.Source, StringComparison.Ordinal);
        Assert.Empty(result.Usings);
        Assert.Contains(
            "public Alpha.Widget Convert(Beta.Widget value);",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CrossTypeAmbiguousSimpleNameStaysQualifiedAcrossUnit()
    {
        // "String" is ambiguous unit-wide (System.String in TypeA, MyNamespace.String
        // in TypeB) even though each type alone sees only one full name. Importing
        // System/MyNamespace (justified by the unambiguous Int32/Int64) would shorten
        // both references to `String`, producing an ambiguous reference. Neither
        // namespace may be imported; every reference they own stays qualified.
        var a = CreateEmptyType("Ns1", "TypeA");
        a.Members.Add(new ApiMember
        {
            Name = "M1",
            Kind = "method",
            SignatureModel = new ApiSignature { ReturnType = "System.String", MemberName = "M1" }
        });
        a.Members.Add(new ApiMember
        {
            Name = "M2",
            Kind = "method",
            SignatureModel = new ApiSignature { ReturnType = "System.Int32", MemberName = "M2" }
        });
        var b = CreateEmptyType("Ns1", "TypeB");
        b.Members.Add(new ApiMember
        {
            Name = "N1",
            Kind = "method",
            SignatureModel = new ApiSignature { ReturnType = "MyNamespace.String", MemberName = "N1" }
        });
        b.Members.Add(new ApiMember
        {
            Name = "N2",
            Kind = "method",
            SignatureModel = new ApiSignature { ReturnType = "MyNamespace.Int64", MemberName = "N2" }
        });

        var result = _printer.PrintBatch([new CSharpTypePrintRequest(a), new CSharpTypePrintRequest(b)]);

        Assert.DoesNotContain("using System;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using MyNamespace;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public System.String M1();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public MyNamespace.String N1();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void CollisionDoesNotBlockUnrelatedSameNamespaceShortening()
    {
        var type = CreateEmptyType("Alpha", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "GetPanel",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.Panel",
                MemberName = "GetPanel"
            }
        });
        type.Members.Add(new ApiMember
        {
            Name = "Pair",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.Button",
                MemberName = "Pair",
                Parameters =
                [
                    new ApiParameter { Type = "Beta.Button", Name = "other" },
                    new ApiParameter { Type = "Alpha.Panel", Name = "panel" }
                ]
            }
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("public Panel GetPanel();", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public Button Pair(Beta.Button other, Panel panel);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NestedDeclaredTypeShadowsSameNamespaceTopLevelReference()
    {
        var outer = CreateEmptyType("Alpha", "Widget");
        var member = CreateMethod("GetButton");
        member.SignatureModel!.ReturnType = "Alpha.Button";
        outer.Members.Add(member);
        var nested = CreateEmptyType("Alpha", "Button");

        var result = _printer.Print(new CSharpTypePrintRequest(
            outer,
            nestedTypes: [new CSharpTypePrintRequest(nested)]));

        Assert.Contains(
            "public Alpha.Button GetButton();",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("public class Button", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnclosingTypeParameterShadowsSameNamespaceReferencesInNestedType()
    {
        var outer = CreateEmptyType("Alpha", "Widget`1");
        outer.TypeParameters = [new TypeParameter { Name = "T" }];
        var nested = CreateEmptyType("Alpha", "Inner");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "Alpha.T";
        nested.Members.Add(member);

        var result = _printer.Print(new CSharpTypePrintRequest(
            outer,
            nestedTypes:
            [
                new CSharpTypePrintRequest(
                    nested,
                    primaryConstructorParameters:
                    [
                        new ApiParameter { Type = "Alpha.T", Name = "value" }
                    ])
            ]));

        Assert.Contains(
            "public class Inner(Alpha.T value)",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("public Alpha.T Get();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextualShortFiltersCollidingCallerImportsAcrossUnit()
    {
        var first = CreateEmptyType("Samples", "First");
        first.Members.Add(new ApiMember
        {
            Name = "Get",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Alpha.Widget",
                MemberName = "Get"
            }
        });
        var second = CreateEmptyType("Samples", "Second");
        second.Members.Add(new ApiMember
        {
            Name = "Get",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Beta.Widget",
                MemberName = "Get"
            }
        });

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(first), new CSharpTypePrintRequest(second)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                Usings = ["Alpha", "Beta"]
            });

        Assert.Contains("using Alpha;", result.Source, StringComparison.Ordinal);
        Assert.Contains("using Beta;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Alpha.Widget Get();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Beta.Widget Get();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortWithUsingsDerivesAlongsideCallerImports()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("CreateTimer");
        member.SignatureModel!.ReturnType = "System.Windows.Forms.Timer";
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
                Usings = ["System.Threading"]
            });

        Assert.Equal(
            ["System.Threading", "System.Windows.Forms"],
            result.Usings.Order(StringComparer.Ordinal));
        Assert.Contains("using System.Windows.Forms;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public Timer CreateTimer();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RawSignatureMethodTypeParameterShadowsReferenceAndStaysQualified()
    {
        // A generic method whose signature failed structured decoding falls back to the
        // raw Signature string (no SignatureModel). Its type parameter `Task` still
        // shadows the same-named return type reference, so the namespace must not be
        // imported and the reference must stay qualified.
        var type = CreateEmptyType("Samples", "Worker");
        type.IsAbstract = true;
        type.Members.Add(new ApiMember
        {
            Name = "Run",
            Kind = "method",
            IsAbstract = true,
            Signature = "System.Threading.Tasks.Task Run<Task>()"
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Contains("System.Threading.Tasks.Task Run<Task>()", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RawSignatureTupleReturnMethodTypeParameterStaysQualified()
    {
        // A tuple return type puts a '(' before the parameter list, so the raw-signature
        // parser must anchor on the method name + generic list, not the first '('. The
        // method type parameter `Task` still shadows the same-named references.
        var type = CreateEmptyType("Samples", "Worker");
        type.IsAbstract = true;
        type.Members.Add(new ApiMember
        {
            Name = "Run",
            Kind = "method",
            IsAbstract = true,
            Signature = "(System.Threading.Tasks.Task, int) Run<Task>(System.Threading.Tasks.Task value)"
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "(Task, int)",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceCollidingWithDeclaredTypeStaysQualified()
    {
        // A type declared as `Task` referencing `System.Threading.Tasks.Task` must not
        // import the namespace: shortening the reference to `Task` would bind to the
        // declared type, not the referenced one.
        var type = CreateEmptyType("Samples", "Task");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public System.Threading.Tasks.Task Run();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeArgumentEnumAccessDoesNotDeriveTypeAsNamespace()
    {
        // The attribute argument `UnmanagedType.I4` is a value expression, not a type
        // reference; deriving `using System.Runtime.InteropServices.UnmanagedType;` from
        // it would emit an illegal type-as-namespace using and mis-shorten the argument.
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Encode",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.Runtime.InteropServices.UnmanagedType",
                MemberName = "Encode",
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "int",
                        Name = "value",
                        Attributes =
                        [
                            "System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)"
                        ]
                    }
                ]
            }
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("using System.Runtime.InteropServices;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "using System.Runtime.InteropServices.UnmanagedType;",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[MarshalAs(System.Runtime.InteropServices.UnmanagedType.I4)]",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[MarshalAs(UnmanagedType.I4)]",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.Contains("public UnmanagedType Encode(", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void DottedAttributeValueSharingDeclaredTypeRootUsesGlobalNamespace()
    {
        var type = CreateEmptyType("App", "Samples");
        var member = CreateMethod("GetColor");
        member.SignatureModel!.ReturnType = "int";
        member.Attributes =
        [
            "System.ComponentModel.DefaultValue(Samples.Models.Color.Red)",
            "System.ComponentModel.Description(\"Items[0]\")"
        ];
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "[System.ComponentModel.DefaultValue(global::Samples.Models.Color.Red)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public int GetColor();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredNestedPathInOtherNamespaceDoesNotCaptureAttributeValue()
    {
        var otherContainer = CreateEmptyType("Other", "Container");
        var kind = CreateEmptyType("Other", "Kind");
        var appContainer = CreateEmptyType("App", "Container");
        appContainer.Attributes = ["Ext.Opt(Container.Kind.Fast)"];

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    otherContainer,
                    nestedTypes: [new CSharpTypePrintRequest(kind)]),
                new CSharpTypePrintRequest(appContainer)
            ],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "[Ext.Opt(global::Container.Kind.Fast)]",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ImportedDeclaredNestedPathKeepsRelativeAttributeValue()
    {
        var foo = CreateEmptyType("A", "Foo");
        var options = CreateEmptyType("A", "Options");
        var consumer = CreateEmptyType("App", "Consumer");
        consumer.Attributes = ["Ext.Opt(Foo.Options.Fast)"];
        var method = CreateMethod("GetFoo");
        method.SignatureModel!.ReturnType = "A.Foo";
        consumer.Members.Add(method);

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    foo,
                    nestedTypes: [new CSharpTypePrintRequest(options)]),
                new CSharpTypePrintRequest(consumer)
            ],
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.Contains("using A;", result.Source, StringComparison.Ordinal);
        Assert.Contains("[Ext.Opt(Foo.Options.Fast)]", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("global::Foo.Options.Fast", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeValueDoesNotDriveImportedDeclaredNestedPathUsing()
    {
        var foo = CreateEmptyType("A", "Foo");
        var options = CreateEmptyType("A", "Options");
        var consumer = CreateEmptyType("App", "Consumer");
        consumer.Attributes = ["Ext.Opt(Foo.Options.Fast)"];

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    foo,
                    nestedTypes: [new CSharpTypePrintRequest(options)]),
                new CSharpTypePrintRequest(consumer)
            ],
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.DoesNotContain("using A;", result.Source, StringComparison.Ordinal);
        Assert.Contains("[Ext.Opt(Foo.Options.Fast)]", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbiguousImportedNestedPathIsNotPreservedAsRelative()
    {
        var aFoo = CreateEmptyType("A", "Foo");
        var aOptions = CreateEmptyType("A", "Options");
        var bFoo = CreateEmptyType("B", "Foo");
        var bOptions = CreateEmptyType("B", "Options");
        var consumer = CreateEmptyType("App", "Consumer");
        consumer.Attributes = ["Ext.Opt(Foo.Options.Fast)"];
        var getLeft = CreateMethod("GetLeft");
        getLeft.SignatureModel!.ReturnType = "A.Left";
        var getRight = CreateMethod("GetRight");
        getRight.SignatureModel!.ReturnType = "B.Right";
        consumer.Members.Add(getLeft);
        consumer.Members.Add(getRight);

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    aFoo,
                    nestedTypes: [new CSharpTypePrintRequest(aOptions)]),
                new CSharpTypePrintRequest(
                    bFoo,
                    nestedTypes: [new CSharpTypePrintRequest(bOptions)]),
                new CSharpTypePrintRequest(consumer)
            ],
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.Contains("using A;", result.Source, StringComparison.Ordinal);
        Assert.Contains("using B;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("[Ext.Opt(Foo.Options.Fast)]", result.Source, StringComparison.Ordinal);
        Assert.Contains("[Ext.Opt(global::Foo.Options.Fast)]", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredNestedPathDoesNotCaptureKnownNamespaceReference()
    {
        var system = CreateEmptyType("App", "System");
        var uri = CreateEmptyType("App", "Uri");
        system.Attributes = ["Ext.Opt(System.Uri.SchemeDelimiter)"];
        var member = CreateMethod("Create");
        member.SignatureModel!.ReturnType = "System.Text.StringBuilder";
        system.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(
                system,
                nestedTypes: [new CSharpTypePrintRequest(uri)]),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "[Ext.Opt(global::System.Uri.SchemeDelimiter)]",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void KeywordRootInDottedAttributeValueIsEscapedWithoutShortening()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("GetColor");
        member.SignatureModel!.ReturnType = "int";
        member.Attributes =
        [
            "System.ComponentModel.DefaultValue(event.Models.Color.Red)"
        ];
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "@event.Models.Color.Red",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultValue(Color.Red)", result.Source, StringComparison.Ordinal);
        Assert.Contains("public int GetColor();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void DottedAttributeValueRootedAtGlobalTypeDoesNotAbortBatch()
    {
        var host = CreateEmptyType("", "Host");
        var worker = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "int";
        member.Attributes = ["Ext.Opt(Host.Options.Fast)"];
        worker.Members.Add(member);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(host), new CSharpTypePrintRequest(worker)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Contains("[Ext.Opt(global::Host.Options.Fast)]", result.Source, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void TypeAndDelegateAttributeValuesKeepDeclaredTypeRoots()
    {
        var samples = CreateEmptyType("App", "Samples");
        samples.Attributes = ["Ext.Opt(Samples.Options.Fast)"];
        var options = CreateEmptyType("App", "Options");
        var handler = CreateEmptyType("App", "Handler");
        handler.Kind = "delegate";
        handler.Attributes = ["Ext.Opt(Samples.Options.Fast)"];
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "void";
        handler.Members.Add(invoke);

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    samples,
                    nestedTypes: [new CSharpTypePrintRequest(options)]),
                new CSharpTypePrintRequest(handler)
            ],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Equal(
            2,
            result.Source.Split(
                "[Ext.Opt(Samples.Options.Fast)]",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("global::Samples.Options.Fast", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void DottedAttributeValueDoesNotInventShadowingNamespace()
    {
        var type = CreateEmptyType("Lib.Sub", "Widget");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "Foo.Deep";
        member.Attributes = ["Ext.Opt(Lib.Sub.Deep.Const.Field)"];
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
                IncludeCustomAttributes = true
            });

        Assert.Contains("using Foo;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Deep Get();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("shadowed by a namespace", string.Join('\n', result.Diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void RawStringAttributeValueIsNotRewritten()
    {
        var type = CreateEmptyType("App", "Consumer");
        var method = CreateMethod("Get");
        method.SignatureModel!.ReturnType = "N.Type";
        method.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "string",
            Name = "value",
            Attributes = ["Ext.Note(\"\"\"\"N.Type\"\"\"\")"]
        });
        type.Members.Add(method);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.Contains("\"\"\"\"N.Type\"\"\"\"", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[Ext.Note(\"\"\"\"Type\"\"\"\")]",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SameNamespaceRootShadowRequalifiesDottedAttributeValue()
    {
        var widget = CreateEmptyType("Lib.Sub", "Widget");
        var member = CreateMethod("GetThing");
        member.SignatureModel!.ReturnType = "Lib.Sub.Thing";
        member.Attributes = ["Ext.Opt(Lib.Sub.Thing.Value)"];
        widget.Members.Add(member);
        var lib = CreateEmptyType("Lib.Sub", "Lib");

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(widget), new CSharpTypePrintRequest(lib)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ShortWithUsings,
                IncludeCustomAttributes = true
            });

        Assert.Contains("[Ext.Opt(global::Lib.Sub.Thing.Value)]", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Thing GetThing();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void RawKeywordSegmentsArePlannedAcrossDeclarationSurfaces()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.BaseType = "Lib.event.Base";
        type.Interfaces.Add("Lib.event.IThing");
        type.Attributes = ["Lib.event.Marker"];
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "Lib.event.Color";
        var handler = CreateEmptyType("Samples", "Handler");
        handler.Kind = "delegate";
        handler.Members.Add(invoke);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(type), new CSharpTypePrintRequest(handler)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Contains("[Lib.@event.Marker]", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public class Widget : Lib.@event.Base, Lib.@event.IThing",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public delegate Lib.@event.Color Handler();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenericTypeParameterShadowsReferenceAndStaysQualified()
    {
        // The type parameter `Task` shadows any same-named type reference within the
        // type body, so importing System.Threading.Tasks and shortening the return type
        // to `Task` would rebind it to the parameter. It must stay fully qualified.
        var type = CreateEmptyType("Samples", "Box`1");
        type.TypeParameters = [new TypeParameter { Name = "Task" }];
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public System.Threading.Tasks.Task Run();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MethodTypeParameterShadowsReferenceAndStaysQualified()
    {
        // A method type parameter named `Task` shadows the same-named type reference;
        // the namespace must not be imported.
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        member.SignatureModel!.TypeParameters = [new TypeParameter { Name = "Task" }];
        type.Members.Add(member);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "System.Threading.Tasks.Task Run",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NestedTypeReferencedAsNamespaceIsNotImportedWhenEnclosingTypeIsReferenced()
    {
        // `System.Environment.SpecialFolder` is a nested type but arrives as a flat
        // dotted string, so the last-dot split derives namespace `System.Environment`
        // — which is actually a type. Emitting `using System.Environment;` is illegal.
        // When the enclosing type `System.Environment` is itself referenced in the
        // unit, its full name shows up as a derived namespace and must be excluded, so
        // the nested reference stays fully qualified.
        var type = CreateEmptyType("App", "Consumer");
        var enclosing = CreateMethod("GetEnv");
        enclosing.SignatureModel!.ReturnType = "System.Environment";
        var nested = CreateMethod("GetFolder");
        nested.SignatureModel!.ReturnType = "System.Environment.SpecialFolder";
        type.Members.Add(enclosing);
        type.Members.Add(nested);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using System.Environment;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "System.Environment.SpecialFolder GetFolder();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceCollidingWithNamespaceSegmentStaysQualified()
    {
        var type = CreateEmptyType("Samples.Models", "Worker");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "External.Models";
        type.Members.Add(member);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using External;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public External.Models Get();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceCollidingWithDerivedNamespaceRootStaysQualified()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var widget = CreateMethod("GetWidget");
        widget.SignatureModel!.ReturnType = "Alpha.Beta.Widget";
        var alpha = CreateMethod("GetAlpha");
        alpha.SignatureModel!.ReturnType = "Zeta.Alpha";
        type.Members.Add(widget);
        type.Members.Add(alpha);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Equal(["Alpha.Beta"], result.Usings);
        Assert.Contains("public Widget GetWidget();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Zeta.Alpha GetAlpha();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceCollidingWithCallerNamespaceRootStaysQualified()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var alpha = CreateMethod("GetAlpha");
        alpha.SignatureModel!.ReturnType = "Zeta.Alpha";
        type.Members.Add(alpha);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                Usings = ["Alpha.Beta"]
            });

        Assert.Equal(["Alpha.Beta"], result.Usings);
        Assert.DoesNotContain("using Zeta;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Zeta.Alpha GetAlpha();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceCollidingWithEnclosingNamespaceChildStaysQualified()
    {
        var type = CreateEmptyType("Alpha.Beta", "Worker");
        var thing = CreateMethod("GetThing");
        thing.SignatureModel!.ReturnType = "Alpha.Gamma.Thing";
        var gamma = CreateMethod("GetGamma");
        gamma.SignatureModel!.ReturnType = "Other.Gamma";
        type.Members.Add(thing);
        type.Members.Add(gamma);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Equal(["Alpha.Gamma"], result.Usings);
        Assert.Contains("public Thing GetThing();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Other.Gamma GetGamma();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void CallerNamespaceChildShadowsSameNamedReference()
    {
        var type = CreateEmptyType("Alpha.Beta", "Worker");
        var gamma = CreateMethod("GetGamma");
        gamma.SignatureModel!.ReturnType = "Other.Gamma";
        type.Members.Add(gamma);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                Usings = ["Alpha.Gamma", "Other"]
            });

        Assert.Equal(
            ["Alpha.Gamma", "Other"],
            result.Usings.Order(StringComparer.Ordinal));
        Assert.Contains("public Other.Gamma GetGamma();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedNamespaceChildDoesNotShadowSameNamedReference()
    {
        var type = CreateEmptyType("Alpha.Beta", "Worker");
        var thing = CreateMethod("GetThing");
        thing.SignatureModel!.ReturnType = "Zeta.Delta.Thing";
        var delta = CreateMethod("GetDelta");
        delta.SignatureModel!.ReturnType = "Other.Delta";
        type.Members.Add(thing);
        type.Members.Add(delta);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Equal(
            ["Other", "Zeta.Delta"],
            result.Usings.Order(StringComparer.Ordinal));
        Assert.Contains("public Thing GetThing();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Delta GetDelta();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SameNamespaceTypeMatchingRootUsesShortName()
    {
        var type = CreateEmptyType("Alpha.Beta", "Worker");
        var alpha = CreateMethod("GetAlpha");
        alpha.SignatureModel!.ReturnType = "Alpha.Beta.Alpha";
        type.Members.Add(alpha);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("public Alpha GetAlpha();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha.Beta.Alpha", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainingNamespaceChildShadowedRootUsesGlobalAlias()
    {
        var type = CreateEmptyType("Alpha.System", "Worker");
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        type.Members.Add(uri);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "public global::System.Uri GetUri();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContainingNamespaceRootDoesNotRequireGlobalAlias()
    {
        var type = CreateEmptyType("System.Example", "Worker");
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        type.Members.Add(uri);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains("public System.Uri GetUri();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("global::System.Uri", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SiblingMemberNamespaceEvidenceTriggersGlobalAlias()
    {
        var type = CreateEmptyType("Alpha.Beta", "Worker");
        var thing = CreateMethod("GetThing");
        thing.SignatureModel!.ReturnType = "Alpha.System.Thing";
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        type.Members.Add(thing);
        type.Members.Add(uri);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains("public Alpha.System.Thing GetThing();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public global::System.Uri GetUri();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseTypeNamespaceEvidenceTriggersGlobalAlias()
    {
        var type = CreateEmptyType("Alpha.Beta", "Worker");
        type.BaseType = "Alpha.System.Base";
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        type.Members.Add(uri);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(": Alpha.System.Base", result.Source, StringComparison.Ordinal);
        Assert.Contains("public global::System.Uri GetUri();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnclosingTypeNameTriggersGlobalAliasInNestedType()
    {
        var outer = CreateEmptyType("Samples", "Beta");
        var nested = CreateEmptyType("Samples", "Inner");
        var widget = CreateMethod("GetWidget");
        widget.SignatureModel!.ReturnType = "Beta.Models.Widget";
        nested.Members.Add(widget);

        var result = _printer.Print(new CSharpTypePrintRequest(
            outer,
            nestedTypes: [new CSharpTypePrintRequest(nested)]));

        Assert.Contains(
            "public global::Beta.Models.Widget GetWidget();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevelSiblingTypeNameTriggersGlobalAlias()
    {
        var worker = CreateEmptyType("Samples", "Worker");
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        worker.Members.Add(uri);
        var system = CreateEmptyType("Samples", "System");

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(worker), new CSharpTypePrintRequest(system)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains("public global::System.Uri GetUri();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public class System", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AncestorNamespaceTypeNameTriggersGlobalAlias()
    {
        var system = CreateEmptyType("Alpha", "System");
        var worker = CreateEmptyType("Alpha.Beta", "Worker");
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        worker.Members.Add(uri);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(worker)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains("public class System", result.Source, StringComparison.Ordinal);
        Assert.Contains("public global::System.Uri GetUri();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalNamespaceTypeConflictingWithNamespaceRootReportsDiagnostic()
    {
        var system = CreateEmptyType("", "System");
        var worker = CreateEmptyType("Samples", "Worker");
        var uri = CreateMethod("GetUri");
        uri.SignatureModel!.ReturnType = "System.Uri";
        worker.Members.Add(uri);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(worker)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "public global::System.Uri GetUri();",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("conflicts with global type 'System'", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalNamespaceTypeConflictRecognizesNestedNamespaceRoot()
    {
        var system = CreateEmptyType("", "System");
        var worker = CreateEmptyType("Samples", "Worker");
        var method = CreateMethod("GetItems");
        method.SignatureModel!.ReturnType = "System.Collections.Generic.List<int>";
        worker.Members.Add(method);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(worker)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "global::System.Collections.Generic.List<int>",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains("conflicts with global type 'System'", StringComparison.Ordinal));
    }

    [Fact]
    public void DelegateWithGlobalNamespaceRootConflictReportsDiagnostic()
    {
        var system = CreateEmptyType("", "System");
        var handler = CreateEmptyType("Samples", "Handler");
        handler.Kind = "delegate";
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "System.Uri";
        handler.Members.Add(invoke);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(handler)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "public delegate global::System.Uri Handler();",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.TypeName == "Samples.Handler"
                && diagnostic.Message.Contains("conflicts with global type 'System'", StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalTypeCanReferenceItsDeclaredNestedType()
    {
        var host = CreateEmptyType("", "Host");
        var classify = CreateMethod("Classify");
        classify.SignatureModel!.ReturnType = "Host.Kind";
        host.Members.Add(classify);
        var kind = CreateEmptyType("", "Kind");
        kind.Kind = "enum";

        var result = _printer.Print(new CSharpTypePrintRequest(
            host,
            nestedTypes: [new CSharpTypePrintRequest(kind)]));

        Assert.Contains("public global::Host.Kind Classify();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public enum Kind", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SiblingTypeReferenceRemainsShort()
    {
        var widget = CreateEmptyType("Alpha", "Widget");
        var panel = CreateEmptyType("Alpha", "Panel");
        var getPanel = CreateMethod("GetPanel");
        getPanel.SignatureModel!.ReturnType = "Alpha.Panel";
        widget.Members.Add(getPanel);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(widget), new CSharpTypePrintRequest(panel)]);

        Assert.Contains("public Panel GetPanel();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Alpha.Panel GetPanel();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextualShortUsesSafeImportThatIsAlsoADeclaringNamespace()
    {
        var thing = CreateEmptyType("Alpha", "Thing");
        var worker = CreateEmptyType("Beta", "Worker");
        var getThing = CreateMethod("GetThing");
        getThing.SignatureModel!.ReturnType = "Alpha.Thing";
        worker.Members.Add(getThing);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(thing), new CSharpTypePrintRequest(worker)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                Usings = ["Alpha"]
            });

        Assert.Contains("using Alpha;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Thing GetThing();", result.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpTypeNamePolicy.ShortWithUsings)]
    [InlineData(CSharpTypeNamePolicy.ContextualShort)]
    public void ImportedDeclaredTypeCannotCaptureQualifiedNamespaceRoot(
        CSharpTypeNamePolicy policy)
    {
        var importedSystem = CreateEmptyType("Imported", "System");
        var importedWidget = CreateEmptyType("Imported", "Widget");
        var consumer = CreateEmptyType("Samples", "Consumer");
        var getWidget = CreateMethod("GetWidget");
        getWidget.SignatureModel!.ReturnType = "Imported.Widget";
        var getUri = CreateMethod("GetUri");
        getUri.SignatureModel!.ReturnType = "System.Uri";
        consumer.Members.Add(getWidget);
        consumer.Members.Add(getUri);

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(importedSystem),
                new CSharpTypePrintRequest(importedWidget),
                new CSharpTypePrintRequest(consumer)
            ],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = policy,
                Usings = policy == CSharpTypeNamePolicy.ContextualShort
                    ? ["Imported"]
                    : []
            });

        Assert.Contains("using Imported;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Widget GetWidget();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public global::System.Uri GetUri();", result.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Alpha", "Beta")]
    [InlineData("A", "A.B")]
    public void ShortWithUsingsImportsOtherDeclaringNamespace(
        string consumerNamespace,
        string dependencyNamespace)
    {
        var worker = CreateEmptyType(consumerNamespace, "Worker");
        var getThing = CreateMethod("GetThing");
        getThing.SignatureModel!.ReturnType = $"{dependencyNamespace}.Thing";
        worker.Members.Add(getThing);
        var thing = CreateEmptyType(dependencyNamespace, "Thing");

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(worker), new CSharpTypePrintRequest(thing)]);

        Assert.Contains($"using {dependencyNamespace};", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Thing GetThing();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredUsingKeepsOtherDeclaredNamespaceReferenceQualified()
    {
        var exception = CreateEmptyType("Lib", "Exception");
        var consumer = CreateEmptyType("App", "Consumer");
        var getException = CreateMethod("GetException");
        getException.SignatureModel!.ReturnType = "Lib.Exception";
        consumer.Members.Add(getException);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(exception), new CSharpTypePrintRequest(consumer)],
            new CSharpTypePrintOptions { Usings = ["System"] });

        Assert.Equal(["System"], result.Usings);
        Assert.DoesNotContain("using Lib;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public Lib.Exception GetException();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedUsingKeepsOtherDeclaredNamespaceReferenceQualified()
    {
        var marker = CreateEmptyType("Lib", "Marker");
        var consumer = CreateEmptyType("App", "Consumer");
        var getMarker = CreateMethod("GetMarker");
        getMarker.SignatureModel!.ReturnType = "Lib.Marker";
        getMarker.SignatureModel.Parameters =
        [
            new ApiParameter { Type = "Other.Value", Name = "value" }
        ];
        consumer.Members.Add(getMarker);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(marker), new CSharpTypePrintRequest(consumer)]);

        Assert.Equal(["Other"], result.Usings);
        Assert.DoesNotContain("using Lib;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public Lib.Marker GetMarker(Value value);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalTypeReferencePreventsCollidingDeclaredNamespaceImport()
    {
        var node = CreateEmptyType("Lib", "Node");
        var client = CreateEmptyType("App", "Client");
        var getNode = CreateMethod("GetNode");
        getNode.SignatureModel!.ReturnType = "Lib.Node";
        getNode.SignatureModel.Parameters =
        [
            new ApiParameter { Type = "Node", Name = "ambient" }
        ];
        client.Members.Add(getNode);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(node), new CSharpTypePrintRequest(client)]);

        Assert.DoesNotContain("using Lib;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("Lib", result.Usings);
        Assert.Contains(
            "public Lib.Node GetNode(Node ambient);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredSimpleNameCollisionKeepsReferencedDeclarationQualified()
    {
        var user = CreateEmptyType("App", "User");
        user.Members.Add(new ApiMember
        {
            Name = "Value",
            Kind = "field",
            ReturnType = "N.Sub.Marker"
        });

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(user),
            new CSharpTypePrintRequest(CreateEmptyType("App", "Marker")),
            new CSharpTypePrintRequest(CreateEmptyType("N.Sub", "Marker"))
        ]);

        Assert.DoesNotContain("using N.Sub;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public N.Sub.Marker Value;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericDeclaredSimpleNameCollisionKeepsReferenceQualified()
    {
        var user = CreateEmptyType("App", "User");
        var getMarker = CreateMethod("GetMarker");
        getMarker.SignatureModel!.ReturnType = "N.Sub.Marker<int>";
        user.Members.Add(getMarker);
        var genericMarker = CreateEmptyType("N.Sub", "Marker`1");
        genericMarker.TypeParameters = [new TypeParameter { Name = "T" }];

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(user),
            new CSharpTypePrintRequest(CreateEmptyType("App", "Marker")),
            new CSharpTypePrintRequest(genericMarker)
        ]);

        Assert.DoesNotContain("using N.Sub;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public N.Sub.Marker<int> GetMarker();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NestedDeclarationDoesNotAuthorizeContainingTypeUsing()
    {
        var user = CreateEmptyType("App", "User");
        user.Members.Add(new ApiMember
        {
            Name = "Value",
            Kind = "field",
            ReturnType = "N.Container.Marker"
        });
        var container = CreateEmptyType("N", "Container");
        var marker = CreateEmptyType("N", "Marker");

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(user),
            new CSharpTypePrintRequest(
                container,
                nestedTypes: [new CSharpTypePrintRequest(marker)])
        ]);

        Assert.DoesNotContain("using N.Container;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public N.Container.Marker Value;",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyPlansTypeAndMemberAttributes()
    {
        var system = CreateEmptyType("Samples", "System");
        var worker = CreateEmptyType("Samples", "Worker");
        worker.Attributes = ["System.ObsoleteAttribute"];
        var run = CreateMethod("Run");
        run.Attributes = ["System.ObsoleteAttribute"];
        worker.Members.Add(run);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(worker)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Equal(
            2,
            result.Source.Split("[global::System.ObsoleteAttribute]", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("[System.ObsoleteAttribute]", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyPlansTypeBearingAttributeArgumentsAndReturnAttributes()
    {
        var type = CreateEmptyType("Samples", "External`1");
        type.Attributes =
        [
            "Other.Marker(typeof(External.Value), (External.Kind)1)",
            "Other.KeywordMarker(typeof(Alpha.@event), (Alpha.@event)1)"
        ];
        type.TypeParameters = [new TypeParameter { Name = "Alpha" }];
        var method = CreateMethod("Get");
        method.Kind = "property";
        method.SignatureModel!.ReturnAttributes = ["External.ReturnMarker"];
        method.SignatureModel.Accessors =
        [
            new ApiAccessor
            {
                Kind = "get",
                ReturnAttributes = ["External.AccessorMarker"]
            }
        ];
        type.Members.Add(method);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "[Other.Marker(typeof(global::External.Value), (global::External.Kind)1)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[Other.KeywordMarker(typeof(global::Alpha.@event), (global::Alpha.@event)1)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::External.ReturnMarker]",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::External.AccessorMarker]",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParenthesizedAttributeValueFollowedByBinaryOperatorIsNotACast()
    {
        var constants = CreateEmptyType("App", "Constants");
        var consumer = CreateEmptyType("App", "Consumer");
        consumer.Attributes = ["App.Probe((Constants.Value) + 1)"];

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(constants),
                new CSharpTypePrintRequest(consumer)
            ],
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.Contains(
            "[App.Probe((Constants.Value) + 1)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("global::Constants.Value", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeValueCanUseTypeDeclaredInAncestorNamespace()
    {
        var foo = CreateEmptyType("A", "Foo");
        var options = CreateEmptyType("A", "Options");
        options.Kind = "enum";
        options.Members =
        [
            new ApiMember
            {
                Name = "Fast",
                Kind = "field",
                ReturnType = "A.Foo.Options"
            }
        ];
        var consumer = CreateEmptyType("A.B", "Consumer");
        consumer.Attributes = ["Marker(Foo.Options.Fast)"];

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    foo,
                    nestedTypes: [new CSharpTypePrintRequest(options)]),
                new CSharpTypePrintRequest(consumer)
            ],
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.Contains(
            "[Marker(Foo.Options.Fast)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("global::Foo.Options.Fast", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void QualifiedPolicyPlansDelegateReferences()
    {
        var type = new ApiType
        {
            Namespace = "Alpha.System",
            Name = "Callback",
            Kind = "delegate",
            Members =
            [
                new ApiMember
                {
                    Name = "Invoke",
                    Kind = "method",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Uri",
                        MemberName = "Invoke"
                    }
                }
            ]
        };

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "public delegate global::System.Uri Callback();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryConstructorShadowingProducesDiagnostic()
    {
        var type = CreateEmptyType("Samples", "Worker`1");
        type.TypeParameters = [new TypeParameter { Name = "Task" }];

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            primaryConstructorParameters:
            [
                new ApiParameter
                {
                    Type = "System.Threading.Tasks.Task",
                    Name = "task"
                }
            ]));

        Assert.Contains(
            "public class Worker<Task>(System.Threading.Tasks.Task task)",
            result.Source,
            StringComparison.Ordinal);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("Task", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("shadowed", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LexicallyShadowedQualifiedRootUsesGlobalAlias()
    {
        var type = CreateEmptyType("Samples", "Worker`2");
        type.TypeParameters =
        [
            new TypeParameter { Name = "Alpha" },
            new TypeParameter { Name = "Thing" }
        ];
        var thing = CreateMethod("GetThing");
        thing.SignatureModel!.ReturnType = "Alpha.Beta.Thing";
        type.Members.Add(thing);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using Alpha.Beta;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public global::Alpha.Beta.Thing GetThing();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpTypeNamePolicy.ShortWithUsings)]
    [InlineData(CSharpTypeNamePolicy.ContextualShort)]
    public void UsingsSuppressedKeepsReferencesQualified(CSharpTypeNamePolicy policy)
    {
        // With IncludeUsings=false the composed Source omits using directives, so
        // shortening a cross-namespace reference would leave it unresolvable.
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = policy,
                Usings = ["System.Threading.Tasks"],
                IncludeUsings = false
            });

        Assert.DoesNotContain("using System.Threading.Tasks;", result.Source, StringComparison.Ordinal);
        Assert.Empty(result.Usings);
        Assert.Contains(
            "public System.Threading.Tasks.Task Run();",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpTypeNamePolicy.Qualified, "System.Threading.Tasks.Task", false)]
    [InlineData(CSharpTypeNamePolicy.ShortWithUsings, "Task", true)]
    [InlineData(CSharpTypeNamePolicy.ContextualShort, "Task", true)]
    public void TypeNamePolicyAppliesToCompleteMemberWithBodyComposition(
        CSharpTypeNamePolicy policy,
        string expectedReturnType,
        bool expectsImport)
    {
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);

        var result = _printer.Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        member,
                        CSharpBodyPolicy.Full,
                        new CSharpBlockBody("return default!;"))
                ]),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = policy,
                Usings = policy == CSharpTypeNamePolicy.ContextualShort
                    ? ["System.Threading.Tasks"]
                    : []
            });

        Assert.Contains($"public {expectedReturnType} Run()", result.Source, StringComparison.Ordinal);
        Assert.Contains("return default!;", result.Source, StringComparison.Ordinal);
        Assert.Equal(expectsImport, result.Usings.Contains("System.Threading.Tasks"));
    }

    [Fact]
    public void ResultEqualityIncludesUsingSet()
    {
        var request = new CSharpTypePrintRequest(CreateEmptyType("Samples", "Worker"));
        var alpha = _printer.Print(
            request,
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                Usings = ["Alpha"]
            });
        var beta = _printer.Print(
            request,
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
                Usings = ["Beta"]
            });

        Assert.Equal(alpha.Units, beta.Units);
        Assert.Equal(alpha.Diagnostics, beta.Diagnostics);
        Assert.NotEqual(alpha, beta);
    }

    [Fact]
    public void ResultEqualityNormalizesUsingSetComparers()
    {
        var insensitive = new CSharpTypePrintResult(
            [],
            ImmutableSortedSet.Create(StringComparer.OrdinalIgnoreCase, "Alpha"),
            [],
            () => "");
        var ordinal = new CSharpTypePrintResult(
            [],
            ImmutableSortedSet.Create(StringComparer.Ordinal, "alpha"),
            [],
            () => "");

        Assert.False(insensitive.Equals(ordinal));
        Assert.False(ordinal.Equals(insensitive));
        Assert.Same(StringComparer.Ordinal, insensitive.Usings.KeyComparer);
    }
}
