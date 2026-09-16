namespace MethodCorrespondenceFixture;

public static unsafe class Widget
{
    public static int Inserted(byte value) => value;

    public static int Neighbor(long value) => checked((int)value);

    public static int Other() => 0;

    public static int Transform(int value) => value + 2;

    public static Helper Allocate(int value) =>
        new() { Value = value };

    public static int CallHelper(Helper helper) =>
        helper.Read();

    public static int Invoke(
        delegate* unmanaged[Cdecl]<int, int> callback,
        int value) =>
        callback(value);

    public static int UseHelper(Helper helper) => helper.Value;
}

public sealed class Helper
{
    public int Value { get; init; }

    public int Read() => Value;
}

public struct KindShape
{
    public static int TransformKind(KindShape value) => 2;
}

public abstract class Bodyless
{
    public abstract int Run();
}
