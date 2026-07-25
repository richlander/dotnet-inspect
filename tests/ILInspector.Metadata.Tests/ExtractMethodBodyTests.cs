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

    [Fact]
    public void Destructor_FirstSequencePointInsideBody_DoesNotStopAtBodyComplement()
    {
        // Regression guard (adversarial review): "#line hidden" can push the first
        // visible sequence point past the signature and initial statements, so the
        // backward scan begins inside the body. A body complement expression such as
        // "int x = ~0;" must NOT be mistaken for the destructor signature; the scan
        // must walk up to the real "~C()" line and never leak the preceding member.
        var source = Lines(
            "class C",                          // 1
            "{",                                // 2
            "    internal int Preceding;",      // 3  <- must NOT be captured
            "",                                 // 4
            "    ~C()",                         // 5  <- real signature
            "    {",                            // 6
            "        int x = ~0;",              // 7  <- must NOT be treated as signature
            "        System.GC.KeepAlive(x);",  // 8  <- StartLine (first visible seq point)
            "    }",                            // 9
            "}");                               // 10

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 8, endLine: 8, methodName: "Finalize", isDestructor: true);

        Assert.StartsWith("~C()", body, System.StringComparison.Ordinal);
        Assert.Contains("int x = ~0;", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Preceding", body, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("~mask;")]          // bitwise complement of a local, bare
    [InlineData("~Mask;")]          // bitwise complement of a constant/field
    [InlineData("~Compute(x);")]    // bitwise complement of an invocation (argument-bearing)
    public void Destructor_HiddenBodyTildeComplement_DoesNotStopAtNonEmptyParenOrBareComplement(string bodyComplementLine)
    {
        // Regression guard (adversarial review): "#line hidden" can push the first
        // visible sequence point past the signature, so the backward scan begins
        // inside the body. A tilde-identifier body statement that is NOT the
        // parameterless "~Type()" signature — a bare complement ("~mask;"), a
        // complemented field ("~Mask;"), or a complemented invocation
        // ("~Compute(x);") — must NOT be mistaken for the destructor signature.
        // The scan must walk up to the real "~C()" line and never leak the
        // preceding member.
        var source = Lines(
            "class C",                          // 1
            "{",                                // 2
            "    internal int Preceding;",      // 3  <- must NOT be captured
            "",                                 // 4
            "    ~C()",                         // 5  <- real signature
            "    {",                            // 6
            "        int x =",                  // 7
            "            " + bodyComplementLine,// 8  <- must NOT be treated as signature
            "        System.GC.KeepAlive(x);",  // 9  <- StartLine (first visible seq point)
            "    }",                            // 10
            "}");                               // 11

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 9, endLine: 9, methodName: "Finalize", isDestructor: true);

        Assert.StartsWith("~C()", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Preceding", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Destructor_UnicodeEscapedTypeName_StopsAtTildeSignature()
    {
        // Regression guard (adversarial review): a destructor's type name may be
        // spelled with a Unicode escape ("~\u0043()" for "~C()"). The signature
        // grammar admits backslashes in the identifier run so the escaped form is
        // still recognized and the preceding member is not leaked.
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    internal static bool s_flag;",     // 3  <- must NOT be captured
            "",                                     // 4
            "    ~\\u0043()",                       // 5
            "    {",                                // 6  <- StartLine
            "        s_flag = true;",               // 7  <- EndLine
            "    }",                                // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true);

        Assert.StartsWith("~\\u0043()", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("s_flag;", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Destructor_WrappedParameterList_TypeNameAnchored_NotTruncated()
    {
        // Regression guard (adversarial review): a destructor whose empty parameter
        // list wraps onto following lines ("unsafe ~ Sample" / "(" / ")") has no
        // "()" on the signature line. Anchoring on the declaring type name (rather
        // than requiring "()" on the same line) still stops at the signature and
        // does not leak the preceding member.
        var source = Lines(
            "class Sample",                         // 1
            "{",                                    // 2
            "    internal int Preceding;",          // 3  <- must NOT be captured
            "",                                     // 4
            "    unsafe ~ Sample",                  // 5  <- signature start
            "    (",                                // 6
            "    )",                                // 7
            "    {",                                // 8
            "        int x = 0;",                   // 9  <- StartLine (first visible seq point)
            "    }",                                // 10
            "}");                                   // 11

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 9, endLine: 9, methodName: "Finalize", isDestructor: true, destructorTypeName: "Sample");

        Assert.StartsWith("unsafe ~ Sample", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Preceding", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Destructor_HiddenZeroArgInvocationComplement_TypeNameAnchored_NotStopped()
    {
        // Regression guard (adversarial review): a "#line hidden" body complement of
        // a zero-argument invocation ("~Compute();") satisfies a bare "~Identifier()"
        // grammar but is NOT the declaring type. Anchoring on the type name walks past
        // it to the real "~C()" signature.
        var source = Lines(
            "class C",                          // 1
            "{",                                // 2
            "    internal int Preceding;",      // 3  <- must NOT be captured
            "",                                 // 4
            "    ~C()",                         // 5  <- real signature
            "    {",                            // 6
            "        int x =",                  // 7
            "            ~Compute();",          // 8  <- must NOT be treated as signature
            "        System.GC.KeepAlive(x);",  // 9  <- StartLine (first visible seq point)
            "    }",                            // 10
            "}");                               // 11

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 9, endLine: 9, methodName: "Finalize", isDestructor: true, destructorTypeName: "C");

        Assert.StartsWith("~C()", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Preceding", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Destructor_UnicodeEscapedTypeName_TypeNameAnchored_Matches()
    {
        // Regression guard (adversarial review): a Unicode-escaped type name
        // ("~\u0043()" for "~C()") is decoded when matched against the declaring
        // type name "C".
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    internal static bool s_flag;",     // 3  <- must NOT be captured
            "",                                     // 4
            "    ~\\u0043()",                       // 5
            "    {",                                // 6  <- StartLine
            "        s_flag = true;",               // 7  <- EndLine
            "    }",                                // 8
            "}");                                   // 9

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true, destructorTypeName: "C");

        Assert.StartsWith("~\\u0043()", body, System.StringComparison.Ordinal);
        Assert.DoesNotContain("s_flag;", body, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C", "C")]
    [InlineData("NS.C", "C")]
    [InlineData("NS.Outer+Inner", "Inner")]
    [InlineData("NS.C`1", "C")]
    [InlineData("NS.Outer`1+Inner", "Inner")]
    [InlineData("NS.Outer+Inner`2", "Inner")]
    public void SimpleTypeName_StripsNamespaceNestingAndArity(string fullName, string expected)
    {
        Assert.Equal(expected, SourceLinkResolver.SimpleTypeName(fullName));
    }
}
