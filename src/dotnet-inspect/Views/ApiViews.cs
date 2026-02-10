using System.Text.Json.Serialization;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for single-type rendering. Pre-computes all display values from ApiType + options.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public class ApiTypeView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    public string Kind { get; set; } = "";

    [MarkoutSkipNull]
    public string? Modifiers { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Base")]
    public string? BaseType { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Type Parameters")]
    public string? TypeParametersInline { get; set; }

    [MarkoutIgnore] public string? Implements { get; set; }

    [MarkoutSkipNull]
    public string? Assembly { get; set; }

    [MarkoutSkipNull]
    public string? Package { get; set; }

    [MarkoutSkipNull]
    public string? Version { get; set; }

    [MarkoutSkipNull]
    [MarkoutPropertyName("Samples")]
    public string? SamplesInfo { get; set; }

    /// <summary>
    /// Enum values without Description column (default).
    /// </summary>
    [MarkoutSection(Name = "Values", IgnoreProperty = nameof(EnumValueRow.Description))]
    [JsonIgnore]
    public List<EnumValueRow>? EnumValues { get; set; }

    /// <summary>
    /// Enum values with Description column (--docs).
    /// </summary>
    [MarkoutSection(Name = "Values")]
    [JsonIgnore]
    public List<EnumValueRow>? EnumValuesWithDocs { get; set; }

    /// <summary>
    /// Type parameters table (Normal+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Type Parameters")]
    [JsonIgnore]
    public List<TypeParameterRow>? TypeParameterRows { get; set; }

    /// <summary>
    /// Implemented interfaces (Detailed+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Interfaces")]
    [JsonIgnore]
    public List<InterfaceRow>? InterfaceRows { get; set; }

    /// <summary>
    /// Base class hierarchy (Detailed+ verbosity). Null → section skipped.
    /// </summary>
    [MarkoutSection(Name = "Baseclass")]
    [JsonIgnore]
    public List<BaseclassRow>? BaseclassRows { get; set; }

}

[MarkoutSerializable]
public class EnumValueRow
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
}

[MarkoutSerializable]
public class TypeParameterRow
{
    public string Parameter { get; set; } = "";
    public string Constraints { get; set; } = "";
}

[MarkoutSerializable]
public class InterfaceRow
{
    public string Interface { get; set; } = "";
}

[MarkoutSerializable]
public class BaseclassRow
{
    public string Type { get; set; } = "";
}

/// <summary>
/// Markout-aware wrapper for ApiSurface used in --all-types serialization path.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Name))]
public class CliApiSurface
{
    private readonly ApiSurface _inner;

    public CliApiSurface(ApiSurface inner)
    {
        _inner = inner;
    }

    [MarkoutIgnore]
    public string? Name => _inner.Name;

    public int Types => _inner.PublicTypeCount;
    public int Methods => _inner.PublicMethodCount;
    public int Properties => _inner.PublicPropertyCount;

    [MarkoutSkipNull]
    public string? Source => _inner.Source;

    [MarkoutSkipNull]
    public string? Version => _inner.Version;

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm => _inner.Tfm;
}

/// <summary>
/// View model for type shape output (--shape).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(FullName))]
public class TypeShapeView
{
    [MarkoutIgnore]
    public string FullName { get; set; } = "";

    public string Kind { get; set; } = "";

    [MarkoutSkipNull]
    public string? Modifiers { get; set; }

    [MarkoutSkipNull]
    public string? Assembly { get; set; }

    [MarkoutSkipNull]
    public string? Package { get; set; }

    [MarkoutSkipNull]
    public string? Version { get; set; }

    [MarkoutIgnoreInTable]
    public List<TreeNode> Members { get; set; } = [];
}

[MarkoutContext(typeof(TypeShapeView))]
public partial class TypeViewContext : MarkoutSerializerContext
{
}
