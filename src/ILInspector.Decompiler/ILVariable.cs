namespace ILInspector.Decompiler;

/// <summary>
/// Classifies the source/purpose of an <see cref="ILVariable"/>.
/// </summary>
public enum ILVariableKind
{
    /// <summary>A method parameter (including implicit 'this').</summary>
    Parameter,
    /// <summary>A local variable declared in the method body.</summary>
    Local,
    /// <summary>A stack slot materialized at a basic block boundary.</summary>
    StackSlot,
    /// <summary>The exception object at a catch/filter handler entry.</summary>
    ExceptionSlot
}

/// <summary>
/// Represents a named variable in the decompiled output. Variables are
/// introduced when stack slots must persist across basic block boundaries,
/// or for method parameters, locals, and exception handler slots.
/// </summary>
public sealed class ILVariable
{
    /// <summary>What kind of variable this represents.</summary>
    public ILVariableKind Kind { get; }

    /// <summary>The stack type category of this variable.</summary>
    public StackValueKind StackType { get; }

    /// <summary>The resolved type name (may be null for unknown types).</summary>
    public string? TypeName { get; }

    /// <summary>
    /// Index in the parameter/local table, or stack slot index at the block boundary.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Optional debug name from PDB local variable info. When set, overrides the generated name.
    /// </summary>
    public string? DebugName { get; init; }

    /// <summary>Generated name (e.g., "P_0", "V_0", "S_0", "E_0"), or debug name when available.</summary>
    public string Name => DebugName ?? _generatedName;

    readonly string _generatedName;

    public ILVariable(ILVariableKind kind, StackValueKind stackType, string? typeName, int index)
    {
        Kind = kind;
        StackType = stackType;
        TypeName = typeName;
        Index = index;
        _generatedName = kind switch
        {
            ILVariableKind.Parameter => $"P_{index}",
            ILVariableKind.Local => $"V_{index}",
            ILVariableKind.StackSlot => $"S_{index}",
            ILVariableKind.ExceptionSlot => $"E_{index}",
            _ => $"?_{index}"
        };
    }

    public ILVariable(ILVariableKind kind, StackValue value, int index)
        : this(kind, value.Kind, value.TypeName, index)
    {
    }

    public override string ToString() =>
        TypeName is not null ? $"{Name}: {TypeName}" : $"{Name}: {StackType}";
}
