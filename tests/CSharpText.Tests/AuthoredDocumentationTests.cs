using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CSharpText.Tests;

public sealed class AuthoredDocumentationTests
{
    public static TheoryData<
        string,
        Microsoft.CodeAnalysis.CSharp.SyntaxKind,
        DeclarationKind> SupportedDeclarationCases
    { get; } = new()
    {
        {
            "/// <summary>Class.</summary>\nclass C { }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.ClassDeclaration,
            DeclarationKind.Class
        },
        {
            "/// <summary>Struct.</summary>\nstruct S { }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.StructDeclaration,
            DeclarationKind.Struct
        },
        {
            "/// <summary>Interface.</summary>\ninterface I { }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.InterfaceDeclaration,
            DeclarationKind.Interface
        },
        {
            "/// <summary>Record.</summary>\nrecord R;",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.RecordDeclaration,
            DeclarationKind.Record
        },
        {
            "/// <summary>Enum.</summary>\nenum E { Value }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.EnumDeclaration,
            DeclarationKind.Enum
        },
        {
            "/// <summary>Delegate.</summary>\ndelegate void D();",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.DelegateDeclaration,
            DeclarationKind.Delegate
        },
        {
            "class C { /// <summary>Destructor.</summary>\n~C() { } }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.DestructorDeclaration,
            DeclarationKind.Destructor
        },
        {
            "class C { /// <summary>Indexer.</summary>\nint this[int i] => i; }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.IndexerDeclaration,
            DeclarationKind.Property
        },
        {
            "class C { /// <summary>Event.</summary>\nevent Action E; }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.EventFieldDeclaration,
            DeclarationKind.Event
        },
        {
            "class C { /// <summary>Field.</summary>\nint F; }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.FieldDeclaration,
            DeclarationKind.Field
        },
        {
            "enum E { /// <summary>Value.</summary>\nValue }",
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.EnumMemberDeclaration,
            DeclarationKind.EnumMember
        },
    };

