namespace ILInspector.Decompiler.Tests;

// #3459: an object initializer used as a CALL ARGUMENT, where the enclosing call's
// receiver (a non-volatile instance field off `this`) and a `default` struct
// argument are the compiler's pure spills sitting on the stack beneath the dup
// chain — the Azure.Data.Tables `TableClient.Create` shape. The importer materializes
// them as `S = _rest;` and `V = default;` around the member store, which #3336's
// member-value fold could not cross. The pass now skips those reorder-safe spills,
// folds the construction into the call-argument position, and inlines each single-use
// spill back into its operand, restoring the canonical stack-only spelling
// `_rest.Create(new CallArgTarget { Name = Label }, default(CallArgFlag?), _options)`
// — which recompiles byte-for-byte to the original IL. These related top-level types
// stay together as one compiler-fixture group.
public sealed class CallArgTarget
{
    public string? Name { get; set; }
}

public enum CallArgFlag { A, B }

public sealed class CallArgRest
{
    public int Create(CallArgTarget target, CallArgFlag? flag, object options) => target.Name?.Length ?? 0;
}

public sealed class CallArgClient
{
    readonly CallArgRest _rest = new();
    readonly object _options = new();
    volatile CallArgRest _volatileRest = new();
    public string Label = "n";

    // Foldable: the receiver `_rest` (pure field-off-this) and the `default` struct
    // argument are reorder-safe spills, so both are inlined into the folded call.
    public int CreateViaField()
        => _rest.Create(new CallArgTarget { Name = Label }, default, _options);

    // Close negative for the inlining guard: a VOLATILE field receiver must NOT be
    // inlined (reordering a volatile access is observable). The initializer still
    // folds — the receiver ran before the `newobj`, an offset-guarded skip — but the
    // volatile spill is left in place rather than hoisted into the call receiver.
    public int CreateViaVolatileField()
        => _volatileRest.Create(new CallArgTarget { Name = Label }, default, _options);
}
