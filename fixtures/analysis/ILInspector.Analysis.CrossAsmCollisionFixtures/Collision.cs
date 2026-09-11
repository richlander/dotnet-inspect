namespace CrossAsmCollision;

// #1804 review. A reference type whose namespace+name (CrossAsmCollision.SharedShape) is
// deliberately identical to an in-assembly STRUCT defined in the test assembly. Because a
// display name omits assembly identity, a name-based value-type shortcut would misclassify a
// `newobj` of THIS reference type as non-heap and silently drop a real allocation. The test
// constructs this external type (via an extern alias) to pin that the allocation survives.
public sealed class SharedShape
{
    public int Value;
    public SharedShape(int value) => Value = value;
}
