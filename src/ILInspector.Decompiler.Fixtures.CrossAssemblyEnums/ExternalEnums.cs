namespace ILInspector.Decompiler.Fixtures.CrossAssemblyEnums;

// Enums whose shape is unresolvable from CfgSampleClass's decompile (they live in
// a different assembly). Each carries an explicit underlying type so the width of
// the `ldelem` opcode CfgSampleClass emits over an array of them is fixed:
//   ExternalULong / ExternalLong -> 8-byte (ldelem.i8), the `long`/`Int64` arm
//   ExternalUInt                 -> 4-byte (ldelem.u4), the `int`/`Int32` arm
// The high-bit members keep the enums from constant-folding to zero and pin the
// unsigned/signed spelling of the largest member.

public enum ExternalULong : ulong
{
    None = 0,
    All = 18446744073709551615UL,
}

public enum ExternalLong : long
{
    Low = 0,
    High = 2,
}

public enum ExternalUInt : uint
{
    None = 0,
    Top = 0x80000000u,
}
