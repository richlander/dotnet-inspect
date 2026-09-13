using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class GenericIsInstanceNullTestTests
{
    [Fact]
    public void ValueContextGenericIsInstanceNotNull_RendersAsTypeTest()
    {
        using var source = MetadataSource.Open(typeof(GenericIsInstanceSpecimens<>).Assembly.Location);
        var function = IrImporter.Import(
            source,
            typeof(GenericIsInstanceSpecimens<>).FullName!,
            nameof(GenericIsInstanceSpecimens<object>.DirectIs));
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("value is T", output);
        Assert.DoesNotContain("value as T", output);
    }

    [Fact]
    public void ValueContextGenericIsInstanceNull_RendersAsNegatedTypeTest()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var generic = TypeRef.GenericParameter(0, "T");
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new IsInstance(generic, new LoadArgument(0, "value", objectType)),
            new Constant(null, objectType));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(boolType, [new Parameter("value", objectType)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("return value is not T;", output);
        Assert.DoesNotContain("value as T", output);
    }
}

public abstract class GenericIsInstanceSpecimens<T>
{
    public bool DirectIs(object value) => value is T;
    public string Ternary(object value) => value is T ? "y" : "n";
}