    [Fact]
    public void ExactDeclaration_ReturnsAttachedParsedDocumentation()
    {
        const string source = """
            class C
            {
                /// <summary>Builds <see cref="T:System.String"/>.</summary>
                /// <param name="value">The value.</param>
                /// <returns>A result.</returns>
                [Obsolete]
                public string Build(int value) => value.ToString();
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "Build"));

        Assert.Equal(DeclarationKind.Method, result.Declaration.Kind);
        Assert.Equal("Builds String.", result.Documentation.Summary);
        Assert.Equal(
            "The value.",
            Assert.Single(result.Documentation.Parameters).Value);
        Assert.Equal("A result.", result.Documentation.Returns);
        Assert.Equal(
            source.IndexOf("///", StringComparison.Ordinal),
            Assert.Single(result.DocumentationSpans).Start);
        DocumentationCommentTriviaSyntax documentation =
            Assert.IsType<DocumentationCommentTriviaSyntax>(
                SyntaxMethods(source)
                    .Single()
                    .GetLeadingTrivia()
                    .Single(static trivia => trivia.HasStructure)
                    .GetStructure());
        int documentationEnd = documentation.ParentTrivia.FullSpan.End;
        while (documentationEnd
                > documentation.ParentTrivia.FullSpan.Start
            && source[documentationEnd - 1] is '\r' or '\n')
        {
            documentationEnd--;
        }
        Assert.Equal(
            new CSharpSourceSpan(
                documentation.ParentTrivia.FullSpan.Start,
                documentationEnd
                    - documentation.ParentTrivia.FullSpan.Start),
            result.DocumentationSpans[0]);
        Assert.Equal(
            CSharpAuthoredDocumentationLimitations.None,
            result.Limitations);
        Assert.True(result.Work.TokensRetained > 0);
        Assert.True(result.Work.XmlNodesExamined > 0);
    }

    [Fact]
    public void ExactSpan_SelectsOneOverloadWithoutNameSearch()
    {
        const string source = """
            class C
            {
                /// <summary>First.</summary>
                void M() { }

                /// <summary>Second.</summary>
                void M(int value) { }
            }
            """;
        MethodDeclarationSyntax method = SyntaxMethods(source)[1];

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                Read(source, method));

        Assert.Equal("Second.", result.Documentation.Summary);
        Assert.Equal(method.Span.Start, result.Declaration.Span.Start);
        Assert.Equal(method.Span.Length, result.Declaration.Span.Length);
    }

    [Fact]
    public void MissingDocumentationAndNonDeclarationAreDistinct()
    {
        const string source = """
            class C
            {
                int P { get; set; }
            }
            """;
        var root = CSharpSyntaxTree.ParseText(
            source,
            cancellationToken: TestContext.Current.CancellationToken).GetRoot(
            TestContext.Current.CancellationToken);
        PropertyDeclarationSyntax property =
            root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        AccessorDeclarationSyntax accessor =
            root.DescendantNodes().OfType<AccessorDeclarationSyntax>().First();

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Absent>(
            Read(source, property));
        Assert.IsType<CSharpAuthoredDocumentationOutcome.NoDeclaration>(
            Read(source, accessor));
    }

    [Fact]
    public void MultiDeclaratorField_IsAmbiguousAtTheSharedPhysicalDeclaration()
    {
        const string source = """
            class C
            {
                /// <summary>Fields.</summary>
                int first, second;
            }
            """;
        FieldDeclarationSyntax field = CSharpSyntaxTree.ParseText(
                source,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Single();

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Ambiguous>(
                Read(source, field));

        Assert.Equal(2, result.Declarations.Count);
        Assert.All(
            result.Declarations,
            declaration => Assert.Equal(
                new CSharpSourceSpan(field.Span.Start, field.Span.Length),
                declaration.Span));
    }

    [Fact]
    public void AmbiguityPrecedesBranchDependentAttachment()
    {
        const string source = """
            class C
            {
            #if DOCUMENTATION
                /// <summary>Fields.</summary>
            #endif
                int first, second;
            }
            """;
        FieldDeclarationSyntax field = CSharpSyntaxTree.ParseText(
                source,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Single();

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Ambiguous>(
            Read(source, field));
    }

    [Fact]
    public void AmbiguousSharedDeclaration_MaterializesDocumentationLinearly()
    {
        var smaller = Fixture(400);
        var larger = Fixture(800);

        _ = CSharpAuthoredDocumentation.Read(smaller);
        _ = CSharpAuthoredDocumentation.Read(larger);

        long smallAllocation = Measure(smaller);
        long largeAllocation = Measure(larger);

        Assert.True(
            largeAllocation < smallAllocation * 3,
            $"doubling fragments and declarators changed allocation from "
                + $"{smallAllocation:N0} to {largeAllocation:N0} bytes");

        static long Measure(CSharpAuthoredDocumentationRequest request)
        {
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var result = Assert.IsType<
                CSharpAuthoredDocumentationOutcome.Ambiguous>(
                    CSharpAuthoredDocumentation.Read(request));
            long allocation =
                GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, result.Work.DocumentationCharactersExamined);
            return allocation;
        }

        static CSharpAuthoredDocumentationRequest Fixture(int count)
        {
            string documentation = string.Concat(
                Enumerable.Range(0, count).Select(index =>
                    index % 2 == 0
                        ? "/// <summary>x</summary>\n"
                        : "/** <remarks>x</remarks> */\n"));
            string declaration = "int "
                + string.Join(
                    ", ",
                    Enumerable.Range(0, count).Select(index => $"F{index}"))
                + ";";
            string source =
                $"class C\n{{\n{documentation}{declaration}\n}}";
            return new(
                source,
                TextSpan(source, declaration),
                limits: CSharpAuthoredDocumentationLimits.Default with
                {
                    MaxDeclarations = count + 1,
                });
        }
    }

    [Fact]
    public void ExactRawSpan_IncludesAttributesAndRejectsContainment()
    {
        const string source = """
            class C
            {
                /// <summary>Method.</summary>
                [Obsolete]
                void M() { }
            }
            """;
        MethodDeclarationSyntax method = SyntaxMethods(source).Single();
        CSharpSourceSpan exact = new(method.Span.Start, method.Span.Length);
        CSharpSourceSpan withoutAttribute = TextSpan(source, "void M() { }");

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Available>(
            CSharpAuthoredDocumentation.Read(new(source, exact)));
        Assert.IsType<CSharpAuthoredDocumentationOutcome.NoDeclaration>(
            CSharpAuthoredDocumentation.Read(
                new(source, withoutAttribute)));
        Assert.IsType<CSharpAuthoredDocumentationOutcome.NoDeclaration>(
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    new(exact.Start + 1, exact.Length - 1))));
    }

    [Fact]
    public void LeadingBom_IsPreambleOutsideTheExactDeclarationSpan()
    {
        const string source = "\uFEFFclass C { }";
        ClassDeclarationSyntax declaration =
            CSharpSyntaxTree.ParseText(
                    source,
                    cancellationToken:
                        TestContext.Current.CancellationToken)
                .GetRoot(TestContext.Current.CancellationToken)
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single();

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Absent>(
                Read(source, declaration));

        Assert.Equal(1, result.Declaration.Span.Start);
        Assert.IsType<CSharpAuthoredDocumentationOutcome.NoDeclaration>(
            CSharpAuthoredDocumentation.Read(
                new(source, new(0, source.Length))));
    }

    [Fact]
    public void ExactSpans_SelectNestedAndSupportedDeclarationKinds()
    {
        const string source = """
            /// <summary>Partial.</summary>
            partial class C
            {
                /// <summary>Constructor.</summary>
                public C() { }

                /// <summary>Property.</summary>
                public int P { get; set; }

                /// <summary>Operator.</summary>
                public static C operator +(C left, C right) => left;

                class Nested
                {
                    /// <summary>Nested method.</summary>
                    void M() { }
                }
            }

            partial class C
            {
                /// <summary>Neighbor method.</summary>
                void M() { }
            }
            """;
        var root = CSharpSyntaxTree.ParseText(
                source,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken);

        AssertDocumentation<ClassDeclarationSyntax>(
            static declaration =>
                declaration.Modifiers.Any(static modifier =>
                    modifier.IsKind(
                        Microsoft.CodeAnalysis.CSharp.SyntaxKind
                            .PartialKeyword))
                && declaration.Members.Count > 1,
            "Partial.");
        AssertDocumentation<ConstructorDeclarationSyntax>(
            static _ => true,
            "Constructor.");
        AssertDocumentation<PropertyDeclarationSyntax>(
            static _ => true,
            "Property.");
        AssertDocumentation<OperatorDeclarationSyntax>(
            static _ => true,
            "Operator.");
        AssertDocumentation<MethodDeclarationSyntax>(
            static declaration =>
                declaration.Parent is ClassDeclarationSyntax
                {
                    Identifier.ValueText: "Nested",
                },
            "Nested method.");
        AssertDocumentation<MethodDeclarationSyntax>(
            static declaration =>
                declaration.Parent is ClassDeclarationSyntax
                {
                    Identifier.ValueText: "C",
                },
            "Neighbor method.");

        void AssertDocumentation<TNode>(
            Func<TNode, bool> predicate,
            string expected)
            where TNode : SyntaxNode
        {
            TNode declaration = root.DescendantNodesAndSelf()
                .OfType<TNode>()
                .Single(predicate);
            var result = Assert.IsType<
                CSharpAuthoredDocumentationOutcome.Available>(
                    Read(source, declaration));
            Assert.Equal(expected, result.Documentation.Summary);
        }
    }

    [Theory]
    [MemberData(nameof(SupportedDeclarationCases))]
    public void SupportedDeclarationKinds_UseRoslynExactSpans(
        string source,
        Microsoft.CodeAnalysis.CSharp.SyntaxKind syntaxKind,
        DeclarationKind expectedKind)
    {
        SyntaxNode declaration = CSharpSyntaxTree.ParseText(
                source,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodesAndSelf()
            .Single(node => node.IsKind(syntaxKind));

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                Read(source, declaration));

        Assert.Equal(expectedKind, result.Declaration.Kind);
        Assert.NotNull(result.Documentation.Summary);
    }

    [Fact]
    public void NamespaceIsOutsideTheSupportedDeclarationProfile()
    {
        const string source = """
            /// <summary>Namespace.</summary>
            namespace N { }
            """;
        NamespaceDeclarationSyntax declaration =
            CSharpSyntaxTree.ParseText(
                    source,
                    cancellationToken:
                        TestContext.Current.CancellationToken)
                .GetRoot(TestContext.Current.CancellationToken)
                .DescendantNodes()
                .OfType<NamespaceDeclarationSyntax>()
                .Single();

        Assert.IsType<CSharpAuthoredDocumentationOutcome.NoDeclaration>(
            Read(source, declaration));
    }

    [Fact]
    public void NearestDocumentationRun_ToleratesTriviaBelowButNotBetweenRuns()
    {
        const string source = """
            class C
            {
                /// <summary>Detached.</summary>
                // separates documentation runs
                /// <summary>Attached.</summary>
                // tolerated below the nearest run
                [Obsolete]
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "M"));

