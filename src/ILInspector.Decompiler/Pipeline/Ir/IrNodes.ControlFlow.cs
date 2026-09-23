using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

using Inverse = ILInspector.Decompiler.Pipeline.InverseArchitecture;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The basic blocks of a function in IL order. Execution falls through to
/// the next block unless a block's last statement branches or returns —
/// fallthrough is implicit, branches are explicit nodes.
/// </summary>
public sealed class BlockContainer : IrNode
{
    internal bool ContainsRetainedBranches { get; set; }

    public void Add(Block block) => AddChild(block);

    public IReadOnlyList<Block> Blocks => Children.Cast<Block>().ToList();

    /// <summary>Index of the block starting at the given IL offset; -1 if none.</summary>
    public int IndexOfOffset(int ilOffset)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (((Block)Children[i]).StartOffset == ilOffset)
                return i;
        }
        return -1;
    }

    public override string Describe() => "BlockContainer";
}

/// <summary>A sequence of statement nodes beginning at <see cref="StartOffset"/>.</summary>
public sealed class Block : IrNode
{
    public Block(int startOffset = 0) => StartOffset = startOffset;

    public int StartOffset { get; }

    public void Add(IrNode statement) => AddChild(statement);

    public override string Describe() => $"Block IL_{StartOffset:X4}";
}

/// <summary>
/// A raised conditional: condition, then-arm, optional else-arm. Produced by
/// the structuring pass from forward branch regions; the flat Branch and
/// ConditionalBranch forms it consumed are gone from the structured tree.
/// </summary>
public sealed class IfStatement : IrNode
{
    public IfStatement(IrExpression condition, Block thenArm, Block? elseArm)
    {
        HasElse = elseArm is not null;
        AddChild(condition);
        AddChild(thenArm);
        if (elseArm is not null)
            AddChild(elseArm);
    }

    public bool HasElse { get; }
    public IrExpression Condition => (IrExpression)Children[0];
    public Block Then => (Block)Children[1];
    public Block? Else => HasElse ? (Block)Children[2] : null;

    public override string Describe() => HasElse ? "IfStatement (with else)" : "IfStatement";
}

/// <summary>
/// A raised while loop: csc's canonical guarded form (entry jump to the
/// condition, body, bottom-tested backward branch). The condition is the
/// stay-in-loop test as the IL wrote it — no negation involved.
/// </summary>
public sealed class WhileLoop : IrNode
{
    public WhileLoop(IrExpression condition, Block body)
    {
        AddChild(condition);
        AddChild(body);
    }

    public IrExpression Condition => (IrExpression)Children[0];
    public Block Body => (Block)Children[1];

    public override string Describe() => "WhileLoop";
}

/// <summary>
/// A raised do-while loop: a bottom-tested back edge with no forward entry
/// jump (<c>BODY; if (cond) goto BODY-start;</c>). The body is a container so
/// inner forward branches structure recursively; the condition is the
/// stay-in-loop test exactly as the IL wrote the back-edge — no negation.
/// </summary>
public sealed class DoWhileLoop : IrNode
{
    public DoWhileLoop(BlockContainer body, IrExpression condition)
    {
        AddChild(body);
        AddChild(condition);
    }

    public BlockContainer Body => (BlockContainer)Children[0];
    public IrExpression Condition => (IrExpression)Children[1];

    public override string Describe() => "DoWhileLoop";
}

/// <summary>
/// A raised for loop: initializer statement, stay-in-loop condition,
/// increment statement, body. Produced from a WhileLoop whose preceding
/// statement initializes the condition variable and whose body ends by
/// stepping it.
/// </summary>
public sealed class ForLoop : IrNode
{
    public ForLoop(IrNode initializer, IrExpression condition, IrNode increment, Block body)
    {
        AddChild(initializer);
        AddChild(condition);
        AddChild(increment);
        AddChild(body);
    }

    public IrNode Initializer => Children[0];
    public IrExpression Condition => (IrExpression)Children[1];
    public IrNode Increment => Children[2];
    public Block Body => (Block)Children[3];

    public override string Describe() => "ForLoop";
}

