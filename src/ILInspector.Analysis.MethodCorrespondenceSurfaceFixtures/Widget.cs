namespace MethodCorrespondenceFixture;

public static unsafe class Widget
{
    public static int Other() => 0;

    public static int Transform(int value) => value + 1;

    public static int Neighbor(long value) => checked((int)value);

    public static int Invoke(
        delegate* unmanaged[Cdecl]<int, int> callback,
        int value) =>
        callback(value);

    public static int UseHelper(Helper helper) => helper.Value;
}

public sealed class Helper
{
    public int Value { get; init; }
}

public sealed class KindShape
{
    public static int TransformKind(KindShape value) => 1;
}
