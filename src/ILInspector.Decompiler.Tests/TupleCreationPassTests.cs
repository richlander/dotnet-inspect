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
    public void RestTuple_FlattensNestedConstructionToOneLiteral()
    {
        var function = Raised(nameof(CfgSampleClass.TupleRest));

        var tuple = Assert.Single(function.Descendants.OfType<TupleExpression>());
        Assert.Equal(8, tuple.Elements.Count);
        // Both the outer ValueTuple`8 and the inner ValueTuple`1 rest are consumed.
        Assert.Empty(function.Descendants.OfType<NewObject>());

        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.DoesNotContain("new ValueTuple", output);
        Assert.DoesNotContain("ValueTuple<", output);
    }

    [Fact]
    public void RestTuple_NonInlineRest_StaysNewObject()
    {
        // The eighth argument is a pre-existing ValueTuple value, not an inline
        // `new ValueTuple<int>(h)` — a hand-written construction, not a literal, so
        // it must stay a constructor (a literal always builds its rest inline).
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var rest1 = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`1"), [intType]);
        var octType = TypeRef.GenericInstance(
            TypeRef.CoreLib("System", "ValueTuple`8"),
            [intType, intType, intType, intType, intType, intType, intType, rest1]);
        var ctor = new MethodRef(octType, ".ctor", voidType,
            [intType, intType, intType, intType, intType, intType, intType, rest1], HasThis: false);
        IrExpression Arg(int i) => new Constant(i, intType);
        var newObject = new NewObject(ctor,
            [Arg(1), Arg(2), Arg(3), Arg(4), Arg(5), Arg(6), Arg(7), new LoadArgument(0, "rest", rest1)]);
        var block = new Block();
        block.Add(new Return(newObject));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(octType, [new Parameter("rest", rest1)], HasThis: false, GenericParameterCount: 0),
            [],
            body);

        new TupleCreationPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<TupleExpression>());
        Assert.Single(function.Descendants.OfType<NewObject>());
        function.CheckInvariant();
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
