using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class DeconstructionAssignmentPassTests
{
    static IrFunction Raised(string methodName, Type? type = null)
    {
        type ??= typeof(CfgSampleClass);
        using var source = MetadataSource.Open(type.Assembly.Location);
        var function = IrImporter.Import(source, type.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ValueTupleFieldStores_RaiseToDeconstruction()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructTuplePair));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.LocalIndices.Length);
        Assert.True(deconstruction.IsDeclaration);
        Assert.IsType<LoadArgument>(deconstruction.Source);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name is "Item1" or "Item2");
    }

    [Fact]
    public void ExistingLocalStores_RaiseToDeconstructionAssignment()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructIntoExistingLocals));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.LocalIndices.Length);
        Assert.False(deconstruction.IsDeclaration);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name is "Item1" or "Item2");
    }

    [Fact]
    public void PrintRaised_RendersDeconstructionAssignment_WithoutTypes()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DeconstructIntoExistingLocals))).Output;

        Assert.NotNull(output);
        Assert.Contains("(sum, product) = pair;", output);
        Assert.DoesNotContain("(int sum, int product) = pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void PrintRaised_RendersDeconstructionDeclaration()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DeconstructTuplePair))).Output;

        Assert.NotNull(output);
        Assert.Contains("(int sum, int product) = pair;", output);
        Assert.Contains("return sum + product;", output);
    }

    [Fact]
    public void DeconstructMethodCall_RaisesToDeconstructionDeclaration()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructViaMethod));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.LocalIndices.Length);
        Assert.True(deconstruction.IsDeclaration);
        Assert.IsType<LoadArgument>(deconstruction.Source);
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "Deconstruct");
    }

    [Fact]
    public void PrintRaised_RendersDeconstructMethodDeclaration()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DeconstructViaMethod))).Output;

        Assert.NotNull(output);
        Assert.Contains(") = pairing;", output);
        Assert.Contains("(int ", output);
        Assert.DoesNotContain(".Deconstruct(", output);
    }

    [Fact]
    public void HandWrittenTupleFieldAccess_IsNotRaised()
    {
        var function = Raised(nameof(DeconstructionAdversarialSamples.ManualTupleFields), typeof(DeconstructionAdversarialSamples));

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        var output = CSharpPrinter.Print(function).Output;
        Assert.NotNull(output);
        Assert.Contains(".Item", output);
        Assert.DoesNotContain("(int sum, int product) = pair;", output);
    }

    [Fact]
    public void UserValueTupleLookalike_FieldStoresAreNotRaised()
    {
        var function = BuildUserValueTupleFieldStores();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item1");
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item2");
        function.CheckInvariant();
    }

    [Fact]
    public void MixedFreshAndExistingValueTupleTargets_AreNotRaised()
    {
        var function = BuildMixedValueTupleTargets();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item1");
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item2");
        function.CheckInvariant();
    }

    [Fact]
    public void DeconstructMethodWithSideEffectingReceiver_IsNotRaised()
    {
        var function = BuildDeconstructCall(receiverIsSideEffecting: true);

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "Deconstruct");
        function.CheckInvariant();
    }

    static IrFunction BuildMixedValueTupleTargets()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`2"), [intType, intType]);
        var block = new Block();
        // Local 1 already exists; local 0 first appears in the deconstruction run.
        block.Add(new StoreLocal(1, intType, new Constant(0, intType)));
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "pair", tupleType)));
        block.Add(new StoreLocal(0, intType, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadStackSlot(0, tupleType))));
        block.Add(new StoreLocal(1, intType, new LoadField(new FieldRef(tupleType, "Item2", intType), new LoadStackSlot(0, tupleType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("pair", tupleType)], HasThis: false, GenericParameterCount: 0),
            [intType, intType],
            body);
    }

    static IrFunction BuildDeconstructCall(bool receiverIsSideEffecting)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var receiverType = TypeRef.Definition("UserAssembly", "Samples", "Pairing");
        var deconstruct = new MethodRef(
            receiverType,
            "Deconstruct",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.ByRef(intType), TypeRef.ByRef(intType)],
            HasThis: true);
        var makePair = new MethodRef(
            TypeRef.Definition("UserAssembly", "Samples", "Factory"),
            "MakePair",
            receiverType,
            [],
            HasThis: false);

        IrExpression receiver = receiverIsSideEffecting
            ? new Call(makePair, isVirtual: false, [])
            : new LoadArgumentAddress(0, "pairing", receiverType);
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            deconstruct,
            isVirtual: false,
            [receiver, new LoadLocalAddress(0, intType), new LoadLocalAddress(1, intType)])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("pairing", receiverType)], HasThis: false, GenericParameterCount: 0),
            [intType, intType, intType, intType],
            body);
    }

    static IrFunction BuildUserValueTupleFieldStores()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(
            TypeRef.Definition("UserAssembly", "System", "ValueTuple`2"),
            [intType, intType]);
        var block = new Block();
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "pair", tupleType)));
        block.Add(new StoreLocal(0, intType, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadStackSlot(0, tupleType))));
        block.Add(new StoreLocal(1, intType, new LoadField(new FieldRef(tupleType, "Item2", intType), new LoadStackSlot(0, tupleType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("pair", tupleType)], HasThis: false, GenericParameterCount: 0),
            [intType, intType],
            body);
    }
}

public static class DeconstructionAdversarialSamples
{
    public static int ManualTupleFields((int Sum, int Product) pair)
    {
        int sum = pair.Sum;
        int product = pair.Product;
        return sum + product;
    }
}
