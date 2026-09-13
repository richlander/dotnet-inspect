namespace ILInspector.Decompiler.Tests;

/// <summary>
/// A generic type with a non-generic nested type, used by
/// <see cref="SkeletonEmitTests"/> to pin the fidelity skeleton's nested-type
/// generic-parameter emission (#1344). A nested type's metadata generic
/// parameter list is cumulative (it inherits the enclosing <c>T</c>), so the
/// skeleton must declare <c>struct Nested</c>, not <c>struct Nested&lt;T&gt;</c> —
/// the latter shadows <c>T</c> and rejects a <c>GenericNestedHolder&lt;int&gt;.Nested</c>
/// reference as CS0305.
/// </summary>
public class GenericNestedHolder<T>
{
    public struct Nested
    {
        public T Value;
    }

    public Nested MakeNested() => default;
}

public static class GenericNestedUser
{
    public static GenericNestedHolder<int>.Nested UseNested() => default;
}
