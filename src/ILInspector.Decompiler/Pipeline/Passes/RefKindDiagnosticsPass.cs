using ILInspector.Decompiler;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Records a <see cref="DiagnosticIds.UnverifiedByRefArgument"/> diagnostic for
/// every <see cref="Call"/>/<see cref="NewObject"/> that forwards a
/// managed-pointer argument to a by-ref parameter whose call-site ref-kind is
/// unknown (the callee resolved as a MemberReference, which carries no
/// parameter rows). The printer spells a default <c>ref</c> there that it
/// cannot verify against the real <c>out</c>/<c>in</c>/<c>ref</c> — wrong for
/// an out/in parameter (CS1620/CS1615). Running last, it keeps these honest,
/// visible gaps off the silent-Full path: the method is already lowered to
/// Partial by <see cref="IrFunction.Fidelity"/>, and now says why.
/// </summary>
public sealed class RefKindDiagnosticsPass : IIrPass
{
    public string Name => "ref-kind-diagnostics";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var node in function.Descendants)
        {
            var callee = node switch
            {
                Call { HasUnverifiedByRefArgument: true } c => c.Callee,
                NewObject { HasUnverifiedByRefArgument: true } o => o.Constructor,
                _ => null,
            };
            if (callee is null)
                continue;
            context.Stepper.StepOver($"diagnose unverified by-ref argument to {callee.DeclaringType.ToDisplayString()}.{callee.Name}", node);
            function.Diagnostics.Add(new DecompilerDiagnostic(
                DiagnosticIds.UnverifiedByRefArgument,
                $"unverified by-ref argument: call-site ref-kind for {callee.DeclaringType.ToDisplayString()}.{callee.Name} is unknown (MemberReference carries no parameter rows)"));
        }
    }
}
