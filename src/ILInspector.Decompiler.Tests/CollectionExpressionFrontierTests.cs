using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CollectionExpressionFrontierTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location, locator: RuntimeLocator);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static IrFunction RaisedWithoutSymbols(string methodName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location, RuntimeLocator);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    static readonly Lazy<IReadOnlyDictionary<string, string>> s_runtimeAssemblies = new(() =>
        (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key!, group => group.First(), StringComparer.OrdinalIgnoreCase));

    static readonly AssemblyLocator RuntimeLocator = (name, trust) =>
        trust == AssemblyTrust.Platform && s_runtimeAssemblies.Value.TryGetValue(name, out var path)
            ? path
            : null;

    [Fact]
    public void GeneralCollectionExpressionWithCapacitySpread_RemainsLowered()
    {
        var function = Raised(nameof(CfgSampleClass.CollectionWithCapacity));

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.DoesNotContain(function.Descendants.OfType<ObjectInitializerExpression>(), initializer => initializer.IsCollection);
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "AddRange");

        var output = Print(nameof(CfgSampleClass.CollectionWithCapacity));
        Assert.Contains("new List<string>(values.Count * 2)", output);
        Assert.Contains(".AddRange(", output);
        Assert.DoesNotContain("[with", output);
    }

    [Fact]
    public void GeneralCollectionExpressionWithComparer_RendersAsCollectionInitializer()
    {
        var function = Raised(nameof(CfgSampleClass.CollectionWithComparer));

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        Assert.Equal(3, initializer.Entries.Count);

        var output = Print(nameof(CfgSampleClass.CollectionWithComparer));
        Assert.Contains(
            "new HashSet<string>(StringComparer.OrdinalIgnoreCase) { \"Hello\", \"HELLO\", \"hello\" }",
            output);
    }

    [Fact]
    public void ArraySpreadWithTail_RaisesToCollectionExpression()
    {
        // The array-target collection expression `[..prefix, tail]` lowers to a
        // compiler-hidden source span, destination span, copy-length index, final
        // element store, and return of the same array. The pass requires those
        // hidden temporaries so source-named manual lowerings stay visible.
        var function = Raised(nameof(CfgSampleClass.ArraySpreadWithTail));
        string output = CSharpPrinter.Print(function).Output!;

        var collection = Assert.Single(function.Descendants.OfType<CollectionExpression>());
        Assert.IsType<CollectionSpreadElement>(collection.Elements[0]);
        Assert.Contains("return [..prefix, tail];", output);
        Assert.DoesNotContain("new int[1 +", output);
        Assert.DoesNotContain("CopyTo", output);
    }

    [Fact]
    public void ArraySpreadWithTail_WithoutSymbols_RemainsLowered()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.ArraySpreadWithTail));
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains("CopyTo", output);
        Assert.DoesNotContain("[..", output);
    }

    [Fact]
    public void ManualArraySpreadLowering_WithSourceNamedTemp_RemainsLowered()
    {
        var function = Raised(nameof(CfgSampleClass.ManualArraySpreadLoweringWithSourceTemp));
        string output = CSharpPrinter.Print(function).Output!;

        Assert.Empty(function.Descendants.OfType<CollectionExpression>());
        Assert.Contains("CopyTo", output);
        Assert.DoesNotContain("return [..prefix, tail];", output);
    }
}