/// <summary>
/// A raised try with one or more catch clauses (the same protected range in
/// IL). Produced by the EH structuring pass from flat regions; bodies are
/// containers so inner structuring composes per-container.
/// </summary>
public sealed class TryCatch : IrNode
{
    public TryCatch(BlockContainer tryBody, IEnumerable<CatchClause> clauses)
    {
        AddChild(tryBody);
        foreach (var clause in clauses)
            AddChild(clause);
    }

    public BlockContainer TryBody => (BlockContainer)Children[0];
    public IReadOnlyList<CatchClause> Clauses => Children.Skip(1).Cast<CatchClause>().ToList();
    internal InstructionExceptionRegionId? ExceptionProtectedRegion
    { get; init; }

    public override string Describe() => $"TryCatch ({Children.Count - 1} clauses)";
}

/// <summary>
/// One catch clause: the exception type, an optional variable binding (the
/// handler-entry store the pass folded into the header), and the body.
/// </summary>
public sealed class CatchClause : IrNode
{
    public CatchClause(TypeRef exceptionType, BlockContainer body, IrExpression? filter = null)
    {
        ExceptionType = exceptionType;
        if (filter is not null)
            AddChild(filter);
        AddChild(body);
    }

    public TypeRef ExceptionType { get; }

    /// <summary>Local the handler stores the caught exception into; null when the exception is discarded.</summary>
    public int? VariableIndex { get; set; }

    public BlockContainer Body => (BlockContainer)Children[^1];

    /// <summary>Optional C# exception filter (<c>when (...)</c>) for filter handlers.</summary>
    public IrExpression? Filter => Children.Count == 2 ? (IrExpression)Children[0] : null;
    internal InstructionExceptionClause? ExceptionClause { get; init; }

    public override IEnumerable<TypeRef> DirectTypes => [ExceptionType];

    public override string Describe() => $"CatchClause ({ExceptionType.ToDisplayString()})";
}

/// <summary>A raised try/finally.</summary>
public sealed class TryFinally : IrNode
{
    public TryFinally(BlockContainer tryBody, BlockContainer finallyBody)
    {
        AddChild(tryBody);
        AddChild(finallyBody);
    }

    public BlockContainer TryBody => (BlockContainer)Children[0];
    public BlockContainer FinallyBody => (BlockContainer)Children[1];
    internal InstructionExceptionClause? ExceptionClause { get; init; }

    public override string Describe() => "TryFinally";
}

/// <summary>
/// A raised <c>switch</c> statement, produced by the switch pass from an IL
/// jump table. The value is the switch operand; each section carries its case
/// labels or is the default, and a body container the structuring pass raises.
/// Labels are normally the zero-based jump-table indices; when the compiler
/// normalized an enum by adding or subtracting its lowest case value, the pass
/// restores the enum operand and translates the labels back to enum values. A
/// section that leaves the switch does so through a <see cref="Break"/>.
/// </summary>
public sealed class Switch : IrNode
{
    public Switch(IrExpression value, IEnumerable<SwitchSection> sections)
    {
        AddChild(value);
        foreach (var section in sections)
            AddChild(section);
    }

    public IrExpression Value => (IrExpression)Children[0];
    public IReadOnlyList<SwitchSection> Sections => Children.Skip(1).Cast<SwitchSection>().ToList();

    public override string Describe() => $"Switch ({Children.Count - 1} sections)";
}

/// <summary>
/// One section of a <see cref="Switch"/>: its case labels (empty for the
/// default) and body. A label is a compile-time <see cref="Constant"/> — a
/// jump-table index or restored enum value, or the literal string for a
/// switch-on-string raised from the op_Equality chain.
/// </summary>
public sealed class SwitchSection : IrNode
{
    public SwitchSection(System.Collections.Immutable.ImmutableArray<Constant> labels, bool isDefault, BlockContainer body)
    {
        Labels = labels;
        IsDefault = isDefault;
        AddChild(body);
    }

    public System.Collections.Immutable.ImmutableArray<Constant> Labels { get; }
    public bool IsDefault { get; }
    public BlockContainer Body => (BlockContainer)Children[0];

    public override string Describe() => IsDefault ? "default" : $"case {string.Join(", ", Labels.Select(l => l.Value))}";
}

