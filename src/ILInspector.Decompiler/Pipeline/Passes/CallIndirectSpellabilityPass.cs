namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Keeps <c>calli</c> invocations honest. <see cref="CallIndirect"/> renders as
/// <c>fp(x)</c>, which is only valid C# when the pointer operand is a spellable,
/// non-instance <c>delegate*</c> whose calling convention matches the call-site
/// signature. The importer discards none of that, but the printer would emit
/// <c>fp(x)</c> for an untyped (<c>nint</c>/<c>IntPtr</c>) operand, an instance
/// function pointer (no C# spelling, and the receiver/parameter lists are off by
/// one), or a convention mismatch — all uncompilable while reporting
/// <see cref="DecompilationFidelity.Full"/> (#1435).
///
/// <para>This pass replaces every such non-spellable <c>calli</c> with an
/// <see cref="UnsupportedNode"/>, so the C# view shows an honest marker and
/// fidelity caps to <see cref="DecompilationFidelity.Partial"/> instead of
/// claiming a faithful call. A <c>calli</c> through a typed managed/unmanaged
/// <c>delegate*</c> with a matching convention still raises to <c>fp(x)</c> at
/// <c>Full</c>. An <see cref="AddressOfMethod"/> operand (<c>&amp;Method</c>) is
/// also spellable when its <c>delegate*</c> result type matches the call site:
/// the printer casts it — <c>((delegate*&lt;...&gt;)&amp;Method)(x)</c> — because a
/// bare <c>(&amp;Method)(x)</c> is invalid C# (CS0149). Runs last in the
/// diagnostics band, alongside <see cref="FunctionPointerDiagnosticsPass"/>.</para>
/// </summary>
public sealed class CallIndirectSpellabilityPass : IIrPass
{
    public string Name => "calli-spellability";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var call in function.Descendants.OfType<CallIndirect>().ToList())
        {
            if (call.Parent is null || IsSpellable(call))
                continue;

            context.Stepper.StepOver("decline unspellable calli (not a matching delegate* invocation)", call);
            var marker = new UnsupportedNode(0, "calli", Reason(call));
            marker.InheritSourceOffset(call);
            call.ReplaceWith(marker);
        }
    }

    // Faithful as `fp(x)` only when the operand is a typed, non-instance
    // `delegate*` whose calling convention AND signature (return + parameter types)
    // match the call-site signature. A standalone calli signature can disagree with
    // a typed operand's `delegate*` type (arity or type mismatch), which `fp(args)`
    // cannot spell faithfully. An `&Method` operand qualifies too — the printer
    // wraps it in a `(delegate*<...>)` cast (a bare `(&Method)(x)` is invalid C#).
    static bool IsSpellable(CallIndirect call)
        => !call.IsInstance
            && call.Pointer.ResultType is { Kind: TypeRefKind.FunctionPointer } pointer
            && pointer.CallingConvention == call.CallingConvention
            && pointer.ElementType is { } returnType
            && returnType.Equals(call.ReturnType)
            && pointer.TypeArguments.Length == call.ParameterTypes.Length
            && pointer.TypeArguments.SequenceEqual(call.ParameterTypes);

    static string Reason(CallIndirect call)
    {
        if (call.IsInstance)
            return "instance function pointer has no C# delegate* spelling";
        if (call.Pointer.ResultType is not { Kind: TypeRefKind.FunctionPointer } pointer)
            return $"function-pointer operand is {call.Pointer.ResultType?.ToDisplayString() ?? "untyped"}, not a spellable delegate*";
        if (pointer.CallingConvention != call.CallingConvention)
            return $"calling convention '{Display(call.CallingConvention)}' does not match the operand delegate* '{Display(pointer.CallingConvention)}'";
        return $"call-site signature does not match the operand delegate* '{pointer.ToDisplayString()}'";
    }

    static string Display(string convention) => convention.Length == 0 ? "managed" : convention;
}
