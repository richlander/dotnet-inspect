using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>The exception value the CLR pushes on entry to a catch or filter handler.</summary>
[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Native,
    forwardName: "the caught exception in a catch clause / handler-entry stack value",
    precondition: "result is the catch region's declared exception type (`Type`), or `System.Object` in filters/untyped contexts — the exception value at handler entry",
    witness: "exception-handling fixtures; corpus compile-back")]
public sealed class CaughtException : IrExpression
{
    public CaughtException(TypeRef? type) => Type = type;

    /// <summary>The region's catch type; null in filters and untyped contexts (object stands in).</summary>
    public TypeRef? Type { get; }
    public override TypeRef? ResultType => Type ?? TypeRef.CoreLib("System", "Object");
    public override IEnumerable<TypeRef> DirectTypes => Type is null ? [] : [Type];

    public override string Describe() => $"CaughtException ({ResultType!.ToDisplayString()})";
}

/// <summary>leave: exits one or more protected regions toward the target, running finallies; the evaluation stack empties.</summary>
public sealed class Leave : IrNode
{
    public Leave(int targetOffset) => TargetOffset = targetOffset;

    public int TargetOffset { get; }

    public override string Describe() => $"Leave IL_{TargetOffset:X4}";
}

/// <summary>endfinally / endfault: returns control from the handler to the EH machinery.</summary>
public sealed class EndFinally : IrNode
{
    public override string Describe() => "EndFinally";
}

/// <summary>endfilter: yields the filter's verdict (nonzero = handle).</summary>
public sealed class EndFilter : IrNode
{
    public EndFilter(IrExpression value) => AddChild(value);

    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => "EndFilter";
}

/// <summary>The address of a local — the receiver form for value-type calls and ref/out arguments.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundLocal,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundLocal (by ref) / ldloca",
    precondition: "result is a managed reference (`ByRef`) to the local's declared type — the receiver form for a value-type instance call or a `ref`/`out`/`in` argument",
    witness: "corpus compile-back")]
public sealed class LoadLocalAddress : IrExpression
{
    public LoadLocalAddress(int index, TypeRef type)
    {
        Index = index;
        Type = type;
    }

    public int Index { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => TypeRef.ByRef(Type);

    public override string Describe() => $"LoadLocalAddress {Index} ({Type.ToDisplayString()})";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundParameter,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundParameter (by ref) / ldarga",
    precondition: "result is a managed reference (`ByRef`) to the argument's declared type (parameter signature; declaring type for `this`)",
    witness: "corpus compile-back")]
public sealed class LoadArgumentAddress : IrExpression
{
    readonly string _name = "";

    public LoadArgumentAddress(int index, string name, TypeRef type)
    {
        Index = index;
        _name = name;
        Type = type;
    }

    internal LoadArgumentAddress(
        int index,
        string name,
        TypeRef type,
        Parameter? parameter)
        : this(index, name, type)
    {
        Parameter = parameter;
    }

    public LoadArgumentAddress(int index, Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        Index = index;
        _name = parameter.Name;
        Parameter = parameter;
        Type = parameter.Type;
    }

    public int Index { get; }
    internal Parameter? Parameter { get; }
    public string Name => Parameter?.DisplayName ?? _name;
    public TypeRef Type { get; }
    public override TypeRef? ResultType => TypeRef.ByRef(Type);

    public override string Describe() => $"LoadArgumentAddress {Index} ({Type.ToDisplayString()} {Name})";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundFieldAccess,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundFieldAccess (by ref) / ldflda·ldsflda",
    precondition: "result is a managed reference (`ByRef`) to the field's declared type (field signature)",
    witness: "corpus compile-back")]
public sealed class LoadFieldAddress : IrExpression
{
    public LoadFieldAddress(FieldRef field, IrExpression? instance)
    {
        Field = field;
        if (instance is not null)
            AddChild(instance);
    }

    public FieldRef Field { get; }
    public IrExpression? Instance => Children.Count > 0 ? (IrExpression)Children[0] : null;
    public override TypeRef? ResultType => TypeRef.ByRef(Field.Type);
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    /// <summary>
    /// For a <c>ldsflda</c> of a field with mapped RVA data — the
    /// <c>&lt;PrivateImplementationDetails&gt;</c> blob a constant
    /// <c>ReadOnlySpan&lt;byte&gt;</c> initializer points at (csc's 1-byte-element
    /// optimization constructs the span as <c>new ReadOnlySpan&lt;byte&gt;(ref
    /// field, length)</c>) — the raw little-endian bytes. Lets the span-literal
    /// raising reconstruct <c>new byte[] { ... }</c>. Null for every other field.
    /// </summary>
    public byte[]? FieldRvaData { get; init; }

