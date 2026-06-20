using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class AnonymousObjectPassTests
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
    public void Shorthand_RaisesAnonymousTypeConstructor()
    {
        // `new { a, b }` lowers to `new <>f__AnonymousType0<int, string>(a, b)`.
        var function = Raised(nameof(CfgSampleClass.AnonShorthand));

        var anonymous = Assert.Single(function.Descendants.OfType<AnonymousObject>());
        Assert.Equal(["a", "b"], anonymous.PropertyNames);
        Assert.Equal(2, anonymous.Values.Count);
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void Shorthand_RendersProjectionShorthand()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonShorthand))).Output;

        Assert.NotNull(output);
        Assert.Contains("new { a, b }", output);
        Assert.DoesNotContain("f__AnonymousType", output);
    }

    [Fact]
    public void NamedMembers_RenderExplicitAssignments()
    {
        // The values are named x/y but the properties are Id/Name, so the
        // shorthand cannot apply — the explicit `Name = value` form is required.
        var function = Raised(nameof(CfgSampleClass.AnonNamed));

        var anonymous = Assert.Single(function.Descendants.OfType<AnonymousObject>());
        Assert.Equal(["Id", "Name"], anonymous.PropertyNames);

        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("new { Id = x, Name = y }", output);
    }

    [Fact]
    public void SingleMember_Renders()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonSingle))).Output;
        Assert.Contains("new { a }", output);
    }

    [Fact]
    public void MemberAccess_UsesShorthandWhenNamesMatch()
    {
        // `new { t.X, t.Y }` projects properties X/Y from the member names.
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.AnonMemberShorthand))).Output;
        Assert.Contains("new { t.X, t.Y }", output);
    }
}
