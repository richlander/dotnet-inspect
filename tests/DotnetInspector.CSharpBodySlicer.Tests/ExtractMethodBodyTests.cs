namespace DotnetInspector.CSharpBodySlicer.Tests;

/// <summary>
/// Characterization tests for <see cref="BodySlicer.ExtractMethodBody"/>, the
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 6, methodName: "Add");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 7, endLine: 9, methodName: "Add");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 6, endLine: 8, methodName: "Add");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 5, methodName: "Add");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 7, endLine: 8, methodName: "Finalize", isDestructor: true);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 6, methodName: "op_OnesComplement", isDestructor: false);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Build", isDestructor: false);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 8, endLine: 8, methodName: "Finalize", isDestructor: true);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 9, endLine: 9, methodName: "Finalize", isDestructor: true);

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 6, endLine: 7, methodName: "Finalize", isDestructor: true);

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

        var body = BodySlicer.ExtractMethodBody(
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

        var body = BodySlicer.ExtractMethodBody(
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

        var body = BodySlicer.ExtractMethodBody(
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
        Assert.Equal(expected, BodySlicer.SimpleTypeName(fullName));
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "get_Doubled");

        Assert.Equal("public int Doubled => Value * 2;", body);
    }

    /// <summary>
    /// A block-bodied property's accessor has no declaration of its own, so its sequence points
    /// select the property — and the whole property is what gets sliced.
    /// <para>
    /// The backward scan used to stop before the sibling accessor, on the reasoning that accessors
    /// resolve separately and the getter's source must not take in the setter's body. That produced
    /// <c>"public string? Tfm\n{\n    get =&gt; _override ?? Compute();"</c>: a property with an
    /// unclosed brace and a missing setter, which is not a declaration at all. Splitting a property
    /// at an accessor boundary cannot yield valid C#, so the choice is between a fragment and the
    /// enclosing declaration, and only one of those is something a reader can be shown. This is one
    /// of the 17 under-captures the parse-validity gate measured, all of which are now gone; see
    /// <see cref="AuthoredSourceValidityTests"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void BlockBodiedPropertyAccessor_SlicesTheWholeProperty()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string? Tfm",                       // 3
            "    {",                                        // 4
            "        get => _override ?? Compute();",        // 5  <- StartLine/EndLine
            "        set => _override = value;",             // 6
            "    }",                                         // 7
            "}");                                           // 8

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Tfm");

        Assert.Equal(
            "public string? Tfm\n{\n    get => _override ?? Compute();\n    set => _override = value;\n}",
            body);

        // The setter selects the same declaration: one property, one slice, whichever accessor
        // the PDB happened to report.
        Assert.Equal(body, BodySlicer.ExtractMethodBody(source, startLine: 6, endLine: 6, methodName: "set_Tfm"));
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Fact");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "set_Target");

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

        var body = BodySlicer.ExtractMethodBody(
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Item");

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

        var body = BodySlicer.ExtractMethodBody(
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "get_Item");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(
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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 4, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 5, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "get_Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 4, endLine: 5, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

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

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 5, methodName: "Target");

        Assert.Equal("public int Target() // ;\n{\n    return 0;\n}", body);
    }

    /// <summary>
    /// A raw string literal spanning lines is tracked, not abandoned. It used to leave the depth
    /// count untracked, which forced the forward scan to run and append the enclosing type's
    /// brace to an expression-bodied member that owns none.
    /// </summary>
    [Fact]
    public void MultiLineRawStringLiteral_IsTrackedAcrossLines()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target() => \"\"\"",         // 3  <- StartLine
            "        a",                                    // 4
            "        \"\"\";",                              // 5  <- EndLine
            "}");                                           // 6

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 5, methodName: "Target");

        Assert.Equal("public string Target() => \"\"\"\n    a\n    \"\"\";", body);
    }

    [Fact]
    public void SingleLineRawStringLiteral_StillEndsTheDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public string Target() => \"\"\"a\"\"\";",  // 3  <- StartLine/EndLine
            "}");                                           // 4

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

        Assert.Equal("public string Target() => \"\"\"a\"\"\";", body);
    }

    [Fact]
    public void BlankLineBelowDeclaration_DoesNotEraseTheTerminator()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() => 0;",                 // 3  <- StartLine
            "",                                             // 4  <- EndLine
            "}");                                           // 5

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

        Assert.Equal("public int Target() => 0;", body);
    }

    /// <summary>
    /// The sequence-point range may extend past the declaration onto a trailing comment. The slice
    /// is the declaration's own span, so the comment is not part of it — a comment below a member
    /// is the file's, or the next member's leading trivia, never a tail of the member above.
    /// The backward-scanning slicer returned the range it was handed and included the comment.
    /// </summary>
    [Fact]
    public void CommentOnlyLineBelowDeclaration_IsNotPartOfTheMember()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    public int Target() => 0;",                 // 3  <- StartLine
            "    // trailing note",                          // 4  <- EndLine
            "}");                                           // 5

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 4, methodName: "Target");

        Assert.Equal("public int Target() => 0;", body);
    }

    /// <summary>
    /// Gates the scanner rule that the <c>}</c> closing an interpolation hole is part of the
    /// enclosing literal and therefore must not become the line's last significant character.
    /// <see cref="BodySlicer.ExtractMethodBody(string, int, int, string?)"/> asks
    /// <c>EndsDeclaration</c> whether the range already terminates; a hole closer that counted
    /// as a terminator would answer yes here and stop the slice on line 3, dropping the raw
    /// literal's closing delimiter and the method's own <c>}</c>.
    ///
    /// The range must be a single line for this to be observable: over a multi-line range
    /// <c>EndsDeclaration</c> and <c>IndexPastClosingBrace</c> compute brace depth over the
    /// same span, so whenever the misclassification would flip the former to <c>true</c> the
    /// latter would have declined to extend anyway. This is the gate for that rule — the
    /// corpus contains no interpolation hole closing on the last significant column of a
    /// single-line member, so removing this test leaves the rule unverified.
    /// </summary>
    [Fact]
    public void HoleClosingBraceOnASingleLineRange_DoesNotTerminateTheDeclaration()
    {
        var source = Lines(
            "class C",                                      // 1
            "{",                                            // 2
            "    void Target() { Log($\"\"\"x{y}",           // 3  <- StartLine, EndLine
            "        \"\"\"); }",                            // 4
            "",                                             // 5
            "    void Other() { }",                          // 6
            "}");                                           // 7

        var body = BodySlicer.ExtractMethodBody(source, startLine: 3, endLine: 3, methodName: "Target");

        Assert.Equal("void Target() { Log($\"\"\"x{y}\n    \"\"\"); }", body);
    }

    /// <summary>
    /// A line that closes a verbatim literal carried in from above and then declares a sibling
    /// accessor is a declaration from the point the literal ends. Asking only whether the line
    /// <i>began</i> inside a literal suppressed the question on exactly the line that answers it,
    /// and the getter's slice then ran on past the property (adversarial review, GPT).
    /// <para>
    /// The slice is now the whole property, so "swallowing the sibling accessor" is no longer a
    /// failure mode — a property's accessors are inside it by definition. The lexical question the
    /// fixture was built for survives, and is what is gated here: the carried literal must not hide
    /// the brace that closes the property, or the slice runs on into the enclosing type.
    /// </para>
    /// </summary>
    [Fact]
    public void AccessorClosingACarriedLiteral_StillEndsAtThePropertysBrace()
    {
        var source = string.Join('\n',
            "class C",
            "{",
            "    public string P",
            "    {",
            "        get",
            "        {",
            "            return @\"a",
            "\" set { _v = value; }",
            "        }",
            "    }",
            "}") + "\n";

        var body = BodySlicer.ExtractMethodBody(source, 5, 7, "get_P");

        Assert.NotNull(body);
        Assert.StartsWith("    public string P", body);

        // Not dedented, and correctly so: the literal's continuation line starts at column 0, so
        // the common indent is 0 and removing four columns would corrupt the string's contents.
        Assert.Contains("\n\" set { _v = value; }\n", body);

        // Ends at the property's own closing brace: the enclosing type is not in the slice.
        Assert.DoesNotContain("class C", body);
        Assert.EndsWith("}", body);
        Assert.Equal(8, body.Split('\n').Length);
    }


    /// <summary>
    /// The backward signature scan reads trivia off each line above the slice. A line that ends
    /// in a lone "/" has no character after it to classify, and reading one throws rather than
    /// deciding the line is not a comment (adversarial review, Gemini).
    /// </summary>
    [Fact]
    public void SignatureScanOverALineEndingInASlash_DoesNotReadPastIt()
    {
        var source = string.Join('\n',
            "class C",
            "{",
            "    public /",
            "    {",
            "        Body();",
            "    }",
            "}") + "\n";

        var thrown = Record.Exception(() => BodySlicer.ExtractMethodBody(source, 5, 6, "M"));

        Assert.Null(thrown);
    }

    /// <summary>
    /// The same shape one character later: a declaration line ending in "=" is asked whether an
    /// expression body ("=>") follows the member's name, and the ">" it would read is past the
    /// end of the line (adversarial review, Gemini).
    /// </summary>
    [Fact]
    public void SignatureScanOverALineEndingInAnEquals_DoesNotReadPastIt()
    {
        var source = string.Join('\n',
            "class C",
            "{",
            "    int Target =",
            "    {",
            "        Body();",
            "    }",
            "}") + "\n";

        // DeclaresMember only ever examines the slice's own first line, so the declaration
        // under test has to be that line.
        // The scan must reach a decision. Declining is a decision; throwing is not.
        var thrown = Record.Exception(() => BodySlicer.ExtractMethodBody(source, 3, 6, "get_Target"));

        Assert.Null(thrown);
    }

    /// <summary>
    /// A destructor's declaring type is matched against the source a character at a time, and a
    /// unicode escape is two characters. A name ending in a lone backslash has no second one
    /// (adversarial review, Gemini).
    /// </summary>
    [Fact]
    public void DestructorMatchOverANameEndingInABackslash_DoesNotReadPastIt()
    {
        var source = string.Join('\n',
            "class C",
            "{",
            "    ~\\",
            "    {",
            "        Body();",
            "    }",
            "}") + "\n";

        var thrown = Record.Exception(
            () => BodySlicer.ExtractMethodBody(source, 5, 6, "Finalize", isDestructor: true, destructorTypeName: "C"));

        Assert.Null(thrown);
    }

    /// <summary>
    /// A body whose braces never close leaves every open row's span a guess, so the index withholds
    /// it and there is nothing to slice.
    /// <para>
    /// The backward-scanning slicer returned <c>"void M()\n{\n    if (x)\n    {"</c> here — a
    /// truncated fragment that does not parse, presented as the member's source. Absent source is
    /// the correct answer for a file the scan could not measure: a truncated file must not be
    /// mistaken for a measured span. The clamp the old scan needed (its forward limit could exceed
    /// the file length and read past the last line) has no counterpart here, because no scan runs.
    /// </para>
    /// </summary>
    [Fact]
    public void UnbalancedBodyAtEndOfFile_ReportsAbsentRatherThanATruncatedFragment()
    {
        var source = string.Join('\n',
            "class C",
            "{",
            "    void M()",
            "    {",
            "        if (x)",
            "        {") + "\n";

        Assert.Null(BodySlicer.ExtractMethodBody(source, 5, 6, "M"));

        // The same member in a file that closes its braces does slice, so absence is caused by the
        // unbalanced file and not by the fixture's shape.
        var closed = string.Join('\n',
            "class C",
            "{",
            "    void M()",
            "    {",
            "        if (x)",
            "        {",
            "        }",
            "    }",
            "}") + "\n";

        Assert.Equal(
            "void M()\n{\n    if (x)\n    {\n    }\n}",
            BodySlicer.ExtractMethodBody(closed, 5, 6, "M"));
    }
}
