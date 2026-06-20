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
        var local = Assert.IsType<LoadLocal>(assignment.Target);
        Assert.Equal("string", local.Type.ToDisplayString());
        Assert.IsType<LoadArgument>(assignment.Value);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);
    }

    [Fact]
    public void StaticFieldNullAssignmentDiamond_RaisesToNullCoalescingAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticField));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>());
        var field = Assert.IsType<LoadField>(assignment.Target);
        Assert.Equal("s_cachedName", field.Field.Name);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("s_cachedName ??= fallback;", output);
    }

    [Fact]
    public void InstanceFieldNullAssignmentDiamond_RaisesToNullCoalescingAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignInstanceField));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>());
        Assert.IsType<LoadField>(assignment.Target);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("_cachedText ??= fallback;", output);
    }

    [Fact]
    public void StaticPropertyNullAssignmentDiamond_RaisesToNullCoalescingAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.NullCoalescingAssignStaticProperty));

        var assignment = Assert.Single(function.Descendants.OfType<NullCoalescingAssignment>());
        Assert.IsType<LoadProperty>(assignment.Target);
        Assert.DoesNotContain(function.Descendants.OfType<IfStatement>(), _ => true);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("StaticName ??= fallback;", output);
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
}
