using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ListPatternPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void SingleElementStringArrayListPattern_RaisesToListPattern()
    {
        var function = Raised(nameof(CfgSampleClass.SingleElementStringArrayListPattern));

        var pattern = Assert.Single(function.Descendants.OfType<SingleElementListPattern>());
        Assert.Equal(["--help", "-h", "/?", "-?"], pattern.Alternatives.Select(a => Assert.IsType<string>(a.Value)));

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("if (args is [\"--help\" or \"-h\" or \"/?\" or \"-?\"])", output);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("V_0", output);
        Assert.DoesNotContain("V_1", output);
        Assert.DoesNotContain("args.Length != 1", output);
    }

    [Fact]
    public void ManualSingleElementStringArrayGuard_StaysSourceGuard()
    {
        var function = Raised(nameof(CfgSampleClass.ManualSingleElementStringArrayGuard));

        Assert.Empty(function.Descendants.OfType<SingleElementListPattern>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("if (args is not null && args.Length == 1)", output);
        Assert.Contains("arg = args[0];", output);
        Assert.Contains("if (arg == \"--help\" || arg == \"-h\" || arg == \"/?\" || arg == \"-?\")", output);
        Assert.DoesNotContain("args is [", output);
    }

    [Fact]
    public void Stepper_RecordsListPatternRaise()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.SingleElementStringArrayListPattern));
        Assert.NotNull(function);

        var stepper = IrPasses.RunWithSteps(function!);
        var descriptions = Flatten(stepper.Steps).Select(s => s.Description).ToList();

        Assert.Contains("raise single-element string-array list pattern", descriptions);
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
