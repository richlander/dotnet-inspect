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

    static string Printed(string methodName, Type type)
        => CSharpPrinter.Print(Raised(methodName, type)).Output ?? "";

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
    public void ValueTupleFieldStoresWithStaticFieldTarget_RaiseToMixedDeconstruction()
    {
        // A genuine corelib ValueTuple spill where the second target is a static
        // field store, not a local — `(int a, Holder.Total) = pair;`. The local is
        // fresh, the field an assignment; the field's `.Item2` read is consumed.
        var function = BuildValueTupleFieldStoresWithFieldTarget();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Collection(
            deconstruction.Targets,
            target => Assert.Equal(DeconstructionTargetKind.Local, target.Kind),
            target => Assert.Equal(DeconstructionTargetKind.Field, target.Kind));
        var field = deconstruction.Targets[1];
        Assert.False(field.IsThisInstance);
        Assert.Equal("Total", field.Field!.Name);
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name.StartsWith("Item"));
        function.CheckInvariant();
    }

    [Fact]
    public void IntoTwoInstanceFields_RaisesToFieldDeconstruction()
    {
        var function = Raised(nameof(FieldDeconstructionTargets.IntoTwoInstanceFields), typeof(FieldDeconstructionTargets));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.All(deconstruction.Targets, target =>
        {
            Assert.Equal(DeconstructionTargetKind.Field, target.Kind);
            Assert.True(target.IsThisInstance);
        });
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), f => f.Field.Name.StartsWith("Item"));
    }

    [Fact]
    public void PrintRaised_RendersInstanceFieldDeconstruction_BareNames()
    {
        var output = Printed(nameof(FieldDeconstructionTargets.IntoTwoInstanceFields), typeof(FieldDeconstructionTargets));

        Assert.Contains("(InstanceX, InstanceY) = pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void PrintRaised_RendersStaticFieldDeconstruction_TypeQualified()
    {
        var output = Printed(nameof(FieldDeconstructionTargets.IntoTwoStaticFields), typeof(FieldDeconstructionTargets));

        Assert.Contains("(FieldDeconstructionTargets.StaticX, FieldDeconstructionTargets.StaticY) = pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void PrintRaised_RendersParameterDeconstruction_BareNames()
    {
        var output = Printed(nameof(CfgSampleClass.DeconstructIntoParameters), typeof(CfgSampleClass));

        Assert.Contains("(a, b) = pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void ParameterTargetInValueTupleForm_RaisesToArgumentTarget()
    {
        var function = Raised(nameof(CfgSampleClass.DeconstructIntoParameters), typeof(CfgSampleClass));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.All(deconstruction.Targets, target => Assert.Equal(DeconstructionTargetKind.Argument, target.Kind));
        Assert.False(deconstruction.IsDeclaration);
    }

    [Fact]
    public void ValueTupleFieldStoresWithPropertyTarget_RaiseToDeconstruction()
    {
        var function = BuildValueTupleFieldStoresWithPropertyTarget(sourceNamedTupleTemp: false);

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Equal(2, deconstruction.Targets.Length);
        Assert.Contains(deconstruction.Targets, target => target.Kind == DeconstructionTargetKind.Property && target.PropertyName == "Count");
        Assert.DoesNotContain(function.Descendants.OfType<LoadField>(), field => field.Field.Name is "Item1" or "Item2");
        function.CheckInvariant();
    }

    [Fact]
    public void PrintRaised_RendersPropertyTargetDeconstruction()
    {
        var function = BuildValueTupleFieldStoresWithPropertyTarget(sourceNamedTupleTemp: false);
        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        var output = CSharpPrinter.Print(function).Output;

        Assert.NotNull(output);
        Assert.Contains("(int x, node.Count) = pair;", output);
        Assert.Contains("return x + node.Count;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void SourcePropertyTarget_RaisesToDeconstruction()
    {
        var function = Raised(nameof(DeconstructionAdversarialSamples.PropertyTarget), typeof(DeconstructionAdversarialSamples));

        var deconstruction = Assert.Single(function.Descendants.OfType<DeconstructionAssignment>());
        Assert.Contains(deconstruction.Targets, target => target.Kind == DeconstructionTargetKind.Property && target.PropertyName == "Count");
        var output = CSharpPrinter.Print(function).Output;

        Assert.NotNull(output);
        Assert.Contains("(int x, node.Count) = pair;", output);
        Assert.DoesNotContain(".Item", output);
    }

    [Fact]
    public void SourceNamedTupleTempPropertyTarget_IsNotRaised()
    {
        var function = Raised(nameof(DeconstructionAdversarialSamples.ManualTupleTempPropertyTarget), typeof(DeconstructionAdversarialSamples));

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<StoreProperty>(), store => store.PropertyName == "Count");
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name is "Item1" or "Item2");
    }

    [Fact]
    public void SourceSideEffectingPropertyReceiver_IsNotRaised()
    {
        var function = Raised(nameof(DeconstructionAdversarialSamples.SideEffectingPropertyTarget), typeof(DeconstructionAdversarialSamples));

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<StoreProperty>(), store => store.PropertyName == "Count");
    }

    [Fact]
    public void ValueTupleFieldStoresWithSourceNamedTupleTemp_IsNotRaised()
    {
        var function = BuildValueTupleFieldStoresWithPropertyTarget(sourceNamedTupleTemp: true);

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<StoreProperty>(), store => store.PropertyName == "Count");
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item1");
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name == "Item2");
        function.CheckInvariant();
    }

    [Fact]
    public void ValueTupleFieldStoresWithSideEffectingPropertyReceiver_IsNotRaised()
    {
        var function = BuildValueTupleFieldStoresWithPropertyTarget(sourceNamedTupleTemp: false, sideEffectingReceiver: true);

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<StoreProperty>(), store => store.PropertyName == "Count");
        function.CheckInvariant();
    }

    [Fact]
    public void RealLocalSeedReusedAfterRun_IsNotRaised()
    {
        // A real-local tuple temp read again after the element stores is a live
        // value, not a spill — folding it into a deconstruction would drop the
        // later read, so it must stay lowered.
        var function = BuildRealLocalSeedReusedAfterRun();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name.StartsWith("Item"));
        function.CheckInvariant();
    }

    [Fact]
    public void NonThisInstanceFieldTarget_IsNotRaised()
    {
        // The field receiver is a parameter, not `this` — out of scope. The run
        // declines rather than guessing an evaluation order for the receiver.
        var function = BuildValueTupleStoresWithNonThisFieldTarget();

        new DeconstructionAssignmentPass().Run(function, PassContext.None);

        Assert.DoesNotContain(function.Descendants.OfType<DeconstructionAssignment>(), _ => true);
        Assert.Contains(function.Descendants.OfType<LoadField>(), field => field.Field.Name.StartsWith("Item"));
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

    static IrFunction BuildRealLocalSeedReusedAfterRun()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`2"), [intType, intType]);
        var holderType = TypeRef.Definition("UserAssembly", "Samples", "Holder");
        var block = new Block();
        // A real-local (not stack-slot) tuple temp.
        block.Add(new StoreLocal(0, tupleType, new LoadArgument(0, "pair", tupleType)));
        block.Add(new StoreField(new FieldRef(holderType, "Total", intType), instance: null, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadLocal(0, tupleType))));
        block.Add(new StoreField(new FieldRef(holderType, "Extra", intType), instance: null, new LoadField(new FieldRef(tupleType, "Item2", intType), new LoadLocal(0, tupleType))));
        // The temp is read once more after the run — it is a live value.
        block.Add(new StoreField(new FieldRef(holderType, "Echo", intType), instance: null, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadLocal(0, tupleType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("pair", tupleType)], HasThis: false, GenericParameterCount: 0),
            [tupleType],
            body);
    }

    static IrFunction BuildValueTupleStoresWithNonThisFieldTarget()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var tupleType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`2"), [intType, intType]);
        var holderType = TypeRef.Definition("UserAssembly", "Samples", "Holder");
        var block = new Block();
        block.Add(new StoreStackSlot(0, new LoadArgument(1, "pair", tupleType)));
        block.Add(new StoreLocal(0, intType, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadStackSlot(0, tupleType))));
        // Receiver is a parameter (`holder`), not `this`.
        block.Add(new StoreField(
            new FieldRef(holderType, "Value", intType),
            new LoadArgument(0, "holder", holderType),
            new LoadField(new FieldRef(tupleType, "Item2", intType), new LoadStackSlot(0, tupleType))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [new Parameter("holder", holderType), new Parameter("pair", tupleType)], HasThis: false, GenericParameterCount: 0),
            [intType],
            body);
    }

    static IrFunction BuildValueTupleFieldStoresWithPropertyTarget(bool sourceNamedTupleTemp, bool sideEffectingReceiver = false)
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var tupleType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ValueTuple`2"), [intType, intType]);
        var nodeType = TypeRef.Definition("UserAssembly", "Samples", "Node");
        var setCount = new MethodRef(nodeType, "set_Count", voidType, [intType], HasThis: true);
        var getCount = new MethodRef(nodeType, "get_Count", intType, [], HasThis: true);
        var makeNode = new MethodRef(TypeRef.Definition("UserAssembly", "Samples", "Factory"), "MakeNode", nodeType, [], HasThis: false);
        IrExpression propertyReceiver = sideEffectingReceiver
            ? new Call(makeNode, isVirtual: false, [])
            : new LoadArgument(0, "node", nodeType);

        var block = new Block();
        block.Add(new StoreLocal(1, tupleType, new LoadArgument(1, "pair", tupleType)));
        block.Add(new StoreLocal(0, intType, new LoadField(new FieldRef(tupleType, "Item1", intType), new LoadLocal(1, tupleType))));
        block.Add(new StoreProperty(
            setCount,
            propertyReceiver,
            [],
            new LoadField(new FieldRef(tupleType, "Item2", intType), new LoadLocal(1, tupleType))));
        block.Add(new Return(new Binary(
            BinaryKind.Add,
            isChecked: false,
            isUnsigned: false,
            new LoadLocal(0, intType),
            new LoadProperty(getCount, new LoadArgument(0, "node", nodeType), []))));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Int32"), [new Parameter("node", nodeType), new Parameter("pair", tupleType)], HasThis: false, GenericParameterCount: 0),
            [intType, tupleType],
            body);
        function.LocalNames = ["x", sourceNamedTupleTemp ? "tmp" : null];
        return function;
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
    static readonly Node s_node = new();

    public sealed class Node
    {
        public int Count { get; set; }
    }

    public static int ManualTupleFields((int Sum, int Product) pair)
    {
        int sum = pair.Sum;
        int product = pair.Product;
        return sum + product;
    }

    public static int PropertyTarget(Node node, (int X, int Count) pair)
    {
        (int x, node.Count) = pair;
        return x + node.Count;
    }

    public static int ManualTupleTempPropertyTarget(Node node, (int X, int Count) pair)
    {
        var tmp = pair;
        int x = tmp.X;
        node.Count = tmp.Count;
        return x + node.Count;
    }

    public static int SideEffectingPropertyTarget((int X, int Count) pair)
    {
        (int x, MakeNode().Count) = pair;
        return x;
    }

    static Node MakeNode() => s_node;
}
