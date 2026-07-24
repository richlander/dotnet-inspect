using System.Text;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpMemberLayoutTests
{
    static string Render(string head, string? body, int indent, bool wrapExpressionBodyArrow = false, bool bodyIsSingleReturnExpression = false)
    {
        var sb = new StringBuilder();
        CSharpMemberLayout.Append(sb, head, body, indent, wrapExpressionBodyArrow, bodyIsSingleReturnExpression);
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

    // Issue #3088: a body that is exactly one multi-line `return <switch>;`
    // renders expression-bodied, with the switch block re-indented under the
    // member. The caller proves the single-return shape (the typed signal);
    // the layout owns the presentation.
    const string SwitchReturnBody =
        "return shape switch\n{\n    Dot d => d.Radius,\n    _ => -1,\n};";

    [Fact]
    public void Append_MultilineSwitchReturn_RendersExpressionBodied_SameLineArrow()
        => Assert.Equal(
            "    int Area(Shape shape) => shape switch\n"
            + "    {\n"
            + "        Dot d => d.Radius,\n"
            + "        _ => -1,\n"
            + "    };\n",
            Render("int Area(Shape shape)", SwitchReturnBody, indent: 4, bodyIsSingleReturnExpression: true));

    [Fact]
    public void Append_MultilineSwitchReturn_WrapsArrowOnNextLine_WhenRequested()
        => Assert.Equal(
            "    int Area(Shape shape)\n"
            + "        => shape switch\n"
            + "        {\n"
            + "            Dot d => d.Radius,\n"
            + "            _ => -1,\n"
            + "        };\n",
            Render("int Area(Shape shape)", SwitchReturnBody, indent: 4, wrapExpressionBodyArrow: true, bodyIsSingleReturnExpression: true));

    [Fact]
    public void Append_MultilineSwitchReturn_InNestedAccessor_RendersExpressionBodied()
        => Assert.Equal(
            "        get => shape switch\n"
            + "        {\n"
            + "            Dot d => d.Radius,\n"
            + "            _ => -1,\n"
            + "        };\n",
            Render("get", SwitchReturnBody, indent: 8, bodyIsSingleReturnExpression: true));

    [Fact]
    public void Append_MultilineSwitchReturn_StaysBlock_WhenSignalNotSet()
        => Assert.Equal(
            "    int Area(Shape shape)\n"
            + "    {\n"
            + "        return shape switch\n"
            + "        {\n"
            + "            Dot d => d.Radius,\n"
            + "            _ => -1,\n"
            + "        };\n"
            + "    }\n",
            Render("int Area(Shape shape)", SwitchReturnBody, indent: 4, bodyIsSingleReturnExpression: false));

    // Issue #3084: the same single-return expression-body fold is not
    // switch-specific — any multi-line single `return <expr>;` (here a wrapped
    // fluent chain) renders expression-bodied, the chain receiver trailing the
    // arrow and each chained call re-indented one level under the member.
    const string FluentChainReturnBody =
        "return builder\n    .Append(\"a\")\n    .Append(\"b\")\n    .ToString();";

    [Fact]
    public void Append_MultilineFluentReturn_RendersExpressionBodied_SameLineArrow()
        => Assert.Equal(
            "    string Build(StringBuilder builder) => builder\n"
            + "        .Append(\"a\")\n"
            + "        .Append(\"b\")\n"
            + "        .ToString();\n",
            Render("string Build(StringBuilder builder)", FluentChainReturnBody, indent: 4, bodyIsSingleReturnExpression: true));

    [Fact]
    public void Append_MultilineFluentReturn_WrapsArrowOnNextLine_WhenRequested()
        => Assert.Equal(
            "    string Build(StringBuilder builder)\n"
            + "        => builder\n"
            + "            .Append(\"a\")\n"
            + "            .Append(\"b\")\n"
            + "            .ToString();\n",
            Render("string Build(StringBuilder builder)", FluentChainReturnBody, indent: 4, wrapExpressionBodyArrow: true, bodyIsSingleReturnExpression: true));

    [Fact]
    public void Append_MultilineFluentReturn_StaysBlock_WhenSignalNotSet()
        => Assert.Equal(
            "    string Build(StringBuilder builder)\n"
            + "    {\n"
            + "        return builder\n"
            + "            .Append(\"a\")\n"
            + "            .Append(\"b\")\n"
            + "            .ToString();\n"
            + "    }\n",
            Render("string Build(StringBuilder builder)", FluentChainReturnBody, indent: 4, bodyIsSingleReturnExpression: false));

    [Fact]
    public void AppendIndentedBody_PreservesBlankLinesAndTrimsTrailing()
    {
        var sb = new StringBuilder();
        CSharpMemberLayout.AppendIndentedBody(sb, "a();  \n\nb();", indent: 8);
        Assert.Equal("        a();\n\n        b();\n", sb.ToString().Replace("\r\n", "\n"));
    }
}
