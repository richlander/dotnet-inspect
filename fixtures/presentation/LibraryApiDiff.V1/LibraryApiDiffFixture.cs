namespace LibraryApiDiffFixture;

public sealed class ProjectionReceiver
{
}

public static class ProjectionExtensions
{
    public static int Transform(
        this ProjectionReceiver receiver,
        int value) => value;
}

public struct TypeDefinitionOnly
{
    public int Value;
}

public class HardChangedType
{
    public virtual int First() => 1;
}

public class OtherHardChangedType
{
    public virtual int Second() => 2;
}

public sealed class RemovedType
{
    public int First() => 1;

    public int Second() => 2;
}
