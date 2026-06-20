using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ObjectInitializerPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void ObjectInitializer_RaisesPropertyMembers()
    {
        var function = Raised(nameof(CfgSampleClass.MakePoint));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Y"], initializer.Members);
        // The creation is retained as a child so fidelity/unsafe scans still see it.
        Assert.Single(function.Descendants.OfType<NewObject>());
        // The lowered dup chain is gone.
        Assert.Empty(function.Descendants.OfType<StoreStackSlot>());
        Assert.Empty(function.Descendants.OfType<StoreProperty>());
    }

    [Fact]
    public void ObjectInitializer_RaisesFieldMembers()
    {
        var initializer = Assert.Single(Raised(nameof(CfgSampleClass.MakePointWithField)).Descendants.OfType<ObjectInitializerExpression>());

        Assert.False(initializer.IsCollection);
        Assert.Equal(["X", "Z"], initializer.Members);
    }

    [Fact]
    public void CollectionInitializer_RaisesAddCalls()
    {
        var function = Raised(nameof(CfgSampleClass.MakeList));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        Assert.Equal(3, initializer.Values.Count);
        // No Add call survives as a standalone statement.
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void DictionaryInitializer_RaisesMultiArgAddCalls()
    {
        var function = Raised(nameof(CfgSampleClass.MakeDictionary));

        var initializer = Assert.Single(function.Descendants.OfType<ObjectInitializerExpression>());
        Assert.True(initializer.IsCollection);
        // Two { k, v } entries, each contributing a key and a value (4 values total).
        Assert.Equal([2, 2], initializer.EntryArities);
        Assert.Equal(4, initializer.Values.Count);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Add");
    }

    [Fact]
    public void PlainConstruction_WithoutInitializer_StaysNewObject()
    {
        var function = Raised(nameof(CfgSampleClass.MakeEmpty));

        Assert.DoesNotContain(function.Descendants.OfType<ObjectInitializerExpression>(), _ => true);
        Assert.Single(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void Initializer_InArgumentPosition_IsRaised()
    {
        var output = Print(nameof(CfgSampleClass.MakeAndRead));

        Assert.Contains("new InitTarget { X = a }", output);
        Assert.DoesNotContain(".X = a;", output);
    }

    [Fact]
    public void InitializerWithExtraOutsideUse_IsNotFoldedIntoSingleExpression()
    {
        // The expression-position slice requires exactly one outside use of the
        // threaded receiver. A kept-alive local has two uses (KeepAlive + return),
        // so folding it into a single object-initializer expression would erase a
        // real use site.
        var function = Raised(nameof(CfgSampleClass.NamedPointInitializerKeptAlive));

        Assert.Empty(function.Descendants.OfType<ObjectInitializerExpression>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".X = a;", output);
        Assert.Contains("GC.KeepAlive", output);
    }

    [Fact]
    public void PrintRaised_RendersInitializers()
    {
        Assert.Contains("return new InitTarget { X = a, Y = b };", Print(nameof(CfgSampleClass.MakePoint)));
        Assert.Contains("return new InitTarget { X = a, Z = b };", Print(nameof(CfgSampleClass.MakePointWithField)));
        Assert.Contains("return new List<int> { a, b, 42 };", Print(nameof(CfgSampleClass.MakeList)));
        Assert.Contains("return new Dictionary<int, string> { { 1, a }, { 2, b } };", Print(nameof(CfgSampleClass.MakeDictionary)));
    }
}
