using System.Collections.Immutable;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed partial class CSharpTypePrinterTests
{

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
            [
                nameof(CSharpTypePrintOutcome.NotRendered.SelfNameFailures),
                nameof(CSharpTypePrintOutcome.NotRendered.MemorySafetyFailures)
            ],
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
}
