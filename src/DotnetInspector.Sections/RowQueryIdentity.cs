namespace DotnetInspector.Sections;

public sealed class RowQuerySchemaIdentity
{
    private RowQuerySchemaIdentity()
    {
    }

    public static RowQuerySchemaIdentity Create() => new();
}

public sealed class RowQueryFieldIdentity
{
    private RowQueryFieldIdentity()
    {
    }

    public static RowQueryFieldIdentity Create() => new();
}

public sealed class RowQueryNamedOrderIdentity
{
    private RowQueryNamedOrderIdentity()
    {
    }

    public static RowQueryNamedOrderIdentity Create() => new();
}

public sealed class ResolvedRowQueryOrderIdentity
{
    internal ResolvedRowQueryOrderIdentity()
    {
    }
}
