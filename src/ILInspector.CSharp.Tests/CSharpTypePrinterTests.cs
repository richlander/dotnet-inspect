using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpTypePrinterTests
{
    readonly CSharpTypePrinter _outcomePrinter = new();
    readonly SuccessfulCSharpTypePrinter _printer = new();

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
    public void SelfNameIsSharedByItsDeclarationPositions()
    {
        var instanceConstructor = new ApiMember
        {
            Name = ".ctor",
            Kind = "constructor",
            Accessibility = "public",
            SignatureModel = new ApiSignature()
        };
        var staticConstructor = new ApiMember
        {
            Name = ".cctor",
            Kind = "constructor",
            IsStatic = true,
            SignatureModel = new ApiSignature()
        };
        var finalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "finalizer",
            Signature = "void Finalize()",
            IsFinalizer = true
        };
        var type = CreateExactType(
            "Samples",
            ["extension`1"],
            [1],
            ["T"]);
        type.Members = [instanceConstructor, staticConstructor, finalizer];
        type.Name = "display-only";

        CSharpTypePrintResult printed = AssertPrinted(
            _outcomePrinter.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains("public class @extension<T>", printed.Source, StringComparison.Ordinal);
        Assert.Contains("public @extension();", printed.Source, StringComparison.Ordinal);
        Assert.Contains("static @extension();", printed.Source, StringComparison.Ordinal);
        Assert.Contains("~@extension();", printed.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("@extension<T>()", printed.Source, StringComparison.Ordinal);

        var suppressedType = CreateExactType(
            "Samples",
            ["extension`1"],
            [1],
            ["T"]);
        var suppressedFinalizer = new ApiMember
        {
            Name = "Finalize",
            Kind = "finalizer",
            Signature = "void Finalize()",
            IsFinalizer = true
        };
        suppressedType.Members = [suppressedFinalizer];
        CSharpTypePrintResult suppressed = AssertPrinted(
            _outcomePrinter.Print(
                new CSharpTypePrintRequest(
                    suppressedType,
                    CSharpBodyPolicy.Full,
                    memberPolicyOverrides:
                    [
                        new CSharpMemberPolicy(
                            suppressedFinalizer,
                            CSharpBodyPolicy.Full,
                            new CSharpBlockBody("return;")
                            {
                                SuppressDestructorSyntax = true
                            })
                    ])));

        Assert.Contains("void Finalize()", suppressed.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("~@extension", suppressed.Source, StringComparison.Ordinal);

        var delegateType = CreateExactType(
            "Samples",
            ["extension`1"],
            [1],
            ["T"],
            kind: "delegate");
        delegateType.Members =
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
        ];

        CSharpTypePrintResult delegateResult = AssertPrinted(
            _outcomePrinter.Print(new CSharpTypePrintRequest(delegateType)));

        Assert.Contains(
            "public delegate void @extension<T>();",
            delegateResult.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SelfNameFailureMakesBatchNotRendered()
    {
        ApiType literalPlus = CreateExactType("N", ["A+B"], [0], []);
        CSharpTypePrintOutcome.NotRendered singleton = AssertNotRendered(
            _outcomePrinter.Print(new CSharpTypePrintRequest(literalPlus)));
        AssertIdentifierFailure(
            Assert.Single(singleton.SelfNameFailures),
            ["A+B"],
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier);
        CSharpTypePrintOutcome.NotRendered angle = AssertNotRendered(
            _outcomePrinter.Print(new CSharpTypePrintRequest(
                CreateExactType("N", ["A<B"], [0], []))));
        AssertIdentifierFailure(
            Assert.Single(angle.SelfNameFailures),
            ["A<B"],
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier);

        var outer = CreateExactType("N", ["Outer"], [0], []);
        var nestedLiteral = CreateExactType(
            "N",
            ["Outer", "A+B"],
            [0, 0],
            []);
        CSharpTypePrintOutcome.NotRendered nested = AssertNotRendered(
            _outcomePrinter.Print(
                new CSharpTypePrintRequest(
                    outer,
                    nestedTypes:
                    [
                        new CSharpTypePrintRequest(nestedLiteral)
                    ])));
        Assert.Equal(["Outer", "A+B"], Assert.Single(nested.SelfNameFailures).Identity.Segments);

        CSharpTypePrintOutcome.NotRendered multiNamespace = AssertNotRendered(
            _outcomePrinter.PrintBatch(
            [
                new CSharpTypePrintRequest(CreateExactType("N", ["Good"], [0], [])),
                new CSharpTypePrintRequest(literalPlus),
                new CSharpTypePrintRequest(CreateExactType("Other", ["Peer"], [0], []))
            ]));
        Assert.Single(multiNamespace.SelfNameFailures);

        var replacementType = CreateExactType("N", ["Good"], [0], []);
        var replacementMethod = CreateMethod("Run");
        replacementType.Members = [replacementMethod];
        CSharpTypePrintOutcome.NotRendered replacement = AssertNotRendered(
            _outcomePrinter.PrintBatch(
            [
                new CSharpTypePrintRequest(
                    replacementType,
                    CSharpBodyPolicy.Full,
                    memberPolicyOverrides:
                    [
                        new CSharpMemberPolicy(
                            replacementMethod,
                            CSharpBodyPolicy.Full,
                            new CSharpBlockBody("return;")
                            {
                                IsReplacementTarget = true
                            })
                    ]),
                new CSharpTypePrintRequest(literalPlus)
            ]));
        Assert.Single(replacement.SelfNameFailures);

        AssertArityNotRendered(CreateExactType("N", ["Widget"], [0, 0], []));
        AssertArityNotRendered(CreateExactType("N", ["Widget`2"], [1], ["T"]));
        AssertArityNotRendered(CreateExactType("N", ["Widget`1"], [1], []));
        AssertArityNotRendered(CreateExactType("N", ["Widget`1"], [1], ["T", "U"]));
        CSharpTypePrintOutcome.NotRendered truncatedNested = AssertNotRendered(
            _outcomePrinter.Print(new CSharpTypePrintRequest(
                CreateExactType("N", ["Outer"], [0], []),
                nestedTypes:
                [
                    new CSharpTypePrintRequest(
                        CreateExactType("N", ["Outer", "Inner"], [0], []))
                ])));
        Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch>(
            Assert.Single(truncatedNested.SelfNameFailures).Reason);

        var legacyMissingIdentity = CreateEmptyType("N", "Widget");
        Assert.IsType<CSharpTypePrintOutcome.Printed>(
            _outcomePrinter.Print(new CSharpTypePrintRequest(legacyMissingIdentity)));
        var legacyNullCounts = CreateExactType("N", ["A+B"], [0], []);
        legacyNullCounts.IntroducedTypeParameterCounts = null;
        Assert.IsType<CSharpTypePrintOutcome.Printed>(
            _outcomePrinter.Print(new CSharpTypePrintRequest(legacyNullCounts)));
        var legacyEmptyCounts = CreateExactType("N", ["A+B"], [0], []);
        legacyEmptyCounts.IntroducedTypeParameterCounts = [];
        Assert.IsType<CSharpTypePrintOutcome.Printed>(
            _outcomePrinter.Print(new CSharpTypePrintRequest(legacyEmptyCounts)));

        foreach ((int[] Counts, string[] Parameters) generatedShape in new[]
        {
            (new[] { 2 }, new[] { "T", "U" }),
            (new[] { 1 }, new[] { "T", "U" }),
            (Array.Empty<int>(), new[] { "T", "U" }),
            (new[] { 0, 2 }, new[] { "T", "U" }),
            (new[] { 2 }, Array.Empty<string>()),
            (new[] { 2 }, new[] { "T", "U", "V" }),
        })
        {
            ApiType generated = CreateExactType(
                "N",
                ["<State>d__1`2"],
                generatedShape.Counts,
                generatedShape.Parameters);
            CSharpTypePrintResult generatedResult = AssertPrinted(
                _outcomePrinter.Print(new CSharpTypePrintRequest(generated)));
            Assert.Contains("_State_d__1", generatedResult.Source, StringComparison.Ordinal);
        }

        CSharpTypePrintResult mixed = AssertPrinted(
            _outcomePrinter.PrintBatch(
            [
                new CSharpTypePrintRequest(legacyMissingIdentity),
                new CSharpTypePrintRequest(CreateExactType("Other", ["class"], [0], []))
            ]));
        Assert.Contains("class @class", mixed.Source, StringComparison.Ordinal);

        var duplicate = CreateEmptyType("N", "Duplicate");
        CSharpTypePrintRequest[] refusalThenDuplicate =
        [
            new CSharpTypePrintRequest(literalPlus),
            new CSharpTypePrintRequest(duplicate),
            new CSharpTypePrintRequest(duplicate)
        ];
        AssertNotRendered(_outcomePrinter.PrintBatch(refusalThenDuplicate));
        AssertNotRendered(_outcomePrinter.PrintBatch(refusalThenDuplicate.Reverse()));

        Assert.Equal(
            [nameof(CSharpTypePrintOutcome.NotRendered.SelfNameFailures)],
            typeof(CSharpTypePrintOutcome.NotRendered)
                .GetProperties()
                .Where(property =>
                    property.DeclaringType == typeof(CSharpTypePrintOutcome.NotRendered))
                .Select(property => property.Name));
    }

    [Fact]
    public void GeneratedLegacyNameIsSharedWithTypeNameContext()
    {
        var outer = CreateExactType("N", ["Outer"], [0], []);
        var nested = CreateExactType(
            "N",
            ["Outer", "<>c__DisplayClass0_0"],
            [0, 0],
            []);
        outer.Members =
        [
            new ApiMember
            {
                Name = "Make",
                Kind = "method",
                SignatureModel = new ApiSignature
                {
                    ReturnType = "N.Outer.___c__DisplayClass0_0",
                    MemberName = "Make"
                }
            }
        ];

        CSharpTypePrintResult result = AssertPrinted(
            _outcomePrinter.Print(new CSharpTypePrintRequest(
                outer,
                nestedTypes:
                [
                    new CSharpTypePrintRequest(nested)
                ])));

        Assert.DoesNotContain("using N.Outer;", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            "public N.Outer.___c__DisplayClass0_0 Make();",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public class ___c__DisplayClass0_0",
            result.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedLegacyNamesUseRenderedSpellingForDuplicateValidation()
    {
        AssertDuplicate(
            CreateExactType("N", ["<A>d_1"], [0], []),
            CreateExactType("N", ["<A>d.1"], [0], []));
        AssertDuplicate(
            CreateExactType("N", ["<A>d-1`2"], [1], ["T"]),
            CreateExactType("N", ["<A>d_1`2"], [1], ["T"]));

        void AssertDuplicate(ApiType first, ApiType second)
        {
            var exception = Assert.Throws<ArgumentException>(
                () => _outcomePrinter.PrintBatch(
                [
                    new CSharpTypePrintRequest(first),
                    new CSharpTypePrintRequest(second)
                ]));

            Assert.Contains("duplicate C# type", exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeneratedLegacyNameUsesExactLeafInsteadOfDottedDisplayName()
    {
        var outer = CreateExactType("N", ["Outer"], [0], []);
        var nested = CreateExactType(
            "N",
            ["Outer", "<A>d__1"],
            [0, 0],
            []);
        nested.Name = "Outer.<A>d__1";
        nested.MetadataName = "Outer+<A>d__1";

        CSharpTypePrintResult result = AssertPrinted(
            _outcomePrinter.Print(new CSharpTypePrintRequest(
                outer,
                nestedTypes:
                [
                    new CSharpTypePrintRequest(nested)
                ])));

        Assert.Contains("public class __A_d__1", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("class Outer.", result.Source, StringComparison.Ordinal);
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
    public void SourceArtifactReplacesTheSelectedNestedMethodBlockOnly()
    {
        var target = CreateMethod("Run");
        var nested = CreateEmptyType("Samples", "Inner");
        nested.Members.Add(target);
        var outer = CreateEmptyType("Samples", "Outer");
        var targetRequest = new CSharpTypePrintRequest(
            nested,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    target,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody("return;") { IsReplacementTarget = true })
            ]);
        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Peer")),
            new CSharpTypePrintRequest(outer, nestedTypes: [targetRequest])
        ],
        new CSharpTypePrintOptions
        {
            EmitPragmaWarningDisable = true,
            Usings = ["System"]
        });

        var range = Assert.IsType<CSharpSourceRange>(result.SourceArtifact.ReplaceableBodyRange);
        Assert.Equal(
            "            {\n"
            + "                return;\n"
            + "            }",
            result.Source.Substring(range.Start, range.Length));

        string replacement = result.SourceArtifact.ReplaceBody(
            """
            System.Console.WriteLine(42);
            return;
            """);

        Assert.Equal(
            result.Source[..range.Start]
            + "            {\n"
            + "System.Console.WriteLine(42);\n"
            + "return;\n"
            + "            }"
            + result.Source[range.End..],
            replacement);
        Assert.Contains("public class Peer", replacement, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceArtifactReplacementPreservesMultilineLiteralBytes()
    {
        var target = CreateMethod("Run");
        var type = CreateEmptyType("Samples", "Literal");
        type.Members.Add(target);
        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    target,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody("return;") { IsReplacementTarget = true })
            ]));
        const string body = "return @\"alpha\r\n\r\nomega\";";

        string replacement = result.SourceArtifact.ReplaceBody(body);
        var range = Assert.IsType<CSharpSourceRange>(result.SourceArtifact.ReplaceableBodyRange);

        Assert.Equal(
            result.Source[..range.Start]
            + "    {\n"
            + body
            + "\n"
            + "    }"
            + result.Source[range.End..],
            replacement);
    }

    [Fact]
    public void SourceArtifactReplacementPreservesConstructorInitializer()
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
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    constructor,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody(
                        "_value = 1;",
                        new CSharpConstructorInitializer(
                            CSharpConstructorInitializerKind.Base,
                            ["1"]))
                    {
                        IsReplacementTarget = true
                    })
            ]));

        string replacement = result.SourceArtifact.ReplaceBody("_value = 2;");

        Assert.Contains("public Widget() : base(1)", replacement, StringComparison.Ordinal);
        Assert.Contains("_value = 2;", replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("_value = 1;", replacement, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceArtifactReplacesOnlyTheSelectedIndexerAccessor()
    {
        var indexer = new ApiMember
        {
            Name = "Item",
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                MemberName = "this[]",
                Parameters = [new ApiParameter { Type = "int", Name = "index" }],
                Accessors =
                [
                    new ApiAccessor { Kind = "get" },
                    new ApiAccessor { Kind = "set" }
                ]
            }
        };
        var type = CreateEmptyType("Samples", "Values");
        type.Members.Add(indexer);
        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    indexer,
                    CSharpBodyPolicy.Full,
                    new CSharpPropertyBody(
                        CSharpAccessorBody.Block("return index;") with { IsReplacementTarget = true },
                        CSharpAccessorBody.Block("_values[index] = value;")))
            ]));

        string replacement = result.SourceArtifact.ReplaceBody("return _values[index];");

        Assert.Contains("return _values[index];", replacement, StringComparison.Ordinal);
        Assert.Contains("_values[index] = value;", replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("return index;", replacement, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceArtifactReplacesOnlyTheSelectedEventAccessor()
    {
        var eventMember = new ApiMember
        {
            Name = "Changed",
            Kind = "event",
            SignatureModel = new ApiSignature
            {
                ReturnType = "System.EventHandler",
                MemberName = "Changed",
                Accessors =
                [
                    new ApiAccessor { Kind = "add" },
                    new ApiAccessor { Kind = "remove" }
                ]
            }
        };
        var type = CreateEmptyType("Samples", "Events");
        type.Members.Add(eventMember);
        var result = _printer.Print(new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    eventMember,
                    CSharpBodyPolicy.Full,
                    new CSharpEventBody(
                        CSharpAccessorBody.Block("_changed += value;"),
                        CSharpAccessorBody.Block("_changed -= value;")
                            with { IsReplacementTarget = true }))
            ]));

        string replacement = result.SourceArtifact.ReplaceBody("Remove(value);");

        Assert.Contains("_changed += value;", replacement, StringComparison.Ordinal);
        Assert.Contains("Remove(value);", replacement, StringComparison.Ordinal);
        Assert.DoesNotContain("_changed -= value;", replacement, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceArtifactFailsWhenNoBodyOrMultipleBodiesAreSelected()
    {
        var unselected = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Empty")));
        Assert.Throws<InvalidOperationException>(() => unselected.SourceArtifact.ReplaceBody("return;"));

        var first = CreateMethod("First");
        var second = CreateMethod("Second");
        var type = CreateEmptyType("Samples", "Ambiguous");
        type.Members.AddRange([first, second]);
        var request = new CSharpTypePrintRequest(
            type,
            memberPolicyOverrides:
            [
                new CSharpMemberPolicy(
                    first,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody("return;") { IsReplacementTarget = true }),
                new CSharpMemberPolicy(
                    second,
                    CSharpBodyPolicy.Full,
                    new CSharpBlockBody("return;") { IsReplacementTarget = true })
            ]);

        var exception = Assert.Throws<ArgumentException>(() => _printer.Print(request));
        Assert.Contains("at most one replacement target", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void ExactGenericTypeWithoutMetadataArityIsNotRendered()
    {
        var type = CreateEmptyType("Samples", "Widget");
        type.DefinitionName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Samples",
                    ["Widget"]))
            .Name;
        type.IntroducedTypeParameterCounts = [1];
        type.TypeParameters = [new TypeParameter { Name = "T" }];

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            _outcomePrinter.Print(new CSharpTypePrintRequest(type)));

        Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch>(
            Assert.Single(outcome.SelfNameFailures).Reason);
    }

    [Theory]
    // A canonical `N that disagrees with the parameter count is inconsistent.
    [InlineData("Converter`2", 1, "inconsistent metadata arity")]
    [InlineData("Converter`1", 2, "inconsistent metadata arity")]
    // No canonical `N at all: the name does not carry arity, whatever text
    // follows the backtick. int.TryParse used to accept a signed, padded, or
    // culture-digit count here and let the type print as if it were generic
    // (#4217).
    [InlineData("Converter", 1, "requires metadata arity")]
    [InlineData("Converter`x", 1, "requires metadata arity")]
    [InlineData("Converter`+1", 1, "requires metadata arity")]
    [InlineData("Converter`01", 1, "requires metadata arity")]
    [InlineData("Converter` 1", 1, "requires metadata arity")]
    [InlineData("Converter`\u0661", 1, "requires metadata arity")]
    [InlineData("Converter`1Extra", 1, "requires metadata arity")]
    [InlineData("Converter`65537", 1, "requires metadata arity")]
    public void InconsistentGenericMetadataArityFailsExplicitly(
        string name,
        int parameterCount,
        string expectedMessage)
    {
        var type = CreateEmptyType("Samples", name);
        type.TypeParameters = Enumerable.Range(0, parameterCount)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var exception = Assert.Throws<ArgumentException>(
            () => _printer.Print(new CSharpTypePrintRequest(type)));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The canonical bound is inclusive at 65536 — ECMA-335 gives
    /// <c>GenericParam.Number</c> a zero-based ushort — so a name at the bound is
    /// a legal arity spelling and prints.
    /// </summary>
    [Fact]
    public void CanonicalArityAtTheMetadataBoundIsAccepted()
    {
        var type = CreateEmptyType("Samples", "Converter`65536");
        type.TypeParameters = Enumerable.Range(0, 65536)
            .Select(index => new TypeParameter { Name = $"T{index}" })
            .ToList();

        var result = _printer.Print(new CSharpTypePrintRequest(type));

        Assert.Contains("class Converter<", result.Source, StringComparison.Ordinal);
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
    public void SnapshotTypeForRendering_CarriesMethodImplementationEvidence()
    {
        var facts = new ApiMethodImplementationFacts(
            Guid.NewGuid(), 0x06000001,
            System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.PinvokeImpl,
            System.Reflection.MethodImplAttributes.PreserveSig, false);
        var method = new ApiMember
        {
            Name = "Native",
            Kind = "method",
            MethodImplementation = facts,
            HasMethodBody = false,
        };
        var property = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            AccessorImplementations = [facts],
        };
        var type = new ApiType { Name = "Example", Kind = "class", Members = [method, property] };

        ApiType snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Same(facts, snapshot.Members[0].MethodImplementation);
        Assert.False(snapshot.Members[0].HasMethodBody);
        Assert.Same(facts, Assert.Single(snapshot.Members[1].AccessorImplementations!.Value));
    }

    [Fact]
    public void SnapshotTypeForRendering_CarriesLayoutFactsWithoutEmittingLayoutSyntax()
    {
        Guid moduleVersionId = Guid.NewGuid();
        var typeFacts = new ApiTypeLayoutFacts(moduleVersionId, 0x02000001, 32, 2);
        var fieldFacts = new ApiFieldLayoutFacts(
            moduleVersionId, typeFacts.TypeToken, 0x04000001, 0);
        var field = new ApiMember
        {
            Name = "Value",
            Kind = "field",
            ReturnType = "int",
            FieldLayout = fieldFacts,
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "LayoutCarrier",
            Kind = "struct",
            Layout = ApiTypeLayout.Explicit,
            LayoutDetails = typeFacts,
            Members = [field],
        };

        ApiType snapshot = CSharpTypePrinter.SnapshotTypeForRendering(type, type.Members);

        Assert.Same(typeFacts, snapshot.LayoutDetails);
        Assert.Same(fieldFacts, Assert.Single(snapshot.Members).FieldLayout);
        Assert.Equal(
            """
            namespace Samples;

            public struct LayoutCarrier
            {
                public int Value;
            }
            """,
            Assert.Single(_printer.Print(new CSharpTypePrintRequest(snapshot)).Units).Source);
    }

    [Fact]
    public void SnapshotTypeForRendering_CarriesSegmentParameterOwnership()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "Outer`1.Inner`1",
            DefinitionName = Assert
                .IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "N",
                        ["Outer`1", "Inner`1"]))
                .Name,
            IntroducedTypeParameterCounts = [2, 0],
            Kind = "class",
            TypeParameters =
            [
                new TypeParameter { Name = "A" },
                new TypeParameter { Name = "B" },
            ],
        };

        ApiType snapshot =
            CSharpTypePrinter.SnapshotTypeForRendering(type, []);

        Assert.Equal([2, 0], snapshot.IntroducedTypeParameterCounts);
        Assert.Equal(
            "Outer`1.Inner`1",
            CSharpFormatter.FormatTypeName(snapshot));
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
    public void GlobalAttributesAreEscapedAndDiagnoseGlobalRootConflicts()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("", "System")),
            new CSharpTypePrintOptions
            {
                TypeNamePolicy = CSharpTypeNamePolicy.Qualified,
                AssemblyAttributes = ["System.CLSCompliantAttribute(true)"],
                ModuleAttributes = ["event.Marker"]
            });

        Assert.Contains(
            "[assembly: global::System.CLSCompliantAttribute(true)]",
            result.Source,
            StringComparison.Ordinal);
        Assert.Contains("[module: @event.Marker]", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.TypeName == "<assembly>"
                && diagnostic.Message.Contains("conflicts with global type 'System'", StringComparison.Ordinal));
    }

    [Fact]
    public void SynthesizedObsoleteAttributeCannotBindToSiblingType()
    {
        var obsolete = CreateEmptyType("Samples", "Obsolete");
        var widget = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "int";
        member.IsObsolete = true;
        widget.Members.Add(member);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(obsolete), new CSharpTypePrintRequest(widget)]);

        Assert.Contains("[System.Obsolete] public int Get();", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void SynthesizedObsoleteReportsGlobalSystemConflict()
    {
        var system = CreateEmptyType("", "System");
        var widget = CreateEmptyType("Samples", "Widget");
        var member = CreateMethod("Get");
        member.SignatureModel!.ReturnType = "int";
        member.IsObsolete = true;
        widget.Members.Add(member);

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(system), new CSharpTypePrintRequest(widget)]);

        Assert.Contains("[global::System.Obsolete]", result.Source, StringComparison.Ordinal);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "conflicts with global type 'System'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GenericGlobalTypeDoesNotConflictWithNamespaceRoot()
    {
        var generic = CreateEmptyType("", "Foo`1");
        generic.TypeParameters = [new TypeParameter { Name = "T" }];
        var namespaced = CreateEmptyType("Foo.Bar", "Worker");

        var result = _printer.PrintBatch(
            [new CSharpTypePrintRequest(generic), new CSharpTypePrintRequest(namespaced)]);

        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Namespace root 'Foo' conflicts with global type 'Foo'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void GlobalNestedTypeReferenceDoesNotReportNamespaceRootConflict()
    {
        var host = CreateEmptyType("", "Host");
        var kind = CreateEmptyType("", "Kind");
        var method = CreateMethod("GetKind");
        method.SignatureModel!.ReturnType = "Host.Kind";
        host.Members.Add(method);

        var result = _printer.Print(new CSharpTypePrintRequest(
            host,
            nestedTypes: [new CSharpTypePrintRequest(kind)]));

        Assert.Contains("public global::Host.Kind GetKind();", result.Source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Message.Contains(
                "Type name 'Host.Kind' conflicts with global type 'Host'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SourceEscapesDeduplicatesAndSortsEmittedUsings()
    {
        var result = _printer.Print(
            new CSharpTypePrintRequest(CreateEmptyType("Samples", "Widget")),
            new CSharpTypePrintOptions
            {
                Usings = ["Alpha", "event", "System", "System", "Some.namespace.Value"]
            });

        Assert.StartsWith(
            "using @event;\nusing Alpha;\nusing Some.@namespace.Value;\nusing System;\n",
            result.Source,
            StringComparison.Ordinal);
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

    [Fact]
    public void BlockScopedBatchPreservesNamespaceIndentationForMultilineInitializers()
    {
        var field = new ApiMember
        {
            Name = "Values",
            Kind = "field",
            ReturnType = "int[]"
        };
        var type = CreateEmptyType("Samples", "First");
        type.Members.Add(field);

        var result = _printer.PrintBatch(
        [
            new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides:
                [
                    new CSharpMemberPolicy(
                        field,
                        CSharpBodyPolicy.Full,
                        new CSharpFieldInitializer(
                            """
                            [
                                1,
                                2
                            ]
                            """))
                ]),
            new CSharpTypePrintRequest(CreateEmptyType("Other", "Second"))
        ]);

        Assert.Contains(
            """
            namespace Samples
            {
                public class First
                {
                    public int[] Values = [
                    1,
                    2
                ];
                }
            }
            """,
            result.Source,
            StringComparison.Ordinal);
    }

    static ApiType CreateEmptyType(string? @namespace, string name)
        => new()
        {
            Namespace = @namespace,
            Name = name,
            Kind = "class"
        };

    static ApiType CreateExactType(
        string? @namespace,
        string[] segments,
        int[] introducedCounts,
        string[] typeParameterNames,
        string kind = "class")
    {
        string leaf = segments[^1];
        return new ApiType
        {
            Namespace = @namespace,
            Name = leaf,
            MetadataName = leaf,
            DefinitionName =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        @namespace ?? "",
                        [.. segments])).Name,
            IntroducedTypeParameterCounts = [.. introducedCounts],
            TypeParameters =
                [.. typeParameterNames.Select(name => new TypeParameter { Name = name })],
            Kind = kind
        };
    }

    static CSharpTypePrintResult AssertPrinted(CSharpTypePrintOutcome outcome)
        => Assert.IsType<CSharpTypePrintOutcome.Printed>(outcome).Result;

    static CSharpTypePrintOutcome.NotRendered AssertNotRendered(
        CSharpTypePrintOutcome outcome)
        => Assert.IsType<CSharpTypePrintOutcome.NotRendered>(outcome);

    static void AssertArityNotRendered(ApiType type)
    {
        CSharpTypePrintOutcome.NotRendered notRendered = AssertNotRendered(
            new CSharpTypePrinter().Print(new CSharpTypePrintRequest(type)));
        Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.ArityMismatch>(
            Assert.Single(notRendered.SelfNameFailures).Reason);
    }

    static void AssertIdentifierFailure(
        CSharpDeclaredTypeSelfNameFailure failure,
        string[] expectedSegments,
        CSharpTypeDeclarationIdentifierRefusalReason expectedReason)
    {
        Assert.Equal(expectedSegments, failure.Identity.Segments);
        var reason =
            Assert.IsType<CSharpDeclaredTypeSelfNameFailureReason.IdentifierNotAdmitted>(
                failure.Reason);
        Assert.Equal(expectedReason, reason.Reason);
    }

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

    sealed class SuccessfulCSharpTypePrinter
    {
        readonly CSharpTypePrinter _printer = new();

        public CSharpTypePrintResult Print(
            CSharpTypePrintRequest request,
            CSharpTypePrintOptions? options = null)
            => Assert.IsType<CSharpTypePrintOutcome.Printed>(
                _printer.Print(request, options)).Result;

        public CSharpTypePrintResult PrintBatch(
            IEnumerable<CSharpTypePrintRequest> requests,
            CSharpTypePrintOptions? options = null)
            => Assert.IsType<CSharpTypePrintOutcome.Printed>(
                _printer.PrintBatch(requests, options)).Result;
    }

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
