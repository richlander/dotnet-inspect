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

    [Fact]
    public void AutoPropertyAccessor_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string? Before { get; set; }",       // 3
            "",                                             // 4
            "    public string? Target { get; set; }",       // 5  <- StartLine/EndLine
            "",                                             // 6
            "    public string? After { get; set; }",        // 7
            "}");                                           // 8

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Target");

        Assert.Equal("public string? Target { get; set; }", body);
    }

    [Fact]
    public void ExpressionBodiedMember_FirstInType_ExcludesEnclosingTypeHeader()
    {
        var source = Lines(
            "public sealed record R(int Value)",             // 1
            "{",                                            // 2
            "    public int Doubled => Value * 2;",          // 3  <- StartLine/EndLine
            "}");                                           // 4

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "get_Doubled");

        Assert.Equal("public int Doubled => Value * 2;", body);
    }

    [Fact]
    public void BlockBodiedPropertyAccessor_WalksBackwardToPropertyDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string? Tfm",                       // 3
            "    {",                                        // 4
            "        get => _override ?? Compute();",        // 5  <- StartLine/EndLine
            "        set => _override = value;",             // 6
            "    }",                                        // 7
            "}");                                           // 8

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Tfm");

        Assert.Equal(
            "public string? Tfm\n{\n    get => _override ?? Compute();",
            body);
    }

    [Fact]
    public void RecursiveFirstStatement_SpellingMethodName_StillCapturesSignature()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Fact(int n)",                    // 3
            "    {",                                        // 4
            "        return n <= 1 ? 1 : n * Fact(n - 1);",  // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Fact");

        Assert.Equal(
            "public int Fact(int n)\n{\n    return n <= 1 ? 1 : n * Fact(n - 1);\n}",
            body);
    }

    [Theory]
    [InlineData("unsafe")]
    [InlineData("extern")]
    [InlineData("async")]
    [InlineData("partial")]
    [InlineData("sealed override")]
    [InlineData("required")]
    public void ModifierLedDeclaration_SequencePointOnDeclaration_ExcludesPrecedingMember(string modifiers)
    {
        var declaration = $"    {modifiers} int Target => 1;";
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Before() => 0;",                 // 3
            "",                                             // 4
            declaration,                                    // 5  <- StartLine/EndLine
            "}");                                           // 6

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Target");

        Assert.Equal(declaration.TrimStart(), body);
    }

    [Fact]
    public void TupleReturningDeclaration_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Before() => 0;",                 // 3
            "",                                             // 4
            "    public (int X, int Y) Target => (1, 2);",   // 5  <- StartLine/EndLine
            "}");                                           // 6

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Target");

        Assert.Equal("public (int X, int Y) Target => (1, 2);", body);
    }

    [Fact]
    public void UnsafeBlockStatement_IsNotMistakenForDeclaration_SignatureStillCaptured()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public void Target()",                      // 3
            "    {",                                        // 4
            "        unsafe { Poke(); }",                    // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            "public void Target()\n{\n    unsafe { Poke(); }\n}",
            body);
    }

    [Fact]
    public void IdentifierSharingModifierPrefix_IsNotMistakenForDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public void Target()",                      // 3
            "    {",                                        // 4
            "        internalCounter = 1;",                  // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            "public void Target()\n{\n    internalCounter = 1;\n}",
            body);
    }

    [Fact]
    public void ModifierlessInterfaceMember_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "public interface IDefault",                     // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    int Target => 1;",                          // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

        Assert.Equal("int Target => 1;", body);
    }

    [Fact]
    public void ImplicitlyPrivateAutoProperty_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    string? Before { get; set; }",              // 3
            "    string? Target { get; set; }",              // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "set_Target");

        Assert.Equal("string? Target { get; set; }", body);
    }

    [Fact]
    public void ExplicitInterfaceImplementation_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "class C : IDefault",                           // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    int IDefault.Target => 1;",                 // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 4, endLine: 4, methodName: "IDefault.get_Target");

        Assert.Equal("int IDefault.Target => 1;", body);
    }

    [Theory]
    [InlineData("return Target(n - 1);")]
    [InlineData("_cache = Target();")]
    [InlineData("var next = Target();")]
    public void BodyLineSpellingMemberName_IsNotMistakenForDeclaration(string firstStatement)
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    int Target(int n)",                         // 3
            "    {",                                        // 4
            $"        {firstStatement}",                     // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            $"int Target(int n)\n{{\n    {firstStatement}\n}}",
            body);
    }

    [Fact]
    public void LocalDeclarationSpellingMemberName_IsNotMistakenForDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public void Run()",                         // 3
            "    {",                                        // 4
            "        Widget Target;",                        // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            "public void Run()\n{\n    Widget Target;\n}",
            body);
    }

    [Theory]
    [InlineData("Helper.Target();")]
    [InlineData("_helper.Target();")]
    [InlineData("Factory<int>.Target();")]
    [InlineData("Outer.Inner.Target();")]
    public void QualifiedCallSpellingMemberName_IsNotMistakenForDeclaration(string firstStatement)
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    void Target()",                             // 3
            "    {",                                        // 4
            $"        {firstStatement}",                     // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            $"void Target()\n{{\n    {firstStatement}\n}}",
            body);
    }

    [Fact]
    public void ExplicitInterfaceImplementation_IsStillRecognized_DespiteQualifiedNameGuard()
    {
        var source = Lines(
            "class C : IDefault",                           // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    int IDefault.Target => 1;",                 // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 4, endLine: 4, methodName: "IDefault.get_Target");

        Assert.Equal("int IDefault.Target => 1;", body);
    }

    [Fact]
    public void ModifierlessIndexer_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "public interface IDefault",                     // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    int this[int index] => index;",             // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Item");

        Assert.Equal("int this[int index] => index;", body);
    }

    [Fact]
    public void ExplicitInterfaceIndexer_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "class C : IDefault",                           // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    int IDefault.this[int index] => index;",    // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 4, endLine: 4, methodName: "IDefault.set_Item");

        Assert.Equal("int IDefault.this[int index] => index;", body);
    }

    [Fact]
    public void IndexerAccessInBody_IsNotMistakenForIndexerDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int this[int index]",                // 3
            "    {",                                        // 4
            "        get => _items[index];",                 // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Item");

        Assert.Equal(
            "public int this[int index]\n{\n    get => _items[index];\n}",
            body);
    }

    [Fact]
    public void GenericModifierlessMethod_SequencePointOnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "public interface IDefault",                     // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    T Target<T>(T value) => value;",            // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "Target");

        Assert.Equal("T Target<T>(T value) => value;", body);
    }

    [Fact]
    public void GenericReturnTypeDeclaration_IsRecognized()
    {
        var source = Lines(
            "public interface IDefault",                     // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    Dictionary<string, int> Target => new();",  // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

        Assert.Equal("Dictionary<string, int> Target => new();", body);
    }

    [Fact]
    public void ReturnTypeSpelledLikeMemberName_IsStillRecognizedAsDeclaration()
    {
        var source = Lines(
            "public interface IDefault",                     // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    CancellationToken CancellationToken => default;", // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(
            source, startLine: 4, endLine: 4, methodName: "get_CancellationToken");

        Assert.Equal("CancellationToken CancellationToken => default;", body);
    }

    [Fact]
    public void BlockOpeningOnDeclarationLine_StillRecoversClosingBrace()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() {",                     // 3  <- StartLine
            "        return 1;",                             // 4  <- EndLine
            "    }",                                         // 5
            "}");                                            // 6

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

        Assert.Equal(
            "public int Target() {\n    return 1;\n}",
            body);
    }

    [Fact]
    public void SelfTerminatingSingleLineDeclaration_StillSuppressesForwardScan()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() { return 1; }",         // 3  <- StartLine/EndLine
            "}");                                           // 4

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

        Assert.Equal("public int Target() { return 1; }", body);
    }

    [Fact]
    public void ModifierlessTupleReturnDeclaration_ExcludesPrecedingMember()
    {
        var source = Lines(
            "public interface IDefault",                     // 1
            "{",                                            // 2
            "    int Before => 0;",                          // 3
            "    (int X, int Y) Target => (1, 2);",          // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

        Assert.Equal("(int X, int Y) Target => (1, 2);", body);
    }

    [Fact]
    public void DeconstructionAssignment_IsNotMistakenForTupleDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public void Run()",                         // 3
            "    {",                                        // 4
            "        (var a, var b) = Target();",            // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            "public void Run()\n{\n    (var a, var b) = Target();\n}",
            body);
    }

    [Theory]
    [InlineData("ref int Target() => ref _value;")]
    [InlineData("new int Target => 1;")]
    [InlineData("ref readonly int Target() => ref _value;")]
    public void RefOrNewLedDeclaration_ExcludesPrecedingMember(string declaration)
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Before => 0;",                   // 3
            $"    {declaration}",                            // 4  <- StartLine/EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

        Assert.Equal(declaration, body);
    }

    [Theory]
    [InlineData("new Widget().Configure();")]
    [InlineData("ref var slot = ref _items[0];")]
    public void RefOrNewLedStatement_IsNotMistakenForDeclaration(string firstStatement)
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public void Target()",                      // 3
            "    {",                                        // 4
            $"        {firstStatement}",                     // 5  <- StartLine/EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

        Assert.Equal(
            $"public void Target()\n{{\n    {firstStatement}\n}}",
            body);
    }

    [Fact]
    public void BraceInsideStringLiteral_DoesNotLookLikeAnOpenBlock()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target =>",                   // 3  <- StartLine
            "        \"{\";",                                // 4  <- EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "get_Target");

        Assert.Equal("public string Target =>\n    \"{\";", body);
    }

    [Fact]
    public void BraceInsideComment_DoesNotLookLikeAnOpenBlock()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target => /* { */",           // 3  <- StartLine
            "        \"x\";",                                // 4  <- EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "get_Target");

        Assert.Equal("public string Target => /* { */\n    \"x\";", body);
    }

    [Fact]
    public void ClosingBraceInsideStringLiteral_DoesNotLookLikeAClosedBlock()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Before() { return 0; }",         // 3
            "    public string Target() {",                  // 4  <- StartLine
            "        return \"}\";",                          // 5  <- EndLine
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 4, endLine: 5, methodName: "Target");

        Assert.Equal(
            "public string Target() {\n    return \"}\";\n}",
            body);
    }

    [Fact]
    public void ClosingBraceInsideVerbatimString_DoesNotLookLikeAClosedBlock()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target() {",                  // 3  <- StartLine
            "        return @\"}\";",                         // 4  <- EndLine
            "    }",                                         // 5
            "}");                                            // 6

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

        Assert.Equal(
            "public string Target() {\n    return @\"}\";\n}",
            body);
    }

    [Fact]
    public void OpeningBraceInsideVerbatimInterpolatedString_DoesNotLookLikeAnOpenBlock()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target() => @$\"first",       // 3  <- StartLine
            "        {{\";",                                 // 4  <- EndLine
            "}");                                           // 5

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

        Assert.Equal(
            "public string Target() => @$\"first\n    {{\";",
            body);
    }

    [Fact]
    public void TrailingLineCommentAfterTerminator_DoesNotTriggerTheForwardScan()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() => 0; // opens nothing {", // 3  <- StartLine/EndLine
            "}");                                           // 4

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

        Assert.Equal("public int Target() => 0; // opens nothing {", body);
    }

    [Fact]
    public void TrailingBlockCommentAfterTerminator_DoesNotTriggerTheForwardScan()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() => 0; /* note */",      // 3  <- StartLine/EndLine
            "}");                                           // 4

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

        Assert.Equal("public int Target() => 0; /* note */", body);
    }

    [Fact]
    public void TerminatorInsideTrailingComment_DoesNotEndTheDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() // ;",                  // 3  <- StartLine
            "    {",                                         // 4
            "        return 0;",                             // 5
            "    }",                                         // 6
            "}");                                            // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 5, methodName: "Target");

        Assert.Equal("public int Target() // ;\n{\n    return 0;\n}", body);
    }

    [Fact]
    public void MultiLineRawStringLiteral_FallsBackToTheForwardScan()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target() => \"\"\"",         // 3  <- StartLine
            "        a",                                    // 4
            "        \"\"\";",                              // 5  <- EndLine
            "    }",                                        // 6
            "}");                                           // 7

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 5, methodName: "Target");

        Assert.Equal("public string Target() => \"\"\"\n    a\n    \"\"\";\n}", body);
    }

    [Fact]
    public void SingleLineRawStringLiteral_StillEndsTheDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target() => \"\"\"a\"\"\";",  // 3  <- StartLine/EndLine
            "}");                                           // 4

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

        Assert.Equal("public string Target() => \"\"\"a\"\"\";", body);
    }
}