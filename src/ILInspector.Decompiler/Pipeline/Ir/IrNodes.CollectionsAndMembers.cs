using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundArrayCreation,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundArrayCreation / newarr",
    precondition: "result is a single-dimension `T[]` (`SzArray`) of the `newarr` element-type token",
    witness: "array fixtures; corpus compile-back")]
public sealed class NewArray : IrExpression
{
    public NewArray(TypeRef elementType, IrExpression length)
    {
        ElementType = elementType;
        AddChild(length);
    }

    public TypeRef ElementType { get; }
    public IrExpression Length => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.SzArray(ElementType);

    public override string Describe() => $"NewArray {ElementType.ToDisplayString()}[]";
}

/// <summary>
/// <c>localloc</c>: a stack-allocated block of <see cref="Size"/> bytes, the
/// inverse of the compiler's <c>stackalloc byte[n]</c> lowering. localloc
/// allocates raw bytes and yields a pointer, so the faithful element type is
/// <c>byte</c> and the result is <c>byte*</c>.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundStackAllocArrayCreation,
    naming: Inverse.NameProvenance.Native,
    forwardName: "stackalloc byte[n] (pointer form) / localloc",
    precondition: "result is `byte*` — localloc yields a raw byte pointer (the faithful element type is `byte`)",
    witness: "unsafe/stackalloc fixtures; corpus compile-back")]
public sealed class StackAllocate : IrExpression
{
    public StackAllocate(IrExpression size) => AddChild(size);

    public IrExpression Size => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.Pointer(TypeRef.CoreLib("System", "Byte"));

    public override string Describe() => "StackAllocate byte[]";
}

/// <summary>
/// A source-level <c>stackalloc T[n]</c> whose result is a
/// <c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c> (target-typed), raised from the
/// compiler's lowering of <c>Span&lt;T&gt; s = stackalloc T[n]</c> — a
/// <c>localloc</c> of <c>n * sizeof(T)</c> bytes fed to the <c>Span&lt;T&gt;(void*,
/// int)</c> constructor. The lowered ctor shape
/// (<c>new Span&lt;T&gt;(stackalloc byte[...], n)</c>) does not compile: a
/// <c>stackalloc</c> in argument position types as <c>Span&lt;byte&gt;</c>, not
/// <c>void*</c>. The element count is the constructor's <c>length</c> argument; the
/// byte size carried by the original <see cref="StackAllocate"/> is redundant
/// (<c>n * sizeof(T)</c>) and dropped.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundStackAllocArrayCreation,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "stackalloc T[n] (Span form) / localloc + Span ctor",
    precondition: "result is the target-typed `Span<T>`/`ReadOnlySpan<T>` (given at construction); the element count is the constructor's `length` argument",
    witness: "unsafe/stackalloc fixtures; corpus compile-back")]
public sealed class StackAllocArray : IrExpression
{
    readonly TypeRef? _resultType;

    public StackAllocArray(TypeRef elementType, IrExpression count, TypeRef? resultType, IEnumerable<IrExpression>? elements = null)
    {
        ElementType = elementType;
        _resultType = resultType;
        AddChild(count);
        if (elements is not null)
        {
            HasInitializer = true;
            foreach (var e in elements)
                AddChild(e);
        }
    }

    public TypeRef ElementType { get; }
    public IrExpression Count => (IrExpression)Children[0];
    public bool HasInitializer { get; }
    public ReadOnlyMemory<IrNode> Elements => HasInitializer ? Children.Skip(1).ToArray() : default;
    public override TypeRef? ResultType => _resultType;
    public override IEnumerable<TypeRef> DirectTypes => [ElementType];

    public override string Describe() => $"StackAllocArray {ElementType.ToDisplayString()}[]";
}

/// <summary>The raised typeof(T): GetTypeFromHandle over a type token, folded.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundTypeOfOperator,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "typeof(T) (BoundTypeOfOperator) / ldtoken + GetTypeFromHandle",
    precondition: "result is `System.Type` — the folded `Type.GetTypeFromHandle(ldtoken T)` shape",
    witness: "corpus compile-back")]
public sealed class TypeOf : IrExpression
{
    public TypeOf(TypeRef type) => Type = type;

    public TypeRef Type { get; }
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Type");
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"TypeOf {Type.ToDisplayString()}";
}

public enum RuntimeTokenKind { Type, Method, Field }

