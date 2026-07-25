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
        // "Finalize", but its C# source line is "~Type()". The backward scan must
        // stop at the "~" declaration line; otherwise it walks past into the
        // preceding member (e.g. an accessibility-prefixed field), leaking
        // unrelated declarations into Original Source / Source Diff.
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

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 7, endLine: 8, methodName: "Finalize");

        Assert.Equal("~C()\n{\n    s_flag = true;\n}", body);
        Assert.DoesNotContain("s_flag;", body, System.StringComparison.Ordinal);
    }

    [Fact]
    public void OnesComplementOperator_SplitSignature_NotTruncatedByTildeStop()
    {
        // Regression guard (adversarial review): the "~" stop is scoped to the
        // finalizer (metadata name "Finalize"). A user-defined unary complement
        // operator whose signature is split so the second line begins with "~"
        // (e.g. "public static C operator" / "~(C value)") must still capture the
        // full signature; the "~" line must not be mistaken for a signature start.
        var source = Lines(
            "class C",                              // 1
            "{",                                    // 2
            "    public static C operator",         // 3  <- must be captured
            "        ~(C value)",                   // 4
            "    {",                                // 5  <- StartLine
            "        return value;",                // 6  <- EndLine
            "    }",                                // 7
            "}");                                   // 8

        var body = SourceLinkResolver.ExtractMethodBody(source, startLine: 5, endLine: 6, methodName: "op_OnesComplement");

        Assert.Contains("public static C operator", body, System.StringComparison.Ordinal);
        Assert.Contains("~(C value)", body, System.StringComparison.Ordinal);
    }
}
