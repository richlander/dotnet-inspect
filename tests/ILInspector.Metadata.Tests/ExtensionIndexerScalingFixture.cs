namespace ILInspector.Metadata.Tests;

public static class ExtensionIndexerScalingFixture
{
    extension(string value)
    {
        public int this[int index] => value.Length + index;
        public int this[long index] => value.Length + (int)index;
        public int this[string index] => value.Length + index.Length;
        public int this[byte index] => value.Length + index;
        public int this[short index] => value.Length + index;
        public int this[uint index] => value.Length + (int)index;
        public int this[ulong index] => value.Length + (int)index;
        public int this[ushort index] => value.Length + index;
        public int this[sbyte index] => value.Length + index;
        public int this[char index] => value.Length + index;
        public int this[bool index] => value.Length + (index ? 1 : 0);
        public int this[float index] => value.Length + (int)index;
        public int this[double index] => value.Length + (int)index;
        public int this[decimal index] => value.Length + (int)index;
        public int this[Guid index] => value.Length + index.GetHashCode();
        public int this[DateTime index] => value.Length + index.Day;
    }
}
