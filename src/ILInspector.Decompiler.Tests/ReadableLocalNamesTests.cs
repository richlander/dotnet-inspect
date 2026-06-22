using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// The opt-in readable-names mode (PrinterOptions.ReadableLocalNames). The default
// must stay byte-identical V_index output; the mode synthesizes readable names
// only for locals with no usable source name, and still falls back to V_index
// when no IR evidence supports a name.
public class ReadableLocalNamesTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void DefaultMode_RendersVIndex()
    {
        var output = Print(StringLocalFunction(), options: null);
        Assert.Contains("string V_0 = \"hi\";", output);
        Assert.Contains("return V_0;", output);
        Assert.DoesNotContain("text", output);
    }

    [Fact]
    public void ReadableMode_NamesLocalByType()
    {
        var output = Print(StringLocalFunction(), new PrinterOptions { ReadableLocalNames = true });
        Assert.Contains("string text = \"hi\";", output);
        Assert.Contains("return text;", output);
        Assert.DoesNotContain("V_0", output);
    }

    [Fact]
    public void ReadableMode_FallsBackToVIndexWithoutEvidence()
    {
        // An object local camel-cases to the keyword `object` (unusable), so even
        // with the mode on it must keep V_index — honest degradation, not a guess.
        var output = Print(ObjectLocalFunction(), new PrinterOptions { ReadableLocalNames = true });
        Assert.Contains("V_0", output);
        Assert.DoesNotContain("object obj", output);
    }

    [Fact]
    public void ReadableMode_NamesForLoopCounterIjk()
    {
        // A local written by a for-loop increment is the induction variable, so
        // the readable mode spells it i (LoopCounterLocals -> isLoopCounter).
        var output = Print(CounterFunction(), new PrinterOptions { ReadableLocalNames = true });
        Assert.Contains("int i = 0;", output);
        Assert.DoesNotContain("V_0", output);
    }

    // for (int V_0 = 0; true; V_0 = V_0 + 1) { }
    static IrFunction CounterFunction()
    {
        var loop = new ForLoop(
            new StoreLocal(0, Int32, new Constant(0, Int32)),
            new Constant(true, Boolean),
            new StoreLocal(0, Int32, new Constant(1, Int32)),
            new Block(0));
        var block = new Block(0);
        block.Add(loop);
        block.Add(new Return(null));
        return Function(Void, [Int32], block);
    }

    // string V_0 = "hi"; return V_0;
    static IrFunction StringLocalFunction()
    {
        var block = new Block(0);
        block.Add(new StoreLocal(0, String, new Constant("hi", String)));
        block.Add(new Return(new LoadLocal(0, String)));
        return Function(String, [String], block);
    }

    // object V_0 = null; return V_0;
    static IrFunction ObjectLocalFunction()
    {
        var block = new Block(0);
        block.Add(new StoreLocal(0, Object, new Constant(null, Object)));
        block.Add(new Return(new LoadLocal(0, Object)));
        return Function(Object, [Object], block);
    }

    static string Print(IrFunction function, PrinterOptions? options)
        => CSharpPrinter.Print(function, options).Output!;

    static IrFunction Function(TypeRef returnType, TypeRef[] locals, Block block)
    {
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction("M", Holder, new MethodSignature(returnType, [], HasThis: false, GenericParameterCount: 0), [.. locals], body);
    }
}
