using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class DefaultConstructorArgumentTests
{
    [Theory]
    [InlineData(typeof(DefaultBaseArgumentArray))]
    [InlineData(typeof(DefaultBaseArgumentSpan))]
    [InlineData(typeof(DefaultThisArgument))]
    public void CompilerDefaultArgument_RaisesConstructorInitializer(Type type)
    {
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, ".ctor");
        Assert.NotNull(function);
        Assert.Contains(function.Descendants, node => node is InitObject);

        IrPasses.Run(function);
        var result = CSharpPrinter.Print(function);

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.NotNull(result.ConstructorChain);
        Assert.Contains("default(", result.ConstructorChain);
        Assert.DoesNotContain(function.Descendants, node => node is InitObject);
        function.CheckInvariant();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [Trait("Area", "Fidelity")]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerDefaultArguments_CompileBackExact(bool lowered)
    {
        var types = new[] { typeof(DefaultBaseArgumentArray), typeof(DefaultBaseArgumentSpan), typeof(DefaultThisArgument) };
        var results = FidelityCheck.Evaluate(types[0].Assembly.Location, lowered: lowered,
            type => types.Any(sample => sample.FullName == type))
            .Where(result => result.Method.StartsWith(".ctor", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, results.Length);
        Assert.All(results, result => Assert.True(
            result.Status == FidelityCheck.CompileBackStatus.Exact,
            $"{result.Method}: {result.Status}: {result.Detail}"));
    }
}

public class DefaultArgumentBase
{
    public DefaultArgumentBase(int? options = null) => Options = options;
    public int? Options;
}

public sealed class DefaultBaseArgumentArray : DefaultArgumentBase
{
    public DefaultBaseArgumentArray(params object?[] items) : base() => Count = items.Length;
    public int Count;
}

public sealed class DefaultBaseArgumentSpan : DefaultArgumentBase
{
    public DefaultBaseArgumentSpan(params ReadOnlySpan<object?> items) : base() => Count = items.Length;
    public int Count;
}

public sealed class DefaultThisArgument
{
    public DefaultThisArgument(object?[] items) : this(default(int?), items.Length) { }
    public DefaultThisArgument(int? options, int count)
    {
        Options = options;
        Count = count;
    }
    public int? Options;
    public int Count;
}
