namespace ILInspector.Metadata.Tests;

/// <summary>
/// Characterization tests for <see cref="SourceLinkResolver.ExtractMethodBody"/>, the
/// signature-boundary reconstruction heuristic moved out of the CLI. These lock in the
/// existing behavior (line numbers are 1-based sequence-point ranges).
/// </summary>
public class ExtractMethodBodyTests
{
    private static string Lines(params string[] lines) => string.Join('\n', lines);

    [Fact]
    public void SimpleMethod_ReconstructsSignatureThroughClosingBrace_Dedented()
    {
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    public int Add(int a, int b)",     // 3
            "    {",                                // 4  <- StartLine
            "        return a + b;",                // 5
            "    }",                                // 6  <- EndLine
            "",                                     // 7
            "    public int Sub() => 0;",           // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 6, methodName: "Add");

        Assert.Equal("public int Add(int a, int b)\n{\n    return a + b;\n}", body);
    }

    [Fact]
    public void MultiLineSignature_WalksBackwardToSignatureStart()
    {
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    /// <summary>M.</summary>",        // 3
            "    public int Add(",                  // 4
            "        int a,",                       // 5
            "        int b)",                       // 6
            "    {",                                // 7  <- StartLine
            "        return a + b;",                // 8
            "    }",                                // 9  <- EndLine
            "",                                     // 10
            "    public int X() => 0;",             // 11
            "}");                                   // 12

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 7, endLine: 9, methodName: "Add");

        Assert.Equal(
            "public int Add(\n    int a,\n    int b)\n{\n    return a + b;\n}",
            body);
    }

    [Fact]
    public void DocCommentsAndAttributesAboveSignature_AreExcluded()
    {
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    /// <summary>Adds.</summary>",     // 3
            "    [Obsolete]",                       // 4
            "    public int Add(int a, int b)",     // 5
            "    {",                                // 6  <- StartLine
            "        return a + b;",                // 7
            "    }",                                // 8  <- EndLine
            "",                                     // 9
            "    public int X() => 0;",             // 10
            "}");                                   // 11

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 6, endLine: 8, methodName: "Add");

        Assert.Equal("public int Add(int a, int b)\n{\n    return a + b;\n}", body);
    }

    [Fact]
    public void ForwardScan_IncludesClosingBrace_WhenEndLineStopsAtLastStatement()
    {
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    public int Add(int a, int b)",     // 3
            "    {",                                // 4  <- StartLine
            "        return a + b;",                // 5  <- EndLine (last statement, not the brace)
            "    }",                                // 6
            "",                                     // 7
            "    void Y() {}",                      // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 5, methodName: "Add");

        Assert.Equal("public int Add(int a, int b)\n{\n    return a + b;\n}", body);
    }

    [Fact]
    public void Destructor_StopsAtTildeSignature_NotPrecedingMember()
    {
        // Regression guard (adversarial review): a finalizer's metadata name is
        // "Finalize", but its C# source line is "~Type()". With the destructor
        // identity supplied by the caller, the backward scan must stop at the "~"
        // declaration line; otherwise it walks past into the preceding member
        // (e.g. an accessibility-prefixed field), leaking unrelated declarations
        // into Original Source / Source Diff.
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    internal static bool s_flag;",     // 3  <- must NOT be captured
            "",                                     // 4
            "    // destructor comment",            // 5
            "    ~C()",                             // 6
            "    {",                                // 7  <- StartLine
            "        s_flag = true;",               // 8  <- EndLine
            "    }",                                // 9
            "}");                                   // 10

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 7, endLine: 8, methodName: "Finalize", isDestructor: true);

        Assert.Equal("~C()\n{\n    s_flag = true;\n}", body);
        Assert.DoesNotContain("s_flag;", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void OnesComplementOperator_SplitSignature_NotTruncatedByTildeStop()
    {
        // Regression guard (adversarial review): the "~" stop is keyed on the
        // caller-supplied destructor identity, not the source text. A user-defined
        // unary complement operator (isDestructor: false) whose signature is split
        // so a line begins with "~" (e.g. "public static C operator" /
        // "~(C value)") must still capture the full signature; the "~" line must
        // not be mistaken for a signature start.
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    public static C operator",         // 3  <- must be captured
            "        ~(C value)",                   // 4
            "    {",                                // 5  <- StartLine
            "        return value;",                // 6  <- EndLine
            "    }",                                // 7
            "}");                                   // 8

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 6, methodName: "op_OnesComplement", isDestructor: false);

        Assert.Contains("public static C operator", body, System.StringComparison.Ordinal);
        Assert.Contains("~(C value)", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void NonDestructor_MultilineDefaultParameterWithComplement_NotTruncated()
    {
        // Regression guard (adversarial review): an ordinary method whose split
        // signature places a bitwise-complement default on its own line
        // ("int x =" / "~DefaultMask") must NOT be treated as a destructor start.
        // Because the caller reports isDestructor: false, the "~" line is not a
        // stop, so the method declaration line is preserved.
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    const int Cap = 1;",               // 3
            "    public int Build(int x =",         // 4  <- must be captured
            "        ~Cap)",                        // 5
            "    {",                                // 6  <- StartLine
            "        return x;",                    // 7  <- EndLine
            "    }",                                // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Build", isDestructor: false);

        Assert.Contains("public int Build(int x =", body, System.StringComparison.Ordinal);
        Assert.Contains("~Cap)", body, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("~C()")]
    [InlineData("~ C()")]
    public void Destructor_StopsAtTildeSignature_RegardlessOfSpacing(string destructorLine)
    {
        // Regression guard (adversarial review): with the destructor identity
        // supplied by the caller, ANY leading-"~" spelling terminates the scan,
        // including a legal space between the tilde and the type name ("~ C()").
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    internal static bool s_flag;",     // 3  <- must NOT be captured
            "",                                     // 4
            "    " + destructorLine,                // 5
            "    {",                                // 6  <- StartLine
            "        s_flag = true;",               // 7  <- EndLine
            "    }",                                // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true);

        Assert.StartsWith("~", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("s_flag;", body, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unsafe ~C()")]
    [InlineData("extern ~C()")]
    public void Destructor_WithLeadingModifier_StopsAtTildeSignature(string destructorLine)
    {
        // Regression guard (adversarial review): a destructor may carry the legal
        // `unsafe`/`extern` modifiers, so its signature line begins with a keyword
        // rather than "~". Because a genuine destructor is parameterless (identity
        // supplied by the caller), the scan may stop at a tilde ANYWHERE on the
        // line and still captures the full modifier-prefixed signature without
        // leaking the preceding member.
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    internal static bool s_flag;",     // 3  <- must NOT be captured
            "",                                     // 4
            "    " + destructorLine,                // 5
            "    {",                                // 6  <- StartLine
            "        s_flag = true;",               // 7  <- EndLine
            "    }",                                // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true);

        Assert.StartsWith(destructorLine, body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("s_flag;", body, System.StringComparison.Ordinal);
    }
}
