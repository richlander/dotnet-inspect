using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RangeFromGetSubArrayPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_intArray = TypeRef.SzArray(s_int);
    static readonly TypeRef s_index = TypeRef.CoreLib("System", "Index");
    static readonly TypeRef s_range = TypeRef.CoreLib("System", "Range");
    static readonly TypeRef s_void = TypeRef.CoreLib("System", "Void");

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
    public void GetSubArray_BothBounds_RaisesToRangeSlice()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeBoth));

        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.True(slice.Range.HasStart);
        Assert.True(slice.Range.HasEnd);
        Assert.Empty(function.Descendants.OfType<Call>());
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void GetSubArray_BothBounds_RendersIndexer()
        => Assert.Contains("return a[i..j];", Print(nameof(CfgSampleClass.ArrayRangeBoth)));

    [Fact]
    public void GetSubArray_ExplicitFromStartIndexCtor_IsNotRaised()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeExplicitFromStartIndex));

        Assert.Empty(function.Descendants.OfType<SliceExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "GetSubArray");
        Assert.Contains(function.Descendants.OfType<NewObject>(),
            n => n.Constructor.DeclaringType is { Namespace: "System", Name: "Index" });

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains("new Index(i, false)", output);
        Assert.DoesNotContain("return a[i..j];", output);
    }

    [Fact]
    public void GetSubArray_FromUserRuntimeHelpersLookalike_IsNotRaised()
    {
        var function = BuildGetSubArrayLookalike();

        new RangeFromGetSubArrayPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SliceExpression>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "GetSubArray");
        function.CheckInvariant();
    }

    [Fact]
    public void GetSubArray_FromOnly_RendersOpenEnd()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeFrom));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.True(slice.Range.HasStart);
        Assert.False(slice.Range.HasEnd);
        Assert.Contains("return a[i..];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_FromEndToOpenEnd_RendersHatOpenEnd()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeFromEnd));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());

        Assert.IsType<IndexFromEnd>(slice.Range.Start);
        Assert.False(slice.Range.HasEnd);
        Assert.Contains("return a[^i..];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_ToOnly_RendersOpenStart()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeTo));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.False(slice.Range.HasStart);
        Assert.True(slice.Range.HasEnd);
        Assert.Contains("return a[..j];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_All_RendersBareRange()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeAll));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.False(slice.Range.HasStart);
        Assert.False(slice.Range.HasEnd);
        Assert.Contains("return a[..];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_FromStartToFromEnd_RendersHat()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeFromEndHi));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.IsType<IndexFromEnd>(slice.Range.End);
        Assert.Contains("return a[i..^1];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_BothFromEnd_RendersHatHat()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeFromEndBoth));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.IsType<IndexFromEnd>(slice.Range.Start);
        Assert.IsType<IndexFromEnd>(slice.Range.End);
        Assert.Contains("return a[^3..^1];", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void GetSubArray_OpenStartFromEnd_RendersOpenHat()
    {
        var function = Raised(nameof(CfgSampleClass.ArrayRangeToFromEnd));
        var slice = Assert.Single(function.Descendants.OfType<SliceExpression>());
        Assert.False(slice.Range.HasStart);
        Assert.IsType<IndexFromEnd>(slice.Range.End);
        Assert.Contains("return a[..^1];", CSharpPrinter.Print(function).Output);
    }

    static IrFunction BuildGetSubArrayLookalike()
    {
        var indexImplicit = new MethodRef(s_index, "op_Implicit", s_index, [s_int], HasThis: false);
        var rangeCtor = new MethodRef(s_range, ".ctor", s_void, [s_index, s_index], HasThis: false);
        var getSubArray = new MethodRef(
            TypeRef.Definition("UserAssembly", "System.Runtime.CompilerServices", "RuntimeHelpers"),
            "GetSubArray",
            s_intArray,
            [s_intArray, s_range],
            HasThis: false);

        var range = new NewObject(rangeCtor,
        [
            new Call(indexImplicit, isVirtual: false, [new LoadArgument(1, "i", s_int)]),
            new Call(indexImplicit, isVirtual: false, [new LoadArgument(2, "j", s_int)]),
        ]);
        var block = new Block();
        block.Add(new Return(new Call(getSubArray, isVirtual: false, [new LoadArgument(0, "a", s_intArray), range])));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(
            s_intArray,
            [new Parameter("a", s_intArray), new Parameter("i", s_int), new Parameter("j", s_int)],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [], body);
    }
}
