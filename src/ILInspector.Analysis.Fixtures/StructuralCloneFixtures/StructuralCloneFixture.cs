namespace ILInspector.Analysis.StructuralCloneFixtures;

public static class StructuralCloneFixture
{
    public static int ExactPositiveA(int value)
    {
        int adjusted = value > 10 ? value + 3 : value - 2;
        return adjusted * 2;
    }

    public static int ExactPositiveB(int input)
    {
        int result = input > 10 ? input + 3 : input - 2;
        return result * 2;
    }

    public static int EdgeRoleNegativeA(int value)
    {
        if (value > 10)
            return value + 3;
        return value - 2;
    }

    public static int EdgeRoleNegativeB(int value)
    {
        if (value > 10)
            return value - 2;
        return value + 3;
    }

    public static int SignatureHazardByte(byte value) => 42;

    public static int SignatureHazardUInt(uint value) => 42;

    public static string? SignatureHazardString() => null;

    public static object? SignatureHazardObject() => null;

    public static int ExceptionHandlingA(int value)
    {
        try
        {
            return value + 1;
        }
        finally
        {
            GC.KeepAlive(value);
        }
    }

    public static int ExceptionHandlingB(int value)
    {
        try
        {
            return value + 1;
        }
        finally
        {
            GC.KeepAlive(value);
        }
    }
}
