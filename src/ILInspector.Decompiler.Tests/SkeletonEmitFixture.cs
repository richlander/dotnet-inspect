namespace ILInspector.Decompiler.Tests;

public enum SkeletonFlags
{
    None = 0,
    A = 1,
    B = 2,
}

public class SkeletonInstanceAssignmentHazard
{
    public int Value;

    public void operator +=(int other)
    {
        Value += other;
    }
}

public class SkeletonOrdinaryOperatorNameHazard
{
    public int Value;

    public void op_AdditionAssignment(int other)
    {
        Value += other;
    }

    public int Invoke(int other)
    {
        op_AdditionAssignment(other);
        return Value;
    }
}

/// <summary>
/// Exercises whole-module skeleton-emit hazards the changed-method fidelity
/// check must survive (#1282), used by <see cref="SkeletonEmitTests"/>:
/// <list type="bullet">
/// <item>a non-generic <b>explicit interface implementation</b> (IL name
/// <c>System.IDisposable.Dispose</c>): a reconstructed `public Iface.Member`
/// stub is invalid C# (CS0106) and poisons the whole-module compile;</item>
/// <item>a <b>const enum field</b>: its metadata value is the integer underlying,
/// so a `public const SkeletonFlags F = 1;` stub is CS0266 without a cast.</item>
/// <item>a C# 14 <b>instance assignment operator</b> on a sibling type: forcing
/// <c>static</c> onto its skeleton declaration is CS0106.</item>
/// <item>an ordinary method whose name resembles an operator: spelling it as an
/// operator makes its direct call illegal (CS0571).</item>
/// </list>
/// The sibling hazards ensure the failure surfaces when any method in the
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
