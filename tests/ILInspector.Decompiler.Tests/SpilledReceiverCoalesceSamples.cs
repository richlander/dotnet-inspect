namespace ILInspector.Decompiler.Tests;

// A reference-type field initializer `field = arg ?? new()` evaluates the
// receiver `this` first, then the `??` branch; both must survive the branch
// join, so the importer spills them to stack slots
// (S_0 = this; S_1 = arg ?? new(); S_0.field = S_1). Reference-type receiver
// purity lets ExpressionInliningPass collapse both temporaries back into
// `this.field = arg ?? new()`. In a primary constructor the implicit base
// object..ctor is emitted AFTER the field inits, so leaving the temps spilled
// also left that base call an unrepresentable `/* Unsupported ... call .ctor */`
// residual (the prologue was no longer all instance-field stores); collapsing
// the temps restores the clean prologue that elides the base call.
public sealed class SpilledCoalesceOptions;

public sealed class SpilledCoalesceField
{
    private readonly SpilledCoalesceOptions _options;

    public SpilledCoalesceField(SpilledCoalesceOptions? options)
    {
        _options = options ?? new SpilledCoalesceOptions();
    }
}

public sealed class SpilledCoalescePrimaryField(SpilledCoalesceOptions? options = null)
{
    private readonly SpilledCoalesceOptions _options = options ?? new SpilledCoalesceOptions();

    public SpilledCoalesceOptions Options => _options;
}
