namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Elides trailing call arguments that the compiler baked in for omitted C#
/// optional parameters: an argument that is a compile-time constant equal to its
/// parameter's declared default (<c>ToWords(gender, null)</c> →
/// <c>ToWords(gender)</c>). Optional parameters are a C# concept the IL type
/// system erases — the compiler emits the default at every call site — so
/// dropping the argument is opcode-neutral: recompiling re-inserts the same
/// default. It is a taste transform (valid-but-different), not a fidelity change.
///
/// <para>The one real hazard is overload resolution: a shorter argument list can
/// rebind to a different same-named overload. The pass never decides safety
/// itself — it is bounded by <see cref="MethodRef.SafeTrailingElidableCount"/>,
/// the conservative overload-safe count the importer stamped from metadata
/// (<see cref="OptionalArgumentFacts"/>). It only drops an argument that is still
/// a bare <see cref="Constant"/> equal to the recovered default, and only when
/// the call still carries the callee's full parameter list. Runs late, before
/// <see cref="CoercionInsertionPass"/> wraps sink values, so the trailing
/// defaults are still bare constants and never named/reshaped.</para>
/// </summary>
public sealed class OptionalArgumentElisionPass : IIrPass
{
    public string Name => "optional-argument-elision";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (call.Parent is null)
                continue;  // already detached by an outer rewrite this pass
            int drop = ElidableTrailingCount(call);
            if (drop == 0)
                continue;

            var detached = call.DetachChildren();
            var kept = detached.Take(detached.Count - drop).Cast<IrExpression>();
            var replacement = new Call(call.Callee, call.IsVirtual, kept) { ConstrainedTo = call.ConstrainedTo };
            context.Stepper.StepOver(
                $"elide {drop} default argument{(drop == 1 ? "" : "s")} of {call.Callee.Name}",
                call);
            call.ReplaceWith(replacement);
        }
    }

    static int ElidableTrailingCount(Call call)
    {
        var callee = call.Callee;
        int safe = callee.SafeTrailingElidableCount;
        if (safe == 0 || callee.ParameterDefaults.IsDefaultOrEmpty)
            return 0;

        int n = callee.ParameterTypes.Length;
        if (callee.ParameterDefaults.Length != n)
            return 0;

        var arguments = call.Arguments;
        int receiverCount = callee.HasThis ? 1 : 0;
        // Only elide when the argument list still matches the full parameter list;
        // a pass that already reshaped the arguments is out of scope.
        if (arguments.Count - receiverCount != n)
            return 0;

        // Overload resolution on the shortened instance call uses the receiver's
        // static type, not the callee's declaring type. If the receiver is a
        // subtype of the declaring type, a shorter same-named overload introduced
        // by that subtype can capture the shortened call — demonstrated: a base
        // optional method (`Base.Log(string, bool = false)`) called through a
        // derived receiver whose `Derived.Log(string)` steals `Log("x")`. The
        // importer's sibling scan only sees the declaring type, so require the
        // receiver to be exactly the declaring type; then no more-derived overload
        // participates. (Static and extension callees have no receiver subtype.)
        if (callee.HasThis)
        {
            var receiverType = arguments[0].ResultType;
            if (receiverType is null || !receiverType.Equals(callee.DeclaringType))
                return 0;
        }

        int drop = 0;
        for (int k = 1; k <= safe; k++)
        {
            var parameter = callee.ParameterDefaults[n - k];
            if (!parameter.HasDefault)
                break;
            if (arguments[receiverCount + (n - k)] is not Constant constant)
                break;
            if (!DefaultMatches(constant, parameter.Value))
                break;
            drop = k;
        }
        if (drop == 0)
            return 0;

        // The overload guardrail (SafeTrailingElidableCount) assumes the callee is
        // the best match on the retained arguments — true only when each retained
        // argument's type equals its parameter type (an identity conversion). A
        // widening/boxing/reference conversion at a retained position could let a
        // sibling with that exact parameter type rebind the shortened call, so
        // decline unless every retained parameter-aligned argument is identity.
        for (int i = 0; i < n - drop; i++)
        {
            var argumentType = arguments[receiverCount + i].ResultType;
            if (argumentType is null || !argumentType.Equals(callee.ParameterTypes[i]))
                return 0;
        }
        return drop;
    }

    /// <summary>
    /// Whether a constant argument equals a recovered parameter default. IL erases
    /// the constant's C# type, so integral/bool/char/enum values are compared by
    /// their two's-complement bits, floating point by exact bit pattern, and
    /// strings ordinally; a mismatched or unrecognized pairing is conservatively
    /// unequal so the argument stays explicit.
    /// </summary>
    static bool DefaultMatches(Constant argument, object? expected)
    {
        object? actual = argument.Value;
        if (actual is null || expected is null)
            return actual is null && expected is null;
        if (TryToInt64(actual, out long actualBits) && TryToInt64(expected, out long expectedBits))
            return actualBits == expectedBits;
        if (actual is float actualSingle && expected is float expectedSingle)
            return BitConverter.SingleToInt32Bits(actualSingle) == BitConverter.SingleToInt32Bits(expectedSingle);
        if (actual is double actualDouble && expected is double expectedDouble)
            return BitConverter.DoubleToInt64Bits(actualDouble) == BitConverter.DoubleToInt64Bits(expectedDouble);
        if (actual is string actualString && expected is string expectedString)
            return string.Equals(actualString, expectedString, StringComparison.Ordinal);
        return false;
    }

    static bool TryToInt64(object value, out long result)
    {
        switch (value)
        {
            case bool b: result = b ? 1 : 0; return true;
            case char c: result = c; return true;
            case sbyte or byte or short or ushort or int or uint or long:
                result = System.Convert.ToInt64(value); return true;
            case ulong u: result = unchecked((long)u); return true;
            default: result = 0; return false;
        }
    }
}
