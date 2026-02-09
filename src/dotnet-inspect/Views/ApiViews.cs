using System.Text.Json.Serialization;
using DotnetInspector.Metadata;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for single-type rendering. Pre-computes all display values from ApiType + options.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description), AutoFields = false)]
public class ApiTypeView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }

    // Backing data (all [MarkoutIgnore] for JSON/table serialization)
    [MarkoutIgnore] public string Kind { get; set; } = "";
    [MarkoutIgnore] public string? Modifiers { get; set; }
    [MarkoutIgnore] public string? BaseType { get; set; }
    [MarkoutIgnore] public string? TypeParametersInline { get; set; }
    [MarkoutIgnore] public string? Implements { get; set; }
    [MarkoutIgnore] public string? Assembly { get; set; }
    [MarkoutIgnore] public string? Package { get; set; }
    [MarkoutIgnore] public string? Version { get; set; }
    [MarkoutIgnore] public string? SamplesInfo { get; set; }

    /// <summary>
    /// Identity fields (rendered as key-value fields under the title).
    /// </summary>
    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Identity => GetIdentityFields();

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

    private List<MarkoutField> GetIdentityFields()
    {
        var fields = new List<MarkoutField>();
        fields.Add(new("Kind", Kind));
        if (!string.IsNullOrEmpty(Modifiers))
            fields.Add(new("Modifiers", Modifiers));
        if (!string.IsNullOrEmpty(BaseType))
            fields.Add(new("Base", BaseType));
        if (!string.IsNullOrEmpty(TypeParametersInline))
            fields.Add(new("Type Parameters", TypeParametersInline));
        if (!string.IsNullOrEmpty(Assembly))
            fields.Add(new("Assembly", Assembly));
        if (!string.IsNullOrEmpty(Package))
            fields.Add(new("Package", Package));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        if (!string.IsNullOrEmpty(SamplesInfo))
            fields.Add(new("Samples", SamplesInfo));
        return fields;
    }
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
[MarkoutSerializable(TitleProperty = nameof(Name), AutoFields = false)]
public class CliApiSurface
{
    private readonly ApiSurface _inner;

    public CliApiSurface(ApiSurface inner)
    {
        _inner = inner;
    }

    [MarkoutPropertyName("Name")]
    public string? Name => _inner.Name;

    /// <summary>
    /// Summary fields (rendered as key-value fields under the title).
    /// </summary>
    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Summary => GetSummaryFields();

    private List<MarkoutField> GetSummaryFields()
    {
        var fields = new List<MarkoutField>();
        fields.Add(new("Types", _inner.PublicTypeCount.ToString()));
        fields.Add(new("Methods", _inner.PublicMethodCount.ToString()));
        fields.Add(new("Properties", _inner.PublicPropertyCount.ToString()));
        if (_inner.Tfm != null)
            fields.Add(new("TFM", _inner.Tfm));
        if (!string.IsNullOrEmpty(_inner.RepositoryUrl))
            fields.Add(new("Repository", _inner.RepositoryUrl));
        return fields;
    }
}

/// <summary>
/// View model for type shape output (--shape).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(FullName))]
public class TypeShapeView
{
    [MarkoutIgnore]
    public string FullName { get; set; } = "";

    [MarkoutIgnore]
    public string Kind { get; set; } = "";

    [MarkoutIgnore]
    public string? Modifiers { get; set; }

    [MarkoutIgnore]
    public string? Assembly { get; set; }

    [MarkoutIgnore]
    public string? Package { get; set; }

    [MarkoutIgnore]
    public string? Version { get; set; }

    /// <summary>
    /// Identity fields (rendered as key-value fields under the title).
    /// </summary>
    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Identity => GetIdentityFields();

    [MarkoutIgnoreInTable]
    public List<TreeNode> Members { get; set; } = [];

    private List<MarkoutField> GetIdentityFields()
    {
        var fields = new List<MarkoutField>();
        fields.Add(new("Kind", Kind));
        if (!string.IsNullOrEmpty(Modifiers))
            fields.Add(new("Modifiers", Modifiers));
        if (!string.IsNullOrEmpty(Assembly))
            fields.Add(new("Assembly", Assembly));
        if (!string.IsNullOrEmpty(Package))
            fields.Add(new("Package", Package));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        return fields;
    }
}

[MarkoutContext(typeof(TypeShapeView))]
public partial class TypeViewContext : MarkoutSerializerContext
{
}
