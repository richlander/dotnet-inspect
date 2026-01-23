using System.Text.Json.Serialization;
using MarkdownData;

namespace DotnetInspector;

[MdfSerializable]
public class ApiSurface
{
    [MdfIgnore]
    public List<ApiType> Types { get; set; } = [];

    [MdfPropertyName("Public Types")]
    public int PublicTypeCount { get; set; }

    [MdfPropertyName("Public Methods")]
    public int PublicMethodCount { get; set; }

    [MdfPropertyName("Public Properties")]
    public int PublicPropertyCount { get; set; }

    [MdfPropertyName("Public Events")]
    public int PublicEventCount { get; set; }

    [MdfPropertyName("Public Fields")]
    public int PublicFieldCount { get; set; }
}

[MdfSerializable]
public class ApiType
{
    public string? Namespace { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // class, struct, interface, enum, delegate

    [MdfPropertyName("Sealed")]
    public bool IsSealed { get; set; }

    [MdfPropertyName("Abstract")]
    public bool IsAbstract { get; set; }

    [MdfPropertyName("Static")]
    public bool IsStatic { get; set; }

    [MdfPropertyName("Base Type")]
    public string? BaseType { get; set; }

    [MdfIgnore]
    public List<string>? Interfaces { get; set; }

    [MdfPropertyName("Interfaces")]
    [JsonIgnore]
    public string? InterfacesSummary => Interfaces is { Count: > 0 }
        ? string.Join(", ", Interfaces)
        : null;

    [MdfIgnore]
    public List<ApiMember>? Members { get; set; }
}

[MdfSerializable]
public class ApiMember
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";  // method, property, field, event, constructor

    [MdfPropertyName("Return Type")]
    public string? ReturnType { get; set; }

    public string? Signature { get; set; }

    [MdfPropertyName("Static")]
    public bool IsStatic { get; set; }

    [MdfPropertyName("Virtual")]
    public bool IsVirtual { get; set; }

    [MdfPropertyName("Abstract")]
    public bool IsAbstract { get; set; }
}