    public override string Describe() => $"LoadFieldAddress {Field.DeclaringType.ToDisplayString()}.{Field.Name}";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundArrayAccess,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundArrayAccess (by ref) / ldelema",
    precondition: "result is a managed reference (`ByRef`) to the array's element type (the `ldelema` type token); `IsReadOnly` marks the `readonly.` prefix (a read-only address)",
    witness: "corpus compile-back")]
public sealed class LoadElementAddress : IrExpression
{
    public LoadElementAddress(TypeRef elementType, IrExpression array, IrExpression index, bool isReadOnly)
    {
        ElementType = elementType;
        IsReadOnly = isReadOnly;
        AddChild(array);
        AddChild(index);
    }

    public TypeRef ElementType { get; }
    /// <summary>The readonly. prefix: no type check, address usable only for reads.</summary>
    public bool IsReadOnly { get; }
    public IrExpression Array => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public override TypeRef? ResultType => TypeRef.ByRef(ElementType);
    public override IEnumerable<TypeRef> DirectTypes => [ElementType];

    public override string Describe() => $"LoadElementAddress {ElementType.ToDisplayString()}{(IsReadOnly ? " readonly" : "")}";
}

/// <summary>
/// Source fixed-buffer element place (<c>buffer[index]</c>) recovered from the
/// compiler-generated backing field's <c>FixedElementField</c> address plus a
/// proven element-scaled offset.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundArrayAccess,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "fixed-buffer element access / FixedElementField address plus scaled offset",
    precondition: "source field carries FixedBufferAttribute; generated element field identity, element type, receiver, and offset scale all match the fixed-buffer metadata",
    witness: "new/legacy unsafe fixed-buffer fixtures and close negative tests")]
public sealed class FixedBufferElementAddress : IrExpression
{
    public FixedBufferElementAddress(FieldRef bufferField, TypeRef elementType, IrExpression instance, IrExpression index)
    {
        BufferField = bufferField;
        ElementType = elementType;
        AddChild(instance);
        AddChild(index);
    }

    public FieldRef BufferField { get; }
    public TypeRef ElementType { get; }
    public IrExpression Instance => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public override TypeRef? ResultType => TypeRef.ByRef(ElementType);
    public override IEnumerable<TypeRef> DirectTypes => [ElementType];

    public override string Describe()
        => $"FixedBufferElementAddress {BufferField.DeclaringType.ToDisplayString()}.{BufferField.Name}[...]";
}

/// <summary>Load through an address (ldobj and the ldind.* family). A null type means the opcode does not encode one (ldind.ref).</summary>
[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "ByRef/pointer dereference / ldobj·ldind.*",
    precondition: "result is the opcode-encoded type (the `ldobj` token or the `ldind.*` element type); a `bool`/`char` location is recovered from the address's `ByRef`/pointer pointee (the `ldind.u1`/`ldind.u2` storage width is shared by `bool`/`byte` and `char`/`ushort`); `ldind.ref` encodes no type and takes the pointee",
    witness: "unsafe/indirect fixtures; corpus compile-back")]
public sealed class LoadIndirect : IrExpression
{
    public LoadIndirect(TypeRef? type, IrExpression address)
    {
        Type = type;
        AddChild(address);
    }

    public TypeRef? Type { get; }
    public bool IsVolatile { get; init; }
    public IrExpression Address => (IrExpression)Children[0];
    public override TypeRef? ResultType
    {
        get
        {
            var pointee = Address.ResultType is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer } indirect
                ? indirect.ElementType
                : null;
            // ldind.u1/ldind.u2 carry only a storage width (byte/ushort), shared by
            // bool/byte and char/ushort. A bool/char location read through a ref or
            // pointer is really that type — without it `*pBool == 0` types as
            // `byte == int`, never recovering the bool constant (CS0019). Prefer the
            // pointee for those two; otherwise the opcode type is authoritative.
            if (pointee is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Boolean" or "Char" })
                return pointee;
            return Type ?? pointee;
        }
    }
    public override IEnumerable<TypeRef> DirectTypes => Type is null ? [] : [Type];

    public override string Describe() => $"LoadIndirect {ResultType?.ToDisplayString() ?? "?"}{(IsVolatile ? " volatile" : "")}";
}

public sealed class StoreIndirect : ScalarStore
{
    public StoreIndirect(TypeRef? type, IrExpression address, IrExpression value)
    {
        Type = type;
        AddChild(address);
        AddChild(value);
    }

    public TypeRef? Type { get; }
    public bool IsVolatile { get; init; }
    public IrExpression Address => (IrExpression)Children[0];
    public override IrExpression Value => (IrExpression)Children[1];
    public override IEnumerable<TypeRef> DirectTypes => Type is null ? [] : [Type];

