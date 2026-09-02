namespace CSharpText.Tests;

public static class UnicodeIdentifierFixtures
{
    public static int CombiningMarkLocal(int value)
    {
        int A\u0301 = value;
        Increment(ref A\u0301);
        return A\u0301;
    }

    static void Increment(ref int value) => value++;
}
