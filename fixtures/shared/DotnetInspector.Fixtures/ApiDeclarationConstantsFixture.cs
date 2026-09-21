namespace DotnetInspector.Fixtures;

public interface IApiDeclarationConstantsFixture
{
    const int Answer = 42;

    void Observe();
}

public static class ApiDeclarationConstantsFixture
{
    public const double NegativeZero = -0D;
    public const float FloatNegativeZero = -0F;
}
