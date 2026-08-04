using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpTypePrinterTests
{
    readonly CSharpTypePrinter _printer = new();

    [Fact]
    public void SkeletonPrintsApiProposalStyleSource()
    {
        var type = new ApiType
        {
            Namespace = "System.Text",
            Name = "StringBuilder",
            Kind = "class",
            IsSealed = true,
            Members =
            [
                new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = "this text must not be used",
                    SignatureModel = new ApiSignature()
                },
                new ApiMember
                {
                    Name = "Append",
                    Kind = "method",
                    Signature = "this text must not be used",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "System.Text.StringBuilder",
                        MemberName = "Append",
                        Parameters = [new ApiParameter { Type = "string?", Name = "value" }]
                    }
                },
                new ApiMember
                {
                    Name = "ToString",
                    Kind = "method",
                    IsOverride = true,
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "string",
                        MemberName = "ToString"
                    }
                }
            ]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Equal("System.Text", unit.Namespace);
        Assert.Equal(
            """
            namespace System.Text;

            public sealed class StringBuilder
            {
                public StringBuilder();
                public StringBuilder Append(string? value);
                public override string ToString();
            }
            """,
            unit.Source);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void BatchGroupsTypesIntoNamespaceSourceUnits()
    {
        var requests = new[]
        {
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "First")),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Third")),
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Second"))
        };

        var result = _printer.PrintBatch(requests);

        Assert.Collection(
            result.Units,
            unit =>
            {
                Assert.Equal("Samples", unit.Namespace);
                Assert.Equal(
                    """
                    namespace Samples
                    {
                        public class First
                        {
                        }

                        public class Second
                        {
                        }
                    }
                    """,
                    unit.Source);
            },
            unit =>
            {
                Assert.Equal("Other", unit.Namespace);
                Assert.Contains("public class Third", unit.Source, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void GlobalNamespaceOmitsNamespaceDeclaration()
    {
        var type = CreateEmptyType(null, "GlobalType");

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Null(unit.Namespace);
        Assert.Equal(
            """
            public class GlobalType
            {
            }
            """,
            unit.Source);
    }

    [Fact]
    public void KeywordNamespaceSegmentsAreEscaped()
    {
        var type = CreateEmptyType("Samples.event", "Widget");

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        var unit = Assert.Single(result.Units);
        Assert.Equal("Samples.event", unit.Namespace);
        Assert.StartsWith("namespace Samples.@event;", unit.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedMetadataTypeNamesAreRenderedAsSafeIdentifiers()
    {
        var type = CreateEmptyType("Samples", "<>c");
        type.MetadataName = "<>c";

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("public class ___c", Assert.Single(result.Units).Source, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedGenericMetadataNamesDoNotRequireAnAritySuffix()
    {
        var type = CreateEmptyType("Samples", "<>A{00000040}`3");
        type.MetadataName = "<>A{00000040}`3";
        type.TypeParameters =
        [
            new TypeParameter { Name = "T1" },
            new TypeParameter { Name = "T2" },
            new TypeParameter { Name = "T3" }
        ];

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public class ___A_00000040_<T1, T2, T3>",
            Assert.Single(result.Units).Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StubPropertyRendersExplicitAccessorBodies()
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
                    new ApiAccessor { Kind = "get" },
                    new ApiAccessor { Kind = "set" }
                ]
            }
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(property);

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    property,
                    CSharpBodyPolicy.Stub,
                    new CSharpPropertyBody(CSharpAccessorBody.Throw, CSharpAccessorBody.Throw))
            ]));

        Assert.Contains(
            """
                public int Value
                {
                    get
                    {
                        throw null;
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
    public void StubFieldLikeEventFailsClosed()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Changed",
            Kind = "event",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Changed"
            }
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type, CSharpBodyPolicy.Stub)));

        Assert.Contains("does not support body policy 'Stub'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FullExplicitInterfaceEventRendersTypedAccessorBodies()
    {
        var explicitEvent = new ApiMember
        {
            Name = "Samples.IEvents.Changed",
            Kind = "explicit-interface-implementation",
            IsStatic = true,
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Samples.IEvents.Changed",
                Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" }
                ]
            }
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(explicitEvent);

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    explicitEvent,
                    CSharpBodyPolicy.Full,
                    new CSharpEventBody(
                        CSharpAccessorBody.Block("_changed += value;"),
                        CSharpAccessorBody.Block("_changed -= value;")))
            ]));

        Assert.Contains(
            """
                static event EventHandler Samples.IEvents.Changed
                {
                    add
                    {
                        _changed += value;
                    }
                    remove
                    {
                        _changed -= value;
                    }
                }
            """,
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitInterfaceEventSkeletonFailsClosed()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Samples.IEvents.Changed",
            Kind = "explicit-interface-implementation",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Samples.IEvents.Changed",
                Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" }
                ]
            }
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("requires add/remove bodies", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StubFieldFailsClosed()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Value",
            Kind = "field",
            ReturnType = "int"
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type, CSharpBodyPolicy.Stub)));

        Assert.Contains("does not support body policy 'Stub'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StubConstructorOnPrimaryConstructorTypeCallsThis()
    {
        var constructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            SignatureModel = new ApiSignature()
        };
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(constructor);

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            members: [constructor],
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    constructor,
                    CSharpBodyPolicy.Stub,
                    new CSharpBlockBody(
                        "throw null;",
                        new CSharpConstructorInitializer(
                            CSharpConstructorInitializerKind.This,
                            ["default"])))
            ],
            primaryConstructorParameters: [new ApiParameter { Type = "int", Name = "value" }]));

        Assert.Contains(
            "public Widget() : this(default) { throw null; }",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("System.Diagnostics.DebuggerDisplay(\"{X} : {Y}\")")]
    [InlineData("System.ComponentModel.Description(\"pick where valid\")")]
    public void PrimaryConstructorParametersIgnoreAttributeText(string attribute)
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Point",
            Kind = "record",
            Attributes = [attribute]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            primaryConstructorParameters: [new ApiParameter { Type = "int", Name = "value" }]),
            new CSharpTypePrintOptions { IncludeCustomAttributes = true });

        Assert.Contains(
            $"[{attribute}]\npublic record Point(int value)",
            Assert.Single(result.Units).Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SkeletonPrefersStructuredGenericSignature()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Converter`1",
            MetadataName = "Converter`1",
            Kind = "class",
            TypeParameters = [new TypeParameter { Name = "T" }],
            Members =
            [
                new ApiMember
                {
                    Name = "Convert",
                    Kind = "method",
                    Signature = "broken compatibility signature",
                    SignatureModel = new ApiSignature
                    {
                        ReturnType = "TResult",
                        MemberName = "Convert<TResult>",
                        TypeParameters = [new TypeParameter { Name = "TResult", Constraints = ["class"] }],
                        Parameters = [new ApiParameter { Type = "T", Name = "value" }]
                    }
                }
            ]
        };

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public class Converter<T>",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public TResult Convert<TResult>(T value) where TResult : class;",
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Converter<T><T>", result.Units[0].Source, StringComparison.Ordinal);
        Assert.DoesNotContain("broken compatibility signature", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredParameterAttributesRepresentMetadataOnlyDefaults()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "GetTicks",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "long",
                MemberName = "GetTicks",
                Parameters =
                [
                    new ApiParameter
                    {
                        Type = "System.DateTime",
                        Name = "when",
                        Attributes =
                        [
                            "System.Runtime.InteropServices.Optional",
                            "System.Runtime.CompilerServices.DateTimeConstant(0)"
                        ],
                        HasDefault = true
                    }
                ]
            }
        });

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public long GetTicks([Optional, DateTimeConstant(0)] DateTime when);",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedGenericCanonicalIdentityUsesRawFallbackMetadataNames()
    {
        var first = CreateEmptyType("Samples", "<State>d__0`1");
        first.TypeParameters = [new TypeParameter { Name = "T" }];
        var second = CreateEmptyType("Samples", "<State>d__0`2");
        second.TypeParameters =
        [
            new TypeParameter { Name = "T" },
            new TypeParameter { Name = "U" }
        ];

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(first),
            new CSharpTypePrintRequest(second)
        ]);

        Assert.Contains("public class __State_d__0<T>", result.Units[0].Source, StringComparison.Ordinal);
        Assert.Contains("public class __State_d__0<T, U>", result.Units[0].Source, StringComparison.Ordinal);
    }
    [Fact]
    public void SkeletonMatchesCSharpFormatter()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(new ApiMember
        {
            Name = "Create",
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "Samples.Widget",
                MemberName = "Create"
            }
        });
        var formatter = new CSharpFormatter(new CSharpFormatOptions
        {
            TypeNamePolicy = CSharpTypeNamePolicy.ContextualShort,
            ContainingNamespace = "Samples",
            NamespacePolicy = CSharpNamespacePolicy.Omit,
            TerminateMemberDeclaration = true
        });
        var expectedDeclaration = formatter.FormatTypeUnit(type, type.Members);

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Equal($"namespace Samples;\n\n{expectedDeclaration.Text}", result.Units[0].Source);
        Assert.Equal(expectedDeclaration.Diagnostics, result.Diagnostics.Select(diagnostic => diagnostic.Message));
    }

    [Fact]
    public void NonSkeletonPolicyFailsInsteadOfDroppingBodies()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members.Add(CreateMethod("Run"));
        var request = new CSharpTypePrintRequest(type, CSharpBodyPolicy.Full);

        var exception = Assert.Throws<NotSupportedException>(() => _printer.Print(request));

        Assert.Contains("requires a body provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPolicyOverridesTypeDefault()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Run");
        type.Members.Add(member);
        var request = new CSharpTypePrintRequest(
            type,
            CSharpBodyPolicy.Full,
            memberPolicyOverrides: [new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton)]);

        var result = _printer.Print(request);

        Assert.Contains("public void Run();", result.Units[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void NonSkeletonMemberPolicyFailsInsteadOfDroppingBody()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Run");
        type.Members.Add(member);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides: [new CSharpMemberPolicy(member, CSharpBodyPolicy.Full)]);

        var exception = Assert.Throws<NotSupportedException>(() => _printer.Print(request));

        Assert.Contains("'Full' for 'Run' requires a body provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPolicyMustTargetSelectedMember()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var selected = CreateMethod("Selected");
        var omitted = CreateMethod("Omitted");
        type.Members.AddRange([selected, omitted]);
        var request = new CSharpTypePrintRequest(
            type,
            members: [selected],
            memberPolicyOverrides: [new CSharpMemberPolicy(omitted, CSharpBodyPolicy.Skeleton)]);

        var exception = Assert.Throws<ArgumentException>(() => _printer.Print(request));

        Assert.Contains("is not in the selected member set", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPolicyOverridesMustBeUnique()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Run");
        type.Members.Add(member);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton),
                new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton)
            ]);

        var exception = Assert.Throws<ArgumentException>(() => _printer.Print(request));

        Assert.Contains("multiple policy overrides", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestSnapshotsMembersBeforeValidation()
    {
        var member = CreateMethod("Run");
        var changingMembers = new DifferentEachEnumerationList<ApiMember>(member, null!);

        var request = new CSharpTypePrintRequest(
            CreateEmptyType("Samples", "Widget"),
            members: changingMembers);

        Assert.Same(member, Assert.Single(request.Members!));
    }

    [Fact]
    public void RequestSnapshotsMemberPoliciesBeforeValidation()
    {
        var member = CreateMethod("Run");
        var policy = new CSharpMemberPolicy(member, CSharpBodyPolicy.Skeleton);
        var changingPolicies = new DifferentEachEnumerationList<CSharpMemberPolicy>(policy, null!);

        var request = new CSharpTypePrintRequest(
            CreateEmptyType("Samples", "Widget"),
            members: [member],
            memberPolicyOverrides: changingPolicies);

        Assert.Same(policy, Assert.Single(request.MemberPolicyOverrides));
    }

    [Fact]
    public void UnsupportedTypeKindFailsInsteadOfEmittingInvalidSkeleton()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Shape",
            Kind = "union"
        };

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("type kind 'union'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FullAndStubBodiesAreRenderedFromMemberPolicies()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var full = CreateMethod("Full");
        var stub = CreateMethod("Stub");
        type.Members.AddRange([full, stub]);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(full, CSharpBodyPolicy.Full, new CSharpBlockBody("return;")),
                new CSharpMemberPolicy(stub, CSharpBodyPolicy.Stub)
            ]);

        var result = _printer.Print(request);

        Assert.Contains(
            """
                public void Full()
                {
                    return;
                }
            """,
            result.Units[0].Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public void Stub() { throw null; }",
            result.Units[0].Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FullBodyModifiersDoNotLeakIntoSkeletons()
    {
        var type = CreateEmptyType("Samples", "Worker");
        var member = CreateMethod("Run");
        member.SignatureModel!.ReturnType = "System.Threading.Tasks.Task";
        type.Members.Add(member);
        var full = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    member,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody("return;")
                    {
                        RequiresAsyncModifier = true,
                        RequiresUnsafeModifier = true
                    })
            ]));
        var skeleton = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains(
            "public unsafe async Task Run()",
            full.Units[0].Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public Task Run();",
            skeleton.Units[0].Source,
            StringComparison.Ordinal);
        Assert.False(member.IsAsync);
        Assert.False(member.IsUnsafe);
    }

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
                ReturnType = "int",
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
    public void NestedTypeFailsWithoutItsDeclaringType()
    {
        var type = CreateEmptyType("Samples", "Outer.Inner");
        type.MetadataName = "Outer+Inner";

        var exception = Assert.Throws<NotSupportedException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("requires its declaring type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateTypeRequestsFailInsteadOfEmittingDuplicateDeclarations()
    {
        var type = CreateEmptyType("Samples", "Widget");
        var requests = new[]
        {
            new CSharpTypePrintRequest(type),
            new CSharpTypePrintRequest(type)
        };

        var exception = Assert.Throws<ArgumentException>(() => _printer.PrintBatch(requests));

        Assert.Contains("duplicate C# type 'Samples.Widget'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalMetadataIdentityRejectsDuplicateGenericDeclarations()
    {
        var first = CreateEmptyType("Samples", "Converter`1");
        first.MetadataName = "Converter`1";
        first.TypeParameters = [new TypeParameter { Name = "T" }];
        var second = CreateEmptyType("Samples", "Other`1");
        second.MetadataName = "Converter`1";
        second.TypeParameters = [new TypeParameter { Name = "U" }];

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.PrintBatch(
            [
                new CSharpTypePrintRequest(first),
                new CSharpTypePrintRequest(second)
            ]));

        Assert.Contains("duplicate C# type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpSpelledGenericTypeNameFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Converter<T>");
        type.TypeParameters = [new TypeParameter { Name = "T" }];

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("must use a metadata name", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Converter", 1)]
    [InlineData("Converter`2", 1)]
    [InlineData("Converter`x", 1)]
    public void InconsistentGenericMetadataArityFailsExplicitly(string name, int parameterCount)
    {
        var type = CreateEmptyType("Samples", name);
        type.TypeParameters = Enumerable.Range(0, parameterCount)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains(
            name.Contains('`', StringComparison.Ordinal)
                ? "inconsistent metadata arity"
                : "requires metadata arity",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedTypeNameFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Name = null!;

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("non-empty type name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedMemberCollectionFailsExplicitly()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.Members = null!;

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("null member collection", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The snapshot has to carry the classified reference-/value-type fact, not just the
    /// constraint strings. An inheriting member must restate that fact -- it decides
    /// whether `T?` binds as a nullable reference type or Nullable&lt;T&gt; -- so a
    /// snapshot that drops it renders an override that does not compile (CS0115/CS0453),
    /// silently, across the whole type-printer path.
    /// </summary>
    [Fact]
    public void SnapshotTypeForRendering_CarriesTheClassifiedTypeParameterKind()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["Samples.BaseType"],
            TypeKind = TypeParameterTypeKind.ReferenceType
        };
        var method = new ApiMember
        {
            Name = "Pick",
            Kind = "method",
            IsOverride = true,
            Signature = "this text must not be used",
            SignatureModel = new ApiSignature
            {
                ReturnType = "T?",
                MemberName = "Pick<T>",
                TypeParameters = [typeParameter],
                Parameters = [new ApiParameter { Type = "T?", Name = "value" }]
            }
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Holder",
            Kind = "class",
            Members = [method]
        };

        var snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Equal(
            TypeParameterTypeKind.ReferenceType,
            snapshot.Members[0].SignatureModel!.TypeParameters[0].TypeKind);
    }

    [Fact]
    public void RenderingSnapshotDoesNotRetainMutableMetadataAliases()
    {
        var typeParameter = new TypeParameter
        {
            Name = "T",
            Constraints = ["System.IDisposable"]
        };
        var parameter = new ApiParameter
        {
            Attributes = ["ParamMarker"],
            Name = "value",
            Type = "T"
        };
        var accessor = new ApiAccessor
        {
            Kind = "get",
            ReturnAttributes = ["AccessorMarker"]
        };
        var method = new ApiMember
        {
            Name = "Transform",
            Kind = "method",
            Attributes = ["MemberMarker"],
            SignatureModel = new ApiSignature
            {
                ReturnType = "T",
                ReturnAttributes = ["ReturnMarker"],
                MemberName = "Transform",
                Parameters = [parameter]
            }
        };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "string",
                MemberName = "Value",
                IsRequired = true,
                Accessors = [accessor]
            }
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Container`1",
            MetadataName = "Container`1",
            Kind = "class",
            Attributes = ["TypeMarker"],
            BaseType = "Samples.BaseType",
            Interfaces = ["Samples.IContract"],
            TypeParameters = [typeParameter],
            Members = [method, property]
        };

        var snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        type.Namespace = "Mutated";
        type.Name = "Mutated";
        type.MetadataName = "Mutated";
        type.Kind = "enum";
        type.Attributes[0] = "Mutated";
        type.BaseType = "Mutated";
        type.Interfaces[0] = "Mutated";
        typeParameter.Name = "Mutated";
        typeParameter.Constraints[0] = "Mutated";
        method.Name = "Mutated";
        method.Kind = "field";
        method.Attributes[0] = "Mutated";
        method.SignatureModel!.ReturnType = "Mutated";
        method.SignatureModel.ReturnAttributes[0] = "Mutated";
        method.SignatureModel.MemberName = "Mutated";
        parameter.Attributes[0] = "Mutated";
        parameter.Name = "Mutated";
        parameter.Type = "Mutated";
        property.SignatureModel!.ReturnType = "Mutated";
        property.SignatureModel.MemberName = "Mutated";
        property.SignatureModel.IsRequired = false;
        accessor.Kind = "set";
        accessor.ReturnAttributes[0] = "Mutated";

        var rendered = CSharpDeclarationWriter.RenderTypeUnit(
            snapshot,
            snapshot.Members,
            new CSharpDeclarationOptions
            {
                TypeNameMode = CSharpTypeNameMode.ContextualShort,
                NamespaceMode = CSharpNamespaceMode.Omit,
                IncludeCustomAttributes = true
            });

        Assert.Contains(
            "[TypeMarker]\npublic class Container<T> : Samples.BaseType, Samples.IContract where T : System.IDisposable",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[MemberMarker]\n    [return: ReturnMarker]\n    public T Transform([ParamMarker] T value);",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public required string Value { [return: AccessorMarker] get; }",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Mutated", rendered.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void NullCallsResolveToExplicitArgumentFailures()
    {
        Assert.Throws<ArgumentNullException>(() => _printer.Print(null!));
        Assert.Throws<ArgumentNullException>(() => _printer.PrintBatch(null!));
    }

    [Fact]
    public void SourceDefaultsToUsingsWithoutPragmaOrAssemblyAttributes()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["System.Collections.Generic", "System"]
            });

        Assert.Equal(
            "using System;\nusing System.Collections.Generic;\nnamespace Samples;\n\npublic class Widget\n{\n}\n",
            result.Source);
        Assert.DoesNotContain("#pragma", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("[assembly:", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceOmitsUsingsWhenIncludeUsingsIsFalse()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["System"],
                IncludeUsings = false
            });

        Assert.DoesNotContain("using System;", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceEmitsPragmaAndAssemblyAndModuleAttributesWhenRequested()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                EmitPragmaWarningDisable = true,
                AssemblyAttributes = ["System.Reflection.AssemblyMetadata(\"k\", \"v\")"],
                ModuleAttributes = ["System.Security.UnverifiableCode"],
                Usings = ["System"]
            });

        Assert.StartsWith(
            "#pragma warning disable\n"
            + "[assembly: System.Reflection.AssemblyMetadata(\"k\", \"v\")]\n"
            + "[module: System.Security.UnverifiableCode]\n"
            + "using System;\n",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SourceEscapesAndDeduplicatesUsings()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["System", "System", "Some.namespace.Value"]
            });

        Assert.Contains("using Some.@namespace.Value;", result.Source, StringComparison.Ordinal);
        Assert.Single(
            result.Source.Split('\n'),
            line => line == "using System;");
    }

    [Fact]
    public void SourceUsesBlockScopedNamespaceForMultipleRequests()
    {
        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "First")),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Second"))
        ]);

        Assert.Contains("namespace Samples\n{\n", result.Source, StringComparison.Ordinal);
        Assert.Contains("namespace Other\n{\n", result.Source, StringComparison.Ordinal);
    }

    static ApiType CreateEmptyType(string? @namespace, string name)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class"
        };

    static ApiMember CreateMethod(string name)
        => new()
        {
            Name = name,
            Kind = "method",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = name
            }
        };

    sealed class DifferentEachEnumerationList<T>(T first, T later) : IReadOnlyList<T>
    {
        int _enumerationCount;

        public int Count => 1;

        public T this[int index] => index == 0 ? first : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<T> GetEnumerator()
        {
            yield return _enumerationCount++ == 0 ? first : later;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
