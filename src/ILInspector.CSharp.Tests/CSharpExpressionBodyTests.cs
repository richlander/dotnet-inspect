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
}
