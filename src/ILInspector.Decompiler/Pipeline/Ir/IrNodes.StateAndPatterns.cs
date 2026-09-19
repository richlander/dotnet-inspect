using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

public sealed class Throw : IrNode
{
    public Throw(IrExpression value) => AddChild(value);

    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => "Throw";
}

/// <summary>A field read — instance (<c>ldfld</c>) or static (<c>ldsfld</c>).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundFieldAccess,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundFieldAccess / ldfld·ldsfld",
    precondition: "type is the field's declared type (field signature)",
    witness: "corpus compile-back")]
public sealed class LoadField : IrExpression
{
    public LoadField(FieldRef field, IrExpression? instance)
    {
        Field = field;
        if (instance is not null)
            AddChild(instance);
    }

    public FieldRef Field { get; }
    public bool IsVolatile { get; init; }
    internal bool UsesAccessorStorage { get; set; }
    public IrExpression? Instance => Children.Count > 0 ? (IrExpression)Children[0] : null;
    public override TypeRef? ResultType => Field.Type;
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    public override string Describe()
        => $"LoadField {Field.DeclaringType.ToDisplayString()}.{Field.Name} ({Field.Type.ToDisplayString()})";
}

public sealed class StoreField : ScalarStore
{
    public StoreField(FieldRef field, IrExpression? instance, IrExpression value)
    {
        Field = field;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        AddChild(value);
    }

    public FieldRef Field { get; }
    public bool IsVolatile { get; init; }
    public bool HasInstance { get; }
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public override IrExpression Value => (IrExpression)Children[HasInstance ? 1 : 0];
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    public override string Describe() => $"StoreField {Field.DeclaringType.ToDisplayString()}.{Field.Name}{UpdateDescription}";
}

public sealed class Return : IrNode
{
    public Return(IrExpression? value)
    {
        if (value is not null)
            AddChild(value);
    }

    public IrExpression? Value => Children.Count > 0 ? (IrExpression)Children[0] : null;

    public override string Describe() => "Return";
}

/// <summary>
/// <c>yield return value;</c> — one element produced by a C# iterator. Raised by
/// <see cref="IteratorReconstructionPass"/> from a state machine's MoveNext
/// (the <c>&lt;&gt;2__current = value</c> store plus the state advance), it has no
/// IL of its own (the kickoff method only constructs the state machine), so it is
/// a reconstructed-source node, not an importer-native one.
/// </summary>
public sealed class YieldReturn : IrNode
{
    public YieldReturn(IrExpression value) => AddChild(value);

    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => "YieldReturn";
}

/// <summary><c>yield break;</c> — the iterator terminates with no further elements.</summary>
public sealed class YieldBreak : IrNode
{
    public override string Describe() => "YieldBreak";
}

/// <summary>
/// A synthetic variable carrying an evaluation-stack value across a block
/// boundary (ternaries, short-circuit values) or materializing a dup.
/// Edge slots are position/type-indexed so every predecessor of a join stores
/// to the same slot, while unrelated incompatible lifetimes do not collide; dup
/// slots allocate from <see cref="DupSlotBase"/> up.
/// </summary>
public sealed class StoreStackSlot : IrNode
{
    public const int DupSlotBase = 256;

    public StoreStackSlot(int slot, IrExpression value)
    {
        Slot = slot;
        AddChild(value);
    }

    public int Slot { get; }
    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => $"StoreStackSlot S_{Slot}";
}

[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Native,
    forwardName: "(synthesized) evaluation-stack slot",
    precondition: "`Type` is the reconciled type of a spilled evaluation-stack entry when known (the join of the slot's typed loads/stores — the value-typed-emission slot reconciliation), else null; no metadata token backs it. After SlotMaterializationPass, decided in-domain slots are retired to typed locals before print — a slot node surviving to the printer is the counted residual (ambiguous testimony, cross-family ranges, element-store identity recovery, nested scopes)",
    witness: "stack-slot fixtures, SlotMaterializationPassTests; corpus compile-back")]
