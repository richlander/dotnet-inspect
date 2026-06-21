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

    static IrFunction BuildReadOnlySpanByteCtor(byte[] blob, int spanLength)
        => BuildReadOnlySpanCtor(s_byte, s_readOnlySpanByte, blob, spanLength);

    static IrFunction BuildReadOnlySpanIntCtor(byte[] blob, int spanLength)
        => BuildReadOnlySpanCtor(s_int, s_readOnlySpanInt, blob, spanLength);

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
}
