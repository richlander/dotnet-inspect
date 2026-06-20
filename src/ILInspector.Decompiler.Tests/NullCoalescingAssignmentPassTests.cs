using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class NullCoalescingAssignmentPassTests
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
    public void LocalNullAssignmentDiamond_RaisesToNullCoalescingAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignLocal));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>());
        Assert.Equal("string", assignment.LocalType.ToDisplayString());
        Assert.IsType<LoadArgument>(assignment.Value);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignLocal))).Output;

        Assert.NotNull(output);
        Assert.Contains("value ??= fallback;", output);
        Assert.Contains("return value;", output);
    }

    [Fact]
    public void LocalNullAssignmentWithExtraThenStatement_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignLocalWithExtraThenStatement));

        Assert.Empty(function.Descendants.OfType<NullCoalescingAssignment>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("??=", output);
        Assert.Contains("if (value is null)", output);
        Assert.Contains("LastValue = value.Length;", output);
    }

    [Fact]
    public void NullCoalescingOperator_RemainsExpression()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalesce));

        Assert.Single(function.Descendants.OfType<Coalesce>());
        Assert.DoesNotContain(function.Descendants.OfType<NullCoalescingAssignment>(), _ => true);
    }

    [Fact]
    public void StaticFieldNullAssignmentDiamond_RaisesToNullCoalescingFieldAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticField));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingFieldAssignment>());
        Assert.Equal("CachedName", assignment.Field.Name);
        Assert.False(assignment.HasInstance);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersStaticFieldNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticField))).Output;

        Assert.NotNull(output);
        Assert.Contains("CachedName ??= fallback;", output);
    }

    [Fact]
    public void InstanceFieldNullAssignmentDiamond_RaisesToNullCoalescingFieldAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceField));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingFieldAssignment>());
        Assert.Equal("Cache", assignment.Field.Name);
        Assert.True(assignment.HasInstance);
        Assert.IsType<LoadArgument>(assignment.Instance);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void PrintRaised_RendersInstanceFieldNullCoalescingAssignment()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceField))).Output;

        Assert.NotNull(output);
        Assert.Contains("holder.Cache ??= fallback;", output);
    }

    [Fact]
    public void FieldNullAssignmentWithExtraThenStatement_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignFieldWithExtraThenStatement));

        Assert.Empty(function.Descendants.OfType<NullCoalescingFieldAssignment>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("??=", output);
    }
}
