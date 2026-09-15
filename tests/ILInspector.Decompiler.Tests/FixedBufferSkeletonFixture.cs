namespace ILInspector.Decompiler.Tests;

public unsafe struct FixedBufferSkeletonFixture
{
    public fixed byte Buffer[16];

    public int Value() => 42;
}
