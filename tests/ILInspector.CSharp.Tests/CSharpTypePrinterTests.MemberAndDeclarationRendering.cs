using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed partial class CSharpTypePrinterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PropertyInitializerPreservesPrimaryParameterAndTargetBody(bool automatic)
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var type = CreateEmptyType("Samples", "Counter");
        type.Kind = "struct";
        type.Members.Add(property);
        var body = new CSharpPropertyBody(
            automatic
                ? CSharpAccessorBody.Auto
                : CSharpAccessorBody.Block("return field + 1;") with { IsReplacementTarget = true },
            null)
        {
            Initializer = "@event",
        };
        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides: [new CSharpMemberPolicy(property, CSharpBodyPolicy.Full, body)],
            primaryConstructorParameters: [new ApiParameter { Type = "int", Name = "event" }]));

        Assert.Contains("struct Counter(int @event)", result.Source);
        Assert.Contains("} = @event;", result.Source);
        if (automatic)
            Assert.Contains("Value { get; } = @event;", result.Source);
        else
        {
            string replacement = result.SourceArtifact.ReplaceBody("return field + 2;");
            Assert.Contains("return field + 2;", replacement);
            Assert.DoesNotContain("return field + 1;", replacement);
            Assert.Contains("struct Counter(int @event)", replacement);
            Assert.Contains("} = @event;", replacement);
        }
    }

    [Theory]
    [InlineData(CSharpBodyPolicy.Full)]
    [InlineData(CSharpBodyPolicy.Stub)]
    public void AbstractMembersRejectImplementationPolicies(CSharpBodyPolicy bodyPolicy)
    {
        var member = CreateMethod("Run");
        member.IsAbstract = true;
        var type = CreateEmptyType("Samples", "Widget");
        type.IsAbstract = true;
        type.Members.Add(member);
        var body = bodyPolicy == CSharpBodyPolicy.Full
            ? new CSharpBlockBody("return;")
            : null;

        var exception = Assert.Throws<ArgumentException>(() => _printer.Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides: [new CSharpMemberPolicy(member, bodyPolicy, body)])));

        Assert.Contains("must use skeleton body policy", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StubPropertyRequiresExplicitAccessorBodyShape()
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(property);

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type, CSharpBodyPolicy.Stub)));

        Assert.Contains("requires an explicit accessor body shape", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryConstructorTypeRequiresExplicitConstructorInitializer()
    {
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature()
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(constructor);

        var exception = Assert.Throws<NotSupportedException>(() => _printer.Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides: [new CSharpMemberPolicy(constructor, CSharpBodyPolicy.Stub)],
                primaryConstructorParameters: [new ApiParameter { Type = "int", Name = "value" }])));

        Assert.Contains("requires an explicit constructor initializer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyBodySpecifiesIndependentAccessorShapes()
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Value",
                Accessors =
                [
                    new ApiAccessor { Kind = "get", ReturnAttributes = ["Marker"] },
                    new ApiAccessor { Kind = "set" }
                ]
            }
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(property);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    property,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Block("return 42;"),
                        CSharpAccessorBody.Throw))
            ]);

        var result = _printer.Print(request);

        Assert.Contains(
            """
                public int Value
                {
                    [return: Marker] get
                    {
                        return 42;
                    }
                    set
                    {
                        throw null;
                    }
                }
            """,
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FormatAccessorHead_OmittedAttributesDoNotLeaveLeadingWhitespace()
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Value",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "get",
                        Accessibility = "private",
                        ReturnAttributes = ["Marker"]
                    }
                ]
            }
        };
        var formatter = new CSharpFormatter(
            new CSharpFormatOptions { IncludeSignatureAttributes = false });

        string head = formatter.FormatAccessorHead(
            CreateEmptyType("Samples", "Widget"),
            property,
            "get");

        Assert.Equal("private get", head);
    }

    [Fact]
    public void FullPropertyAndEventBodiesPlanAccessorReturnAttributes()
    {
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Value",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "get",
                        ReturnAttributes = ["External.GetterMarker"]
                    }
                ]
            }
        };
        var @event = new ApiMember
        {
            Name = "Changed",
            Kind = "event",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Changed",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "add",
                        ReturnAttributes = ["External.AddMarker"]
                    },
                    new ApiAccessor
                    {
                        Kind = "remove",
                        ReturnAttributes = ["External.RemoveMarker"]
                    }
                ]
            }
        };
        var type = CreateEmptyType("Samples", "External`1");
        type.TypeParameters = [new TypeParameter { Name = "void" }];
        type.Members.Add(property);
        type.Members.Add(@event);

        var result = _printer.Print(
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        property,
                        CSharpBodyPolicy.Full,
                        new CSharpPropertyBody(CSharpAccessorBody.Block("return 42;"), null)),
                    new CSharpMemberPolicy(
                        @event,
                        CSharpBodyPolicy.Full,
                        new CSharpEventBody(
                            CSharpAccessorBody.Block("_changed += value;"),
                            CSharpAccessorBody.Block("_changed -= value;")))
                ]),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "[return: global::External.GetterMarker] get",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::External.AddMarker] add",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[return: global::External.RemoveMarker] remove",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInterfacePropertyPreservesQualifiedNameAndOmitsAccessibility()
    {
        var property = new ApiMember
        {
            Name = "Samples.IValue.Value",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Samples.IValue.Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Interfaces.Add("Samples.IValue");
        type.Members.Add(property);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    property,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Block("return 42;"),
                        null))
            ]);

        var result = _printer.Print(request);

        Assert.Contains(
            """
                int Samples.IValue.Value
                {
                    get
                    {
                        return 42;
                    }
                }
            """,
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public int Samples.IValue.Value",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInterfaceIndexerPreservesQualifierAndOmitsAccessibility()
    {
        var indexer = new ApiMember
        {
            Name = "Samples.IValues.Item",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "this[]",
                Parameters = [new ApiParameter { Type = "int", Name = "index" }],
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Interfaces.Add("Samples.IValues");
        type.Members.Add(indexer);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    indexer,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Block("return index;"),
                        null))
            ]);

        var result = _printer.Print(request);

        Assert.Contains(
            """
                int Samples.IValues.this[int index]
                {
                    get
                    {
                        return index;
                    }
                }
            """,
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public int Samples.IValues.this",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInterfaceQualifierRespectsLexicalShadowing()
    {
        var property = new ApiMember
        {
            Name = "Samples.IValue.Value",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Samples.IValue.Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var type = CreateEmptyType("Samples", "Widget`1");
        type.MetadataName = "Widget`1";
        type.TypeParameters = [new TypeParameter { Name = "Samples" }];
        type.Interfaces.Add("Samples.IValue");
        type.Members.Add(property);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "int global::Samples.IValue.Value",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SiblingMemberTypeReferenceContributesRootShadowing()
    {
        var type = CreateEmptyType("Contoso.Data", "Store");
        var getJson = CreateMethod("GetJson");
        getJson.SignatureModel!.ReturnType = "Contoso.Data.Json";
        var getNode = CreateMethod("GetNode");
        getNode.SignatureModel!.ReturnType = "Json.Node";
        type.Members.Add(getJson);
        type.Members.Add(getNode);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeUsings = false
            });

        Assert.Contains(
            "public global::Json.Node GetNode();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AncestorNamespaceTypeReferenceContributesRootShadowing()
    {
        var type = CreateEmptyType("Contoso.Data.Serialization", "Store");
        var convert = CreateMethod("Convert");
        convert.SignatureModel!.ReturnType = "Json.Node";
        convert.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "Contoso.Data.Json",
            Name = "value"
        });
        type.Members.Add(convert);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                IncludeUsings = false
            });

        Assert.Contains(
            "public global::Json.Node Convert(Contoso.Data.Json value);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenericInterfaceReferencesArePlannedByTypeComponent()
    {
        var type = CreateEmptyType("App", "Host");
        type.Interfaces.Add("A.IFoo<B.C>");

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("using A;", result.Source, StringComparison.Ordinal);
        Assert.Contains("using B;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public class Host : IFoo<C>", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using A.IFoo<B;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedGenericTypePathPreservesEveryNestedSegment()
    {
        var type = CreateEmptyType("App", "Host");
        type.Interfaces.Add("N.Outer<T>.Middle.Inner<U>");

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("using N;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public class Host : Outer<T>.Middle.Inner<U>",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("using Middle;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryConstructorAttributeEvidenceContributesToMemberPlanning()
    {
        var type = CreateEmptyType("Contoso.Data", "Store");
        var method = CreateMethod("GetNode");
        method.SignatureModel!.ReturnType = "Json.Node";
        type.Members.Add(method);
        var parameter = new ApiParameter
        {
            Type = "int",
            Name = "value",
            Attributes = ["Marker(typeof(Contoso.Data.Json))"]
        };

        var result = _printer.Print(
            new CSharpTypePrintRequest(
                type,
                primaryConstructorParameters: [parameter]),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "public global::Json.Node GetNode();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryConstructorAttributeNameRemainsQualified()
    {
        var type = CreateEmptyType("Samples", "Host");
        var method = CreateMethod("Get");
        method.SignatureModel!.ReturnType = "B.Marker";
        type.Members.Add(method);
        var parameter = new ApiParameter
        {
            Type = "int",
            Name = "value",
            Attributes = ["External.Marker"]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            primaryConstructorParameters: [parameter]));

        Assert.Contains(
            "public class Host([External.Marker] int value)",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("using External;", result.Source, StringComparison.Ordinal);
        Assert.Contains("using B;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Marker Get();", result.Source, StringComparison.Ordinal);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void UnitWideAttributeSuffixCollisionPreventsUnsafeImports()
    {
        var host = CreateEmptyType("App", "Host");
        var method = CreateMethod("GetWidget");
        method.SignatureModel!.ReturnType = "External.Widget";
        host.Members.Add(method);
        var parameter = new ApiParameter
        {
            Type = "int",
            Name = "value",
            Attributes = ["External.Marker"]
        };

        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "Collision.MarkerAttribute";
        var handler = CreateEmptyType("App", "Handler");
        handler.Kind = "delegate";
        handler.Members.Add(invoke);

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    host,
                    primaryConstructorParameters: [parameter]),
                new CSharpTypePrintRequest(handler)
            ]);

        Assert.DoesNotContain("using External;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Collision;", result.Source, StringComparison.Ordinal);
        Assert.Contains("[External.Marker] int value", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "delegate Collision.MarkerAttribute Handler();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SameNamespaceAttributeSuffixCollisionPreventsUnsafeImport()
    {
        var host = CreateEmptyType("App", "Host");
        var method = CreateMethod("GetWidget");
        method.SignatureModel!.ReturnType = "External.Widget";
        host.Members.Add(method);
        var parameter = new ApiParameter
        {
            Type = "int",
            Name = "value",
            Attributes = ["App.Marker"]
        };

        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "Collision.MarkerAttribute";
        var handler = CreateEmptyType("App", "Handler");
        handler.Kind = "delegate";
        handler.Members.Add(invoke);

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    host,
                    primaryConstructorParameters: [parameter]),
                new CSharpTypePrintRequest(handler)
            ]);

        Assert.Contains("using External;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Collision;", result.Source, StringComparison.Ordinal);
        Assert.Contains("[Marker] int value", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "delegate Collision.MarkerAttribute Handler();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnitWidePrimaryAttributeSuffixCollisionIsSymmetric()
    {
        var host = CreateEmptyType("App", "Host");
        var getLeft = CreateMethod("GetLeft");
        getLeft.SignatureModel!.ReturnType = "A.Left";
        host.Members.Add(getLeft);
        var hostParameter = new ApiParameter
        {
            Type = "int",
            Name = "value",
            Attributes = ["A.Marker"]
        };

        var worker = CreateEmptyType("App", "Worker");
        var getRight = CreateMethod("GetRight");
        getRight.SignatureModel!.ReturnType = "B.Right";
        worker.Members.Add(getRight);
        var workerParameter = new ApiParameter
        {
            Type = "int",
            Name = "value",
            Attributes = ["B.MarkerAttribute"]
        };

        var result = _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    host,
                    primaryConstructorParameters: [hostParameter]),
                new CSharpTypePrintRequest(
                    worker,
                    primaryConstructorParameters: [workerParameter])
            ]);

        Assert.DoesNotContain("using A;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using B;", result.Source, StringComparison.Ordinal);
        Assert.Contains("[A.Marker] int value", result.Source, StringComparison.Ordinal);
        Assert.Contains("[B.MarkerAttribute] int value", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInterfaceDualProvenanceRemainsQualified()
    {
        var property = new ApiMember
        {
            Name = "Contracts.IValue.Value",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Contracts.IValue",
                MemberName = "Contracts.IValue.Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var type = CreateEmptyType("App", "Widget");
        type.Interfaces.Add("Contracts.IValue");
        type.Members.Add(property);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "Contracts.IValue Contracts.IValue.Value",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IValue IValue.Value",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInterfaceCollisionKeepsBothReferencesQualified()
    {
        var property = new ApiMember
        {
            Name = "Contracts.IValue.Value",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Other.IValue",
                MemberName = "Contracts.IValue.Value",
                Accessors = [new ApiAccessor { Kind = "get" }]
            }
        };
        var sibling = CreateMethod("GetThing");
        sibling.SignatureModel!.ReturnType = "Contracts.Thing";
        var type = CreateEmptyType("App", "Widget");
        type.Interfaces.Add("Contracts.IValue");
        type.Members.Add(property);
        type.Members.Add(sibling);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.DoesNotContain("using Contracts;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using Other;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "Other.IValue Contracts.IValue.Value",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void KeptQualifiedReferenceIsNotRewrittenByShorterPrefix()
    {
        var type = CreateEmptyType("App", "Widget");
        var method = CreateMethod("Get");
        method.SignatureModel!.ReturnType = "A.B";
        method.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "A.B.C",
            Name = "value"
        });
        type.Members.Add(method);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public B Get(A.B.C value);",
            result.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("public B Get(B.C value);", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenCustomAttributesStillContributeBindingEvidence()
    {
        var type = CreateEmptyType("App", "Widget");
        type.Attributes = ["App.Foo.MarkerAttribute"];
        var method = CreateMethod("Get");
        method.SignatureModel!.ReturnType = "Foo.Bar.Baz";
        type.Members.Add(method);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions
            {
                IncludeCustomAttributes = false,
                IncludeUsings = false
            });

        Assert.DoesNotContain("MarkerAttribute", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public global::Foo.Bar.Baz Get();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenCustomAttributeDoesNotReportAnEmittedRootConflict()
    {
        var root = CreateEmptyType("", "Foo");
        var type = CreateEmptyType("App", "Widget");
        type.Attributes = ["Foo.Bar.Marker"];

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(root), new CSharpTypePrintRequest(type)],
            new CSharpTypePrintOptions { IncludeCustomAttributes = false });

        Assert.DoesNotContain("Marker", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Type name 'Foo.Bar.Marker' conflicts with global type 'Foo'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ImportedHiddenAttributeNamespacePreventsConflictingSignatureShortening()
    {
        var type = CreateEmptyType("App", "Widget");
        var getFoo = CreateMethod("GetFoo");
        getFoo.SignatureModel!.ReturnType = "A.Foo";
        getFoo.Attributes = ["B.Foo"];
        var getOther = CreateMethod("GetOther");
        getOther.SignatureModel!.ReturnType = "B.Other";
        type.Members.Add(getFoo);
        type.Members.Add(getOther);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions { IncludeCustomAttributes = false });

        Assert.DoesNotContain("using A;", result.Source, StringComparison.Ordinal);
        Assert.Contains("using B;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public A.Foo GetFoo();", result.Source, StringComparison.Ordinal);
        Assert.Contains("public Other GetOther();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AttributeTypeIsNotImportedAsNestedTypeNamespace()
    {
        var type = CreateEmptyType("App", "Widget");
        type.Attributes = ["N.Outer"];
        var method = CreateMethod("Get");
        method.SignatureModel!.ReturnType = "N.Outer.Inner";
        type.Members.Add(method);

        var result = _printer.Print(
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintOptions { IncludeCustomAttributes = false });

        Assert.DoesNotContain("using N.Outer;", result.Source, StringComparison.Ordinal);
        Assert.Contains("public N.Outer.Inner Get();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalTypeConflictingWithNamespaceDeclarationReportsDiagnostic()
    {
        var root = CreateEmptyType("", "Foo");
        var namespaced = CreateEmptyType("Foo.Bar", "Worker");

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(root), new CSharpTypePrintRequest(namespaced)]);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Namespace root 'Foo' conflicts with global type 'Foo'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalTypeConflictingWithUsingReportsDiagnostic()
    {
        var system = CreateEmptyType("", "System");

        var result = _printer.Print(
            new CSharpTypePrintRequest(system),
            new CSharpTypePrintOptions
            {
                Usings = ["System.Text"]
            });

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Namespace root 'System' conflicts with global type 'System'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void NestedTypesAndPrimaryConstructorsRenderInFileScopedNamespaceUnits()
    {
        var nested = CreateEmptyType("Samples", "Nested");
        var outer = CreateEmptyType("Samples", "Outer`1");
        outer.MetadataName = "Outer`1";
        outer.TypeParameters = [new TypeParameter { Name = "T", Constraints = ["class"] }];
        var request = new CSharpTypePrintRequest(
            outer,
            primaryConstructorParameters: [new ApiParameter { Type = "T", Name = "value" }],
            nestedTypes: [new CSharpTypePrintRequest(nested)]);

        var result = _printer.Print(request);

        Assert.Equal(
            """
            namespace Samples;

            public class Outer<T>(T value) where T : class
            {
                public class Nested
                {
                }
            }
            """,
            result.Units[0].Source);
    }

    [Fact]
    public void NestedTypeWithEmptyMetadataNamespaceInheritsContainingNamespace()
    {
        var nested = CreateEmptyType(null, "Nested");
        var outer = CreateEmptyType("Samples", "Outer");

        var result = _printer.Print(new CSharpTypePrintRequest(
            outer,
            nestedTypes: [new CSharpTypePrintRequest(nested)]));

        Assert.Contains("public class Nested", Assert.Single(result.Units).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void EnumAndDelegateRequestsUseTheirLanguageDeclarations()
    {
        var value = new ApiMember
        {
            Name = "One",
            Kind = "field",
            ReturnType = "int"
        };
        var enumType = new ApiType
        {
            Namespace = "Samples",
            Name = "Choice",
            Kind = "enum",
            Members = [value]
        };
        var invoke = new ApiMember
        {
            Name = "Invoke",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "Invoke",
                Parameters = [new ApiParameter { Type = "string", Name = "value" }]
            }
        };
        var delegateType = new ApiType
        {
            Namespace = "Samples",
            Name = "Converter`1",
            MetadataName = "Converter`1",
            Accessibility = "internal",
            Kind = "delegate",
            TypeParameters =
            [
                new TypeParameter
                {
                    Name = "event",
                    Variance = "in",
                    Constraints = ["System.IEquatable<event>"]
                }
            ],
            Members = [invoke]
        };

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(
                enumType,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        value,
                        CSharpBodyPolicy.Full,
                        new CSharpFieldInitializer("1"))
                ]),
            new CSharpTypePrintRequest(delegateType)
        ]);

        Assert.Contains("    public enum Choice\n    {\n        One = 1\n    }", result.Units[0].Source, StringComparison.Ordinal);
        Assert.Contains(
            "    internal delegate int Converter<in @event>(string value) where @event : System.IEquatable<@event>;",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateReturnAttributesAreRenderedAndPlanned()
    {
        var external = CreateEmptyType("", "External");
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "bool";
        invoke.SignatureModel.ReturnAttributes = ["External.Marker"];
        var delegateType = CreateEmptyType("Samples", "Predicate");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(external), new CSharpTypePrintRequest(delegateType)],
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified
            });

        Assert.Contains(
            "[return: global::External.Marker]\n    public delegate bool Predicate();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredNamespaceShortensDelegateSignatureTypes()
    {
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "External.Result";
        invoke.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "External.Input",
            Name = "value"
        });
        var delegateType = CreateEmptyType("Samples", "Handler");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);

        var result = _printer.Print(
            new CSharpTypePrintRequest(delegateType),
            new CSharpTypePrintOptions { Usings = ["External"] });

        Assert.Contains("using External;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public delegate Result Handler(Input value);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateConstraintStaysQualifiedWithDistinctConfiguredUsing()
    {
        var invoke = CreateMethod("Invoke");
        var delegateType = CreateEmptyType("Samples", "Handler`1");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);
        delegateType.TypeParameters =
        [
            new TypeParameter
            {
                Name = "T",
                Constraints = ["Lib.Exception"]
            }
        ];

        var result = _printer.Print(
            new CSharpTypePrintRequest(delegateType),
            new CSharpTypePrintOptions { Usings = ["System"] });

        Assert.Equal(["System"], result.Usings);
        Assert.Contains(
            "public delegate void Handler<T>() where T : Lib.Exception;",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateSignatureTypeStaysQualifiedWithDistinctConfiguredUsing()
    {
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "Lib.Exception";
        var delegateType = CreateEmptyType("Samples", "Callback");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);

        var result = _printer.Print(
            new CSharpTypePrintRequest(delegateType),
            new CSharpTypePrintOptions { Usings = ["System"] });

        Assert.Equal(["System"], result.Usings);
        Assert.Contains(
            "public delegate Lib.Exception Callback();",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateSignatureTypesStayQualifiedAcrossDistinctDerivedUsings()
    {
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "Alpha.Result";
        invoke.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "Beta.Input",
            Name = "value"
        });
        var delegateType = CreateEmptyType("Samples", "Handler");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);

        var result = _printer.Print(new CSharpTypePrintRequest(delegateType));

        Assert.Empty(result.Usings);
        Assert.Contains(
            "public delegate Alpha.Result Handler(Beta.Input value);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateParameterAttributesRemainQualified()
    {
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "Contracts.Marker";
        invoke.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "string",
            Name = "value",
            Attributes = ["Attributes.Marker"]
        });
        var delegateType = CreateEmptyType("Samples", "Handler");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);

        var result = _printer.Print(new CSharpTypePrintRequest(delegateType));

        Assert.Empty(result.Usings);
        Assert.Contains(
            "public delegate Contracts.Marker Handler([Attributes.Marker] string value);",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegateAttributeDoesNotEraseSameTypeSignatureEvidence()
    {
        var invoke = CreateMethod("Invoke");
        invoke.SignatureModel!.ReturnType = "A.Foo";
        invoke.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "B.Foo",
            Name = "value",
            Attributes = ["B.Foo"]
        });
        invoke.SignatureModel.Parameters.Add(new ApiParameter
        {
            Type = "B.Other",
            Name = "other"
        });
        var delegateType = CreateEmptyType("Samples", "Handler");
        delegateType.Kind = "delegate";
        delegateType.Members.Add(invoke);

        var result = _printer.Print(new CSharpTypePrintRequest(delegateType));

        Assert.DoesNotContain("using A;", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("using B;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public delegate A.Foo Handler([B.Foo] B.Foo value, B.Other other);",
            result.Source,
            StringComparison.Ordinal);
    }
}
