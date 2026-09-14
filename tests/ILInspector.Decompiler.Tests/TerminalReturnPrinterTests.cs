using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Printer")]
public class TerminalReturnPrinterTests
{
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");

    [Fact]
    public void TerminalReturnBeforeLocalFunction_IsTrimmed()
    {
        string output = Raise(nameof(TerminalReturnSamples.BeforeLocalFunction));

        Assert.Equal(
            """
            F();
            static void F()
            {
            }
            """.ReplaceLineEndings("\n"),
            output);
    }

    [Fact]
    public void EarlyReturnBeforeLocalFunction_IsPreserved()
    {
        string output = Raise(nameof(TerminalReturnSamples.EarlyReturnBeforeLocalFunction));

        Assert.Equal(1, CountOccurrences(output, "return;"));
        Assert.Contains("static void F()", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void TerminalReturnBeforeLocalFunction_RecompilesOpcodeExact()
    {
        string assembly = typeof(TerminalReturnSamples).Assembly.Location;
        var result = Assert.Single(FidelityCheck.EvaluateTargets(
            [assembly],
            [new FidelityCheck.CompileBackTarget(
                assembly,
                typeof(TerminalReturnSamples).FullName!,
                nameof(TerminalReturnSamples.BeforeLocalFunction),
                Overload: 0,
                Signature: "() -> corelib:System.Void")]));

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LabeledTerminalReturnBeforeLocalFunction_IsPreserved(bool targetStatementOffset)
    {
        var localBody = new BlockContainer();
        localBody.Add(new Block());
        var localFunction = new LocalFunctionStatement(
            "F",
            s_void,
            [],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localBody);

        var entry = new Block();
        entry.Add(new Branch(0x10));
        var target = new Block(targetStatementOffset ? 0x08 : 0x10);
        var terminalReturn = new Return(null);
        if (targetStatementOffset)
            terminalReturn.SetSourceOffset(0x10);
        target.Add(terminalReturn);
        target.Add(localFunction);
        var body = new BlockContainer();
        body.Add(entry);
        body.Add(target);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(s_void, [], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        function.CheckInvariant();
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").Trim();

        Assert.Contains("IL_0010:", output);
        Assert.Contains("return;", output);
        Assert.Contains("static void F()", output);
    }

    static string Raise(string methodName)
    {
        var type = typeof(TerminalReturnSamples);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    static int CountOccurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;
}