public sealed class LoadStackSlot : IrExpression
{
    public LoadStackSlot(int slot, TypeRef? type)
    {
        Slot = slot;
        Type = type;
    }

    public int Slot { get; }
    public TypeRef? Type { get; }
    public override TypeRef? ResultType => Type;

    public override string Describe() => $"LoadStackSlot S_{Slot}";
}

[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundArrayLength,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundArrayLength / ldlen",
    precondition: "result is `System.Int32` — the array's `.Length` (`ldlen` pushes a native int; the `.Length` property is `int`)",
    witness: "array fixtures; corpus compile-back")]
public sealed class ArrayLength : IrExpression
{
    public ArrayLength(IrExpression array) => AddChild(array);

    public IrExpression Array => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Int32");

    public override string Describe() => "ArrayLength";
}

/// <summary>
/// A raised C# range expression (<c>start..end</c>), used as the index of a
/// <see cref="SliceExpression"/>. Either endpoint may be omitted, spelling the
/// open forms <c>start..</c>, <c>..end</c>, and <c>..</c>.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundRangeExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundRangeExpression / start..end",
    precondition: "result is `System.Range` (the `start..end` operator; either endpoint may be omitted)",
    witness: "range/index fixtures; corpus compile-back")]
public sealed class RangeExpression : IrExpression
{
    public RangeExpression(IrExpression? start, IrExpression? end)
    {
        HasStart = start is not null;
        HasEnd = end is not null;
        if (start is not null)
            AddChild(start);
        if (end is not null)
            AddChild(end);
    }

    public bool HasStart { get; }
    public bool HasEnd { get; }
    public IrExpression? Start => HasStart ? (IrExpression)Children[0] : null;
    public IrExpression? End => HasEnd ? (IrExpression)Children[HasStart ? 1 : 0] : null;
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Range");

    public override string Describe() => "RangeExpression";
}

/// <summary>A raised C# range-indexer access (<c>receiver[start..end]</c>) — the inverse
/// of the compiler's range-slice lowering (<c>RuntimeHelpers.GetSubArray</c> for
/// arrays). The result type is the slice's type (the receiver's array type), not
/// an element type.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.None,
    naming: Inverse.NameProvenance.Native,
    forwardName: "receiver[start..end] (range-indexer access) / GetSubArray·Slice",
    precondition: "result is the slice's type (the receiver's array/span type, given at construction), not an element type; raised from the range-slice lowering",
    witness: "range/index fixtures; corpus compile-back")]
public sealed class SliceExpression : IrExpression
{
    public SliceExpression(IrExpression receiver, RangeExpression range, TypeRef? resultType,
        MethodRef? sliceMethod = null)
    {
        AddChild(receiver);
        AddChild(range);
        ResultType = resultType;
        SliceMethod = sliceMethod;
    }

    public IrExpression Receiver => (IrExpression)Children[0];
    public RangeExpression Range => (RangeExpression)Children[1];
    public MethodRef? SliceMethod { get; }
    public override TypeRef? ResultType { get; }

    public override string Describe() => "SliceExpression";
}

/// <summary>A raised C# index-from-end operand (<c>^n</c>), used inside array/string element access.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundFromEndIndexExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "^n (BoundFromEndIndexExpression) / new Index(n, fromEnd: true)",
    precondition: "result is `System.Index` (the `^n` from-end operand)",
    witness: "range/index fixtures; corpus compile-back")]
public sealed class IndexFromEnd : IrExpression
{
    public IndexFromEnd(IrExpression offset) => AddChild(offset);

    public IrExpression Offset => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Index");

    public override string Describe() => "IndexFromEnd";
}

/// <summary>The boxing conversion (the <c>box</c> opcode): a value type materialized as a reference.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundConversion (boxing)",
    precondition: "target is the boxed value type",
    witness: "box/unbox fixtures")]
public sealed class Box : IrExpression
{
    public Box(TypeRef type, IrExpression operand)
    {
        Type = type;
        AddChild(operand);
    }

