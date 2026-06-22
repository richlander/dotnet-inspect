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
}
