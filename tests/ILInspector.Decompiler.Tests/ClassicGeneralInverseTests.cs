using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The general classic async inverse over its compiler-produced fixture family
/// (<c>docs/design/classic-async-reconstruction.md</c>, "General inverse"):
/// each positive pins one protocol role through the product path, each
/// negative pins a store the inverse must carry or a shape it must decline.
/// </summary>
[Trait("Area", "Pass")]
public sealed class ClassicGeneralInverseTests
{
    const string FixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.GeneralInverseFixtures";
    const string LoopFixtureType =
        "ILInspector.Decompiler.Fixtures.ClassicAsync.GeneralInverseLoopFixtures";
    const string ReaderType = FixtureType + ".Reader";
    const string HolderType = FixtureType + ".Holder`1";

    [Theory]
    [InlineData(FixtureType, "ThreeSequentialAwaits", 3, "await a;", "await c;")]
    [InlineData(FixtureType, "EarlyReturns", 2, "return -1;", "return 0;")]
    [InlineData(FixtureType, "ValueTaskBuilder", 1, "await value", "+ add")]
    [InlineData(FixtureType, "ParameterWrite", 1, "await a;", "n *= 2;")]
    [InlineData(FixtureType, "UserCatchFilter", 1, "catch (Exception e) when (accept", "return -1;")]
    [InlineData(FixtureType, "AccumulateUntil", 1, "while (true)", "return limit;")]
    [InlineData(FixtureType, "ScopedHoistedLocal", 2, "object scoped = new object();", "scoped = null;")]
    [InlineData(ReaderType, "SkipAsync", 2, "if (IsStart)", "depth = Depth;")]
    [InlineData(HolderType, "GetAsync", 2, "await Task.Yield();", "return Value;")]
    [InlineData(LoopFixtureType, "UsingVar", 1, "using (StringWriter writer = new StringWriter())", "writer.Write(value);")]
    public void GeneralInverseReconstructsTheRoleWitness(
        string typeName,
        string methodName,
        int awaits,
        string firstExpected,
        string secondExpected)
    {
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, typeName, methodName);
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal(awaits, function.Descendants.OfType<AwaitExpression>().Count());
        Assert.Contains(firstExpected, result.Output, StringComparison.Ordinal);
        Assert.Contains(secondExpected, result.Output, StringComparison.Ordinal);
        AssertNoMachineResidue(function, result);
    }

    [Fact]
    public void AsyncVoidKickoffReconstructs()
    {
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, FixtureType, "VoidBuilder");
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Contains("await Task.Yield();", result.Output, StringComparison.Ordinal);
        Assert.Contains("completed.Invoke();", result.Output, StringComparison.Ordinal);
        AssertNoMachineResidue(function, result);
    }

    [Fact]
    public void CapturedLocalAfterAwaitFoldsItsClosure()
    {
        // B11-1: Roslyn clears the display-class environment field at its
        // block's end; the clear is dead and is removed so the lambda raise
        // sees one store to the environment.
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, FixtureType, "CapturedLocalAfterAwait");
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal(2, function.Descendants.OfType<AwaitExpression>().Count());
        Assert.DoesNotContain("DisplayClass", result.Output, StringComparison.Ordinal);
        Assert.Contains("+ 1;", result.Output, StringComparison.Ordinal);
        Assert.Contains("read.Invoke()", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void UserClearBeforeAwaitIsCarried()
    {
        // R4-1: the user's `big = null` has the physical form of Roslyn's clear.
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, FixtureType, "UserClearBeforeAwait");
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Contains("big = null;", result.Output, StringComparison.Ordinal);
        Assert.Contains("big is null ? 1 : 0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void JoinClearIsNotAnAdjacentClear()
    {
        // R8-1: the second `text = null` is a branch target, so it stays.
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, FixtureType, "ConditionalThenUnconditionalClear");
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Equal(2, CountOccurrences(result.Output ?? "", "text = null;"));
    }

    [Fact]
    public void CatchSelectorResetsAreCarriedState()
    {
        // R5-1: the await-in-catch lowering's pending-catch selector is reset
        // every iteration and read after the try; it is state, not a clear.
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, LoopFixtureType, "AwaitInCatchLoop");
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal(2, function.Descendants.OfType<AwaitExpression>().Count());
        Assert.Contains("catch (InvalidOperationException)", result.Output, StringComparison.Ordinal);
        Assert.Contains("== 1)", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FixtureType, "AwaitInCatchAndFinallyLoop")]
    [InlineData(LoopFixtureType, "AwaitInFinallyOnly")]
    public void AwaitInsideFinallyDeclinesVisibly(string typeName, string methodName)
    {
        // The lowering catches `object` into a pending-exception holder it
        // rethrows; C# cannot spell a used `catch (object e)`.
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, typeName, methodName);
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Partial, result.Fidelity);
        Assert.False(result.RequiresAsyncBodyModifier);
        Assert.Contains(".Start<", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            function.Diagnostics,
            diagnostic => diagnostic.Id == DiagnosticIds.UnsupportedConstruct
                && diagnostic.Message.Contains("catch (object)", StringComparison.Ordinal));
        Assert.DoesNotContain(function.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.InternalError);
    }

    [Fact]
    public void LocalFunctionKickoffReconstructs()
    {
        // The certificate, not the `<M>d__N` spelling, identifies the machine
        // local: a local function's machine is `<<Outer>g__Inner|N_M>d`.
        using MetadataSource source = OpenFixture();
        IrFunction function = ImportAndRaise(source, FixtureType, LocalFunctionName(source, "<WithLocalFunction>g__Inner|"));
        DecompilerResult result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.True(result.RequiresAsyncBodyModifier);
        Assert.Equal(2, function.Descendants.OfType<AwaitExpression>().Count());
        Assert.Contains("return await task;", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RecipeAcceptancePopulationIsUnchanged()
    {
        // Stage 2 runs behind the recipe index: every request a recipe accepts
        // still reconstructs through that recipe.
        using MetadataSource source = OpenFixture();
        foreach (string methodName in new[] { "AwaitValue", "AwaitVoid", "TwoSequentialAwaits", "AwaitInLoop", "AwaitInTryFinally", "AwaitConditional" })
        {
            IrFunction function = ImportAndRaise(
                source,
                "ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures",
                methodName);
            DecompilerResult result = CSharpPrinter.Print(function);
            Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
            Assert.True(result.RequiresAsyncBodyModifier);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void GeneralInverseFixtureFamilyCompilesBack()
    {
        // The raised body recompiles to a new kickoff and MoveNext, so opcode
        // fidelity against the kickoff is the wrong subject (#4472); the gate is
        // that every reconstructed positive compiles back at all.
        var results = FidelityCheck.Evaluate(
            FixtureCatalog.DecompilerClassicAsync.AssemblyPath(),
            type => type is FixtureType or LoopFixtureType or ReaderType or HolderType,
            method => method.Method is not ("AwaitInCatchAndFinallyLoop" or "AwaitInFinallyOnly" or "Lambda" or "WithLocalFunction"));
        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(
            result.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }

    static void AssertNoMachineResidue(IrFunction function, DecompilerResult result)
    {
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), load => GeneratedCodeIdentity.IsGeneratedFieldName(load.Field.Name));
        Assert.DoesNotContain(function.Descendants.OfType<StoreField>(), store => GeneratedCodeIdentity.IsGeneratedFieldName(store.Field.Name));
        Assert.DoesNotContain(function.Descendants, node => node is Branch or ConditionalBranch or SwitchBranch or Leave);
        Assert.DoesNotContain("<>", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("IsCompleted", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("GetResult", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(function.Diagnostics, diagnostic => diagnostic.Id == DiagnosticIds.InternalError);
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // Roslyn numbers local-function names per containing type, so the suffix
    // moves whenever a fixture is added; find the method by its stable prefix.
    static string LocalFunctionName(MetadataSource source, string prefix)
    {
        var reader = source.Reader;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != "GeneralInverseFixtures")
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                string name = reader.GetString(reader.GetMethodDefinition(methodHandle).Name);
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return name;
            }
        }
        throw new InvalidOperationException($"no method starts with '{prefix}'");
    }

    static MetadataSource OpenFixture()
        => MetadataSource.Open(FixtureCatalog.DecompilerClassicAsync.AssemblyPath());

    static IrFunction ImportAndRaise(MetadataSource source, string typeName, string methodName)
    {
        IrFunction? function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(
            function,
            IrPasses.Default,
            PassContext.ForImport(method => IrImporter.Import(source, method)));
        function.CheckInvariant();
        return function;
    }
}
