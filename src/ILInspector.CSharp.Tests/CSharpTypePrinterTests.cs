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
                    namespace Samples;

                    public class First
                    {
                    }

                    public class Second
                    {
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
            $"[{attribute}] public record Point(int value)",
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
            "public long GetTicks([System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(0)] System.DateTime when);",
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
    public void NestedTypesAndPrimaryConstructorsRenderInBlockNamespaceUnits()
    {
        var nested = CreateEmptyType("Samples", "Nested");
        var outer = CreateEmptyType("Samples", "Outer`1");
        outer.MetadataName = "Outer`1";
        outer.TypeParameters = [new TypeParameter { Name = "T", Constraints = ["class"] }];
        var request = new CSharpTypePrintRequest(
            outer,
            primaryConstructorParameters: [new ApiParameter { Type = "T", Name = "value" }],
            nestedTypes: [new CSharpTypePrintRequest(nested)]);

        var result = _printer.Print(
            request,
            new CSharpTypePrintOptions { NamespaceStyle = CSharpNamespaceStyle.BlockScoped });

        Assert.Equal(
            """
            namespace Samples
            {
                public class Outer<T>(T value) where T : class
                {
                    public class Nested
                    {
                    }
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

        Assert.Contains("public enum Choice\n{\n    One = 1\n}", result.Units[0].Source, StringComparison.Ordinal);
        Assert.Contains(
            "internal delegate int Converter<in @event>(string value) where @event : System.IEquatable<@event>;",
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
            "[TypeMarker] public class Container<T> : Samples.BaseType, Samples.IContract where T : System.IDisposable",
            rendered.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[MemberMarker] [return: ReturnMarker] public T Transform([ParamMarker] T value);",
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
