namespace DotnetInspector.MatchBinding;

public sealed class Payload
{
    public Payload(object? target, nint method)
    {
    }

    public int Invoke(int value) => value;
}
