using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StringInterpolationPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_string = TypeRef.CoreLib("System", "String");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void HandlerAppendSequence_RaisesToInterpolatedString()
    {
        var function = Raised(nameof(CfgSampleClass.StringInterpolation));

        var interpolation = Assert.Single(function.Descendants.OfType<InterpolatedStringExpression>());
        Assert.Equal(5, interpolation.Parts.Length);
        Assert.Equal(2, interpolation.FormattedValues.Count);
        Assert.DoesNotContain(function.Descendants.OfType<NewObject>(),
            n => n.Constructor.DeclaringType.Name == "DefaultInterpolatedStringHandler");
    }

    [Fact]
    public void PrintRaised_RendersInterpolatedString()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.StringInterpolation))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"Hello, {name}! You are {age} years old.\";", output);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", output);
    }

    [Fact]
    public void RepeatedFormattedValues_RendersEachHole()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolatedStruct))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"value={value} again={value}\";", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }

    [Fact]
    public void InterpolationAssignedToLocal_RaisesToInterpolatedString()
    {
        var function = Raised(nameof(CfgSampleClass.InterpolationToLocal));

        Assert.Single(function.Descendants.OfType<InterpolatedStringExpression>());
        Assert.DoesNotContain(function.Descendants.OfType<NewObject>(),
            n => n.Constructor.DeclaringType.Name == "DefaultInterpolatedStringHandler");

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("$\"Hello {name}, you are {age}\"", output);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", output);
    }

    [Fact]
    public void InterpolationPassedAsArgument_RaisesToInterpolatedString()
    {
        var function = Raised(nameof(CfgSampleClass.InterpolationAsArgument));

        Assert.Single(function.Descendants.OfType<InterpolatedStringExpression>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("ConsumeInterpolation($\"Hello {name}, you are {age}\")", output);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }

    [Fact]
    public void ManualHandlerSourceLocal_IsNotRaised()
    {
        // This is a source-level handler local, not the compiler's hidden temp
        // for `$"..."`. Raising it would erase the user's chosen lower-level
        // spelling and, for richer overloads, can drop semantics.
        var function = Raised(nameof(CfgSampleClass.ManualInterpolatedStringHandler));

        Assert.DoesNotContain(function.Descendants.OfType<InterpolatedStringExpression>(), _ => true);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("DefaultInterpolatedStringHandler handler", output);
        Assert.Contains("handler.AppendLiteral", output);
        Assert.DoesNotContain("$\"Hello", output);
    }

    [Fact]
    public void ManualHandlerProviderCtor_IsNotRaised()
    {
        // The provider overload carries formatting semantics not represented by
        // a plain interpolated string in this IR slice.
        var function = Raised(nameof(CfgSampleClass.ManualInterpolatedStringHandlerWithProvider));

        Assert.DoesNotContain(function.Descendants.OfType<InterpolatedStringExpression>(), _ => true);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("CultureInfo.InvariantCulture", output);
        Assert.DoesNotContain("$\"value=", output);
    }

    [Fact]
    public void HandlerSequence_FromUserHandlerLookalike_IsNotRaised()
    {
        var function = BuildUserHandlerLookalike();

        new StringInterpolationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<InterpolatedStringExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "AppendLiteral");
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "ToStringAndClear");
        function.CheckInvariant();
    }

    [Fact]
    public void FormatSpecifier_RecoversColonFormat()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolationWithFormat))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"Total: {amount:N2}\";", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }

    [Fact]
    public void AlignmentSpecifier_RecoversCommaAlignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolationWithAlignment))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"[{code,5}]\";", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }

    [Fact]
    public void NegativeAlignmentSpecifier_RecoversSignedAlignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolationWithNegativeAlignment))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"[{label,-10}]\";", output);
    }

    [Fact]
    public void AlignmentAndFormat_RecoversBothClauses()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolationWithAlignmentAndFormat))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"{ratio,8:P1}\";", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }

    [Fact]
    public void MixedHoles_RecoversPlainAndFormattedHoles()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.InterpolationMixedHoles))).Output;

        Assert.NotNull(output);
        Assert.Contains("return $\"{name} scored {score:D4}!\";", output);
        Assert.DoesNotContain("AppendFormatted", output);
    }

    [Fact]
    public void HiddenTempHandler_BraceInFormat_StaysLowered()
    {
        // A format containing a brace cannot round-trip through `{value:format}`
        // syntax, so the call must stay lowered even when every other condition of
        // the hidden-temp handler pattern is met.
        var function = BuildAppendFormattedOverload(
            [s_int, s_string], [new LoadArgument(0, "value", s_int), new Constant("a}b", s_string)]);

        new StringInterpolationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<InterpolatedStringExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "AppendFormatted");
        function.CheckInvariant();
    }

    [Fact]
    public void HiddenTempHandler_PlainFormat_Raises()
    {
        // The same scaffold with a brace-free format raises, proving the negative
        // above is the brace guard and not an unrelated mismatch.
        var function = BuildAppendFormattedOverload(
            [s_int, s_string], [new LoadArgument(0, "value", s_int), new Constant("N2", s_string)]);

        new StringInterpolationPass().Run(function, PassContext.None);

        var interpolation = Assert.Single(function.Descendants.OfType<InterpolatedStringExpression>());
        Assert.Equal(":N2", FormatClause(interpolation));
        function.CheckInvariant();
    }

    [Fact]
    public void BackslashFormat_RendersEscaped_SoItRoundTrips()
    {
        // The common TimeSpan/DateTime custom format `h\:mm\:ss` reaches the IR as
        // a single backslash, but the hole sits inside a non-verbatim `$"…"`, which
        // csc escape-processes. The printer must escape it (`h\\:mm\\:ss`) or the
        // bare `\:` is CS1009 — the #811 format clause rendered the raw string and
        // produced malformed C# for every backslash-escaped format.
        var function = BuildAppendFormattedOverload(
            [s_int, s_string], [new LoadArgument(0, "value", s_int), new Constant(@"h\:mm\:ss", s_string)]);

        new StringInterpolationPass().Run(function, PassContext.None);
        var output = CSharpPrinter.Print(function).Output;

        Assert.NotNull(output);
        Assert.Contains(@"$""{value:h\\:mm\\:ss}""", output);
        Assert.DoesNotContain("AppendFormatted", output);
        function.CheckInvariant();
    }

    [Fact]
    public void QuoteAndNewlineFormat_RenderEscaped_StayValid()
    {
        // A double quote or newline in a format equally cannot appear raw in a
        // non-verbatim `$"…"` (the quote closes the literal, the newline is
        // CS1010). The shared string escaping handles both: `\"` and `\n`. These
        // come from hand-written handlers, but the printer must never emit invalid
        // C# for a matched shape.
        var quote = BuildAppendFormattedOverload(
            [s_int, s_string], [new LoadArgument(0, "value", s_int), new Constant("a\"b", s_string)]);
        new StringInterpolationPass().Run(quote, PassContext.None);
        Assert.Contains(@"{value:a\""b}", CSharpPrinter.Print(quote).Output);

        var newline = BuildAppendFormattedOverload(
            [s_int, s_string], [new LoadArgument(0, "value", s_int), new Constant("a\nb", s_string)]);
        new StringInterpolationPass().Run(newline, PassContext.None);
        Assert.Contains(@"{value:a\nb}", CSharpPrinter.Print(newline).Output);
    }

    static string FormatClause(InterpolatedStringExpression node)
    {
        var part = node.Parts.Single(p => !p.IsLiteral);
        var format = part.Format!;
        var alignment = format.HasAlignment ? "," + format.Alignment : "";
        var formatString = format.FormatString is { } f ? ":" + f : "";
        return alignment + formatString;
    }

    static IrFunction BuildUserHandlerLookalike()
    {
        var handler = TypeRef.Definition(
            "UserAssembly",
            "System.Runtime.CompilerServices",
            "DefaultInterpolatedStringHandler");
        var ctor = new MethodRef(handler, ".ctor", s_void, [s_int, s_int], HasThis: true);
        var appendLiteral = new MethodRef(handler, "AppendLiteral", s_void, [s_string], HasThis: true);
        var toStringAndClear = new MethodRef(handler, "ToStringAndClear", s_string, [], HasThis: true);

        var block = new Block();
        block.Add(new StoreLocal(0, handler, new NewObject(ctor, [new Constant(5, s_int), new Constant(0, s_int)])));
        block.Add(new ExpressionStatement(new Call(
            appendLiteral,
            isVirtual: false,
            [new LoadLocalAddress(0, handler), new Constant("Hello", s_string)])));
        block.Add(new Return(new Call(toStringAndClear, isVirtual: false, [new LoadLocalAddress(0, handler)])));
        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_string, [], HasThis: false, GenericParameterCount: 0),
            [handler],
            body);
    }

    static IrFunction BuildAppendFormattedOverload(TypeRef[] parameterTypes, IrExpression[] arguments)
    {
        var handler = TypeRef.CoreLib("System.Runtime.CompilerServices", "DefaultInterpolatedStringHandler");
        var ctor = new MethodRef(handler, ".ctor", s_void, [s_int, s_int], HasThis: true);
        var appendFormatted = new MethodRef(handler, "AppendFormatted", s_void, [.. parameterTypes], HasThis: true)
        {
            TypeArguments = [s_int],
        };
        var toStringAndClear = new MethodRef(handler, "ToStringAndClear", s_string, [], HasThis: true);

        var block = new Block();
        block.Add(new StoreLocal(0, handler, new NewObject(ctor, [new Constant(0, s_int), new Constant(1, s_int)])));
        block.Add(new ExpressionStatement(new Call(
            appendFormatted,
            isVirtual: false,
            [new LoadLocalAddress(0, handler), .. arguments])));
        block.Add(new Return(new Call(toStringAndClear, isVirtual: false, [new LoadLocalAddress(0, handler)])));
        var body = new BlockContainer();
        body.Add(block);

        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(s_string, [new Parameter("value", s_int)], HasThis: false, GenericParameterCount: 0),
            [handler],
            body);
    }
}
