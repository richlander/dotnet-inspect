using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class BoxedReferenceEqualityTests
{
    [Fact]
    public void BoxedGenericEquality_PreservesObjectCasts()
    {
        using var source = MetadataSource.Open(typeof(BoxedReferenceEqualitySpecimens).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(BoxedReferenceEqualitySpecimens).FullName!,
            nameof(BoxedReferenceEqualitySpecimens.IndexOfReference));
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("if ((object)item == (object)list[i])", output);
        Assert.DoesNotContain("if ((item) == (list[i]))", output);
    }

    [Fact]
    public void BoxedGenericInequality_PreservesObjectCasts()
    {
        var generic = TypeRef.MethodGenericParameter(0, "T");
        var comparison = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new Box(generic, new LoadArgument(0, "left", generic)),
            new Box(generic, new LoadArgument(1, "right", generic)));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [new Parameter("left", generic), new Parameter("right", generic)],
                HasThis: false,
                GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("return (object)left != (object)right;", output);
        Assert.DoesNotContain("return left != right;", output);
    }
}

public static class BoxedReferenceEqualitySpecimens
{
    public static int IndexOfReference<T>(System.Collections.Generic.List<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if ((object?)item == (object?)list[i])
                return i;
        }

        return -1;
    }
}
