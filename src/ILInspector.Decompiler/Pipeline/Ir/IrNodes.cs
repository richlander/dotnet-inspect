using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>How a by-reference argument must be spelled at a C# call site — recovered from the callee's parameter metadata.</summary>
public enum ArgumentRefKind { Value, Ref, Out, In }

/// <summary>A metadata fact whose evidence may be unavailable from the current token.</summary>
public enum MetadataFactState { Unknown, No, Yes }

/// <summary>Whether by-ref parameter keyword metadata was needed and recovered.</summary>
public enum ParameterRefKindFacts { Unknown, NotRequired, Known }

/// <summary>A materialized method reference — callee identity with symbolic types, no metadata handles.</summary>
public sealed record MethodRef(
    TypeRef DeclaringType,
    string Name,
    TypeRef ReturnType,
    ImmutableArray<TypeRef> ParameterTypes,
    bool HasThis)
{
    /// <summary>Generic method type arguments (MethodSpec instantiations); empty for non-generic callees.</summary>
    public ImmutableArray<TypeRef> TypeArguments { get; init; } = [];

    /// <summary>
    /// Per-parameter call-site ref-kind (ref/out/in), aligned 1:1 with
    /// <see cref="ParameterTypes"/>. Populated for callees resolved as a
    /// MethodDef, from the parameter rows (IsReadOnlyAttribute / the Out flag),
    /// either directly or by resolving a MemberRef through the metadata context.
    /// Empty means either no by-ref facts were needed or the rows were
    /// unreachable; <see cref="ParameterRefKindsFacts"/> disambiguates.
    /// </summary>
    public ImmutableArray<ArgumentRefKind> ParameterRefKinds { get; init; } = [];

    /// <summary>
    /// Whether <see cref="ParameterRefKinds"/> is known. Distinguishes "no by-ref
    /// parameters needed spelling facts" from "a MemberRef did not expose rows."
    /// </summary>
    public ParameterRefKindFacts ParameterRefKindsFacts { get; init; } = ParameterRefKindFacts.Unknown;

    /// <summary>
    /// The callee is <em>requires-unsafe</em>: under the updated memory-safety
    /// rules a member declared <c>unsafe</c>/<c>extern</c> is stamped with
    /// <c>RequiresUnsafeAttribute</c>, and every call site needs an unsafe
    /// context even when no pointer crosses the call boundary. Recovered from
    /// the callee MethodDef's attributes when reachable. The printer's
    /// signature-pointer heuristic covers the compat-mode cross-assembly case
    /// when the attribute cannot be read.
    /// </summary>
    public bool RequiresUnsafe { get; init; }

    /// <summary>
    /// Metadata SpecialName evidence (accessors, operators). Exact for
    /// same-assembly MethodDefs; cross-assembly MemberRefs carry no flags,
    /// so the resolver falls back to accessor-shape naming — the strongest
    /// local evidence available without assembly resolution.
    /// </summary>
    public bool IsSpecialName { get; init; }

    /// <summary>
    /// Metadata <c>[CompilerGenerated]</c> evidence on this method, or
    /// <see cref="MetadataFactState.Unknown"/> when the defining MethodDef was
    /// unreachable from the call-site token.
    /// </summary>
    public MetadataFactState CompilerGenerated { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// Metadata <c>[CompilerGenerated]</c> evidence on the declaring type, or
    /// <see cref="MetadataFactState.Unknown"/> when the defining TypeDef was
    /// unreachable from the call-site token.
    /// </summary>
    public MetadataFactState DeclaringTypeCompilerGenerated { get; init; } = MetadataFactState.Unknown;

    /// <summary>
    /// True when a managed-pointer argument is passed to a by-ref parameter of
    /// this callee while <see cref="ParameterRefKinds"/> is empty — the callee
    /// resolved as a MemberReference (cross-assembly, or a same-assembly call on
    /// a generic type instance), which carries no parameter rows, so the
    /// call-site <c>out</c>/<c>in</c>/<c>ref</c> kind is unknown. The printer
    /// then spells a default keyword it cannot verify (wrong for out/in:
    /// CS1620/CS1615), so callers lower fidelity rather than claim a faithful
    /// render. <paramref name="nonReceiverArguments"/> aligns 1:1 with
    /// <see cref="ParameterTypes"/> (the instance receiver dropped first).
    /// </summary>
    public bool HasUnverifiableByRefArgument(IReadOnlyList<IrExpression> nonReceiverArguments)
    {
        if (ParameterRefKindsFacts != ParameterRefKindFacts.Unknown || !ParameterRefKinds.IsDefaultOrEmpty)
            return false;
        for (int i = 0; i < ParameterTypes.Length && i < nonReceiverArguments.Count; i++)
            if (ParameterTypes[i].Kind == TypeRefKind.ByRef
                && nonReceiverArguments[i].ResultType is { Kind: TypeRefKind.ByRef })
                return true;
        return false;
    }
}

/// <summary>A materialized field reference.</summary>
public sealed record FieldRef(TypeRef DeclaringType, string Name, TypeRef Type);

/// <summary>The root of one method's IR: signature plus a body container, with diagnostics accumulated during construction and passes.</summary>
public sealed class IrFunction : IrNode
{
    public IrFunction(string name, TypeRef declaringType, MethodSignature signature, ImmutableArray<TypeRef> locals, BlockContainer body)
    {
        Name = name;
        DeclaringType = declaringType;
        Signature = signature;
        Locals = locals;
        AddChild(body);
    }

    public string Name { get; }
    public TypeRef DeclaringType { get; }
    public MethodSignature Signature { get; }
    public ImmutableArray<TypeRef> Locals { get; }

    /// <summary>
    /// Source names for the entries in <see cref="Locals"/>, by slot index,
    /// recovered from the PDB at import. Empty when no PDB was available;
    /// individual entries are null when a slot has no usable source name. The
    /// printer renders a present name and falls back to <c>V_index</c> otherwise.
    /// </summary>
    public ImmutableArray<string?> LocalNames { get; set; } = [];
    public BlockContainer Body => (BlockContainer)Children[0];
    public List<DecompilerDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// True when the defining module opts into the updated C# memory-safety
    /// rules — it carries a module-level
    /// <c>System.Runtime.CompilerServices.MemorySafetyRulesAttribute</c>. Under
    /// those rules the member <c>unsafe</c> modifier no longer introduces a body
    /// unsafe context, so the printer must wrap each unsafe operation in an
    /// explicit, minimally scoped <c>unsafe { }</c> block. Legacy modules (no
    /// attribute) keep relying on the member modifier and render no blocks.
    /// </summary>
    public bool UsesUpdatedMemorySafetyRules { get; set; }

    /// <summary>
    /// True when the method body's locals are not zero-initialized — the
    /// effective result of <c>[SkipLocalsInit]</c> (applied at the member, type,
    /// or module level), observed as a cleared <c>.locals init</c> flag. Under
    /// the updated memory-safety rules a <c>stackalloc</c> converted to a
    /// <c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c> with no initializer is
    /// unsafe only in such a body, because the stack space is then uninitialized.
    /// </summary>
    public bool SkipLocalsInit { get; set; }

    /// <summary>
    /// Exception regions over the flat block container, by IL offset. The
    /// importer keeps blocks flat (region boundaries are block leaders);
    /// the EH structuring pass consumes these into <see cref="TryCatch"/>/
    /// <see cref="TryFinally"/> nodes and clears the list — non-empty regions
    /// mean the flat form is still the truth.
    /// </summary>
    public ImmutableArray<HandlerRegion> Regions { get; set; } = [];

    /// <summary>
    /// Resolved C# shapes for the definition types this function references,
    /// materialized at import (the printer is metadata-free). Same-assembly
    /// only; cross-assembly types are absent and read as
    /// <see cref="TypeShape.Unknown"/>. Lets the printer null-test a reference
    /// definition and zero-test an enum where <see cref="TypeFamilies.Of"/>
    /// cannot classify a bare definition.
    /// </summary>
    public IReadOnlyDictionary<TypeRef, TypeShape> TypeShapes { get; set; }
        = ImmutableDictionary<TypeRef, TypeShape>.Empty;

    /// <summary>
    /// Named members (value → name) of the same-assembly enum types this
    /// function references, materialized at import. Lets the printer render an
    /// enum constant as <c>EnumType.Member</c> instead of its raw integer.
    /// </summary>
    public IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>> EnumMembers { get; set; }
        = ImmutableDictionary<TypeRef, IReadOnlyDictionary<long, string>>.Empty;

    public override IEnumerable<TypeRef> DirectTypes
        => Signature.Parameters.Select(p => p.Type)
            .Append(Signature.ReturnType)
            .Append(DeclaringType)
            .Concat(Locals)
            .Concat(Regions.Where(r => r.CatchType is not null).Select(r => r.CatchType!));

    /// <summary>
    /// Computed from the tree, never asserted: any unsupported node, any
    /// unsupported type referenced anywhere, or any expression whose result
    /// type the pipeline does not know (null — e.g. a join slot merged from
    /// conflicting types) ⇒ at most <see cref="DecompilationFidelity.Partial"/>.
    /// </summary>
    public DecompilationFidelity Fidelity
        => Descendants.Prepend(this).Any(n =>
            n is UnsupportedNode
            || n is LoadFunctionPointer
            || n is Call { HasUnverifiedByRefArgument: true }
            || n is NewObject { HasUnverifiedByRefArgument: true }
            || n.DirectTypes.Any(t => t.ContainsUnsupported)
            || n is IrExpression { ResultType: null }
            || (n as IrExpression)?.ResultType?.ContainsUnsupported == true)
            ? DecompilationFidelity.Partial
            : DecompilationFidelity.Full;

    public override string Describe()
        => $"Function {Signature.ReturnType.ToDisplayString()} {Name}({string.Join(", ", Signature.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})";
}

/// <summary>
/// The basic blocks of a function in IL order. Execution falls through to
/// the next block unless a block's last statement branches or returns —
/// fallthrough is implicit, branches are explicit nodes.
/// </summary>
public sealed class BlockContainer : IrNode
{
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

    public override string Describe() => $"TryCatch ({Children.Count - 1} clauses)";
}

/// <summary>
/// One catch clause: the exception type, an optional variable binding (the
/// handler-entry store the pass folded into the header), and the body.
/// </summary>
public sealed class CatchClause : IrNode
{
    public CatchClause(TypeRef exceptionType, BlockContainer body)
    {
        ExceptionType = exceptionType;
        AddChild(body);
    }

    public TypeRef ExceptionType { get; }

    /// <summary>Local the handler stores the caught exception into; null when the exception is discarded.</summary>
    public int? VariableIndex { get; init; }

    public BlockContainer Body => (BlockContainer)Children[0];

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

    public override string Describe() => "TryFinally";
}

/// <summary>
/// A raised <c>switch</c> statement, produced by the switch pass from an IL
/// jump table. The value is the switch operand; each section carries its case
/// labels (the zero-based jump-table indices) or is the default, and a body
/// container the structuring pass raises. A section that leaves the switch
/// does so through a <see cref="Break"/>.
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
/// default) and body. A label is a compile-time <see cref="Constant"/> — the
/// zero-based jump-table index for an IL jump table, or the literal string for a
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
/// its case labels (the zero-based jump-table indices) or is the default, and the
/// expression it yields. Unlike <see cref="Switch"/> (a statement) this is an
/// expression, so it appears as the value of a <see cref="Return"/> or a store.
/// </summary>
public sealed class SwitchExpression : IrExpression
{
    public SwitchExpression(IrExpression value, IEnumerable<SwitchExpressionArm> arms)
    {
        AddChild(value);
        foreach (var arm in arms)
            AddChild(arm);
    }

    public IrExpression Value => (IrExpression)Children[0];
    public IReadOnlyList<SwitchExpressionArm> Arms => Children.Skip(1).Cast<SwitchExpressionArm>().ToList();

    public override TypeRef? ResultType => Arms.Select(a => a.Value.ResultType).FirstOrDefault(t => t is not null);

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
    public Fixed(TypeRef elementType, int localIndex, IrExpression pinSource, BlockContainer body)
    {
        ElementType = elementType;
        LocalIndex = localIndex;
        AddChild(pinSource);
        AddChild(body);
    }

    /// <summary>The pointed-to element type — the <c>T</c> in the <c>T*</c> pinned pointer.</summary>
    public TypeRef ElementType { get; }

    /// <summary>The pinned local slot that becomes the <c>fixed</c> pointer variable.</summary>
    public int LocalIndex { get; }

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
/// calls <c>IDisposable.Dispose</c>. <see cref="LocalIndex"/> is the resource
/// slot declared by the using header.
/// </summary>
public sealed class UsingStatement : IrNode
{
    public UsingStatement(int localIndex, TypeRef resourceType, IrExpression resource, BlockContainer body)
    {
        LocalIndex = localIndex;
        ResourceType = resourceType;
        AddChild(resource);
        AddChild(body);
    }

    public int LocalIndex { get; }
    public TypeRef ResourceType { get; }
    public IrExpression Resource => (IrExpression)Children[0];
    public BlockContainer Body => (BlockContainer)Children[1];

    public override IEnumerable<TypeRef> DirectTypes => [ResourceType];

    public override string Describe() => $"UsingStatement V_{LocalIndex} ({ResourceType.ToDisplayString()})";
}

/// <summary>
/// A raised <c>foreach</c> statement. Produced by <see cref="ForeachStatementPass"/>
/// from csc's enumerator lowering: hidden enumerator resource, MoveNext loop,
/// and Current assignment to the iteration variable.
/// </summary>
public sealed class ForeachStatement : IrNode
{
    public ForeachStatement(int localIndex, TypeRef localType, IrExpression collection, Block body)
    {
        LocalIndex = localIndex;
        LocalType = localType;
        AddChild(collection);
        AddChild(body);
    }

    public int LocalIndex { get; }
    public TypeRef LocalType { get; }
    public IrExpression Collection => (IrExpression)Children[0];
    public Block Body => (Block)Children[1];
    public override IEnumerable<TypeRef> DirectTypes => [LocalType];

    public override string Describe() => $"ForeachStatement V_{LocalIndex} ({LocalType.ToDisplayString()})";
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
    public ConditionalBranch(IrExpression condition, int targetOffset)
    {
        TargetOffset = targetOffset;
        AddChild(condition);
    }

    public IrExpression Condition => (IrExpression)Children[0];
    public int TargetOffset { get; }

    public override string Describe() => $"ConditionalBranch IL_{TargetOffset:X4}";
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

public enum ComparisonKind { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }

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
public sealed class Coalesce : IrExpression
{
    public Coalesce(IrExpression left, IrExpression right)
    {
        AddChild(left);
        AddChild(right);
    }

    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];
    public override TypeRef? ResultType => Left.ResultType ?? Right.ResultType;

    public override string Describe() => "Coalesce";
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
/// A raised null-conditional member access — <c>target?.Member</c>. The single
/// child is the member access (a <see cref="Call"/>, <see cref="LoadProperty"/>,
/// or <see cref="LoadField"/>) whose receiver IS the <c>?.</c> target; the
/// printer prints that receiver, then <c>?</c>, then the member suffix. The
/// NullConditionalPass raises this only from the <c>recv is not null ? recv.M :
/// null</c> shape, where the literal-null false arm proves the member result is
/// a reference type — so the access carries the member's own result type with no
/// Nullable wrapping, and the lowered receiver spill collapses into the target.
/// </summary>
public sealed class NullConditional : IrExpression
{
    public NullConditional(IrExpression member) => AddChild(member);

    /// <summary>The member access whose receiver is the <c>?.</c> target.</summary>
    public IrExpression Member => (IrExpression)Children[0];

    public override TypeRef? ResultType => Member.ResultType;

    public override string Describe() => "NullConditional";
}

/// <summary>A raised ternary: condition selects between two values (the slot-diamond shape).</summary>
public sealed class Conditional : IrExpression
{
    public Conditional(IrExpression condition, IrExpression whenTrue, IrExpression whenFalse)
    {
        AddChild(condition);
        AddChild(whenTrue);
        AddChild(whenFalse);
    }

    public IrExpression Condition => (IrExpression)Children[0];
    public IrExpression WhenTrue => (IrExpression)Children[1];
    public IrExpression WhenFalse => (IrExpression)Children[2];

    /// <summary>
    /// The merged slot type the importer computed for the join the two arms
    /// feed (a genuine common supertype of both arms). When set it is the
    /// honest result type — the bare <c>WhenTrue ?? WhenFalse</c> fallback
    /// would otherwise lie whenever the arms carry unequal reference types
    /// (e.g. <c>cond ? new DirectoryInfo() : new FileInfo()</c> must type as
    /// <c>FileSystemInfo</c>, not <c>DirectoryInfo</c>).
    /// </summary>
    public TypeRef? MergedType { get; set; }

    public override TypeRef? ResultType => MergedType ?? WhenTrue.ResultType ?? WhenFalse.ResultType;

    public override string Describe() => "Conditional";
}

public sealed class LogicalNot : IrExpression
{
    public LogicalNot(IrExpression operand) => AddChild(operand);

    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");

    public override string Describe() => "LogicalNot";
}

public enum UnaryKind { Negate, BitwiseNot }

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
/// A recovered C# <c>await</c> expression, produced by
/// <see cref="AwaitRecoveryPass"/> from a runtime-async (async v2)
/// <c>System.Runtime.CompilerServices.AsyncHelpers.Await</c> call. The single
/// child is the awaited operand; <see cref="ResultType"/> is the awaited result
/// type (the helper call's return type — <c>void</c> for the non-generic form).
/// Runtime async lowers <c>await x</c> directly to this call rather than to a
/// state machine, so recovery is a call-site rewrite with no MoveNext to unwind.
/// </summary>
public sealed class AwaitExpression : IrExpression
{
    public AwaitExpression(IrExpression operand, TypeRef? resultType)
    {
        AddChild(operand);
        ResultType = resultType;
    }

    public IrExpression Operand => (IrExpression)Children[0];
    public override TypeRef? ResultType { get; }

    public override string Describe() => "AwaitExpression";
}
/// <c>--x</c>, <c>x--</c>. The compiler lowers these (and compound array
/// element stores like <c>a[--i] = ...</c>) to a <c>dup</c> that the importer
/// raises into a single-use stack slot capturing the value beside the matching
/// local update; <see cref="IncrementDecrementPass"/> folds that idiom back so
/// the value renders as the operator the source spelled — and recompiles to the
/// same <c>dup</c> rather than spilling to extra locals.
/// </summary>
public sealed class IncrementDecrement : IrExpression
{
    public IncrementDecrement(IrExpression target, bool isIncrement, bool isPrefix)
    {
        IsIncrement = isIncrement;
        IsPrefix = isPrefix;
        AddChild(target);
    }

    public bool IsIncrement { get; }
    public bool IsPrefix { get; }
    /// <summary>The incremented place — a local or argument load.</summary>
    public IrExpression Target => (IrExpression)Children[0];
    public override TypeRef? ResultType => Target.ResultType;

    public override string Describe()
        => $"{(IsPrefix ? "Pre" : "Post")}{(IsIncrement ? "Increment" : "Decrement")}";
}

/// <summary>A numeric conversion (the conv.* family).</summary>
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

/// <summary>An expression evaluated for its side effects (void call, popped value).</summary>
public sealed class ExpressionStatement : IrNode
{
    public ExpressionStatement(IrExpression expression) => AddChild(expression);

    public IrExpression Expression => (IrExpression)Children[0];

    public override string Describe() => "ExpressionStatement";
}

public sealed class LoadArgument : IrExpression
{
    public LoadArgument(int index, string name, TypeRef type)
    {
        Index = index;
        Name = name;
        Type = type;
    }

    public int Index { get; }
    public string Name { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => Type;

    public override string Describe() => $"LoadArgument {Index} ({Type.ToDisplayString()} {Name})";
}

public sealed class StoreArgument : IrNode
{
    public StoreArgument(int index, string name, TypeRef type, IrExpression value)
    {
        Index = index;
        Name = name;
        Type = type;
        AddChild(value);
    }

    public int Index { get; }
    public string Name { get; }
    public TypeRef Type { get; }
    public IrExpression Value => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"StoreArgument {Index} ({Type.ToDisplayString()} {Name})";
}

public sealed class LoadLocal : IrExpression
{
    public LoadLocal(int index, TypeRef type)
    {
        Index = index;
        Type = type;
    }

    public int Index { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => Type;

    public override string Describe() => $"LoadLocal {Index} ({Type.ToDisplayString()})";
}

public sealed class StoreLocal : IrNode
{
    public StoreLocal(int index, TypeRef type, IrExpression value)
    {
        Index = index;
        Type = type;
        AddChild(value);
    }

    public int Index { get; }
    public TypeRef Type { get; }
    public IrExpression Value => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"StoreLocal {Index} ({Type.ToDisplayString()})";
}

public sealed class Constant : IrExpression
{
    public Constant(object? value, TypeRef type)
    {
        Value = value;
        Type = type;
    }

    public object? Value { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => Type;

    public override string Describe() => Value switch
    {
        null => "Constant null",
        string s => $"Constant \"{s}\" (string)",
        _ => $"Constant {Value} ({Type.ToDisplayString()})",
    };
}

public enum BinaryKind { Add, Subtract, Multiply, Divide, Remainder, And, Or, Xor, ShiftLeft, ShiftRight }

public sealed class Binary : IrExpression
{
    public Binary(BinaryKind kind, bool isChecked, bool isUnsigned, IrExpression left, IrExpression right)
    {
        Kind = kind;
        IsChecked = isChecked;
        IsUnsigned = isUnsigned;
        AddChild(left);
        AddChild(right);
    }

    public BinaryKind Kind { get; }
    public bool IsChecked { get; }
    public bool IsUnsigned { get; }
    public IrExpression Left => (IrExpression)Children[0];
    public IrExpression Right => (IrExpression)Children[1];

    /// <summary>ECMA-335 III.1.5 binary numeric promotion: the wider operand wins (see <see cref="TypeFamilies.BinaryResult"/>).</summary>
    public override TypeRef? ResultType => TypeFamilies.BinaryResult(Left.ResultType, Right.ResultType);

    public override string Describe()
        => $"Binary.{Kind}{(IsChecked ? " checked" : "")}{(IsUnsigned ? " unsigned" : "")}";
}

public sealed class Call : IrExpression
{
    public Call(MethodRef callee, bool isVirtual, IEnumerable<IrExpression> arguments)
    {
        Callee = callee;
        IsVirtual = isVirtual;
        foreach (var argument in arguments)
            AddChild(argument);
    }

    public MethodRef Callee { get; }
    public bool IsVirtual { get; }

    /// <summary>The constrained. prefix type for constrained callvirt; null otherwise.</summary>
    public TypeRef? ConstrainedTo { get; init; }
    /// <summary>Arguments including the receiver for instance calls.</summary>
    public IReadOnlyList<IrExpression> Arguments => Children.Cast<IrExpression>().ToList();

    /// <summary>
    /// A by-ref argument is forwarded against an unknown call-site ref-kind —
    /// the printer spells a keyword it cannot verify. Lowers fidelity. See
    /// <see cref="MethodRef.HasUnverifiableByRefArgument"/>.
    /// </summary>
    public bool HasUnverifiedByRefArgument
        => Callee.HasUnverifiableByRefArgument(Callee.HasThis ? [.. Arguments.Skip(1)] : Arguments);

    public override TypeRef? ResultType => Callee.ReturnType;
    public override IEnumerable<TypeRef> DirectTypes
        => Callee.ParameterTypes.Concat(Callee.TypeArguments).Append(Callee.DeclaringType).Append(Callee.ReturnType)
            .Concat(ConstrainedTo is null ? [] : [ConstrainedTo]);

    public override string Describe()
        => $"{(IsVirtual ? "CallVirt" : "Call")} {Callee.DeclaringType.ToDisplayString()}.{Callee.Name}";
}

/// <summary>
/// <c>calli</c>: an indirect call through a function-pointer value. The
/// pointer (a <c>delegate*&lt;...&gt;</c>-typed expression) is the first child;
/// the arguments follow. The standalone call-site signature supplies the
/// return and parameter types, so the node is self-describing without the
/// pointer's own type. Renders as a C# function-pointer invocation
/// <c>pointer(args)</c>.
/// </summary>
public sealed class CallIndirect : IrExpression
{
    public CallIndirect(IrExpression pointer, IEnumerable<IrExpression> arguments, TypeRef returnType, ImmutableArray<TypeRef> parameterTypes)
    {
        AddChild(pointer);
        foreach (var argument in arguments)
            AddChild(argument);
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }

    public TypeRef ReturnType { get; }
    public ImmutableArray<TypeRef> ParameterTypes { get; }

    /// <summary>The function-pointer value being invoked.</summary>
    public IrExpression Pointer => (IrExpression)Children[0];
    /// <summary>Call arguments (the function pointer's own parameters, receiver included when the signature carries one).</summary>
    public IReadOnlyList<IrExpression> Arguments => Children.Skip(1).Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => ReturnType;
    public override IEnumerable<TypeRef> DirectTypes => ParameterTypes.Append(ReturnType);

    public override string Describe() => $"CallIndirect {ReturnType.ToDisplayString()}";
}

/// <summary>Object construction: <c>newobj</c> with the constructor's MethodRef (receiver excluded from arguments).</summary>
public sealed class NewObject : IrExpression
{
    public NewObject(MethodRef constructor, IEnumerable<IrExpression> arguments)
    {
        Constructor = constructor;
        foreach (var argument in arguments)
            AddChild(argument);
    }

    public MethodRef Constructor { get; }
    public IReadOnlyList<IrExpression> Arguments => Children.Cast<IrExpression>().ToList();

    /// <summary>
    /// For a constructor of a compiler-generated anonymous type
    /// (<c>&lt;&gt;f__AnonymousType*</c>), the property names in argument order —
    /// the metadata the importer captures so <see cref="AnonymousObjectPass"/> can
    /// raise the call to a <c>new { Name = value, ... }</c> literal. Empty for
    /// every ordinary constructor; a pass keys off non-emptiness.
    /// </summary>
    public ImmutableArray<string> AnonymousPropertyNames { get; init; } = [];

    /// <summary>
    /// A by-ref constructor argument forwarded against an unknown call-site
    /// ref-kind. Lowers fidelity. See
    /// <see cref="MethodRef.HasUnverifiableByRefArgument"/>.
    /// </summary>
    public bool HasUnverifiedByRefArgument => Constructor.HasUnverifiableByRefArgument(Arguments);

    public override TypeRef? ResultType => Constructor.DeclaringType;
    public override IEnumerable<TypeRef> DirectTypes
        => Constructor.ParameterTypes.Append(Constructor.DeclaringType);

    public override string Describe() => $"NewObject {Constructor.DeclaringType.ToDisplayString()}";
}

/// <summary>
/// A raised C# anonymous-object creation — <c>new { a = x, b = y }</c> — produced
/// by <see cref="AnonymousObjectPass"/> from the compiler's lowering of an
/// anonymous-type construction to <c>new &lt;&gt;f__AnonymousType0&lt;...&gt;(x, y)</c>.
/// The child slots are the member value expressions; <see cref="PropertyNames"/>
/// is the parallel name list (argument order), carried as metadata rather than
/// child nodes since names are not expressions.
/// </summary>
public sealed class AnonymousObject : IrExpression
{
    public AnonymousObject(TypeRef type, ImmutableArray<string> propertyNames, IEnumerable<IrExpression> values)
    {
        Type = type;
        PropertyNames = propertyNames;
        foreach (var value in values)
            AddChild(value);
    }

    public TypeRef Type { get; }
    public ImmutableArray<string> PropertyNames { get; }
    public IReadOnlyList<IrExpression> Values => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => Type;
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"AnonymousObject ({Children.Count} properties)";
}

/// <summary>One segment in a raised interpolated string: either literal text or a formatted-expression child by index.</summary>
public sealed record InterpolatedStringPart(string? Literal, int ExpressionIndex)
{
    public static InterpolatedStringPart LiteralText(string text) => new(text, -1);
    public static InterpolatedStringPart FormattedValue(int expressionIndex) => new(null, expressionIndex);
    public bool IsLiteral => Literal is not null;
}

/// <summary>
/// A raised C# interpolated string. Produced by
/// <see cref="StringInterpolationPass"/> from csc's straight-line
/// <c>DefaultInterpolatedStringHandler</c> lowering.
/// </summary>
public sealed class InterpolatedStringExpression : IrExpression
{
    public InterpolatedStringExpression(IEnumerable<InterpolatedStringPart> parts, IEnumerable<IrExpression> formattedValues)
    {
        Parts = [.. parts];
        foreach (var value in formattedValues)
            AddChild(value);
    }

    public ImmutableArray<InterpolatedStringPart> Parts { get; }
    public IReadOnlyList<IrExpression> FormattedValues => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "String");

    public override string Describe() => $"InterpolatedString ({Parts.Length} parts)";
}

/// <summary>
/// A raised C# tuple literal, produced by <see cref="TupleCreationPass"/> from
/// a direct <c>System.ValueTuple&lt;...&gt;</c> constructor call. The binary only
/// records the element values and the underlying ValueTuple type; tuple element
/// names are a signature/custom-attribute concern and are not recovered here.
/// </summary>
public sealed class TupleExpression : IrExpression
{
    public TupleExpression(TypeRef tupleType, IEnumerable<IrExpression> elements)
    {
        TupleType = tupleType;
        foreach (var element in elements)
            AddChild(element);
    }

    public TypeRef TupleType { get; }
    public IReadOnlyList<IrExpression> Elements => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => TupleType;
    public override IEnumerable<TypeRef> DirectTypes => [TupleType];

    public override string Describe() => $"TupleExpression ({Children.Count} elements)";
}

/// <summary>
/// A raised local tuple deconstruction declaration, produced by
/// <see cref="DeconstructionAssignmentPass"/> from the compiler's
/// <c>ValueTuple</c> receiver spill followed by sequential <c>ItemN</c> stores.
/// </summary>
public sealed class DeconstructionAssignment : IrNode
{
    public DeconstructionAssignment(ImmutableArray<int> localIndices, ImmutableArray<TypeRef> localTypes, IrExpression source)
    {
        LocalIndices = localIndices;
        LocalTypes = localTypes;
        AddChild(source);
    }

    public ImmutableArray<int> LocalIndices { get; }
    public ImmutableArray<TypeRef> LocalTypes { get; }
    public IrExpression Source => (IrExpression)Children[0];
    public override IEnumerable<TypeRef> DirectTypes => LocalTypes;

    public override string Describe() => $"DeconstructionAssignment ({LocalIndices.Length} locals)";
}

/// <summary>
/// A raised C# object or collection initializer, produced by
/// <see cref="ObjectInitializerPass"/> from the compiler's lowering of
/// <c>new T { X = a, ... }</c> / <c>new C { e0, e1, ... }</c> — a constructor
/// call whose result is threaded through a dup chain, mutated by a run of member
/// stores (object form) or <c>Add</c> calls (collection form), then consumed
/// once. Child 0 is the <see cref="NewObject"/> creation; the remaining children
/// are the entry values, parallel to <see cref="Members"/> (a member name for the
/// object form, <c>null</c> for a collection element).
/// </summary>
public sealed class ObjectInitializerExpression : IrExpression
{
    public ObjectInitializerExpression(NewObject creation, bool isCollection, IEnumerable<(string? Member, IrExpression Value)> entries)
    {
        IsCollection = isCollection;
        AddChild(creation);
        var members = ImmutableArray.CreateBuilder<string?>();
        foreach (var (member, value) in entries)
        {
            members.Add(member);
            AddChild(value);
        }
        Members = members.ToImmutable();
    }

    /// <summary>Collection-initializer (<c>{ e0, e1 }</c> via <c>Add</c>) vs object-initializer (<c>{ X = a }</c> via member stores).</summary>
    public bool IsCollection { get; }

    /// <summary>The <c>new T(...)</c> creation the initializer decorates.</summary>
    public NewObject Creation => (NewObject)Children[0];

    /// <summary>Target member name per entry value, parallel to <see cref="Values"/>; <c>null</c> for a collection element.</summary>
    public ImmutableArray<string?> Members { get; }

    /// <summary>The entry values, in source order.</summary>
    public IReadOnlyList<IrExpression> Values => Children.Skip(1).Cast<IrExpression>().ToList();

    public override TypeRef? ResultType => Creation.ResultType;

    public override string Describe()
        => $"ObjectInitializer {Creation.Constructor.DeclaringType.ToDisplayString()} ({Members.Length} {(IsCollection ? "elements" : "members")})";
}

/// <summary>
/// <c>ldftn</c>/<c>ldvirtftn</c>: a method's entry-point address as a native
/// int. C# has no spelling for a bare function-pointer load, so this only
/// reaches print as a comment; the dominant case — feeding a delegate
/// constructor — is raised to <see cref="DelegateCreation"/> by a pass.
/// </summary>
public sealed class LoadFunctionPointer : IrExpression
{
    public LoadFunctionPointer(MethodRef method, bool isVirtual, IrExpression? instance)
    {
        Method = method;
        IsVirtual = isVirtual;
        if (instance is not null)
            AddChild(instance);
    }

    public MethodRef Method { get; }
    public bool IsVirtual { get; }

    /// <summary>The receiver dispatched on for ldvirtftn; null for ldftn.</summary>
    public IrExpression? Instance => Children.Count > 0 ? (IrExpression)Children[0] : null;
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "IntPtr");
    public override IEnumerable<TypeRef> DirectTypes
        => Method.ParameterTypes.Append(Method.DeclaringType).Append(Method.ReturnType);

    public override string Describe()
        => $"{(IsVirtual ? "LoadVirtualFunctionPointer" : "LoadFunctionPointer")} {Method.DeclaringType.ToDisplayString()}.{Method.Name}";
}

/// <summary>
/// <c>&amp;Method</c> — the address of a static method as a function pointer.
/// The renderable form of a static <c>ldftn</c> that did not feed a delegate
/// constructor: it feeds a <c>calli</c>, a native-callback argument, or a
/// <c>delegate*</c>-typed field. Raised from a surviving
/// <see cref="LoadFunctionPointer"/> by <see cref="MethodAddressPass"/>; its
/// result type is the managed function-pointer type of the method's signature.
/// </summary>
public sealed class AddressOfMethod : IrExpression
{
    public AddressOfMethod(MethodRef method) => Method = method;

    public MethodRef Method { get; }
    public override TypeRef? ResultType
        => TypeRef.FunctionPointer(Method.ReturnType, Method.ParameterTypes, "");
    public override IEnumerable<TypeRef> DirectTypes
        => Method.ParameterTypes.Append(Method.DeclaringType).Append(Method.ReturnType).Concat(Method.TypeArguments);

    public override string Describe()
        => $"AddressOfMethod {Method.DeclaringType.ToDisplayString()}.{Method.Name}";
}

/// <summary>
/// A delegate instance from a method group — the inverse of the compiler's
/// <c>ldftn; newobj DelegateType::.ctor(object, native int)</c> lowering. The
/// target is the receiver object (a null constant for a static method group).
/// </summary>
public sealed class DelegateCreation : IrExpression
{
    public DelegateCreation(TypeRef delegateType, MethodRef method, bool isVirtual, IrExpression target)
    {
        DelegateType = delegateType;
        Method = method;
        IsVirtual = isVirtual;
        AddChild(target);
    }

    public TypeRef DelegateType { get; }
    public MethodRef Method { get; }
    public bool IsVirtual { get; }
    public IrExpression Target => (IrExpression)Children[0];
    public override TypeRef? ResultType => DelegateType;
    public override IEnumerable<TypeRef> DirectTypes
        => Method.ParameterTypes.Append(Method.DeclaringType).Append(Method.ReturnType).Append(DelegateType);

    public override string Describe()
        => $"DelegateCreation {DelegateType.ToDisplayString()} <- {Method.DeclaringType.ToDisplayString()}.{Method.Name}";
}

/// <summary>
/// A lambda expression <c>(params) =&gt; body</c> recovered from the compiler's
/// closure lowering — the inverse of the delegate-over-synthesized-method shape
/// ClosureConversion emits. Carries the lambda's parameters and its raised body
/// (the synthesized method's block container, imported and run through the
/// pipeline). The result type is the delegate type the lambda is converted to.
///
/// <para>Non-capturing, zero-local bodies only for now: the body reads no
/// display-class state and declares no locals, so it prints inside the outer
/// function's scope without a local context of its own (arguments are
/// self-naming on the node). A capturing body, or one with its own locals, needs
/// the printer to switch scope when it descends here — a later increment.</para>
/// </summary>
public sealed class Lambda : IrExpression
{
    public Lambda(TypeRef delegateType, ImmutableArray<Parameter> parameters, BlockContainer body)
    {
        DelegateType = delegateType;
        Parameters = parameters;
        AddChild(body);
    }

    public TypeRef DelegateType { get; }
    public ImmutableArray<Parameter> Parameters { get; }
    public BlockContainer Body => (BlockContainer)Children[0];
    public override TypeRef? ResultType => DelegateType;
    public override IEnumerable<TypeRef> DirectTypes
        => Parameters.Select(p => p.Type).Append(DelegateType);

    /// <summary>
    /// The single returned expression when the body is one block ending in a
    /// bare <c>return expr;</c> — the expression-bodied form <c>p =&gt; expr</c>.
    /// Null when the body needs the block form <c>p =&gt; { ... }</c>.
    /// </summary>
    public IrExpression? ExpressionBody
        => Body.Blocks is [{ Children: [Return { Value: { } value }] }] ? value : null;

    public override string Describe()
        => $"Lambda {DelegateType.ToDisplayString()} ({Parameters.Length} params)";
}

public sealed class Throw : IrNode
{
    public Throw(IrExpression value) => AddChild(value);

    public IrExpression Value => (IrExpression)Children[0];

    public override string Describe() => "Throw";
}

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
    public IrExpression? Instance => Children.Count > 0 ? (IrExpression)Children[0] : null;
    public override TypeRef? ResultType => Field.Type;
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    public override string Describe()
        => $"LoadField {Field.DeclaringType.ToDisplayString()}.{Field.Name} ({Field.Type.ToDisplayString()})";
}

public sealed class StoreField : IrNode
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
    public IrExpression Value => (IrExpression)Children[HasInstance ? 1 : 0];
    public override IEnumerable<TypeRef> DirectTypes => [Field.DeclaringType, Field.Type];

    public override string Describe() => $"StoreField {Field.DeclaringType.ToDisplayString()}.{Field.Name}";
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
/// A synthetic variable carrying an evaluation-stack value across a block
/// boundary (ternaries, short-circuit values) or materializing a dup.
/// Edge slots are position-indexed so every predecessor of a join stores to
/// the same slot; dup slots allocate from <see cref="DupSlotBase"/> up.
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

/// <summary>
/// A raised C# range-indexer access (<c>receiver[start..end]</c>) — the inverse
/// of the compiler's range-slice lowering (<c>RuntimeHelpers.GetSubArray</c> for
/// arrays). The result type is the slice's type (the receiver's array type), not
/// an element type.
/// </summary>
public sealed class SliceExpression : IrExpression
{
    public SliceExpression(IrExpression receiver, RangeExpression range, TypeRef? resultType)
    {
        AddChild(receiver);
        AddChild(range);
        ResultType = resultType;
    }

    public IrExpression Receiver => (IrExpression)Children[0];
    public RangeExpression Range => (RangeExpression)Children[1];
    public override TypeRef? ResultType { get; }

    public override string Describe() => "SliceExpression";
}

/// <summary>A raised C# index-from-end operand (<c>^n</c>), used inside array/string element access.</summary>
public sealed class IndexFromEnd : IrExpression
{
    public IndexFromEnd(IrExpression offset) => AddChild(offset);

    public IrExpression Offset => (IrExpression)Children[0];
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Index");

    public override string Describe() => "IndexFromEnd";
}

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

    /// <summary>The value being tested.</summary>
    public IrExpression Value => (IrExpression)Children[0];

    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Boolean");
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"IsPattern {Type.ToDisplayString()} V_{LocalIndex}";
}

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
public sealed class StackAllocArray : IrExpression
{
    readonly TypeRef? _resultType;

    public StackAllocArray(TypeRef elementType, IrExpression count, TypeRef? resultType)
    {
        ElementType = elementType;
        _resultType = resultType;
        AddChild(count);
    }

    public TypeRef ElementType { get; }
    public IrExpression Count => (IrExpression)Children[0];
    public override TypeRef? ResultType => _resultType;
    public override IEnumerable<TypeRef> DirectTypes => [ElementType];

    public override string Describe() => $"StackAllocArray {ElementType.ToDisplayString()}[]";
}

/// <summary>The raised typeof(T): GetTypeFromHandle over a type token, folded.</summary>
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
/// A C# 12 collection expression — <c>[e0, e1, ...]</c> in a
/// <see cref="System.ReadOnlySpan{T}"/> context — raised from the compiler's
/// inline-array lowering of a span collection expression with non-constant
/// elements: a <c>&lt;&gt;y__InlineArrayN&lt;T&gt;</c> temporary default-initialized,
/// each slot stored through
/// <c>&lt;PrivateImplementationDetails&gt;.InlineArrayElementRef</c>, then exposed
/// as a span by <c>&lt;PrivateImplementationDetails&gt;.InlineArrayAsReadOnlySpan</c>.
/// The elements are the per-slot stored values, in index order. Its result type
/// is the <c>ReadOnlySpan&lt;T&gt;</c> the AsReadOnlySpan call produced, so
/// replacing that call leaves the surrounding expression's type unchanged; the
/// compiler re-lowers <c>[...]</c> to the same inline-array sequence.
/// </summary>
public sealed class CollectionExpression : IrExpression
{
    public CollectionExpression(TypeRef elementType, TypeRef spanType, IEnumerable<IrExpression> elements)
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

    public override string Describe() => $"CollectionExpression {ElementType.ToDisplayString()}[{Children.Count}]";
}

/// <summary>ldtoken: a runtime handle for a type, method, or field (the typeof/ldtoken patterns raise from this).</summary>
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
public sealed class StoreProperty : IrNode
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
    public IrExpression Value => (IrExpression)Children[^1];
    public override IEnumerable<TypeRef> DirectTypes
        => Accessor.ParameterTypes.Append(Accessor.DeclaringType);

    public override string Describe() => $"StoreProperty {Accessor.DeclaringType.ToDisplayString()}.{PropertyName}";
}

/// <summary>The exception value the CLR pushes on entry to a catch or filter handler.</summary>
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

public sealed class LoadArgumentAddress : IrExpression
{
    public LoadArgumentAddress(int index, string name, TypeRef type)
    {
        Index = index;
        Name = name;
        Type = type;
    }

    public int Index { get; }
    public string Name { get; }
    public TypeRef Type { get; }
    public override TypeRef? ResultType => TypeRef.ByRef(Type);

    public override string Describe() => $"LoadArgumentAddress {Index} ({Type.ToDisplayString()} {Name})";
}

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

    public override string Describe() => $"LoadFieldAddress {Field.DeclaringType.ToDisplayString()}.{Field.Name}";
}

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