    public override string Describe() => $"StoreIndirect {Type?.ToDisplayString() ?? "?"}{(IsVolatile ? " volatile" : "")}{UpdateDescription}";
}

/// <summary>
/// An unchecked integer pointer-element update. Evaluate pointer and index once,
/// read the selected location, evaluate Value, then write the result to that location.
/// </summary>
public sealed class PointerElementCompoundAssignment : IrNode
{
    public PointerElementCompoundAssignment(
        TypeRef elementType, BinaryKind operation, IrExpression pointer, IrExpression index, IrExpression value)
    {
        ElementType = elementType;
        Operation = operation;
        AddChild(pointer);
        AddChild(index);
        AddChild(value);
    }

    public TypeRef ElementType { get; }
    public BinaryKind Operation { get; }
    public IrExpression Pointer => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public IrExpression Value => (IrExpression)Children[2];
    public override IEnumerable<TypeRef> DirectTypes => [ElementType];

    public override string Describe() => $"PointerElementCompoundAssignment {ElementType.ToDisplayString()} {Operation}";
}

public enum PointerUpdateKind { Add, Subtract, Increment, Decrement }

/// <summary>A decided pointer-place read, element displacement, and write, in that evaluation order.</summary>
public sealed class PointerCompoundAssignment : IrNode
{
    public PointerCompoundAssignment(
        TypeRef pointerType, PointerUpdateKind kind, bool isChecked,
        IrExpression target, IrExpression index, MethodRef? setter = null)
    {
        PointerType = pointerType;
        Kind = kind;
        IsChecked = isChecked;
        Setter = setter;
        AddChild(target);
        AddChild(index);
    }

    public TypeRef PointerType { get; }
    public PointerUpdateKind Kind { get; }
    public bool IsChecked { get; }
    public MethodRef? Setter { get; }
    public IrExpression Target => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public override IEnumerable<TypeRef> DirectTypes => [PointerType];
    public override string Describe() => $"PointerCompoundAssignment {Kind}{(IsChecked ? " checked" : "")}";
}

/// <summary>initobj: default-initialize the storage at an address.</summary>
public sealed class CopyBlock : IrNode
{
    public CopyBlock(IrExpression destination, IrExpression source, IrExpression size)
    {
        AddChild(destination);
        AddChild(source);
        AddChild(size);
    }

    public IrExpression Destination => (IrExpression)Children[0];
    public IrExpression Source => (IrExpression)Children[1];
    public IrExpression Size => (IrExpression)Children[2];
    public bool IsVolatile { get; init; }

    public override string Describe() => "CopyBlock";
}

public sealed class InitObject : IrNode
{
    public InitObject(TypeRef type, IrExpression address)
    {
        Type = type;
        AddChild(address);
    }

    public TypeRef Type { get; }
    public IrExpression Address => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"InitObject {Type.ToDisplayString()}";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundArrayAccess,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundArrayAccess / ldelem",
    precondition: "result is the element type: the `ldelem` type token, the fixed type of a typed `ldelem.*` opcode (`ldelem.i4` → `int`, `ldelem.r8` → `double`, …), or — for `ldelem.ref`, which encodes no type — the array operand's element type",
    witness: "array fixtures; corpus compile-back")]
public sealed class LoadElement : IrExpression
{
    public LoadElement(TypeRef? elementType, IrExpression array, IrExpression index)
    {
        ElementType = elementType;
        AddChild(array);
        AddChild(index);
    }

    /// <summary>Null when the opcode does not encode one (ldelem.ref); the array's element type stands in.</summary>
    public TypeRef? ElementType { get; }
    public MetadataFactState ResultIsDynamic { get; internal set; } = MetadataFactState.Unknown;
    public IrExpression Array => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public override TypeRef? ResultType
        => ElementType ?? (Array.ResultType is { Kind: TypeRefKind.SzArray } array ? array.ElementType : null);
    public override IEnumerable<TypeRef> DirectTypes => ElementType is null ? [] : [ElementType];

    public override string Describe() => $"LoadElement {ResultType?.ToDisplayString() ?? "?"}";
}

public sealed class StoreElement : IrNode
{
    public StoreElement(TypeRef? elementType, IrExpression array, IrExpression index, IrExpression value)
    {
        ElementType = elementType;
        AddChild(array);
        AddChild(index);
        AddChild(value);
    }

    public TypeRef? ElementType { get; }
    /// <summary>
    /// Preserves the former statement-level unsafe context when the receiver
    /// value moved here from an adjacent local store.
    /// </summary>
    internal bool ReceiverTempInlined { get; set; }
    public IrExpression Array => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public IrExpression Value => (IrExpression)Children[2];
    public override IEnumerable<TypeRef> DirectTypes => ElementType is null ? [] : [ElementType];

