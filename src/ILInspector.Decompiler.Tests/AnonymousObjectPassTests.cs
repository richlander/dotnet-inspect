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

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void AnonymousNewObject_RaisesToAnonymousObjectExpression()
    {
        var function = Raised(nameof(CfgSampleClass.AnonymousPair));

        var anonymous = Assert.Single(function.Descendants.OfType<AnonymousObjectExpression>());
        Assert.Equal(["a", "b"], anonymous.MemberNames);
        Assert.Equal(2, anonymous.Values.Count);
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void AnonymousProjection_RendersNamedMembers()
        => Assert.Contains("return new { a = a, b = b };", Print(nameof(CfgSampleClass.AnonymousPair)));

    [Fact]
    public void AnonymousExplicitNames_PreservesNamesAndOrder()
        => Assert.Contains("return new { Id = x, Name = s };", Print(nameof(CfgSampleClass.AnonymousNamed)));

    [Fact]
    public void AnonymousNested_RaisesBothLevels()
    {
        var function = Raised(nameof(CfgSampleClass.AnonymousNested));

        Assert.Equal(2, function.Descendants.OfType<AnonymousObjectExpression>().Count());
        Assert.Empty(function.Descendants.OfType<NewObject>());
        Assert.Contains("return new { p = new { a = a }, q = b };", CSharpPrinter.Print(function).Output!);
    }
}
