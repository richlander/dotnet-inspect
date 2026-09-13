namespace ILInspector.Decompiler.Tests;

// Regression fixtures for #2990: a 64-bit-backed [Flags] enum OR/AND accumulation.
// The compiler lowers the flag arithmetic into the enum's Int64 underlying space
// (`ldc.i4 N; conv.i8; or/and`) and spills the intermediate accumulator into long
// stack slots. When an enum operand sits on the RIGHT of an `or`/`and` (the IL
// accumulation order), TypeFamilies.BinaryResult must still surface the enum type
// so the OR chain stays enum-typed. Otherwise the chain collapses to `long` and the
// bare-constant flag arms render as `... | (long)32768` — CS0019 (operator '|'
// cannot be applied to 'FlagCaps64' and 'long').
[System.Flags]
public enum FlagCaps64 : long
{
    None = 0,
    Protocol = 512,
    Interactive = 1024,
    LoadLocal = 128,
    Secure = 32768,
    MultiStatements = 65536,
    MultiResults = 131072,
}

public static class FlagsEnumAccumulatorSamples
{
    public static int Accumulate(FlagCaps64 server, bool interactive)
    {
        FlagCaps64 caps = FlagCaps64.Protocol
            | (interactive ? (server & FlagCaps64.Interactive) : FlagCaps64.None)
            | (server & FlagCaps64.LoadLocal)
            | FlagCaps64.Secure
            | (server & FlagCaps64.MultiStatements)
            | FlagCaps64.MultiResults;
        return (int)caps;
    }
}
