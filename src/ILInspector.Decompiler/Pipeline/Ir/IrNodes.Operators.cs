using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

public enum ComparisonKind { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }

/// <summary>
/// An integer/float comparison (<c>ceq</c>/<c>clt</c>/<c>cgt</c> family), producing a boolean.
/// Native name: Roslyn has no distinct comparison node (it is <c>BoundBinaryOperator</c> with a
/// comparison kind) and IL uses <c>ceq</c>/<c>clt</c>/<c>cgt</c> — the decompiler groups them into
/// one boolean-yielding node.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundBinaryOperator,
    naming: Inverse.NameProvenance.Native,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundBinaryOperator (comparison) / ceq·clt·cgt",
    precondition: "result is bool (the ceq/clt/cgt integer 0/1 result)",
    witness: "corpus compile-back")]
public sealed class Comparison : IrExpression
{
    public Comparison(ComparisonKind kind, bool isUnsigned, IrExpression left, IrExpression right)
    {
        Kind = kind;
        IsUnsigned = isUnsigned;
        AddChild(left);
        AddChild(right);
    }

    public ComparisonKind Kind { get; }
    public bool IsUnsigned { get; }
    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");

    public override string Describe() => $"Comparison.{Kind}{(IsUnsigned ? " unsigned" : "")}";
}

/// <summary>Logical negation of a truth-valued operand (the brfalse lowering; raising passes refine to comparisons).</summary>
public enum LogicalKind { And, Or }

/// <summary>
/// Short-circuit boolean composition (&amp;&amp;/||) — distinct from the
/// bitwise <see cref="Binary"/> forms. Raised by boolean folding from
/// guard-return chains and nested guards; IL has no direct encoding.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundBinaryOperator,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundBinaryOperator (logical AND / OR)",
    precondition: "result is bool; raised from short-circuit branch patterns (IL has no logical-and/or encoding)",
    witness: "corpus compile-back")]
public sealed class LogicalBinary : IrExpression
{
    public LogicalBinary(LogicalKind kind, IrExpression left, IrExpression right)
    {
        Kind = kind;
        AddChild(left);
        AddChild(right);
    }

    public LogicalKind Kind { get; }
    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");

    public override string Describe() => $"Logical{Kind}";
}

/// <summary>The raised null-coalescing operator: left when non-null, else right.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundNullCoalescingOperator,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundNullCoalescingOperator (a ?? b)",
    precondition: "result is the left operand's `Nullable<T>` value type when the raise unwrapped a lifted operand, else the left (falling back to right) operand type — metadata-structural, no stack widening involved",
    witness: "NullConditionalCoalescePassTests, corpus compile-back")]
public sealed class Coalesce : IrExpression, IPrimitiveJoin
{
    TypeRef? _assignmentType;

    public Coalesce(IrExpression left, IrExpression right)
    {
        AddChild(left);
        AddChild(right);
        BindAssignmentType(ImmutableDictionary<TypeRef, TypeShape>.Empty);
        ((IPrimitiveJoin)this).BindInitialPrimitiveTargets();
    }

    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];
    public override TypeRef? ResultType => NullableValueCoalesceResult(Left.ResultType, Right.ResultType) ?? Left.ResultType ?? Right.ResultType;
    public override TypeRef? AssignmentType => _assignmentType;

    internal void BindAssignmentType(IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => _assignmentType = CoercionRendering.CoalesceAssignmentType(this, shapes);

    IReadOnlyList<IrExpression> IPrimitiveJoin.CompatibilityArms => [Left, Right];
    IReadOnlyList<IrExpression> IPrimitiveJoin.RenderedArms => [Right];
    PrimitiveJoinTargetCompatibility IPrimitiveJoin.PrimitiveTargets { get; set; } =
        PrimitiveJoinTargetCompatibility.Empty;

    public override string Describe() => "Coalesce";

    static TypeRef? NullableValueCoalesceResult(TypeRef? left, TypeRef? right)
        => left is
        {
            Kind: TypeRefKind.GenericInstance,
            ElementType: { Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "Nullable`1" },
            TypeArguments: [var value],
        } && right?.Equals(value) == true
            ? value
            : null;
}