/// <summary>
/// A raised C# <c>switch</c> expression (<c>value switch { labels =&gt; v, …, _ =&gt; v }</c>),
/// produced by the switch pass from a value-producing IL jump table: every case
/// target (and the default) assigns one local that a single downstream read
/// consumes at the join, so the whole dispatch yields one value. Each arm carries
/// its case labels (jump-table indices or restored enum values) or is the default,
/// and the expression it yields. Unlike <see cref="Switch"/> (a statement) this
/// is an expression, so it appears as the value of a <see cref="Return"/> or a
/// store.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConvertedSwitchExpression,
    naming: Inverse.NameProvenance.Inherited,
    forwardName: "BoundConvertedSwitchExpression (value switch { ... })",
    precondition: "every arm (and the default) assigns the single join local one downstream read consumes, so the arms share that local's type; result is the arms' shared type",
    witness: "SwitchExpressionRaisingTests, corpus compile-back")]
public sealed class SwitchExpression : IrExpression, IPrimitiveJoin
{
    public SwitchExpression(IrExpression value, IEnumerable<SwitchExpressionArm> arms)
    {
        AddChild(value);
        foreach (var arm in arms)
            AddChild(arm);
        ((IPrimitiveJoin)this).BindInitialPrimitiveTargets();
    }

    public IrExpression Value => (IrExpression)Children[0];
    public IReadOnlyList<SwitchExpressionArm> Arms => Children.Skip(1).Cast<SwitchExpressionArm>().ToList();

    public override TypeRef? ResultType => Arms.Select(a => a.Value.ResultType).FirstOrDefault(t => t is not null);

    IReadOnlyList<IrExpression> IPrimitiveJoin.CompatibilityArms =>
        [.. Arms.Select(arm => arm.Value)];
    IReadOnlyList<IrExpression> IPrimitiveJoin.RenderedArms =>
        [.. Arms.Select(arm => arm.Value)];
    PrimitiveJoinTargetCompatibility IPrimitiveJoin.PrimitiveTargets { get; set; } =
        PrimitiveJoinTargetCompatibility.Empty;

    public override string Describe() => $"SwitchExpression ({Children.Count - 1} arms)";
}

/// <summary>One arm of a <see cref="SwitchExpression"/>: its case labels (empty for the default) and the value it yields.</summary>
public sealed class SwitchExpressionArm : IrNode
{
    public SwitchExpressionArm(ImmutableArray<int> labels, bool isDefault, IrExpression value)
    {
        Labels = labels;
        IsDefault = isDefault;
        AddChild(value);
    }

    public ImmutableArray<int> Labels { get; }
    public bool IsDefault { get; }
    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => IsDefault ? "default arm" : $"arm {string.Join(", ", Labels)}";
}

/// <summary>
/// A raised type-pattern switch expression over a union receiver:
/// <c>pet switch { Cat cat =&gt; ..., Dog =&gt; ... }</c>. The value is the
/// compiler-emitted union <c>Value</c> getter access; the printer uses the
/// proven union receiver when rendering the switch input.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConvertedSwitchExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundConvertedSwitchExpression (type-pattern arms over a union receiver)",
    precondition: "the receiver is the compiler-emitted union `Value` getter access over a proven union case set; each arm is a type pattern for one case; result is the arms' (or null/default arm's) shared type",
    witness: "MemberBodyProducerUnionTests, union fixture corpus")]
public sealed class UnionSwitchExpression : IrExpression
{
    readonly bool _hasDefault;
    readonly bool _hasNullArm;

    public UnionSwitchExpression(
        IrExpression value,
        IEnumerable<UnionSwitchExpressionArm> arms,
        IrExpression? defaultValue = null,
        IrExpression? nullValue = null)
    {
        AddChild(value);
        foreach (var arm in arms)
            AddChild(arm);
        if (nullValue is not null)
        {
            _hasNullArm = true;
            AddChild(new SynthesizedSwitchExpressionArm(isNull: true, nullValue));
        }
        if (defaultValue is not null)
        {
            _hasDefault = true;
            AddChild(new SynthesizedSwitchExpressionArm(isNull: false, defaultValue));
        }
    }