/// <summary>
/// A constant span literal — <c>new T[] { c0, c1, ... }</c> in a
/// <see cref="System.ReadOnlySpan{T}"/> context — raised from the compiler's
/// <c>RuntimeHelpers.CreateSpan&lt;T&gt;(ldtoken &lt;PrivateImplementationDetails&gt;.field)</c>
/// lowering of a constant array initializer. The element constants are decoded
/// from the field's mapped RVA blob. Its result type is the
/// <c>ReadOnlySpan&lt;T&gt;</c> the CreateSpan call produced, so replacing the
/// call leaves the surrounding expression's type unchanged; the printer spells
/// it as the array literal that the compiler re-lowers to the same blob.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Native,
    forwardName: "new T[] { … } in ReadOnlySpan context / RuntimeHelpers.CreateSpan",
    precondition: "result is the `ReadOnlySpan<T>` `SpanType` the CreateSpan call produced (given); the elements are decoded from the field RVA blob",
    witness: "span-literal fixtures; corpus compile-back")]
public sealed class SpanLiteral : IrExpression
{
    public SpanLiteral(TypeRef elementType, TypeRef spanType, IEnumerable<IrExpression> elements)
    {
        ElementType = elementType;
        SpanType = spanType;
        foreach (var element in elements)
            AddChild(element);
    }

    public TypeRef ElementType { get; }
    public TypeRef SpanType { get; }
    public IReadOnlyList<IrExpression> Elements => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => SpanType;
    public override IEnumerable<TypeRef> DirectTypes => [ElementType, SpanType];

    public override string Describe() => $"SpanLiteral {ElementType.ToDisplayString()}[{Children.Count}]";
}

/// <summary>
/// A C# 12 collection expression — <c>[e0, e1, ...]</c> or
/// <c>[..source, e]</c> — raised from exact compiler collection-expression
/// lowerings. Span targets come from compiler-synthesized inline-array buffers,
/// supported <c>List&lt;T&gt;</c> targets come from PDB-discriminated
/// <c>CollectionsMarshal.SetCount</c>/<c>AsSpan</c> fill patterns, and array
/// spread-with-tail targets come from symbol-confirmed span copy/slice shapes.
/// The result type is the target type the replaced expression or returned
/// temporary produced, so the surrounding context is unchanged when csc
/// re-lowers the collection expression.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundCollectionExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundCollectionExpression ([e0, ..spread, e1])",
    precondition: "result is the target type the replaced expression or returned temporary produced (the surrounding context is unchanged when csc re-lowers); raised only from exact synthesized lowerings — inline-array span buffers, PDB-discriminated `List<T>` SetCount/AsSpan fills, and symbol-confirmed array spread-with-tail copy/slice shapes",
    witness: "CollectionExpressionFrontierTests, corpus compile-back")]
public sealed class CollectionExpression : IrExpression
{
    public CollectionExpression(TypeRef elementType, TypeRef targetType, IEnumerable<IrExpression> elements,
        ImmutableArray<MethodRef> consumedMemberRefs = default)
    {
        ElementType = elementType;
        TargetType = targetType;
        ConsumedMemberRefs = consumedMemberRefs.IsDefault ? [] : consumedMemberRefs;
        foreach (var element in elements)
            AddChild(element);
    }

    public TypeRef ElementType { get; }
    public TypeRef TargetType { get; }
    public ImmutableArray<MethodRef> ConsumedMemberRefs { get; }
    public IReadOnlyList<IrExpression> Elements => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => TargetType;
    public override IEnumerable<TypeRef> DirectTypes => [ElementType, TargetType];

    public override string Describe() => $"CollectionExpression {ElementType.ToDisplayString()}[{Children.Count}]";
}

/// <summary>A spread element inside a <see cref="CollectionExpression"/>: <c>..source</c>.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundCollectionExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundCollectionExpressionSpreadElement (..source)",
    precondition: "result is the spread source's type; appears only as an element of a CollectionExpression",
    witness: "CollectionExpressionFrontierTests, corpus compile-back")]
public sealed class CollectionSpreadElement : IrExpression
{
    public CollectionSpreadElement(IrExpression source) => AddChild(source);

    public IrExpression Source => (IrExpression)Children[0];
    public override TypeRef? ResultType => Source.ResultType;

    public override string Describe() => "CollectionSpreadElement";
}