        Assert.Equal("Attached.", result.Documentation.Summary);
        Assert.Single(result.DocumentationSpans);
        Assert.Equal(
            source.IndexOf(
                "/// <summary>Attached.",
                StringComparison.Ordinal),
            result.DocumentationSpans[0].Start);
    }

    [Fact]
    public void DocumentationAfterSameLineTerminators_AttachesToNextDeclaration()
    {
        const string memberSource = """
            class C
            {
                int A; /// <summary>Method.</summary>
                void M() { }
            }
            """;
        var method = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(memberSource, "M"));
        Assert.Equal("Method.", method.Documentation.Summary);

        const string enumSource = """
            enum E
            {
                A, /// <summary>Member.</summary>
                B
            }
            """;
        EnumMemberDeclarationSyntax member =
            CSharpSyntaxTree.ParseText(
                    enumSource,
                    cancellationToken:
                        TestContext.Current.CancellationToken)
                .GetRoot(TestContext.Current.CancellationToken)
                .DescendantNodes()
                .OfType<EnumMemberDeclarationSyntax>()
                .Single(candidate => candidate.Identifier.ValueText == "B");
        var enumMember = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                Read(enumSource, member));
        Assert.Equal("Member.", enumMember.Documentation.Summary);
    }

    [Fact]
    public void DocumentationAfterTheFirstAttribute_IsNotAttached()
    {
        const string source = """
            class C
            {
                [Obsolete]
                /// <summary>Too late.</summary>
                void M() { }
            }
            """;

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Absent>(
            ReadMethod(source, "M"));
    }

    [Fact]
    public void ConditionalDocumentationAfterAttribute_DoesNotPoisonAttachedRun()
    {
        const string source = """
            class C
            {
                /// <summary>Attached.</summary>
                [Obsolete]
            #if DOCUMENTATION
                /// <summary>Too late.</summary>
            #endif
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "M"));

        Assert.Equal("Attached.", result.Documentation.Summary);
        Assert.Single(result.DocumentationSpans);
    }

    [Theory]
    [InlineData("//// <summary>Not documentation.</summary>")]
    [InlineData("/*** <summary>Not documentation.</summary> */")]
    [InlineData("/**/")]
    public void NearMissDelimiters_AreNotDocumentation(string comment)
    {
        string source = $$"""
            class C
            {
                {{comment}}
                void M() { }
            }
            """;

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Absent>(
            ReadMethod(source, "M"));
    }

    [Fact]
    public void AdjacentSingleAndDelimitedDocumentation_UsesCSharpExteriors()
    {
        const string source = """
            class C
            {
                /// <summary>
                /// First
                /// </summary>
                /**
                 * <remarks>
                 * Second
                 * </remarks>
                 */
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "M"));

        Assert.Equal("First", result.Documentation.Summary);
        Assert.Equal("Second", result.Documentation.Remarks);
        Assert.Equal(2, result.DocumentationSpans.Count);
    }

    [Theory]
    [InlineData("/// <summary>Broken")]
    [InlineData("/// <!DOCTYPE summary [<!ENTITY x \"value\">]><summary>&x;</summary>")]
    public void InvalidXml_IsMalformedWithoutPlainTextFallback(
        string documentation)
    {
        string source = $$"""
            class C
            {
                {{documentation}}
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Malformed>(
                ReadMethod(source, "M"));

        Assert.Equal(
            CSharpAuthoredDocumentationMalformedReason.InvalidXml,
            result.Reason);
    }

    [Fact]
    public void SyntheticWrapperEscape_IsMalformedRatherThanThrown()
    {
        const string source = """
            class C
            {
                /// </member><member name="M:Source"><summary>Injected</summary>
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Malformed>(
                ReadMethod(source, "M"));

        Assert.Equal(
            CSharpAuthoredDocumentationMalformedReason.InvalidXml,
            result.Reason);
    }

    [Fact]
    public void UnterminatedDelimitedDocumentation_IsMalformed()
    {
        const string source = """
            class C
            {
                /** <summary>Broken.</summary>
                void M() { }
            }
            """;
        CSharpSourceSpan span = TextSpan(source, "void M() { }");

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Malformed>(
                CSharpAuthoredDocumentation.Read(new(source, span)));

        Assert.Equal(
            CSharpAuthoredDocumentationMalformedReason
                .UnterminatedDocumentationComment,
            result.Reason);
        Assert.Equal(
            new CSharpSourceSpan(
                source.IndexOf("/**", StringComparison.Ordinal),
                source.Length
                    - source.IndexOf("/**", StringComparison.Ordinal)),
            Assert.Single(result.DocumentationSpans));
    }

    [Fact]
    public void ConditionalUnterminatedDocumentation_IsUncertain()
    {
        const string source = """
            class C
            {
            #if DOCUMENTATION
                /** broken
            #endif
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(
                    new(source, MethodSpan(source, "M"))));

        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty
                .ConditionalBranchUnresolved,
            result.Reason);
    }

    [Fact]
    public void DetachedConditionalDocumentation_DoesNotHideUnterminatedFragment()
    {
        const string source = """
            #if X
            /// <summary>Older.</summary>
            #endif
            /** broken
            class C { }
            """;
        CSharpSourceSpan declaration = TextSpan(source, "class C { }");

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Malformed>(
                CSharpAuthoredDocumentation.Read(
                    new(source, declaration)));

        Assert.Equal(
            CSharpAuthoredDocumentationMalformedReason
                .UnterminatedDocumentationComment,
            result.Reason);
    }

    [Fact]
    public void UnterminatedDocumentationLimit_CoversTheFullLexicalComment()
    {
        string source = """
            class C
            {
                /** <summary>Broken.</summary>
                void M() { }
            }
            """
            + new string(' ', 100);
        CSharpSourceSpan span = TextSpan(source, "void M() { }");
        int commentLength =
            source.Length - source.IndexOf("/**", StringComparison.Ordinal);

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Malformed>(
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    span,
                    limits: CSharpAuthoredDocumentationLimits.Default with
                    {
                        MaxDocumentationCharacters = commentLength,
                    })));
        var incomplete = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        span,
                        limits:
                            CSharpAuthoredDocumentationLimits.Default with
                            {
                                MaxDocumentationCharacters =
                                    commentLength - 1,
                            })));
        Assert.Equal(
            CSharpAuthoredDocumentationIncompleteBoundary
                .DocumentationCharacters,
            incomplete.Boundary);
    }

    [Fact]
    public void EmptyFieldsAndUnexpandedElements_AreAvailableWithLimitations()
    {
        const string source = """
            class C
            {
                /// <include file="docs.xml" path="/doc/member"/>
                /// <inheritdoc/>
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "M"));

        Assert.Null(result.Documentation.Summary);
        Assert.Equal(
            CSharpAuthoredDocumentationLimitations.Include
                | CSharpAuthoredDocumentationLimitations.InheritDoc,
            result.Limitations);
    }

    [Fact]
    public void ExactSpanSelectsConditionalBranch_AndConflictingEvidenceIsUncertain()
    {
        const string source = """
            class C
            {
            #if FIRST
                /// <summary>First.</summary>
                void First() { }
            #else
                /// <summary>Second.</summary>
                void Second() { }
            #endif
            }
            """;
        CSharpSourceSpan first = TextSpan(source, "void First() { }");

        var selected = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                CSharpAuthoredDocumentation.Read(new(source, first)));
        Assert.Equal("First.", selected.Documentation.Summary);

        var uncertain = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(
                    new(source, first, activePhysicalLines: [8])));
        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty
                .ConditionalBranchUnresolved,
            uncertain.Reason);
    }

    [Fact]
    public void BranchDependentAttachment_RequiresBranchEvidence()
    {
        const string source = """
            class C
            {
            #if DOCUMENTATION
                /// <summary>Conditional.</summary>
            #endif
                void M() { }
            }
            """;
        CSharpSourceSpan method = MethodSpan(source, "M");

        var unresolved = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(new(source, method)));
        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty
                .DocumentationAttachmentUnvouched,
            unresolved.Reason);

        var selected = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                CSharpAuthoredDocumentation.Read(
                    new(source, method, activePhysicalLines: [4])));
        Assert.Equal("Conditional.", selected.Documentation.Summary);
    }

    [Fact]
    public void BranchDependentDocumentation_DoesNotUnvouchKnownDeclarationMismatch()
    {
        const string source = """
            class C
            {
            #if DOCUMENTATION
                /// <summary>Conditional.</summary>
            #endif
                void M() { }
            }
            """;
        CSharpSourceSpan method = MethodSpan(source, "M");

        var exact = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(new(source, method)));
        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty
                .DocumentationAttachmentUnvouched,
            exact.Reason);

        Assert.IsType<CSharpAuthoredDocumentationOutcome.NoDeclaration>(
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    new(method.Start, method.Length - 1))));
    }

    [Fact]
    public void UnclosedDeclaration_IsUncertainRatherThanAvailable()
    {
        const string source = """
            class C
            {
                /// <summary>Text.</summary>
                void M() {
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(
                    new(source, MethodSpan(source, "M"))));

        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty.DeclarationSpanUnvouched,
            result.Reason);
    }

    [Fact]
    public void BranchDependentTerminal_IsUncertainRatherThanAvailable()
    {
        const string source = """
            class C
            {
                /// <summary>Text.</summary>
                class Subject { }
            #if X
                int P { get; set; }
            #endif
                ;
            }
            """;
        CSharpSourceSpan subject = TextSpan(source, "class Subject { }");

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(new(source, subject)));

        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty.DeclarationSpanUnvouched,
            result.Reason);
    }

    [Fact]
    public void DetachedConditionalDocumentation_DoesNotPoisonNearestRun()
    {
        const string source = """
            class C
            {
            #if DOCUMENTATION
                /// <summary>Detached.</summary>
            #endif
                // separator
                /// <summary>Attached.</summary>
                void M() { }
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "M"));

        Assert.Equal("Attached.", result.Documentation.Summary);
        Assert.Single(result.DocumentationSpans);
    }

    [Fact]
    public void ConditionalGroupCrossingDeclarationBoundary_IsUncertain()
    {
        const string source = """
            class C
            {
                int P =>
            #if FIRST
                    1;
            #else
                    2;
            #endif
            }
            """;
        var options = new CSharpParseOptions(
            preprocessorSymbols: ["FIRST"]);
        PropertyDeclarationSyntax property = CSharpSyntaxTree.ParseText(
                source,
                options,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Single();

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Uncertain>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        new(property.Span.Start, property.Span.Length),
                        activePhysicalLines: [5])));

        Assert.Equal(
            CSharpAuthoredDocumentationUncertainty
                .ConditionalBranchUnresolved,
            result.Reason);
    }

    [Fact]
    public void LineDirective_DoesNotInvalidateExactPhysicalCoordinates()
    {
        const string source = """
            class C
            {
            #line 200 "Generated.cs"
                /// <summary>Physical.</summary>
                void M() { }
            #line default
            }
            """;

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                ReadMethod(source, "M"));

        Assert.Equal("Physical.", result.Documentation.Summary);
    }

    [Fact]
    public void EveryScalarWorkLimit_AcceptsThresholdAndRefusesOneBeyond()
    {
        const string source = """
            class C
            {
                /// <summary>Bounded.</summary>
                void M() { }
            }
            """;
        CSharpSourceSpan span = MethodSpan(source, "M");
        var baseline = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                CSharpAuthoredDocumentation.Read(new(source, span)));

        AssertThreshold(
            source,
            span,
            baseline.Work.SourceCharactersExamined,
            (limits, value) => limits with
            {
                MaxSourceCharacters = value,
            },
            CSharpAuthoredDocumentationIncompleteBoundary.SourceCharacters);
        AssertThreshold(
            source,
            span,
            baseline.Work.LinesExamined!.Value,
            (limits, value) => limits with { MaxLines = value },
            CSharpAuthoredDocumentationIncompleteBoundary.Lines);
        AssertThreshold(
            source,
            span,
            baseline.Work.TokensRetained!.Value,
            (limits, value) => limits with { MaxTokens = value },
            CSharpAuthoredDocumentationIncompleteBoundary.Tokens);
        AssertThreshold(
            source,
            span,
            baseline.Work.DeclarationsCompared!.Value,
            (limits, value) => limits with { MaxDeclarations = value },
            CSharpAuthoredDocumentationIncompleteBoundary.Declarations);
        AssertThreshold(
            source,
            span,
            baseline.Work.DocumentationCharactersExamined,
            (limits, value) => limits with
            {
                MaxDocumentationCharacters = value,
            },
            CSharpAuthoredDocumentationIncompleteBoundary
                .DocumentationCharacters);
        AssertThreshold(
            source,
            span,
            baseline.Work.XmlNodesExamined,
            (limits, value) => limits with { MaxXmlNodes = value },
            CSharpAuthoredDocumentationIncompleteBoundary.XmlNodes);
        AssertThreshold(
            source,
            span,
            baseline.Work.RetainedTextCharacters,
            (limits, value) => limits with
            {
                MaxRetainedTextCharacters = value,
            },
            CSharpAuthoredDocumentationIncompleteBoundary.RetainedText);
    }

    [Fact]
    public void SourceLimitPrecedesDeferredPhysicalLineValidation()
    {
        string source = new('x', 1_000_000);
        var request = new CSharpAuthoredDocumentationRequest(
            source,
            new(0, 0),
            activePhysicalLines: new ThrowingActiveLines(),
            limits: CSharpAuthoredDocumentationLimits.Default with
            {
                MaxSourceCharacters = 1,
            });

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(request));

        Assert.Equal(
            CSharpAuthoredDocumentationIncompleteBoundary.SourceCharacters,
            result.Boundary);
        Assert.Equal(2, result.Work.SourceCharactersExamined);
    }

    [Fact]
    public void LineLimitPrecedesDeferredPhysicalLineValidation()
    {
        const string source = "first\nsecond";
        var request = new CSharpAuthoredDocumentationRequest(
            source,
            new(0, 0),
            activePhysicalLines: new ThrowingActiveLines(),
            limits: CSharpAuthoredDocumentationLimits.Default with
            {
                MaxLines = 1,
            });

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(request));

        Assert.Equal(
            CSharpAuthoredDocumentationIncompleteBoundary.Lines,
            result.Boundary);
    }

    [Fact]
    public void DenseDeclarations_UseOneBoundedComparisonPass()
    {
        const int memberCount = 10_000;
        string source = "enum E { "
            + string.Join(
                ", ",
                Enumerable.Range(0, memberCount).Select(
                    static index => $"A{index}"))
            + " }";
        string selectedText = $"A{memberCount - 1}";
        CSharpSourceSpan selected = TextSpan(source, selectedText);

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Absent>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        selected,
                        limits:
                            CSharpAuthoredDocumentationLimits.Default with
                            {
                                MaxDeclarations = memberCount + 1,
                            })));

        Assert.Equal(
            memberCount + 1,
            result.Work.DeclarationsCompared);
    }

    [Fact]
    public void DenseConditionalEvidence_SelectsBranchesWithSortedLookup()
    {
        const int groupCount = 10_000;
        string conditionals = string.Concat(
            Enumerable.Range(0, groupCount).Select(index =>
                $"#if C{index}\nint F{index};\n#endif\n"));
        string source = $"class C\n{{\n{conditionals}void M() {{ }}\n}}";
        int[] activeLines =
            [.. Enumerable.Range(0, groupCount).Select(index => 4 + (3 * index))];

        Assert.IsType<CSharpAuthoredDocumentationOutcome.Absent>(
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    MethodSpan(source, "M"),
                    activePhysicalLines: activeLines)));
    }

    [Fact]
    public void DuplicateParameterNames_UseFinalRetainedTextAtThreshold()
    {
        const string source = """
            class C
            {
                /// <param name="x">A</param>
                /// <param name="x">B</param>
                void M() { }
            }
            """;
        CSharpSourceSpan span = MethodSpan(source, "M");
        var limits = CSharpAuthoredDocumentationLimits.Default with
        {
            MaxRetainedTextCharacters = 2,
        };

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                CSharpAuthoredDocumentation.Read(
                    new(source, span, limits: limits)));

        Assert.Equal("B", Assert.Single(result.Documentation.Parameters).Value);
        Assert.Equal(2, result.Work.RetainedTextCharacters);
    }

    [Fact]
    public void RepeatedFieldAndDepthLimits_AreTypedIncompleteOutcomes()
    {
        const string source = """
            class C
            {
                /// <summary><b>Nested.</b></summary>
                /// <param name="first">First.</param>
                /// <param name="second">Second.</param>
                /// <exception cref="T:System.Exception">First.</exception>
                /// <exception cref="T:System.InvalidOperationException">Second.</exception>
                /// <example><code source="one.cs"/></example>
                /// <example><code source="two.cs"/></example>
                void M(int first, int second) { }
            }
            """;
        CSharpSourceSpan span = MethodSpan(source, "M");

        AssertAvailable(source, span, limits => limits with
        {
            MaxXmlDepth = 3,
            MaxParameters = 2,
            MaxExceptions = 2,
            MaxSamples = 2,
        });
        AssertIncomplete(
            source,
            span,
            limits => limits with { MaxXmlDepth = 2 },
            CSharpAuthoredDocumentationIncompleteBoundary.XmlDepth);
        AssertIncomplete(
            source,
            span,
            limits => limits with { MaxParameters = 1 },
            CSharpAuthoredDocumentationIncompleteBoundary.Parameters);
        AssertIncomplete(
            source,
            span,
            limits => limits with { MaxExceptions = 1 },
            CSharpAuthoredDocumentationIncompleteBoundary.Exceptions);
        AssertIncomplete(
            source,
            span,
            limits => limits with { MaxSamples = 1 },
            CSharpAuthoredDocumentationIncompleteBoundary.Samples);
    }

    [Fact]
    public void IncompleteResults_ReportCompletedBoundedWork()
    {
        const string source = """
            class C
            {
                /// <summary>One</summary>
                /// <remarks>Two</remarks>
                void M() { }
            }
            """;
        CSharpSourceSpan span = MethodSpan(source, "M");
        var baseline = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                CSharpAuthoredDocumentation.Read(new(source, span)));

        var tokens = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        span,
                        limits:
                            CSharpAuthoredDocumentationLimits.Default with
                            {
                                MaxTokens =
                                    baseline.Work.TokensRetained!.Value - 1,
                            })));
        Assert.Equal(tokens.Limit, tokens.Work.TokensRetained);

        var declarations = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        span,
                        limits:
                            CSharpAuthoredDocumentationLimits.Default with
                            {
                                MaxDeclarations = 1,
                            })));
        Assert.Equal(
            declarations.Limit,
            declarations.Work.DeclarationsCompared);

        var retained = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        span,
                        limits:
                            CSharpAuthoredDocumentationLimits.Default with
                            {
                                MaxRetainedTextCharacters = 3,
                            })));
        Assert.Equal(3, retained.Work.RetainedTextCharacters);
        Assert.True(retained.Observed > retained.Limit);
    }

    [Fact]
    public void RealMemberTextSlicerDeclaration_ProducesDetachedDocumentation()
    {
        string root = RepositoryRoot();
        string source = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "CSharpText.MemberSlicing",
                "MemberTextSlicer.cs"));
        MethodDeclarationSyntax method = SyntaxMethods(source)
            .Single(candidate =>
                candidate.Identifier.ValueText == "ExtractMemberText");

        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Available>(
                Read(source, method));

        Assert.Contains(
            "Locates the declaration",
            result.Documentation.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalTypes_DoNotRetainInputOrReopeningState()
    {
        Type[] prohibited =
        [
            typeof(CSharpAuthoredDocumentationRequest),
            typeof(DeclarationIndex),
            typeof(Stream),
            typeof(TextReader),
            typeof(System.Xml.XmlReader),
        ];

        foreach (Type type in
            typeof(CSharpAuthoredDocumentationOutcome).GetNestedTypes())
        {
            Type[] retainedTypes = type.GetFields(
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic)
                .Select(static field => field.FieldType)
                .ToArray();
            Assert.DoesNotContain(
                retainedTypes,
                retained => prohibited.Any(
                    prohibitedType =>
                        prohibitedType.IsAssignableFrom(retained)));
        }
    }

    private static CSharpAuthoredDocumentationOutcome ReadMethod(
        string source,
        string name) =>
        CSharpAuthoredDocumentation.Read(
            new(source, MethodSpan(source, name)));

    private static CSharpAuthoredDocumentationOutcome Read(
        string source,
        SyntaxNode declaration) =>
        CSharpAuthoredDocumentation.Read(
            new(
                source,
                new(
                    declaration.Span.Start,
                    declaration.Span.Length)));

    private static CSharpSourceSpan MethodSpan(
        string source,
        string name)
    {
        MethodDeclarationSyntax method = SyntaxMethods(source)
            .Single(candidate => candidate.Identifier.ValueText == name);
        return new(method.Span.Start, method.Span.Length);
    }

    private static MethodDeclarationSyntax[] SyntaxMethods(string source) =>
        [.. CSharpSyntaxTree.ParseText(
                source,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetRoot(TestContext.Current.CancellationToken)
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()];

    private static CSharpSourceSpan TextSpan(
        string source,
        string text)
    {
        int start = source.IndexOf(text, StringComparison.Ordinal);
        Assert.True(start >= 0);
        return new(start, text.Length);
    }

    private static void AssertThreshold(
        string source,
        CSharpSourceSpan span,
        int threshold,
        Func<
            CSharpAuthoredDocumentationLimits,
            int,
            CSharpAuthoredDocumentationLimits> configure,
        CSharpAuthoredDocumentationIncompleteBoundary boundary)
    {
        Assert.True(threshold > 1);
        AssertAvailable(
            source,
            span,
            limits => configure(limits, threshold));
        AssertIncomplete(
            source,
            span,
            limits => configure(limits, threshold - 1),
            boundary);
    }

    private static void AssertAvailable(
        string source,
        CSharpSourceSpan span,
        Func<
            CSharpAuthoredDocumentationLimits,
            CSharpAuthoredDocumentationLimits> configure) =>
        Assert.IsType<CSharpAuthoredDocumentationOutcome.Available>(
            CSharpAuthoredDocumentation.Read(
                new(
                    source,
                    span,
                    limits: configure(
                        CSharpAuthoredDocumentationLimits.Default))));

    private static void AssertIncomplete(
        string source,
        CSharpSourceSpan span,
        Func<
            CSharpAuthoredDocumentationLimits,
            CSharpAuthoredDocumentationLimits> configure,
        CSharpAuthoredDocumentationIncompleteBoundary boundary)
    {
        var result = Assert.IsType<
            CSharpAuthoredDocumentationOutcome.Incomplete>(
                CSharpAuthoredDocumentation.Read(
                    new(
                        source,
                        span,
                        limits: configure(
                            CSharpAuthoredDocumentationLimits.Default))));
        Assert.Equal(boundary, result.Boundary);
    }

    private sealed class ThrowingActiveLines : IReadOnlyList<int>
    {
        public int Count =>
            throw new InvalidOperationException("Evidence was consumed.");

        public int this[int index] =>
            throw new InvalidOperationException("Evidence was consumed.");

        public IEnumerator<int> GetEnumerator() =>
            throw new InvalidOperationException("Evidence was consumed.");

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(
                Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
