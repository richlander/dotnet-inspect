using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LocalFunctionRaisingPassTests
{
    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    [Fact]
    public void StaticLocalFunctionCalledTwice_CollapsesToOneDeclaration()
    {
        string output = PrintRaised(nameof(CfgSampleClass.StaticLocalFunctionCalledTwice));

        Assert.Contains("Twice(x)", output);                              // both calls rendered unqualified
        Assert.Contains("Twice(x + 1)", output);
        Assert.Contains("static int Twice(int v) => v * 2;", output);     // declaration emitted
        // Each call site carries its own (reference-unequal) MethodRef, so a per-Callee
        // grouping would emit one declaration per call. Recovery must collapse to one.
        Assert.Equal(1, CountOccurrences(output, "int Twice(int v)"));
        Assert.DoesNotContain("g__", output);
        Assert.DoesNotContain("CfgSampleClass.Twice", output);
    }

    [Fact]
    public void CapturingLocalFunctionCalledTwice_RecoversSingleDeclarationAcrossBothCalls()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingCalledTwice));

        Assert.Contains("Add(5)", output);                       // both calls drop the ref-env argument
        Assert.Contains("Add(7)", output);
        Assert.Contains("int Add(int v) => v + n;", output);     // captured `n` substituted; env param gone
        Assert.Equal(1, CountOccurrences(output, "int Add(int v)"));
        Assert.DoesNotContain("static int Add", output);         // capturing local function is not static (CS8421)
        Assert.DoesNotContain("DisplayClass", output);           // environment elided
    }

    [Fact]
    public void SharedEnvironmentLocalFunctions_StayLowered()
    {
        // Two local functions share one display-class environment, so neither
        // resolves to a clean single-use environment — recovery must not fire.
        string output = PrintRaised(nameof(CfgSampleClass.SharedEnvironmentLocalFunctions));

        Assert.Contains("DisplayClass", output);                 // environment not elided
        Assert.DoesNotContain("int Add(int v) =>", output);      // no recovered declaration
        Assert.DoesNotContain("int Mul(int v) =>", output);
    }

    [Fact]
    public void RecursiveLocalFunction_StaysLowered()
    {
        // The body calls itself; keeping the import non-recursive bars recovery.
        string output = PrintRaised(nameof(CfgSampleClass.RecursiveLocalFunction));

        Assert.DoesNotContain("int Fact(int v) =>", output);     // no recovered declaration
        Assert.Contains("Fact(n)", output);                      // call left as the lowered invocation
    }

    [Fact]
    public void CapturingAfterMutation_StaysLowered()
    {
        // The captured local is mutated, so the environment field is written from a
        // read of itself (not a clean capture value) — recovery must not fire.
        string output = PrintRaised(nameof(CfgSampleClass.CapturingAfterMutation));

        Assert.Contains("DisplayClass", output);                 // environment not elided
        Assert.DoesNotContain("int Add(int v) =>", output);      // no recovered declaration
    }

    [Fact]
    public void LocalFunctionNameLookalikeWithoutCompilerGeneratedMetadata_StaysCall()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var method = new MethodRef(
            TypeRef.Definition("UserAssembly", "Samples", "Owner"),
            "<M>g__Helper|0_0",
            intType,
            [intType],
            HasThis: false)
        {
            CompilerGenerated = MetadataFactState.No,
        };
        var function = FunctionReturningCall(method, intType);
        var body = LocalFunctionBody(method, intType);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: m => m == method ? body : null);

        new LocalFunctionRaisingPass().Run(function, context);

        Assert.Empty(function.Descendants.OfType<LocalFunctionStatement>());
        Assert.Empty(function.Descendants.OfType<LocalFunctionInvocation>());
        Assert.Single(function.Descendants.OfType<Call>());
        function.CheckInvariant();
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public void StaticLocalFunction_RecoveredAsDeclarationAndUnqualifiedCall()
    {
        string output = PrintRaised(nameof(CfgSampleClass.DoubleViaLocalFunction));

        Assert.Contains("return Twice(x);", output);                  // call rendered unqualified
        Assert.Contains("static int Twice(int v) => v * 2;", output); // declaration emitted
        Assert.DoesNotContain("g__", output);                          // no synthesized name
        Assert.DoesNotContain("CfgSampleClass.Twice", output);         // not the qualified mis-binding
    }

    [Fact]
    public void CapturingLocalFunction_SubstitutesEnvironmentAndDropsRefParameter()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CapturingLocalFunction));

        Assert.Contains("return Add(5);", output);              // call drops the ref-env argument
        Assert.Contains("int Add(int v) => v + n;", output);    // captured `n` substituted; env param gone
        Assert.DoesNotContain("static int Add", output);        // capturing local function is not static (CS8421)
        Assert.DoesNotContain("DisplayClass", output);          // environment elided
    }

    static IrFunction FunctionReturningCall(MethodRef method, TypeRef intType)
    {
        var block = new Block();
        block.Add(new Return(new Call(method, isVirtual: false, [new LoadArgument(0, "x", intType)])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            method.DeclaringType,
            new MethodSignature(method.ReturnType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction LocalFunctionBody(MethodRef method, TypeRef intType)
    {
        var block = new Block();
        block.Add(new Return(new Binary(
            BinaryKind.Multiply,
            isChecked: false,
            isUnsigned: false,
            new LoadArgument(0, "x", intType),
            new Constant(2, intType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            method.Name,
            method.DeclaringType,
            new MethodSignature(method.ReturnType, [new Parameter("x", intType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }
}
