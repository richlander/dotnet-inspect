using ILInspector.DecompilerHarness;
using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using static ILInspector.Decompiler.Tests.ReferenceSlotMaterializationTestHelpers;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class SingleLoadSlotMaterializationTests
{
    [Fact]
    public void CompilerProducedSingleLoadCopyComponentMaterializes()
    {
        string path = typeof(SingleLoadSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(SingleLoadSlotMaterializationSamples).FullName!,
            nameof(SingleLoadSlotMaterializationSamples.ReadDisplay));
        AssertSingleLoadComponentMaterializes(function);
    }

    [Fact]
    public void RealRoslynDisplaySingleLoadComponentMaterializes()
    {
        string path = typeof(SyntaxTree).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source,
            "Microsoft.CodeAnalysis.Diagnostics.AnalyzerImageReference", "get_Display");
        AssertSingleLoadComponentMaterializes(function);
    }

    [Theory]
    [InlineData(nameof(SingleLoadSlotMaterializationSamples.ConditionalConsumer), typeof(Conditional), "?")]
    [InlineData(nameof(SingleLoadSlotMaterializationSamples.CoalesceConsumer), typeof(Coalesce), "??")]
    [InlineData(nameof(SingleLoadSlotMaterializationSamples.ShortCircuitConsumer), typeof(LogicalBinary), "&&")]
    [InlineData(nameof(SingleLoadSlotMaterializationSamples.CacheDisplay), typeof(NullCoalescingFieldAssignmentExpression), "??=")]
    public void ExistingExpressionFoldPrecedesMaterialization(string method, Type expressionType, string spelling)
    {
        string path = typeof(SingleLoadSlotMaterializationSamples).Assembly.Location;
        using var metadata = CorpusMetadata.Create([path]);
        using var source = MetadataSource.Open(path, context: metadata);
        var function = RaiseToMaterialization(source, typeof(SingleLoadSlotMaterializationSamples).FullName!, method);
        var expression = Assert.Single(function.Descendants, node => node.GetType() == expressionType);
        Assert.Empty(SlotMaterializationPass.Analyze(function));
        string before = Assert.IsType<string>(CSharpPrinter.Print(function).Output);

        new SlotMaterializationPass().Run(function, PassContext.None);

        Assert.Contains(expression, function.Descendants);
        Assert.Equal(before, CSharpPrinter.Print(function).Output);
        Assert.Contains(spelling, before);
        function.CheckInvariant(includeSemantics: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    public async Task CompilerProducedStorageAndFoldsKeepNativeRtsContracts()
    {
        Type samples = typeof(SingleLoadSlotMaterializationSamples);
        string[] methods =
        [
            nameof(SingleLoadSlotMaterializationSamples.ReadDisplay),
            nameof(SingleLoadSlotMaterializationSamples.CacheDisplay),
            nameof(SingleLoadSlotMaterializationSamples.ConditionalConsumer),
            nameof(SingleLoadSlotMaterializationSamples.CoalesceConsumer),
            nameof(SingleLoadSlotMaterializationSamples.ShortCircuitConsumer),
        ];
        var results = await ReturnToSender.CompileBackTargets(samples.Assembly.Location,
            methods.Select(method => new ReturnToSender.RequestedTarget(samples.FullName!, method, 0)).ToArray(),
            sourceIndex: null, applyCompileBackFloor: false);
        Assert.Equal(methods.Order(),
            results.Select(result => result.MemberAnchor?.MemberName).Order());
        foreach (var result in results)
        {
            Assert.False(result.UsedCompileBackFloor);
            Assert.NotNull(result.MemberAnchor);
            if (result.MemberAnchor.MemberName == nameof(SingleLoadSlotMaterializationSamples.ReadDisplay))
            {
                // Earlier raising already retains the outer fallback as a branch.
                Assert.Equal(FidelityCheck.CompileBackStatus.OpcodeDiff, result.Status);
                Assert.Equal(
                    "ldarg ldfld dup brtrue pop ldarg ldfld dup brtrue pop ldarg ldfld callvirt ret",
                    result.OriginalOpcodes);
                Assert.Equal(
                    "ldarg ldfld dup stloc brtrue ldarg ldfld dup brtrue pop ldarg ldfld callvirt stloc ldloc ret",
                    result.RecompiledOpcodes);
            }
            else
            {
                Assert.True(result.Status == FidelityCheck.CompileBackStatus.Exact,
                    $"{result.MemberAnchor}: {result.Status}: {result.Detail}");
            }
        }
    }

    static void AssertSingleLoadComponentMaterializes(IrFunction function)
    {
        var decisions = SlotMaterializationPass.Analyze(function);
        Assert.NotEmpty(decisions);
        Assert.Contains(decisions, decision =>
            function.Descendants.OfType<StoreStackSlot>().Count(store => store.Slot == decision.Slot) > 1
            && function.Descendants.OfType<LoadStackSlot>().Count(load => load.Slot == decision.Slot) == 1);
        Assert.All(decisions, decision => Assert.True(decision.WillMaterialize));
        Assert.Contains(function.Descendants.OfType<StoreStackSlot>(),
            store => store.Value is LoadStackSlot);
        var controlFlow = function.Descendants.OfType<IfStatement>().ToArray();
        var calls = function.Descendants.OfType<Call>().ToArray();
        var coalesces = function.Descendants.OfType<Coalesce>().ToArray();
        var invariant = SlotMaterializationInvariant.Capture(function);

        new SlotMaterializationPass().Run(function, PassContext.None);

        invariant.Check();
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<LoadStackSlot>());
        Assert.Equal(controlFlow, function.Descendants.OfType<IfStatement>());
        Assert.Equal(calls, function.Descendants.OfType<Call>());
        Assert.Equal(coalesces, function.Descendants.OfType<Coalesce>());
        Assert.All(decisions, decision => Assert.Contains($"S_{decision.Slot}", function.SynthesizedLocalNames));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
    }
}