/// <summary>
/// A raised local-variable null-coalescing assignment (<c>V ??= fallback</c>).
/// Produced from csc's local null-test diamond:
/// <c>if (V is null) V = fallback;</c>.
/// </summary>
public sealed class NullCoalescingAssignment : IrNode
{
    public NullCoalescingAssignment(int localIndex, TypeRef localType, IrExpression value)
    {
        LocalIndex = localIndex;
        LocalType = localType;
        AddChild(value);
    }

    public int LocalIndex { get; }
    public TypeRef LocalType { get; }
    public IrExpression Value => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => [LocalType];

    public override string Describe() => $"NullCoalescingAssignment V_{LocalIndex}";
}

/// <summary>
/// A raised field null-coalescing assignment (<c>obj.field ??= fallback</c>, or
/// <c>Type.field ??= fallback</c> for a static field). Produced from csc's field
/// null-test diamond: <c>if (obj.field is null) obj.field = fallback;</c>. The
/// receiver — when present — is re-evaluable (a local/argument/this), so folding
/// the two loads into one <c>??=</c> reorders nothing.
/// </summary>
public sealed class NullCoalescingFieldAssignment : IrNode
{
    public NullCoalescingFieldAssignment(FieldRef field, IrExpression? instance, IrExpression value)
    {
        Field = field;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        AddChild(value);
    }

    public FieldRef Field { get; }
    public bool HasInstance { get; }
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public IrExpression Value => (IrExpression)Children[HasInstance ? 1 : 0];
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    public override string Describe()
        => $"NullCoalescingFieldAssignment {Field.DeclaringType.ToDisplayString()}.{Field.Name}";
}

/// <summary>
/// A field null-coalescing assignment used as an expression (<c>obj.field ??= fallback</c>).
/// Produced from csc's lazy field-initializing getter lowering, where the
/// assignment result is returned through a compiler result slot.
/// </summary>
public sealed class NullCoalescingFieldAssignmentExpression : IrExpression
{
    public NullCoalescingFieldAssignmentExpression(FieldRef field, IrExpression? instance, IrExpression value)
    {
        Field = field;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        AddChild(value);
    }

    public FieldRef Field { get; }
    public bool HasInstance { get; }
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public IrExpression Value => (IrExpression)Children[HasInstance ? 1 : 0];
    public override TypeRef? ResultType => Field.Type;
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    public override string Describe()
        => $"NullCoalescingFieldAssignmentExpression {Field.DeclaringType.ToDisplayString()}.{Field.Name}";
}

/// <summary>
/// A raised property null-coalescing assignment (<c>obj.Prop ??= fallback</c>, or
/// <c>Type.Prop ??= fallback</c> for a static property), or an indexer form
/// (<c>d[k] ??= fallback</c>). Produced from csc's property null-test diamond:
/// <c>if (obj.Prop is null) obj.Prop = fallback;</c>, where the getter and setter
/// are paired as one property and the receiver — when present — together with any
/// index arguments are re-evaluable (a local/argument/this, or a constant index),
/// so collapsing the two accessor calls into one <c>??=</c> reorders nothing.
/// </summary>
public sealed class NullCoalescingPropertyAssignment : IrNode
{
    public NullCoalescingPropertyAssignment(MethodRef setter, IrExpression? instance, IReadOnlyList<IrExpression> indexArguments, IrExpression value, bool isVirtual)
    {
        Setter = setter;
        IsVirtual = isVirtual;
        HasInstance = instance is not null;
        if (instance is not null)
            AddChild(instance);
        foreach (var argument in indexArguments)
            AddChild(argument);
        AddChild(value);
    }

    public MethodRef Setter { get; }
    public bool IsVirtual { get; }
    public bool HasInstance { get; }
    public string PropertyName => Setter.Name["set_".Length..];

    /// <summary>The property's type — the setter's value parameter.</summary>
    public TypeRef PropertyType => Setter.ParameterTypes[^1];
    public IrExpression? Instance => HasInstance ? (IrExpression)Children[0] : null;
    public IReadOnlyList<IrExpression> IndexArguments
        => Children.Skip(HasInstance ? 1 : 0).Take(Children.Count - (HasInstance ? 1 : 0) - 1).Cast<IrExpression>().ToList();
    public IrExpression Value => (IrExpression)Children[^1];
    public override IEnumerable<TypeRef> DirectTypes => Setter.ParameterTypes.Append(Setter.DeclaringType);