    public IrExpression Value => (IrExpression)Children[0];
    public bool HasDefault => _hasDefault;
    public bool HasNullArm => _hasNullArm;
    public IReadOnlyList<UnionSwitchExpressionArm> Arms
        => Children.Skip(1).Take(Children.Count - 1 - (_hasNullArm ? 1 : 0) - (_hasDefault ? 1 : 0)).Cast<UnionSwitchExpressionArm>().ToList();
    internal SynthesizedSwitchExpressionArm? NullArm
        => _hasNullArm ? (SynthesizedSwitchExpressionArm)Children[Children.Count - 1 - (_hasDefault ? 1 : 0)] : null;
    internal SynthesizedSwitchExpressionArm? DefaultArm
        => _hasDefault ? (SynthesizedSwitchExpressionArm)Children[^1] : null;
    public IrExpression? NullValue => NullArm?.Value;
    public IrExpression? DefaultValue => DefaultArm?.Value;
    public override TypeRef? ResultType
        => Arms.Select(a => a.Value.ResultType).Append(NullValue?.ResultType).Append(DefaultValue?.ResultType).FirstOrDefault(t => t is not null);

    public override string Describe() => $"UnionSwitchExpression ({Arms.Count} arms{(_hasNullArm ? " + null" : "")}{(_hasDefault ? " + default" : "")})";
}

/// <summary>One type-pattern arm of a <see cref="UnionSwitchExpression"/>.</summary>
public sealed class UnionSwitchExpressionArm : IrNode
{
    readonly bool _hasGuard;

    public UnionSwitchExpressionArm(TypeRef patternType, int? localIndex, IrExpression value, IrExpression? guard = null)
    {
        PatternType = patternType;
        LocalIndex = localIndex;
        if (guard is not null)
        {
            _hasGuard = true;
            AddChild(guard);
        }
        AddChild(value);
    }

    public TypeRef PatternType { get; }
    public int? LocalIndex { get; }
    public bool HasGuard => _hasGuard;
    public IrExpression? Guard => _hasGuard ? (IrExpression)Children[0] : null;
    public IrExpression Value => (IrExpression)Children[_hasGuard ? 1 : 0];

    public override IEnumerable<TypeRef> DirectTypes => [PatternType];
    public override string Describe() => LocalIndex is { } index
        ? $"arm {PatternType.ToDisplayString()} V_{index}"
        : $"arm {PatternType.ToDisplayString()}";
}

internal sealed class SynthesizedSwitchExpressionArm : IrNode
{
    public SynthesizedSwitchExpressionArm(bool isNull, IrExpression value)
    {
        IsNull = isNull;
        AddChild(value);
    }

    public bool IsNull { get; }
    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => IsNull ? "null arm" : "default arm";
}

internal sealed class SynthesizedRenderedExpression : IrNode
{
    public SynthesizedRenderedExpression(string kind) => Kind = kind;

    public string Kind { get; }

    public override string Describe() => $"rendered {Kind}";
}

/// <summary>
/// A raised tuple relational-pattern switch expression over independent
/// side-effect-free places: <c>(x, y) switch { (&gt; 0, &gt; 0) =&gt; "I", ...,
/// _ =&gt; "axis" }</c>. Produced by <see cref="TupleSwitchExpressionPass"/> from
/// the exhaustive nested if/return comparison tree <see cref="ReturnDispatchPass"/>
/// already folds; each component is a distinct <c>LoadArgument</c>/<c>LoadLocal</c>
/// read (no tuple is materialized), and arms reuse <see cref="PositionalPatternSubpattern"/>
/// exactly as <see cref="PositionalPattern"/> does.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConvertedSwitchExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundConvertedSwitchExpression (tuple relational-pattern arms over independent places)",
    precondition: "components are independent side-effect-free LoadArgument/LoadLocal reads sharing one integer-like anchor constant per component; arms are mutually exclusive positional relational/equality patterns proven exhaustive by intersecting per-component constraints along the compiler's nested if/return comparison tree; exactly one trailing default arm merges the remaining structurally-identical constant leaves",
    witness: "TupleSwitchExpressionPassTests, LadderRung5 Quadrant fixture, corpus compile-back")]
public sealed class TupleSwitchExpression : IrExpression
{
    public TupleSwitchExpression(IReadOnlyList<IrExpression> components, IEnumerable<TupleSwitchExpressionArm> arms)
    {
        if (components.Count < 2)
            throw new ArgumentException("A tuple switch expression needs at least two components.", nameof(components));

        ComponentCount = components.Count;
        foreach (var component in components)
            AddChild(component);
        foreach (var arm in arms)
            AddChild(arm);
    }

