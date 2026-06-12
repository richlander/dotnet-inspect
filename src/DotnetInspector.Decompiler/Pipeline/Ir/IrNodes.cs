using System.Collections.Immutable;

namespace DotnetInspector.Decompiler.Pipeline;

/// <summary>A materialized method reference — callee identity with symbolic types, no metadata handles.</summary>
public sealed record MethodRef(
    TypeRef DeclaringType,
    string Name,
    TypeRef ReturnType,
    ImmutableArray<TypeRef> ParameterTypes,
    bool HasThis);

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
    public BlockContainer Body => (BlockContainer)Children[0];
    public List<DecompilerDiagnostic> Diagnostics { get; } = [];

    public override IEnumerable<TypeRef> DirectTypes
        => Signature.Parameters.Select(p => p.Type)
            .Append(Signature.ReturnType)
            .Append(DeclaringType)
            .Concat(Locals);

    /// <summary>Computed from the tree, never asserted: any unsupported node or any unsupported type referenced anywhere ⇒ at most <see cref="DecompilationFidelity.Partial"/>.</summary>
    public DecompilationFidelity Fidelity
        => Descendants.Prepend(this).Any(n =>
            n is UnsupportedNode
            || n.DirectTypes.Any(t => t.ContainsUnsupported)
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

    /// <summary>Result typing follows the left operand for the slice; full ECMA-335 binary numeric promotion arrives with the type pass.</summary>
    public override TypeRef? ResultType => Left.ResultType;

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
    /// <summary>Arguments including the receiver for instance calls.</summary>
    public IReadOnlyList<IrExpression> Arguments => Children.Cast<IrExpression>().ToList();
    public override TypeRef? ResultType => Callee.ReturnType;
    public override IEnumerable<TypeRef> DirectTypes
        => Callee.ParameterTypes.Append(Callee.DeclaringType).Append(Callee.ReturnType);

    public override string Describe()
        => $"{(IsVirtual ? "CallVirt" : "Call")} {Callee.DeclaringType.ToDisplayString()}.{Callee.Name}";
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
    public override TypeRef? ResultType => Constructor.DeclaringType;
    public override IEnumerable<TypeRef> DirectTypes
        => Constructor.ParameterTypes.Append(Constructor.DeclaringType);

    public override string Describe() => $"NewObject {Constructor.DeclaringType.ToDisplayString()}";
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
