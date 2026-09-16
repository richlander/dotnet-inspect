using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Instructions;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class FinallyReturnTimingTests
{
    static readonly Type SampleType = typeof(FinallyReturnTimingSample);

    [Fact]
    public void NormalContinuationReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.Run(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.Run(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.Run));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void NestedFinallyReturnStaysAfterExitedFinallys()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunNested(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunNested(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunNested));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void ArgumentReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunArgument(
            result: 0,
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunArgument(
            result: 0,
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunArgument));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void AliasedLocalReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunAliasedLocal(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunAliasedLocal(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunAliasedLocal));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void ConditionalAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunConditionalAlias(
            useResult: true,
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunConditionalAlias(
            useResult: true,
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(10, FinallyReturnTimingSample.RunConditionalAlias(
            useResult: false,
            loop: true,
            setValue: true,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunConditionalAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void FieldAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunFieldAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunFieldAlias(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunFieldAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void RefReturnFieldAliasConvergesAndStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunRefReturnFieldAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunRefReturnFieldAlias(
            loop: true,
            setValue: false,
            exit: true));

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunRefReturnFieldAlias)));
        Assert.Contains(
            function.Descendants.OfType<StoreField>(),
            store => store.Instance is Call);

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void CallAliasReturnStaysAfterFinally()
    {
        Assert.Equal(100, FinallyReturnTimingSample.RunCallAlias(
            useResult: true,
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(10, FinallyReturnTimingSample.RunCallAlias(
            useResult: false,
            loop: true,
            setValue: true,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunCallAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void ArgumentAliasReturnStaysAfterFinally()
    {
        int alias = 7;
        Assert.Equal(110, FinallyReturnTimingSample.RunArgumentAlias(
            ref alias,
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(7, alias);
        Assert.Equal(100, FinallyReturnTimingSample.RunArgumentAlias(
            ref alias,
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(7, alias);

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunArgumentAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void ConstructorAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunConstructorAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunConstructorAlias(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunConstructorAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void FieldExtractionAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunFieldExtractionAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunFieldExtractionAlias(
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunFieldExtractionAlias(
            loop: false,
            setValue: true,
            exit: true));

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunFieldExtractionAlias)));
        StoreLocal extraction = Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            store => store.Value is LoadField);
        IrExpression receiver = Assert.IsAssignableFrom<IrExpression>(
            Assert.IsType<LoadField>(extraction.Value).Instance);
        Assert.True(receiver is LoadLocal or LoadLocalAddress);

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void NestedFieldExtractionAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunNestedFieldExtractionAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunNestedFieldExtractionAlias(
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunNestedFieldExtractionAlias(
            loop: false,
            setValue: true,
            exit: true));

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunNestedFieldExtractionAlias)));
        StoreLocal extraction = Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            store => store.Value is LoadField);
        LoadFieldAddress nestedCarrier = Assert.IsType<LoadFieldAddress>(
            Assert.IsType<LoadField>(extraction.Value).Instance);
        Assert.IsType<LoadLocalAddress>(nestedCarrier.Instance);

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void HelperAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunHelperAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunHelperAlias(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunHelperAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void CopiedFieldAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunCopiedFieldAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunCopiedFieldAlias(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunCopiedFieldAlias));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void IndirectCarrierAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunIndirectCarrierAlias(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunIndirectCarrierAlias(
            loop: true,
            setValue: false,
            exit: true));

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunIndirectCarrierAlias)));
        Assert.Contains(
            function.Descendants.OfType<StoreIndirect>(),
            store => store.Type?.Name.EndsWith(
                "RefHolder",
                StringComparison.Ordinal) == true);

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void ConditionalIndirectCarrierAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunConditionalIndirectCarrierAlias(
            useCopy: true,
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunConditionalIndirectCarrierAlias(
            useCopy: true,
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(10, FinallyReturnTimingSample.RunConditionalIndirectCarrierAlias(
            useCopy: false,
            loop: true,
            setValue: true,
            exit: true));

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunConditionalIndirectCarrierAlias)));
        StoreIndirect indirect = Assert.Single(
            function.Descendants.OfType<StoreIndirect>(),
            store => store.Type?.Name.EndsWith(
                "RefHolder",
                StringComparison.Ordinal) == true
                && store.Address is LoadStackSlot);
        int destinationSlot = Assert.IsType<LoadStackSlot>(
            indirect.Address).Slot;
        StoreStackSlot[] definitions =
        [
            .. function.Descendants.OfType<StoreStackSlot>()
                .Where(store => store.Slot == destinationSlot),
        ];
        Assert.Equal(2, definitions.Length);
        Assert.All(
            definitions,
            definition => Assert.IsType<LoadLocalAddress>(
                definition.Value));

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void RefLocalIndirectCarrierAliasReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunRefLocalIndirectCarrierAlias(
            useCopy: true,
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunRefLocalIndirectCarrierAlias(
            useCopy: true,
            loop: true,
            setValue: false,
            exit: true));
        Assert.Equal(10, FinallyReturnTimingSample.RunRefLocalIndirectCarrierAlias(
            useCopy: false,
            loop: true,
            setValue: true,
            exit: true));

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunRefLocalIndirectCarrierAlias)));
        StoreIndirect[] indirectStores =
        [
            .. function.Descendants.OfType<StoreIndirect>()
                .Where(store => store.Type?.Name.EndsWith(
                    "RefHolder",
                    StringComparison.Ordinal) == true
                    && store.Address is LoadLocal),
        ];
        Assert.Equal(2, indirectStores.Length);
        int destinationLocal = Assert.IsType<LoadLocal>(
            indirectStores[0].Address).Index;
        Assert.All(
            indirectStores,
            store => Assert.Equal(
                destinationLocal,
                Assert.IsType<LoadLocal>(store.Address).Index));
        StoreLocal definition = Assert.Single(
            function.Descendants.OfType<StoreLocal>(),
            store => store.Index == destinationLocal);
        int destinationSlot = Assert.IsType<LoadStackSlot>(
            definition.Value).Slot;
        Assert.Equal(
            2,
            function.Descendants.OfType<StoreStackSlot>()
                .Count(store => store.Slot == destinationSlot));

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void NestedReturnUsesOrderedSharedCleanupFacts()
    {
        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        IrFunction function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.RunNested)));
        InstructionExceptionFlowFacts facts = Assert.IsType<
            InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available>(
                    function.ExceptionFlow).Value;
        InstructionNormalTransfer[] transfers =
        [
            .. function.Descendants.OfType<Leave>()
                .Select(leave => facts.NormalTransferAt(
                    leave.SourceOffset,
                    leave.TargetOffset))
                .OfType<InstructionExceptionFlowResult<
                    InstructionNormalTransfer>.Available>()
                .Select(static result => result.Value)
                .Where(static transfer =>
                    transfer.CleanupHandlers.Length > 0),
        ];

        Assert.Contains(
            transfers,
            transfer => transfer.CleanupHandlers.Length == 2
                && transfer.CleanupHandlers.Select(
                    static cleanup => cleanup.Clause).Distinct().Count() == 2);

        var (_, output) = Render(
            nameof(FinallyReturnTimingSample.RunNested));
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void FinallyReturnTimingMethodsCompileBackExactly()
    {
        var results = FidelityCheck.Evaluate(
            SampleType.Assembly.Location,
            type => type == SampleType.FullName,
            method => method.Method is nameof(FinallyReturnTimingSample.Run)
                or nameof(FinallyReturnTimingSample.RunArgument)
                or nameof(FinallyReturnTimingSample.RunAliasedLocal)
                or nameof(FinallyReturnTimingSample.RunConditionalAlias)
                or nameof(FinallyReturnTimingSample.RunFieldAlias)
                or nameof(FinallyReturnTimingSample.RunRefReturnFieldAlias)
                or nameof(FinallyReturnTimingSample.RunCallAlias)
                or nameof(FinallyReturnTimingSample.RunArgumentAlias)
                or nameof(FinallyReturnTimingSample.RunConstructorAlias)
                or nameof(FinallyReturnTimingSample.RunFieldExtractionAlias)
                or nameof(FinallyReturnTimingSample.RunNestedFieldExtractionAlias)
                or nameof(FinallyReturnTimingSample.RunHelperAlias)
                or nameof(FinallyReturnTimingSample.RunCopiedFieldAlias)
                or nameof(FinallyReturnTimingSample.RunIndirectCarrierAlias)
                or nameof(FinallyReturnTimingSample.RunConditionalIndirectCarrierAlias)
                or nameof(FinallyReturnTimingSample.RunRefLocalIndirectCarrierAlias));

        Assert.Equal(16, results.Count);
        Assert.All(results, result =>
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
    }

    static (DecompilationFidelity Fidelity, string Output) Render(string methodName)
    {
        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            methodName));

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        return (function.Fidelity, output);
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
    }
}
