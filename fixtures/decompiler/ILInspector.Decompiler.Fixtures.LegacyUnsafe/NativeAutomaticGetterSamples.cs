using System.Runtime.InteropServices;

namespace ILInspector.Decompiler.Fixtures;

public unsafe class SelectedUnsafeAutoPropertySamples
{
    public int* Pointer { get; }
    public static int* SharedPointer { get; }
    public delegate* unmanaged[Cdecl]<int, int> FunctionPointer { get; }
    public static delegate* unmanaged[Cdecl]<int, int> SharedFunctionPointer { get; }
}

[StructLayout(LayoutKind.Explicit)]
public struct SelectedLayoutAutoPropertySamples
{
    [field: FieldOffset(0)]
    public int Count { get; }
}