/// <summary>Load through an address (ldobj and the ldind.* family). A null type means the opcode does not encode one (ldind.ref).</summary>
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
        => Type ?? (Address.ResultType is { Kind: TypeRefKind.ByRef or TypeRefKind.Pointer } indirect ? indirect.ElementType : null);
    public override IEnumerable<TypeRef> DirectTypes => Type is null ? [] : [Type];

    public override string Describe() => $"LoadIndirect {ResultType?.ToDisplayString() ?? "?"}{(IsVolatile ? " volatile" : "")}";
}

public sealed class StoreIndirect : IrNode
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
    public IrExpression Value => (IrExpression)Children[1];
    public override IEnumerable<TypeRef> DirectTypes => Type is null ? [] : [Type];

    public override string Describe() => $"StoreIndirect {Type?.ToDisplayString() ?? "?"}{(IsVolatile ? " volatile" : "")}";
}

/// <summary>initobj: default-initialize the storage at an address.</summary>
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
    public IrExpression Array => (IrExpression)Children[0];
    public IrExpression Index => (IrExpression)Children[1];
    public IrExpression Value => (IrExpression)Children[2];
    public override IEnumerable<TypeRef> DirectTypes => ElementType is null ? [] : [ElementType];

    public override string Describe() => $"StoreElement {ElementType?.ToDisplayString() ?? "?"}";
}

public sealed class SizeOf : IrExpression
{
    public SizeOf(TypeRef type) => Type = type;

    public TypeRef Type { get; }
    public override TypeRef? ResultType => TypeRef.CoreLib("System", "Int32");
    public override IEnumerable<TypeRef> DirectTypes => [Type];

    public override string Describe() => $"SizeOf {Type.ToDisplayString()}";
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
