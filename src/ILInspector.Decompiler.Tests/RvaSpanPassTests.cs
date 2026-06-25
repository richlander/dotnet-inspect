using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class RvaSpanPassTests
{
    static readonly TypeRef s_int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef s_runtimeFieldHandle = TypeRef.CoreLib("System", "RuntimeFieldHandle");
    static readonly TypeRef s_readOnlySpanInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [s_int]);

    [Fact]
    public void CreateSpan_FromUserRuntimeHelpersLookalike_IsNotRaised()
    {
        var function = BuildCreateSpanLookalike();

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "CreateSpan");
        function.CheckInvariant();
    }

    static IrFunction BuildCreateSpanLookalike()
    {
        var createSpan = new MethodRef(
            TypeRef.Definition("UserAssembly", "System.Runtime.CompilerServices", "RuntimeHelpers"),
            "CreateSpan",
            s_readOnlySpanInt,
            [s_runtimeFieldHandle],
            HasThis: false)
        {
            TypeArguments = [s_int],
        };
        var token = new LoadToken(RuntimeTokenKind.Field, null, "User.Field")
        {
            FieldRvaData = [1, 0, 0, 0],
        };

        var block = new Block();
        block.Add(new Return(new Call(createSpan, isVirtual: false, [token])));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(s_readOnlySpanInt, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [], body);
    }

    static readonly TypeRef s_byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef s_readOnlySpanByte = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [s_byte]);

    [Fact]
    public void ReadOnlySpanByte_FromPaddedRvaField_RaisesToExactLengthLiteral()
    {
        // csc's 1-byte-element optimization sizes the backing holder struct one
        // byte past the span's length (a trailing pad), so the captured blob runs
        // one byte longer than the data the span reads. The raise must honour the
        // span's own length (3 here), decoding exactly those bytes and ignoring the
        // pad — not bail because blob length (4) != span length (3).
        var function = BuildReadOnlySpanByteCtor([10, 20, 30, 0], spanLength: 3);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NewObject>());
        var literal = Assert.Single(function.Descendants.OfType<SpanLiteral>());
        Assert.Equal(s_byte, literal.ElementType);
        Assert.Equal([10, 20, 30], literal.Elements.Select(e => (byte)((Constant)e).Value!));
        function.CheckInvariant();
    }

    [Fact]
    public void ReadOnlySpanByte_FromNegativeLength_StaysUnraised()
    {
        var function = BuildReadOnlySpanByteCtor([10, 20, 30, 0], spanLength: -1);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains(function.Descendants.OfType<NewObject>(), n => n.Constructor.Name == ".ctor");
        function.CheckInvariant();
    }

    [Fact]
    public void ReadOnlySpanInt_FromOverflowingLength_StaysUnraised()
    {
        var function = BuildReadOnlySpanIntCtor([1, 0, 0, 0], spanLength: int.MaxValue);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains(function.Descendants.OfType<NewObject>(), n => n.Constructor.Name == ".ctor");
        function.CheckInvariant();
    }

    [Fact]
    public void ReadOnlySpanEnum_FromRvaField_RaisesToEnumLiteral()
    {
        var enumType = TypeRef.Definition("Synthetic", "Samples", "State", ValueTypeHint.ValueType);
        var function = BuildReadOnlySpanEnumCtor(enumType, [0, 2, 3], spanLength: 3);
        function.TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum };
        function.EnumMembers = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
        {
            [enumType] = new Dictionary<long, string> { [0] = "Start", [2] = "Done" },
        };

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<NewObject>());
        var literal = Assert.Single(function.Descendants.OfType<SpanLiteral>());
        Assert.Equal(enumType, literal.ElementType);
        Assert.Equal([0, 2, 3], literal.Elements.Select(e => (int)((Constant)e).Value!));
        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("State.Start", output);
        Assert.Contains("State.Done", output);
        Assert.Contains("(State)3", output);
        function.CheckInvariant();
    }

    [Fact]
    public void ReadOnlySpanEnum_WithUnknownShape_StaysUnraised()
    {
        var enumType = TypeRef.Definition("Synthetic", "Samples", "State", ValueTypeHint.ValueType);
        var function = BuildReadOnlySpanEnumCtor(enumType, [0, 1, 2], spanLength: 3);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains(function.Descendants.OfType<NewObject>(), n => n.Constructor.Name == ".ctor");
        function.CheckInvariant();
    }

    [Fact]
    public void InitializeArray_LocalClobberedBeforeCall_StaysUnraised()
    {
        var function = BuildInitializeArrayWithClobberedLocal();

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Single(function.Descendants.OfType<NewArray>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InitializeArray");
        function.CheckInvariant();
    }

    [Fact]
    public void InitializeArray_StackSlotClobberedBeforeCall_StaysUnraised()
    {
        var function = BuildInitializeArrayWithClobberedStackSlot();

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Single(function.Descendants.OfType<NewArray>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InitializeArray");
        function.CheckInvariant();
    }

    [Fact]
    public void ReadOnlySpanByte_FromUserLookalikeConstructor_IsNotRaised()
    {
        // #1399: a user assembly's `System.ReadOnlySpan<byte>` lookalike has the same
        // name/shape but is not corelib, so the RVA optimization raise must decline.
        var userSpan = TypeRef.GenericInstance(
            TypeRef.Definition("UserAssembly", "System", "ReadOnlySpan`1"), [s_byte]);
        var function = BuildReadOnlySpanCtor(s_byte, userSpan, [10, 20, 30, 0], spanLength: 3);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<SpanLiteral>());
        Assert.Contains(function.Descendants.OfType<NewObject>(), n => n.Constructor.Name == ".ctor");
        function.CheckInvariant();
    }

    [Fact]
    public void InitializeArray_RealSignature_RaisesToArrayLiteral()
    {
        var function = BuildInitializeArrayRaising(RealInitializeArray());

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Single(function.Descendants.OfType<ArrayLiteral>());
        Assert.DoesNotContain(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InitializeArray");
        function.CheckInvariant();
    }

    [Fact]
    public void InitializeArray_WrongFirstParameter_IsNotRaised()
    {
        // #1472: a corelib `RuntimeHelpers.InitializeArray` overload whose first
        // parameter is not `System.Array` (here `System.Object`) is not the real
        // initializer; the RVA fold must decline rather than drop the call.
        var lookalike = new MethodRef(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers"),
            "InitializeArray",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Object"), s_runtimeFieldHandle],
            HasThis: false);
        var function = BuildInitializeArrayRaising(lookalike);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Single(function.Descendants.OfType<NewArray>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InitializeArray");
        function.CheckInvariant();
    }

    [Fact]
    public void InitializeArray_NonVoidReturn_IsNotRaised()
    {
        var lookalike = new MethodRef(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers"),
            "InitializeArray",
            s_int,
            [TypeRef.CoreLib("System", "Array"), s_runtimeFieldHandle],
            HasThis: false);
        var function = BuildInitializeArrayRaising(lookalike);

        new RvaSpanPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<ArrayLiteral>());
        Assert.Contains(function.Descendants.OfType<Call>(), c => c.Callee.Name == "InitializeArray");
        function.CheckInvariant();
    }

    static MethodRef RealInitializeArray()
        => new MethodRef(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers"),
            "InitializeArray",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Array"), s_runtimeFieldHandle],
            HasThis: false);

    static IrFunction BuildInitializeArrayRaising(MethodRef initMethod)
    {
        var intArray = TypeRef.SzArray(s_int);
        var token = new LoadToken(RuntimeTokenKind.Field, null, "<PrivateImplementationDetails>.Blob")
        {
            FieldRvaData = [1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0],
        };
        var block = new Block();
        block.Add(new StoreLocal(0, intArray, new NewArray(s_int, new Constant(3, s_int))));
        block.Add(new ExpressionStatement(new Call(initMethod, isVirtual: false, [new LoadLocal(0, intArray), token])));
        block.Add(new Return(new LoadLocal(0, intArray)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(intArray, [], HasThis: false, GenericParameterCount: 0),
            [intArray],
            body);
    }

    static IrFunction BuildReadOnlySpanByteCtor(byte[] blob, int spanLength)
        => BuildReadOnlySpanCtor(s_byte, s_readOnlySpanByte, blob, spanLength);

    static IrFunction BuildReadOnlySpanIntCtor(byte[] blob, int spanLength)
        => BuildReadOnlySpanCtor(s_int, s_readOnlySpanInt, blob, spanLength);

    static IrFunction BuildReadOnlySpanEnumCtor(TypeRef element, byte[] blob, int spanLength)
        => BuildReadOnlySpanCtor(
            element,
            TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [element]),
            blob,
            spanLength);

    static IrFunction BuildReadOnlySpanCtor(TypeRef element, TypeRef spanType, byte[] blob, int spanLength)
    {
        var ctor = new MethodRef(
            spanType,
            ".ctor",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.ByRef(element), s_int],
            HasThis: true);
        var field = new FieldRef(
            TypeRef.Definition("CoreLib", "", "<PrivateImplementationDetails>"),
            "HASH",
            TypeRef.Definition("CoreLib", "", "__StaticArrayInitTypeSize"));
        var address = new LoadFieldAddress(field, instance: null) { FieldRvaData = blob };
        var newObject = new NewObject(ctor, [address, new Constant(spanLength, s_int)]);

        var block = new Block();
        block.Add(new Return(newObject));
        var body = new BlockContainer();
        body.Add(block);
        var signature = new MethodSignature(s_readOnlySpanByte, [], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.Definition("Synthetic", "", "T"), signature, [], body);
    }

    static IrFunction BuildInitializeArrayWithClobberedLocal()
    {
        var intArray = TypeRef.SzArray(s_int);
        var block = new Block();
        block.Add(new StoreLocal(0, intArray, new NewArray(s_int, new Constant(1, s_int))));
        block.Add(new StoreLocal(0, intArray, new LoadArgument(0, "other", intArray)));
        block.Add(new ExpressionStatement(InitializeArrayCall(new LoadLocal(0, intArray))));
        block.Add(new Return(new LoadLocal(0, intArray)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(intArray, [new Parameter("other", intArray)], HasThis: false, GenericParameterCount: 0),
            [intArray],
            body);
    }

    static IrFunction BuildInitializeArrayWithClobberedStackSlot()
    {
        var intArray = TypeRef.SzArray(s_int);
        var block = new Block();
        block.Add(new StoreStackSlot(0, new NewArray(s_int, new Constant(1, s_int))));
        block.Add(new StoreStackSlot(0, new LoadArgument(0, "other", intArray)));
        block.Add(new ExpressionStatement(InitializeArrayCall(new LoadStackSlot(0, intArray))));
        block.Add(new Return(new LoadStackSlot(0, intArray)));

        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "", "T"),
            new MethodSignature(intArray, [new Parameter("other", intArray)], HasThis: false, GenericParameterCount: 0),
            [],
            body);
    }

    static Call InitializeArrayCall(IrExpression array)
    {
        var initializeArray = new MethodRef(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "RuntimeHelpers"),
            "InitializeArray",
            TypeRef.CoreLib("System", "Void"),
            [TypeRef.CoreLib("System", "Array"), s_runtimeFieldHandle],
            HasThis: false);
        var token = new LoadToken(RuntimeTokenKind.Field, null, "<PrivateImplementationDetails>.Blob")
        {
            FieldRvaData = [1, 0, 0, 0],
        };
        return new Call(initializeArray, isVirtual: false, [array, token]);
    }
}
