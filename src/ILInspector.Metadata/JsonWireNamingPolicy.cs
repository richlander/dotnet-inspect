namespace ILInspector.Metadata;

public enum JsonWireNamingPolicy
{
    None = 0,
    CamelCase,
    SnakeCaseLower,
    SnakeCaseUpper,
    KebabCaseLower,
    KebabCaseUpper,
    Unsupported,
}

/// <summary>
/// Metadata projection of
/// <c>System.Text.Json.Serialization.JsonSourceGenerationMode</c>.
/// </summary>
/// <remarks>
/// <see cref="Default"/> delegates a root to its context setting. The default
/// context setting supports both directions; <see cref="Serialization"/> does
/// not generate deserialize metadata. The combined value is valid because the
/// framework enum is flags-shaped.
/// </remarks>
public enum JsonSourceGenerationMode
{
    Default = 0,
    Metadata = 1,
    Serialization = 2,
    MetadataAndSerialization = Metadata | Serialization,
}
