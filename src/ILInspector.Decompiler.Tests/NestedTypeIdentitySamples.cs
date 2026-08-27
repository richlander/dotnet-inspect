namespace ILInspector.Decompiler.Tests;

// A top-level type sharing the nested type's leaf name, to prove the importer
// keys on the fully-qualified name, not the leaf.
public sealed class NestedSample
{
    public static int Negate(int x) => -x;
}
