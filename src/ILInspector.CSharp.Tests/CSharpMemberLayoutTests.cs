using System.Text;

namespace ILInspector.CSharp.Tests;

public sealed class CSharpMemberLayoutTests
{
    static string Render(string head, string? body, int indent, bool wrapExpressionBodyArrow = false, bool bodyIsSingleExpressionBody = false, bool disableSignatureWrapping = false)
    {
        var sb = new StringBuilder();
        CSharpMemberLayout.Append(sb, head, body, indent, wrapExpressionBodyArrow, bodyIsSingleExpressionBody, disableSignatureWrapping);
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
            Render("int Area(Shape shape)", SwitchReturnBody, indent: 4, bodyIsSingleExpressionBody: true));

    [Fact]
    public void Append_MultilineSwitchReturn_WrapsArrowOnNextLine_WhenRequested()
        => Assert.Equal(
            "    int Area(Shape shape)\n"
            + "        => shape switch\n"
            + "        {\n"
            + "            Dot d => d.Radius,\n"
            + "            _ => -1,\n"
            + "        };\n",
            Render("int Area(Shape shape)", SwitchReturnBody, indent: 4, wrapExpressionBodyArrow: true, bodyIsSingleExpressionBody: true));

    [Fact]
    public void Append_MultilineSwitchReturn_InNestedAccessor_RendersExpressionBodied()
        => Assert.Equal(
            "        get => shape switch\n"
            + "        {\n"
            + "            Dot d => d.Radius,\n"
            + "            _ => -1,\n"
            + "        };\n",
            Render("get", SwitchReturnBody, indent: 8, bodyIsSingleExpressionBody: true));

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
            Render("int Area(Shape shape)", SwitchReturnBody, indent: 4, bodyIsSingleExpressionBody: false));

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
            Render("string Build(StringBuilder builder)", FluentChainReturnBody, indent: 4, bodyIsSingleExpressionBody: true));

    [Fact]
    public void Append_MultilineFluentReturn_WrapsArrowOnNextLine_WhenRequested()
        => Assert.Equal(
            "    string Build(StringBuilder builder)\n"
            + "        => builder\n"
            + "            .Append(\"a\")\n"
            + "            .Append(\"b\")\n"
            + "            .ToString();\n",
            Render("string Build(StringBuilder builder)", FluentChainReturnBody, indent: 4, wrapExpressionBodyArrow: true, bodyIsSingleExpressionBody: true));

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
            Render("string Build(StringBuilder builder)", FluentChainReturnBody, indent: 4, bodyIsSingleExpressionBody: false));

    // Issue #3084 (this slice): the fold is not return-specific either — a void
    // member whose only statement is a multi-line expression statement (a wrapped
    // fluent chain, no `return`) renders expression-bodied too, the whole first
    // line trailing the arrow and each chained call re-indented one level under
    // the member.
    const string FluentChainStatementBody =
        "builder\n    .Append(\"a\")\n    .Append(\"b\")\n    .Clear();";

    [Fact]
    public void Append_MultilineFluentStatement_RendersExpressionBodied_SameLineArrow()
        => Assert.Equal(
            "    void Build(StringBuilder builder) => builder\n"
            + "        .Append(\"a\")\n"
            + "        .Append(\"b\")\n"
            + "        .Clear();\n",
            Render("void Build(StringBuilder builder)", FluentChainStatementBody, indent: 4, bodyIsSingleExpressionBody: true));

    [Fact]
    public void Append_MultilineFluentStatement_StaysBlock_WhenSignalNotSet()
        => Assert.Equal(
            "    void Build(StringBuilder builder)\n"
            + "    {\n"
            + "        builder\n"
            + "            .Append(\"a\")\n"
            + "            .Append(\"b\")\n"
            + "            .Clear();\n"
            + "    }\n",
            Render("void Build(StringBuilder builder)", FluentChainStatementBody, indent: 4, bodyIsSingleExpressionBody: false));

    [Fact]
    public void AppendIndentedBody_PreservesBlankLinesAndTrimsTrailing()
    {
        var sb = new StringBuilder();
        CSharpMemberLayout.AppendIndentedBody(sb, "a();  \n\nb();", indent: 8);
        Assert.Equal("        a();\n\n        b();\n", sb.ToString().Replace("\r\n", "\n"));
    }

    // Issue #3185: a member whose single physical line would exceed the 120-column
    // runtime budget wraps its parameter list one parameter per line, keeping the
    // `=>`/`{`/`;` on the closing `)` line. Token-identical, IL-unchanged, and the
    // revealed corpus practice. Mirrors the always-on fluent-chain wrapper.
    const string LongParseHead =
        "public static JsonElement Parse([StringSyntax(\"Json\")] ReadOnlySpan<byte> utf8Json, JsonDocumentOptions options = default)";

    [Fact]
    public void Append_LongExpressionBodiedSignature_WrapsParametersKeepingArrowOnCloseParen()
        => Assert.Equal(
            "    public static JsonElement Parse(\n"
            + "        [StringSyntax(\"Json\")] ReadOnlySpan<byte> utf8Json,\n"
            + "        JsonDocumentOptions options = default) => JsonDocument.ParseValue(utf8Json, options).RootElement;\n",
            Render(LongParseHead, "return JsonDocument.ParseValue(utf8Json, options).RootElement;", indent: 4));

    [Fact]
    public void Append_LongSignature_DisableOptOut_KeepsSingleLine()
        => Assert.Equal(
            "    " + LongParseHead + " => JsonDocument.ParseValue(utf8Json, options).RootElement;\n",
            Render(LongParseHead, "return JsonDocument.ParseValue(utf8Json, options).RootElement;", indent: 4, disableSignatureWrapping: true));

    [Fact]
    public void Append_ShortSignature_StaysInline()
        => Assert.Equal(
            "    public int Add(int a, int b) => a + b;\n",
            Render("public int Add(int a, int b)", "return a + b;", indent: 4));

    [Fact]
    public void Append_LongSignature_NullBody_WrapsAndKeepsSemicolonOnCloseParen()
        => Assert.Equal(
            "    public static void RegisterLongDescriptiveFactoryMethod<TService, TImpl>(\n"
            + "        IServiceCollection services,\n"
            + "        string longParameterName) where TImpl : TService, new();\n",
            Render(
                "public static void RegisterLongDescriptiveFactoryMethod<TService, TImpl>(IServiceCollection services, string longParameterName) where TImpl : TService, new()",
                body: null,
                indent: 4));

    [Fact]
    public void Append_LongGenericSignature_KeepsGenericArgCommasIntact()
        => Assert.Equal(
            "    public static TResult LongGenericHelperMethodName<TSource, TResult>(\n"
            + "        TSource source,\n"
            + "        Func<TSource, TResult> selector,\n"
            + "        IComparer<TResult> comparer) => selector(source);\n",
            Render(
                "public static TResult LongGenericHelperMethodName<TSource, TResult>(TSource source, Func<TSource, TResult> selector, IComparer<TResult> comparer)",
                "return selector(source);",
                indent: 4));

    [Fact]
    public void Append_LongSignature_TupleReturnType_WrapsParamsNotReturnTuple()
        => Assert.Equal(
            "    public static (int Quotient, int Remainder) DivRemWithAVeryLongMethodNameThatExceeds(\n"
            + "        int dividend,\n"
            + "        int divisorValue) => (dividend / divisorValue, dividend % divisorValue);\n",
            Render(
                "public static (int Quotient, int Remainder) DivRemWithAVeryLongMethodNameThatExceeds(int dividend, int divisorValue)",
                "return (dividend / divisorValue, dividend % divisorValue);",
                indent: 4));

    [Fact]
    public void Append_LongOperatorSignature_FallsBackToInline()
    {
        // The '(' after 'operator +' is preceded by '+', which the conservative
        // locator does not recognize as a member name, so the signature degrades
        // to today's single line rather than risk a mangled operator declaration.
        const string head =
            "public static VeryLongCustomNumericTypeNameHere operator +(VeryLongCustomNumericTypeNameHere left, VeryLongCustomNumericTypeNameHere right)";
        Assert.Equal("    " + head + " => left;\n", Render(head, "return left;", indent: 4));
    }

    [Fact]
    public void Append_LongSignature_BlockBody_WrapsParamsAndKeepsBraceOnOwnLine()
        => Assert.Equal(
            "    public static void ConfigureLongServiceRegistrationPipeline(\n"
            + "        IServiceCollection services,\n"
            + "        IConfiguration configuration,\n"
            + "        ILoggerFactory loggerFactory)\n"
            + "    {\n"
            + "        DoWork();\n"
            + "        DoMore();\n"
            + "    }\n",
            Render(
                "public static void ConfigureLongServiceRegistrationPipeline(IServiceCollection services, IConfiguration configuration, ILoggerFactory loggerFactory)",
                "DoWork();\nDoMore();",
                indent: 4));

    // Issue #3185: SplitTopLevelCommas only models conventional `\`-escaped string
    // and char literals. A signature carrying a verbatim/interpolated/raw string
    // (whose quotes and commas it cannot parse) must decline to wrap and stay on
    // one line rather than risk splitting inside the literal.
    const string OverBudgetPrefix =
        "public static void MethodWithAnExtremelyLongNameToForceWrappingOfTheParametersDefinitelyOverBudget";

    [Fact]
    public void Append_LongSignature_InterpolatedStringDefault_StaysInline()
    {
        string head = OverBudgetPrefix + "(string s = $\"{\",\"}\", int x = 0)";
        Assert.Equal("    " + head + ";\n", Render(head, body: null, indent: 4));
    }

    [Fact]
    public void Append_LongSignature_VerbatimStringDefault_StaysInline()
    {
        string head = OverBudgetPrefix + "(string s = @\"a\"\"b, c\", int x = 0)";
        Assert.Equal("    " + head + ";\n", Render(head, body: null, indent: 4));
    }

    [Fact]
    public void Append_LongSignature_RawStringDefault_StaysInline()
    {
        string head = OverBudgetPrefix + "(string s = \"\"\"a,b\"\"\", int x = 0)";
        Assert.Equal("    " + head + ";\n", Render(head, body: null, indent: 4));
    }

    [Fact]
    public void Append_LongSignature_ConventionalStringWithComma_WrapsWithoutSplittingLiteral()
        => Assert.Equal(
            "    " + OverBudgetPrefix + "(\n"
            + "        string s = \"a,b,c\",\n"
            + "        int x = 0);\n",
            Render(OverBudgetPrefix + "(string s = \"a,b,c\", int x = 0)", body: null, indent: 4));
}
