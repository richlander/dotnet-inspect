using CSharpText;

namespace CSharpText.MemberSlicing.Tests;

public class GetMemberTextPartsTests
{
    [Fact]
    public void DocumentationAttributesAndInterveningTrivia_PreserveOriginalTextAndOffsets()
    {
        const string source =
            "class C\r\n" +
            "{\r\n" +
            "    /// <summary>First.</summary>\r\n" +
            "    /// <remarks>Second.</remarks>\r\n" +
            "    // kept between documentation groups\r\n" +
            "\r\n" +
            "    /** <summary>Block.</summary> */\r\n" +
            "    [First][Second(\r\n" +
            "        42)]\r\n" +
            "    public int M(\r\n" +
            "        int value)\r\n" +
            "    {\r\n" +
            "        return value;\r\n" +
            "    }\r\n" +
            "}\r\n";

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 12, 13, "M"));

        Assert.Collection(
            parts.XmlDocumentation,
            docs => AssertPart(
                source,
                docs,
                "/// <summary>First.</summary>\r\n    /// <remarks>Second.</remarks>",
                3,
                4),
            docs => AssertPart(
                source,
                docs,
                "/** <summary>Block.</summary> */",
                7,
                7));
        Assert.Collection(
            parts.Attributes,
            attribute => AssertPart(source, attribute, "[First]", 8, 8),
            attribute => AssertPart(source, attribute, "[Second(\r\n        42)]", 8, 9));
        AssertPart(
            source,
            parts.Signature,
            "public int M(\r\n        int value)",
            10,
            11);
        AssertPart(
            source,
            Assert.IsType<MemberTextPart>(parts.Body),
            "{\r\n        return value;\r\n    }",
            12,
            14);
        AssertPart(
            source,
            parts.Declaration,
            "[First][Second(\r\n" +
            "        42)]\r\n" +
            "    public int M(\r\n" +
            "        int value)\r\n" +
            "    {\r\n" +
            "        return value;\r\n" +
            "    }",
            8,
            14);
        AssertPart(
            source,
            parts.Member,
            "/// <summary>First.</summary>\r\n" +
            "    /// <remarks>Second.</remarks>\r\n" +
            "    // kept between documentation groups\r\n" +
            "\r\n" +
            "    /** <summary>Block.</summary> */\r\n" +
            "    [First][Second(\r\n" +
            "        42)]\r\n" +
            "    public int M(\r\n" +
            "        int value)\r\n" +
            "    {\r\n" +
            "        return value;\r\n" +
            "    }",
            3,
            14);
    }

    [Fact]
    public void DocumentationOnContainingTypeOpeningLine_AttachesToExactMemberParts()
    {
        const string source =
            "class C { /** <summary>M.</summary> */ void M() { } }";

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 1, 1, "M"));

        var documentation = Assert.Single(parts.XmlDocumentation);
        AssertPart(
            source,
            documentation,
            "/** <summary>M.</summary> */",
            1,
            1);
        AssertPart(
            source,
            parts.Member,
            "/** <summary>M.</summary> */ void M() { }",
            1,
            1);
        AssertPart(source, parts.Declaration, "void M() { }", 1, 1);
        Assert.Null(MemberTextSlicer.ExtractMemberText(source, 1, 1, "M"));
    }

    [Fact]
    public void DocumentationAfterPreviousMemberClosingBrace_AttachesToNextMember()
    {
        const string source = """
            class C
            {
                void Before()
                {
                } /** <summary>M.</summary> */
                void M()
                {
                }
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 7, 8, "M"));

        var documentation = Assert.Single(parts.XmlDocumentation);
        AssertPart(
            source,
            documentation,
            "/** <summary>M.</summary> */",
            5,
            5);
        AssertPart(
            source,
            parts.Member,
            "/** <summary>M.</summary> */\n" +
            "    void M()\n" +
            "    {\n" +
            "    }",
            5,
            8);
        Assert.Equal(
            "void M()\n{\n}",
            MemberTextSlicer.ExtractMemberText(source, 7, 8, "M"));
    }

    [Theory]
    [InlineData("//// ordinary banner")]
    [InlineData("/*** ordinary banner */")]
    [InlineData("/**/")]
    public void OrdinaryCommentLookalikes_DoNotBecomeDocumentation(string comment)
    {
        string source = string.Join(
            '\n',
            "class C",
            "{",
            $"    {comment}",
            "    void M() { }",
            "}");

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 4, 4, "M"));

        Assert.Empty(parts.XmlDocumentation);
        AssertPart(source, parts.Member, "void M() { }", 4, 4);
        Assert.Equal(
            "void M() { }",
            MemberTextSlicer.ExtractMemberText(source, 4, 4, "M"));
    }

    [Theory]
    [InlineData("/// <summary>M.</summary>")]
    [InlineData("/** <summary>M.</summary> */")]
    public void GenuineDocumentationDelimiters_RemainAttached(string documentationText)
    {
        string source = string.Join(
            '\n',
            "class C",
            "{",
            $"    {documentationText}",
            "    void M() { }",
            "}");

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 4, 4, "M"));

        var documentation = Assert.Single(parts.XmlDocumentation);
        AssertPart(source, documentation, documentationText, 3, 3);
        Assert.StartsWith(
            documentationText,
            Text(source, parts.Member),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryComments_SeparateGenuineDocumentationGroups()
    {
        const string source = """
            class C
            {
                /// <summary>First.</summary>
                //// ordinary separator
                /// <summary>Second.</summary>
                /*** ordinary separator */
                /** <summary>Third.</summary> */
                void M() { }
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 8, 8, "M"));

        Assert.Collection(
            parts.XmlDocumentation,
            documentation => AssertPart(
                source,
                documentation,
                "/// <summary>First.</summary>",
                3,
                3),
            documentation => AssertPart(
                source,
                documentation,
                "/// <summary>Second.</summary>",
                5,
                5),
            documentation => AssertPart(
                source,
                documentation,
                "/** <summary>Third.</summary> */",
                7,
                7));
    }

    [Fact]
    public void SignatureAndBodyBoundaries_DistinguishBlockExpressionAndBodylessMembers()
    {
        const string source = """
            interface I
            {
                void Bodyless();
            }

            class C : I
            {
                public void Block()
                {
                }

                public int Expression(int value) => value + 1;
            }
            """;

        var bodyless = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 3, 3, "Bodyless"));
        AssertPart(source, bodyless.Declaration, "void Bodyless();", 3, 3);
        AssertPart(source, bodyless.Signature, "void Bodyless();", 3, 3);
        Assert.Null(bodyless.Body);
        Assert.Empty(bodyless.XmlDocumentation);
        Assert.Empty(bodyless.Attributes);

        var block = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 9, 9, "Block"));
        AssertPart(source, block.Signature, "public void Block()", 8, 8);
        AssertPart(source, Assert.IsType<MemberTextPart>(block.Body), "{\n    }", 9, 10);

        var expression = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 12, 12, "Expression"));
        AssertPart(
            source,
            expression.Signature,
            "public int Expression(int value)",
            12,
            12);
        AssertPart(
            source,
            Assert.IsType<MemberTextPart>(expression.Body),
            "=> value + 1;",
            12,
            12);
    }

    [Fact]
    public void ExpressionBodySignature_RetainsGenericConstraints()
    {
        const string source = """
            class C
            {
                public T Identity<T>(T value)
                    where T : class
                    => value;
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 5, 5, "Identity"));

        AssertPart(
            source,
            parts.Signature,
            "public T Identity<T>(T value)\n        where T : class",
            3,
            4);
        AssertPart(
            source,
            Assert.IsType<MemberTextPart>(parts.Body),
            "=> value;",
            5,
            5);
    }

    [Fact]
    public void ConstructorInitializer_RemainsInSignature()
    {
        const string source = """
            class C
            {
                public C()
                    : this(42)
                {
                }

                private C(int value)
                {
                }
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 5, 6, ".ctor"));

        AssertPart(source, parts.Signature, "public C()\n        : this(42)", 3, 4);
        AssertPart(source, Assert.IsType<MemberTextPart>(parts.Body), "{\n    }", 5, 6);
    }

    [Fact]
    public void ExpressionBodiedConstructorInitializer_RemainsInSignature()
    {
        const string source = """
            class C
            {
                public C() : this(42) => value = 42;

                private C(int value)
                {
                }

                private int value;
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 3, 3, ".ctor"));

        AssertPart(source, parts.Signature, "public C() : this(42)", 3, 3);
        AssertPart(
            source,
            Assert.IsType<MemberTextPart>(parts.Body),
            "=> value = 42;",
            3,
            3);
    }

    [Fact]
    public void SynthesizedInitializerOnlyConstructor_RemainsUnresolved()
    {
        const string source = """
            class C
            {
                private int value = Create();
            }
            """;

        Assert.Null(MemberTextSlicer.GetMemberTextParts(source, 3, 3, ".ctor"));
    }

    [Fact]
    public void AccessorPoint_SelectsContainingProperty()
    {
        const string source = """
            class C
            {
                public int P
                {
                    get => field;
                    set => field = value;
                }
            }
            """;

        var getter = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 5, 5, "get_P"));
        var setter = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 6, 6, "set_P"));

        Assert.Equal(getter, setter);
        AssertPart(source, getter.Signature, "public int P", 3, 3);
        AssertPart(
            source,
            Assert.IsType<MemberTextPart>(getter.Body),
            "{\n        get => field;\n        set => field = value;\n    }",
            4,
            7);
    }

    [Fact]
    public void PropertyInitializer_ExtendsTheBodyThroughItsTerminalSemicolon()
    {
        const string source = """
            class C
            {
                public int P { get; } = 42;
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 3, 3, "get_P"));

        AssertPart(source, parts.Signature, "public int P", 3, 3);
        AssertPart(
            source,
            Assert.IsType<MemberTextPart>(parts.Body),
            "{ get; } = 42;",
            3,
            3);
    }

    [Fact]
    public void UniqueSameLineParentBoundary_IsExactWhileLegacyExtractionStillRefuses()
    {
        const string source = "class C { void M() { } }";

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 1, 1, "M"));

        AssertPart(source, parts.Member, "void M() { }", 1, 1);
        AssertPart(source, parts.Signature, "void M()", 1, 1);
        AssertPart(source, Assert.IsType<MemberTextPart>(parts.Body), "{ }", 1, 1);
        Assert.Null(MemberTextSlicer.ExtractMemberText(source, 1, 1, "M"));
    }

    [Fact]
    public void SameLineSiblingDeclarations_RemainAmbiguous()
    {
        const string source = "class C\n{\n    void A() { } void B() { }\n}";

        Assert.Null(MemberTextSlicer.GetMemberTextParts(source, 3, 3, "A"));
        Assert.Null(MemberTextSlicer.GetMemberTextParts(source, 3, 3, "B"));
    }

    [Fact]
    public void ConditionalEvidence_SelectsOriginalBranchOffsets()
    {
        const string source = """
            class C
            {
            #if FIRST
                int M() => 1;
            #else
                int M() => 2;
            #endif
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(
                source,
                startLine: 6,
                endLine: 6,
                methodName: "M",
                activeLineNumbers: [6]));

        AssertPart(source, parts.Member, "int M() => 2;", 6, 6);
        Assert.Equal(source.IndexOf("int M() => 2;", StringComparison.Ordinal), parts.Member.Start);
    }

    [Fact]
    public void ConditionalEvidence_SelectsOnlyTheActiveDocumentationGroup()
    {
        const string source = """
            class C
            {
            #if FIRST
                /// <summary>First.</summary>
            #else
                /// <summary>Second.</summary>
            #endif
                int M() => 2;
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(
                source,
                startLine: 8,
                endLine: 8,
                methodName: "M",
                activeLineNumbers: [6, 8]));

        var documentation = Assert.Single(parts.XmlDocumentation);
        AssertPart(
            source,
            documentation,
            "/// <summary>Second.</summary>",
            6,
            6);
        Assert.Equal(6, parts.Member.Lines.StartLine);
        Assert.Contains(
            "#endif\n    int M() => 2;",
            Text(source, parts.Member),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConditionalCrossingAndLineDirective_RefuseCorrelation()
    {
        const string crossing = """
            class C
            {
            #if FIRST
                int M()
            #else
                int M()
            #endif
                {
                    return 1;
                }
            }
            """;
        Assert.Null(MemberTextSlicer.GetMemberTextParts(
            crossing,
            startLine: 8,
            endLine: 9,
            methodName: "M",
            activeLineNumbers: [8]));

        const string redirected = """
            class C
            {
            #line 500 "other.cs"
                int M() => 1;
            }
            """;
        Assert.Null(MemberTextSlicer.GetMemberTextParts(
            redirected,
            startLine: 4,
            endLine: 4,
            methodName: "M",
            activeLineNumbers: [4]));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void SupportedLineTerminators_PreserveOriginalOffsets(string newline)
    {
        string source = string.Join(
            newline,
            "class C",
            "{",
            "    void M() { }",
            "}");

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 3, 3, "M"));

        AssertPart(source, parts.Member, "void M() { }", 3, 3);
    }

    [Fact]
    public void SurrogatePairsBeforeTheDeclaration_CountAsTwoUtf16CodeUnits()
    {
        const string source = """
            class C
            {
                /* 😀 */ void M() { }
            }
            """;

        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(source, 3, 3, "M"));

        int expectedStart = source.IndexOf("void M()", StringComparison.Ordinal);
        Assert.Equal(expectedStart, parts.Member.Start);
        Assert.Equal(expectedStart, parts.Signature.Start);
        AssertPart(source, parts.Member, "void M() { }", 3, 3);
    }

    [Fact]
    public void ProductionExtractMemberTextDeclaration_ExposesExactRepositorySource()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(
            root,
            "src",
            "CSharpText.MemberSlicing",
            "MemberTextSlicer.cs");
        string source = File.ReadAllText(path);
        string[] lines = CSharpSourceText.SplitLines(source, 10_000);
        int bodyLine = Array.FindIndex(
            lines,
            line => line.Contains(
                "var selection = SelectRequestedDeclaration(",
                StringComparison.Ordinal)) + 1;

        Assert.True(bodyLine > 0);
        var parts = Assert.IsType<MemberTextParts>(
            MemberTextSlicer.GetMemberTextParts(
                source,
                bodyLine,
                bodyLine,
                nameof(MemberTextSlicer.ExtractMemberText)));

        string member = Text(source, parts.Member);
        Assert.StartsWith("/// <summary>", member, StringComparison.Ordinal);
        Assert.StartsWith(
            "public static string? ExtractMemberText(",
            Text(source, parts.Declaration),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/// <summary>",
            Text(source, parts.Declaration),
            StringComparison.Ordinal);
        Assert.Contains(
            "public static string? ExtractMemberText(",
            Text(source, parts.Signature),
            StringComparison.Ordinal);
        Assert.StartsWith("{", Text(source, Assert.IsType<MemberTextPart>(parts.Body)), StringComparison.Ordinal);
        Assert.EndsWith("}", member, StringComparison.Ordinal);
    }

    private static void AssertPart(
        string source,
        MemberTextPart part,
        string expectedText,
        int startLine,
        int endLine)
    {
        Assert.Equal(expectedText, Text(source, part));
        Assert.Equal(new LineRange(startLine, endLine), part.Lines);
        Assert.Equal(
            source.IndexOf(expectedText, StringComparison.Ordinal),
            part.Start);
        Assert.Equal(expectedText.Length, part.Length);
    }

    private static string Text(string source, MemberTextPart part) =>
        source.Substring(part.Start, part.Length);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the dotnet-inspect repository root.");
    }
}