    /// <summary>How many leading children are components (the rest are arms).</summary>
    public int ComponentCount { get; }

    /// <summary>The independent switch-governing places, e.g. <c>x</c> and <c>y</c> in <c>(x, y) switch</c>.</summary>
    public IReadOnlyList<IrExpression> Components => Children.Take(ComponentCount).Cast<IrExpression>().ToList();

    public IReadOnlyList<TupleSwitchExpressionArm> Arms => Children.Skip(ComponentCount).Cast<TupleSwitchExpressionArm>().ToList();

    public override TypeRef? ResultType => Arms.Select(a => a.Value.ResultType).FirstOrDefault(t => t is not null);

    public override string Describe() => $"TupleSwitchExpression ({ComponentCount} components, {Arms.Count} arms)";
}

/// <summary>One arm of a <see cref="TupleSwitchExpression"/>: one relational/equality sub-pattern per component (empty for the default arm) and the value it yields.</summary>
public sealed class TupleSwitchExpressionArm : IrNode
{
    public TupleSwitchExpressionArm(IReadOnlyList<PositionalPatternSubpattern> subpatterns, IReadOnlyList<Constant> constants, IrExpression value)
    {
        if (subpatterns.Count != constants.Count)
            throw new ArgumentException("A tuple switch arm needs one constant per sub-pattern.", nameof(constants));

        Subpatterns = [.. subpatterns];
        AddChild(value);
        foreach (var constant in constants)
            AddChild(constant);
    }

    public IrExpression Value => (IrExpression)Children[0];

    /// <summary>The per-component sub-pattern kinds, parallel to <see cref="Constants"/>; empty means the default arm.</summary>
    public ImmutableArray<PositionalPatternSubpattern> Subpatterns { get; }

    /// <summary>The per-component anchor constants used by the relational/equality sub-patterns.</summary>
    public IReadOnlyList<Constant> Constants => Children.Skip(1).Cast<Constant>().ToList();

    public bool IsDefault => Subpatterns.Length == 0;

    public override string Describe() => IsDefault ? "default arm" : $"arm ({Subpatterns.Length} components)";
}

/// <summary>
/// A raised type / single-level property-declaration-pattern switch expression
/// over an arbitrary receiver:
/// <c>expression switch { Comparison c when g =&gt; v, LogicalNot { Operand: Comparison c } when g =&gt; w, _ =&gt; false }</c>.
/// Unlike <see cref="UnionSwitchExpression"/> the receiver is any expression (not
/// a discriminated-union <c>Value</c> getter) and arms may carry a single-level
/// property subpattern. Produced by <see cref="PatternSwitchExpressionPass"/> from
/// the compiler's nested type-test <c>as</c>/null-test if/return dispatch, when
/// every arm is mutually exclusive and side-effect-free and the fall-through
/// yields one shared default value.
/// </summary>
[Inverse.InverseOf(
    Inverse.Forward.RoslynBoundConvertedSwitchExpression,
    naming: Inverse.NameProvenance.Native,
    forwardName: "BoundConvertedSwitchExpression (type / single-level property-pattern arms over an arbitrary receiver)",
    precondition: "each arm is a mutually-exclusive type or single-level property-declaration pattern (optionally when-guarded) yielding one return value, the fall-through default yields the trailing value; result is the arms' shared type",
    witness: "PatternSwitchExpressionPassTests, corpus compile-back")]
public sealed class PatternSwitchExpression : IrExpression
{
    readonly bool _hasDefault;

    public PatternSwitchExpression(
        IrExpression value,
        IEnumerable<PatternSwitchExpressionArm> arms,
        IrExpression? defaultValue = null)
    {
        AddChild(value);
        foreach (var arm in arms)
            AddChild(arm);
        if (defaultValue is not null)
        {
            _hasDefault = true;
            AddChild(new SynthesizedSwitchExpressionArm(isNull: false, defaultValue));
        }
    }

