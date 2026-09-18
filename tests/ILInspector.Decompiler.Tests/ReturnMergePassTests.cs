using ILInspector.Decompiler.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class ReturnMergePassTests
{
    static readonly TypeRef Holder = TypeRef.CoreLib("Synthetic", "Holder");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void LeaveTargetedReturnTail_RemainsAfterMergeFold()
    {
        var function = BuildReturnTailCandidate(includeLeaveTarget: true);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Contains(function.Body.Blocks, block => block.StartOffset == 0x0003);
        Assert.Single(function.Descendants.OfType<Leave>());
    }

    [Fact]
    public void UntargetedReturnTail_IsRemovedAfterMergeFold()
    {
        var function = BuildReturnTailCandidate(includeLeaveTarget: false);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.DoesNotContain(function.Body.Blocks, block => block.StartOffset == 0x0003);
        Assert.All(function.Body.Blocks, block => Assert.IsType<Return>(Assert.Single(block.Children)));
    }

    [Fact]
    public void MixedConditionalAndGotoPredecessors_InlineDefaultGotoButKeepSharedTail()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<StoreLocal>(defaultArm.Children[0]);
        Assert.IsType<Return>(defaultArm.Children[1]);
        Assert.Contains(function.Body.Blocks, block => block.StartOffset == 0x0005);
    }

    [Fact]
    public void SingleConditionalAndGotoPredecessors_StayForDiamondStructuring()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 1);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Fact]
    public void SwitchAndGotoPredecessors_StayForSwitchRaising()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);
        var success = Assert.Single(function.Body.Blocks, block => block.StartOffset == 0x0003);
        success.Children[0].ReplaceWith(
            new SwitchBranch(new Constant(0, Int32), [0x0005, 0x0005]));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Fact]
    public void MixedPredecessorsWithFallthrough_StayForExistingStructuringRules()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);
        var success = Assert.Single(function.Body.Blocks, block => block.StartOffset == 0x0003);
        success.Children[0].ReplaceWith(new StoreLocal(0, Int32, new Constant(1, Int32)));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Fact]
    public void MixedPredecessorsWithDirectReturn_StayForExistingStructuringRules()
    {
        var (function, defaultArm) = BuildMixedReturnTailCandidate(conditionalPredecessors: 2);
        var merge = Assert.Single(function.Body.Blocks, block => block.StartOffset == 0x0005);
        merge.Children[0].Detach();
        var ret = Assert.IsType<Return>(merge.Children[0]);
        Assert.NotNull(ret.Value);
        ret.Value.ReplaceWith(new Constant(0, Int32));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.IsType<Branch>(Assert.Single(defaultArm.Children));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuredTransferBeforeReturnTail_DoesNotReceiveClonedTail(bool useContinue)
    {
        var (function, transferBlock) = BuildStructuredTransferReturnTailCandidate(useContinue);

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Collection(
            transferBlock.Children,
            statement => Assert.IsType<StoreLocal>(statement),
            statement =>
            {
                if (useContinue)
                    Assert.IsType<Continue>(statement);
                else
                    Assert.IsType<Break>(statement);
            });
        Assert.DoesNotContain(
            function.Descendants.OfType<Block>(),
            block => block.StartOffset == 0x0005);
    }

    [Fact]
    public void CompilerProducedLabeledBreakBeforeReturnTail_PreservesBreak()
    {
        var function = ImportFixtureBeforeReturnMerge(
            nameof(ReturnMergeSamples.LabeledBreakBeforeReturnTail));
        var loop = Assert.Single(function.Descendants.OfType<DoWhileLoop>());
        var transfer = Assert.Single(
            loop.Body.Blocks,
            block => block.Children is
            [StoreLocal { Value: Constant { Value: 2 } }, Break]);
        var merge = Assert.Single(
            loop.Body.Blocks,
            block => block.Children is [Return { Value: LoadLocal }]);
        Assert.Equal(
            2,
            loop.Body.Blocks.Count(block =>
                block.Children is [.., Branch branch]
                && branch.TargetOffset == merge.StartOffset));

        new ReturnMergePass().Run(function, PassContext.None);
        function.CheckInvariant();

        Assert.Collection(
            transfer.Children,
            statement => Assert.IsType<StoreLocal>(statement),
            statement => Assert.IsType<Break>(statement));
        Assert.DoesNotContain(loop.Body.Blocks, block => ReferenceEquals(block, merge));
    }

    [Fact]
    public void CompilerProducedLabeledBreakBeforeReturnTail_RendersAndExecutes()
    {
        var function = ImportFixture(nameof(ReturnMergeSamples.LabeledBreakBeforeReturnTail));
        string output = CSharpPrinter.Print(function).Output ?? "";

        Assert.Contains("result = 2;\n        break;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("break;\n        return result;", output, StringComparison.Ordinal);

        var compiled = Compile(output);
        foreach (int input in new[] { -1, 0, 1, 2, 3, 4 })
            Assert.Equal(ReturnMergeSamples.LabeledBreakBeforeReturnTail(input), compiled(input));
    }

    static (IrFunction Function, Block DefaultArm) BuildMixedReturnTailCandidate(int conditionalPredecessors)
    {
        var body = new BlockContainer();
        var firstGuard = new Block(0x0000);
        firstGuard.Add(new ConditionalBranch(new Constant(true, TypeRef.CoreLib("System", "Boolean")), 0x0005));
        body.Add(firstGuard);
        var defaultArm = new Block(0x0001);
        defaultArm.Add(new Branch(0x0005));
        body.Add(defaultArm);
        if (conditionalPredecessors == 2)
        {
            var secondGuard = new Block(0x0002);
            secondGuard.Add(new ConditionalBranch(new Constant(false, TypeRef.CoreLib("System", "Boolean")), 0x0005));
            body.Add(secondGuard);
        }
        var success = new Block(0x0003);
        success.Add(new Return(new Constant(1, Int32)));
        body.Add(success);
        var merge = new Block(0x0005);
        merge.Add(new StoreLocal(0, Int32, new Constant(0, Int32)));
        merge.Add(new Return(new LoadLocal(0, Int32)));
        body.Add(merge);
        return (new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body), defaultArm);
    }

    static IrFunction BuildReturnTailCandidate(bool includeLeaveTarget)
    {
        var body = new BlockContainer();

        var first = new Block(0x0000);
        first.Add(new Branch(0x0003));
        body.Add(first);

        var second = new Block(0x0001);
        second.Add(new Branch(0x0003));
        body.Add(second);

        var merge = new Block(0x0003);
        merge.Add(new Return(new Constant(1, Int32)));
        body.Add(merge);

        if (includeLeaveTarget)
        {
            var residue = new Block(0x0004);
            residue.Add(new Leave(0x0003));
            body.Add(residue);
        }

        return new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [new Parameter("x", Int32)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static (IrFunction Function, Block TransferBlock) BuildStructuredTransferReturnTailCandidate(
        bool useContinue)
    {
        var loopBody = new BlockContainer();

        var first = new Block(0x0000);
        first.Add(new Branch(0x0005));
        loopBody.Add(first);

        var second = new Block(0x0001);
        second.Add(new Branch(0x0005));
        loopBody.Add(second);

        var transfer = new Block(0x0003);
        transfer.Add(new StoreLocal(0, Int32, new Constant(2, Int32)));
        transfer.Add(useContinue ? new Continue() : new Break());
        loopBody.Add(transfer);

        var merge = new Block(0x0005);
        merge.Add(new Return(new Constant(1, Int32)));
        loopBody.Add(merge);

        var entry = new Block(0x0010);
        entry.Add(new DoWhileLoop(
            loopBody,
            new Constant(false, TypeRef.CoreLib("System", "Boolean"))));
        entry.Add(new Return(new Constant(0, Int32)));

        var body = new BlockContainer();
        body.Add(entry);
        return (new IrFunction(
            "M",
            Holder,
            new MethodSignature(Int32, [], HasThis: false, GenericParameterCount: 0),
            [Int32],
            body), transfer);
    }

    static IrFunction ImportFixtureBeforeReturnMerge(string methodName)
    {
        using var source = MetadataSource.Open(typeof(ReturnMergeSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ReturnMergeSamples).FullName!, methodName);
        Assert.NotNull(function);
        foreach (var pass in IrPasses.Default)
        {
            if (pass is ReturnMergePass)
                break;
            pass.Run(function, PassContext.None);
        }
        function.CheckInvariant();
        return function;
    }

    static IrFunction ImportFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(ReturnMergeSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ReturnMergeSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function);
        function.CheckInvariant();
        return function;
    }

    static Func<int, int> Compile(string body)
    {
        string source = $$"""
            static class Synthetic
            {
                public static int M(int x)
                {
            {{body}}
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "return-merge-gate",
            [tree],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id}: {diagnostic.GetMessage()}")
            .ToArray();
        Assert.True(
            errors.Length == 0,
            "Rendered body must compile, got:\n  "
                + string.Join("\n  ", errors)
                + "\n--- body ---\n"
                + body);

        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(assemblyStream);
        Assert.True(
            emit.Success,
            string.Join(
                "\n",
                emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        var method = Assembly.Load(assemblyStream.ToArray())
            .GetType("Synthetic")!
            .GetMethod("M", BindingFlags.Public | BindingFlags.Static)!;
        return value => (int)method.Invoke(null, [value])!;
    }
}
