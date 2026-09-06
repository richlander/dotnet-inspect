using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StepperTests
{
    static IrFunction ImportFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }

    [Fact]
    public void DisabledStepper_RecordsNothing()
    {
        var stepper = new Stepper(enabled: false);

        stepper.StepOver("ignored");
        using (stepper.StepInto("ignored group"))
            stepper.StepOver("ignored child");

        Assert.Empty(stepper.Steps);
        Assert.Equal(0, stepper.Count);
    }

    [Fact]
    public void EnabledStepper_RecordsLeafStepsInOrder()
    {
        var stepper = new Stepper(enabled: true);

        stepper.StepOver("first");
        stepper.StepOver("second");

        Assert.Equal(2, stepper.Count);
        Assert.Equal(["first", "second"], stepper.Steps.Select(s => s.Description));
        Assert.Equal([0, 1], stepper.Steps.Select(s => s.Index));
    }

    [Fact]
    public void StepInto_NestsChildrenUnderTheGroup()
    {
        var stepper = new Stepper(enabled: true);

        using (stepper.StepInto("group"))
        {
            stepper.StepOver("inner-a");
            stepper.StepOver("inner-b");
        }
        stepper.StepOver("after");

        var group = Assert.Single(stepper.Steps, s => s.Description == "group");
        Assert.Equal(["inner-a", "inner-b"], group.Children.Select(s => s.Description));
        Assert.Contains(stepper.Steps, s => s.Description == "after");
    }

    [Fact]
    public void StepLimit_ThrowsRightBeforeTheLimitedStep()
    {
        var stepper = new Stepper(enabled: true) { StepLimit = 2 };

        stepper.StepOver("zero");
        stepper.StepOver("one");

        // Step 2 is the limit: recording it throws, leaving steps 0 and 1.
        Assert.Throws<StepLimitReachedException>(() => stepper.StepOver("two"));
        Assert.Equal(2, stepper.Count);
    }

    [Fact]
    public void RecordsTheNearNodePosition()
    {
        var stepper = new Stepper(enabled: true);
        var node = new Constant(7, TypeRef.CoreLib("System", "Int32"));

        stepper.StepOver("touch", node);

        Assert.Equal(node.Describe(), Assert.Single(stepper.Steps).Position);
    }

    [Fact]
    public void RunWithSteps_RecordsPassRewrites()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));

        var stepper = IrPasses.RunWithSteps(function);

        // The default run completes without a limit; stepping is observation only.
        Assert.Equal(IrPrinter.Dump(function), IrPrinter.Dump(function));
        Assert.True(stepper.Count >= 0);
    }

    [Fact]
    public void RunWithSteps_InstrumentsMultiplePasses()
    {
        // ReverseCopy exercises structuring plus the ++/-- fold; before the
        // passes were instrumented only the inliner/structurer recorded steps,
        // so a method like this reported a single step. Guard that the raising
        // passes now each record their rewrite.
        var function = ImportFixture(nameof(CfgSampleClass.ReverseCopy));

        var stepper = IrPasses.RunWithSteps(function);
        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();

        Assert.True(stepper.Count >= 3, $"expected several recorded steps, got {stepper.Count}");
        Assert.Contains(descriptions, d => d.Contains("structure container", StringComparison.Ordinal));
        Assert.Contains(descriptions, d => d.Contains("fold dup", StringComparison.Ordinal));
    }

    [Fact]
    public void CrossMethodImports_DoNotRecordNestedStepsInParentStepper()
    {
        var parent = ImportFixture(nameof(CfgSampleClass.Add));
        var imported = ImportFixture(nameof(CfgSampleClass.Twice));
        var stepper = new Stepper(enabled: true);
        var target = new MethodRef(
            TypeRef.Definition("Asm", "Ns", "Type"),
            "Imported",
            TypeRef.CoreLib("System", "Void"),
            [],
            HasThis: false);
        var context = new PassContext(stepper, importMethodBody: method => method == target ? imported : null);

        new ImportingStepPass(target).Run(parent, context);

        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();
        Assert.Equal(["parent before import", "parent after import"], descriptions);
    }

    [Theory]
    [InlineData(nameof(HeterogeneousArmSample.GuardedArea), false, false)]
    [InlineData(nameof(HeterogeneousArmSample.GuardedArea), true, true)]
    [InlineData(nameof(HeterogeneousArmSample.Area), false, true)]
    [InlineData(nameof(HeterogeneousArmSample.Area), true, true)]
    public void RunWithSteps_UsesOptionalDisjointnessEvidence(
        string methodName, bool useEvidence, bool expectPatternSwitch)
    {
        using var source = MetadataSource.Open(typeof(HeterogeneousArmSample).Assembly.Location);
        var function = IrImporter.Import(source, typeof(HeterogeneousArmSample).FullName!, methodName);
        var stagedFunction = IrImporter.Import(source, typeof(HeterogeneousArmSample).FullName!, methodName);
        Assert.NotNull(function);
        Assert.NotNull(stagedFunction);
        var stages = IrPasses.RunWithStages(stagedFunction, importMethodBody: null,
            typesProvablyDisjoint: useEvidence ? source.AreProvablyDisjoint : null);

        var steps = useEvidence
            ? IrPasses.RunWithSteps(function, int.MaxValue, importMethodBody: null, source.AreProvablyDisjoint)
            : IrPasses.RunWithSteps(function, int.MaxValue, importMethodBody: null);

        function.CheckInvariant();
        Assert.Equal(expectPatternSwitch, function.Descendants.OfType<PatternSwitchExpression>().Any());
        Assert.Equal(stages[^1].Projection, IrPrinter.Dump(function));
        Assert.Equal(stagedFunction.Fidelity, function.Fidelity);
        Assert.Equal(expectPatternSwitch, Flatten(steps.Steps).Any(
            step => step.Description == "raise nested type-pattern dispatch to switch expression"));
    }

    [Theory]
    [InlineData(nameof(HeterogeneousArmSample.GuardedArea))]
    [InlineData(nameof(HeterogeneousArmSample.Area))]
    public void RunWithSteps_StopsBeforePatternSwitchRewrite(string methodName)
    {
        using var source = MetadataSource.Open(typeof(HeterogeneousArmSample).Assembly.Location);
        IrFunction Import()
        {
            var function = IrImporter.Import(source, typeof(HeterogeneousArmSample).FullName!,
                methodName);
            Assert.NotNull(function);
            return function;
        }

        var completed = Import();
        var steps = IrPasses.RunWithSteps(completed, int.MaxValue, importMethodBody: null,
            source.AreProvablyDisjoint);
        completed.CheckInvariant();
        Assert.Single(completed.Descendants.OfType<PatternSwitchExpression>());
        var rewrite = Assert.Single(Flatten(steps.Steps),
            step => step.Description == "raise nested type-pattern dispatch to switch expression");

        var before = Import();
        var stopped = IrPasses.RunWithSteps(before, rewrite.Index, importMethodBody: null,
            source.AreProvablyDisjoint);

        before.CheckInvariant();
        Assert.Equal(rewrite.Index, stopped.Count);
        Assert.Empty(before.Descendants.OfType<PatternSwitchExpression>());

        var after = Import();
        var advanced = IrPasses.RunWithSteps(after, rewrite.Index + 1, importMethodBody: null,
            source.AreProvablyDisjoint);

        after.CheckInvariant();
        Assert.Equal(rewrite.Index + 1, advanced.Count);
        Assert.Single(after.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void PinnedLocalAudit_RecordsFixedStatementRewrite()
    {
        var function = ImportFixture(nameof(CfgSampleClass.SumPinnedArray));

        var stepper = IrPasses.RunWithSteps(function);
        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();

        Assert.Contains(descriptions, d => d == "raise pinned locals to fixed statements");
        var fixedStatement = Assert.Single(function.Descendants.OfType<Fixed>());
        Assert.Equal("int", fixedStatement.ElementType.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<StoreLocal>(), store =>
            store.Type.Kind == TypeRefKind.Pinned);
    }

    [Fact]
    public void PinnedArrayAudit_RecordsFixedArrayRewrite()
    {
        var function = ImportFixture(nameof(CfgSampleClass.FixedWholeArray));

        var stepper = IrPasses.RunWithSteps(function);
        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();

        Assert.Contains(descriptions, d => d == "raise pinned array to fixed statement");
        var fixedStatement = Assert.Single(function.Descendants.OfType<Fixed>());
        Assert.Equal("byte", fixedStatement.ElementType.ToDisplayString());
        Assert.DoesNotContain(function.Descendants.OfType<StoreLocal>(), store =>
            store.Type.Kind == TypeRefKind.Pinned);
    }

    [Fact]
    public void RawStackAllocAudit_RecordsNoStackAllocSpanRewrite()
    {
        var function = ImportFixture(nameof(CfgSampleClass.StackAllocFirst));

        var stepper = IrPasses.RunWithSteps(function);
        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();

        Assert.DoesNotContain(descriptions, d => d == "raise Span-over-stackalloc to stackalloc T[n]");
        Assert.Single(function.Descendants.OfType<StackAllocate>());
        Assert.DoesNotContain(function.Descendants.OfType<StackAllocArray>(), _ => true);
    }

    [Fact]
    public void DeclarationPlacementAudit_ReturnAccumulatorIsSunkAfterStructuring()
    {
        var function = ImportFixture(nameof(CfgSampleClass.GotoCommonExit));

        var stepper = IrPasses.RunWithSteps(function);
        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();

        Assert.Contains(descriptions, d => d.StartsWith("inline return-merge", StringComparison.Ordinal));
        Assert.Contains(descriptions, d => d == "sink return-accumulator store into return");
        Assert.DoesNotContain(function.Descendants.OfType<StoreLocal>(), store => store.Index == 0);
        Assert.DoesNotContain(function.Descendants.OfType<LoadLocal>(), load => load.Index == 0);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.DoesNotContain("V_0", output);
        Assert.DoesNotContain("= default", output);
        Assert.Contains("return 2;", output);
        Assert.Contains("return 1;", output);
        Assert.Contains("return 0;", output);
    }

    static IEnumerable<Step> Flatten(IEnumerable<Step> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var child in Flatten(step.Children))
                yield return child;
        }
    }

    sealed class ImportingStepPass(MethodRef target) : IIrPass
    {
        public string Name => "importing-step-pass";

        public void Run(IrFunction function, PassContext context)
        {
            context.Stepper.StepOver("parent before import");
            Assert.True(context.TryImportAndRunMethodBody(target, [new NestedStepPass()], out var imported));
            Assert.NotNull(imported);
            context.Stepper.StepOver("parent after import");
        }
    }

    sealed class NestedStepPass : IIrPass
    {
        public string Name => "nested-step-pass";

        public void Run(IrFunction function, PassContext context)
            => context.Stepper.StepOver("nested imported rewrite");
    }
}
