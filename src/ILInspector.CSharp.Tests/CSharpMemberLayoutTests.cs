using System.Text;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpMemberLayoutTests
{
    static string Render(string head, string? body, int indent, bool wrapExpressionBodyArrow = false)
    {
        var sb = new StringBuilder();
        CSharpMemberLayout.Append(sb, head, body, indent, wrapExpressionBodyArrow);
        return sb.ToString().Replace("\r\n", "\n");
    }

    [Fact]
    public void Append_NullBody_TerminatesDeclaration()
        => Assert.Equal(
            "    void M();\n",
            Render("void M()", body: null, indent: 4));

    [Fact]
    public void Append_ThrowStub_RendersExpressionBodied()
        => Assert.Equal(
            "    int Foo() => throw new NotImplementedException();\n",
            Render("int Foo()", "throw new NotImplementedException();", indent: 4));

    [Fact]
    public void Append_SingleReturn_RendersExpressionBodied()
        => Assert.Equal(
            "    int Length() => value.Length;\n",
            Render("int Length()", "return value.Length;", indent: 4));

    [Fact]
    public void Append_SingleReturn_WrapsArrowOnNextLine_WhenRequested()
        => Assert.Equal(
            "    int Length()\n        => value.Length;\n",
            Render("int Length()", "return value.Length;", indent: 4, wrapExpressionBodyArrow: true));

    [Fact]
    public void Append_MultiStatementBody_RendersBlockWithBodyOneLevelDeeper()
        => Assert.Equal(
            "    void M()\n    {\n        Foo();\n        Bar();\n    }\n",
            Render("void M()", "Foo();\nBar();", indent: 4));

    [Fact]
    public void Append_MultiStatementBody_IgnoresWrappedArrowOption()
        => Assert.Equal(
            "    void M()\n    {\n        Foo();\n        Bar();\n    }\n",
            Render("void M()", "Foo();\nBar();", indent: 4, wrapExpressionBodyArrow: true));

    [Fact]
    public void Append_PreservesBlankLinesInBlockBody()
        => Assert.Equal(
            "    void M()\n    {\n        Foo();\n\n        Bar();\n    }\n",
            Render("void M()", "Foo();\n\nBar();", indent: 4));

    [Fact]
    public void Append_NestedAccessorBlock_UsesIndentPlusFour()
        => Assert.Equal(
            "        get\n        {\n            _touch();\n            return _x;\n        }\n",
            Render("get", "_touch();\nreturn _x;", indent: 8));

    [Fact]
    public void Append_NestedAccessorExpression_RendersArrow()
        => Assert.Equal(
            "        set => _x = value;\n",
            Render("set", "_x = value;", indent: 8));

    [Fact]
    public void Append_NestedAccessorExpression_WrapsArrowOnNextLine_WhenRequested()
        => Assert.Equal(
            "        set\n            => _x = value;\n",
            Render("set", "_x = value;", indent: 8, wrapExpressionBodyArrow: true));

    [Fact]
    public void AppendIndentedBody_PreservesBlankLinesAndTrimsTrailing()
    {
        var sb = new StringBuilder();
        CSharpMemberLayout.AppendIndentedBody(sb, "a();  \n\nb();", indent: 8);
        Assert.Equal("        a();\n\n        b();\n", sb.ToString().Replace("\r\n", "\n"));
    }
}
