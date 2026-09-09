namespace LibraryApiDiffFixture;

public sealed class ProjectionReceiver
{
    public int Transform(int value) => value;
}

public static class ProjectionExtensions
{
}

public ref struct TypeDefinitionOnly
{
    public int Value;
}

public class HardChangedType
{
    public int First() => 1;
}

public class OtherHardChangedType
{
    public int Second() => 2;
}

public sealed class AddedType
{
    public int First() => 1;

    public int Second() => 2;
}
