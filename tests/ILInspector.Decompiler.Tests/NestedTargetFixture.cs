namespace ILInspector.Decompiler.Tests;

/// <summary>
/// A top-level type with a nested type, used by
/// <see cref="NestedTargetLookupTests"/> to pin the changed-method fidelity
/// path's nested-type identity matching (#1274). The bodies are trivial; only
/// the type/member identity surface matters to the test.
/// </summary>
public class NestedTargetFixture
{
    public int OuterAdd(int a, int b) => a + b;

    public class Inner
    {
        public int InnerAdd(int a, int b) => a + b;
    }
}
