namespace DotnetInspector.Fixtures;

public abstract class ExternalConstraintClass;

public interface IExternalConstraint;

public abstract class ExternalGenericBase<T>;

public abstract class ExternalDerivedFromGeneric
    : ExternalGenericBase<int>;

public abstract class CrossAssemblyConstraintBase
{
    public abstract T? ClassConstraint<T>(T? value)
        where T : ExternalConstraintClass;

    public abstract T? DelegateConstraint<T>(T? value)
        where T : Delegate;

    public abstract T? InterfaceConstraint<T>(T? value)
        where T : IExternalConstraint;

    public abstract T? EnumConstraint<T>(T? value)
        where T : Enum;

    public abstract T? TransitiveConstraint<T, U>(T? value)
        where T : U
        where U : ExternalConstraintClass;

    public abstract T? GenericBaseConstraint<T>(T? value)
        where T : ExternalDerivedFromGeneric;
}