    public IrExpression Value => (IrExpression)Children[0];
    public bool HasDefault => _hasDefault;
    public IReadOnlyList<PatternSwitchExpressionArm> Arms
        => Children.Skip(1).Take(Children.Count - 1 - (_hasDefault ? 1 : 0)).Cast<PatternSwitchExpressionArm>().ToList();
    internal SynthesizedSwitchExpressionArm? DefaultArm
        => _hasDefault ? (SynthesizedSwitchExpressionArm)Children[^1] : null;
    public IrExpression? DefaultValue => DefaultArm?.Value;
    public override TypeRef? ResultType
        => Arms.Select(a => a.Value.ResultType).Append(DefaultValue?.ResultType).FirstOrDefault(t => t is not null);

    public override string Describe() => $"PatternSwitchExpression ({Arms.Count} arms{(_hasDefault ? " + default" : "")})";
}

/// <summary>
/// A single-level recursive property subpattern carried by a
/// <see cref="PatternSwitchExpressionArm"/>: the <c>{ Property: T inner }</c> in
/// <c>LogicalNot { Operand: Comparison comparison }</c>. Immutable value data
/// (copied by <see cref="IrNode.Clone"/> via <c>MemberwiseClone</c>).
/// </summary>
public sealed record PropertySubpattern(MethodRef Accessor, TypeRef PatternType, int LocalIndex)
{
    public string PropertyName => Accessor.Name["get_".Length..];
}

/// <summary>One arm of a <see cref="PatternSwitchExpression"/>: a type pattern
/// (with optional single-level property subpattern and optional <c>when</c> guard)
/// and the value it yields.</summary>
public sealed class PatternSwitchExpressionArm : IrNode
{
    readonly bool _hasGuard;

    public PatternSwitchExpressionArm(
        TypeRef patternType,
        int? localIndex,
        PropertySubpattern? subpattern,
        IrExpression value,
        IrExpression? guard = null)
    {
        PatternType = patternType;
        LocalIndex = localIndex;
        Subpattern = subpattern;
        if (guard is not null)
        {
            _hasGuard = true;
            AddChild(guard);
        }
        AddChild(value);
    }

    /// <summary>The arm's outer type pattern — the <c>Comparison</c> / <c>LogicalNot</c>.</summary>
    public TypeRef PatternType { get; }

    /// <summary>The local bound by the outer type pattern, or <c>null</c> when it binds nothing (a property-subpattern arm binds only the inner local).</summary>
    public int? LocalIndex { get; }

    /// <summary>The single-level property subpattern, or <c>null</c> for a bare type pattern.</summary>
    public PropertySubpattern? Subpattern { get; }

    public bool HasGuard => _hasGuard;
    public IrExpression? Guard => _hasGuard ? (IrExpression)Children[0] : null;
    public IrExpression Value => (IrExpression)Children[_hasGuard ? 1 : 0];

    public override IEnumerable<TypeRef> DirectTypes
        => Subpattern is { } sub ? [PatternType, sub.PatternType] : [PatternType];

    public override string Describe()
    {
        string local = LocalIndex is { } index ? $" V_{index}" : "";
        string sub = Subpattern is { } s ? $" {{ {s.PropertyName}: {s.PatternType.ToDisplayString()} V_{s.LocalIndex} }}" : "";
        return $"arm {PatternType.ToDisplayString()}{local}{sub}";
    }
}

/// <summary>
/// A raised <c>lock</c> statement. Produced by the lock-sugar pass from the
/// csc Monitor lowering — <c>Monitor.Enter(obj, ref taken)</c> in a try whose
/// finally is <c>if (taken) Monitor.Exit(obj)</c>.
/// </summary>
public sealed class Lock : IrNode
{
    public Lock(IrExpression lockObject, BlockContainer body)
    {
        AddChild(lockObject);
        AddChild(body);
    }

    public IrExpression LockObject => (IrExpression)Children[0];
    public BlockContainer Body => (BlockContainer)Children[1];

    public override string Describe() => "Lock";
}