    public TypeRef Type { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Object");
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"Box {Type.ToDisplayString()}";
}

/// <summary>The isinst test producing the cast-or-null value (raising refines to is-patterns or as-casts).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundAsOperator,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "value as T (BoundAsOperator) / isinst",
    precondition: "result is the tested reference type `Type` (the `isinst` token); the value is that type or null",
    witness: "cast fixtures; corpus compile-back")]
public sealed class IsInstance : IrExpression
{
    public IsInstance(TypeRef type, IrExpression operand)
    {
        Type = type;
        AddChild(operand);
    }

    public TypeRef Type { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => Type;
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"IsInstance {Type.ToDisplayString()}";
}

/// <summary>
/// A raised <c>is</c> type pattern with a binding: <c>value is T t</c>. Produced
/// by <see cref="IsPatternPass"/> from csc's type-pattern lowering — a local
/// assigned <c>value as T</c> immediately before a null test that gates a scope
/// using the local as the narrowed <c>T</c>. The null test becomes this
/// expression; <see cref="LocalIndex"/> is the bound pattern variable, declared
/// by the pattern itself (so the printer skips its up-front declaration).
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundIsPatternExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundIsPatternExpression (value is T t)",
    precondition: "result is `System.Boolean`; `LocalIndex` is the pattern-bound narrowed local (declared by the pattern, so its up-front declaration is skipped); raised only when the `value as T` store immediately precedes the null test gating the local's sole use scope",
    witness: "IsPatternPassTests, corpus compile-back")]
public sealed class IsPattern : IrExpression
{
    public IsPattern(IrExpression value, TypeRef type, int localIndex)
    {
        Type = type;
        LocalIndex = localIndex;
        AddChild(value);
    }

    /// <summary>The type the value is tested against — the <c>T</c> in <c>value is T t</c>.</summary>
    public TypeRef Type { get; }

    /// <summary>The local slot bound by the pattern when the test succeeds.</summary>
    public int LocalIndex { get; }

    /// <summary>When folded into a property pattern, keep the local designation because the guarded scope uses it.</summary>
    public bool PreserveLocalInPropertyPattern { get; init; }

    /// <summary>The value being tested.</summary>
    public IrExpression Value => (IrExpression)Children[0];

    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"IsPattern {Type.ToDisplayString()} V_{LocalIndex}";
}

/// <summary>
/// A raised recursive property declaration pattern:
/// <c>value is { Property: T t }</c>. Produced from csc's null-guarded
/// property <c>as</c> store plus bool-slot test when the bound local is used only
/// in the matched branch.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundIsPatternExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundIsPatternExpression (value is { Property: T t })",
    precondition: "result is `System.Boolean`; `Accessor` is the property getter the subpattern reads; raised only from the null-guarded property `as` store plus bool-slot test with the bound local used solely in the matched branch",
    witness: "IsPatternPassTests, IdiomShapeScorecard pattern cases, corpus compile-back")]
public sealed class RecursivePropertyDeclarationPattern : IrExpression
{
    public RecursivePropertyDeclarationPattern(IrExpression value, MethodRef accessor, TypeRef patternType, int localIndex)
    {
        Accessor = accessor;
        PatternType = patternType;
        LocalIndex = localIndex;
        AddChild(value);
    }

    /// <summary>The value being tested.</summary>
    public IrExpression Value => (IrExpression)Children[0];

    /// <summary>The property getter named by the recursive property subpattern.</summary>
    public MethodRef Accessor { get; }

    public string PropertyName => Accessor.Name["get_".Length..];

    /// <summary>The declaration-pattern type <c>T</c>.</summary>
    public TypeRef PatternType { get; }

    /// <summary>The local slot bound by the property declaration pattern.</summary>
    public int LocalIndex { get; }

    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");
    public override IEnumerable<TypeRef> DirectTypes => [Accessor.DeclaringType, Accessor.ReturnType, PatternType];

