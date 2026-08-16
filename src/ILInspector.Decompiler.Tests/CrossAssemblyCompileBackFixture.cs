using DotnetInspector.Fixtures;

namespace ILInspector.Decompiler.Tests;

public static class CrossAssemblyCompileBackExtensions
{
    public static int Twice(
        this CrossAssemblyAccessorCompileBackFixture value) =>
        value.Value * 2;
}

public class CrossAssemblyAccessorCompileBackFixture
    : CrossAssemblyAccessorBase
{
    public sealed override int Value => base.Value + 1;

    public override int this[int index]
    {
        get => base[index];
        set => base[index] = value;
    }

    public override event EventHandler? Changed
    {
        add => base.Changed += value;
        remove => base.Changed -= value;
    }
}

public sealed class CrossAssemblyNeedsArgumentCompileBackFixture
    : CrossAssemblyNeedsArgumentBase
{
    public CrossAssemblyNeedsArgumentCompileBackFixture(int value)
        : base(value)
    {
    }

    public int Sum(int left, int right) => left + right;
}

public sealed class CrossAssemblyCompileBackFixture
    : CrossAssemblyConstraintBase
{
    public override T? ClassConstraint<T>(T? value)
        where T : class =>
        value;

    public override T? DelegateConstraint<T>(T? value)
        where T : class =>
        value;

    public override T? InterfaceConstraint<T>(T? value)
        where T : default =>
        value;

    public override T? EnumConstraint<T>(T? value)
        where T : default =>
        value;

    public override T? TransitiveConstraint<T, U>(T? value)
        where T : class
        where U : class =>
        value;

    public override T? GenericBaseConstraint<T>(T? value)
        where T : class =>
        value;
}
