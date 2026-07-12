public class @class
{
}

public enum @event
{
    None
}

[System.AttributeUsage(System.AttributeTargets.Parameter)]
public sealed class GlobalTypeAttribute : System.Attribute
{
    public GlobalTypeAttribute(System.Type type, @event mode)
    {
        _ = type;
        _ = mode;
    }
}
