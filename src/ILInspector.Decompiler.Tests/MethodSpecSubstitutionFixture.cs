namespace ILInspector.Decompiler.Tests;

public sealed class MethodSpecSubstitutionBox<T>
{
    public U Pick<U>(T declaringTypeValue, U methodValue)
        => methodValue;
}

public static class MethodSpecSubstitutionFixture
{
    public static U Invoke<T, U>(
        MethodSpecSubstitutionBox<T> box,
        T declaringTypeValue,
        U methodValue)
        => box.Pick<U>(declaringTypeValue, methodValue);
}
