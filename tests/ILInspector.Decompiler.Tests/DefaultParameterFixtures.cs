namespace ILInspector.Decompiler.Tests;

public static class DefaultParameterFixtures
{
    public static int ValueTypeStructDefault(System.Threading.CancellationToken cancellationToken = default)
        => 0;

    public static int EnumNonZeroDefault(System.DayOfWeek day = System.DayOfWeek.Monday)
        => 0;

    public static int EnumZeroDefault(System.DayOfWeek day = System.DayOfWeek.Sunday)
        => 0;

    public static int CharControlDefault(char c = '\n') => 0;

    public static int CharPrintableDefault(char c = 'A') => 0;

    public static int StringSpecialDefault(string s = "a\"b\\c") => 0;

    public static int StringPlainDefault(string s = "abc") => 0;

    public static int DecimalDefault(decimal value = 1.5m) => 0;

    public static int NullableValueDefault(int? value = null) => 0;

    public static int ReferenceTypeDefault(string? value = null) => 0;

    public static int IntDefault(int value = 5) => 0;

    public static int BoolDefault(bool value = true) => 0;

    public static int DoubleDefault(double value = 1.5) => 0;

    public static int FloatDefault(float value = 2.5f) => 0;
}
