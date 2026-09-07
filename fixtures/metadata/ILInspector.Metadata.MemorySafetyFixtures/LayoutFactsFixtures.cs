using System.Runtime.InteropServices;

namespace ILInspector.Metadata.MemorySafetyFixtures;

[StructLayout(LayoutKind.Explicit, Pack = 2, Size = 32)]
public struct LayoutFactsExplicitFixture
{
    [FieldOffset(0)]
    public safe int Zero;

    [FieldOffset(12)]
    public safe long Nonzero;

    public static int Static;

    public int Method() => 0;

    public int Property => 0;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 24)]
public struct LayoutFactsSequentialFixture
{
    public int Value;
}

public class LayoutFactsDefaultFixture
{
    public int Value;

    [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 16)]
    public struct Nested
    {
        [FieldOffset(4)]
        public safe int Value;
    }
}
