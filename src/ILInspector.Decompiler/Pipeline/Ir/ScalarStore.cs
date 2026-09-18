namespace ILInspector.Decompiler.Pipeline;

public enum ScalarUpdateKind
{
    Binary,
    Increment,
    Decrement,
}

/// <summary>A store retaining its read/compute/write tree and a final scalar self-update decision.</summary>
public abstract class ScalarStore : IrNode
{
    public abstract IrExpression Value { get; }
    public ScalarUpdateKind? UpdateKind { get; internal set; }
    protected string UpdateDescription => UpdateKind is { } kind ? $" ({kind} self-update)" : "";
}
