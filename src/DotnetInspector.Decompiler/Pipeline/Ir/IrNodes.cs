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

/// <summary>The root of one method's IR: signature plus a body block, with diagnostics accumulated during construction and passes.</summary>
public sealed class IrFunction : IrNode
{
    public IrFunction(string name, TypeRef declaringType, MethodSignature signature, ImmutableArray<TypeRef> locals, Block body)
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
    public Block Body => (Block)Children[0];
    public List<DecompilerDiagnostic> Diagnostics { get; } = [];

    /// <summary>Computed from the tree, never asserted: any unsupported node or type ⇒ at most <see cref="DecompilationFidelity.Partial"/>.</summary>
    public DecompilationFidelity Fidelity
        => Descendants.OfType<UnsupportedNode>().Any()
            || Descendants.OfType<IrExpression>().Any(e => e.ResultType?.ContainsUnsupported == true)
            ? DecompilationFidelity.Partial
            : DecompilationFidelity.Full;

    public override string Describe()
        => $"Function {Signature.ReturnType.ToDisplayString()} {Name}({string.Join(", ", Signature.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})";
}

/// <summary>A sequence of statement nodes.</summary>
public sealed class Block : IrNode
{
    public void Add(IrNode statement) => AddChild(statement);

    public override string Describe() => "Block";
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

    public override string Describe()
        => $"{(IsVirtual ? "CallVirt" : "Call")} {Callee.DeclaringType.ToDisplayString()}.{Callee.Name}";
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
