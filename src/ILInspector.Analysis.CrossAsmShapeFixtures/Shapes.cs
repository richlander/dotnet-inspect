namespace ILInspector.Analysis.CrossAsmShapeFixtures;

// Cross-assembly type-shape fixtures (analysis ladder #1623 rung 7). The product is
// SRM-direct and does NOT load referenced assemblies, so a consumer in another assembly
// sees these types only as TypeRef/TypeSpec metadata. They let the tests assert that the
// allocation signal classifies a `newobj` of each shape honestly (#1804):
//
//   * CrossValueStruct       — non-generic struct: value-type-ness is UNRESOLVABLE from the
//                              consumer alone (a bare TypeRef), so its `newobj` is an owned
//                              false-positive allocation at the no-assembly-loading boundary.
//   * CrossGenericStruct<T>  — constructed generic struct: the consumer's own TypeSpec
//                              signature blob encodes VALUETYPE, so its `newobj` IS resolved
//                              as non-heap.
//   * CrossRefType           — reference type: its `newobj` is a real heap allocation.
//   * CrossEnum              — enum: a value type; cast/use must not be a heap allocation.

public struct CrossValueStruct
{
    public int Value;
    public CrossValueStruct(int value) => Value = value;
}

public struct CrossGenericStruct<T>
{
    public T Value;
    public CrossGenericStruct(T value) => Value = value;
}

public sealed class CrossRefType
{
    public int Value;
    public CrossRefType(int value) => Value = value;
}

public enum CrossEnum
{
    Zero,
    One,
}
