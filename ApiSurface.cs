namespace DotnetInspector;

public class ApiSurface
{
    public List<ApiType> Types { get; set; } = [];
    public int PublicTypeCount { get; set; }
    public int PublicMethodCount { get; set; }
    public int PublicPropertyCount { get; set; }
    public int PublicEventCount { get; set; }
    public int PublicFieldCount { get; set; }
}

public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate
    public bool IsSealed { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsStatic { get; set; }
    public string? BaseType { get; set; }
    public List<string>? Interfaces { get; set; }
    public List<ApiMember>? Members { get; set; }
}

public class ApiMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // method, property, field, event, constructor
    public string? ReturnType { get; set; }
    public string? Signature { get; set; }
    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsAbstract { get; set; }
}
