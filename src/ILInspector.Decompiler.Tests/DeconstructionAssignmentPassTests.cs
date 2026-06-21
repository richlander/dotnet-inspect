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
    public void MixedLocalTargets_RaiseToMixedDeconstruction()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructMixedLocal));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.False(deconstruction.IsDeclaration);
        // `sum` is declared here, `product` pre-exists.
        Assert.Equal([true, false], deconstruction.IsDeclared);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name is "Item1" or "Item2");
    }

    [Fact]
    public void PrintRaised_RendersMixedDeconstruction_DeclaresOnlyTheFreshTarget()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.DeconstructMixedLocal))).Output;

        Assert.NotNull(output);
        // `sum` declared inline, `product` (pre-existing) assigned bare.
        Assert.Contains("(int sum, product) = pair;", output);
        Assert.DoesNotContain(".Item", output);
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
    public void MixedFreshAndExistingValueTupleTargets_RaiseToMixedDeconstruction()
    {
        var function = BuildMixedValueTupleTargets();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        // Local 0 is fresh (declared here); local 1 pre-exists (assigned).
        Assert.Equal([true, false], deconstruction.IsDeclared);
        Assert.False(deconstruction.IsDeclaration);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), field => field.Field.Name.StartsWith("Item"));
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

    [Fact]
    public void DeconstructMethodWithNonLocalTarget_IsNotRaised()
    {
        // `r.Deconstruct(out localA, out byRefParam)` — a target that is a
        // parameter (StoreArgument/by-ref), not a local. Only all-local targets
        // are in scope, so the non-local target must keep the de-sugared call.
        var function = BuildDeconstructCallWithParameterTarget();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "Deconstruct");
        function.CheckInvariant();
    }

    [Fact]
    public void DeconstructMethodWithFieldReceiver_IsNotRaised()
    {
        // `holder.Pair.Deconstruct(out a, out b)` — a field-load receiver, the
        // shape the temp-then-copy lowering leaves behind. Only side-effect-free
        // local/parameter receivers are in scope, so this keeps the call.
        var function = BuildDeconstructCallWithFieldReceiver();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<Call>(), call => call.Callee.Name == "Deconstruct");
        function.CheckInvariant();
    }

    [Fact]
    public void ValueTupleFieldStoresWithNonLocalTarget_IsNotRaised()
    {
        // A genuine corelib ValueTuple spill, but the second target is a field
        // store, not a local. Mixed local/non-local runs are out of scope, so the
        // whole run declines rather than raising a partial deconstruction.
        var function = BuildValueTupleFieldStoresWithFieldTarget();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item1");
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item2");
        function.CheckInvariant();
    }

    static IrFunction BuildDeconstructCallWithParameterTarget()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var receiverType = TypeRef.Definition("UserAssembly", "Samples", "Pairing");
        var deconstruct = new MethodRef(
            receiverType,
            "Deconstruct",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.ByRef(intType), TypeRef.ByRef(intType)],
            HasThis: true);

        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            deconstruct,
            isVirtual: false,
            // Second out target is a by-ref parameter, not a local.
            [new LoadLocalAddress(0, intType), new LoadLocalAddress(1, intType), new LoadArgumentAddress(1, "product", intType)])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("pairing", receiverType), new Parameter("product", TypeRef.ByRef(intType))],
                HasThis: false,
                GenericParameterCount: 0),
            [receiverType, intType],
            body);
    }

    static IrFunction BuildDeconstructCallWithFieldReceiver()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var receiverType = TypeRef.Definition("UserAssembly", "Samples", "Pairing");
        var holderType = TypeRef.Definition("UserAssembly", "Samples", "Holder");
        var deconstruct = new MethodRef(
            receiverType,
            "Deconstruct",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.ByRef(intType), TypeRef.ByRef(intType)],
            HasThis: true);

        IrExpression receiver = new LoadField(
            new FieldRef(holderType, "Pair", receiverType),
            new LoadArgument(0, "holder", holderType));
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
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("holder", holderType)], HasThis: false, GenericParameterCount: 0),
            [intType, intType],
            body);
    }

    static IrFunction BuildValueTupleFieldStoresWithFieldTarget()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`2"), [intType, intType]);
        var holderType = TypeRef.Definition("UserAssembly", "Samples", "Holder");
        var block = new Block();
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "pair", tupleType)));
        block.Add(new StoreLocal(0, intType, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadStackSlot(0, tupleType))));
        // Second target is a static field store, not a local.
        block.Add(new StoreField(
            new FieldRef(holderType, "Total", intType),
            instance: null,
            new LoadField(new FieldRef(tupleType, "Item2", intType), new LoadStackSlot(0, tupleType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("pair", tupleType)], HasThis: false, GenericParameterCount: 0),
            [intType],
            body);
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