/// <summary>
/// A constant array creation with an element initializer — <c>new T[] { e0, e1, ... }</c>
/// — raised from the compiler's <c>RuntimeHelpers.InitializeArray</c> lowering: a
/// <c>new T[N]</c> whose elements are bulk-loaded from a <c>&lt;PrivateImplementationDetails&gt;</c>
/// field mapping the raw little-endian element bytes. Left as-is that renders as
/// <c>RuntimeHelpers.InitializeArray(arr, /* LoadToken Field ... */)</c> — the
/// unspellable <c>ldtoken</c> of a compiler-internal field — which never parses.
/// The compiler re-lowers the reconstructed literal to the same content-addressed
/// blob, so the round-trip is opcode-exact.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundArrayCreation,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundArrayCreation + BoundArrayInitialization (new T[] { ... } via RuntimeHelpers.InitializeArray)",
    precondition: "result is the array type; elements are decoded from the `<PrivateImplementationDetails>` blob's raw little-endian bytes at the element width; the compiler re-lowers the literal to the same content-addressed blob, so the round-trip is opcode-exact",
    witness: "InitializeArrayTests, corpus compile-back")]
public sealed class ArrayLiteral : IrExpression
{
    public ArrayLiteral(TypeRef elementType, TypeRef arrayType, IEnumerable<IrExpression> elements,
        MethodRef? initializationMethod = null)
    {
        ElementType = elementType;
        ArrayType = arrayType;
        InitializationMethod = initializationMethod;
        foreach (var element in elements)
            AddChild(element);
    }

    public TypeRef ElementType { get; }
    public TypeRef ArrayType { get; }
    public MethodRef? InitializationMethod { get; }
    public IReadOnlyList<IrExpression> Elements => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => ArrayType;
    public override IEnumerable<TypeRef> DirectTypes => [ElementType, ArrayType];

    public override string Describe() => $"ArrayLiteral {ElementType.ToDisplayString()}[{Children.Count}]";
}

/// <summary>
/// A C# 12 inline-array span conversion — <c>(System.Span&lt;T&gt;)place</c> or
/// <c>(System.ReadOnlySpan&lt;T&gt;)place</c> — raised from the compiler's
/// <c>&lt;PrivateImplementationDetails&gt;.InlineArrayAsSpan&lt;TBuffer, T&gt;(ref place, N)</c>
/// (and the <c>AsReadOnlySpan</c> dual) lowering of the implicit/explicit
/// conversion an <c>[InlineArray(N)]</c> buffer has to a span. Unlike
/// <see cref="CollectionExpression"/> (which recovers a synthesized
/// <c>&lt;&gt;y__InlineArrayN</c> temporary built from element stores), this is a
/// direct conversion of a pre-existing inline-array <em>place</em> — a field,
/// parameter, or array element — to a span, e.g. <c>(Span&lt;uint&gt;)_values</c>.
/// The <see cref="Place"/> is the address node naming the buffer; the printer
/// dereferences it to the lvalue spelling. Its result type is the span the
/// call produced, so replacing the call leaves the surrounding expression's type
/// unchanged and the compiler re-lowers the cast to the same AsSpan call. The
/// angle-bracketed <c>&lt;PrivateImplementationDetails&gt;</c> method name never
/// parses, so leaving the call flat is malformed C#.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundConversion (inline-array → span) / InlineArrayAsSpan",
    precondition: "result is the `Span<T>`/`ReadOnlySpan<T>` `SpanType` the inline-array AsSpan conversion produced (given)",
    witness: "inline-array fixtures; corpus compile-back")]
public sealed class InlineArraySpanConversion : IrExpression
{
    public InlineArraySpanConversion(TypeRef spanType, IrExpression place)
    {
        SpanType = spanType;
        AddChild(place);
    }

    public TypeRef SpanType { get; }
    public IrExpression Place => (IrExpression)Children[0];
    public override TypeRef? ResultType => SpanType;
    public override IEnumerable<TypeRef> DirectTypes => [SpanType];

    public override string Describe() => $"InlineArraySpanConversion {SpanType.ToDisplayString()}";
}

/// <summary>ldtoken: a runtime handle for a type, method, or field (the typeof/ldtoken patterns raise from this).</summary>
[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "ldtoken (type/method/field handle)",
    precondition: "result is the `RuntimeTypeHandle`/`RuntimeMethodHandle`/`RuntimeFieldHandle` selected by the token `Kind`",
    witness: "corpus compile-back")]
public sealed class LoadToken : IrExpression
{
    public LoadToken(RuntimeTokenKind kind, TypeRef? type, string display)
    {
        Kind = kind;
        Type = type;
        Display = display;
    }

    public RuntimeTokenKind Kind { get; }

    /// <summary>The token's type when it is a type token; null for method/field tokens.</summary>
    public TypeRef? Type { get; }
    public string Display { get; }

