using System.Text.Json.Serialization;
using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class ApiSurface
{
    [MarkOutIgnore]
    public List<ApiType> Types { get; set; } = [];

    [MarkOutPropertyName("Public Types")]
    public int PublicTypeCount { get; set; }

    [MarkOutPropertyName("Public Methods")]
    public int PublicMethodCount { get; set; }

    [MarkOutPropertyName("Public Properties")]
    public int PublicPropertyCount { get; set; }

    [MarkOutPropertyName("Public Events")]
    public int PublicEventCount { get; set; }

    [MarkOutPropertyName("Public Fields")]
    public int PublicFieldCount { get; set; }
}

[MarkOutSerializable]
public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate

    [MarkOutPropertyName("Sealed")]
    public bool IsSealed { get; set; }

    [MarkOutPropertyName("Abstract")]
    public bool IsAbstract { get; set; }

    [MarkOutPropertyName("Static")]
    public bool IsStatic { get; set; }

    [MarkOutPropertyName("Base Type")]
    public string? BaseType { get; set; }

    [MarkOutIgnore]
    public List<string>? Interfaces { get; set; }

    [MarkOutPropertyName("Interfaces")]
    [JsonIgnore]
    public string? InterfacesSummary => Interfaces is { Count: > 0 }
        ? string.Join(", ", Interfaces)
        : null;

    [MarkOutIgnore]
    public List<ApiMember>? Members { get; set; }
}

[MarkOutSerializable]
public class ApiMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // method, property, field, event, constructor

    [MarkOutPropertyName("Return Type")]
    public string? ReturnType { get; set; }

    public string? Signature { get; set; }

    [MarkOutPropertyName("Static")]
    public bool IsStatic { get; set; }

    [MarkOutPropertyName("Virtual")]
    public bool IsVirtual { get; set; }

    [MarkOutPropertyName("Abstract")]
    public bool IsAbstract { get; set; }
}