/// <summary>
/// A raised <c>fixed</c> statement. Produced by <see cref="FixedStatementPass"/>
/// from the csc pin lowering: a <c>pinned T&amp;</c> local assigned a managed
/// reference, used to derive an unmanaged pointer inside the pinned region, and
/// (when the region ends before the method) unpinned by a store of null/zero.
/// The pinned local becomes the <c>fixed</c> pointer variable
/// (<c>fixed (T* V = &amp;place) { ... }</c>): its source spelling is
/// <c>&amp;</c> applied to the reference being pinned (<see cref="PinSource"/>),
/// and its loads inside the body read as a pointer of type
/// <see cref="ElementType"/><c>*</c>. <see cref="LocalIndex"/> is the pinned
/// slot, so the printer can name the variable and skip its up-front declaration.
/// </summary>
public sealed class Fixed : IrNode
{
    public Fixed(TypeRef elementType, int localIndex, IrExpression pinSource, BlockContainer body, bool sourceIsAddress = true, bool localIsStackSlot = false, TypeRef? localStackSlotType = null, bool requiresUnsafeContext = false)
    {
        ElementType = elementType;
        LocalIndex = localIndex;
        SourceIsAddress = sourceIsAddress;
        LocalIsStackSlot = localIsStackSlot;
        LocalStackSlotType = localStackSlotType;
        RequiresUnsafeContext = requiresUnsafeContext;
        AddChild(pinSource);
        AddChild(body);
    }

    /// <summary>The pointed-to element type — the <c>T</c> in the <c>T*</c> pinned pointer.</summary>
    public TypeRef ElementType { get; }

    /// <summary>The pinned local slot that becomes the <c>fixed</c> pointer variable.</summary>
    public int LocalIndex { get; }

    /// <summary>True when <see cref="LocalIndex"/> names a synthesized stack slot rather than a metadata local.</summary>
    public bool LocalIsStackSlot { get; }

    /// <summary>The stack-slot render type used to resolve <see cref="LocalIndex"/>'s emitted name when <see cref="LocalIsStackSlot"/> is true.</summary>
    public TypeRef? LocalStackSlotType { get; }

    /// <summary>True when this fixed statement must be wrapped in an explicit unsafe context under updated memory-safety rules.</summary>
    public bool RequiresUnsafeContext { get; }

    /// <summary>
    /// True for the managed-reference pin (<c>fixed (T* p = &amp;place)</c>), where the
    /// source renders as <c>&amp;</c> applied to a place. False for the array/string pin
    /// (<c>fixed (T* p = array)</c>), where the source is a pinnable expression rendered
    /// as-is — the language inserts the element-address and null/empty guard itself.
    /// </summary>
    public bool SourceIsAddress { get; }

    /// <summary>The managed reference being pinned; rendered as <c>&amp;</c> applied to the place it refers to.</summary>
    public IrExpression PinSource => (IrExpression)Children[0];
    public BlockContainer Body => (BlockContainer)Children[1];

    public override IEnumerable<TypeRef> DirectTypes => [ElementType];

    public override string Describe() => $"Fixed V_{LocalIndex} ({ElementType.ToDisplayString()}*)";
}

/// <summary>
/// A raised <c>using</c> statement. Produced by <see cref="UsingStatementPass"/>
/// from csc's reference-type disposal lowering: a resource local initialized
/// immediately before a try/finally whose finally null-checks the resource and
/// calls <c>IDisposable.Dispose</c>. <see cref="LocalIndex"/> identifies the
/// consumed resource slot.
/// <para>
/// <see cref="DeclaresResourceVariable"/> is <see langword="false"/> when the
/// resource local is disposed-only and carries no recovered source name. The
/// idiomatic C# for that shape omits the local entirely — <c>using (expr)</c>
/// rather than <c>using (T V_n = expr)</c> — while preserving any conversion
/// the declaration applied (#3346).
/// </para>
/// </summary>
public sealed class UsingStatement : IrNode
{
    public UsingStatement(
        int localIndex,
        TypeRef resourceType,
        IrExpression resource,
        BlockContainer body,
        bool isAwait = false,
        ImmutableArray<MethodRef> consumedMemberRefs = default,
        bool declaresResourceVariable = true)
    {
        LocalIndex = localIndex;
        ResourceType = resourceType;
        IsAwait = isAwait;
        ConsumedMemberRefs = consumedMemberRefs.IsDefault ? [] : consumedMemberRefs;
        DeclaresResourceVariable = declaresResourceVariable;
        AddChild(resource);
        AddChild(body);
    }

    public int LocalIndex { get; }
    public TypeRef ResourceType { get; }
    public bool IsAwait { get; }
    public ImmutableArray<MethodRef> ConsumedMemberRefs { get; }
    public bool DeclaresResourceVariable { get; }
    public IrExpression Resource => (IrExpression)Children[0];
    public BlockContainer Body => (BlockContainer)Children[1];

