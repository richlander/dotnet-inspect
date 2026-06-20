using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Records a <see cref="DiagnosticIds.UnsupportedFunctionPointer"/> diagnostic
/// for every <see cref="LoadFunctionPointer"/> that survived raising — the
/// residue the <see cref="DelegateConstructionPass"/> could not consume
/// (function pointers feeding <c>calli</c> or native callbacks). Running last,
/// it keeps these honest, visible gaps off the silent-Partial path: a method
/// with a bare function-pointer load is Partial, and now says why.
/// </summary>
public sealed class FunctionPointerDiagnosticsPass : IIrPass
{
    public string Name => "function-pointer-diagnostics";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var pointer in function.Descendants.OfType<LoadFunctionPointer>())
        {
            var opcode = pointer.IsVirtual ? "ldvirtftn" : "ldftn";
            context.Stepper.StepOver($"diagnose residual function pointer to {pointer.Method.DeclaringType.ToDisplayString()}.{pointer.Method.Name}", pointer);
            function.Diagnostics.Add(new DecompilerDiagnostic(
                DiagnosticIds.UnsupportedFunctionPointer,
                pointer.IsVirtual
                    ? $"function-pointer {opcode}: {FormatMethod(pointer.Method)} requires a receiver; C# cannot spell a virtual method address as a delegate* target"
                    : $"function-pointer {opcode}: {FormatMethod(pointer.Method)} is not a delegate construction and was not raised to &Method"));
        }
    }

    private static string FormatMethod(MethodRef method)
        => $"{method.DeclaringType.ToDisplayString()}.{method.Name}";
}
