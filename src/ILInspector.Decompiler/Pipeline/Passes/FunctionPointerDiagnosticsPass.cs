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

    public void Run(IrFunction function)
    {
        foreach (var pointer in function.Descendants.OfType<LoadFunctionPointer>())
        {
            // The second whitespace token (ldftn:) is the harness roadmap bucket.
            function.Diagnostics.Add(new DecompilerDiagnostic(
                DiagnosticIds.UnsupportedFunctionPointer,
                $"function-pointer ldftn: {pointer.Method.DeclaringType.ToDisplayString()}.{pointer.Method.Name} is not a delegate construction"));
        }
    }
}
