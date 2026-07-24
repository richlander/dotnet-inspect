namespace ILInspector.CSharp.Tests;

public sealed class CSharpExpressionBodyTests
{
    [Theory]
    [InlineData("return value.Length;", "value.Length")]
    [InlineData("return _field;", "_field")]
    [InlineData("_last = value;", "_last = value")]
    [InlineData("_count += 1;", "_count += 1")]
    [InlineData("_map ??= new();", "_map ??= new()")]
    [InlineData("i++;", "i++")]
    [InlineData("--i;", "--i")]
    [InlineData("Console.WriteLine(value);", "Console.WriteLine(value)")]
    [InlineData("new Widget();", "new Widget()")]
    [InlineData("await LoadAsync();", "await LoadAsync()")]
    public void FromSingleStatement_ProducesExpression(string body, string expected)
        => Assert.Equal(expected, CSharpExpressionBody.FromSingleStatement(body));

    [Fact]
    public void FromSingleStatement_KeepsThrowVerbatim()
        => Assert.Equal(
            "throw new NotImplementedException()",
            CSharpExpressionBody.FromSingleStatement("throw new NotImplementedException();"));

    [Theory]
    [InlineData("")]
    [InlineData("return;")]
    [InlineData("no semicolon")]
    [InlineData("// a comment;")]
    [InlineData("/* block */ Foo();")]
    [InlineData("int x = 1;")]
    [InlineData("if (x) Foo();")]
    [InlineData("Foo();\nBar();")]
    public void FromSingleStatement_RejectsNonExpressionForms(string body)
        => Assert.Null(CSharpExpressionBody.FromSingleStatement(body));

    // ── Multi-line single-statement expression extraction (issues #3088, #3084) ─

    [Fact]
    public void MultilineExpressionBodyLines_SplitsSwitchReturn()
    {
        var lines = CSharpExpressionBody.MultilineExpressionBodyLines(
            "return shape switch\n{\n    Dot d => d.Radius,\n    _ => -1,\n};");
        Assert.NotNull(lines);
        Assert.Equal(
            ["shape switch", "{", "    Dot d => d.Radius,", "    _ => -1,", "}"],
            lines);
    }

    [Fact]
    public void MultilineExpressionBodyLines_SplitsFluentChainReturn()
    {
        // Issue #3084: extraction is not switch-specific. A wrapped fluent chain
        // return yields its receiver as the value line and each chained call as a
        // continuation line at its body-relative indent.
        var lines = CSharpExpressionBody.MultilineExpressionBodyLines(
            "return builder\n    .Append(\"a\")\n    .Append(\"b\")\n    .ToString();");
        Assert.NotNull(lines);
        Assert.Equal(
            ["builder", "    .Append(\"a\")", "    .Append(\"b\")", "    .ToString()"],
            lines);
    }

    [Fact]
    public void MultilineExpressionBodyLines_SplitsWrappedTernaryReturn()
    {
        var lines = CSharpExpressionBody.MultilineExpressionBodyLines(
            "return condition\n    ? first\n    : second;");
        Assert.NotNull(lines);
        Assert.Equal(
            ["condition", "    ? first", "    : second"],
            lines);
    }

    [Fact]
    public void MultilineExpressionBodyLines_SplitsVoidFluentExpressionStatement()
    {
        // Issue #3084 (this slice): a void fluent call chain printed as a single
        // expression statement has no `return` keyword, so the whole first line is
        // the arrow value and the chained calls follow as continuations.
        var lines = CSharpExpressionBody.MultilineExpressionBodyLines(
            "builder\n    .Append(\"a\")\n    .Append(\"b\")\n    .Clear();");
        Assert.NotNull(lines);
        Assert.Equal(
            ["builder", "    .Append(\"a\")", "    .Append(\"b\")", "    .Clear()"],
            lines);
    }

    [Fact]
    public void MultilineExpressionBodyLines_IsShapeAgnosticForThrow()
    {
        // The extractor keeps any non-`return` first line whole, so a wrapped
        // `throw <expr>;` would fold to `=> throw <expr>;` should one ever print
        // multi-line. (The printer does not currently produce multi-line throws,
        // so this is latent, not reachable — see BodyIsSingleExpressionBody.)
        var lines = CSharpExpressionBody.MultilineExpressionBodyLines(
            "throw Build(\n    first,\n    second);");
        Assert.NotNull(lines);
        Assert.Equal(
            ["throw Build(", "    first,", "    second)"],
            lines);
    }

    [Theory]
    [InlineData("return value.Length;")]          // single line — FromSingleStatement owns it
    [InlineData("builder.Append(x);")]            // single-line expr statement — FromSingleStatement owns it
    [InlineData("")]                               // empty
    [InlineData("return x switch\n{\n    _ => 1,\n}")] // no terminating ';'
    [InlineData("builder\n    .Append(x)")]        // no terminating ';'
    [InlineData("unsafe\n{\n    NativeMemory.Free(p);\n}")] // unsafe wrapper — ends in '}', not ';'
    public void MultilineExpressionBodyLines_RejectsNonMultilineStatementForms(string body)
        => Assert.Null(CSharpExpressionBody.MultilineExpressionBodyLines(body));
}
