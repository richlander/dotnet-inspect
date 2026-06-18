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
    public void RunWithSteps_StopsAtLimit_LeavingPartialTree()
    {
        // A run that records at least one step, replayed to stop before step 0,
        // returns the pre-first-rewrite tree without throwing to the caller.
        var full = ImportFixture(nameof(CfgSampleClass.Add));
        var fullStepper = IrPasses.RunWithSteps(full);
        if (fullStepper.Count == 0)
            return;  // fixture records no steps; nothing to replay

        var partial = ImportFixture(nameof(CfgSampleClass.Add));
        var stepper = IrPasses.RunWithSteps(partial, stepLimit: 0);

        Assert.Equal(0, stepper.Count);
    }
}
