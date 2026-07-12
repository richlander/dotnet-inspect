public class @class
{
}

public enum @event
{
    None
}

public class @delegate
{
}

public class @readonly
{
}

public class @scoped
{
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
