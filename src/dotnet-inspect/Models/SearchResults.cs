namespace DotnetInspector.Models;

/// <summary>
/// Result of extension method search.
/// </summary>
public record class ExtensionMethodResult
{
    public string MethodName { get; set; } = "";

    public string ExtensionClass { get; set; } = "";

    public string ExtendedType { get; set; } = "";

    public string Assembly { get; set; } = "";

    public string? Signature { get; set; }

    public List<string>? Signatures { get; set; }

    public int? Overloads { get; set; }

    public string Kind { get; set; } = "method";

    public string? Source { get; set; }

    public string? SourceVersion { get; set; }

    public string? ReachablePath { get; set; }

    public string? ReachableFromType { get; set; }
}

/// <summary>
/// Result of implementer search.
/// </summary>
public record class ImplementerResult
{
    public string TypeName { get; set; } = "";

    public string? Namespace { get; set; }

    public string Kind { get; set; } = "";

    public string Relationship { get; set; } = "";

    public string? Assembly { get; set; }

    public string? Source { get; set; }

    public string? SourceVersion { get; set; }
}
