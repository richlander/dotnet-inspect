using System.Runtime.CompilerServices;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Turns the IR structural invariant check on for the whole test run. The check
/// is off by default in the shipped tool (<see cref="IrInvariants"/>), so without
/// this the suite would exercise the pipeline exactly as production does but
/// never validate the tree after each pass. Enabling it here — in the Release
/// configuration the suite runs under (see AGENTS.md) — is what gives the check
/// its teeth: any pass that corrupts parent/child wiring fails a real test.
/// </summary>
internal static class IrInvariantsTestHost
{
    [ModuleInitializer]
    public static void Enable() => IrInvariants.Enabled = true;
}