    public override string Describe()
        => $"NullCoalescingPropertyAssignment {Setter.DeclaringType.ToDisplayString()}.{PropertyName}";
}

/// <summary>
/// A raised null-conditional member access — <c>target?.Member</c>. The single
/// child is the member access (a <see cref="Call"/>, <see cref="LoadProperty"/>,
/// or <see cref="LoadField"/>) whose receiver IS the <c>?.</c> target; the
/// printer prints that receiver, then <c>?</c>, then the member suffix. The
/// raising passes recover either the expression-valued
/// <c>recv is not null ? recv.M : null</c> shape or csc's void-call
/// <c>dup; brtrue call; pop</c> shape. The member carries its own result type;
/// surrounding nodes carry any nullable wrapping or coalesce.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConditionalAccess,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundConditionalAccess / ?.",
    precondition: "result is the member's unwrapped type (`Member.ResultType`); raised from a fully owned ?. null-check pattern (surrounding nodes carry any nullable wrapping / coalesce)",
    witness: "corpus compile-back")]
public sealed class NullConditional : IrExpression
{
    public NullConditional(IrExpression member) => AddChild(member);

    /// <summary>The member access whose receiver is the <c>?.</c> target.</summary>
    public IrExpression Member => (IrExpression)Children[0];

    public override TypeRef? ResultType => Member.ResultType;

    public override string Describe() => "NullConditional";
}

/// <summary>A raised ternary: condition selects between two values (the slot-diamond shape).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConditionalOperator,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundConditionalOperator (c ? t : f)",
    precondition: "result is `MergedType` — the importer's join of the arm types (nominal-exact for integer/enum joins; a common supertype for references) — when set, else the arms' type; arms of disagreeing width or family never join silently (slot testimony vetoes the merge)",
    witness: "SlotStoreDiamondPassTests, DiamondArmTypeReconciliationTests, corpus compile-back")]
public sealed class Conditional : IrExpression, IPrimitiveJoin
{
    TypeRef? _mergedType;

    public Conditional(IrExpression condition, IrExpression whenTrue, IrExpression whenFalse)
    {
        AddChild(condition);
        AddChild(whenTrue);
        AddChild(whenFalse);
        BindReferenceAssignments(ImmutableDictionary<TypeRef, TypeShape>.Empty);
        ((IPrimitiveJoin)this).BindInitialPrimitiveTargets();
    }

    public IrExpression Condition => (IrExpression)Children[0];
    public IrExpression WhenTrue => (IrExpression)Children[1];
    public IrExpression WhenFalse => (IrExpression)Children[2];

    internal ReferenceAssignmentTargets ReferenceAssignments { get; private set; }

    public bool CanAssignReferenceArmsTo(
        TypeRef target, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => ReferenceAssignments.Contains(target, shapes);

    internal void BindReferenceAssignments(IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => ReferenceAssignments = ReferenceAssignmentTargets.ForArms(this, shapes);

    /// <summary>
    /// The merged slot type the importer computed for the join the two arms
    /// feed (a genuine common supertype of both arms). When set it is the
    /// honest result type — the bare <c>WhenTrue ?? WhenFalse</c> fallback
    /// would otherwise lie whenever the arms carry unequal reference types
    /// (e.g. <c>cond ? new DirectoryInfo() : new FileInfo()</c> must type as
    /// <c>FileSystemInfo</c>, not <c>DirectoryInfo</c>).
    /// </summary>
    public TypeRef? MergedType
    {
        get => _mergedType;
        set
        {
            _mergedType = value;
            ((IPrimitiveJoin)this).BindInitialPrimitiveTargets();
        }
    }

    public override TypeRef? ResultType => MergedType ?? WhenTrue.ResultType ?? WhenFalse.ResultType;

    IReadOnlyList<IrExpression> IPrimitiveJoin.CompatibilityArms => [WhenTrue, WhenFalse];
    IReadOnlyList<IrExpression> IPrimitiveJoin.RenderedArms => [WhenTrue, WhenFalse];
    PrimitiveJoinTargetCompatibility IPrimitiveJoin.PrimitiveTargets { get; set; } =
        PrimitiveJoinTargetCompatibility.Empty;

    public override string Describe() => "Conditional";
}

/// <summary>Boolean negation (<c>!</c>), raised from a <c>ceq</c>-zero or inverted-branch pattern.</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundUnaryOperator,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundUnaryOperator (logical negation)",
    precondition: "result is bool; raised from ceq-zero / inverted-branch patterns (IL has no `!` opcode)",
    witness: "corpus compile-back")]
