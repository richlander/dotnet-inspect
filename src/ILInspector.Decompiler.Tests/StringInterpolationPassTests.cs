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
}
