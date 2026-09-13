namespace ILInspector.Decompiler.Tests;

public enum SkeletonFlags
{
    None = 0,
    A = 1,
    B = 2,
}

/// <summary>
/// Exercises two whole-module skeleton-emit hazards the changed-method fidelity
/// check must survive (#1282), used by <see cref="SkeletonEmitTests"/>:
/// <list type="bullet">
/// <item>a non-generic <b>explicit interface implementation</b> (IL name
/// <c>System.IDisposable.Dispose</c>): a reconstructed `public Iface.Member`
/// stub is invalid C# (CS0106) and poisons the whole-module compile;</item>
/// <item>a <b>const enum field</b>: its metadata value is the integer underlying,
/// so a `public const SkeletonFlags F = 1;` stub is CS0266 without a cast.</item>
/// </list>
/// Both live on a sibling type so the failure surfaces when any method in the
/// module is fidelity-checked.
/// </summary>
public class SkeletonEmitFixture : IDisposable
{
    public const SkeletonFlags DefaultFlags = SkeletonFlags.A;

    void IDisposable.Dispose()
    {
    }

    public int Sum(int a, int b) => a + b;
}
