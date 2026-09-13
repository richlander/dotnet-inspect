namespace InspectWeb.CloneTransportFixtures;

public sealed class GenericSeed<T>
{
    public static object Create() => new();
}

public interface IExplicitValue
{
    object Value { get; }
}

public sealed class ExplicitSeed : IExplicitValue
{
    object IExplicitValue.Value => new();
}
