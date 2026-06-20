using ILInspector.Decompiler.Pipeline;
using System.Collections.Immutable;

namespace ILInspector.Decompiler.Tests;

public class TupleCreationPassTests
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
    public void ValueTupleConstructor_RaisesToTupleExpression()
    {
        var function = Raised(nameof(CfgSampleClass.TuplePair));

        var tuple = Assert.Single(function.Descendants.OfType<TupleExpression>());
        Assert.Equal(2, tuple.Elements.Count);
        Assert.StartsWith("ValueTuple<", tuple.TupleType.ToDisplayString());
        Assert.Empty(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void PrintRaised_RendersTupleLiteral()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.TuplePair))).Output;

        Assert.NotNull(output);
        Assert.Contains("return (a + b, a * b);", output);
        Assert.DoesNotContain("new ValueTuple", output);
    }

    [Fact]
    public void ValueTupleConstructor_AsExpressionStatement_StaysNewObject()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "ValueTuple`2"),
            [intType, intType]);
        var ctor = new MethodRef(tupleType, ".ctor", TypeRef.CoreLib("System", "Void"), [intType, intType], HasThis: false);
        var newObject = new NewObject(ctor, [new Constant(1, intType), new Constant(2, intType)]);
        var block = new Block();
        block.Add(new ExpressionStatement(newObject));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new TupleCreationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<TupleExpression>());
        Assert.Single(function.Descendants.OfType<NewObject>());
    }

    [Fact]
    public void UserValueTupleLookalike_IsNotRaised()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(
            TypeRef.Definition("UserAssembly", "System", "ValueTuple`2"),
            [intType, intType]);
        var ctor = new MethodRef(tupleType, ".ctor", TypeRef.CoreLib("System", "Void"), [intType, intType], HasThis: false);
        var newObject = new NewObject(ctor, [new Constant(1, intType), new Constant(2, intType)]);
        var block = new Block();
        block.Add(new Return(newObject));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(tupleType, ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new TupleCreationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<TupleExpression>());
        Assert.Single(function.Descendants.OfType<NewObject>());
    }
}