    public override IEnumerable<TypeRef> DirectTypes => [ResourceType];

    public override string Describe() => $"{(IsAwait ? "AwaitUsingStatement" : "UsingStatement")} V_{LocalIndex} ({ResourceType.ToDisplayString()})";
}

/// <summary>
/// A raised <c>foreach</c> or <c>await foreach</c> statement. Produced by
/// <see cref="ForeachStatementPass"/> from csc's enumerator lowering: hidden
/// enumerator resource, MoveNext/MoveNextAsync loop, and Current assignment to
/// the iteration variable.
/// </summary>
public sealed class ForeachStatement : IrNode
{
    public ForeachStatement(
        int localIndex,
        TypeRef localType,
        IrExpression collection,
        Block body,
        ImmutableArray<MethodRef> consumedMemberRefs = default,
        bool isAwait = false)
    {
        LocalIndex = localIndex;
        LocalType = localType;
        ConsumedMemberRefs = consumedMemberRefs.IsDefault ? [] : consumedMemberRefs;
        IsAwait = isAwait;
        AddChild(collection);
        AddChild(body);
    }

    public int LocalIndex { get; }
    public TypeRef LocalType { get; }
    public bool IsAwait { get; }
    public ImmutableArray<MethodRef> ConsumedMemberRefs { get; }
    public IrExpression Collection => (IrExpression)Children[0];
    public Block Body => (Block)Children[1];
    public override IEnumerable<TypeRef> DirectTypes => [LocalType];

    public override string Describe() => $"{(IsAwait ? "AwaitForeachStatement" : "ForeachStatement")} V_{LocalIndex} ({LocalType.ToDisplayString()})";
}

/// <summary>
/// A stable empty statement that owns a retained branch-target label. Unlike a
/// neighboring statement, later expression and sugar passes cannot consume it,
/// and the printer keeps it outside any synthesized <c>unsafe</c> block.
/// </summary>
public sealed class LabelAnchor : IrNode
{
    internal bool RetainsPdbLocalScope { get; set; }

    public override string Describe() => "LabelAnchor";
}

/// <summary>An unconditional branch to the block starting at <see cref="TargetOffset"/>.</summary>
public sealed class Branch : IrNode
{
    public Branch(int targetOffset) => TargetOffset = targetOffset;

    public int TargetOffset { get; }

    public override string Describe() => $"Branch IL_{TargetOffset:X4}";
}

/// <summary>Branches to <see cref="TargetOffset"/> when the condition is true; falls through otherwise.</summary>
public sealed class ConditionalBranch : IrNode
{
    public ConditionalBranch(
        IrExpression condition,
        int targetOffset,
        ConditionalBranchOrigin origin = ConditionalBranchOrigin.Synthesized)
    {
        TargetOffset = targetOffset;
        Origin = origin;
        AddChild(condition);
    }

    public IrExpression Condition => (IrExpression)Children[0];
    public int TargetOffset { get; }
    public ConditionalBranchOrigin Origin { get; }

    public override string Describe() => $"ConditionalBranch IL_{TargetOffset:X4}";
}

public enum ConditionalBranchOrigin
{
    Synthesized,
    Imported,
}

/// <summary>
/// A C# <c>break</c>: an in-loop branch the loop-structuring pass raised from a
/// goto to the loop's single exit block. Childless terminator, like
/// <see cref="Branch"/>.
/// </summary>
public sealed class Break : IrNode
{
    public override string Describe() => "Break";
}

/// <summary>
/// A C# <c>continue</c>: a loop-continuation transfer raised from a branch or
/// an EH <see cref="Leave"/>. Childless terminator, like <see cref="Branch"/>.
/// </summary>
public sealed class Continue : IrNode
{
    public Continue(ContinueOrigin origin = ContinueOrigin.Unverified)
        => Origin = origin;

    public ContinueOrigin Origin { get; }

    public override string Describe() => "Continue";
}

/// <summary>The structural proof, if any, that licenses a raised <see cref="Continue"/>.</summary>
public enum ContinueOrigin
{
    /// <summary>No proof currently licenses opcode-exact fidelity.</summary>
    Unverified,

    /// <summary>A protected-region leave bound to the owning for-loop increment.</summary>
    ProtectedRegionLeaveToForIncrement,
}