    /// <summary>
    /// For an <c>ldtoken</c> of a field with mapped RVA data — the
    /// <c>&lt;PrivateImplementationDetails&gt;</c> blob a constant array/span
    /// initializer points at — the raw little-endian bytes. Lets the span-literal
    /// raising reconstruct <c>new T[] { ... }</c> from a
    /// <c>RuntimeHelpers.CreateSpan&lt;T&gt;</c> call. Null for every other token.
    /// </summary>
    public byte[]? FieldRvaData { get; init; }

    public override TypeRef? ResultType => TypeRef.CoreLib("System", Kind switch
    {
        RuntimeTokenKind.Type => "RuntimeTypeHandle",
        RuntimeTokenKind.Method => "RuntimeMethodHandle",
        _ => "RuntimeFieldHandle",
    });
    public override IEnumerable<TypeRef> DirectTypes => Type is null ? [] : [Type];

    public override string Describe() => $"LoadToken {Kind} {Display}";
}

/// <summary>A raised property or indexer read (from a get_ accessor call).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundPropertyAccess,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundPropertyAccess / BoundIndexerAccess (get_ accessor call)",
    precondition: "type is the property or indexer's return type (accessor signature); raised from a get_ accessor call",
    witness: "corpus compile-back")]
public sealed class LoadProperty : IrExpression
{
    public LoadProperty(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments)
    {
        Accessor = accessor;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        foreach (var argument in indexArguments)
            AddChild(argument);
    }

    public MethodRef Accessor { get; }

    /// <summary>Whether the accessor call was virtual; non-virtual cross-type this-receiver access spells base.</summary>
    public bool IsVirtual { get; init; }
    public bool HasInstance { get; }
    public string PropertyName => Accessor.Name["get_".Length..];
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public IReadOnlyList<IrExpression> IndexArguments
        => Children.Skip(HasInstance ? 1 : 0).Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => Accessor.ReturnType;
    public override IEnumerable<TypeRef> DirectTypes
        => Accessor.ParameterTypes.Append(Accessor.DeclaringType).Append(Accessor.ReturnType);

    public override string Describe() => $"LoadProperty {Accessor.DeclaringType.ToDisplayString()}.{PropertyName}";
}

/// <summary>A raised property or indexer write (from a set_ accessor call).</summary>
public sealed class StoreProperty : ScalarStore
{
    public StoreProperty(MethodRef accessor, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, IrExpression value)
    {
        Accessor = accessor;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        foreach (var argument in indexArguments)
            AddChild(argument);
        AddChild(value);
    }

    public MethodRef Accessor { get; }

    /// <summary>Whether the accessor call was virtual; non-virtual cross-type this-receiver access spells base.</summary>
    public bool IsVirtual { get; init; }
    public bool HasInstance { get; }
    public string PropertyName => Accessor.Name["set_".Length..];
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public IReadOnlyList<IrExpression> IndexArguments
        => Children.Skip(HasInstance ? 1 : 0).Take(Children.Count - (HasInstance ? 1 : 0) - 1).Cast<IrExpression>().ToList();
    public override IrExpression Value => (IrExpression)Children[^1];
    public override IEnumerable<TypeRef> DirectTypes
        => Accessor.ParameterTypes.Append(Accessor.DeclaringType);

    public override string Describe() => $"StoreProperty {Accessor.DeclaringType.ToDisplayString()}.{PropertyName}{UpdateDescription}";
}

/// <summary>A raised event subscription or unsubscription (from an add_/remove_ accessor call) — C#'s <c>e += h</c> / <c>e -= h</c>.</summary>
public sealed class EventSubscription : IrNode
{
    public EventSubscription(MethodRef accessor, bool isAdd, IrExpression? instance, IrExpression value)
    {
        Accessor = accessor;
        IsAdd = isAdd;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        AddChild(value);
    }

    public MethodRef Accessor { get; }

    /// <summary>true for add_ (+=); false for remove_ (-=).</summary>
    public bool IsAdd { get; }

    /// <summary>Whether the accessor call was virtual; non-virtual cross-type this-receiver access spells base.</summary>
    public bool IsVirtual { get; init; }
    public bool HasInstance { get; }

    // Both "add_" and "remove_" prefixes are stripped to the event name.
    public string EventName => Accessor.Name[(IsAdd ? "add_".Length : "remove_".Length)..];
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public IrExpression Value => (IrExpression)Children[^1];
    public override IEnumerable<TypeRef> DirectTypes
        => Accessor.ParameterTypes.Append(Accessor.DeclaringType);

    public override string Describe() => $"EventSubscription {Accessor.DeclaringType.ToDisplayString()}.{EventName} {(IsAdd ? "+=" : "-=")}";
}
