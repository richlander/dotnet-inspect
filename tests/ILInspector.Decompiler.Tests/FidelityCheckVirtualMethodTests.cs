using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Trait("Area", "Fidelity")]
public class FidelityCheckVirtualMethodTests
{
    static readonly string s_assembly = typeof(VirtualMethodCallFixture).Assembly.Location;

    [Theory]
    [InlineData(nameof(VirtualMethodCallFixture.CallProtectedVirtual))]
    [InlineData(nameof(VirtualMethodCallFixture.CallInternalVirtual))]
    [InlineData(nameof(VirtualMethodCallFixture.CallPrivateProtectedVirtual))]
    [InlineData(nameof(VirtualMethodCallFixture.CallNonVirtual))]
    public void SameTypeMethodCall_PreservesDeclaredDispatchKind(string method)
    {
        AssertExact(typeof(VirtualMethodCallFixture), method);
    }

    static void AssertExact(Type type, string method)
    {
        var result = Assert.Single(FidelityCheck.EvaluateTargets(
            [s_assembly],
            [new FidelityCheck.CompileBackTarget(
                s_assembly,
                type.FullName!,
                method,
                Overload: 0,
                Signature: "() -> corelib:System.Void")]));

        Assert.Equal(
            FidelityCheck.CompileBackStatus.Exact,
            result.Status);
    }
}

public class VirtualMethodCallFixture
{
    public void CallProtectedVirtual() => ProtectedVirtualTarget();

    public void CallInternalVirtual() => InternalVirtualTarget();

    public void CallPrivateProtectedVirtual() => PrivateProtectedVirtualTarget();

    public void CallNonVirtual() => NonVirtualTarget();

    protected virtual void ProtectedVirtualTarget()
    {
    }

    internal virtual void InternalVirtualTarget()
    {
    }

    private protected virtual void PrivateProtectedVirtualTarget()
    {
    }

    void NonVirtualTarget()
    {
    }
}
