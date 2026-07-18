using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A metadata identifier whose name is a C# reserved keyword must be @-escaped
// in the rendered body — a bare `delegate` is CS1001 "Identifier expected".
public class KeywordIdentifierTests
{
    static readonly TypeRef AwaitType = TypeRef.Definition("Synthetic", "", "Await");

    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void KeywordParameter_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.KeywordParam));

        Assert.Contains("@delegate + 1", output);
        Assert.DoesNotContain(" delegate", output);
    }

    [Fact]
    public void ContextualKeywordParameter_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.ContextualKeywordParam));

        Assert.Contains("@await + 1", output);
        Assert.DoesNotContain(" await", output);
    }

    [Fact]
    public void KeywordFieldRead_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.ReadKeywordField));

        Assert.Contains("value.@else", output);
        Assert.DoesNotContain("value.else", output);
    }

    [Fact]
    public void KeywordFieldWrite_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.WriteKeywordField));

        Assert.Contains("value.@else = input", output);
        Assert.DoesNotContain("value.else", output);
    }

    [Fact]
    public void LoweredNullConditionalKeywordFieldRead_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.ReadKeywordFieldNullConditional));

        Assert.Contains("value.@else", output);
        Assert.DoesNotContain("value.else", output);
    }

    [Fact]
    public void RaisedNullConditionalKeywordFieldRead_IsEscaped()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var value = new LoadArgument(0, "value", holder);
        var field = new FieldRef(holder, "else", stringType);
        var body = new BlockContainer();
        var block = new Block();
        body.Add(block);
        block.Add(new Return(new NullConditional(new LoadField(field, value))));
        var function = new IrFunction(
            "M",
            holder,
            new MethodSignature(
                stringType,
                [new Parameter("value", holder)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("value?.@else", output);
        Assert.DoesNotContain("value?.else", output);
    }

    [Fact]
    public void KeywordObjectInitializerMember_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.InitializeKeywordField));

        Assert.Contains("@else = value", output);
        Assert.DoesNotContain("{ else = value", output);
    }

    [Fact]
    public void RaisedWithExpressionKeywordMember_IsEscaped()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var expression = new WithExpression(
            new LoadArgument(0, "value", holder),
            [new InitializerEntry("else", [new Constant(null, stringType)])]);

        var (function, output) = RenderExpression(holder, expression);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("value with { @else = null }", output);
    }

    [Fact]
    public void RaisedAnonymousObjectKeywordProperty_IsEscaped()
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var expression = new AnonymousObject(
            holder,
            ["else"],
            [new Constant(1, TypeRef.CoreLib("System", "Int32"))]);

        var (function, output) = RenderExpression(holder, expression);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Contains("new { @else = 1 }", output);
    }

    [Fact]
    public void RaisedNullConditionalUnspellableBackingProperty_PreservesIdentity()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var value = new LoadArgument(0, "value", holder);
        var field = new FieldRef(holder, "<bad-name>k__BackingField", stringType)
        {
            BackingPropertyName = "bad-name",
        };

        var (function, output) = RenderExpression(
            stringType,
            new NullConditional(new LoadField(field, value)));

        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
        Assert.Contains("value?.bad-name", output);
        Assert.DoesNotContain("value?._bad_name", output);
    }

    [Fact]
    public void RaisedNullConditionalKeywordPrimaryConstructorCapture_IsEscaped()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var value = new LoadArgument(0, "value", holder);
        var field = new FieldRef(holder, "<else>P", stringType);

        var (_, output) = RenderExpression(
            stringType,
            new NullConditional(new LoadField(field, value)));

        Assert.Contains("value?.@else", output);
        Assert.DoesNotContain("value?.else", output);
    }

    [Fact]
    public void KeywordStaticMethodName_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.CallsKeywordStaticMethod));

        Assert.Contains("@return(value)", output);
        Assert.DoesNotContain("CfgSampleClass.return(value)", output);
    }

    [Fact]
    public void KeywordInstanceMethodName_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.CallsKeywordInstanceMethod));

        Assert.Contains("@event(value)", output);
        Assert.DoesNotContain(" event(value)", output);
    }

    [Fact]
    public void KeywordMethodGroupName_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.KeywordInstanceMethodGroup));

        Assert.Contains("new Func<int, int>(@event)", output);
        Assert.DoesNotContain("new Func<int, int>(event)", output);
    }

    [Fact]
    public void KeywordTypeName_IsEscaped()
    {
        var output = Render(nameof(CfgSampleClass.CreateKeywordType));

        Assert.Contains("new @class()", output);
        Assert.DoesNotContain("new class()", output);
    }

    [Fact]
    public void ReadableLocalName_DoesNotEmitBareAwait()
    {
        var body = new BlockContainer();
        var block = new Block();
        block.Add(new StoreLocal(0, AwaitType, new Constant(null, AwaitType)));
        block.Add(new ExpressionStatement(new LoadLocal(0, AwaitType)));
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0),
            [AwaitType],
            body);

        var output = CSharpPrinter.Print(function, new PrinterOptions { ReadableLocalNames = true }).Output!;

        Assert.Contains("V_0", output);
        Assert.DoesNotContain(" await", output);
    }

    static (IrFunction Function, string Output) RenderExpression(
        TypeRef returnType,
        IrExpression expression)
    {
        var holder = TypeRef.Definition("Synthetic", "Samples", "Holder");
        var body = new BlockContainer();
        var block = new Block();
        body.Add(block);
        block.Add(new Return(expression));
        var function = new IrFunction(
            "M",
            holder,
            new MethodSignature(
                returnType,
                [new Parameter("value", holder)],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body);
        return (function, CSharpPrinter.Print(function).Output!);
    }
}
