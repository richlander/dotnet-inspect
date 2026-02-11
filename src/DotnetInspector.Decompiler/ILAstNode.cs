using System.Reflection.Metadata;
using System.Text;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Base class for all nodes in the IL abstract syntax tree.
/// The ILAst replaces stack-based operations with explicit variable
/// assignments and nested expression trees.
/// </summary>
public abstract class ILAstNode
{
    /// <summary>The IL offset where this node originates (-1 if synthetic).</summary>
    public int Offset { get; init; } = -1;

    public abstract void WriteTo(StringBuilder sb, int indent);

    public override string ToString()
    {
        var sb = new StringBuilder();
        WriteTo(sb, 0);
        return sb.ToString();
    }

    protected static void WriteIndent(StringBuilder sb, int indent)
        => sb.Append(' ', indent * 2);
}

/// <summary>
/// An expression that produces a value (or void). Core building block of the ILAst.
/// </summary>
public sealed class ILAstExpression : ILAstNode
{
    /// <summary>The IL opcode (or pseudo-opcode) this expression represents.</summary>
    public ILOpCode OpCode { get; init; }

    /// <summary>
    /// Resolved operand string (method name, field name, type name, literal, branch target).
    /// Null for opcodes with no operand or for implicit operands.
    /// </summary>
    public string? Operand { get; init; }

    /// <summary>The type this expression produces on the stack.</summary>
    public StackValue ResultType { get; init; }

    /// <summary>
    /// Sub-expressions consumed by this operation (e.g., arguments to a call,
    /// the object for a field load, operands of an add). Empty for leaf nodes
    /// like constants and loads.
    /// </summary>
    public List<ILAstExpression> Arguments { get; } = [];

    public override void WriteTo(StringBuilder sb, int indent)
    {
        sb.Append(FormatOpCode(OpCode));
        if (Operand is not null)
            sb.Append(' ').Append(Operand);
        if (Arguments.Count > 0)
        {
            sb.Append('(');
            for (int i = 0; i < Arguments.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                Arguments[i].WriteTo(sb, 0);
            }
            sb.Append(')');
        }
    }

    static string FormatOpCode(ILOpCode opcode) => opcode switch
    {
        // Use short readable names for common opcodes
        ILOpCode.Ldarg_0 => "ldarg.0",
        ILOpCode.Ldarg_1 => "ldarg.1",
        ILOpCode.Ldarg_2 => "ldarg.2",
        ILOpCode.Ldarg_3 => "ldarg.3",
        ILOpCode.Ldloc_0 => "ldloc.0",
        ILOpCode.Ldloc_1 => "ldloc.1",
        ILOpCode.Ldloc_2 => "ldloc.2",
        ILOpCode.Ldloc_3 => "ldloc.3",
        _ => opcode.ToString().ToLowerInvariant().Replace('_', '.')
    };
}

/// <summary>
/// Assignment: variable = expression.
/// Represents a stack push that has been materialized as a named variable.
/// </summary>
public sealed class ILAstAssignment : ILAstNode
{
    public ILVariable Variable { get; init; } = null!;
    public ILAstExpression Value { get; init; } = null!;

    /// <summary>
    /// If true, this assignment's variable was inlined into its single use site
    /// and this node can be skipped during emission.
    /// </summary>
    public bool IsInlined { get; set; }

    public override void WriteTo(StringBuilder sb, int indent)
    {
        WriteIndent(sb, indent);
        sb.Append(Variable.Name).Append(" = ");
        Value.WriteTo(sb, 0);
    }
}

/// <summary>
/// A statement that has no result value (store to local, return, branch, etc.).
/// Wraps an expression with void semantics.
/// </summary>
public sealed class ILAstStatement : ILAstNode
{
    public ILAstExpression Expression { get; init; } = null!;

    public override void WriteTo(StringBuilder sb, int indent)
    {
        WriteIndent(sb, indent);
        Expression.WriteTo(sb, 0);
    }
}

/// <summary>
/// A sequential block of ILAst nodes (assignments + statements),
/// corresponding to one or more basic blocks.
/// </summary>
public sealed class ILAstBlock : ILAstNode
{
    /// <summary>Label for this block (e.g., "Block_0", "try", "catch").</summary>
    public string? Label { get; init; }

    /// <summary>Ordered list of nodes in this block.</summary>
    public List<ILAstNode> Nodes { get; } = [];

    public override void WriteTo(StringBuilder sb, int indent)
    {
        if (Label is not null)
        {
            WriteIndent(sb, indent);
            sb.AppendLine($"{Label}:");
        }
        foreach (var node in Nodes)
        {
            node.WriteTo(sb, indent + (Label is not null ? 1 : 0));
            sb.AppendLine();
        }
    }
}

/// <summary>
/// Root node representing an entire method body as an ILAst.
/// </summary>
public sealed class ILAstMethod : ILAstNode
{
    public List<ILVariable> Parameters { get; } = [];
    public List<ILVariable> Locals { get; } = [];
    public List<ILAstBlock> Blocks { get; } = [];

    public override void WriteTo(StringBuilder sb, int indent)
    {
        foreach (var block in Blocks)
            block.WriteTo(sb, indent);
    }
}