public sealed class LogicalNot : IrExpression
{
    public LogicalNot(IrExpression operand) => AddChild(operand);

    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");

    public override string Describe() => "LogicalNot";
}

public enum UnaryKind { Negate, BitwiseNot }

/// <summary>A unary arithmetic operator: negation (<c>neg</c>) or bitwise complement (<c>not</c>).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundUnaryOperator,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitImporter,
    forwardName: "BoundUnaryOperator (neg/not)",
    precondition: "result type is the operand's type (neg/not preserve the operand type)",
    witness: "corpus compile-back")]
public sealed class Unary : IrExpression
{
    public Unary(UnaryKind kind, IrExpression operand)
    {
        Kind = kind;
        AddChild(operand);
    }

    public UnaryKind Kind { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => Operand.ResultType;

    public override string Describe() => $"Unary.{Kind}";
}

/// <summary>
/// A recovered C# <c>await</c> expression. It is produced by two passes:
/// <see cref="AwaitRecoveryPass"/> rewrites a runtime-async (async v2)
/// <c>System.Runtime.CompilerServices.AsyncHelpers.Await</c> call directly (no
/// MoveNext to unwind), <see cref="RuntimeAsyncAwaiterPass"/> rewrites the
/// runtime-async <c>AwaitAwaiter</c>/<c>UnsafeAwaitAwaiter</c> guard scaffold,
/// and <see cref="ClassicAsyncReconstructionPass"/> recovers it from a classic
/// (runtime-async=off) <c>MoveNext</c> state machine's awaiter <c>GetResult</c>
/// shape. The single child is the awaited operand;
/// <see cref="ResultType"/> is the awaited result type — the
/// <c>AsyncHelpers.Await</c> call's return type for the runtime-async form, or the
/// awaiter's <c>GetResult</c> return type for the classic form (<c>void</c> for the
/// non-generic form).
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundAwaitExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "await x (BoundAwaitExpression) / AsyncHelpers direct or awaiter-helper lowering (runtime-async) or MoveNext-GetResult reconstruction (classic async)",
    precondition: "result is the awaited result type, given at construction: the direct `AsyncHelpers.Await` call or correlated awaiter `GetResult` return type for runtime-async, or the awaiter's `GetResult` return type for classic-async MoveNext reconstruction (`void` for the non-generic form)",
    witness: "async fixtures (classic-async MoveNext reconstruction); corpus compile-back")]
public sealed class AwaitExpression : IrExpression
{
    public AwaitExpression(
        IrExpression operand,
        TypeRef? resultType,
        MetadataFactState resultIsDynamic = MetadataFactState.Unknown,
        ImmutableArray<MethodRef> consumedMemberRefs = default,
        bool provesClassicCompletionPaths = false)
    {
        AddChild(operand);
        ResultType = resultType;
        ResultIsDynamic = resultIsDynamic;
        ConsumedMemberRefs = consumedMemberRefs.IsDefault ? [] : consumedMemberRefs;
        ProvesClassicCompletionPaths = provesClassicCompletionPaths;
    }

    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType { get; }
    public MetadataFactState ResultIsDynamic { get; }
    public ImmutableArray<MethodRef> ConsumedMemberRefs { get; }
    public bool ProvesClassicCompletionPaths { get; }

    public override string Describe() => "AwaitExpression";
}
/// <c>--x</c>, <c>x--</c>. The compiler lowers these (and compound array
/// element stores like <c>a[--i] = ...</c>) to a <c>dup</c> that the importer
/// raises into a single-use stack slot capturing the value beside the matching
/// local update; <see cref="IncrementDecrementPass"/> folds that idiom back so
/// the value renders as the operator the source spelled — and recompiles to the
/// same <c>dup</c> rather than spilling to extra locals.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundIncrementOperator,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundIncrementOperator (++/--, prefix and postfix)",
    precondition: "result type is the target's declared type; the folded value equals the pre-/post-update value per `IsPrefix` — IncrementDecrementPass folds only the exact dup-beside-update idiom, so recompilation reproduces the same dup",
    witness: "IncrementDecrementPassTests, corpus compile-back")]
