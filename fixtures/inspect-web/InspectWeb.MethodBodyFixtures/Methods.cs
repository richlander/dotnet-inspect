namespace InspectWeb.MethodBodyFixtures;

public class Left
{
    private static int ReferenceTokenDrift() => 99;
    public static int Compute(int value) => value + 1;
    public static int Compute(int value, int other) => value + other;
    public int Value { get; set; }
}

public static class Right
{
    public static int Transform(int value) => value + 2;
}

public interface IBodyless
{
    int WithoutBody(int value);
}
