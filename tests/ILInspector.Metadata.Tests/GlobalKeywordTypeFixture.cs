public class @class
{
}

public enum @event
{
    None
}

[System.AttributeUsage(System.AttributeTargets.Parameter)]
public sealed class GlobalTypeAttribute(System.Type type, @event mode) : System.Attribute
{
}