    public override string Describe() => $"RecursivePropertyDeclarationPattern {PropertyName}: {PatternType.ToDisplayString()} V_{LocalIndex}";
}

/// <summary>
/// A raised single-element list pattern over a string array:
/// <c>value is ["a" or "b"]</c>. Produced by <see cref="ListPatternPass"/> from
/// csc's null/length/element-temp/equality-chain lowering when the element temp
/// and bool result slot are compiler-generated and do not escape.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundIsPatternExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundIsPatternExpression (value is [\"a\" or \"b\"])",
    precondition: "result is `System.Boolean`; raised from csc's null/length/element-temp/equality-chain lowering over a string array, only when the element temp and bool result slot are compiler-generated and do not escape; alternatives are the element-zero constants",
    witness: "ListPatternPassTests, corpus compile-back")]
public sealed class SingleElementListPattern : IrExpression
{
    public SingleElementListPattern(IrExpression value, IReadOnlyList<Constant> alternatives)
    {
        AddChild(value);
        foreach (var alternative in alternatives)
            AddChild(alternative);
    }

    /// <summary>The list-pattern input expression.</summary>
    public IrExpression Value => (IrExpression)Children[0];

    /// <summary>The constant alternatives for element zero.</summary>
    public IReadOnlyList<Constant> Alternatives => Children.Skip(1).Cast<Constant>().ToList();

    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");

    public override string Describe() => $"SingleElementListPattern ({Alternatives.Count} alternatives)";
}

public readonly record struct PositionalPatternSubpattern(ComparisonKind Kind);

/// <summary>
/// A raised C# positional pattern over a deconstructable receiver:
/// <c>value is ("ok", &gt; 0)</c>. The first slice stores constant or relational
/// sub-patterns produced from csc's null/deconstruct/guard lowering.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundIsPatternExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundIsPatternExpression (value is (\"ok\", > 0))",
    precondition: "result is `System.Boolean`; raised from csc's null/deconstruct/guard lowering over a deconstructable receiver; element subpatterns are constant or relational, with one constant per subpattern",
    witness: "IdiomShapeScorecard pattern cases, corpus compile-back")]
public sealed class PositionalPattern : IrExpression
{
    public PositionalPattern(
        IrExpression value,
        IReadOnlyList<PositionalPatternSubpattern> subpatterns,
        IReadOnlyList<Constant> constants,
        MethodRef? consumedDeconstructMethod = null)
    {
        if (subpatterns.Count != constants.Count)
            throw new ArgumentException("A positional pattern needs one constant per sub-pattern.", nameof(constants));

        Subpatterns = [.. subpatterns];
        ConsumedDeconstructMethod = consumedDeconstructMethod;
        AddChild(value);
        foreach (var constant in constants)
            AddChild(constant);
    }

    /// <summary>The positional-pattern input expression.</summary>
    public IrExpression Value => (IrExpression)Children[0];

    /// <summary>The element sub-pattern kinds, parallel to <see cref="Constants"/>.</summary>
    public ImmutableArray<PositionalPatternSubpattern> Subpatterns { get; }

    public MethodRef? ConsumedDeconstructMethod { get; }

    /// <summary>The constants used by equality or relational element sub-patterns.</summary>
    public IReadOnlyList<Constant> Constants => Children.Skip(1).Cast<Constant>().ToList();

    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");

    public override string Describe() => $"PositionalPattern ({Subpatterns.Length} elements)";
}

/// <summary>The <c>castclass</c> reference type-check cast (<c>(T)x</c> for a reference type <c>T</c>).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundConversion (reference cast)",
    precondition: "target is the reference type the cast checks",
    witness: "cast fixtures; corpus compile-back")]
public sealed class CastClass : IrExpression
{
    public CastClass(TypeRef type, IrExpression operand)
    {
        Type = type;
        AddChild(operand);
    }

    public TypeRef Type { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => Type;
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"CastClass {Type.ToDisplayString()}";
}
