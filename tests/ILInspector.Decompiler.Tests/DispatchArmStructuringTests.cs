using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// StructuringPass moves a single-entry dispatch arm into its selecting guard
/// (#10192 PR 3). csc lays a comparison tree's or lowered switch's case bodies
/// out after the tree, each reached by exactly one conditional and rejoining
/// the dispatch's merge; the guard reads as <c>if (c) { arm } else { rest }</c>
/// and every statement still prints once. A body with two entries stays put.
/// </summary>
[Trait("Area", "Pass")]
public class DispatchArmStructuringTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static int Occurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal);
            index >= 0;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    [Fact]
    public void SwitchTreeWithMergingArms_MovesEachArmIntoItsGuard()
    {
        var function = Raised(nameof(CfgSampleClass.SwitchTreeWithMergingArms));

        Assert.Empty(function.Descendants.OfType<Branch>());
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());

        var output = DecidedPrint.Print(function).Output!;
        Assert.DoesNotContain("goto", output);
        Assert.Contains("else", output);
        foreach (string arm in new[] { "flags |= 1;", "flags |= 2;", "flags |= 16;", "flags |= 4;" })
            Assert.Equal(1, Occurrences(output, arm));
    }

    [Fact]
    public void SwitchTreeWithSharedArm_MovesCombinedGuardArm()
    {
        var function = Raised(nameof(CfgSampleClass.SwitchTreeWithSharedArm));

        Assert.Empty(function.Descendants.OfType<Branch>());
        Assert.Empty(function.Descendants.OfType<ConditionalBranch>());

        var output = DecidedPrint.Print(function).Output!;
        Assert.DoesNotContain("goto", output);
        Assert.Contains("V_2 == 'a' || V_2 == 'b'", output);
        Assert.Equal(1, Occurrences(output, "flags |= 1;"));
        Assert.Equal(1, Occurrences(output, "flags |= 4;"));
    }

    [Fact]
    public void SwitchTreeWithScatteredSharedArm_IsNotMoved()
    {
        var function = Raised(nameof(CfgSampleClass.SwitchTreeWithScatteredSharedArm));

        // The shared body is reached by a conditional and by fallthrough, so it
        // is no single-entry arm; the container keeps its valid goto form and
        // prints the body once.
        var output = DecidedPrint.Print(function).Output!;
        Assert.Contains("goto", output);
        Assert.Equal(1, Occurrences(output, "flags |= 1;"));
        Assert.Equal(1, Occurrences(output, "flags |= 2;"));
        Assert.Equal(1, Occurrences(output, "flags |= 4;"));
    }

    [Fact]
    public void SparseSwitchArmsRejoin_KeepsSwitchRaise()
    {
        var function = Raised(nameof(CfgSampleClass.SparseSwitchArmsRejoin));

        var output = DecidedPrint.Print(function).Output!;
        Assert.Contains("switch (kind)", output);
        Assert.DoesNotContain("goto", output);
    }
}
