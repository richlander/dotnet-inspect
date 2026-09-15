namespace ILInspector.Decompiler.Tests;

/// <summary>Base type for constructor-chain fixtures (base(...) targets).</summary>
public class CtorChainBase
{
    public CtorChainBase() { }

    public CtorChainBase(string? message) => Message = message;

    public string? Message { get; }
}

/// <summary>
/// Constructor shapes the chain pass must render: a plain base call, a base
/// call whose argument carries control flow (the spilled-this <c>??</c>
/// shape), a <c>this(...)</c> delegation, and an implicit parameterless base.
/// </summary>
public sealed class CtorChainSamples : CtorChainBase
{
    public CtorChainSamples() { }                       // implicit base()

    public CtorChainSamples(string message) : base(message) { }

    public CtorChainSamples(int code) : base(code > 0 ? "positive" : null) { }

    public CtorChainSamples(string message, bool _) : base(message ?? "default") { }

    public CtorChainSamples(long value) : this(value.ToString()) { }
}

public class NamedCtorArgumentOrderBase
{
    protected NamedCtorArgumentOrderBase(int before, int after)
    {
        Before = before;
        After = after;
    }

    public int Before { get; }
    public int After { get; }
}

public sealed class NamedCtorArgumentOrder : NamedCtorArgumentOrderBase
{
    public NamedCtorArgumentOrder(int value)
        : base(after: Mutate(ref value), before: value)
    {
    }

    static int Mutate(ref int value) => value++;
}
