using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public sealed class ExpressionContextParenthesesTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.OrderedBinarySpillSamples";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompoundConditionalUsesFullExpressionContexts(bool updated, bool lowered)
    {
        string path = (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy)
            .AssemblyPath();
        using var source = MetadataSource.Open(path);
        Assert.Contains("checked { value += choose ? Replace(ref value, amount) : amount * 2; }",
            Print(source, FixtureType, "SnapshotAcrossMutation", lowered));
        Assert.Contains("value += choose ? amount + 1 : amount * 2;",
            Print(source, FixtureType, "UncheckedConditional", lowered));
        Assert.Contains("checked { value -= choose ? unchecked(amount + 1) : amount * 2; }",
            Print(source, FixtureType, "Subtract", lowered));
        Assert.Contains("checked { value *= choose ? unchecked(amount + 1) : amount * 2; }",
            Print(source, FixtureType, "Multiply", lowered));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalAsAdditiveOperandRetainsGrouping(bool updated)
    {
        string path = (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy)
            .AssemblyPath();
        using var source = MetadataSource.Open(path);
        Assert.Contains("checked(value + (choose ? unchecked(amount + 1) : amount * 2))",
            Print(source, FixtureType, "ReturnConditional"));
    }

    [Fact]
    public void RuntimeSqlBytesConditionalKeepsCastsWithoutOuterParentheses()
    {
        using var source = MetadataSource.Open(typeof(System.Data.SqlTypes.SqlBytes).Assembly.Location);
        string output = Print(source, "System.Data.SqlTypes.SqlBytes", "get_MaxLength");
        Assert.Contains("return _rgbBuf is null ? (long)-1 : (long)_rgbBuf.Length;", output);
    }

    static string Print(MetadataSource source, string type, string method, bool lowered = false)
    {
        var function = IrImporter.Import(source, type, method);
        Assert.NotNull(function);
        IrPasses.Run(function, lowered ? IrPasses.Lowered : IrPasses.Default,
            PassContext.ForImport(reference => IrImporter.Import(source, reference), source.AreProvablyDisjoint));
        Assert.Empty(CoercionInvariant.Check(function));
        function.CheckInvariant(includeSemantics: true);
        return CSharpPrinter.Print(function).Output!;
    }
}