public sealed class IncrementDecrement : IrExpression
{
    public IncrementDecrement(IrExpression target, bool isIncrement, bool isPrefix, bool isUserDefined = false, bool isChecked = false, MethodRef? consumedMethod = null)
    {
        IsIncrement = isIncrement;
        IsPrefix = isPrefix;
        IsUserDefined = isUserDefined;
        IsChecked = isChecked;
        ConsumedMethod = consumedMethod;
        AddChild(target);
    }

    public bool IsIncrement { get; }
    public bool IsPrefix { get; }
    /// <summary>True when folded from a user-defined <c>op_Increment</c>/<c>op_Decrement</c> call (a non-primitive operand), whose overload is selected by the checked context.</summary>
    public bool IsUserDefined { get; }
    /// <summary>True when folded from a user-defined <c>op_CheckedIncrement</c>/<c>op_CheckedDecrement</c> call, so the use must render in a <c>checked(...)</c> context.</summary>
    public bool IsChecked { get; }
    /// <summary>The folded user-defined operator method (<c>op_Increment</c>/<c>op_Decrement</c> and their <c>op_Checked*</c> variants), or null for a primitive increment. Carries the typed member evidence so consumers (e.g. compile-back closure planning) route the exact operator rather than reconstructing its name.</summary>
    public MethodRef? ConsumedMethod { get; }
    /// <summary>The incremented place — a local or argument load.</summary>
    public IrExpression Target => (IrExpression)Children[0];
    public override TypeRef? ResultType => Target.ResultType;

    public override string Describe()
        => $"{(IsPrefix ? "Pre" : "Post")}{(IsIncrement ? "Increment" : "Decrement")}";
}

public enum CoercionKind { Value, ReferenceWitness }

/// <summary>
/// The C#-surface coercion of a value into a typed sink
/// (docs/design/value-typed-emission.md): "render this value into a position
/// that requires <see cref="Target"/>". Distinct from <see cref="Convert"/>,
/// which models the value's IL history (a conv.* opcode); the two compose —
/// a Convert-wrapped value at a mismatched sink is wrapped, not rewritten.
/// Inserted by CoercionInsertionPass; rendered by the printer's one
/// CoerceText rule; asserted by CoercionInvariant. Roslyn's BoundConversion,
/// in reverse.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Native,
    oracle: Inverse.Oracle.RyuJitStackNormalization,
    forwardName: "BoundConversion (implicit, target-driven)",
    precondition: "sink type recoverable and distinguishable from the stack type",
    assumes: nameof(Inverse.InverseAssumptions.SinkDistinguishableFromStack),
    witness: "CoerceChokePointTests, CoercionInvariantTests, corpus render-text A/B")]
public sealed class Coerce : IrExpression
{
    public Coerce(TypeRef target, IrExpression operand, CoercionKind kind = CoercionKind.Value)
    {
        Target = target;
        Kind = kind;
        AddChild(operand);
    }

    public TypeRef Target { get; }
    public CoercionKind Kind { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => Target;
    public override IEnumerable<TypeRef> DirectTypes => [Target];

    public override string Describe() => $"Coerce {Target.ToDisplayString()}";
}

/// <summary>A numeric conversion (the conv.* family).</summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConversion,
    naming: Inverse.NameProvenance.Inherited,
    oracle: Inverse.Oracle.RyuJitStackNormalization,
    forwardName: "BoundConversion (numeric)",
    precondition: "none — models the conv.* that ran",
    witness: "round-trips by construction; corpus compile-back")]
public sealed class Convert : IrExpression
{
    public Convert(TypeRef target, bool isChecked, bool isUnsigned, IrExpression operand)
    {
        Target = target;
        IsChecked = isChecked;
        IsUnsigned = isUnsigned;
        AddChild(operand);
    }

    public TypeRef Target { get; }
    public bool IsChecked { get; }
    public bool IsUnsigned { get; }
    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => Target;

    public override string Describe()
        => $"Convert {Target.ToDisplayString()}{(IsChecked ? " checked" : "")}{(IsUnsigned ? " unsigned" : "")}";
}
