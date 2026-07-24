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

    // ── Multi-line single-return extraction (issue #3088) ──────────────────

    [Fact]
    public void MultilineReturnExpressionLines_SplitsSwitchReturn()
    {
        var lines = CSharpExpressionBody.MultilineReturnExpressionLines(
            "return shape switch\n{\n    Dot d => d.Radius,\n    _ => -1,\n};");
        Assert.NotNull(lines);
        Assert.Equal(
            ["shape switch", "{", "    Dot d => d.Radius,", "    _ => -1,", "}"],
            lines);
    }

    [Theory]
    [InlineData("return value.Length;")]          // single line — FromSingleStatement owns it
    [InlineData("result = 0;\nreturn x switch\n{\n    _ => 1,\n};")]  // has a leading statement, but the string still starts non-`return`
    [InlineData("x switch\n{\n    _ => 1,\n};")]   // no `return`
    [InlineData("return\nx;")]                      // bare `return` (no space)
    [InlineData("return x switch\n{\n    _ => 1,\n}")] // no terminating ';'
    public void MultilineReturnExpressionLines_RejectsNonMultilineReturnForms(string body)
        => Assert.Null(CSharpExpressionBody.MultilineReturnExpressionLines(body));
}
