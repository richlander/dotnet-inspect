namespace ILInspector.Decompiler.Tests;

// A flags enum with a named member at a non-low bit (Gamma = 16): a `ref CfgStyles`
// bitwise test reads the enum via `ldind.i4`, so the importer must register the
// pointee's shape and member names for the int constant to name `Gamma`.
[System.Flags]
public enum CfgStyles { None = 0, Alpha = 1, Beta = 2, Gamma = 16 }
