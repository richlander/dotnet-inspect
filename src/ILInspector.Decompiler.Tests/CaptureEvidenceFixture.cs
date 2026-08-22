namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Compiler-produced closures whose capture shapes exercise the capture-evidence
/// plane: two captured variables per closure, uses in separate statements (each
/// separately addressable), and a repeated use inside one statement (which the
/// printer refuses to name, so it is unaddressable everywhere).
/// </summary>
/// <remarks>
/// These live outside <see cref="CfgSampleClass"/> deliberately: they exist to
/// pin capture evidence, not to move the compile-back fidelity docket that
/// fixture owns.
/// </remarks>
public static class CaptureEvidenceFixture
{
    // Two captured parameters, each read twice. The two reads of each sit under
    // different sub-expressions, which is what lets the printer name both: it
    // refuses a range for text that is ambiguous inside its parent's window.
    public static System.Func<int, int> TwoCaptureLambda(int first, int second)
        => x => x * first - second + (second - first);

    // The same two captured parameters in a capturing local function, which the
    // compiler lowers to a by-ref struct display class rather than a class.
    public static int TwoCaptureLocalFunction(int first, int second)
    {
        return Combine(5);

        int Combine(int v) => v * first - second + (second - first);
    }

    // Both reads of `only` sit in one statement, where the printer refuses to
    // name either occurrence uniquely. The capture is still real and the row
    // must still name the read it can address.
    public static System.Func<int, int> RepeatedUseInOneStatementLambda(int only)
        => x => x * only + only;

    // Negative: no captured variable at all, so a capture row would be an
    // invention rather than evidence.
    public static System.Func<int, int> NonCapturingLambda() => x => x * 3 + 1;

    // Negative: a static local function cannot capture.
    public static int StaticLocalFunction(int x)
    {
        return Triple(x);

        static int Triple(int v) => v * 3;
    }
}
