using DotnetInspector.Fixtures;

namespace DotnetInspector.Tests;

public sealed class CrossAssemblyConstraintRestatementFixture
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