    public override string Describe() => $"StoreElement {ElementType?.ToDisplayString() ?? "?"}";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundSizeOfOperator,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "sizeof(T) (BoundSizeOfOperator) / sizeof",
    precondition: "result is `System.Int32` — the `sizeof(T)` byte size",
    witness: "corpus compile-back")]
public sealed class SizeOf : IrExpression
{
    public SizeOf(TypeRef type) => Type = type;

    public TypeRef Type { get; }
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Int32");
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"SizeOf {Type.ToDisplayString()}";
}

/// <summary>
/// <c>default(T)</c>: the zero value of a type. csc lowers <c>default(T)</c> for
/// an unconstrained or struct type parameter to a fresh temporary zero-inited by
/// <c>initobj</c> and then loaded; the default-recovery pass folds that
/// single-assignment temp back to this leaf so the value can inline into its use
/// site as <c>default(T)</c> (reference-typed defaults are already <c>null</c>
/// constants).
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundDefaultExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundDefaultExpression / default(T)",
    precondition: "result is the default-expression's type — the `initobj` type token of the recovered zero-initialized temporary",
    witness: "generic default fixtures; corpus compile-back")]
public sealed class DefaultValue : IrExpression
{
    public DefaultValue(TypeRef type) => Type = type;

    public TypeRef Type { get; }
    public override TypeRef? ResultType => Type;
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"DefaultValue {Type.ToDisplayString()}";
}

/// <summary>The switch opcode: jump to Targets[value], else fall through.</summary>
public sealed class SwitchBranch : IrNode
{
    public SwitchBranch(IrExpression value, ImmutableArray<int> targetOffsets)
    {
        TargetOffsets = targetOffsets;
        AddChild(value);
    }

    public IrExpression Value => (IrExpression)Children[0];
    public ImmutableArray<int> TargetOffsets { get; }

    public override string Describe()
        => $"SwitchBranch [{string.Join(", ", TargetOffsets.Select(t => $"IL_{t:X4}"))}]";
}

/// <summary>unbox: a managed pointer into the box (distinct from unbox.any, which loads the value).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundConversion (unboxing → managed pointer)",
    precondition: "operand is a box of the value type; result is a managed pointer into it",
    witness: "box/unbox fixtures; corpus compile-back")]
public sealed class Unbox : IrExpression
{
    public Unbox(TypeRef type, IrExpression operand)
    {
        Type = type;
        AddChild(operand);
    }

    public TypeRef Type { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.ByRef(Type);
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"Unbox {Type.ToDisplayString()}";
}

/// <summary>unbox.any: loads the unboxed value (<c>(T)boxed</c> for a value type <c>T</c>).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundConversion (unboxing)",
    precondition: "target is the unbox.any type token (value type, reference type, or type parameter)",
    witness: "box/unbox fixtures; corpus compile-back")]
public sealed class UnboxAny : IrExpression
{
    public UnboxAny(TypeRef type, IrExpression operand)
    {
        Type = type;
        AddChild(operand);
    }

    public TypeRef Type { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => Type;
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"UnboxAny {Type.ToDisplayString()}";
}

/// <summary>
/// IL the pipeline does not (yet) represent — kept explicit in the tree and
/// rendered honestly, never forced into plausible output. Any occurrence
/// caps the function's fidelity at <see cref="DecompilationFidelity.Partial"/>.
/// </summary>
[Inverse.NotInverted("unrepresented IL kept explicit and rendered honestly — outside the inverse's checkable domain by construction; any occurrence caps fidelity at Partial")]
public sealed class UnsupportedNode : IrExpression
{
    public UnsupportedNode(int ilOffset, string opcode, string reason)
    {
        ILOffset = ilOffset;
        Opcode = opcode;
        Reason = reason;
    }

    public int ILOffset { get; }
    public string Opcode { get; }
    public string Reason { get; }
    public override TypeRef? ResultType => null;

    public override string Describe() => $"Unsupported IL_{ILOffset:X4} {Opcode}: {Reason}";
}

/// <summary>The raised dynamic call site: `((dynamic)receiver).Member`.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundDynamicMemberAccess,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "((dynamic)receiver).Member (BoundDynamicMemberAccess)",
    precondition: "the compiler-emitted dynamic call site cache block is canonical",
    witness: "corpus compile-back")]
public sealed class DynamicGetMember : IrExpression
{
    public DynamicGetMember(IrExpression receiver, string propertyName)
    {
        PropertyName = propertyName;
        AddChild(receiver);
    }
    public string PropertyName { get; }
    public IrExpression Receiver => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Object");
    public override string Describe() => $"dynamic-get {PropertyName}";
}
